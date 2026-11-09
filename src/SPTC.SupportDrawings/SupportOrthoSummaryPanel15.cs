using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.15: single concise source-grounded support panel in the unused
/// gap between the two native Ortho view columns. Replaces ten identical
/// verbose callouts; never overlays native viewports or view title labels.
/// Pure geometry: no drawing writes.
/// </summary>
public static class SupportOrthoSummaryPanel15
{
    public const string Layer="SPTC_SUPPORT_ANNOTATIONS";
    public const double BodyTextHeightMm=2.8;
    public const double HeaderTextHeightMm=3.6;
    public const double PanelMarginMm=6;
    public const string Header="STRUCTURAL SUPPORT";

    public sealed record Panel(double X,double Top,double Width,double Height,
        string IdentityLine,string SizeLine,int BlockCount,int ObjectCount,
        bool HasMemberSizes);

    public static Panel Build(
        IReadOnlyList<NativeOrthoViewTitlePlan.Frame> frames,
        IReadOnlyList<NativeOrthoViewTitlePlan.Title> titles,
        SupportOrthoSelection15.Summary selection,
        double paperWidth,double paperHeight)
    {
        if(frames==null||titles==null||selection==null||
           frames.Count!=10||titles.Count!=10||
           !double.IsFinite(paperWidth)||!double.IsFinite(paperHeight)||
           paperWidth<=0||paperHeight<=0)
            throw new ArgumentException("REV22.15 requires ten native frames and titles.");
        if(frames.Select(f=>f.Name).Distinct(StringComparer.Ordinal).Count()!=10)
            throw new ArgumentException("REV22.15 duplicated native views.");
        foreach(var frame in frames)
            NativeOrthoViewTitlePlan.CaptionFor(frame.Name);
        var columns=frames.GroupBy(f=>Math.Round(f.X,3))
            .OrderBy(g=>g.Key).ToArray();
        if(columns.Length!=2||columns[0].Count()!=5||columns[1].Count()!=5)
            throw new InvalidOperationException(
                "REV22.15 needs the original two-column by five-row layout.");
        double leftEdge=columns[0].Max(f=>f.X+f.W/2);
        double rightEdge=columns[1].Min(f=>f.X-f.W/2);
        double x=leftEdge+PanelMarginMm;
        double width=rightEdge-leftEdge-2*PanelMarginMm;
        if(width<85)
            throw new InvalidOperationException(
                "REV22.15 no safe gap for support summary; keep Ortho views intact.");
        string identity=selection.IdentityKind=="BLOCK"?
            "BLOCK: "+selection.Identity:
            "ASSEMBLY: "+selection.Identity;
        string size=selection.HasSizes?
            "MEMBER: "+selection.SizeText:
            "MEMBER: SIZE NOT RECORDED";
        if(identity.Length>500||size.Length>500)
            throw new InvalidOperationException(
                "REV22.15 source metadata is too long for sheet annotation.");
        int textCols=Math.Max(12,(int)Math.Floor((width-9)/(BodyTextHeightMm*.75)));
        int rows=Rows(identity,textCols)+Rows(size,textCols);
        double height=15.5+rows*BodyTextHeightMm*1.55;
        var topFrame=frames.Single(f=>f.Name=="TOP");
        double top=Math.Min(topFrame.Y+topFrame.H/2,paperHeight*.92);
        var panel=new Panel(x,top,width,height,identity,size,
            selection.BlockCount,selection.SelectedCount,selection.HasSizes);
        if(panel.X<0||panel.X+panel.Width>paperWidth*.78+1e-6||
           panel.Top>paperHeight*.94+1e-6||
           panel.Top-panel.Height<paperHeight*.12-1e-6)
            throw new InvalidOperationException(
                "REV22.15 annotation panel does not fit the native sheet keepout.");
        foreach(var f in frames)
            if(Overlaps(panel.X,panel.Top-panel.Height,
                panel.X+panel.Width,panel.Top,
                f.X-f.W/2,f.Y-f.H/2,f.X+f.W/2,f.Y+f.H/2))
                throw new InvalidOperationException(
                    "REV22.15 support panel overlaps viewport "+f.Name);
        foreach(var t in titles)
            if(Overlaps(panel.X,panel.Top-panel.Height,
                panel.X+panel.Width,panel.Top,
                t.CenterX-t.Width/2,t.TopY-t.Height,
                t.CenterX+t.Width/2,t.TopY))
                throw new InvalidOperationException(
                    "REV22.15 support panel overlaps view title "+t.Name);
        return panel;
    }

    private static int Rows(string s,int width) =>
        Math.Max(1,(int)Math.Ceiling(s.Length/(double)width));

    private static bool Overlaps(double ax0,double ay0,double ax1,double ay1,
        double bx0,double by0,double bx1,double by1) =>
        ax0<bx1&&bx0<ax1&&ay0<by1&&by0<ay1;
}
