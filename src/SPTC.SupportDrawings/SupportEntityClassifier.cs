using System;

namespace SPTC.SupportDrawings;

/// <summary>
/// Classifies CAD object descriptions for support audit purposes.
/// These are hints, not proof of native Plant 3D support relationships.
/// No Plant SDK binaries or native PnP class references are required.
/// </summary>
public static class SupportEntityClassifier
{
    public static string Classify(string? managedType, string? runtimeClass,
        string? dxfName, string? layer, string? blockName)
    {
        string type = (managedType ?? "") + " " + (runtimeClass ?? "") + " " + (dxfName ?? "");
        if (Has(type, "AcPpDb3dStructureMember") || Has(type, "3dStructureMember") ||
            Has(type, "StructureMember"))
            return "PLANT_STRUCTURE_MEMBER_CANDIDATE";
        if (Has(type, "PipeSupport") || Has(type, "PnP3dSupport") ||
            Has(type, "AcPpDb3dSupport"))
            return "PLANT_SUPPORT_CANDIDATE";
        if (Has(type, "Support"))
            return "SUPPORT_TYPE_CANDIDATE";
        if (Has(layer, "SUPPORT") || Has(blockName, "SUPPORT") ||
            Has(layer, "HANGER") || Has(blockName, "HANGER"))
            return "SUPPORT_NAMING_CANDIDATE";
        if (Has(type, "BlockReference") || Has(dxfName, "INSERT"))
            return "GENERIC_BLOCK";
        return "UNCLASSIFIED";
    }

    private static bool Has(string? text, string fragment) =>
        (text ?? "").Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
