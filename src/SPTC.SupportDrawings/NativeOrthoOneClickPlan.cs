using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV21 pure geometry: one TOP anchor becomes TOP/FRONT/SIDE paper positions.
/// Public Plant 3D CreateOrthos has no insertion point; internal generation
/// is experimental. This helper never opens or modifies DWGs.
/// </summary>
public static class NativeOrthoOneClickPlan
{
    public sealed record Placement(
        string Name,double CenterX,double CenterY,
        double PaperFrameWidth,double PaperFrameHeight);

    // REV21.1: one click anywhere inside the actual sheet. If a valid
    // three-view group cannot fit at the requested TOP center, translate the
    // WHOLE group to its nearest valid position. Never scale or reposition
    // individual projections independently, and never silently shift the
    // original click: expose both anchors and the exact displacement.
    public sealed record AdjustedGroup(
        double RequestedTopX,double RequestedTopY,
        double EffectiveTopX,double EffectiveTopY,
        double MinimumTopX,double MaximumTopX,
        double MinimumTopY,double MaximumTopY,
        bool WasAdjusted,IReadOnlyList<Placement> Views);

    public static AdjustedGroup BuildAdjusted(
        NativeOrthoThreeViewPlan.Plan candidate,
        double paperWidth,double paperHeight,double anchorX,double anchorY)
    {
        if(candidate==null)throw new ArgumentNullException(nameof(candidate));
        if(candidate.Views.Count!=3||candidate.Views[0].Name!="TOP")
            throw new ArgumentException("Three-view plan with TOP first required.");
        if(!double.IsFinite(paperWidth)||!double.IsFinite(paperHeight)||
            paperWidth<=0||paperHeight<=0||
            !double.IsFinite(anchorX)||!double.IsFinite(anchorY))
            throw new ArgumentException("Finite positive paper size and anchor required.");

        // A click outside the physical paper is not a meaningful placement.
        // Do not silently accept an anchor in model space or off the sheet.
        if(anchorX<0||anchorX>paperWidth||anchorY<0||anchorY>paperHeight)
            throw new ArgumentOutOfRangeException(nameof(anchorX),
                "The single placement point must be inside the paper sheet.");

        double topX=candidate.Views[0].CenterX;
        double topY=candidate.Views[0].CenterY;
        double minimumX=double.NegativeInfinity;
        double maximumX=double.PositiveInfinity;
        double minimumY=double.NegativeInfinity;
        double maximumY=double.PositiveInfinity;

        foreach(var slot in candidate.Views)
        {
            double width=Math.Max(5,slot.ModelWmm*candidate.Scale*1.05);
            double height=Math.Max(5,slot.ModelHmm*candidate.Scale*1.05);
            double deltaX=slot.CenterX-topX;
            double deltaY=slot.CenterY-topY;
            // Match Build's conservative keepout: left=0, right=78%,
            // bottom=12%, top=94% of the configured physical sheet.
            minimumX=Math.Max(minimumX,width/2-deltaX);
            maximumX=Math.Min(maximumX,paperWidth*.78-width/2-deltaX);
            minimumY=Math.Max(minimumY,paperHeight*.12+height/2-deltaY);
            maximumY=Math.Min(maximumY,paperHeight*.94-height/2-deltaY);
        }
        if(minimumX>maximumX||minimumY>maximumY||
            !double.IsFinite(minimumX)||!double.IsFinite(maximumX)||
            !double.IsFinite(minimumY)||!double.IsFinite(maximumY))
            throw new InvalidOperationException(
                "The three-view group cannot fit inside the available sheet " +
                "and reserved title-block limits, even with automatic translation.");

        double effectiveX=Math.Clamp(anchorX,minimumX,maximumX);
        double effectiveY=Math.Clamp(anchorY,minimumY,maximumY);
        // The original strict geometry check still validates each translated
        // viewport and rejects any overlapping projected view frames.
        IReadOnlyList<Placement> views=Build(
            candidate,paperWidth,paperHeight,effectiveX,effectiveY);
        bool adjusted=Math.Abs(effectiveX-anchorX)>1e-8||
            Math.Abs(effectiveY-anchorY)>1e-8;
        return new AdjustedGroup(anchorX,anchorY,effectiveX,effectiveY,
            minimumX,maximumX,minimumY,maximumY,adjusted,views);
    }

    public static IReadOnlyList<Placement> Build(
        NativeOrthoThreeViewPlan.Plan candidate,
        double paperWidth,double paperHeight,double anchorX,double anchorY)
    {
        if(candidate==null)throw new ArgumentNullException(nameof(candidate));
        if(candidate.Views.Count!=3||candidate.Views[0].Name!="TOP")
            throw new ArgumentException("Three-view plan with TOP first required.");
        if(!double.IsFinite(paperWidth)||!double.IsFinite(paperHeight)||
            paperWidth<=0||paperHeight<=0||
            !double.IsFinite(anchorX)||!double.IsFinite(anchorY))
            throw new ArgumentException("Finite positive paper size and anchor required.");
        var output=new List<Placement>(3);
        double topX=candidate.Views[0].CenterX,topY=candidate.Views[0].CenterY;
        foreach(var slot in candidate.Views)
        {
            double x=anchorX+(slot.CenterX-topX),y=anchorY+(slot.CenterY-topY);
            double w=Math.Max(5,slot.ModelWmm*candidate.Scale*1.05);
            double h=Math.Max(5,slot.ModelHmm*candidate.Scale*1.05);
            if(x-w/2<0||y-h/2<paperHeight*.12||
                x+w/2>paperWidth*.78||y+h/2>paperHeight*.94)
                throw new InvalidOperationException(
                    "One-click placement for "+slot.Name+
                    " enters the tentative title-block keepout or extends off-sheet. " +
                    "Pick TOP near its suggested center.");
            output.Add(new Placement(slot.Name,x,y,w,h));
        }
        for(int i=0;i<output.Count;i++)
            for(int j=i+1;j<output.Count;j++)
            {
                var a=output[i];var b=output[j];
                if(Math.Abs(a.CenterX-b.CenterX)<(a.PaperFrameWidth+b.PaperFrameWidth)/2 &&
                    Math.Abs(a.CenterY-b.CenterY)<(a.PaperFrameHeight+b.PaperFrameHeight)/2)
                    throw new InvalidOperationException(
                        "Derived "+a.Name+" and "+b.Name+" paper viewports overlap.");
            }
        return output;
    }
}
