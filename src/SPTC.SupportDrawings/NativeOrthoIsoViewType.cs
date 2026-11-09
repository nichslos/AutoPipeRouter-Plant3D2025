using System;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.1: canonical native isometric view identifiers. Plant 3D previously
/// received strings such as NWIsometricView, which aren't the known Autodesk
/// isometric scheme names (NWIsometric, NEIsometric, SWIsometric, SEIsometric).
/// This helper does not mutate the validated six standard view identifiers.
/// Native GenerateOrthos remains unsupported; verify in a copied project.
/// </summary>
public static class NativeOrthoIsoViewType
{
    public static bool IsIsometric(string viewName) =>
        viewName is "NW" or "NE" or "SW" or "SE";

    public static string Resolve(string viewName,string previousTypeName) =>
        viewName switch
        {
            "NW" => "NWIsometric",
            "NE" => "NEIsometric",
            "SW" => "SWIsometric",
            "SE" => "SEIsometric",
            _ => previousTypeName
        };

    public static bool MatchesCamera(string viewName,double x,double y,double z,
        double expectedX,double expectedY,double expectedZ,double tolerance=1e-5)
    {
        if(!IsIsometric(viewName))return true;
        double len=Math.Sqrt(x*x+y*y+z*z);
        double targetLen=Math.Sqrt(expectedX*expectedX+expectedY*expectedY+expectedZ*expectedZ);
        if(!double.IsFinite(len)||!double.IsFinite(targetLen)||
            len<1e-12||targetLen<1e-12)return false;
        return Math.Abs(x/len-expectedX/targetLen)<=tolerance &&
            Math.Abs(y/len-expectedY/targetLen)<=tolerance &&
            Math.Abs(z/len-expectedZ/targetLen)<=tolerance;
    }

    public static bool HasExpectedQuadrant(string name,double x,double y,double z) =>
        name switch
        {
            "NW" => x<0&&y>0&&z>0,
            "NE" => x>0&&y>0&&z>0,
            "SW" => x<0&&y<0&&z>0,
            "SE" => x>0&&y<0&&z>0,
            _ => true
        };
}
