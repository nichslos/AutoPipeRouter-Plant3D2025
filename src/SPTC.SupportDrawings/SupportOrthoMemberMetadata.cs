using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.14: Only annotate size values actually present in CAD metadata
/// or explicit structural designations in source member/block names.
/// The 3D bounding box is NEVER interpreted as a channel/angle section.
/// </summary>
public static class SupportOrthoMemberMetadata
{
    public static bool IsSizeProperty(string? key)
    {
        var k=(key??"").Trim().ToUpperInvariant()
            .Replace("-","_").Replace(" ","_");
        return k is "SIZE" or "MEMBER_SIZE" or "SECTION" or
            "SECTION_SIZE" or "SECTION_NAME" or "SECTION_TYPE" or
            "PROFILE" or "PROFILE_SIZE" or "STEEL_SIZE" or
            "SHAPE" or "SHAPE_SIZE" or "CHANNEL_SIZE" or
            "ANGLE_SIZE" or "STRUCTURAL_SIZE" or "STRUCTURE_SIZE" or
            "PLATE_SIZE" or "MEMBER_PROFILE" or "SECTION_DESIGNATION";
    }

    // Require a structural prefix to avoid interpreting arbitrary
    // dimensions, dates, model numbers, or support coordinates as sizes.
    private static readonly Regex ProfileName=new(
        @"(?<![A-Z0-9])(?:CHANNEL|ANGLE|CH|C|L|H|I|U|SHS|RHS|PIPE)\s*[-_ ]?\s*\d{2,4}(?:\s*[X×*]\s*\d+(?:\.\d+)?){0,3}(?![A-Z0-9])",
        RegexOptions.Compiled|RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> SectionsFromName(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return Array.Empty<string>();
        return ProfileName.Matches(text).Cast<Match>()
            .Where(m=>m.Success).Select(m=>Clean(m.Value))
            .Where(v=>v.Length>0).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? SectionFromName(string? text) =>
        SectionsFromName(text).FirstOrDefault();

    public static string? SectionFromAttribute(string? name,string? value)
    {
        if(!IsSizeProperty(name))return null;
        string v=Clean(value);
        if(v.Length==0||v.Length>100||IsNonvalue(v))
            return null;
        // A typed attribute may legitimately be numeric without a C/L prefix;
        // the CAD author explicitly declared it as section-size metadata.
        return v;
    }

    public static string Clean(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return "";
        return new string(value.Where(c=>!char.IsControl(c)&&
            c!='{'&&c!='}'&&c!='\\'&&c!=';').ToArray()).Trim();
    }

    private static bool IsNonvalue(string s) =>
        s.Equals("UNSET",StringComparison.OrdinalIgnoreCase)||
        s.Equals("N/A",StringComparison.OrdinalIgnoreCase)||
        s.Equals("NONE",StringComparison.OrdinalIgnoreCase)||
        s.Equals("UNKNOWN",StringComparison.OrdinalIgnoreCase)||
        s=="-"||s=="?";
    
    public sealed record Summary(
        string BlockName,IReadOnlyList<string> MemberSizes,
        IReadOnlyList<string> Evidence,IReadOnlyList<string> Warnings);

    public static Summary Summarize(
        IEnumerable<string> blockNames,
        IEnumerable<string> sectionNames,
        IEnumerable<string>? provenance=null,
        IEnumerable<string>? warnings=null)
    {
        var blocks=blockNames.Where(s=>!string.IsNullOrWhiteSpace(s))
            .Select(Clean).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray();
        var sections=sectionNames.Where(s=>!string.IsNullOrWhiteSpace(s))
            .Select(Clean).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray();
        return new Summary(
            blocks.Length==0?"NOT A BLOCK REFERENCE":string.Join(", ",blocks),
            sections,
            (provenance??Array.Empty<string>()).ToArray(),
            (warnings??Array.Empty<string>()).ToArray());
    }

    public static string SizeLabel(Summary summary) =>
        summary.MemberSizes.Count==0?"NOT AVAILABLE (NO SECTION METADATA)":
        string.Join(", ",summary.MemberSizes);

    // MText formatting escapes must never be sourced from CAD names.
    public static string SafeMText(string? value)
    {
        string clean=Clean(value);
        return clean.Replace("%","%%");
    }
}
