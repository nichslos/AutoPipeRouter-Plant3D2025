using System;
using System.Collections.Generic;

namespace SPTC.AutoRoute;

public readonly record struct RoutePoint(double X, double Y, double Z)
{
    public double DistanceTo(RoutePoint other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        double dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>
/// REV 01: orthogonal 3-axis route only. No obstacle, port or spec checks.
/// </summary>
public static class RoutePlanner
{
    private const double Epsilon = 1e-7;

    public static IReadOnlyList<RoutePoint> Build(RoutePoint start, RoutePoint end, string axisOrder)
    {
        if (start.DistanceTo(end) <= Epsilon)
            throw new ArgumentException("Start and end are coincident.");
        if (axisOrder is not ("XYZ" or "XZY" or "YXZ" or "YZX" or "ZXY" or "ZYX"))
            throw new ArgumentException("Axis order must be XYZ, XZY, YXZ, YZX, ZXY, or ZYX.");

        var result = new List<RoutePoint> { start };
        RoutePoint current = start;
        foreach (char axis in axisOrder)
        {
            RoutePoint next = axis switch
            {
                'X' => current with { X = end.X },
                'Y' => current with { Y = end.Y },
                'Z' => current with { Z = end.Z },
                _ => throw new InvalidOperationException("Unexpected axis")
            };
            if (current.DistanceTo(next) > Epsilon)
            {
                result.Add(next);
                current = next;
            }
        }
        result[^1] = end;
        return result;
    }
}
