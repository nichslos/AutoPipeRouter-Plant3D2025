using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.18: pure vertical-member detection and Top of Steel annotation.
/// A true source start/end vector is authoritative. If Plant 3D exposes
/// no endpoints, a strongly vertical 3D bounding box is a logged fallback.
/// TOS is exact only when a project/entity TopOfSteel property is available.
/// </summary>
public static class SupportOrthoVerticalMember18
{
    public const double MinimumHeightMm=150.0;
    public const double VerticalAxisRatio=0.98;
    public const double VerticalBoundsRatio=1.8;

    public sealed record Observation(
        bool IsVertical,string DirectionSource,string? TopOfSteelText,
        string TopOfSteelSource);

    public static bool IsVerticalAxis(double ax,double ay,double az,
        double bx,double by,double bz)
    {
        double dx=bx-ax,dy=by-ay,dz=bz-az;
        double len=Math.Sqrt(dx*dx+dy*dy+dz*dz);
        return double.IsFinite(len) && len>=MinimumHeightMm &&
            Math.Abs(dz)/len>=VerticalAxisRatio;
    }

    public static bool IsStronglyVerticalBounds(
        double xLength,double yLength,double zLength) =>
        double.IsFinite(xLength)&&double.IsFinite(yLength)&&
        double.IsFinite(zLength)&&
        zLength>=MinimumHeightMm &&
        zLength>VerticalBoundsRatio*Math.Max(xLength,yLength);

    public static Observation Evaluate(
        double xLength,double yLength,double zLength,
        (double X,double Y,double Z)? start,
        (double X,double Y,double Z)? end,
        string? propertyTos,double geometricTopMm)
    {
        bool hasAxis=start.HasValue&&end.HasValue;
        bool vertical=hasAxis?
            IsVerticalAxis(start!.Value.X,start.Value.Y,start.Value.Z,
                end!.Value.X,end.Value.Y,end.Value.Z):
            IsStronglyVerticalBounds(xLength,yLength,zLength);
        string source=hasAxis?"EXPLICIT_START_END":"STRONG_VERTICAL_BOUNDS";
        if(!vertical)return new Observation(false,source,null,"NOT_VERTICAL");
        if(TryParseTos(propertyTos,out double exact))
            return new Observation(true,source,
                FormatElevation(exact),"PLANT_TOP_OF_STEEL_PROPERTY");
        if(double.IsFinite(geometricTopMm))
            return new Observation(true,source,
                FormatElevation(geometricTopMm),"GEOMETRIC_TOP_APPROX");
        return new Observation(true,source,null,"TOS_UNAVAILABLE");
    }

    public static bool TryParseTos(string? text,out double elevation)
    {
        elevation=0;
        if(string.IsNullOrWhiteSpace(text))return false;
        var s=text.Trim().Replace("mm","",
            StringComparison.OrdinalIgnoreCase).Trim();
        if(!double.TryParse(s,NumberStyles.Float,
            CultureInfo.InvariantCulture,out elevation)&&
            !double.TryParse(s,NumberStyles.Float,
                CultureInfo.CurrentCulture,out elevation))
            return false;
        return double.IsFinite(elevation);
    }

    public static string FormatElevation(double elevationMm)
    {
        string s=Math.Abs(elevationMm)
            .ToString("0.####",CultureInfo.InvariantCulture);
        return (elevationMm>=0?"+":"-")+s+" mm";
    }

    public static string TosLabel(Observation data) =>
        !data.IsVertical?"":data.TopOfSteelText==null?"TOS: N/A":
        (data.TopOfSteelSource=="GEOMETRIC_TOP_APPROX"?
            "TOS~ ":"TOS EL. ")+data.TopOfSteelText;
}
