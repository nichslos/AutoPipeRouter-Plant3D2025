using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV11 native Ortho staging (no write to Plant 3D project).
/// The emitted plan captures the model, support handles, padded cube, standard
/// top/front/side view directions, scale candidates, and sheet positions.
/// Actual native Ortho registration and GenerateOrthos are NOT called.
/// </summary>
public sealed class NativeOrthoStagingCommands
{
    [CommandMethod("SPTC_ORTHOAUTOPLAN", CommandFlags.Modal)]
    public void CreatePlan()
    {
        Document? document = AcadApp.DocumentManager.MdiActiveDocument;
        if (document == null) return;
        Editor editor = document.Editor;
        string drawing = document.Database.Filename ?? "";
        if (string.IsNullOrWhiteSpace(drawing) || !File.Exists(drawing))
        {
            editor.WriteMessage("\nREV11: Save the source 3D model DWG in the Plant 3D project first.");
            return;
        }
        double unitToMm = UnitFactor(document.Database.Insunits.ToString());
        if (unitToMm <= 0)
        {
            editor.WriteMessage("\nREV11: Source DWG units are not a supported physical unit. Set DWG UNITS (e.g., Millimeters) before staging.");
            return;
        }
        PromptSelectionResult selection = editor.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect all structural members that form ONE support for native Ortho: "
        });
        if (selection.Status != PromptStatus.OK) return;

        var handles = new List<string>();
        BoundingBox3? bounds = null;
        var skipped = new List<string>();
        using (Transaction tx = document.Database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selection.Value.GetObjectIds().Distinct())
            {
                try
                {
                    if (tx.GetObject(id, OpenMode.ForRead, false) is not Entity member) continue;
                    handles.Add(id.Handle.ToString());
                    try
                    {
                        Extents3d ext = member.GeometricExtents;
                        var next = new BoundingBox3(
                            ext.MinPoint.X, ext.MinPoint.Y, ext.MinPoint.Z,
                            ext.MaxPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z);
                        bounds = bounds.HasValue ? bounds.Value.Include(next) : next;
                    }
                    catch (System.Exception ex)
                    {
                        skipped.Add(id.Handle + ": no readable extents (" + ex.GetType().Name + ")");
                    }
                }
                catch (System.Exception ex)
                {
                    skipped.Add("Object read failed: " + ex.GetType().Name);
                }
            }
        }
        if (handles.Count == 0 || !bounds.HasValue)
        {
            editor.WriteMessage("\nREV11: No selected support has readable 3D extents. No Ortho plan generated.");
            return;
        }
        NativeOrthoLayout.Plan plan;
        try { plan = NativeOrthoLayout.Build(bounds.Value, unitToMm); }
        catch (System.Exception ex)
        {
            editor.WriteMessage("\nREV11: Invalid support envelope: " + ex.Message);
            return;
        }

        using var save = new WinForms.SaveFileDialog
        {
            Title = "SPTC REV11 — Save native Ortho automation plan (non-mutating)",
            Filter = "JSON plan (*.json)|*.json",
            FileName = "SPTC_ORTHO_AUTOPLAN_REV11.json",
            DefaultExt = "json", AddExtension = true, OverwritePrompt = true
        };
        if (save.ShowDialog() != WinForms.DialogResult.OK) return;
        try
        {
            NativeOrthoProjectCheck.Result nativeProject = NativeOrthoProjectCheck.Inspect(drawing);
            var payload = new
            {
                Revision = "REV11",
                Purpose = "Native Plant 3D orthographic creation plan; NOT an actual native Ortho drawing",
                State = "STAGED_ONLY_PROJECT_NOT_MODIFIED",
                Source3dModel = drawing,
                SourceProject = nativeProject.ProjectSummary,
                SourceRegistrationVerified = nativeProject.SourceRegistrationVerified,
                SourceRegistrationStatus = nativeProject.RegistrationStatus,
                OrthoProjectPart = nativeProject.OrthoProjectPart,
                ExistingProjectOrthoDrawings = nativeProject.ExistingOrthoDrawingNames,
                SourceDrawingUnits = document.Database.Insunits.ToString(),
                SourceUnitToMillimeters = unitToMm,
                SelectedSupportHandles = handles,
                NoExtentsWarnings = skipped,
                OrthoCube = new
                {
                    Original = plan.OriginalBounds,
                    Padded = plan.PaddedCube,
                    ClearanceByAxisInSourceUnits = new { X = plan.PaddingX, Y = plan.PaddingY, Z = plan.PaddingZ },
                    MinimumClearancePerSideMillimeters = 10,
                    ClearancePercentPerAxis = 10
                },
                ViewLayouts = plan.Views.Select(v => new
                {
                    v.Name,
                    ViewDirection = new[] {v.DirectionX,v.DirectionY,v.DirectionZ},
                    Up = new[] {v.UpX,v.UpY,v.UpZ},
                    InsertionCenterInPaperMm = new[] {v.PaperCenterX,v.PaperCenterY},
                    PaperViewportSizeMm = new[] {v.PaperWidth,v.PaperHeight},
                    ModelProjectedSpanInSourceUnits = new[] {v.ModelWidth,v.ModelHeight},
                    CandidateScale = v.CandidateScale,
                    Scale = "1:" + v.ScaleDenominator
                }),
                Sheet = new { Size = "ISO A3 landscape", WidthMm = 420, HeightMm = 297 },
                NativeOrthoAutomationVerified = false,
                Blocker = "PlantOrthoView.CreateOrthos() invokes interactive viewport placement; project registration also opens a selection dialog.",
                NextStep = "Verify native Ortho SDK constructor/session initialization and unattended placement in a copy of the current project."
            };

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions {WriteIndented = true});
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(save.FileName))!);
            File.WriteAllText(save.FileName, json, new UTF8Encoding(false));
            editor.WriteMessage("\nREV11 Ortho automation plan saved: " + save.FileName);
            editor.WriteMessage("\nProject source registration check: " + nativeProject.RegistrationStatus);
            editor.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\nAxis-specific margins (DWG units): X={0:0.###}, Y={1:0.###}, Z={2:0.###}",
                plan.PaddingX, plan.PaddingY, plan.PaddingZ));
            editor.WriteMessage("\nSource members: " + handles.Count + ", extents warnings: " + skipped.Count);
            foreach (NativeOrthoLayout.ViewPlan view in plan.Views)
                editor.WriteMessage("\n" + view.Name + ": scale 1:" + view.ScaleDenominator +
                    ", A3 center " + view.PaperCenterX + "," + view.PaperCenterY + " mm.");
            editor.WriteMessage("\nNOTE: No project Ortho DWG/view has been created; these are planned parameters.");
            if (!nativeProject.SourceRegistrationVerified)
                editor.WriteMessage("\nWARNING: Source model project registration is not verified. Review the JSON status.");
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage("\nREV10 Ortho stage failed: " + ex);
        }
    }

    private static double UnitFactor(string units) => units switch
    {
        "Millimeters" => 1,
        "Centimeters" => 10,
        "Meters" => 1000,
        "Inches" => 25.4,
        "Feet" => 304.8,
        _ => 0
    };

}
