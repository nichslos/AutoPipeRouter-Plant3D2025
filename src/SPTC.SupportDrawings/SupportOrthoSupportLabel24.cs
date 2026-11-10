using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.24: parse Plant 3D's PnP Support property values, never treating
/// a pipe's nominal Size as the steel Structure Member profile.
/// </summary>
public static class SupportOrthoSupportLabel24
{
    public static string Normalize(string? value) =>
        new string((value??"").Where(char.IsLetterOrDigit).ToArray())
            .ToUpperInvariant();

    public static bool IsSupportClass(string? value)
    {
        string normalized=Normalize(value);
        return normalized=="SUPPORT"||normalized=="PIPESUPPORT"||
            normalized=="PIPINGSUPPORT"||normalized=="ACPPSUPPORT"||
            normalized=="ACPPSUPP";
    }

    public static string? Get(IReadOnlyDictionary<string,string> properties,
        params string[] keys)
    {
        foreach(string key in keys)
        {
            string requested=Normalize(key);
            foreach(var item in properties)
                if(Normalize(item.Key)==requested &&
                    !string.IsNullOrWhiteSpace(item.Value) &&
                    !IsUnset(item.Value))
                    return item.Value.Trim();
        }
        return null;
    }

    public static string? ChooseDescription(
        IReadOnlyDictionary<string,string> properties)
    {
        // In the user's actual Plant 3D Properties palette:
        // Long Description (Size): Clamped Stanchion 16"
        // Short Description: Clamped Stanchion
        // Size: 400. The Size alone is not structural-steel profile.
        string? description=Get(properties,
            "LongDescriptionSize","Long Description (Size)",
            "LongDescription","ShortDescription","SupportType");
        string? size=Get(properties,"Size");
        if(!string.IsNullOrWhiteSpace(description))
            return SupportOrthoMemberMetadata.Clean(description);
        if(!string.IsNullOrWhiteSpace(size))
            return "Support (Size "+SupportOrthoMemberMetadata.Clean(size)+")";
        return null;
    }

    private static bool IsUnset(string text)
    {
        string s=text.Trim();
        return s=="?"||s=="-"||s.Equals("UNSET",StringComparison.OrdinalIgnoreCase)||
            s.Equals("UNKNOWN",StringComparison.OrdinalIgnoreCase)||
            s.Equals("N/A",StringComparison.OrdinalIgnoreCase);
    }
}
