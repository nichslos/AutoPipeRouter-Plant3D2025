using SPTC.SupportDrawings;
var box = new BoundingBox3(10,20,30,110,220,330);
Assert(box.SizeX == 100 && box.SizeY == 200 && box.SizeZ == 300, "model spans");
Assert(box.MidX == 60 && box.MidY == 120 && box.MidZ == 180, "model midpoint");
Assert(box.Include(new(-10,-20,0,10,20,30)).MinX == -10, "union bounds");
foreach (var view in new[] { "TOP", "FRONT", "SIDE", "ISO" })
    Assert(SupportSheetMath.ViewHeight(box,view,180,120)>0, view+" fit");
Assert(SupportSheetMath.ViewHeight(box,"FRONT",180,120)>=375, "front vertical extent");
Assert(SupportEntityClassifier.Classify("AutoCAD.Database.Entity", "AcPpDb3dStructureMember", "", "0", "") == "PLANT_STRUCTURE_MEMBER_CANDIDATE", "native structure class");
Assert(SupportEntityClassifier.Classify("AutoCAD.Database.Entity", "AcPpDb3dPipeSupport", "", "0", "") == "PLANT_SUPPORT_CANDIDATE", "native support class");
Assert(SupportEntityClassifier.Classify("Autodesk.AutoCAD.DatabaseServices.BlockReference", "AcDbBlockReference", "INSERT", "PIPE_SUPPORTS", "PS-101") == "SUPPORT_NAMING_CANDIDATE", "named support block");
Assert(SupportEntityClassifier.Classify("Autodesk.AutoCAD.DatabaseServices.BlockReference", "AcDbBlockReference", "INSERT", "0", "C-1") == "GENERIC_BLOCK", "non-support block");
Assert(SupportEntityClassifier.Classify("Autodesk.AutoCAD.DatabaseServices.Line", "AcDbLine", "LINE", "0", "") == "UNCLASSIFIED", "non-support line");
var lines3D = new[]
{
    new SketchSegment3(new(10,20,30), new(110,20,30)),
    new SketchSegment3(new(110,20,30), new(110,220,330))
};
BoundingBox3 geometryBox = Support2DMath.FromSegments(lines3D);
Assert(geometryBox == box, "edge envelope matches expected coordinates");
double projectionGap = Support2DMath.Gap(box);
Assert(projectionGap > 0, "positive projection gap");
SketchPoint2 pointTop = Support2DMath.Project(new(110,220,330),box,OrthoPlane.Top,projectionGap);
SketchPoint2 pointFront = Support2DMath.Project(new(110,220,330),box,OrthoPlane.Front,projectionGap);
SketchPoint2 pointSide = Support2DMath.Project(new(110,220,330),box,OrthoPlane.Side,projectionGap);
Assert(pointTop.X == projectionGap + 100, "top projection x axis");
Assert(pointTop.Y == 2*projectionGap + 300 + 200, "top projection y axis");
Assert(pointFront.X == projectionGap + 100 && pointFront.Y == projectionGap + 300, "front projection");
Assert(pointSide.X == 2*projectionGap + 100 + 200 && pointSide.Y == projectionGap + 300, "side projection");
SketchPoint2 collapsedA = Support2DMath.Project(new(10,20,30),box,OrthoPlane.Top,projectionGap);
SketchPoint2 collapsedB = Support2DMath.Project(new(10,20,40),box,OrthoPlane.Top,projectionGap);
Assert(Support2DMath.DistanceSquared(collapsedA,collapsedB) == 0, "top view Z line collapses correctly");
bool emptyRejected = false;
try { Support2DMath.FromSegments(Array.Empty<SketchSegment3>()); }
catch (ArgumentException) { emptyRejected = true; }
Assert(emptyRejected, "missing geometry rejected");
var nativePlan = NativeOrthoLayout.Build(box, 1.0);
Assert(nativePlan.Views.Count == 3, "native Ortho plan has TOP FRONT SIDE");
Assert(nativePlan.PaddedCube.MinX < box.MinX && nativePlan.PaddedCube.MaxZ > box.MaxZ, "native OrthoCube padding");
Assert(nativePlan.PaddingX == 10 && nativePlan.PaddingY == 20 && nativePlan.PaddingZ == 30, "10 percent per axis");
Assert(nativePlan.PaddedCube.SizeX == 120 && nativePlan.PaddedCube.SizeY == 240 &&
    nativePlan.PaddedCube.SizeZ == 360, "axis-specific OrthoCube sizes");
var thinSupport = new BoundingBox3(-1023.392,-2784.801,0,201,-2709.801,1149.392);
var tightPlan = NativeOrthoLayout.Build(thinSupport, 1.0);
Assert(Math.Abs(tightPlan.PaddingY - 10) < 1e-9, "thin Y dimension uses minimum 10mm padding");
Assert(Math.Abs(tightPlan.PaddedCube.SizeY - 95) < 1e-9, "thin Y OrthoCube reduced to 95mm");
Assert(tightPlan.Views[0].ScaleDenominator == 10, "TOP scale for steel support");
Assert(tightPlan.Views[1].ScaleDenominator == 20 && tightPlan.Views[2].ScaleDenominator == 20, "FRONT and SIDE scales");
Assert(nativePlan.Views[0].Name == "TOP" && nativePlan.Views[1].Name == "FRONT" && nativePlan.Views[2].Name == "SIDE", "view ordering");
foreach (var view in nativePlan.Views)
{
    Assert(view.CandidateScale > 0 && view.CandidateScale <= 1, "valid paper scale " + view.Name);
    Assert(view.PaperCenterX - view.PaperWidth / 2 > 0 && view.PaperCenterX + view.PaperWidth / 2 < 420, "within A3 sheet " + view.Name);
    Assert(view.PaperCenterY - view.PaperHeight / 2 > 40 && view.PaperCenterY + view.PaperHeight / 2 < 297, "above title block " + view.Name);
}
bool invalidScaleRejected = false;
try { NativeOrthoLayout.Build(box, 0); } catch (ArgumentOutOfRangeException) { invalidScaleRejected = true; }
Assert(invalidScaleRejected, "unrecognized source units rejected");
string planA = NativeOrthoLayout.SuggestedPlanFileName(
    @"C:\\Projects\\Line_A\\Plant Models\\Support-A.dwg", new[] { "1A" }, "REV13");
string planB = NativeOrthoLayout.SuggestedPlanFileName(
    @"D:\\OtherProject\\Plant Models\\Unit-B.dwg", new[] { "2B" }, "REV13");
string planC = NativeOrthoLayout.SuggestedPlanFileName(
    @"C:\\Projects\\Line_A\\Plant Models\\Support-A.dwg", new[] { "3C" }, "REV13");
Assert(planA.StartsWith("SPTC_Support-A_1A_", StringComparison.Ordinal), "source drawing determines report name");
Assert(planA != planB && planA != planC && planB != planC, "model and support handle change report name");
Assert(!planA.Contains('\\') && !planA.Contains(' '), "proposed filename excludes path and spaces");
Assert(planA.EndsWith("_REV13.json", StringComparison.Ordinal), "versioned report name");
string nativeA = NativeOrthoRegistrationNames.NewOrthoDrawingFileName(
    "/projects/one/Plant Models/Support 01.dwg", new[] {"A1","A2"}, "20261106_090000_cafe");
string nativeB = NativeOrthoRegistrationNames.NewOrthoDrawingFileName(
    "/projects/two/Plant Models/Support 02.dwg", new[] {"B1","B2"}, "20261106_090001_beef");
Assert(nativeA.StartsWith("SPTC_Support_01_A1_ORTHO_R14_", StringComparison.Ordinal), "native Ortho new DWG depends on active model");
Assert(nativeA != nativeB, "native Ortho naming follows model and selection");
Assert(nativeA.EndsWith(".dwg", StringComparison.Ordinal), "native Ortho test output extension");
Assert(NativeOrthoRegistrationNames.IsInsideProjectDirectory("/projects/sample/Orthos/new.dwg", "/projects/sample"), "test Ortho destination inside project");
Assert(!NativeOrthoRegistrationNames.IsInsideProjectDirectory("/projects/sample2/Orthos/new.dwg", "/projects/sample"), "other project destination forbidden");
Assert(!NativeOrthoRegistrationNames.IsInsideProjectDirectory("/projects/sample/../outside.dwg", "/projects/sample"), "parent traversal forbidden");
Assert(NativeOrthoRegistrationNames.IsInsideProjectDirectory(
    "/plant-project/Orthos/DWGs/Wizard_Seed.dwg", "/plant-project/Orthos"),
    "REV16 native seed in Orthos/DWGs subfolder accepted");
Assert(!NativeOrthoRegistrationNames.IsInsideProjectDirectory(
    "/other-project/Orthos/DWGs/Wizard_Seed.dwg", "/plant-project/Orthos"),
    "REV16 seed outside native Ortho project folder rejected");
Assert(!NativeOrthoRegistrationNames.IsInsideProjectDirectory(
    "/plant-project/Orthos/../Plant Models/Wrong.dwg", "/plant-project/Orthos"),
    "REV16 model DWG outside native Ortho folder rejected");
var topA1 = NativeOrthoTopViewPlan.Build(tightPlan, 841, 594);
var topA3 = NativeOrthoTopViewPlan.Build(tightPlan, 420, 297);
Assert(topA1.PaperWidthMm == 841 && topA1.PaperHeightMm == 594,
    "REV17 uses actual A1 Ortho paper size rather than fixed A3");
Assert(topA1.PaperCenterX == 841 * 0.32 && topA1.PaperCenterY == 594 * 0.62,
    "REV17 calculates paper placement from target sheet");
Assert(topA1.CandidateScaleDenominator <= topA3.CandidateScaleDenominator,
    "Larger Ortho paper must not require a smaller TOP view");
Assert(topA1.Status.StartsWith("PROPOSED_ONLY:", StringComparison.Ordinal),
    "Never imply unverified native Ortho placement is automatic");
Assert(topA1.ProjectedModelWidthMm > 0 && topA1.ProjectedModelHeightMm > 0,
    "Selected support model spans must be nonzero");
var rev20Sample=NativeOrthoLayout.Build(new BoundingBox3(0,0,0,300,75,220),1.0);
var threeA1=NativeOrthoThreeViewPlan.Build(rev20Sample,594,841);
Assert(threeA1.Views.Count==3 && threeA1.Views[0].Name=="TOP" &&
    threeA1.Views[1].Name=="FRONT" && threeA1.Views[2].Name=="SIDE",
    "REV20 TOP FRONT SIDE order");
Assert(threeA1.Denominator==2,"REV20 calibrated A1 common scale 1:2");
Assert(threeA1.Views[0].Dz==1 && threeA1.Views[0].Uy==1,
    "REV20 TOP orientation");
Assert(threeA1.Views[1].Dy==-1 && threeA1.Views[1].Uz==1,
    "REV20 FRONT orientation");
Assert(threeA1.Views[2].Dx==1 && threeA1.Views[2].Uz==1,
    "REV20 SIDE orientation");
Assert(threeA1.Views.All(v=>v.Y>=841*.12 && v.X+v.W<=594*.78),
    "REV20 proposed A1 title-block keepout");
for(int i=0;i<threeA1.Views.Count;i++)
    for(int j=i+1;j<threeA1.Views.Count;j++)
    {
        var a=threeA1.Views[i];var b=threeA1.Views[j];
        Assert(!(a.X<b.X+b.W && b.X<a.X+a.W && a.Y<b.Y+b.H && b.Y<a.Y+a.H),
            "REV20 no proposed tile collisions");
    }
var threeA3=NativeOrthoThreeViewPlan.Build(rev20Sample,420,297);
Assert(threeA3.Denominator>=threeA1.Denominator,"REV20 A3 needs equal or smaller drawing scale");
Assert(threeA1.Status.StartsWith("PROPOSED_ONLY:"),
    "REV20 native placement not falsely claimed");
var clickPlan=NativeOrthoOneClickPlan.Build(threeA1,594,841,
    threeA1.Views[0].CenterX,threeA1.Views[0].CenterY);
Assert(clickPlan.Count==3 &&
    clickPlan[0].Name=="TOP"&&clickPlan[1].Name=="FRONT"&&
    clickPlan[2].Name=="SIDE","REV21 one click computes three view centers");
Assert(Math.Abs(clickPlan[0].CenterX-threeA1.Views[0].CenterX)<1e-8 &&
    Math.Abs(clickPlan[0].CenterY-threeA1.Views[0].CenterY)<1e-8,
    "REV21 click anchors TOP exactly");
Assert(Math.Abs(clickPlan[1].CenterY-threeA1.Views[1].CenterY)<1e-8 &&
    Math.Abs(clickPlan[2].CenterX-threeA1.Views[2].CenterX)<1e-8,
    "REV21 FRONT and SIDE derive their centers automatically");
var clickShift=NativeOrthoOneClickPlan.Build(threeA1,594,841,
    threeA1.Views[0].CenterX+5,threeA1.Views[0].CenterY+5);
Assert(clickShift.Zip(clickPlan).All(v=>
    Math.Abs(v.First.CenterX-v.Second.CenterX-5)<1e-8 &&
    Math.Abs(v.First.CenterY-v.Second.CenterY-5)<1e-8),
    "REV21 entire group tracks just one anchor");
bool outOfBoundsRejected=false;
try{NativeOrthoOneClickPlan.Build(threeA1,594,841,500,800);}
catch(InvalidOperationException){outOfBoundsRejected=true;}
Assert(outOfBoundsRejected,"REV21 rejects off-sheet or title-block anchor");
// REV21.1 regression: real runtime failed after TOP click (115,370)
 // because its FRONT center fell into the reserved bottom area.
var adjustedOneClick=NativeOrthoOneClickPlan.BuildAdjusted(
    threeA1,594,841,115,370);
Assert(adjustedOneClick.WasAdjusted &&
    adjustedOneClick.RequestedTopX==115 &&
    adjustedOneClick.RequestedTopY==370,
    "REV21.1 single clicked point preserved in audit record");
Assert(adjustedOneClick.EffectiveTopY>370 &&
    adjustedOneClick.EffectiveTopY>=adjustedOneClick.MinimumTopY &&
    adjustedOneClick.EffectiveTopY<=adjustedOneClick.MaximumTopY,
    "REV21.1 off-limits FRONT moves entire group up to nearest safe anchor");
Assert(Math.Abs(adjustedOneClick.EffectiveTopX-115)<1e-8,
    "REV21.1 X stays fixed when only Y correction is needed");
Assert(adjustedOneClick.Views.All(v=>
    v.CenterX-v.PaperFrameWidth/2>=-1e-7 &&
    v.CenterX+v.PaperFrameWidth/2<=594*.78+1e-7 &&
    v.CenterY-v.PaperFrameHeight/2>=841*.12-1e-7 &&
    v.CenterY+v.PaperFrameHeight/2<=841*.94+1e-7),
    "REV21.1 translated TOP FRONT SIDE all fit the conservative A1 sheet");
Assert(Math.Abs(adjustedOneClick.Views[0].CenterY-
    adjustedOneClick.EffectiveTopY)<1e-8,
    "REV21.1 effective TOP center agrees with derived group position");
var noShift=NativeOrthoOneClickPlan.BuildAdjusted(threeA1,594,841,
    threeA1.Views[0].CenterX,threeA1.Views[0].CenterY);
Assert(!noShift.WasAdjusted &&
    Math.Abs(noShift.EffectiveTopY-threeA1.Views[0].CenterY)<1e-8,
    "REV21.1 already-valid anchor must never be repositioned");
var cornerAdjusted=NativeOrthoOneClickPlan.BuildAdjusted(threeA1,594,841,5,5);
Assert(cornerAdjusted.WasAdjusted &&
    cornerAdjusted.EffectiveTopX>5 && cornerAdjusted.EffectiveTopY>5,
    "REV21.1 extreme inside-sheet click shifts group in both dimensions");
bool outsidePaperRejected=false;
try{NativeOrthoOneClickPlan.BuildAdjusted(threeA1,594,841,-1,370);}
catch(ArgumentOutOfRangeException){outsidePaperRejected=true;}
Assert(outsidePaperRejected,"REV21.1 does not allow a click outside the physical sheet");
var tenSource = NativeOrthoLayout.Build(
    new BoundingBox3(0,0,0,362.68,75,272.679),1.0);
var ten=NativeOrthoTenViewPlan.Build(tenSource,594,841);
string[] tenExpected={"TOP","BOTTOM","LEFT","RIGHT","FRONT","BACK","NW","NE","SW","SE"};
Assert(ten.Views.Count==10&&ten.Views.Select(v=>v.Name).SequenceEqual(tenExpected),
    "REV22 exactly ten native view directions in requested sequence");
Assert(ten.Views.Select(v=>v.EnumName).Distinct().Count()==10,
    "REV22 all native view enums unique");
Assert(ten.Denominator>=2&&ten.Views.All(v=>v.ModelWmm>0&&v.ModelHmm>0),
    "REV22 native projected support dimensions and common scale");
foreach(var view in ten.Views)
{
    double directionLength=Math.Sqrt(view.Dx*view.Dx+view.Dy*view.Dy+view.Dz*view.Dz);
    double upLength=Math.Sqrt(view.Ux*view.Ux+view.Uy*view.Uy+view.Uz*view.Uz);
    double dot=view.Dx*view.Ux+view.Dy*view.Uy+view.Dz*view.Uz;
    Assert(Math.Abs(dot)<1e-7&&Math.Abs(directionLength-1)<1e-7&&
        Math.Abs(upLength-1)<1e-7,
        "REV22 unit-orthogonal view direction and up vector "+view.Name);
}
Assert(ten.Views[6].Dx<0&&ten.Views[6].Dy>0&&ten.Views[6].Dz>0&&
    ten.Views[7].Dx>0&&ten.Views[7].Dy>0&&ten.Views[7].Dz>0&&
    ten.Views[8].Dx<0&&ten.Views[8].Dy<0&&ten.Views[8].Dz>0&&
    ten.Views[9].Dx>0&&ten.Views[9].Dy<0&&ten.Views[9].Dz>0,
    "REV22 four isometric camera quadrants");
var tenGroup=NativeOrthoTenViewPlan.BuildAdjusted(ten,594,841,
    ten.Views[0].CenterX,ten.Views[0].CenterY);
Assert(tenGroup.Views.Count==10&&!tenGroup.WasAdjusted,
    "REV22 ten-view group fits at its default A1 center");
for(int i=0;i<tenGroup.Views.Count;i++)
{
    var a=tenGroup.Views[i];
    Assert(a.CenterX-a.PaperFrameWidth/2>=-1e-7&&
        a.CenterX+a.PaperFrameWidth/2<=594*.78+1e-7&&
        a.CenterY-a.PaperFrameHeight/2>=841*.12-1e-7&&
        a.CenterY+a.PaperFrameHeight/2<=841*.94+1e-7,
        "REV22 actual view envelope fits A1 sheet "+a.Name);
    for(int j=i+1;j<tenGroup.Views.Count;j++)
    {
        var b=tenGroup.Views[j];
        Assert(Math.Abs(a.CenterX-b.CenterX)>=(a.PaperFrameWidth+b.PaperFrameWidth)/2||
            Math.Abs(a.CenterY-b.CenterY)>=(a.PaperFrameHeight+b.PaperFrameHeight)/2,
            "REV22 ten viewport rectangles cannot overlap");
    }
}
var tenClick=NativeOrthoTenViewPlan.BuildAdjusted(ten,594,841,115,370);
Assert(tenClick.Views.Count==10&&tenClick.WasAdjusted&&
    tenClick.EffectiveTopY>370&&tenClick.RequestedTopY==370,
    "REV22 shift full ten-view group when initial TOP click is too low");
bool tenRejected=false;
try{NativeOrthoTenViewPlan.BuildAdjusted(ten,594,841,-1,370);}
catch(ArgumentOutOfRangeException){tenRejected=true;}
Assert(tenRejected,"REV22 reject click outside actual sheet");
var tenA3=NativeOrthoTenViewPlan.Build(tenSource,420,297);
Assert(tenA3.Denominator>=ten.Denominator,
    "REV22 smaller A3 paper must not have a larger view scale");
Assert(ten.Status.StartsWith("PROPOSED_ONLY:"),
    "REV22 private API remains unverified until native runtime test");
// REV22.1: use canonical native type strings for only four isometric views,
// not the custom NWIsometricView etc strings that may be treated as Current View.
foreach(string iso in new[]{"NW","NE","SW","SE"})
{
    string canonical=NativeOrthoIsoViewType.Resolve(iso,iso+"IsometricView");
    Assert(canonical==iso+"Isometric","REV22.1 native iso identifier "+iso);
    var direction=ten.Views.Single(v=>v.Name==iso);
    Assert(NativeOrthoIsoViewType.IsIsometric(iso) &&
        NativeOrthoIsoViewType.HasExpectedQuadrant(iso,direction.Dx,direction.Dy,direction.Dz),
        "REV22.1 preset quadrant "+iso);
    Assert(NativeOrthoIsoViewType.MatchesCamera(
        iso,direction.Dx*4,direction.Dy*4,direction.Dz*4,
        direction.Dx,direction.Dy,direction.Dz),
        "REV22.1 normalized iso camera matches native WCS direction");
    Assert(!NativeOrthoIsoViewType.MatchesCamera(
        iso,0,0,1,direction.Dx,direction.Dy,direction.Dz),
        "REV22.1 Current View TOP fallback rejected for "+iso);
    Assert(!NativeOrthoIsoViewType.MatchesCamera(
        iso,ten.Views.Single(v=>v.Name=="TOP").Dx,
        ten.Views.Single(v=>v.Name=="TOP").Dy,
        ten.Views.Single(v=>v.Name=="TOP").Dz,
        direction.Dx,direction.Dy,direction.Dz),
        "REV22.1 default TOP camera cannot masquerade as iso "+iso);
}
foreach(string standard in new[]{"TOP","BOTTOM","LEFT","RIGHT","FRONT","BACK"})
{
    var v=ten.Views.Single(v=>v.Name==standard);
    Assert(NativeOrthoIsoViewType.Resolve(standard,v.TypeName)==v.TypeName,
        "REV22.1 standard native string unchanged "+standard);
    Assert(!NativeOrthoIsoViewType.IsIsometric(standard),
        "REV22.1 standard view remains nonisometric "+standard);
}
// REV22.2: Plant 3D 2025 PnP3dOrthoDrawingsUI.dll IL inspection
// proved UIUtils::.cctor defines s_NWView="NW" etc; the Edit View
// GetViewTypeFromName() parser does NOT identify "NWIsometric".
foreach(string iso in new[]{"NW","NE","SW","SE"})
{
    string shortName=NativeOrthoEditorPreset.Resolve(iso,iso+"Isometric");
    Assert(shortName==iso&&NativeOrthoEditorPreset.IsExactIsoPreset(iso,shortName),
        "REV22.2 exact native Edit View preset string "+iso);
    Assert(NativeOrthoEditorPreset.IsUnclassifiedCurrentView(
        iso,iso+"Isometric"),
        "REV22.2 deprecated long name must not be accepted as Edit View preset");
    Assert(NativeOrthoEditorPreset.IsUnclassifiedCurrentView(
        iso,iso+"IsometricView"),
        "REV22.2 original custom isometric name must not be accepted");
}
foreach(string axial in new[]{"TOP","BOTTOM","LEFT","RIGHT","FRONT","BACK"})
{
    var view=ten.Views.Single(v=>v.Name==axial);
    Assert(NativeOrthoEditorPreset.Resolve(axial,view.TypeName)==view.TypeName,
        "REV22.2 axial view name preserved exactly "+axial);
}
// REV22.3: replay the verified 2026-11-09 runtime discrepancy:
// planned TOP 76.163x19.950 mm versus native 362.68x95 mm.
// Verify that actual post-generation geometry is authoritative, and the
// failure guard stops generation rather than trusting preflight dimensions.
var paperExpected=new NativeOrthoViewportPaperGuard.Frame(
    "TOP",50,682.256,76.163,19.950,.2,99.75);
var paperActual=new NativeOrthoViewportPaperGuard.Frame(
    "TOP",50,682.256,362.68,95,.2,99.75);
Assert(NativeOrthoViewportPaperGuard.Validate(
    paperExpected,paperActual,594,841,
    Array.Empty<NativeOrthoViewportPaperGuard.Frame>())=="NATIVE_FRAME_SIZE_CHANGED",
    "REV22.3 reject actual native oversized paper viewport from REV22.2 log");
Assert(NativeOrthoViewportPaperGuard.Validate(
    paperExpected,paperExpected,594,841,
    Array.Empty<NativeOrthoViewportPaperGuard.Frame>())=="PASS",
    "REV22.3 accept correctly generated viewport after scale correction");
var conflicting=new NativeOrthoViewportPaperGuard.Frame(
    "BOTTOM",50,682.256,76.163,19.95,.2,99.75);
Assert(NativeOrthoViewportPaperGuard.Validate(
    paperExpected,paperExpected,594,841,new[]{conflicting})==
    "NATIVE_VIEWPORT_OVERLAPS_BOTTOM",
    "REV22.3 reject post-generation viewport overlap");
var moved=new NativeOrthoViewportPaperGuard.Frame(
    "TOP",62,682.256,76.163,19.95,.2,99.75);
Assert(NativeOrthoViewportPaperGuard.Validate(
    paperExpected,moved,594,841,
    Array.Empty<NativeOrthoViewportPaperGuard.Frame>())=="NATIVE_CENTER_CHANGED",
    "REV22.3 reject unexpected native view translation");
var wrongScale=new NativeOrthoViewportPaperGuard.Frame(
    "TOP",50,682.256,76.163,19.95,1,99.75);
Assert(NativeOrthoViewportPaperGuard.Validate(
    paperExpected,wrongScale,594,841,
    Array.Empty<NativeOrthoViewportPaperGuard.Frame>())=="NATIVE_VIEWPORT_SCALE_CHANGED",
    "REV22.3 reject native viewport scale change");
// REV22.5 replay of the actual REV22.4 Plant 3D runtime audit:
// all four saved view types and WCS camera quadrant checks passed, but
// viewport TwistAngle was 180 degrees in every isometric viewport.
foreach(string iso in new[]{"NW","NE","SW","SE"})
{
    var v=ten.Views.Single(x=>x.Name==iso);
    Assert(NativeOrthoIsoTwistGuard.IsProjectedWorldUp(
        v.Dx,v.Dy,v.Dz,v.Ux,v.Uy,v.Uz),
        "REV22.5 "+iso+" predefined camera up vector projects WCS +Z");
    Assert(NativeOrthoIsoTwistGuard.IsHalfTurn(Math.PI),
        "REV22.5 original 180-degree twist is a half-turn");
    Assert(!NativeOrthoIsoTwistGuard.IsZeroTwist(Math.PI),
        "REV22.5 original 180-degree twist cannot masquerade as zero");
    Assert(NativeOrthoIsoTwistGuard.IsZeroTwist(0),
        "REV22.5 "+iso+" normalize paper twist to zero");
}
Assert(!NativeOrthoIsoTwistGuard.IsProjectedWorldUp(0,0,1,0,1,0),
    "REV22.5 do not assume world-up projection for TOP camera");
Assert(!NativeOrthoIsoTwistGuard.IsZeroTwist(double.NaN),
    "REV22.5 reject nonfinite twist");
// REV22.6: camera quadrant alone is insufficient. Compare normalized native
// reference camera AND twist, where 360 degrees is equivalent to zero.
var refNW=new NativeOrthoIsoReferenceMath.Pose(
    "NW","A1",-1,1,1,0,.2);
var generatedNW=new NativeOrthoIsoReferenceMath.Pose(
    "NW","B9A",-157.43164,157.43164,157.43164,2*Math.PI,.2);
var same=NativeOrthoIsoReferenceMath.Compare(refNW,generatedNW);
Assert(same.OrientationMatches&&same.CameraMatches&&same.RollMatches&&same.ScaleMatches,
    "REV22.6 normalized camera plus modulo-360 twist matches reference");
var upsideDown=new NativeOrthoIsoReferenceMath.Pose(
    "NW","B9A",-1,1,1,Math.PI,.2);
var wrongRoll=NativeOrthoIsoReferenceMath.Compare(refNW,upsideDown);
Assert(wrongRoll.CameraMatches&&!wrongRoll.RollMatches&&!wrongRoll.OrientationMatches,
    "REV22.6 reject an upside-down viewport despite same camera direction");
var differentQuadrant=new NativeOrthoIsoReferenceMath.Pose(
    "NW","B9A",1,1,1,0,.2);
Assert(!NativeOrthoIsoReferenceMath.Compare(refNW,differentQuadrant).CameraMatches,
    "REV22.6 detect native direction mismatch");
var differentScale=new NativeOrthoIsoReferenceMath.Pose(
    "NW","B9A",-1,1,1,0,.1);
var scaleInfo=NativeOrthoIsoReferenceMath.Compare(refNW,differentScale);
Assert(scaleInfo.OrientationMatches&&!scaleInfo.ScaleMatches,
    "REV22.6 scale difference is informational, not an orientation failure");
bool invalidRefRejected=false;
try
{
    NativeOrthoIsoReferenceMath.Compare(refNW,
        new NativeOrthoIsoReferenceMath.Pose("NW","B9A",0,0,0,0,.2));
}
catch(ArgumentException){invalidRefRejected=true;}
Assert(invalidRefRejected,"REV22.6 reject invalid reference direction");
// REV22.7 live evidence: REV22.5 initially used zero TwistAngle but
// retained center from native 180deg viewport. Later native Update View
// reversed center coordinates; that shifted the model back into the frame.
// Test both center coordinates rotate together, not only the paper twist.
var knownCenters=new[]{
    ("NW",347.236462,-17.816316),
    ("NE",-129.819230,107.709582),
    ("SW",129.819230,-293.244548),
    ("SE",-347.236462,-167.718651)
};
foreach(var t in knownCenters)
{
    var before=new NativeOrthoIsoFramingGuard.ViewState(t.Item2,t.Item3,Math.PI);
    var after=NativeOrthoIsoFramingGuard.NormalizeHalfTurn(before);
    Assert(after.AppliedHalfTurn&&after.CenterX==-t.Item2&&
        after.CenterY==-t.Item3&&after.TwistRadians==0,
        "REV22.7 compensate native 180-to-0 twist for "+t.Item1);
    Assert(NativeOrthoIsoFramingGuard.Matches(after,
        new NativeOrthoIsoFramingGuard.ViewState(-t.Item2,-t.Item3,0)),
        "REV22.7 native update center matches compensated center "+t.Item1);
    Assert(!NativeOrthoIsoFramingGuard.Matches(after,before),
        "REV22.7 old unadjusted center no longer passes "+t.Item1);
}
var zeroState=new NativeOrthoIsoFramingGuard.ViewState(35,-70,2*Math.PI);
var zeroResult=NativeOrthoIsoFramingGuard.NormalizeHalfTurn(zeroState);
Assert(!zeroResult.AppliedHalfTurn&&zeroResult.CenterX==35&&zeroResult.CenterY==-70,
    "REV22.7 do not double-flip already normalized viewport");
bool arbitraryTwistRejected=false;
try{ NativeOrthoIsoFramingGuard.NormalizeHalfTurn(
    new NativeOrthoIsoFramingGuard.ViewState(10,20,Math.PI/3)); }
catch(InvalidOperationException){ arbitraryTwistRejected=true; }
Assert(arbitraryTwistRejected,"REV22.7 reject unsupported arbitrary paper twists");
// REV22.8: six standard presets were previously saved as TopView,
// BottomView, etc. without checking Plant 3D Edit View's actual parser.
string[] axialNames={"TOP","BOTTOM","LEFT","RIGHT","FRONT","BACK"};
for(int i=0;i<axialNames.Length;i++)
{
    string name=axialNames[i];
    Assert(NativeOrthoAxialEditorPreset.IsAxial(name),
        "REV22.8 "+name+" classified axial");
    var candidateLabels=NativeOrthoAxialEditorPreset.Candidates(name,name+"View");
    Assert(candidateLabels.Count>1&&
        !candidateLabels.Contains("Current",StringComparer.OrdinalIgnoreCase),
        "REV22.8 "+name+" never picks Current View");
    string first=candidateLabels[0];
    string resolved=NativeOrthoAxialEditorPreset.Resolve(
        name,name+"View",i+1,c=>c==first?i+1:0);
    Assert(resolved==first,"REV22.8 "+name+" selects actual recognized short native label");
    string oldLabel=name+"View";
    string fallback=NativeOrthoAxialEditorPreset.Resolve(
        name,oldLabel,i+1,c=>c==oldLabel?i+1:0);
    Assert(fallback==oldLabel,"REV22.8 honors parser-supported original type");
    bool rejected=false;
    try{NativeOrthoAxialEditorPreset.Resolve(name,oldLabel,i+1,c=>0);}
    catch(InvalidOperationException){rejected=true;}
    Assert(rejected,"REV22.8 "+name+" rejects unknown-to-native Edit View names");
}
Assert(!NativeOrthoAxialEditorPreset.IsAxial("NW"),
    "REV22.8 leave four ISO view identifiers unaffected");
// REV22.10: generated orthographic views appeared half-turned compared
// with the SAME views after native Edit View/Update View. Test that the
// paper-screen rotation and ViewCenter are transformed together.
foreach(string axial in new[]{"TOP","BOTTOM","LEFT","RIGHT","FRONT","BACK"})
{
    var native180=new NativeOrthoIsoFramingGuard.ViewState(347.236,-17.816,Math.PI);
    var fixToZero=NativeOrthoAxialHalfTurnGuard.Flip(native180);
    Assert(fixToZero.AppliedHalfTurn&&fixToZero.CenterX==-347.236&&
        fixToZero.CenterY==17.816&&
        NativeOrthoIsoTwistGuard.IsZeroTwist(fixToZero.TwistRadians),
        "REV22.10 "+axial+" 180-to-0 turn with ViewCenter compensation");
    Assert(NativeOrthoAxialHalfTurnGuard.Matches(fixToZero,
        new NativeOrthoIsoFramingGuard.ViewState(-347.236,17.816,0)),
        "REV22.10 "+axial+" 180-to-0 saved readback");
    var nativeZero=new NativeOrthoIsoFramingGuard.ViewState(100,-250,2*Math.PI);
    var fixTo180=NativeOrthoAxialHalfTurnGuard.Flip(nativeZero);
    Assert(fixTo180.AppliedHalfTurn&&fixTo180.CenterX==-100&&
        fixTo180.CenterY==250&&
        NativeOrthoIsoTwistGuard.IsHalfTurn(fixTo180.TwistRadians),
        "REV22.10 "+axial+" 0-to-180 turn with ViewCenter compensation");
    Assert(NativeOrthoAxialHalfTurnGuard.Matches(fixTo180,
        new NativeOrthoIsoFramingGuard.ViewState(-100,250,Math.PI)),
        "REV22.10 "+axial+" 0-to-180 saved readback");
}
bool nonHalfAngleRefused=false;
try
{
    NativeOrthoAxialHalfTurnGuard.Flip(
        new NativeOrthoIsoFramingGuard.ViewState(10,20,Math.PI/2));
}
catch(InvalidOperationException){nonHalfAngleRefused=true;}
Assert(nonHalfAngleRefused,
    "REV22.10 reject unsupported 90-degree native view twist");
bool nonfiniteAxialRefused=false;
try
{
    NativeOrthoAxialHalfTurnGuard.Flip(
        new NativeOrthoIsoFramingGuard.ViewState(double.NaN,20,0));
}
catch(ArgumentException){nonfiniteAxialRefused=true;}
Assert(nonfiniteAxialRefused,"REV22.10 reject invalid axial viewport center");
// REV22.11: long pipe runs never expand the support-focused XY cube.
var supportBox=new BoundingBox3(0,0,0,250,150,2000);
var longRoute=new BoundingBox3(-100000,25,2075,100000,100,2180);
var focus=SupportFocusedCubePlanner.Build(
    supportBox,new[]{longRoute},1,100,500);
Assert(focus.PipesAccepted==1&&focus.PipesRejected==0,
    "REV22.11 accepts horizontal long pipe over support");
Assert(focus.FocusBounds.MinX==-100&&focus.FocusBounds.MaxX==350&&
    focus.FocusBounds.MinY==-100&&focus.FocusBounds.MaxY==250,
    "REV22.11 excludes far pipe endpoints from XY footprint");
Assert(focus.FocusBounds.MinZ==0&&focus.FocusBounds.MaxZ==2180,
    "REV22.11 includes local pipe outside diameter and support elevation");
Assert(focus.FocusBounds.SizeX<1000&&
    focus.FocusBounds.SizeX<longRoute.SizeX/100,
    "REV22.11 no enlarged pipe route OrthoCube");
var padded=NativeOrthoLayout.Build(focus.FocusBounds,1);
Assert(padded.PaddedCube.SizeX<1000&&padded.PaddedCube.MaxZ>2180,
    "REV22.11 native layout receives compact padded cube");
var farRoute=new BoundingBox3(-100000,3000,2075,100000,3100,2180);
var farVertical=new BoundingBox3(-100000,25,5000,100000,100,6000);
var unrelated=SupportFocusedCubePlanner.Build(
    supportBox,new[]{farRoute,farVertical},1,100,500);
Assert(unrelated.PipesAccepted==0&&unrelated.PipesRejected==2&&
    unrelated.FocusBounds.MinZ==supportBox.MinZ&&
    unrelated.FocusBounds.MaxZ==supportBox.MaxZ,
    "REV22.11 exclude XY-disjoint and far-above-support pipe bounds");
var extremelyTallPipe=new BoundingBox3(-100000,25,-5000,100000,100,8000);
var capped=SupportFocusedCubePlanner.Build(
    supportBox,new[]{extremelyTallPipe},1,100,500);
Assert(capped.PipesAccepted==1&&capped.PipesVerticalClipped==1&&
    capped.FocusBounds.MinZ==-500&&capped.FocusBounds.MaxZ==2500,
    "REV22.11 cap local vertical reach without stretching to route elevation");
var noPipes=SupportFocusedCubePlanner.Build(
    supportBox,Array.Empty<BoundingBox3>(),1,50,500);
Assert(noPipes.PipesAccepted==0&&noPipes.FocusBounds.SizeX==350,
    "REV22.11 support-focused mode works without selecting pipes");
bool invalidCubeRejected=false;
try{
    SupportFocusedCubePlanner.Build(supportBox,
        new[]{new BoundingBox3(0,0,0,double.NaN,10,10)},1);
}catch(ArgumentException){invalidCubeRejected=true;}
Assert(invalidCubeRejected,"REV22.11 reject invalid pipe bounding extents");
// REV22.12: no typed XY or Z clearance. Geometry drives both values.
var adaptiveSmall=new BoundingBox3(0,0,0,200,150,400);
var adaptiveLarge=new BoundingBox3(0,0,0,1800,1400,3500);
var autoSmall=SupportFocusedCubePlanner.BuildAutomatic(
    adaptiveSmall,Array.Empty<BoundingBox3>(),1);
var autoLarge=SupportFocusedCubePlanner.BuildAutomatic(
    adaptiveLarge,Array.Empty<BoundingBox3>(),1);
Assert(autoSmall.XYClearanceMm==50&&autoSmall.PipeVerticalReachMm==300,
    "REV22.12 enforce minimum safe geometry-derived XY and Z reaches");
Assert(autoLarge.XYClearanceMm==160&&autoLarge.PipeVerticalReachMm==1200,
    "REV22.12 cap automatic clearances for large steel support");
var adaptiveExample=new BoundingBox3(4471.098,3022.743,-6,
    5174.373,3946.972,1376.489);
var autoExample=SupportFocusedCubePlanner.BuildAutomatic(
    adaptiveExample,new[]{new BoundingBox3(
        -25000,3100,1350,25000,3200,1490)},1);
Assert(autoExample.XYClearanceMm>80&&autoExample.XYClearanceMm<90,
    "REV22.12 actual user support dimensions determine XY padding");
Assert(autoExample.PipeVerticalReachMm>600&&
    autoExample.PipeVerticalReachMm<650,
    "REV22.12 actual user support dimensions determine vertical reach");
Assert(autoExample.Cube.PipesAccepted==1 &&
    autoExample.Cube.FocusBounds.MaxX<
        adaptiveExample.MaxX+200 &&
    autoExample.Cube.FocusBounds.MaxZ==1490,
    "REV22.12 long pipe route remains XY-clipped and Z diameter included");
var scaledMeters=SupportFocusedCubePlanner.BuildAutomatic(
    new BoundingBox3(0,0,0,1.0,.5,1.5),
    new[]{new BoundingBox3(-200,.2,1.6,200,.3,1.7)},1000);
Assert(Math.Abs(scaledMeters.XYClearanceMm-60)<1e-7 &&
    Math.Abs(scaledMeters.PipeVerticalReachMm-675)<1e-7 &&
    scaledMeters.Cube.FocusBounds.MaxX<1.5,
    "REV22.12 automatic units conversion and long-route containment");
bool badAutoUnits=false;
try{ SupportFocusedCubePlanner.BuildAutomatic(
    adaptiveSmall,Array.Empty<BoundingBox3>(),double.NaN); }
catch(ArgumentOutOfRangeException){badAutoUnits=true;}
Assert(badAutoUnits,"REV22.12 fail closed for missing DWG unit scale");
// REV22.13: view titles are independent paper-space MText based on the
// exact saved native ViewType labels, outside every Ortho viewport.
string[] titlesInOrder={"TOP","BOTTOM","LEFT","RIGHT","FRONT",
    "BACK","NW","NE","SW","SE"};
double[] titleY={679.812,679.812,545.252,545.252,410.692,
    410.692,276.132,276.132,141.572,141.572};
double[] titleH={45.907,45.907,58.065,58.065,58.065,
    58.065,81.104,81.104,81.104,81.104};
double[] titleW={36.627,36.627,45.907,45.907,36.627,
    36.627,58.36,58.36,58.36,58.36};
var titleFrames=titlesInOrder.Select((name,i)=>
    new NativeOrthoViewTitlePlan.Frame(
        name,i%2==0?95:314.78,titleY[i],titleW[i],titleH[i])).ToArray();
var reservedTitleFrames=NativeOrthoViewTitlePlan.ReserveBelow(
    titleFrames,594,841);
Assert(reservedTitleFrames.ShiftY>9&&reservedTitleFrames.ShiftY<11,
    "REV22.13 automatically reserve bottom title band without changing group geometry");
Assert(reservedTitleFrames.Titles.Count==10,
    "REV22.13 ten distinct titles produced");
Assert(reservedTitleFrames.Titles[0].Text=="TOP VIEW"&&
    reservedTitleFrames.Titles[1].Text=="BOTTOM VIEW"&&
    reservedTitleFrames.Titles[6].Text=="NW ISOMETRIC VIEW"&&
    reservedTitleFrames.Titles[9].Text=="SE ISOMETRIC VIEW",
    "REV22.13 title strings match native preset names");
for(int i=0;i<10;i++)
{
    var original=titleFrames[i];
    var adjusted=reservedTitleFrames.Frames[i];
    var title=reservedTitleFrames.Titles[i];
    Assert(adjusted.X==original.X&&
        Math.Abs(adjusted.Y-original.Y-reservedTitleFrames.ShiftY)<1e-6&&
        adjusted.W==original.W&&adjusted.H==original.H,
        "REV22.13 preserve projection spacing, widths and heights");
    Assert(title.TopY<adjusted.Y-adjusted.H/2 &&
        title.TopY-title.Height>=841*.12-1e-6,
        "REV22.13 "+title.Name+" caption below its frame and above title block");
}
bool unknownCaptionRejected=false;
try{NativeOrthoViewTitlePlan.CaptionFor("Current");}
catch(ArgumentException){unknownCaptionRejected=true;}
Assert(unknownCaptionRejected,
    "REV22.13 Current View never receives an invented title");
bool overlapRejected=false;
try
{
    var overlapped=reservedTitleFrames.Frames.ToArray();
    overlapped[2]=overlapped[2] with{
        X=overlapped[0].X,
        Y=reservedTitleFrames.Titles[0].TopY-3
    };
    NativeOrthoViewTitlePlan.BuildTitles(overlapped,594,841);
}
catch(InvalidOperationException){overlapRejected=true;}
Assert(overlapRejected,"REV22.13 reject title/viewport collisions");
bool noRoomRejected=false;
try
{
    var tooHigh=titleFrames.Select(f=>f with {Y=f.Y+90}).ToArray();
    NativeOrthoViewTitlePlan.ReserveBelow(tooHigh,594,841);
}
catch(InvalidOperationException){noRoomRejected=true;}
Assert(noRoomRejected,"REV22.13 fail closed if caption group exceeds sheet keepout");
Console.WriteLine("PASS: REV22.13 ten native preset view titles, whole-group caption clearance and no overlaps.");

Console.WriteLine("PASS: REV22.12 numeric-free automatically derived SupportFocus clearances and long-route clipping.");

Console.WriteLine("PASS: REV22.11 compact support-based OrthoCube with long-route XY clipping and local Z selection.");

Console.WriteLine("PASS: REV22.10 six axial half-turn and center guards.");

Console.WriteLine("PASS: REV22.8 six native axial Edit View presets are runtime-recognized or fail closed.");

Console.WriteLine("PASS: REV22.7 four native ISO view-center sign flips with guarded twist correction.");

Console.WriteLine("PASS: REV22.6 native reference vs generated camera+twist comparison.");

Console.WriteLine("PASS: REV22.5 four WCS projected-up isometric cameras and 180-degree twist correction.");

Console.WriteLine("PASS: REV22.3 actual native viewport frame, collision and scale guards.");

Console.WriteLine("PASS: REV22.2 exact native Edit View preset metadata and six unmodified axial views.");

Console.WriteLine("PASS: REV22.1 canonical NW NE SW SE native strings, no Current View fallback and six standard views unchanged.");

Console.WriteLine("PASS: REV22 ten orthographic and isometric views, common scale and single-click fit.");

Console.WriteLine("PASS: REV21.1 one-click automatic group translation and sheet guards.");
bool nonFiniteRejected=false;
try{NativeOrthoOneClickPlan.Build(threeA1,594,841,double.NaN,200);}
catch(ArgumentException){nonFiniteRejected=true;}
Assert(nonFiniteRejected,"REV21 rejects invalid insertion point");
Console.WriteLine("PASS: REV21 one-click TOP anchor, FRONT/SIDE offsets and placement guards.");

Console.WriteLine("PASS: REV20 proposed three-view orientation, common scale and collisions.");
Console.WriteLine("PASS: REV17 paper-aware TOP view planning and REV16 native Ortho safety guards.");
static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: "+name); }
