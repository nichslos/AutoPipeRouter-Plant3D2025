using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.13 pure paper-space title planner. Titles derive ONLY from the
/// ten native view names, never current camera or selected support geometry.
/// Captions sit BELOW their viewport, outside the OrthoCube/viewport itself.
/// Reserve the caption band before creating any native views; translate
/// the entire ten-view group together so no title enters the title-block area.
/// </summary>
public static class NativeOrthoViewTitlePlan
{
    public const double TextHeightMm=4.0;
    public const double GapBelowFrameMm=4.0;
    // MText glyph/line-height reserve, including a 2mm text descent buffer.
    public const double CaptionBandHeightMm=6.0;
    public const string TitleLayer="SPTC_VIEW_TITLES";

    private static readonly string[] ViewNames=
        {"TOP","BOTTOM","LEFT","RIGHT","FRONT","BACK","NW","NE","SW","SE"};

    public sealed record Frame(string Name,double X,double Y,double W,double H);
    public sealed record Title(string Name,string Text,double CenterX,
        double TopY,double Width,double Height);
    public sealed record ReservedGroup(double ShiftY,IReadOnlyList<Frame> Frames,
        IReadOnlyList<Title> Titles);

    public static string CaptionFor(string viewName) => viewName switch
    {
        "TOP"=>"TOP VIEW",
        "BOTTOM"=>"BOTTOM VIEW",
        "LEFT"=>"LEFT VIEW",
        "RIGHT"=>"RIGHT VIEW",
        "FRONT"=>"FRONT VIEW",
        "BACK"=>"BACK VIEW",
        "NW"=>"NW ISOMETRIC VIEW",
        "NE"=>"NE ISOMETRIC VIEW",
        "SW"=>"SW ISOMETRIC VIEW",
        "SE"=>"SE ISOMETRIC VIEW",
        _=>throw new ArgumentException(
            "REV22.13 unknown Ortho preset; cannot label Current View: "+viewName)
    };

    public static ReservedGroup ReserveBelow(
        IReadOnlyList<Frame> source,double paperWidth,double paperHeight)
    {
        ValidateFrames(source,paperWidth,paperHeight);
        double lowest=source.Min(f=>f.Y-f.H/2);
        double highest=source.Max(f=>f.Y+f.H/2);
        double bottomSafe=paperHeight*.12;
        double topSafe=paperHeight*.94;
        double needed=bottomSafe+GapBelowFrameMm+CaptionBandHeightMm-lowest;
        double shift=Math.Max(0,needed);
        if(highest+shift>topSafe+1e-6)
            throw new InvalidOperationException(
                "REV22.13 titles cannot fit with the ten viewports inside "+
                "the reserved title-block/paper area; no native views generated.");
        var translated=source.Select(f=>f with {Y=f.Y+shift}).ToArray();
        var titles=BuildTitles(translated,paperWidth,paperHeight);
        return new ReservedGroup(shift,translated,titles);
    }

    public static IReadOnlyList<Title> BuildTitles(
        IReadOnlyList<Frame> actual,double paperWidth,double paperHeight)
    {
        ValidateFrames(actual,paperWidth,paperHeight);
        var result=actual.Select(f=>new Title(
            f.Name,CaptionFor(f.Name),f.X,
            f.Y-f.H/2-GapBelowFrameMm,f.W,CaptionBandHeightMm)).ToArray();
        const double tol=1e-6;
        foreach(var t in result)
        {
            if(t.Width< (t.Name.Length==2?32:20))
                throw new InvalidOperationException(
                    "REV22.13 viewport too narrow for native title "+t.Name);
            if(t.CenterX-t.Width/2 < -tol ||
               t.CenterX+t.Width/2>paperWidth*.78+tol ||
               t.TopY-t.Height<paperHeight*.12-tol)
                throw new InvalidOperationException(
                    "REV22.13 "+t.Name+" title encroaches on sheet keepout.");
            foreach(var f in actual)
                if(Overlaps(t.CenterX-t.Width/2,t.TopY-t.Height,
                    t.CenterX+t.Width/2,t.TopY,
                    f.X-f.W/2,f.Y-f.H/2,f.X+f.W/2,f.Y+f.H/2))
                    throw new InvalidOperationException(
                        "REV22.13 "+t.Name+" title overlaps viewport "+f.Name);
        }
        for(int i=0;i<result.Length;i++)
            for(int j=i+1;j<result.Length;j++)
            {
                var a=result[i];var b=result[j];
                if(Overlaps(a.CenterX-a.Width/2,a.TopY-a.Height,
                    a.CenterX+a.Width/2,a.TopY,
                    b.CenterX-b.Width/2,b.TopY-b.Height,
                    b.CenterX+b.Width/2,b.TopY))
                    throw new InvalidOperationException(
                        "REV22.13 titles overlap: "+a.Name+" / "+b.Name);
            }
        return result;
    }

    private static bool Overlaps(double ax0,double ay0,double ax1,double ay1,
        double bx0,double by0,double bx1,double by1) =>
        ax0<bx1 && bx0<ax1 && ay0<by1 && by0<ay1;

    private static void ValidateFrames(
        IReadOnlyList<Frame>? frames,double paperW,double paperH)
    {
        if(frames==null||frames.Count!=10||!double.IsFinite(paperW)||
           !double.IsFinite(paperH)||paperW<=0||paperH<=0)
            throw new ArgumentException("REV22.13 requires ten valid paper viewports.");
        if(frames.Select(f=>f.Name).Distinct(StringComparer.Ordinal).Count()!=10 ||
           ViewNames.Except(frames.Select(f=>f.Name),StringComparer.Ordinal).Any())
            throw new ArgumentException(
                "REV22.13 titles must match all ten native preset labels exactly.");
        foreach(var f in frames)
            if(!double.IsFinite(f.X)||!double.IsFinite(f.Y)||
               !double.IsFinite(f.W)||!double.IsFinite(f.H)||
               f.W<=0||f.H<=0)
                throw new ArgumentException(
                    "REV22.13 nonfinite or empty native viewport: "+f.Name);
    }
}
