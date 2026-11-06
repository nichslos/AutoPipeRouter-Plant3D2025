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
bool nonFiniteRejected=false;
try{NativeOrthoOneClickPlan.Build(threeA1,594,841,double.NaN,200);}
catch(ArgumentException){nonFiniteRejected=true;}
Assert(nonFiniteRejected,"REV21 rejects invalid insertion point");
Console.WriteLine("PASS: REV21 one-click TOP anchor, FRONT/SIDE offsets and placement guards.");

Console.WriteLine("PASS: REV20 proposed three-view orientation, common scale and collisions.");
Console.WriteLine("PASS: REV17 paper-aware TOP view planning and REV16 native Ortho safety guards.");
static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: "+name); }
