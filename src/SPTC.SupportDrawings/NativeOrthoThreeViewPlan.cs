using System;
using System.Collections.Generic;
using System.Linq;
namespace SPTC.SupportDrawings;

/// <summary>REV20 proposed common-scale paper layout; not an enforced native placement.</summary>
public static class NativeOrthoThreeViewPlan
{
    public sealed record Slot(string Name,string EnumName,string TypeName,
        double Dx,double Dy,double Dz,double Ux,double Uy,double Uz,
        double X,double Y,double W,double H,double CenterX,double CenterY,
        double ModelWmm,double ModelHmm);
    public sealed record Plan(int Denominator,double Scale,
        IReadOnlyList<Slot> Views,string Status);
    public static Plan Build(NativeOrthoLayout.Plan source,double paperW,double paperH)
    {
        if(source==null)throw new ArgumentNullException(nameof(source));
        if(!double.IsFinite(paperW)||!double.IsFinite(paperH)||paperW<=0||paperH<=0)
            throw new ArgumentOutOfRangeException(nameof(paperW));
        if(source.Views.Count<3||source.Views[0].Name!="TOP"||
            source.Views[1].Name!="FRONT"||source.Views[2].Name!="SIDE")
            throw new ArgumentException("Three expected native Ortho orientations missing.");
        // Rightmost 22% and bottom 12% reserved as a title-block estimate.
        // Actual placement/title-block interaction must be checked from DWG.
        var tiles=new(string EnumName,string TypeName,double X,double Y,double W,double H)[]
        {
            ("Top","TopView",.07,.55,.42,.33),
            ("Front","FrontView",.07,.16,.42,.30),
            ("Right","RightView",.52,.16,.24,.30)
        };
        double minimum=1;
        for(int i=0;i<3;i++)
        {
            var a=source.Views[i];var t=tiles[i];
            minimum=Math.Max(minimum,Math.Max(
                a.ModelWidth*source.SourceUnitToMm/(paperW*t.W*.85),
                a.ModelHeight*source.SourceUnitToMm/(paperH*t.H*.85)));
        }
        int[] standards={1,2,3,5,10,15,20,25,30,40,50,75,100,150,
            200,250,500,1000,2000,5000,10000};
        int denom=standards.FirstOrDefault(v=>v>=minimum);
        if(denom==0)
        {
            double large=Math.Ceiling(minimum/10000.0)*10000;
            if(large>int.MaxValue)throw new InvalidOperationException("Scale too small.");
            denom=(int)large;
        }
        var output=new List<Slot>();
        for(int i=0;i<3;i++)
        {
            var v=source.Views[i];var t=tiles[i];
            double x=paperW*t.X,y=paperH*t.Y,w=paperW*t.W,h=paperH*t.H;
            if(x<=0||y<paperH*.12||x+w>paperW*.78||y+h>paperH*.94)
                throw new InvalidOperationException("Proposed tile enters reserved title-block area.");
            if(v.ModelWidth*source.SourceUnitToMm/denom>w*.85+1e-6 ||
                v.ModelHeight*source.SourceUnitToMm/denom>h*.85+1e-6)
                throw new InvalidOperationException("Proposed scale clips a support view.");
            output.Add(new Slot(v.Name,t.EnumName,t.TypeName,
                v.DirectionX,v.DirectionY,v.DirectionZ,v.UpX,v.UpY,v.UpZ,
                x,y,w,h,x+w/2,y+h/2,
                v.ModelWidth*source.SourceUnitToMm,v.ModelHeight*source.SourceUnitToMm));
        }
        for(int i=0;i<3;i++)for(int j=i+1;j<3;j++)
        {
            var a=output[i];var b=output[j];
            if(a.X<b.X+b.W&&b.X<a.X+a.W&&a.Y<b.Y+b.H&&b.Y<a.Y+a.H)
                throw new InvalidOperationException("Proposed tiles overlap.");
        }
        return new Plan(denom,1.0/denom,output,
            "PROPOSED_ONLY: public native CreateOrthos API does not accept a paper insertion point.");
    }
}
