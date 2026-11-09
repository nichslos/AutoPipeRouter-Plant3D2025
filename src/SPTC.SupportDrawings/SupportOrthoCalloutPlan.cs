using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.14 paper-space-only annotations. Source BlockReference names and
/// metadata-evidenced structural section sizes are shown beside each
/// generated native viewport, connected by a short leader to its frame.
/// Never creates dimensions or falsely associates one section with a
/// specific nested solid when only block-level metadata is available.
/// </summary>
public static class SupportOrthoCalloutPlan
{
    public const string AnnotationLayer="SPTC_SUPPORT_ANNOTATIONS";
    public const double TextHeightMm=2.8;
    public const double FrameGapMm=7.0;
    public const double MaxTextBoxWidthMm=112.0;

    public sealed record Callout(string ViewName,
        string BlockLine,string SizeLine,
        double X,double TopY,double Width,double Height,
        double LeaderFromX,double LeaderFromY,
        double LeaderToX,double LeaderToY);

    public static IReadOnlyList<Callout> Build(
        IReadOnlyList<NativeOrthoViewTitlePlan.Frame> frames,
        SupportOrthoMemberMetadata.Summary data,
        double paperW,double paperH)
    {
        if(frames==null||data==null||frames.Count!=10||
           !double.IsFinite(paperW)||!double.IsFinite(paperH)||
           paperW<=0||paperH<=0)
            throw new ArgumentException("REV22.14 requires ten view frames and metadata.");
        if(frames.Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=10)
            throw new ArgumentException("REV22.14 duplicate native view preset.");
        foreach(var f in frames)
            NativeOrthoViewTitlePlan.CaptionFor(f.Name);

        string block="BLOCK: "+data.BlockName;
        string size="MEMBER SIZE: "+SupportOrthoMemberMetadata.SizeLabel(data);
        if(block.Length+size.Length>600)
            throw new InvalidOperationException(
                "REV22.14 source metadata is too long for compact paper notes.");

        var plan=new List<Callout>(frames.Count);
        foreach(var f in frames)
        {
            if(!double.IsFinite(f.X)||!double.IsFinite(f.Y)||
               !double.IsFinite(f.W)||!double.IsFinite(f.H)||
               f.W<=0||f.H<=0)
                throw new ArgumentException("REV22.14 invalid viewport frame.");
            double x=f.X+f.W/2+FrameGapMm;
            double width=Math.Min(MaxTextBoxWidthMm,paperW*.78-2-x);
            if(width<64)
                throw new InvalidOperationException(
                    "REV22.14 no horizontal annotation room next to "+
                    f.Name+"; keep the working REV22.13 layout.");
            double top=f.Y+f.H/2-2.0;
            // Conservative MText width estimate; fail closed on dense metadata.
            int charsPerLine=Math.Max(8,(int)Math.Floor(width/(TextHeightMm*.75)));
            int lines=WrappedLineCount(block,charsPerLine)+
                WrappedLineCount(size,charsPerLine);
            double height=lines*TextHeightMm*1.65+2.5;
            if(height>Math.Min(60.0,f.H+15))
                throw new InvalidOperationException(
                    "REV22.14 source metadata does not fit "+f.Name+" callout.");
            var item=new Callout(f.Name,block,size,x,top,width,height,
                f.X+f.W/2-1,f.Y+Math.Min(f.H*.12,7),
                x-1,top-TextHeightMm*1.1);
            if(x+width>paperW*.78+1e-6||
               top>paperH*.94+1e-6||
               top-height<paperH*.12-1e-6)
                throw new InvalidOperationException(
                    "REV22.14 "+f.Name+" metadata enters Ortho sheet keepout.");
            plan.Add(item);
        }
        for(int i=0;i<plan.Count;i++)
        {
            var a=plan[i];
            foreach(var f in frames)
                if(Overlaps(a.X,a.TopY-a.Height,a.X+a.Width,a.TopY,
                    f.X-f.W/2,f.Y-f.H/2,f.X+f.W/2,f.Y+f.H/2))
                    throw new InvalidOperationException(
                        "REV22.14 "+a.ViewName+" text box overlaps "+f.Name+" viewport.");
            for(int j=i+1;j<plan.Count;j++)
            {
                var b=plan[j];
                if(Overlaps(a.X,a.TopY-a.Height,a.X+a.Width,a.TopY,
                    b.X,b.TopY-b.Height,b.X+b.Width,b.TopY))
                    throw new InvalidOperationException(
                        "REV22.14 support annotation boxes overlap.");
            }
        }
        return plan;
    }

    private static int WrappedLineCount(string text,int maxChars) =>
        Math.Max(1,(int)Math.Ceiling(text.Length/(double)maxChars));

    private static bool Overlaps(double ax0,double ay0,double ax1,double ay1,
        double bx0,double by0,double bx1,double by1) =>
        ax0<bx1&&bx0<ax1&&ay0<by1&&by0<ay1;
}
