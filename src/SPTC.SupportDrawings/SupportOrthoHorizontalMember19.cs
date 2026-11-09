using System;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.19 correction: TOS applies ONLY to horizontal structural members.
/// Native start/end axis is authoritative. A strongly horizontal 3D bounding
/// box is an explicitly logged fallback when no endpoints are accessible.
/// Vertical and inclined members never receive a TOS line.
/// </summary>
public static class SupportOrthoHorizontalMember19
{
    public const double MinimumMemberLengthMm=150.0;
    public const double MaximumVerticalFraction=0.15;
    public const double HorizontalBoundsRatio=1.8;

    public sealed record Observation(
        bool IsHorizontal,string DirectionSource,string? TopOfSteelText,
        string TopOfSteelSource);

    public static bool IsHorizontalAxis(
        double ax,double ay,double az,double bx,double by,double bz)
    {
        double dx=bx-ax,dy=by-ay,dz=bz-az;
        double horizontal=Math.Sqrt(dx*dx+dy*dy);
        double length=Math.Sqrt(horizontal*horizontal+dz*dz);
        return double.IsFinite(length)&&length>=MinimumMemberLengthMm&&
            horizontal>=MinimumMemberLengthMm&&
            Math.Abs(dz)/length<=MaximumVerticalFraction;
    }

    public static bool IsStronglyHorizontalBounds(
        double xLength,double yLength,double zLength)
    {
        double span=Math.Max(xLength,yLength);
        return double.IsFinite(xLength)&&double.IsFinite(yLength)&&
            double.IsFinite(zLength)&&span>=MinimumMemberLengthMm&&
            zLength>=0&&span>HorizontalBoundsRatio*zLength;
    }

    public static Observation Evaluate(
        double xLength,double yLength,double zLength,
        (double X,double Y,double Z)? start,
        (double X,double Y,double Z)? end,
        string? propertyTos,double geometricTopMm)
    {
        bool explicitAxis=start.HasValue&&end.HasValue;
        bool horizontal=explicitAxis?
            IsHorizontalAxis(start!.Value.X,start.Value.Y,start.Value.Z,
                end!.Value.X,end.Value.Y,end.Value.Z):
            IsStronglyHorizontalBounds(xLength,yLength,zLength);
        string source=explicitAxis?"EXPLICIT_START_END":"STRONG_HORIZONTAL_BOUNDS";
        if(!horizontal)
            return new Observation(false,source,null,"NOT_HORIZONTAL");

        if(SupportOrthoVerticalMember18.TryParseTos(propertyTos,out double exact))
            return new Observation(true,source,
                SupportOrthoVerticalMember18.FormatElevation(exact),
                "PLANT_TOP_OF_STEEL_PROPERTY");

        if(double.IsFinite(geometricTopMm))
            return new Observation(true,source,
                SupportOrthoVerticalMember18.FormatElevation(geometricTopMm),
                "GEOMETRIC_TOP_APPROX");

        return new Observation(true,source,null,"TOS_UNAVAILABLE");
    }

    public static string TosLabel(Observation data) =>
        !data.IsHorizontal?"":data.TopOfSteelText==null?"TOS: N/A":
        (data.TopOfSteelSource=="GEOMETRIC_TOP_APPROX"?
            "TOS~ ":"TOS EL. ")+data.TopOfSteelText;
}
