using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV17 sheet-aware candidate for the first native TOP Ortho view.
/// This is layout planning, not an Autodesk Ortho view definition.
/// </summary>
public static class NativeOrthoTopViewPlan
{
    public sealed record TopPlan(
        double PaperWidthMm, double PaperHeightMm,
        double PaperCenterX, double PaperCenterY,
        double TargetTileWidthMm, double TargetTileHeightMm,
        int CandidateScaleDenominator, double CandidateScale,
        double ProjectedModelWidthMm, double ProjectedModelHeightMm,
        string Status);

    public static TopPlan Build(NativeOrthoLayout.Plan modelPlan,
        double paperWidthMm, double paperHeightMm)
    {
        if (modelPlan == null) throw new ArgumentNullException(nameof(modelPlan));
        if (!double.IsFinite(paperWidthMm) || !double.IsFinite(paperHeightMm) ||
            paperWidthMm <= 0 || paperHeightMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(paperWidthMm), "Actual paper size in mm is required.");
        if (modelPlan.Views.Count == 0 || modelPlan.Views[0].Name != "TOP")
            throw new ArgumentException("TOP view plan missing.", nameof(modelPlan));

        // Reserve the right 28% for a potential title block and other notes.
        // The template may differ; placement MUST be validated from the actual
        // drawing's geometry before generating a native Ortho view.
        double tileW = paperWidthMm * 0.55;
        double tileH = paperHeightMm * 0.50;
        double centerX = paperWidthMm * 0.32;
        double centerY = paperHeightMm * 0.62;
        var top = modelPlan.Views[0];
        double sourceWmm = top.ModelWidth * modelPlan.SourceUnitToMm;
        double sourceHmm = top.ModelHeight * modelPlan.SourceUnitToMm;
        double required = Math.Max(1, Math.Max(sourceWmm / (tileW * 0.85),
            sourceHmm / (tileH * 0.85)));

        int[] preferred = {1,2,3,5,10,15,20,25,30,40,50,75,100,150,200,250,
            500,1000,2000,5000,10000};
        int denominator = preferred.FirstOrDefault(v => v >= required);
        if (denominator == 0)
            denominator = (int)(Math.Ceiling(required / 10000.0) * 10000);
        return new TopPlan(paperWidthMm, paperHeightMm,
            centerX, centerY, tileW, tileH, denominator, 1.0 / denominator,
            sourceWmm, sourceHmm,
            "PROPOSED_ONLY: Layout/title-block clearance and native Ortho placement not validated.");
    }
}
