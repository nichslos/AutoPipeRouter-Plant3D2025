using System;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.22: Encode separate trusted annotation paragraphs as native MText.
/// IMPORTANT: SupportOrthoMemberMetadata.SafeMText() removes control chars,
/// including newlines. Splitting FIRST prevents adjacent Size/TOS text from
/// being concatenated as "fl-7.5TOS". Each paragraph remains independently
/// sanitized, then joined with AutoCAD's genuine \P paragraph separator.
/// </summary>
public static class SupportOrthoMemberMText22
{
    public static string Encode(string? annotation)
    {
        if(string.IsNullOrEmpty(annotation))return "";
        string normalized=annotation.Replace("\r\n","\n").Replace('\r','\n');
        string[] paragraphs=normalized.Split('\n');
        return string.Join("\\P",
            paragraphs.Select(SupportOrthoMemberMetadata.SafeMText));
    }
}
