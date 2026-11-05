using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV 03: Read-only evidence report of selected CAD entities and first-level
/// block-definition contents. Does NOT assume Plant PnP identifiers or BOM data.
/// </summary>
internal static class SupportEntityAudit
{
    internal sealed record Summary(int SelectedEntities, int DefinitionEntities,
        int SupportCandidates, int MissingExtents);

    private sealed record Entry(
        string Scope, string ParentHandle, string Handle, string ManagedType,
        string RuntimeClass, string DxfName, string Layer, string BlockName,
        string Classification, string GeometryScope, BoundingBox3? Extents,
        string Attributes);

    internal static Summary WriteReport(Database database, ObjectId[] ids, string outputCsv)
    {
        var records = new List<Entry>();
        var visitedIds = new HashSet<ObjectId>();

        using (var tx = database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in ids)
            {
                if (!visitedIds.Add(id) || id.IsNull || !id.IsValid || id.IsErased) continue;
                if (tx.GetObject(id, OpenMode.ForRead, false) is not Entity entity) continue;

                records.Add(Describe(id, entity, "SELECTED", "", "WCS_SELECTED",
                    IncludeExtents(entity), ReadAttributes(tx, entity)));
                if (entity is not BlockReference block) continue;

                // One level only. Definition children are not individually transformed
                // into this instance's WCS; no misleading per-member dimensions.
                if (tx.GetObject(block.BlockTableRecord, OpenMode.ForRead, false)
                    is not BlockTableRecord definition) continue;
                foreach (ObjectId memberId in definition)
                {
                    if (memberId.IsNull || memberId.IsErased ||
                        tx.GetObject(memberId, OpenMode.ForRead, false) is not Entity member) continue;
                    records.Add(Describe(memberId, member, "BLOCK_DEFINITION_MEMBER",
                        id.Handle.ToString(), "BLOCK_DEFINITION_LOCAL",
                        null, ""));
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputCsv))!);
        using (var writer = new StreamWriter(outputCsv, false))
        {
            writer.WriteLine(
                "Scope,ParentHandle,Handle,ManagedType,RXClass,DxfName,Layer,BlockName," +
                "Classification,GeometryScope,ExtentsAvailable,MinX,MinY,MinZ," +
                "MaxX,MaxY,MaxZ,Attributes,DrawingUnits");
            foreach (Entry e in records)
            {
                string[] cells =
                {
                    e.Scope, e.ParentHandle, e.Handle, e.ManagedType, e.RuntimeClass,
                    e.DxfName, e.Layer, e.BlockName, e.Classification, e.GeometryScope,
                    e.Extents.HasValue ? "TRUE" : "FALSE",
                    Number(e.Extents?.MinX), Number(e.Extents?.MinY),
                    Number(e.Extents?.MinZ), Number(e.Extents?.MaxX),
                    Number(e.Extents?.MaxY), Number(e.Extents?.MaxZ),
                    e.Attributes, database.Insunits.ToString()
                };
                writer.WriteLine(string.Join(",", cells.Select(Escape)));
            }
        }

        int candidateCount = records.Count(e => e.Classification.EndsWith("_CANDIDATE",
            StringComparison.Ordinal));
        return new Summary(records.Count(e => e.Scope == "SELECTED"),
            records.Count(e => e.Scope == "BLOCK_DEFINITION_MEMBER"),
            candidateCount,
            records.Count(e => e.Scope == "SELECTED" && e.Extents is null));
    }

    private static Entry Describe(ObjectId id, Entity entity, string scope,
        string parentHandle, string geometryScope, BoundingBox3? extents,
        string attributes)
    {
        string managed = entity.GetType().FullName ?? entity.GetType().Name;
        // RX class is evidence of actual underlying ObjectARX DB entity type,
        // even when the managed runtime exposes only a proxy or generic wrapper.
        string rxClass = id.ObjectClass?.Name ?? "";
        string dxf = id.ObjectClass?.DxfName ?? "";
        string layer = entity.Layer ?? "";
        string blockName = entity is BlockReference block ? block.Name : "";
        return new Entry(scope, parentHandle, id.Handle.ToString(), managed, rxClass,
            dxf, layer, blockName,
            SupportEntityClassifier.Classify(managed, rxClass, dxf, layer, blockName),
            geometryScope, extents, attributes);
    }

    private static BoundingBox3? IncludeExtents(Entity entity)
    {
        try
        {
            Extents3d e = entity.GeometricExtents;
            return new BoundingBox3(e.MinPoint.X, e.MinPoint.Y, e.MinPoint.Z,
                e.MaxPoint.X, e.MaxPoint.Y, e.MaxPoint.Z);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
    }

    private static string ReadAttributes(Transaction tx, Entity entity)
    {
        if (entity is not BlockReference block) return "";
        var attributes = new List<string>();
        foreach (ObjectId id in block.AttributeCollection)
            if (tx.GetObject(id, OpenMode.ForRead, false) is AttributeReference attribute)
            {
                string tag = attribute.Tag?.Replace("|", "/") ?? "";
                string value = (attribute.TextString ?? "").Replace("|", "/");
                attributes.Add(tag + "=" + value);
            }
        return string.Join(" | ", attributes);
    }

    private static string Number(double? value) =>
        value.HasValue ? value.Value.ToString("0.######", CultureInfo.InvariantCulture) : "";

    private static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
