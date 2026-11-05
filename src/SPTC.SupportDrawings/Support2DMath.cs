using System;

namespace SPTC.SupportDrawings;

public enum OrthoPlane { Top, Front, Side }

public readonly record struct SketchPoint3(double X, double Y, double Z);
public readonly record struct SketchPoint2(double X, double Y);
public readonly record struct SketchSegment3(SketchPoint3 Start, SketchPoint3 End);

/// <summary>
/// Orthographic LINEWORK projection only. No visibility/hidden-line elimination.
/// Coordinates are in source drawing units; nothing is scaled to paper units.
/// </summary>
public static class Support2DMath
{
    public static SketchPoint2 Project(
        SketchPoint3 point, BoundingBox3 bounds, OrthoPlane view, double gap)
    {
        if (gap <= 0 || !double.IsFinite(gap))
            throw new ArgumentOutOfRangeException(nameof(gap));

        return view switch
        {
            OrthoPlane.Front => new(
                gap + point.X - bounds.MinX,
                gap + point.Z - bounds.MinZ),
            OrthoPlane.Top => new(
                gap + point.X - bounds.MinX,
                2 * gap + Math.Max(bounds.SizeZ, 1) + point.Y - bounds.MinY),
            OrthoPlane.Side => new(
                2 * gap + Math.Max(bounds.SizeX, 1) + point.Y - bounds.MinY,
                gap + point.Z - bounds.MinZ),
            _ => throw new ArgumentOutOfRangeException(nameof(view))
        };
    }

    public static BoundingBox3 FromSegments(
        System.Collections.Generic.IReadOnlyList<SketchSegment3> segments)
    {
        if (segments == null || segments.Count == 0)
            throw new ArgumentException("No supported 3D linework exists.", nameof(segments));

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        foreach (SketchSegment3 segment in segments)
        {
            foreach (SketchPoint3 p in new[] { segment.Start, segment.End })
            {
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z))
                    throw new ArgumentException("Linework contains a non-finite coordinate.", nameof(segments));

                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }
        }
        return new BoundingBox3(minX, minY, minZ, maxX, maxY, maxZ);
    }

    public static double Gap(BoundingBox3 bounds)
    {
        double size = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
        return Math.Max(1, size * 0.25);
    }

    public static double DistanceSquared(SketchPoint2 a, SketchPoint2 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
