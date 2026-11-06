using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace SPTC.SupportDrawings;

/// <summary>
/// SPTC SUPPORT DRAWINGS - REV 18.
/// Clones a selected support into its own DWG and arranges four true 3D model
/// viewports on an A3-like paper-space sheet. It is not a flattened fabrication drawing.
/// </summary>
public sealed class SupportDrawingCommands : IExtensionApplication
{
    public void Initialize() =>
        AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\nSPTC REV18: SPTC_ORTHO18_TOP_API_TEST verifies a real native OrthoCube and optionally attempts public CreateOrthos on a fresh cloned DWG, TEST PROJECT ONLY.");
    public void Terminate() { }

    [CommandMethod("SPTC_SUPPORTEXPORT", CommandFlags.Modal)]
    public void ExportOne()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        PromptSelectionResult res = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect all objects forming ONE pipe support: "
        });
        if (res.Status != PromptStatus.OK) return;

        using var dialog = new WinForms.SaveFileDialog
        {
            Title = "SPTC - Export Support Drawing",
            Filter = "AutoCAD Drawing (*.dwg)|*.dwg",
            FileName = "SUPPORT_001.dwg",
            AddExtension = true,
            DefaultExt = "dwg",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        try
        {
            string name = Path.GetFileNameWithoutExtension(dialog.FileName);
            SheetExporter.Export(doc.Database, res.Value.GetObjectIds(), dialog.FileName, name);
            ed.WriteMessage("\nSupport drawing created: " + dialog.FileName);
            ed.WriteMessage("\nIncludes four model viewports, overall envelope, and object inventory CSV.");
            ed.WriteMessage("\nOpen the DWG layout and VERIFY geometry, units, orientation, and all dimensions.");
        }
        catch (System.Exception e) { ed.WriteMessage("\nSupport export failed: " + e.Message); }
    }

    // REV08: Native Plant 3D Ortho creation is an interactive project workflow.
    // Start from the registered 3D model, not from our independent linework DWG.
    // Avoid driving the OrthoCube and project dialogs with blind keystrokes.
    [CommandMethod("SPTC_SUPPORT2D", CommandFlags.Modal)]
    public void StartNativeOrtho()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor editor = doc.Editor;

        if (string.IsNullOrWhiteSpace(doc.Database.Filename))
        {
            editor.WriteMessage("\nREV13: Save the source model DWG inside the active Plant 3D project first.");
            return;
        }

        // REV13: derive the source and Plant project from the active document.
        // Never assume a named sample DWG, fixed project, zone, or object handle.
        NativeOrthoProjectCheck.Result source = NativeOrthoProjectCheck.Inspect(doc.Database.Filename);
        editor.WriteMessage("\nREV13 source 3D model: " + doc.Database.Filename);
        editor.WriteMessage("\nREV13 active project: " + source.ProjectSummary);
        editor.WriteMessage("\nREV13 project registration: " + source.RegistrationStatus);
        if (!source.SourceRegistrationVerified)
            editor.WriteMessage("\nWARNING: Ortho source registration is unverified. Plant 3D may reject this drawing.");

        editor.WriteMessage("\nSPTC REV13: Select the support members to frame with Plant 3D OrthoCube.");
        editor.WriteMessage("\nIMPORTANT: Native PLANTORTHOCREATE uses the model and OrthoCube. Selection is guidance, not an automatic Ortho-only filter.");
        PromptSelectionResult selection = editor.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect support steel members in the SOURCE project 3D model: "
        });
        if (selection.Status != PromptStatus.OK) return;

        int count = 0;
        Extents3d? selectedBounds = null;
        using (Transaction transaction = doc.Database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selection.Value.GetObjectIds())
            {
                if (transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity)
                    continue;
                count++;
                try
                {
                    Extents3d ext = entity.GeometricExtents;
                    if (selectedBounds.HasValue)
                    {
                        Extents3d current = selectedBounds.Value;
                        current.AddExtents(ext);
                        selectedBounds = current;
                    }
                    else selectedBounds = ext;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Native Plant 3D objects may expose only approximate or no
                    // AutoCAD extents. This must not prevent the native Ortho dialog.
                }
            }
        }
        if (count == 0)
        {
            editor.WriteMessage("\nREV13: No valid source entities selected. Ortho was not started.");
            return;
        }

        editor.WriteMessage($"\nREV13 selected {count} support entities.");
        if (selectedBounds.HasValue)
        {
            Extents3d ext = selectedBounds.Value;
            editor.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\nSource bounding box: MIN({0:0.###}, {1:0.###}, {2:0.###}) MAX({3:0.###}, {4:0.###}, {5:0.###}), source drawing units.",
                ext.MinPoint.X, ext.MinPoint.Y, ext.MinPoint.Z,
                ext.MaxPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z));
        }

        editor.WriteMessage("\nNext: choose Create New Ortho DWG (or an existing project Ortho),");
        editor.WriteMessage("\nposition and resize the OrthoCube around ONLY these supports, select Front/Top/Side,");
        editor.WriteMessage("\nchoose scale, press Create, then place the Ortho viewport in the drawing.");
        editor.WriteMessage("\nPlant 3D generates the project-linked 2D DWG. This remains INTERACTIVE.");
        editor.WriteMessage("\nFor fully unattended automation API feasibility, run SPTC_ORTHOAUTOPROBE and review its report.");

        try
        {
            // Queue the native Plant 3D command after our modal managed
            // command ends; interactive UI and OrthoCube belong to Plant 3D.
            doc.SendStringToExecute("_.PLANTORTHOCREATE ", true, false, false);
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage("\nREV13 unable to start PLANTORTHOCREATE: " + ex.Message);
            editor.WriteMessage("\nTry running PLANTORTHOCREATE manually in the original project model DWG.");
        }
    }

    [CommandMethod("SPTC_SUPPORT2DLINEWORK", CommandFlags.Modal)]
    public void Export2D()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor editor = doc.Editor;
        PromptSelectionResult selection = editor.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect support linework or blocks for projected 2D geometry: "
        });
        if (selection.Status != PromptStatus.OK) return;

        using var dialog = new WinForms.SaveFileDialog
        {
            Title = "SPTC REV07 - Save 2D Orthographic Linework",
            Filter = "AutoCAD Drawing (*.dwg)|*.dwg",
            FileName = "SUPPORT_2D_PROJECTION.dwg",
            DefaultExt = "dwg",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        try
        {
            Support2DExporter.Result result =
                Support2DExporter.Export(doc.Database, selection.Value.GetObjectIds(), dialog.FileName);
            editor.WriteMessage($"\nREV07 projected DWG: {dialog.FileName}");
            editor.WriteMessage($"\nGenerated {result.Lines2D} 2D LINE entities from {result.ExtractedSegments} 3D edges; " +
                $"collapsed {result.SkippedCollapsed}, unsupported {result.Unsupported}, approx curves/decompositions {result.Approximated}.");
            editor.WriteMessage($"\nReview unsupported geometry report: {result.Report}");
            editor.WriteMessage("\nREV07: BRep extraction attempts real Solid3d edges from Plant 3D components, including structural steel.");
            editor.WriteMessage("\nWARNING: curve sampling is approximate; no solid silhouette or hidden-line removal. Not a fabrication drawing.");
        }
        catch (Support2DGeometryException ex)
        {
            // A separate, explicitly labeled 3D view-based sheet is safer than
            // inventing a rectangular 2D pipe outline from bounding extents.
            string fallback = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dialog.FileName))!,
                Path.GetFileNameWithoutExtension(dialog.FileName) + "_3D_VIEWPORT_FALLBACK.dwg");
            if (File.Exists(fallback))
            {
                editor.WriteMessage("\nREV07: 2D extraction unsupported. Fallback file already exists, so nothing overwritten.");
                editor.WriteMessage("\n" + ex.Message);
                return;
            }
            try
            {
                SheetExporter.Export(doc.Database, selection.Value.GetObjectIds(), fallback,
                    "VISUAL FALLBACK - NOT 2D FABRICATION");
                editor.WriteMessage("\nREV07: 2D extraction unavailable; produced a 3D VIEWPORT FALLBACK instead: " + fallback);
                editor.WriteMessage("\nThis DWG contains the native 3D model in viewports, NOT flattened 2D geometry.");
                editor.WriteMessage("\n" + ex.Message);
            }
            catch (System.Exception fallbackError)
            {
                editor.WriteMessage("\nREV07: 2D extraction and visual fallback both failed: " + fallbackError.Message);
                Write2DDiagnostic(editor, dialog.FileName, fallbackError, "3D viewport fallback");
                editor.WriteMessage("\n" + ex.Message);
            }
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage("\nREV07 2D export stopped: " + ex.GetType().Name + ": " + ex.Message);
            Write2DDiagnostic(editor, dialog.FileName, ex, "SPTC_SUPPORT2D");
        }
    }

    [CommandMethod("SPTC_SUPPORT2DBATCH", CommandFlags.Modal)]
    public void Export2DBatch()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor editor = doc.Editor;
        editor.WriteMessage("\nREV07 batch 2D mode: one selected support block reference per output DWG.");
        PromptSelectionResult selection = editor.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect individual support block inserts for 2D output: "
        });
        if (selection.Status != PromptStatus.OK) return;
        var blocks = new List<(ObjectId id, string name)>();
        using (Transaction tx = doc.Database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selection.Value.GetObjectIds())
                if (tx.GetObject(id, OpenMode.ForRead, false) is BlockReference block)
                    blocks.Add((id, block.Name));
        }
        if (blocks.Count == 0)
        {
            editor.WriteMessage("\nNo block inserts were selected; use SPTC_SUPPORT2D for loose linework.");
            return;
        }

        using var dialog = new SupportBatchDialog(blocks);
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        int passed = 0, failed = 0, fallbackCount = 0;
        foreach (int index in dialog.CheckedIndices)
        {
            (ObjectId id, string name) selected = blocks[index];
            string filename = string.Format(CultureInfo.InvariantCulture,
                "{0}_{1:000}_{2}_2D.dwg", SafeFilename(dialog.Prefix), index + 1,
                SafeFilename(selected.name));
            string path = Path.Combine(dialog.OutputFolder, filename);
            if (File.Exists(path))
            {
                failed++;
                editor.WriteMessage("\nSKIPPED existing 2D DWG: " + path);
                continue;
            }
            try
            {
                Support2DExporter.Result result =
                    Support2DExporter.Export(doc.Database, new[] { selected.id }, path);
                passed++;
                editor.WriteMessage($"\nCREATED {path}: {result.Lines2D} line entities, {result.Unsupported} unsupported objects/segments.");
            }
            catch (Support2DGeometryException ex)
            {
                string fallbackPath = Path.Combine(dialog.OutputFolder, string.Format(
                    CultureInfo.InvariantCulture, "{0}_{1:000}_{2}_3D_VIEWPORT_FALLBACK.dwg",
                    SafeFilename(dialog.Prefix), index + 1, SafeFilename(selected.name)));
                if (File.Exists(fallbackPath))
                {
                    failed++;
                    editor.WriteMessage("\nSKIPPED: existing visual fallback DWG, nothing overwritten: " + fallbackPath);
                    continue;
                }
                try
                {
                    SheetExporter.Export(doc.Database, new[] { selected.id }, fallbackPath,
                        "VISUAL FALLBACK - NOT 2D FABRICATION");
                    fallbackCount++;
                    editor.WriteMessage("\n3D VIEWPORT FALLBACK (NOT 2D): " + fallbackPath);
                    editor.WriteMessage("\nDetails: " + ex.Message);
                }
                catch (System.Exception fallbackError)
                {
                    failed++;
                    editor.WriteMessage("\n2D extraction and fallback failed: " + fallbackError.Message);
                    Write2DDiagnostic(editor, path, fallbackError, "3D viewport batch fallback");
                }
            }
            catch (System.Exception ex)
            {
                failed++;
                editor.WriteMessage("\nSKIPPED/FAILED " + selected.name + ": " + ex.Message);
                Write2DDiagnostic(editor, path, ex, "SPTC_SUPPORT2DBATCH");
            }
        }
        editor.WriteMessage($"\nREV07 batch finished: {passed} actual 2D DWGs, {fallbackCount} explicitly labeled 3D viewport fallback DWGs, {failed} failures/skips.");
    }

    [CommandMethod("SPTC_SUPPORTINSPECT", CommandFlags.Modal)]
    public void Inspect()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        PromptSelectionResult result = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect support objects or block inserts to inspect: "
        });
        if (result.Status != PromptStatus.OK) return;

        using var dialog = new WinForms.SaveFileDialog
        {
            Title = "SPTC - Save Support Entity Audit",
            Filter = "CSV File (*.csv)|*.csv",
            FileName = "SPTC_Support_Entity_Audit.csv",
            DefaultExt = "csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        try
        {
            SupportEntityAudit.Summary summary =
                SupportEntityAudit.WriteReport(doc.Database, result.Value.GetObjectIds(), dialog.FileName);
            ed.WriteMessage($"\nSPTC REV03 audit saved: {dialog.FileName}");
            ed.WriteMessage($"\nSelected entities: {summary.SelectedEntities}; 1-level block members: {summary.DefinitionEntities}; support candidates: {summary.SupportCandidates}; selected without extents: {summary.MissingExtents}.");
            ed.WriteMessage("\nClassifications are identification hints, not validated Plant 3D support/BOM data.");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nSPTC support audit failed: " + ex.Message);
        }
    }

    [CommandMethod("SPTC_SUPPORTMANAGER", CommandFlags.Modal)]
    public void OpenManager() => ExportBatch();

    [CommandMethod("SPTC_SUPPORTBATCH", CommandFlags.Modal)]
    public void ExportBatch()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        ed.WriteMessage("\nBatch mode: each selected BLOCK INSERT must contain one complete support.");
        PromptSelectionResult res = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect support block inserts for individual drawings: "
        });
        if (res.Status != PromptStatus.OK) return;

        var list = new List<(ObjectId id, string name)>();
        using (var transaction = doc.Database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in res.Value.GetObjectIds())
            {
                var ent = transaction.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                if (ent != null) list.Add((id, ent.Name));
            }
        }
        if (list.Count == 0)
        {
            ed.WriteMessage("\nNo block inserts selected. For loose steel members, use SPTC_SUPPORTEXPORT.");
            return;
        }
        using var dialog = new SupportBatchDialog(list);
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        int ok = 0, errors = 0;
        foreach (int index in dialog.CheckedIndices)
        {
            var selected = list[index];
            string name = SafeFilename(selected.name);
            string prefix = SafeFilename(dialog.Prefix);
            string output = Path.Combine(dialog.OutputFolder, string.Format(
                CultureInfo.InvariantCulture, "{0}_{1:000}_{2}.dwg", prefix, index + 1, name));
            if (File.Exists(output))
            {
                ed.WriteMessage("\nSKIPPED, already exists: " + output);
                errors++;
                continue;
            }
            try
            {
                SheetExporter.Export(doc.Database, new[] { selected.id }, output,
                    string.Format(CultureInfo.InvariantCulture, "SUPPORT {0:000} - {1}", index + 1, selected.name));
                ok++;
                ed.WriteMessage("\nCREATED " + output);
            }
            catch (System.Exception ex)
            {
                errors++;
                ed.WriteMessage("\nFAILED " + selected.name + ": " + ex.Message);
            }
        }
        ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
            "\nBatch finished: {0} DWG(s) created, {1} skipped/failed. Check all sheets.", ok, errors));
    }

    private static void Write2DDiagnostic(Editor editor, string dwgPath,
        System.Exception failure, string operation)
    {
        // REV07 diagnostic: precise managed stack trace for any remaining
        // Plant 3D custom-object / output-database error.
        try
        {
            string filename = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(dwgPath))!,
                Path.GetFileNameWithoutExtension(dwgPath) + "_REV07_ERROR.txt");
            string detail = "SPTC Support Drawings REV07" + Environment.NewLine +
                "Operation: " + operation + Environment.NewLine +
                "Exception: " + failure.ToString() + Environment.NewLine +
                "Time (local): " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture);
            File.WriteAllText(filename, detail);
            editor.WriteMessage("\nREV07 diagnostic stack trace saved: " + filename);
        }
        catch (System.Exception logError)
        {
            editor.WriteMessage("\nREV07 could not save diagnostic: " + logError.Message);
            editor.WriteMessage("\nStack trace: " + failure);
        }
    }

    private static string SafeFilename(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "UNNAMED";
        string result = new string(value.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) || ch == '*' || ch == '?' ? '_' : ch).ToArray());
        return result.Length > 50 ? result[..50] : result;
    }
}

internal static class SheetExporter
{
    private const double A3Width = 420.0;
    private const double A3Height = 297.0;
    private record ObjectSummary(string EntityType, string Layer, int Count);

    internal static void Export(Database source, ObjectId[] selected, string outputDwg, string title)
    {
        if (selected.Length == 0) throw new InvalidOperationException("No support geometry selected.");
        BoundingBox3? bbox = null;
        var itemCounts = new Dictionary<(string Type, string Layer), int>();

        using (var tx = source.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selected.Distinct())
            {
                Entity? entity = tx.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (entity == null) continue;
                var key = (entity.GetType().Name, entity.Layer);
                itemCounts[key] = itemCounts.GetValueOrDefault(key) + 1;
                try
                {
                    Extents3d extents = entity.GeometricExtents;
                    var next = new BoundingBox3(
                        extents.MinPoint.X, extents.MinPoint.Y, extents.MinPoint.Z,
                        extents.MaxPoint.X, extents.MaxPoint.Y, extents.MaxPoint.Z);
                    bbox = bbox.HasValue ? bbox.Value.Include(next) : next;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { /* Some proxy objects have no extents. */ }
            }
        }
        if (!bbox.HasValue)
            throw new InvalidOperationException("Selected objects have no readable 3D extents.");
        BoundingBox3 model = bbox.Value;

        // Preserve the original 3D support geometry. Wblock copies selected entities
        // and their dependencies into an independent drawing database.
        using var sheet = new Database(true, false);
        source.Wblock(sheet, new ObjectIdCollection(selected.Distinct().ToArray()),
            Point3d.Origin, DuplicateRecordCloning.Ignore);

        using (var tx = sheet.TransactionManager.StartTransaction())
        {
            var blocks = (BlockTable)tx.GetObject(sheet.BlockTableId, OpenMode.ForRead);
            var paper = (BlockTableRecord)tx.GetObject(
                blocks[BlockTableRecord.PaperSpace], OpenMode.ForWrite);

            DrawRect(tx, paper, 8, 8, A3Width - 8, A3Height - 8);
            DrawLine(tx, paper, 8, 42, A3Width - 8, 42);
            DrawLine(tx, paper, 265, 8, 265, 42);
            DrawLine(tx, paper, 8, 27, 265, 27);
            AddText(tx, paper, "SPTC  |  PIPE SUPPORT DRAWING  |  REV 08", 12, 32, 3.6);
            AddText(tx, paper, Crop(title, 43), 12, 19, 3.5);
            AddText(tx, paper, string.Format(CultureInfo.InvariantCulture,
                "ENVELOPE (DWG units): X={0:0.##}  Y={1:0.##}  Z={2:0.##}",
                model.SizeX, model.SizeY, model.SizeZ), 12, 12, 2.8);
            AddText(tx, paper, "TOP", 20, 277, 3);
            AddText(tx, paper, "SIDE", 225, 277, 3);
            AddText(tx, paper, "FRONT", 20, 141, 3);
            AddText(tx, paper, "ISOMETRIC", 225, 141, 3);

            AddViewport(tx, paper, model, "TOP", 108, 209, 183, 125, new Vector3d(0, 0, 1));
            AddViewport(tx, paper, model, "SIDE", 312, 209, 183, 125, new Vector3d(1, 0, 0));
            AddViewport(tx, paper, model, "FRONT", 108, 91, 183, 82, new Vector3d(0, -1, 0));
            AddViewport(tx, paper, model, "ISO", 312, 91, 183, 82, new Vector3d(1, -1, 1));
            SupportSheetAnnotations.Add(tx, paper, model, source.Insunits.ToString(), itemCounts);
            tx.Commit();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputDwg))!);
        sheet.SaveAs(outputDwg, DwgVersion.Current);
        string csv = Path.Combine(Path.GetDirectoryName(outputDwg)!,
            Path.GetFileNameWithoutExtension(outputDwg) + "_object_inventory.csv");
        WriteInventory(csv, title, source.Insunits.ToString(), model, itemCounts);
        SupportAttributeRegister.WriteIfAvailable(source, selected, outputDwg);
        string auditCsv = Path.Combine(Path.GetDirectoryName(outputDwg)!,
            Path.GetFileNameWithoutExtension(outputDwg) + "_entity_audit.csv");
        SupportEntityAudit.WriteReport(source, selected, auditCsv);
    }

    private static string Crop(string s, int max) => s.Length > max ? s[..max] : s;

    private static void AddViewport(Transaction tx, BlockTableRecord paper,
        BoundingBox3 box, string view, double x, double y, double w, double h, Vector3d direction)
    {
        var vp = new Viewport
        {
            CenterPoint = new Point3d(x, y, 0),
            Width = w,
            Height = h,
            ViewTarget = new Point3d(box.MidX, box.MidY, box.MidZ),
            ViewDirection = direction,
            ViewCenter = new Point2d(0, 0),
            ViewHeight = SupportSheetMath.ViewHeight(box, view, w, h),
            GridOn = false
        };
        paper.AppendEntity(vp);
        tx.AddNewlyCreatedDBObject(vp, true);
        vp.On = true;
        vp.Locked = true;
    }

    private static void DrawLine(Transaction tx, BlockTableRecord paper,
        double x1, double y1, double x2, double y2)
    {
        var line = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
        paper.AppendEntity(line);
        tx.AddNewlyCreatedDBObject(line, true);
    }

    private static void DrawRect(Transaction tx, BlockTableRecord paper,
        double x1, double y1, double x2, double y2)
    {
        DrawLine(tx, paper, x1, y1, x2, y1);
        DrawLine(tx, paper, x2, y1, x2, y2);
        DrawLine(tx, paper, x2, y2, x1, y2);
        DrawLine(tx, paper, x1, y2, x1, y1);
    }

    private static void AddText(Transaction tx, BlockTableRecord paper,
        string value, double x, double y, double height)
    {
        var text = new DBText
        {
            TextString = value,
            Position = new Point3d(x, y, 0),
            Height = height
        };
        paper.AppendEntity(text);
        tx.AddNewlyCreatedDBObject(text, true);
    }

    private static void WriteInventory(string path, string name, string units,
        BoundingBox3 box, Dictionary<(string Type, string Layer), int> counts)
    {
        using var output = new StreamWriter(path, false);
        output.WriteLine("Support,DimensionX,DimensionY,DimensionZ,DrawingUnits,ObjectType,Layer,Quantity");
        foreach (var entry in counts.OrderBy(x => x.Key.Type).ThenBy(x => x.Key.Layer))
            output.WriteLine(string.Join(",",
                Csv(name),
                box.SizeX.ToString("0.###", CultureInfo.InvariantCulture),
                box.SizeY.ToString("0.###", CultureInfo.InvariantCulture),
                box.SizeZ.ToString("0.###", CultureInfo.InvariantCulture),
                Csv(units), Csv(entry.Key.Type), Csv(entry.Key.Layer),
                entry.Value.ToString(CultureInfo.InvariantCulture)));
    }

    private static string Csv(string value)
    {
        // Ensure Excel interprets potentially user-controlled CAD names as text.
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
