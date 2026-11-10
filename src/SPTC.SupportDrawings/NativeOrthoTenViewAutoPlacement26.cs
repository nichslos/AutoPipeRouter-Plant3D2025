using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.26: pick the TEN-view group anchor deterministically from the
/// already-computed native Ortho sheet plan. No Editor.GetPoint or mouse
/// placement. Preserves the tested two-column/five-row group and all titles.
/// If the complete group (including captions) cannot fit, stop rather than
/// write an off-sheet or overlapping native viewport.
/// </summary>
public static class NativeOrthoTenViewAutoPlacement26
{
    public sealed record Result(
        double SuggestedTopX,double SuggestedTopY,
        double EffectiveTopX,double EffectiveTopY,
        double FinalTopX,double FinalTopY,
        double TitleShiftY,
        bool WasShifted,
        IReadOnlyList<NativeOrthoTenViewPlan.Placement> Views,
        IReadOnlyList<NativeOrthoViewTitlePlan.Title> Titles);

    public static Result Build(
        NativeOrthoTenViewPlan.Plan candidate,
        double paperWidth,double paperHeight)
    {
        if(candidate==null||candidate.Views.Count!=10||
            candidate.Views[0].Name!="TOP")
            throw new ArgumentException("Ten-view TOP-first native plan required.");
        if(!double.IsFinite(paperWidth)||!double.IsFinite(paperHeight)||
           paperWidth<=0||paperHeight<=0)
            throw new ArgumentOutOfRangeException(nameof(paperWidth));
        // Build() originally places the TOP view at 20% of sheet width,
        // 86% of sheet height, with the other nine views in fixed slots.
        // This is the entire group's recommended point, not user input.
        double topX=candidate.Views[0].CenterX;
        double topY=candidate.Views[0].CenterY;
        var adjusted=NativeOrthoTenViewPlan.BuildAdjusted(
            candidate,paperWidth,paperHeight,topX,topY);
        var reserved=NativeOrthoViewTitlePlan.ReserveBelow(
            adjusted.Views.Select(v=>new NativeOrthoViewTitlePlan.Frame(
                v.Name,v.CenterX,v.CenterY,
                v.PaperFrameWidth,v.PaperFrameHeight)).ToArray(),
            paperWidth,paperHeight);
        if(reserved.Frames.Count!=10||reserved.Titles.Count!=10)
            throw new InvalidOperationException(
                "REV22.26 failed to reserve ten native frames and captions.");
        var final=reserved.Frames.Select(v=>
            new NativeOrthoTenViewPlan.Placement(
                v.Name,v.X,v.Y,v.W,v.H)).ToArray();

        // The original two-column five-row geometry and positions are
        // retained exactly, modulo a single title-safe group translation.
        for(int i=0;i<final.Length;i++)
        {
            var original=adjusted.Views[i];
            var current=final[i];
            if(current.Name!=original.Name ||
                Math.Abs(current.CenterX-original.CenterX)>1e-8 ||
                Math.Abs(current.CenterY-original.CenterY-reserved.ShiftY)>1e-8 ||
                Math.Abs(current.PaperFrameWidth-original.PaperFrameWidth)>1e-8 ||
                Math.Abs(current.PaperFrameHeight-original.PaperFrameHeight)>1e-8)
                throw new InvalidOperationException(
                    "REV22.26 changed individual native view slot "+current.Name);
        }
        return new Result(topX,topY,
            adjusted.EffectiveTopX,adjusted.EffectiveTopY,
            final[0].CenterX,final[0].CenterY,
            reserved.ShiftY,adjusted.WasAdjusted||
                Math.Abs(reserved.ShiftY)>1e-8,
            final,reserved.Titles);
    }
}
