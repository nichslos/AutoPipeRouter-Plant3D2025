using System;
using System.Collections.Generic;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.11 source-DWG-unit geometry-only focus planner. Supports define
/// the horizontal OrthoCube footprint; optional long pipe routes may raise
/// or lower the local vertical envelope, but NEVER extend its X/Y limits.
/// Selected route bounds are ONLY a conservative intersection candidate;
/// the Autodesk native OrthoCube clips the actual referenced geometry.
/// </summary>
public static class SupportFocusedCubePlanner
{
    public sealed record Result(
        BoundingBox3 SupportBounds, BoundingBox3 FocusBounds,
        int PipesAccepted, int PipesRejected, int PipesVerticalClipped,
        double ClearanceSourceUnits, double VerticalReachSourceUnits);

    public static Result Build(
        BoundingBox3 support,
        IReadOnlyList<BoundingBox3> pipeBounds,
        double sourceUnitToMm,
        double xyClearanceMm = 100,
        double maxPipeVerticalReachMm = 500)
    {
        if(pipeBounds == null) throw new ArgumentNullException(nameof(pipeBounds));
        if(!double.IsFinite(sourceUnitToMm)||sourceUnitToMm<=0||
           !double.IsFinite(xyClearanceMm)||xyClearanceMm<0||
           !double.IsFinite(maxPipeVerticalReachMm)||maxPipeVerticalReachMm<0)
            throw new ArgumentException("REV22.11 finite nonnegative clearances and source units required.");
        Valid(support);
        if(support.SizeX<=0||support.SizeY<=0||support.SizeZ<=0)
            throw new ArgumentException("REV22.11 select a full 3D structural support, not a zero-volume object.");
        double xy=xyClearanceMm/sourceUnitToMm;
        double reach=maxPipeVerticalReachMm/sourceUnitToMm;
        double minX=support.MinX-xy,maxX=support.MaxX+xy;
        double minY=support.MinY-xy,maxY=support.MaxY+xy;
        double minZ=support.MinZ,maxZ=support.MaxZ;
        int accepted=0,rejected=0,verticalClipped=0;
        const double epsilon=1e-9;
        foreach(var pipe in pipeBounds)
        {
            Valid(pipe);
            bool xyHit=pipe.MaxX>=minX-epsilon&&pipe.MinX<=maxX+epsilon &&
                       pipe.MaxY>=minY-epsilon&&pipe.MinY<=maxY+epsilon;
            // Reject disjoint pipes whose bounds are too far above/below
            // the selected support; do not silently generate a huge cube.
            bool closeZ=pipe.MaxZ>=support.MinZ-reach-epsilon &&
                        pipe.MinZ<=support.MaxZ+reach+epsilon;
            if(!xyHit||!closeZ){rejected++;continue;}
            accepted++;
            double localMinZ=Math.Max(pipe.MinZ,support.MinZ-reach);
            double localMaxZ=Math.Min(pipe.MaxZ,support.MaxZ+reach);
            if(localMinZ>pipe.MinZ+epsilon||localMaxZ<pipe.MaxZ-epsilon)
                verticalClipped++;
            minZ=Math.Min(minZ,localMinZ);
            maxZ=Math.Max(maxZ,localMaxZ);
        }
        // This guarantee is the core requirement: long horizontal routes
        // cannot expand the cube to their distant endpoints.
        var focused=new BoundingBox3(minX,minY,minZ,maxX,maxY,maxZ);
        Valid(focused);
        return new Result(support,focused,accepted,rejected,verticalClipped,xy,reach);
    }

    /// <summary>
    /// REV22.12: derive both SupportFocus clearances from the selected
    /// structural support's actual dimensions. No user-supplied values.
    /// The XY footprint is NEVER based on a long pipe's full route extents.
    /// The capped Z reach prevents accidentally enclosing remote levels.
    /// </summary>
    public static AutoResult BuildAutomatic(
        BoundingBox3 support,
        IReadOnlyList<BoundingBox3> pipeBounds,
        double sourceUnitToMm)
    {
        if(!double.IsFinite(sourceUnitToMm)||sourceUnitToMm<=0)
            throw new ArgumentOutOfRangeException(nameof(sourceUnitToMm));
        Valid(support);
        double minHorizontalMm=Math.Min(support.SizeX,support.SizeY)*sourceUnitToMm;
        double heightMm=support.SizeZ*sourceUnitToMm;
        if(minHorizontalMm<=0||heightMm<=0||
           !double.IsFinite(minHorizontalMm)||!double.IsFinite(heightMm))
            throw new ArgumentException("REV22.12 support needs finite positive 3D extents.");

        // 12% of smaller support footprint (per-side), limited to a
        // practical 50-160 mm. Existing NativeOrthoLayout.Build() still
        // adds its axis-specific padding after this first stage.
        double autoXY=Math.Clamp(minHorizontalMm*0.12,50.0,160.0);

        // Up to 45% of support height, 300-1200 mm each side.
        // Selected nearby pipe extents can contribute ONLY their local
        // vertical overlap; the Z cap rejects distant floors/routing.
        double autoVertical=Math.Clamp(heightMm*0.45,300.0,1200.0);
        Result cube=Build(support,pipeBounds,sourceUnitToMm,
            autoXY,autoVertical);
        return new AutoResult(cube,autoXY,autoVertical);
    }

    public sealed record AutoResult(
        Result Cube,double XYClearanceMm,double PipeVerticalReachMm);

    private static void Valid(BoundingBox3 bounds)
    {
        if(!double.IsFinite(bounds.MinX)||!double.IsFinite(bounds.MinY)||
           !double.IsFinite(bounds.MinZ)||!double.IsFinite(bounds.MaxX)||
           !double.IsFinite(bounds.MaxY)||!double.IsFinite(bounds.MaxZ)||
           bounds.MinX>bounds.MaxX||bounds.MinY>bounds.MaxY||bounds.MinZ>bounds.MaxZ)
            throw new ArgumentException("REV22.11 invalid 3D bounding box.");
    }
}
