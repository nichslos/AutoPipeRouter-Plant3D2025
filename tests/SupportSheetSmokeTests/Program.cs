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
Console.WriteLine("PASS: REV04 bounds, class hints, 2D orthographic geometry, and empty input checks.");
static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: "+name); }
