using System;

namespace SPTC.SupportDrawings;

public readonly record struct BoundingBox3(
    double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    public double SizeX => Math.Max(0.0, MaxX - MinX);
    public double SizeY => Math.Max(0.0, MaxY - MinY);
    public double SizeZ => Math.Max(0.0, MaxZ - MinZ);
    public double MidX => (MinX + MaxX) / 2.0;
    public double MidY => (MinY + MaxY) / 2.0;
    public double MidZ => (MinZ + MaxZ) / 2.0;
    public BoundingBox3 Include(BoundingBox3 other) => new(
        Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Min(MinZ, other.MinZ),
        Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY), Math.Max(MaxZ, other.MaxZ));
}

public static class SupportSheetMath
{
    /// <summary>Model-space height needed to fit bounding extents inside viewport (20% margin).</summary>
    public static double ViewHeight(BoundingBox3 b, string view, double viewportWidth, double viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        double width;
        double height;
        switch (view)
        {
            case "TOP": width = b.SizeX; height = b.SizeY; break;
            case "FRONT": width = b.SizeX; height = b.SizeZ; break;
            case "SIDE": width = b.SizeY; height = b.SizeZ; break;
            case "ISO":
                width = b.SizeX + b.SizeY;
                height = b.SizeZ + (b.SizeX + b.SizeY) * .6;
                break;
            default: throw new ArgumentException("Unknown view.", nameof(view));
        }
        const double safeMargin = .80;
        return Math.Max(1.0, Math.Max(height / safeMargin, width * viewportHeight / (viewportWidth * safeMargin)));
    }
}
