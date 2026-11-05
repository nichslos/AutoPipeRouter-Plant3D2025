using SPTC.AutoRoute;

string[] orders = ["XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX"];
var a = new RoutePoint(100, 200, 300);
var b = new RoutePoint(400, 500, 600);
int count = 0;

foreach (string order in orders)
{
    var points = RoutePlanner.Build(a, b, order);
    Assert(points.Count == 4, "three-axis path should have four vertices");
    Assert(points[0] == a && points[^1] == b, "endpoint mismatch");
    for (int i = 1; i < points.Count; i++)
    {
        RoutePoint p = points[i - 1], q = points[i];
        int differences = (p.X != q.X ? 1 : 0) + (p.Y != q.Y ? 1 : 0) + (p.Z != q.Z ? 1 : 0);
        Assert(differences == 1, "non-orthogonal leg");
    }
    count++;
}
Assert(RoutePlanner.Build(new(0, 0, 0), new(20, 0, 50), "XYZ").Count == 3, "skip unchanged axis");
count++;
Assert(RoutePlanner.Build(new(0, 0, 0), new(0, 0, 100), "XYZ").Count == 2, "single straight line");
count++;
bool threw = false;
try { RoutePlanner.Build(a, a, "XYZ"); }
catch (ArgumentException) { threw = true; }
Assert(threw, "coincident point check");
count++;
Console.WriteLine($"PASS: {count} smoke checks.");

static void Assert(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
}
