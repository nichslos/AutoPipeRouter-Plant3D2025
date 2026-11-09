using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.15: source-grounded summary for BOTH block references and separate
/// support objects. Never calls separate solids a block reference and never
/// infers steel profile size from an object's bounding-box dimensions.
/// </summary>
public static class SupportOrthoSelection15
{
    public sealed record Summary(
        string Identity, string SizeText, int SelectedCount, int BlockCount,
        int StructuralMemberCount, IReadOnlyList<string> BlockNames,
        IReadOnlyList<string> MemberSizes, IReadOnlyList<string> Evidence,
        IReadOnlyList<string> Warnings)
    {
        public bool HasSizes => MemberSizes.Count>0;
        public string IdentityKind => BlockCount>0?"BLOCK":"ASSEMBLY";
    }

    public static Summary Build(
        int selectedCount,int blockCount,int structuralMemberCount,
        IEnumerable<string> blockNames,IEnumerable<string> sectionSizes,
        IEnumerable<string>? evidence=null,IEnumerable<string>? warnings=null)
    {
        if(selectedCount<1||blockCount<0||blockCount>selectedCount||
           structuralMemberCount<0||structuralMemberCount>selectedCount)
            throw new ArgumentOutOfRangeException(nameof(selectedCount));
        string[] names=(blockNames??Array.Empty<string>())
            .Select(SupportOrthoMemberMetadata.Clean)
            .Where(s=>!string.IsNullOrWhiteSpace(s)&&
                !s.Equals("NOT A BLOCK REFERENCE",StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray();
        string[] sizes=(sectionSizes??Array.Empty<string>())
            .Select(SupportOrthoMemberMetadata.Clean)
            .Where(s=>!string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray();

        string identity;
        if(blockCount==1&&names.Length==1)
            identity=names[0];
        else if(blockCount>0)
            identity=names.Length==0?blockCount+" BLOCK(S)":
                names.Length==1?names[0]+" ("+blockCount+" INSERTS)":
                names[0]+" +"+(names.Length-1)+" MORE";
        else
            identity=selectedCount+" OBJECT"+(selectedCount==1?"":"S");

        return new Summary(identity,
            sizes.Length==0?"NOT RECORDED":string.Join(", ",sizes),
            selectedCount,blockCount,structuralMemberCount,
            names,sizes,
            (evidence??Array.Empty<string>()).ToArray(),
            (warnings??Array.Empty<string>()).ToArray());
    }
}
