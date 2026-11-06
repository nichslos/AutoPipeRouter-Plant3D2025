using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV11 pure geometry/sheet-layout plan for later native Plant 3D Ortho API use.
/// No project database or drawing mutations.
/// Scale is computed only for supported unit-to-mm conversions.
/// </summary>
public static class NativeOrthoLayout
{
    public readonly record struct ViewPlan(
        string Name, double DirectionX, double DirectionY, double DirectionZ,
        double UpX, double UpY, double UpZ,
        double PaperCenterX, double PaperCenterY,
        double PaperWidth, double PaperHeight, double CandidateScale, int ScaleDenominator,
        double ModelWidth, double ModelHeight);

    public sealed record Plan(
        BoundingBox3 OriginalBounds, BoundingBox3 PaddedCube,
        double PaddingX, double PaddingY, double PaddingZ,
        double SheetWidthMm, double SheetHeightMm, double SourceUnitToMm,
        IReadOnlyList<ViewPlan> Views);

    public static Plan Build(BoundingBox3 original, double sourceUnitToMm, double marginPercent = 10.0)
    {
        if (!double.IsFinite(sourceUnitToMm) || sourceUnitToMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceUnitToMm), "Source-to-mm conversion required.");
        if (!double.IsFinite(marginPercent) || marginPercent < 0 || marginPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(marginPercent));
        if (!double.IsFinite(original.SizeX) || !double.IsFinite(original.SizeY) ||
            !double.IsFinite(original.SizeZ))
            throw new ArgumentException("Invalid model bounds.", nameof(original));

        double longest = Math.Max(original.SizeX, Math.Max(original.SizeY, original.SizeZ));
        if (longest <= 0) throw new ArgumentException("Model envelope is empty.", nameof(original));
        // REV11: axis-specific clearance. The old longest-axis padding made a
        // 75 mm-thick support 319.88 mm wide, capturing nearby plant geometry.
        // Keep at least 10 mm each side (in DWG units), then expand 10% per axis.
        double minimum = 10.0 / sourceUnitToMm;
        double padX = Math.Max(minimum, original.SizeX * marginPercent / 100.0);
        double padY = Math.Max(minimum, original.SizeY * marginPercent / 100.0);
        double padZ = Math.Max(minimum, original.SizeZ * marginPercent / 100.0);

        var expanded = new BoundingBox3(
            original.MinX - padX, original.MinY - padY, original.MinZ - padZ,
            original.MaxX + padX, original.MaxY + padY, original.MaxZ + padZ);
        // A3 landscape (420×297 mm). All four view centers/tiles are in paper mm.
        // Independent top/front/side views; no authoritative native scale yet.
        var views = new List<ViewPlan>
        {
            View("TOP", 0,0,1, 0,1,0, 112,210, 176,102, expanded.SizeX,expanded.SizeY,sourceUnitToMm),
            View("FRONT", 0,-1,0, 0,0,1, 112,95, 176,84, expanded.SizeX,expanded.SizeZ,sourceUnitToMm),
            View("SIDE", 1,0,0, 0,0,1, 313,95, 176,84, expanded.SizeY,expanded.SizeZ,sourceUnitToMm)
        };
        return new Plan(original, expanded, padX, padY, padZ, 420, 297, sourceUnitToMm, views);
    }

    /// <summary>
    /// Name the proposed plan using the ACTIVE source drawing and selected
    /// objects. Never embed an example project, DWG, member handle or zone.
    /// </summary>
    public static string SuggestedPlanFileName(string sourceDwg,
        IEnumerable<string> selectedHandles, string revision)
    {
        if (string.IsNullOrWhiteSpace(sourceDwg))
            throw new ArgumentException("Active source model path is required.", nameof(sourceDwg));
        if (selectedHandles == null) throw new ArgumentNullException(nameof(selectedHandles));
        string modelName = Path.GetFileNameWithoutExtension(sourceDwg);
        string handle = selectedHandles.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) ?? "NO_SELECTION";
        return "SPTC_" + SafeFilePart(modelName, 48) + "_" + SafeFilePart(handle, 16) +
            "_ORTHO_AUTOPLAN_" + SafeFilePart(revision, 12) + ".json";
    }

    private static string SafeFilePart(string value, int maximum)
    {
        string safe = new string(value.Select(c =>
            char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray()).Trim('_');
        if (safe.Length == 0) return "UNNAMED";
        return safe.Length > maximum ? safe[..maximum] : safe;
    }

    private static ViewPlan View(string name, double dx, double dy, double dz,
        double ux, double uy, double uz, double px, double py, double paperW, double paperH,
        double modelW, double modelH, double sourceUnitToMm)
    {
        // Reserve 15% in each tile and choose a standard scale with no clipping.
        double widthMm = modelW * sourceUnitToMm, heightMm = modelH * sourceUnitToMm;
        double minimumDenominator = Math.Max(1,
            Math.Max(widthMm / (paperW * .85), heightMm / (paperH * .85)));
        int denominator = StandardDenominator(minimumDenominator);
        return new ViewPlan(name, dx,dy,dz, ux,uy,uz,
            px,py,paperW,paperH,1.0 / denominator,denominator,modelW,modelH);
    }

    private static int StandardDenominator(double required)
    {
        int[] scales = {1,2,3,5,10,15,20,25,30,40,50,75,100,150,200,250,500,1000,2000,5000,10000};
        foreach (int value in scales) if (value >= required) return value;
        return (int)Math.Ceiling(required / 10000) * 10000;
    }
}
