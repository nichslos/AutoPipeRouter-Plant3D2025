using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV 02 A3 annotation overlay. These are paper-space graphic envelope callouts,
/// not associative CAD dimensions or fabrication measurements.
/// </summary>
internal static class SupportSheetAnnotations
{
    internal static void Add(
        Transaction tx, BlockTableRecord paper, BoundingBox3 box, string unit,
        IReadOnlyDictionary<(string Type, string Layer), int> counts)
    {
        // All viewport centers / widths match the existing REV 01 A3 template.
        AnnotateView(tx, paper, 108, 209, 183, 125, "X", box.SizeX, "Y", box.SizeY, unit);
        AnnotateView(tx, paper, 312, 209, 183, 125, "Y", box.SizeY, "Z", box.SizeZ, unit);
        AnnotateView(tx, paper, 108, 91, 183, 82, "X", box.SizeX, "Z", box.SizeZ, unit);

        DrawObjectSchedule(tx, paper, counts);
        Text(tx, paper, "ENVELOPE ONLY - NON-ASSOCIATIVE", 14, 44, 2.1);
    }

    private static void AnnotateView(Transaction tx, BlockTableRecord paper,
        double cx, double cy, double w, double h, string horizontalAxis,
        double horizontalSize, string verticalAxis, double verticalSize, string units)
    {
        double left = cx - w * .36, right = cx + w * .36;
        double bottom = cy - h * .39, top = cy + h * .33;
        double dimX = cx - w * .435;
        // Dimension extension ticks are in paper units. These indicate overall
        // axis extents, NOT the real projected endpoints of individual members.
        Segment(tx, paper, left, bottom - 3, left, bottom + 3);
        Segment(tx, paper, right, bottom - 3, right, bottom + 3);
        Segment(tx, paper, left, bottom, right, bottom);
        Tick(tx, paper, left, bottom);
        Tick(tx, paper, right, bottom);
        Text(tx, paper, DimensionLabel(horizontalAxis, horizontalSize, units),
            cx - 27, bottom + 2.5, 2.2);

        Segment(tx, paper, dimX - 3, bottom + 9, dimX + 3, bottom + 9);
        Segment(tx, paper, dimX - 3, top - 5, dimX + 3, top - 5);
        Segment(tx, paper, dimX, bottom + 9, dimX, top - 5);
        Tick(tx, paper, dimX, bottom + 9);
        Tick(tx, paper, dimX, top - 5);
        RotatedText(tx, paper, DimensionLabel(verticalAxis, verticalSize, units),
            dimX + 3.5, cy - 22, 2.2, Math.PI / 2.0);
    }

    private static string DimensionLabel(string axis, double value, string units)
        => string.Format(CultureInfo.InvariantCulture, "{0} = {1:0.##} {2}", axis, value, ShortUnit(units));

    private static string ShortUnit(string units) => units switch
    {
        "Millimeters" => "mm",
        "Centimeters" => "cm",
        "Meters" => "m",
        "Inches" => "in",
        "Feet" => "ft",
        "Unitless" => "drawing units",
        _ => "DWG units"
    };

    private static void DrawObjectSchedule(Transaction tx, BlockTableRecord paper,
        IReadOnlyDictionary<(string Type, string Layer), int> counts)
    {
        Text(tx, paper, "OBJECT SCHEDULE (NOT FABRICATION BOM)", 269, 36, 2.3);
        Segment(tx, paper, 267, 32, 410, 32);
        Text(tx, paper, "TYPE / LAYER", 269, 27, 2.35);
        Text(tx, paper, "QTY", 394, 27, 2.35);
        Segment(tx, paper, 267, 24.5, 410, 24.5);
        Segment(tx, paper, 389, 9, 389, 32);
        var entries = counts.OrderBy(x => x.Key.Type).ThenBy(x => x.Key.Layer).ToList();
        int rows = Math.Min(entries.Count, 2);
        for (int i = 0; i < rows; i++)
        {
            var item = entries[i];
            string text = item.Key.Type + " / " + item.Key.Layer;
            if (text.Length > 40) text = text.Substring(0, 37) + "...";
            Text(tx, paper, text, 269, 20 - i * 7, 2.2);
            Text(tx, paper, item.Value.ToString(CultureInfo.InvariantCulture), 394, 20 - i * 7, 2.2);
        }
        if (entries.Count > rows)
            Text(tx, paper, "+" + (entries.Count - rows) + " more in CSV", 269, 9, 2.0);
    }

    private static void Tick(Transaction tx, BlockTableRecord paper, double x, double y)
        => Segment(tx, paper, x - 1.75, y - 1.75, x + 1.75, y + 1.75);

    private static void Segment(Transaction tx, BlockTableRecord paper, double x1, double y1, double x2, double y2)
    {
        var line = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
        paper.AppendEntity(line);
        tx.AddNewlyCreatedDBObject(line, true);
    }

    private static void Text(Transaction tx, BlockTableRecord paper,
        string value, double x, double y, double height)
    {
        RotatedText(tx, paper, value, x, y, height, 0);
    }

    private static void RotatedText(Transaction tx, BlockTableRecord paper,
        string value, double x, double y, double height, double radians)
    {
        var text = new DBText
        {
            TextString = value,
            Position = new Point3d(x, y, 0),
            Height = height,
            Rotation = radians
        };
        paper.AppendEntity(text);
        tx.AddNewlyCreatedDBObject(text, true);
    }
}
