using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV05: extracts line/face edges, samples general CAD curves, and attempts
/// nondestructive entity decomposition before writing real 2D AutoCAD
/// line entities in three orthographic model-space views. Native piping solids,
/// curves, ACIS surfaces and other unsupported entities are skipped and reported.
/// This is NOT a hidden-line drawing or fabrication deliverable.
/// </summary>
internal sealed class Support2DGeometryException : InvalidOperationException
{
    internal Support2DGeometryException(string message) : base(message) { }
}

internal static class Support2DExporter
{
    private sealed record Warning(string Path, string Type, string Reason, bool Approximate = false);

    internal sealed record Result(int ExtractedSegments, int Lines2D,
        int SkippedCollapsed, int Unsupported, string Report, int Approximated = 0);

    internal static Result Export(Database source, ObjectId[] selected, string outputDwg)
    {
        if (selected.Length == 0) throw new InvalidOperationException("No support objects were selected.");

        var geometry = new List<SketchSegment3>();
        var warnings = new List<Warning>();

        using (var transaction = source.TransactionManager.StartTransaction())
        {
            var traversal = new Extraction(transaction, geometry, warnings);
            foreach (ObjectId id in selected.Distinct()) traversal.Visit(id, Array.Empty<Matrix3d>(), "", 0);
        }
        if (geometry.Count == 0)
        {
            string types = string.Join(", ", warnings.Select(x => x.Type).Distinct().Take(6));
            string failedReport = ReportPath(outputDwg);
            WriteReport(failedReport, source.Insunits.ToString(), 0, 0, 0, warnings);
            throw new Support2DGeometryException("No usable 2D edge geometry could be extracted from this selection. " +
                "Native Plant 3D piping/solids may require orthographic projection. Unsupported: " + types +
                ". Diagnostic CSV: " + failedReport);
        }

        BoundingBox3 bounds = Support2DMath.FromSegments(geometry);
        double gap = Support2DMath.Gap(bounds);
        double textSize = Math.Max(gap * 0.12, 1.0);
        int generated = 0, collapsed = 0;
        string[] labels = { "TOP (X-Y)", "FRONT (X-Z)", "SIDE (Y-Z)" };
        OrthoPlane[] views = { OrthoPlane.Top, OrthoPlane.Front, OrthoPlane.Side };

        using (var output = new Database(true, false))
        {
            output.Insunits = source.Insunits;
            using (Transaction transaction = output.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)transaction.GetObject(output.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)transaction.GetObject(
                    table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                for (int index = 0; index < views.Length; index++)
                {
                    OrthoPlane view = views[index];
                    string layer = "SPTC_2D_" + view.ToString().ToUpperInvariant();
                    CreateLayer(output, transaction, layer, (short)(index + 1));

                    foreach (SketchSegment3 edge in geometry)
                    {
                        SketchPoint2 a = Support2DMath.Project(edge.Start, bounds, view, gap);
                        SketchPoint2 b = Support2DMath.Project(edge.End, bounds, view, gap);
                        if (Support2DMath.DistanceSquared(a, b) <= 1e-16)
                        {
                            collapsed++;
                            continue;
                        }

                        var line = new Line(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0))
                        { Layer = layer };
                        model.AppendEntity(line);
                        transaction.AddNewlyCreatedDBObject(line, true);
                        generated++;
                    }

                    SketchPoint2 viewOrigin = Support2DMath.Project(
                        new SketchPoint3(bounds.MinX, bounds.MinY, bounds.MinZ),
                        bounds, view, gap);
                    AddText(transaction, model, labels[index],
                        viewOrigin.X, viewOrigin.Y - textSize * 4, textSize * 1.1);

                    var (horizontal, vertical) = view switch
                    {
                        OrthoPlane.Top => (bounds.SizeX, bounds.SizeY),
                        OrthoPlane.Front => (bounds.SizeX, bounds.SizeZ),
                        _ => (bounds.SizeY, bounds.SizeZ)
                    };
                    AddText(transaction, model,
                        string.Format(CultureInfo.InvariantCulture,
                            "Projected linework envelope: {0:0.###} x {1:0.###} DWG units",
                            horizontal, vertical),
                        viewOrigin.X, viewOrigin.Y - textSize * 6, textSize * 0.75);
                }

                SketchPoint2 titleOrigin = Support2DMath.Project(
                    new SketchPoint3(bounds.MinX, bounds.MinY, bounds.MinZ),
                    bounds, OrthoPlane.Top, gap);
                AddText(transaction, model, "SPTC REV05 - ORTHOGRAPHIC LINEWORK / APPROX CURVES",
                    titleOrigin.X, titleOrigin.Y + bounds.SizeY + textSize * 7, textSize * 1.7);
                AddText(transaction, model, "NO HIDDEN-LINE REMOVAL / CURVES APPROXIMATE / NOT FOR FABRICATION",
                    titleOrigin.X, titleOrigin.Y + bounds.SizeY + textSize * 4, textSize);
                transaction.Commit();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputDwg))!);
            output.SaveAs(outputDwg, DwgVersion.Current);
        }

        string report = ReportPath(outputDwg);
        WriteReport(report, source.Insunits.ToString(), geometry.Count, generated, collapsed, warnings);
        return new Result(geometry.Count, generated, collapsed, warnings.Count(x => !x.Approximate), report, warnings.Count(x => x.Approximate));
    }

    private static string ReportPath(string outputDwg) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputDwg))!,
            Path.GetFileNameWithoutExtension(outputDwg) + "_projection_report.csv");

    private static void CreateLayer(Database database, Transaction transaction, string name, short color)
    {
        var layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (layers.Has(name)) return;
        layers.UpgradeOpen();
        var layer = new LayerTableRecord { Name = name, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, color) };
        layers.Add(layer);
        transaction.AddNewlyCreatedDBObject(layer, true);
    }

    private static void AddText(Transaction tx, BlockTableRecord model, string value,
        double x, double y, double height)
    {
        var label = new DBText
        {
            TextString = value,
            Position = new Point3d(x, y, 0),
            Height = height
        };
        model.AppendEntity(label);
        tx.AddNewlyCreatedDBObject(label, true);
    }

    private static void WriteReport(string path, string units, int segments, int lines,
        int collapsed, List<Warning> warnings)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Category,EntityPath,EntityType,Description,Count,SourceDrawingUnits");
        writer.WriteLine(Row("SUMMARY", "", "", "3D extracted line segments", segments, units));
        writer.WriteLine(Row("SUMMARY", "", "", "Created 2D LINE entities (all views)", lines, units));
        writer.WriteLine(Row("SUMMARY", "", "", "Projection-collapsed segments", collapsed, units));
        writer.WriteLine(Row("SUMMARY", "", "", "Unsupported or skipped items", warnings.Count(x => !x.Approximate), units));
        writer.WriteLine(Row("SUMMARY", "", "", "Approximate curve sampling events", warnings.Count(x => x.Approximate), units));
        foreach (Warning warning in warnings)
            writer.WriteLine(Row(warning.Approximate ? "APPROXIMATED" : "UNSUPPORTED", warning.Path, warning.Type, warning.Reason, 1, units));
    }

    private static string Row(string cat, string path, string type,
        string description, int count, string units) => string.Join(",", new[]
    {
        Csv(cat), Csv(path), Csv(type), Csv(description),
        count.ToString(CultureInfo.InvariantCulture), Csv(units)
    });

    private static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed class Extraction
    {
        private readonly Transaction tx;
        private readonly List<SketchSegment3> segments;
        private readonly List<Warning> warnings;
        private readonly HashSet<ObjectId> visitedDefinitions = new();

        public Extraction(Transaction transaction, List<SketchSegment3> segments,
            List<Warning> warnings)
        {
            tx = transaction;
            this.segments = segments;
            this.warnings = warnings;
        }

        public void Visit(ObjectId id, IReadOnlyList<Matrix3d> transforms, string prefix, int depth)
        {
            if (id.IsNull || id.IsErased) return;
            Entity? entity;
            try { entity = tx.GetObject(id, OpenMode.ForRead, false) as Entity; }
            catch (System.Exception ex)
            {
                warnings.Add(new Warning(prefix + id.Handle, "UNREADABLE", ex.GetType().Name));
                return;
            }
            if (entity == null) return;

            string label = prefix + id.Handle.ToString();
            try
            {
                if (entity is BlockReference block)
                {
                    if (depth >= 8)
                    {
                        warnings.Add(new Warning(label, "BlockReference", "Maximum block nesting depth reached"));
                        return;
                    }
                    ObjectId definitionId = block.BlockTableRecord;
                    if (visitedDefinitions.Contains(definitionId))
                    {
                        warnings.Add(new Warning(label, "BlockReference", "Recursive block definition detected"));
                        return;
                    }
                    var definition = tx.GetObject(definitionId, OpenMode.ForRead, false) as BlockTableRecord;
                    if (definition == null || definition.IsFromExternalReference)
                    {
                        warnings.Add(new Warning(label, "BlockReference", "Unresolved or externally referenced block"));
                        return;
                    }
                    visitedDefinitions.Add(definitionId);
                    var next = new List<Matrix3d>(transforms.Count + 1) { block.BlockTransform };
                    next.AddRange(transforms);
                    foreach (ObjectId member in definition)
                        Visit(member, next, label + "/", depth + 1);
                    visitedDefinitions.Remove(definitionId);
                    return;
                }

                if (entity is Line line)
                {
                    Add(line.StartPoint, line.EndPoint, transforms);
                    return;
                }
                if (entity is Polyline pline)
                {
                    for (int i = 0; i < pline.NumberOfVertices - 1; i++)
                        AddPolylineLeg(pline, i, i + 1, transforms, label);
                    if (pline.Closed && pline.NumberOfVertices > 1)
                        AddPolylineLeg(pline, pline.NumberOfVertices - 1, 0, transforms, label);
                    return;
                }
                if (entity is Polyline3d poly3d)
                {
                    if (poly3d.PolyType != Poly3dType.SimplePoly)
                    {
                        warnings.Add(new Warning(label, entity.GetType().Name, "Fitted/smoothed polyline not converted"));
                        return;
                    }
                    var points = new List<Point3d>();
                    foreach (ObjectId vid in poly3d)
                        if (tx.GetObject(vid, OpenMode.ForRead, false) is PolylineVertex3d vertex)
                            points.Add(vertex.Position);
                    for (int i = 1; i < points.Count; i++) Add(points[i - 1], points[i], transforms);
                    if (poly3d.Closed && points.Count > 2)
                        Add(points[^1], points[0], transforms);
                    return;
                }
                if (entity is Face face)
                {
                    var vertices = new[]
                    {
                        face.GetVertexAt(0), face.GetVertexAt(1),
                        face.GetVertexAt(2), face.GetVertexAt(3)
                    };
                    int n = vertices[2].DistanceTo(vertices[3]) < 1e-9 ? 3 : 4;
                    for (int i = 0; i < n; i++)
                        Add(vertices[i], vertices[(i + 1) % n], transforms);
                    return;
                }

                // General AutoCAD Curve support. An ImpCurve may or may not implement
                // Curve; we verify at runtime rather than assuming its class inheritance.
                if (entity is Curve curve && SampleCurve(curve, transforms, label))
                    return;

                // Some Plant 3D/custom entities can expose ordinary AutoCAD geometry
                // by Explode(). The source entity is never altered. Unsupported solids
                // are NOT projected from their bounding boxes.
                if (TryDecompose(entity, transforms, label, 0))
                    return;

                warnings.Add(new Warning(label, entity.GetType().Name,
                    "No supported linework after Curve and safe Explode attempts; native Ortho/solid projection needed"));
            }
            catch (System.Exception ex)
            {
                warnings.Add(new Warning(label, entity.GetType().Name,
                    "Geometry unavailable: " + ex.GetType().Name));
            }
        }

        private bool SampleCurve(Curve curve, IReadOnlyList<Matrix3d> transforms, string label)
        {
            // Parameter-spaced 96 segments are explicitly APPROXIMATE, not
            // manufacturing tolerances. Do not silently treat this as exact geometry.
            try
            {
                double start = curve.StartParam;
                double end = curve.EndParam;
                if (!double.IsFinite(start) || !double.IsFinite(end) || end <= start ||
                    end - start > 1e9) return false;
                const int samples = 96;
                var points = new List<Point3d>(samples + 1);
                for (int i = 0; i <= samples; i++)
                {
                    double t = start + (end - start) * i / samples;
                    Point3d point = curve.GetPointAtParameter(t);
                    if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                        !double.IsFinite(point.Z)) return false;
                    points.Add(point);
                }
                int before = segments.Count;
                for (int i = 1; i < points.Count; i++)
                    Add(points[i - 1], points[i], transforms);
                if (segments.Count == before) return false;
                warnings.Add(new Warning(label, curve.GetType().Name,
                    "APPROXIMATE: curve sampled at 96 parameter intervals; check shape and engineering tolerance", true));
                return true;
            }
            catch (System.Exception ex)
            {
                warnings.Add(new Warning(label, curve.GetType().Name,
                    "Curve evaluation unavailable: " + ex.GetType().Name));
                return false;
            }
        }

        private bool TryDecompose(Entity original, IReadOnlyList<Matrix3d> transforms,
            string label, int depth)
        {
            if (depth >= 4) return false;
            DBObjectCollection exploded = new DBObjectCollection();
            try
            {
                original.Explode(exploded);
                if (exploded.Count == 0) return false;
                int before = segments.Count;
                int index = 0;
                foreach (DBObject obj in exploded)
                {
                    if (obj is Entity piece)
                    {
                        string childLabel = label + "/EXP" + (++index).ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                        if (piece is Line line)
                        {
                            Add(line.StartPoint, line.EndPoint, transforms);
                        }
                        else if (piece is Polyline pline)
                        {
                            for (int i = 0; i < pline.NumberOfVertices - 1; i++)
                                AddPolylineLeg(pline, i, i + 1, transforms, childLabel);
                            if (pline.Closed && pline.NumberOfVertices > 1)
                                AddPolylineLeg(pline, pline.NumberOfVertices - 1, 0, transforms, childLabel);
                        }
                        else if (piece is Face face)
                        {
                            var vertices = new[]
                            {
                                face.GetVertexAt(0), face.GetVertexAt(1),
                                face.GetVertexAt(2), face.GetVertexAt(3)
                            };
                            int n = vertices[2].DistanceTo(vertices[3]) < 1e-9 ? 3 : 4;
                            for (int i = 0; i < n; i++)
                                Add(vertices[i], vertices[(i + 1) % n], transforms);
                        }
                        else if (piece is Curve curve)
                        {
                            SampleCurve(curve, transforms, childLabel);
                        }
                        else if (piece.GetType() != original.GetType() &&
                                 piece is not Solid3d && piece is not Autodesk.AutoCAD.DatabaseServices.Region)
                        {
                            TryDecompose(piece, transforms, childLabel, depth + 1);
                        }
                        else
                        {
                            warnings.Add(new Warning(childLabel, piece.GetType().Name,
                                "Exploded geometry still unsupported, no solid silhouette extraction"));
                        }
                    }
                }
                if (segments.Count > before)
                {
                    warnings.Add(new Warning(label, original.GetType().Name,
                        "Some geometries were extracted via non-destructive Explode; verify all omitted objects",
                        true));
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                warnings.Add(new Warning(label, original.GetType().Name,
                    "Explode unavailable: " + ex.GetType().Name));
                return false;
            }
            finally
            {
                foreach (DBObject part in exploded)
                    part.Dispose();
            }
        }

        private void AddPolylineLeg(Polyline poly, int a, int b,
            IReadOnlyList<Matrix3d> transforms, string label)
        {
            if (Math.Abs(poly.GetBulgeAt(a)) > 1e-10)
            {
                warnings.Add(new Warning(label, "Polyline", "Curved bulge segment is not a straight line"));
                return;
            }
            Add(poly.GetPoint3dAt(a), poly.GetPoint3dAt(b), transforms);
        }

        private void Add(Point3d a, Point3d b, IReadOnlyList<Matrix3d> transforms)
        {
            foreach (Matrix3d matrix in transforms)
            {
                a = a.TransformBy(matrix);
                b = b.TransformBy(matrix);
            }
            if (a.DistanceTo(b) < 1e-9) return;
            segments.Add(new SketchSegment3(
                new SketchPoint3(a.X, a.Y, a.Z),
                new SketchPoint3(b.X, b.Y, b.Z)));
        }
    }
}
