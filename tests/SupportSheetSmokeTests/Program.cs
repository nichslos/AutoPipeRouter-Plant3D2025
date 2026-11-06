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
Console.WriteLine("PASS: REV14 dynamic Ortho DWG naming, project boundary guards and prior geometry tests.");
static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: "+name); }
