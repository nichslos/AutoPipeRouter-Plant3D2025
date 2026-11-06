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
