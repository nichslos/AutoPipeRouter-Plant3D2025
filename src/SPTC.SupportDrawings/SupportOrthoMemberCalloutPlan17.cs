using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.17 pure per-STRUCTURE MEMBER paper-space projection and callout planner.
/// Uses actual saved native viewport DCS pose and exact project Size values.
/// Callouts are one per SOURCE member per view; never substitute a BOM legend.
/// A member is annotated only if its projected source midpoint is actually
/// inside the saved native viewport. Does not claim occlusion visibility.
/// </summary>
public static class SupportOrthoMemberCalloutPlan17
{
    public const string Layer="SPTC_MEMBER_CALLOUTS";
    public const double TextHeightMm=2.1;
    public const double RowPitchMm=3.9;
    public const double FrameToLabelMm=8.0;
    public const double TextBoxMinimumMm=66.0;

    public sealed record Member(string Handle,string Size,double X,double Y,double Z);
    public sealed record Camera(string Name,double X,double Y,double W,double H,
        double DirX,double DirY,double DirZ,
        double ViewCenterX,double ViewCenterY,
        double TwistRadians,double Scale);
    public sealed record Callout(
        string View,string Handle,string Text,double Ax,double Ay,
        double ElbowX,double ElbowY,double TextX,double TextTop,
        double TextWidth,double TextHeight);
    public sealed record Result(IReadOnlyList<Callout> Callouts,
        IReadOnlyList<string> Skipped,IReadOnlyList<string> ViewsWithoutLabels);

    public static Result Build(
        IReadOnlyList<Camera> cameras,IReadOnlyList<Member> members,
        double paperW,double paperH)
    {
        if(cameras==null||cameras.Count!=10||members==null||
           !double.IsFinite(paperW)||!double.IsFinite(paperH)||
           paperW<=0||paperH<=0)
            throw new ArgumentException("REV22.17 needs ten native viewports.");
        if(cameras.Select(c=>c.Name).Distinct(StringComparer.Ordinal).Count()!=10)
            throw new ArgumentException("REV22.17 view names must be unique.");
        foreach(var c in cameras)NativeOrthoViewTitlePlan.CaptionFor(c.Name);
        if(members.Select(m=>m.Handle).Distinct(
            StringComparer.OrdinalIgnoreCase).Count()!=members.Count)
            throw new ArgumentException("REV22.17 duplicate source member handle.");
        var callouts=new List<Callout>();
        var skipped=new List<string>();
        var empty=new List<string>();
        foreach(var camera in cameras)
        {
            ValidateCamera(camera);
            var neighbor=cameras
                .Where(c=>c.X>camera.X+1e-4 &&
                    Math.Abs(c.Y-camera.Y)<Math.Max(c.H,camera.H))
                .OrderBy(c=>c.X).FirstOrDefault();
            double rightBoundary=neighbor!=null?
                neighbor.X-neighbor.W/2-6:paperW*.78-3;
            double textX=camera.X+camera.W/2+FrameToLabelMm;
            double width=Math.Min(112,rightBoundary-textX);
            if(width<TextBoxMinimumMm)
            {
                skipped.Add(camera.Name+":NO_TEXT_GUTTER");
                empty.Add(camera.Name);
                continue;
            }
            double upperY=camera.Y+camera.H/2-2;
            double minY=Math.Max(paperH*.12+10,
                camera.Y-Math.Max(camera.H/2+13,69));
            var located=new List<(Member Member,double X,double Y)>();
            foreach(var member in members)
            {
                if(!ValidMember(member))
                {
                    skipped.Add(camera.Name+":"+member.Handle+":INVALID_SOURCE");
                    continue;
                }
                var p=Project(camera,member.X,member.Y,member.Z);
                if(p.X<camera.X-camera.W/2-1e-5||
                    p.X>camera.X+camera.W/2+1e-5||
                    p.Y<camera.Y-camera.H/2-1e-5||
                    p.Y>camera.Y+camera.H/2+1e-5)
                {
                    skipped.Add(camera.Name+":"+member.Handle+":OUTSIDE_VIEW");
                    continue;
                }
                located.Add((member,p.X,p.Y));
            }
            located=located.OrderByDescending(m=>m.Y)
                .ThenBy(m=>m.Member.Handle,StringComparer.OrdinalIgnoreCase).ToList();
            int capacity=(int)Math.Floor((upperY-minY-TextHeightMm)/RowPitchMm)+1;
            capacity=Math.Max(0,capacity);
            for(int i=0;i<located.Count;i++)
            {
                var item=located[i];
                if(i>=capacity)
                {
                    skipped.Add(camera.Name+":"+item.Member.Handle+
                        ":NO_NONOVERLAPPING_ROW");
                    continue;
                }
                double labelTop=upperY-i*RowPitchMm;
                if(labelTop-TextHeightMm<minY-1e-5)
                {
                    skipped.Add(camera.Name+":"+item.Member.Handle+":BELOW_SAFE_ROW");
                    continue;
                }
                string label="["+item.Member.Handle+"] "+item.Member.Size;
                // One line per source member; no silent MText wrapping.
                double estimatedWidth=label.Length*TextHeightMm*.70;
                if(estimatedWidth>width-1.0)
                {
                    skipped.Add(camera.Name+":"+item.Member.Handle+
                        ":SIZE_TEXT_TOO_LONG");
                    continue;
                }
                // End at the label, but keep the elbow outside the frame.
                double elbowX=camera.X+camera.W/2+2.0;
                callouts.Add(new Callout(camera.Name,item.Member.Handle,
                    label,item.X,item.Y,elbowX,labelTop-TextHeightMm*.52,
                    textX,labelTop,width,TextHeightMm));
            }
            if(!callouts.Any(c=>c.View==camera.Name))empty.Add(camera.Name);
        }
        // Guard paper keepouts and all other view frames.
        foreach(var note in callouts)
        {
            if(note.TextX+note.TextWidth>paperW*.78+1e-5||
               note.TextTop-note.TextHeight<paperH*.12-1e-5||
               note.TextTop>paperH*.94+1e-5)
                throw new InvalidOperationException(
                    "REV22.17 member callout violates native sheet keepout.");
            foreach(var f in cameras)
            {
                if(Overlaps(note.TextX,note.TextTop-note.TextHeight,
                    note.TextX+note.TextWidth,note.TextTop,
                    f.X-f.W/2,f.Y-f.H/2,f.X+f.W/2,f.Y+f.H/2))
                    throw new InvalidOperationException(
                        "REV22.17 member label "+note.Handle+
                        " overlaps native view frame "+f.Name);
            }
        }
        return new Result(callouts,skipped,empty);
    }

    private static bool Overlaps(double ax,double ay,double bx,double by,
        double cx,double cy,double dx,double dy) =>
        ax<dx&&cx<bx&&ay<dy&&cy<by;

    private static bool ValidMember(Member m) =>
        !string.IsNullOrWhiteSpace(m.Handle)&&
        !string.IsNullOrWhiteSpace(m.Size)&&
        double.IsFinite(m.X)&&double.IsFinite(m.Y)&&double.IsFinite(m.Z);

    private static void ValidateCamera(Camera c)
    {
        if(!double.IsFinite(c.X)||!double.IsFinite(c.Y)||
           !double.IsFinite(c.W)||!double.IsFinite(c.H)||
           !double.IsFinite(c.ViewCenterX)||!double.IsFinite(c.ViewCenterY)||
           !double.IsFinite(c.TwistRadians)||!double.IsFinite(c.Scale)||
           c.W<=0||c.H<=0||c.Scale<=0)
            throw new ArgumentException("REV22.17 invalid camera for "+c.Name);
        double norm=Math.Sqrt(c.DirX*c.DirX+c.DirY*c.DirY+c.DirZ*c.DirZ);
        if(!double.IsFinite(norm)||norm<1e-9)
            throw new ArgumentException("REV22.17 invalid view direction for "+c.Name);
    }

    /// <summary>
    /// Matches AutoCAD's arbitrary-axis algorithm (WorldToPlane(viewDirection)),
    /// with WCS origin DCS convention evidenced by the native viewport ViewCenter
    /// values (e.g. TOP center 7956,3484, FRONT center 7956,613).
    /// This returns the PAPER point in millimeters; no screen-space approximations.
    /// </summary>
    public static (double X,double Y) Project(Camera c,
        double worldX,double worldY,double worldZ)
    {
        ValidateCamera(c);
        double len=Math.Sqrt(c.DirX*c.DirX+c.DirY*c.DirY+c.DirZ*c.DirZ);
        double nx=c.DirX/len,ny=c.DirY/len,nz=c.DirZ/len;
        // Autodesk arbitrary axis: if normal approximately parallel Z,
        // use world Y x normal; otherwise use world Z x normal.
        double xx,xy,xz;
        if(Math.Abs(nx)<1.0/64&&Math.Abs(ny)<1.0/64)
        {xx=nz;xy=0;xz=-nx;}
        else{xx=-ny;xy=nx;xz=0;}
        double xl=Math.Sqrt(xx*xx+xy*xy+xz*xz);
        xx/=xl;xy/=xl;xz/=xl;
        double yx=ny*xz-nz*xy,yy=nz*xx-nx*xz,yz=nx*xy-ny*xx;
        double u=worldX*xx+worldY*xy+worldZ*xz;
        double v=worldX*yx+worldY*yy+worldZ*yz;
        double cos=Math.Cos(c.TwistRadians),sin=Math.Sin(c.TwistRadians);
        double rx=u*cos+v*sin,ry=-u*sin+v*cos;
        return(c.X+(rx-c.ViewCenterX)*c.Scale,
            c.Y+(ry-c.ViewCenterY)*c.Scale);
    }
}
