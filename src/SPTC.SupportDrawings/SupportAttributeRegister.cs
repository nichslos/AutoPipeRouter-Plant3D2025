using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV 02: Only records material properties explicitly present as attributes
/// on the selected support block inserts. This is NOT a derived BOM or takeoff.
/// </summary>
internal static class SupportAttributeRegister
{
    private sealed record Row(string Handle, string BlockName,
        string PartNo, string Description, string Material, string Length, string Qty);

    internal static void WriteIfAvailable(Database source, ObjectId[] selected, string outputDwg)
    {
        var results = new List<Row>();
        using (var tx = source.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selected.Distinct())
            {
                if (tx.GetObject(id, OpenMode.ForRead, false) is not BlockReference block)
                    continue;

                var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId attrId in block.AttributeCollection)
                {
                    if (tx.GetObject(attrId, OpenMode.ForRead, false) is AttributeReference attr)
                        attrs[attr.Tag.Trim()] = attr.TextString ?? string.Empty;
                }
                if (attrs.Count == 0) continue;
                string partNo = First(attrs, "PART_NO", "PARTNO", "ITEM_NO", "ITEM");
                string description = First(attrs, "DESCRIPTION", "DESC");
                string material = First(attrs, "MATERIAL", "MAT", "MATERIAL_GRADE", "GRADE");
                string length = First(attrs, "LENGTH", "CUT_LENGTH");
                string qty = First(attrs, "QTY", "QUANTITY");

                if (string.IsNullOrWhiteSpace(partNo) && string.IsNullOrWhiteSpace(description)
                    && string.IsNullOrWhiteSpace(material) && string.IsNullOrWhiteSpace(length))
                    continue;

                // Do not infer a fabricated material quantity from a support-assembly count.
                results.Add(new Row(id.Handle.ToString(), block.Name,
                    partNo, description, material, length, qty));
            }
        }
        if (results.Count == 0) return;

        string csv = Path.Combine(Path.GetDirectoryName(outputDwg)!,
            Path.GetFileNameWithoutExtension(outputDwg) + "_attribute_material_register.csv");
        using var output = new StreamWriter(csv, false);
        output.WriteLine("SelectedBlockHandle,BlockName,PartNo,Description,Material,Length,Quantity");
        foreach (Row item in results)
            output.WriteLine(string.Join(",", new[]
            {
                Escape(item.Handle), Escape(item.BlockName), Escape(item.PartNo),
                Escape(item.Description), Escape(item.Material), Escape(item.Length),
                Escape(item.Qty)
            }));
    }

    private static string First(Dictionary<string, string> attributes, params string[] aliases)
    {
        foreach (string name in aliases)
            if (attributes.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return "";
    }

    private static string Escape(string value)
    {
        // Avoid spreadsheet formula interpretation of CAD-supplied values.
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
