using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>REV22 experimental 10-view A1 plan; no DWG or project mutations.</summary>
public static class NativeOrthoTenViewPlan
{
    public sealed record Slot(string Name,string EnumName,string TypeName,
        double Dx,double Dy,double Dz,double Ux,double Uy,double Uz,
        double X,double Y,double W,double H,double CenterX,double CenterY,
        double ModelWmm,double ModelHmm);
    public sealed record Plan(int Denominator,double Scale,IReadOnlyList<Slot> Views,string Status);
    public sealed record Placement(string Name,double CenterX,double CenterY,
        double PaperFrameWidth,double PaperFrameHeight);
    public sealed record AdjustedGroup(
        double RequestedTopX,double RequestedTopY,double EffectiveTopX,double EffectiveTopY,
        double MinimumTopX,double MaximumTopX,double MinimumTopY,double MaximumTopY,
        bool WasAdjusted,IReadOnlyList<Placement> Views);

    public static Plan Build(NativeOrthoLayout.Plan geometry,double paperWidth,double paperHeight)
    {
        if(geometry==null)throw new ArgumentNullException(nameof(geometry));
        if(!double.IsFinite(paperWidth)||!double.IsFinite(paperHeight)||paperWidth<=0||paperHeight<=0)
            throw new ArgumentOutOfRangeException(nameof(paperWidth));
        double root3=1/Math.Sqrt(3),root6=1/Math.Sqrt(6);
        var dirs=new (string Name,string EnumName,string TypeName,
            double Dx,double Dy,double Dz,double Ux,double Uy,double Uz)[]
        {
            ("TOP","Top","TopView",0,0,1,0,1,0),
            ("BOTTOM","Bottom","BottomView",0,0,-1,0,-1,0),
            ("LEFT","Left","LeftView",-1,0,0,0,0,1),
            ("RIGHT","Right","RightView",1,0,0,0,0,1),
            ("FRONT","Front","FrontView",0,-1,0,0,0,1),
            ("BACK","Back","BackView",0,1,0,0,0,1),
            ("NW","NW","NWIsometricView",-root3,root3,root3,root6,-root6,2*root6),
            ("NE","NE","NEIsometricView",root3,root3,root3,-root6,-root6,2*root6),
            ("SW","SW","SWIsometricView",-root3,-root3,root3,root6,root6,2*root6),
            ("SE","SE","SEIsometricView",root3,-root3,root3,-root6,root6,2*root6)
        };
        // 2 columns x 5 rows; leave 22% right and 12% bottom for title block.
        double[] xCenters={.20,.57};
        double[] yCenters={.86,.70,.54,.38,.22};
        double tileW=paperWidth*.34,tileH=paperHeight*.125;
        var cells=new List<Slot>();
        double minScale=1;
        for(int i=0;i<dirs.Length;i++)
        {
            var d=dirs[i];
            double rightX=d.Uy*d.Dz-d.Uz*d.Dy;
            double rightY=d.Uz*d.Dx-d.Ux*d.Dz;
            double rightZ=d.Ux*d.Dy-d.Uy*d.Dx;
            double basisDot=d.Dx*d.Ux+d.Dy*d.Uy+d.Dz*d.Uz;
            double rightNorm=Math.Sqrt(rightX*rightX+rightY*rightY+rightZ*rightZ);
            double upNorm=Math.Sqrt(d.Ux*d.Ux+d.Uy*d.Uy+d.Uz*d.Uz);
            if(Math.Abs(basisDot)>1e-7||Math.Abs(rightNorm-1)>1e-7||
                Math.Abs(upNorm-1)>1e-7)
                throw new InvalidOperationException("REV22 projection basis is not orthonormal: "+d.Name);
            var b=geometry.PaddedCube;
            double modelW=(Math.Abs(rightX)*b.SizeX+Math.Abs(rightY)*b.SizeY+
                Math.Abs(rightZ)*b.SizeZ)*geometry.SourceUnitToMm;
            double modelH=(Math.Abs(d.Ux)*b.SizeX+Math.Abs(d.Uy)*b.SizeY+
                Math.Abs(d.Uz)*b.SizeZ)*geometry.SourceUnitToMm;
            if(!double.IsFinite(modelW)||!double.IsFinite(modelH)||modelW<=0||modelH<=0)
                throw new InvalidOperationException("REV22 invalid projection dimensions.");
            minScale=Math.Max(minScale,Math.Max(modelW/(tileW*.8),modelH/(tileH*.8)));
            double cx=paperWidth*xCenters[i%2],cy=paperHeight*yCenters[i/2];
            cells.Add(new Slot(d.Name,d.EnumName,d.TypeName,d.Dx,d.Dy,d.Dz,
                d.Ux,d.Uy,d.Uz,cx-tileW/2,cy-tileH/2,tileW,tileH,cx,cy,modelW,modelH));
        }
        int[] scales={1,2,3,5,10,15,20,25,30,40,50,75,100,150,200,250,500,
            1000,2000,5000,10000};
        int denominator=scales.FirstOrDefault(v=>v>=minScale);
        if(denominator==0)
        {
            double n=Math.Ceiling(minScale/10000)*10000;
            if(n>int.MaxValue)throw new InvalidOperationException("REV22 sheet cannot fit ten views.");
            denominator=(int)n;
        }
        var result=new Plan(denominator,1.0/denominator,cells,
            "PROPOSED_ONLY: internal native 10-view registration and Update View unverified.");
        BuildAdjusted(result,paperWidth,paperHeight,cells[0].CenterX,cells[0].CenterY);
        return result;
    }

    public static AdjustedGroup BuildAdjusted(Plan candidate,double paperWidth,
        double paperHeight,double anchorX,double anchorY)
    {
        if(candidate==null||candidate.Views.Count!=10||candidate.Views[0].Name!="TOP")
            throw new ArgumentException("REV22 requires exactly ten view slots, TOP first.");
        if(!double.IsFinite(anchorX)||!double.IsFinite(anchorY)||!double.IsFinite(paperWidth)||
            !double.IsFinite(paperHeight)||paperWidth<=0||paperHeight<=0)
            throw new ArgumentException("REV22 paper dimensions and anchor must be finite.");
        if(anchorX<0||anchorX>paperWidth||anchorY<0||anchorY>paperHeight)
            throw new ArgumentOutOfRangeException(nameof(anchorX),"Anchor must be inside paper.");
        double topX=candidate.Views[0].CenterX,topY=candidate.Views[0].CenterY;
        double minX=double.NegativeInfinity,maxX=double.PositiveInfinity;
        double minY=double.NegativeInfinity,maxY=double.PositiveInfinity;
        foreach(var v in candidate.Views)
        {
            double w=Math.Max(5,v.ModelWmm*candidate.Scale*1.05);
            double h=Math.Max(5,v.ModelHmm*candidate.Scale*1.05);
            double dx=v.CenterX-topX,dy=v.CenterY-topY;
            minX=Math.Max(minX,w/2-dx+.1);
            maxX=Math.Min(maxX,paperWidth*.78-w/2-dx-.1);
            minY=Math.Max(minY,paperHeight*.12+h/2-dy+.1);
            maxY=Math.Min(maxY,paperHeight*.94-h/2-dy-.1);
        }
        if(!double.IsFinite(minX)||!double.IsFinite(maxX)||minX>maxX||
            !double.IsFinite(minY)||!double.IsFinite(maxY)||minY>maxY)
            throw new InvalidOperationException("REV22 ten views cannot fit reserved paper area.");
        double effectiveX=Math.Clamp(anchorX,minX,maxX);
        double effectiveY=Math.Clamp(anchorY,minY,maxY);
        var frames=candidate.Views.Select(v=>new Placement(v.Name,
            effectiveX+v.CenterX-topX,effectiveY+v.CenterY-topY,
            Math.Max(5,v.ModelWmm*candidate.Scale*1.05),
            Math.Max(5,v.ModelHmm*candidate.Scale*1.05))).ToList();
        for(int i=0;i<frames.Count;i++)
        {
            var a=frames[i];
            if(a.CenterX-a.PaperFrameWidth/2<-1e-7||
                a.CenterX+a.PaperFrameWidth/2>paperWidth*.78+1e-7||
                a.CenterY-a.PaperFrameHeight/2<paperHeight*.12-1e-7||
                a.CenterY+a.PaperFrameHeight/2>paperHeight*.94+1e-7)
                throw new InvalidOperationException("REV22 "+a.Name+" off-sheet after auto-fit.");
            for(int j=i+1;j<frames.Count;j++)
            {
                var b=frames[j];
                if(Math.Abs(a.CenterX-b.CenterX)<(a.PaperFrameWidth+b.PaperFrameWidth)/2&&
                    Math.Abs(a.CenterY-b.CenterY)<(a.PaperFrameHeight+b.PaperFrameHeight)/2)
                    throw new InvalidOperationException("REV22 "+a.Name+" overlaps "+b.Name);
            }
        }
        return new AdjustedGroup(anchorX,anchorY,effectiveX,effectiveY,
            minX,maxX,minY,maxY,
            Math.Abs(anchorX-effectiveX)>1e-8||Math.Abs(anchorY-effectiveY)>1e-8,
            frames);
    }
}
