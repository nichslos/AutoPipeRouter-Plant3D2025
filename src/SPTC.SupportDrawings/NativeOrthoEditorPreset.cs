using System;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.2 exact Plant 3D 2025 Edit View identifiers, recovered from the
/// Autodesk PnP3dOrthoDrawingsUI.dll implementation of UIUtils:
/// UIUtils::.cctor sets s_NWView="NW" etc; UIUtils.GetViewTypeFromName
/// compares those values and recognizes "Current" as a distinct type.
/// Do not substitute AutoCAD documentation view names like NWIsometric.
/// </summary>
public static class NativeOrthoEditorPreset
{
    public static string Resolve(string name,string previousTypeName) =>
        name switch
        {
            "NW" => "NW",
            "NE" => "NE",
            "SW" => "SW",
            "SE" => "SE",
            _ => previousTypeName
        };

    public static bool IsExactIsoPreset(string name,string storedType) =>
        NativeOrthoIsoViewType.IsIsometric(name) &&
        string.Equals(name,storedType,StringComparison.Ordinal);

    public static bool IsUnclassifiedCurrentView(string name,string storedType) =>
        NativeOrthoIsoViewType.IsIsometric(name) &&
        !IsExactIsoPreset(name,storedType);
}
