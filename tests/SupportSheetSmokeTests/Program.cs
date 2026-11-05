using SPTC.SupportDrawings;
var box = new BoundingBox3(10,20,30,110,220,330);
Assert(box.SizeX == 100 && box.SizeY == 200 && box.SizeZ == 300, "model spans");
Assert(box.MidX == 60 && box.MidY == 120 && box.MidZ == 180, "model midpoint");
Assert(box.Include(new(-10,-20,0,10,20,30)).MinX == -10, "union bounds");
foreach (var view in new[] { "TOP", "FRONT", "SIDE", "ISO" })
    Assert(SupportSheetMath.ViewHeight(box,view,180,120)>0, view+" fit");
Assert(SupportSheetMath.ViewHeight(box,"FRONT",180,120)>=375, "front vertical extent");
Console.WriteLine("PASS: support sheet bounds and 4 view fitting checks.");
static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: "+name); }
