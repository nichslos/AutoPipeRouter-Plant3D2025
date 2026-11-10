using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.27: PIPE only. Use the real Line Number Tag and Bottom of Pipe
/// property, NEVER COP Elevation or Top of Pipe. Missing properties are
/// identified as unavailable, not fabricated from pipe geometry.
/// </summary>
public static class SupportOrthoPipeLabel27
{
    public sealed record Result(string LineTag,string BopText,
        string LineSource,string BopSource);

    public static Result Evaluate(IReadOnlyDictionary<string,string> properties)
    {
        if(properties==null)throw new ArgumentNullException(nameof(properties));
        string? tag=SupportOrthoSupportLabel24.Get(properties,
            "LineNumberTag","Line Number Tag","LineNumber",
            "Line Number","LineTag","PipingLineNumberTag");
        string? bop=SupportOrthoSupportLabel24.Get(properties,
            "BottomOfPipe","Bottom of Pipe","BOP",
            "BottomOfPipeElevation","Bottom of Pipe Elevation");
        // The Properties palette may report "Unassigned" as an actual
        // Line Number Tag. This must not be invented when the field is absent.
        string lineSource=tag!=null?"EXPLICIT_LINE_NUMBER_TAG":
            "LINE_NUMBER_TAG_NOT_AVAILABLE";
        string bopSource="BOP_NOT_AVAILABLE";
        string bopText="BOP: N/A";
        if(TryElevation(bop,out double elevation))
        {
            bopSource="EXPLICIT_BOTTOM_OF_PIPE";
            bopText="BOP EL. "+SupportOrthoVerticalMember18.FormatElevation(elevation);
        }
        return new Result(tag??"Not available",bopText,lineSource,bopSource);
    }

    public static bool TryElevation(string? value,out double elevation)
    {
        elevation=0;
        if(string.IsNullOrWhiteSpace(value))return false;
        string s=value.Trim().Replace("mm","",
            StringComparison.OrdinalIgnoreCase).Trim();
        if(!double.TryParse(s,NumberStyles.Float,
            CultureInfo.InvariantCulture,out elevation) &&
           !double.TryParse(s,NumberStyles.Float,
            CultureInfo.CurrentCulture,out elevation))
            return false;
        return double.IsFinite(elevation);
    }

    public static bool IsNativePipeType(string? name)
    {
        var n=SupportOrthoSupportLabel24.Normalize(name);
        return n=="PIPE"||n=="ACPPPIPE"||n=="ACPPDB3DPIPE"||
            n=="PNP3DPIPE"||n=="PIPESEGMENT";
    }
}
