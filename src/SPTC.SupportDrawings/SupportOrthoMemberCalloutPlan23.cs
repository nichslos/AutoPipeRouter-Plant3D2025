using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.23 pure annotation layout:
/// * Every native steel member receives its own size tag and projected leader.
/// * HORIZONTAL members also receive a second TOS elevation line.
/// * Sorted-by-projected-height landing rows, unique outward dogleg lanes,
///   wider inter-row spacing, and all-view paper collision guards.
/// * Never changes actual Ortho view placement, camera, geometry, or title.
/// </summary>
public static class SupportOrthoMemberCalloutPlan23
{
    public const string Layer="SPTC_MEMBER_CALLOUTS";
    public const double TextHeightMm=1.75;
    public const double RowSpacingMm=1.8;
    public const double TextLineHeightMm=2.7;
    public const double LabelGapFromFrameMm=17.5;
    public const double LaneFirstMm=2.0;
    public const double LaneStepMm=0.7;

    public sealed record Member(string Handle,string Size,
        double X,double Y,double Z,bool IsHorizontal,string? TosText,string TosSource);
    public sealed record Callout(
        string View,string Handle,string Text,bool HasTos,
        double AnchorX,double AnchorY,double LaneX,
        double LabelX,double LabelTop,double LabelWidth,double LabelHeight);
    public sealed record Result(
        IReadOnlyList<Callout> Callouts,
        IReadOnlyList<string> Skipped,
        IReadOnlyList<string> ViewWithoutLabels,
        int HorizontalTosLines,int ApproximateTosLines);

    public static Result Build(
        IReadOnlyList<SupportOrthoMemberCalloutPlan17.Camera> cameras,
        IReadOnlyList<NativeOrthoViewTitlePlan.Title> titles,
        IReadOnlyList<Member> members,double paperW,double paperH)
    {
        if(cameras==null||cameras.Count!=10||members==null||
           titles==null||titles.Count!=10||
           !double.IsFinite(paperW)||!double.IsFinite(paperH)||
           paperW<=0||paperH<=0)
            throw new ArgumentException("REV22.23 requires ten views/titles.");
        if(cameras.Select(c=>c.Name).Distinct(StringComparer.Ordinal).Count()!=10||
           titles.Select(t=>t.Name).Distinct(StringComparer.Ordinal).Count()!=10)
            throw new ArgumentException("REV22.23 duplicate view or title.");
        foreach(var c in cameras)
            NativeOrthoViewTitlePlan.CaptionFor(c.Name);
        if(members.Select(m=>m.Handle).Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()!=members.Count)
            throw new ArgumentException("REV22.23 duplicate member handle.");
        var notes=new List<Callout>();
        var skips=new List<string>();
        var empty=new List<string>();
        foreach(var camera in cameras)
        {
            double viewRight=camera.X+camera.W/2;
            // Reserve 17.5mm for orthogonal leader dogleg lanes. Labels
            // remain outside every native viewport, not on top of its lines.
            double x=viewRight+LabelGapFromFrameMm;
            var rightNeighbor=cameras.Where(v=>v.X>camera.X+1e-5 &&
                    Math.Abs(v.Y-camera.Y)<Math.Max(v.H,camera.H))
                .OrderBy(v=>v.X).FirstOrDefault();
            double rightLimit=rightNeighbor!=null?
                rightNeighbor.X-rightNeighbor.W/2-5:paperW*.78-3;
            double width=Math.Min(114,rightLimit-x);
            if(width<54)
            {
                skips.Add(camera.Name+":NO_SAFE_TEXT_GUTTER");
                empty.Add(camera.Name);
                continue;
            }
            var projected=new List<(Member Member,double X,double Y,
                string Text,double Height,bool HasTos)>();
            foreach(var member in members)
            {
                if(string.IsNullOrWhiteSpace(member.Handle)||
                   string.IsNullOrWhiteSpace(member.Size)||
                   !double.IsFinite(member.X)||!double.IsFinite(member.Y)||
                   !double.IsFinite(member.Z))
                {
                    skips.Add(camera.Name+":"+member.Handle+":INVALID_MEMBER");
                    continue;
                }
                var p=SupportOrthoMemberCalloutPlan17.Project(
                    camera,member.X,member.Y,member.Z);
                if(p.X<camera.X-camera.W/2-1e-5||
                   p.X>camera.X+camera.W/2+1e-5||
                   p.Y<camera.Y-camera.H/2-1e-5||
                   p.Y>camera.Y+camera.H/2+1e-5)
                {
                    skips.Add(camera.Name+":"+member.Handle+":OUTSIDE_VIEW");
                    continue;
                }
                string label="["+member.Handle+"] "+member.Size;
                bool addTos=member.IsHorizontal;
                string? tos=null;
                if(addTos)
                {
                    tos=member.TosText==null?"TOS: N/A":
                        (member.TosSource=="GEOMETRIC_TOP_APPROX"?
                            "TOS ~ ":"TOS EL. ")+member.TosText;
                }
                // REV22.23 no speculative character-width heuristic.
                // Each native DBText line will be MEASURED with actual
                // AutoCAD style glyph extents before writing the leader.
                // DBText cannot wrap; the writer shrinks to fit or
                // explicitly skips if the real glyph width cannot fit.
                double height=addTos?TextLineHeightMm*2:
                    TextLineHeightMm;
                projected.Add((member,p.X,p.Y,
                    tos==null?label:label+"\n"+tos,height,addTos));
            }
            // Sorting both source and landing rows by projected Y is the
            // key no-cross ordering invariant for the external leader lanes.
            projected=projected.OrderByDescending(t=>t.Y)
                .ThenBy(t=>t.Member.Handle,StringComparer.OrdinalIgnoreCase)
                .ToList();
            double nextRowGap=cameras
                .Where(c=>Math.Abs(c.Y-camera.Y)>1e-5)
                .Select(c=>Math.Abs(c.Y-camera.Y))
                .DefaultIfEmpty(134.56).Min();
            double halfBand=Math.Min(60.0,nextRowGap/2-8);
            double topSafe=Math.Min(camera.Y+halfBand,paperH*.94-2);
            double bottomSafe=Math.Max(camera.Y-halfBand,paperH*.12+8);
            double total=projected.Sum(t=>t.Height)+
                Math.Max(0,projected.Count-1)*RowSpacingMm;
            if(total>topSafe-bottomSafe+1e-6)
            {
                // Keep all properly stored structural Size values intact,
                // never write overlapping paper-space labels.
                skips.Add(camera.Name+":NOT_ENOUGH_LABEL_HEIGHT");
                empty.Add(camera.Name);
                continue;
            }
            double rowTop=Math.Min(topSafe,
                Math.Max(bottomSafe+total,
                    camera.Y+total/2));
            int placed=0;
            foreach(var item in projected)
            {
                double labelTop=rowTop;
                rowTop-=item.Height+RowSpacingMm;
                double lane=viewRight+LaneFirstMm+
                    placed*LaneStepMm;
                if(lane>=x-1.5)
                {
                    skips.Add(camera.Name+":"+item.Member.Handle+
                        ":NO_SEPARATE_LEADER_LANE");
                    continue;
                }
                notes.Add(new Callout(camera.Name,item.Member.Handle,
                    item.Text,item.HasTos,item.X,item.Y,lane,
                    x,labelTop,width,item.Height));
                placed++;
            }
            if(placed==0)empty.Add(camera.Name);
        }
        foreach(var note in notes)
        {
            double lx=note.LabelX,rx=note.LabelX+note.LabelWidth;
            double bottom=note.LabelTop-note.LabelHeight;
            if(lx<0||rx>paperW*.78+1e-5||
                note.LabelTop>paperH*.94+1e-5||
                bottom<paperH*.12-1e-5)
                throw new InvalidOperationException(
                    "REV22.23 member label outside native drawing keepout.");
            foreach(var frame in cameras)
                if(Overlaps(lx,bottom,rx,note.LabelTop,
                    frame.X-frame.W/2,frame.Y-frame.H/2,
                    frame.X+frame.W/2,frame.Y+frame.H/2))
                    throw new InvalidOperationException(
                        "REV22.23 label "+note.Handle+
                        " intersects native viewport "+frame.Name);
            foreach(var title in titles)
                if(Overlaps(lx,bottom,rx,note.LabelTop,
                    title.CenterX-title.Width/2,
                    title.TopY-title.Height,
                    title.CenterX+title.Width/2,title.TopY))
                    throw new InvalidOperationException(
                        "REV22.23 label intersects native view title.");
        }
        for(int i=0;i<notes.Count;i++)
            for(int j=i+1;j<notes.Count;j++)
            {
                var a=notes[i];var b=notes[j];
                if(Overlaps(a.LabelX,a.LabelTop-a.LabelHeight,
                    a.LabelX+a.LabelWidth,a.LabelTop,
                    b.LabelX,b.LabelTop-b.LabelHeight,
                    b.LabelX+b.LabelWidth,b.LabelTop))
                    throw new InvalidOperationException(
                        "REV22.23 independent labels overlap: "+
                        a.View+"/"+a.Handle+" vs "+b.View+"/"+b.Handle);
            }
        return new Result(notes,skips,empty,
            notes.Count(n=>n.HasTos),
            notes.Count(n=>n.HasTos&&n.Text.Contains("TOS ~ ")));
    }

    private static bool Overlaps(double ax,double ay,double bx,double by,
        double cx,double cy,double dx,double dy) =>
        ax<dx&&cx<bx&&ay<dy&&cy<by;
}
