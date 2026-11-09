using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.8 native axial Edit View preset selection.
/// A saved TypeName like "TopView" is not necessarily recognized by Plant 3D's
/// UIUtils.GetViewTypeFromName. Probe the installed 2025 parser and accept
/// only an exact enum match, never silently fall back to Current View.
/// Pure helper: no Autodesk or DWG dependencies.
/// </summary>
public static class NativeOrthoAxialEditorPreset
{
    private static readonly string[] AxialNames =
        {"TOP","BOTTOM","LEFT","RIGHT","FRONT","BACK"};

    public static bool IsAxial(string name) =>
        AxialNames.Contains(name,StringComparer.Ordinal);

    public static IReadOnlyList<string> Candidates(string name,string oldTypeName)
    {
        if(!IsAxial(name))throw new ArgumentException("Not an axial Ortho view: "+name);
        string nativeTitle=name switch
        {
            "TOP"=>"Top",
            "BOTTOM"=>"Bottom",
            "LEFT"=>"Left",
            "RIGHT"=>"Right",
            "FRONT"=>"Front",
            "BACK"=>"Back",
            _=>throw new ArgumentException("Unknown view: "+name)
        };
        // Prefer the native ViewType enum name itself; accept the previous
        // type only if Autodesk's parser truly resolves it.
        return new[]{nativeTitle,oldTypeName,name,
            nativeTitle+" View",name+" VIEW"}
            .Where(s=>!string.IsNullOrWhiteSpace(s)&&
                !s.Equals("Current",StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string Resolve(string name,string oldTypeName,int expectedEnum,
        Func<string,int?> nativeParser)
    {
        if(nativeParser==null)throw new ArgumentNullException(nameof(nativeParser));
        foreach(string candidate in Candidates(name,oldTypeName))
        {
            int? parsed=nativeParser(candidate);
            if(parsed.HasValue&&parsed.Value==expectedEnum)return candidate;
        }
        throw new InvalidOperationException("REV22.8 "+name+
            " has no recognized native Edit View preset string. "+
            "Refusing Current View fallback (candidates: "+
            string.Join(", ",Candidates(name,oldTypeName))+").");
    }
}
