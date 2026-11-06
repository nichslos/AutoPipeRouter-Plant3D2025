using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV14 experimental, EXPLICITLY OPT-IN native Ortho DRAWING registration.
/// Writes only to an operator-confirmed BACKUP Plant 3D project.
/// No fabricated 2D Ortho view, no private SDK calls or raw DCF changes.
/// </summary>
public sealed class NativeOrthoRegistrationTest
{
    [CommandMethod("SPTC_ORTHO14REGISTER_TEST", CommandFlags.Modal)]
    public void RegisterTestDrawing()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        string model = doc.Database.Filename ?? "";
        if (string.IsNullOrWhiteSpace(model) || !File.Exists(model))
        {
            ed.WriteMessage("\nREV14: Save and open a registered source 3D model in a COPIED TEST project.");
            return;
        }

        NativeOrthoProjectCheck.Result check = NativeOrthoProjectCheck.Inspect(model);
        ed.WriteMessage("\nREV14 source: " + model);
        ed.WriteMessage("\nREV14 project: " + check.ProjectSummary);
        if (!check.SourceRegistrationVerified)
        {
            ed.WriteMessage("\nSTOP: Native Piping source registration is not valid: " + check.RegistrationStatus);
            return;
        }

        // Don't use any fixed support or model. Require selected source 3D entities.
        PromptSelectionResult selected = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nREV14: select the support steel members in CURRENT source model: "
        });
        if (selected.Status != PromptStatus.OK) return;

        var handles = new List<string>();
        BoundingBox3? envelope = null;
        using (Transaction tx = doc.Database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selected.Value.GetObjectIds().Distinct())
            {
                if (tx.GetObject(id, OpenMode.ForRead, false) is not Entity entity) continue;
                try
                {
                    Extents3d ext = entity.GeometricExtents;
                    var box = new BoundingBox3(
                        ext.MinPoint.X, ext.MinPoint.Y, ext.MinPoint.Z,
                        ext.MaxPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z);
                    envelope = envelope.HasValue ? envelope.Value.Include(box) : box;
                    handles.Add(id.Handle.ToString());
                }
                catch (System.Exception e)
                {
                    ed.WriteMessage("\nSKIP support without valid 3D extents: " + id.Handle + " (" + e.GetType().Name + ")");
                }
            }
        }
        if (handles.Count == 0 || !envelope.HasValue)
        {
            ed.WriteMessage("\nNo valid support geometry. No changes made.");
            return;
        }

        double factor = doc.Database.Insunits.ToString() switch
        {
            "Millimeters" => 1, "Centimeters" => 10, "Meters" => 1000,
            "Inches" => 25.4, "Feet" => 304.8, _ => 0
        };
        if (factor <= 0)
        {
            ed.WriteMessage("\nREV14 requires supported drawing units. No changes made.");
            return;
        }
        NativeOrthoLayout.Plan geometry;
        try { geometry = NativeOrthoLayout.Build(envelope.Value, factor); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV14 geometry rejected: " + ex.Message);
            return;
        }

        NativeContext context;
        try { context = LocateContext(); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV14 native API unavailable: " + ex.Message);
            return;
        }
        if (!NativeOrthoRegistrationNames.IsInsideProjectDirectory(model, context.ProjectRoot))
        {
            ed.WriteMessage("\nSTOP: Active source model is outside the active Plant 3D project directory.");
            return;
        }
        if (!NativeOrthoRegistrationNames.IsInsideProjectDirectory(context.OrthoFolder, context.ProjectRoot))
        {
            ed.WriteMessage("\nSTOP: Native Ortho folder is not inside active project: " + context.OrthoFolder);
            return;
        }
        if (!Directory.Exists(context.OrthoFolder))
        {
            ed.WriteMessage("\nSTOP: Native Ortho destination folder not found: " + context.OrthoFolder);
            return;
        }
        if (context.AddMethod == null || context.SetVersionMethod == null || context.ValidateMethod == null)
        {
            ed.WriteMessage("\nSTOP: Required public Ortho creation/version/validation API is unavailable.");
            ed.WriteMessage("\nNo project writes have occurred.");
            return;
        }

        using var templateDialog = new WinForms.OpenFileDialog
        {
            Title = "REV14 TEST - select the Ortho template DWT configured for the COPIED project",
            Filter = "AutoCAD Drawing Template (*.dwt)|*.dwt",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        if (templateDialog.ShowDialog() != WinForms.DialogResult.OK) return;

        string dwt = Path.GetFullPath(templateDialog.FileName);
        if (!dwt.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase))
        {
            ed.WriteMessage("\nSTOP: A valid .dwt template is required.");
            return;
        }

        // Two positive, deliberate confirmations: the code cannot prove a project
        // folder is a backup merely by examining its name or path.
        var confirmation = WinForms.MessageBox.Show(
            "EXPERIMENTAL NATIVE PLANT 3D ORTHO PROJECT WRITE\n\n" +
            "Project: " + context.ProjectRoot + "\n" +
            "3D Model: " + Path.GetFileName(model) + "\n" +
            "Members: " + string.Join(", ", handles) + "\n" +
            "Template: " + dwt + "\n\n" +
            "Only proceed in a SEPARATE COPIED TEST PROJECT.\n" +
            "This command can modify its Ortho project registration database.\n" +
            "It does not create a finished 2D Ortho view.\n\n" +
            "Is your active Plant 3D project an isolated BACKUP/TEST COPY?",
            "SPTC REV14 — EXPERIMENTAL PROJECT WRITE",
            WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning,
            WinForms.MessageBoxDefaultButton.Button2);
        if (confirmation != WinForms.DialogResult.Yes) return;

        PromptResult acknowledged = ed.GetString(new PromptStringOptions(
            "\nTo authorize changes in the COPIED TEST PROJECT, type TEST PROJECT: ")
        { AllowSpaces = true });
        if (acknowledged.Status != PromptStatus.OK ||
            !string.Equals(acknowledged.StringResult.Trim(), "TEST PROJECT", StringComparison.Ordinal))
        {
            ed.WriteMessage("\nCancelled. No project writes.");
            return;
        }

        string nonce = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
            "_" + Guid.NewGuid().ToString("N")[..6];
        string filename = NativeOrthoRegistrationNames.NewOrthoDrawingFileName(model, handles, nonce);
        string outDwg = Path.Combine(context.OrthoFolder, filename);
        if (File.Exists(outDwg))
        {
            ed.WriteMessage("\nSTOP: Destination already exists: " + outDwg);
            return;
        }

        string report = Path.Combine(context.OrthoFolder,
            Path.GetFileNameWithoutExtension(filename) + "_REV14_RESULT.txt");
        var log = new StringBuilder();
        log.AppendLine("SPTC REV14 - NATIVE ORTHO DRAWING REGISTRATION EXPERIMENT");
        log.AppendLine("Backup-only test. NOT a generated Ortho view.");
        log.AppendLine("UTC " + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        log.AppendLine("Project: " + context.ProjectRoot);
        log.AppendLine("Source model: " + model);
        log.AppendLine("Support handles: " + string.Join(", ", handles));
        log.AppendLine("Template: " + dwt);
        log.AppendLine("Proposed Ortho DWG: " + outDwg);
        log.AppendLine("OrthoCube min: " + Describe(geometry.PaddedCube.MinX,
            geometry.PaddedCube.MinY, geometry.PaddedCube.MinZ));
        log.AppendLine("OrthoCube max: " + Describe(geometry.PaddedCube.MaxX,
            geometry.PaddedCube.MaxY, geometry.PaddedCube.MaxZ));
        log.AppendLine("Top view candidate scale: 1:" + geometry.Views[0].ScaleDenominator);
        log.AppendLine("Native registration method: " + context.AddMethod);
        log.AppendLine("Native version method: " + context.SetVersionMethod);
        log.AppendLine("Native validation method: " + context.ValidateMethod);

        bool registrationInvoked = false;
        bool created = false;
        try
        {
            // STEP 1: convert a chosen real Ortho DWT to a unique DWG.
            // Never copy an existing Ortho DWG that might contain live views.
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(dwt, FileShare.Read, true, null);
                db.CloseInput(true);
                db.SaveAs(outDwg, DwgVersion.Current);
            }
            created = File.Exists(outDwg);
            if (!created) throw new IOException("AutoCAD did not write the test DWG.");
            log.AppendLine("STEP1_TEMPLATE_SAVE=PASS");

            // STEP 2: use Autodesk's public Project API, not direct DCF writes.
            // Empty parent GUID registers in the root of the Ortho project part.
            object?[] args = {outDwg, "", Path.GetFileNameWithoutExtension(outDwg), false, false};
            registrationInvoked = true;
            object? returned = context.AddMethod.Invoke(context.OrthoProject, args);
            string addResult = returned?.ToString() ?? "";
            log.AppendLine("STEP2_ADD_PNP_DRAWING_RETURN=" + addResult);
            log.AppendLine("FILE_ALREADY_EXISTS=" + args[3]);
            log.AppendLine("ILLEGAL_XREFS=" + args[4]);
            if (args[3] is true || args[4] is true)
                throw new InvalidOperationException("Ortho API flagged an existing drawing or illegal Xrefs.");
            log.AppendLine("STEP2_PROJECT_REGISTRATION=CALLED");

            // STEP 3: critical Ortho.dcf DwgVersion field. The public native
            // API exposes SetOrthoDwgVersion(string,int); never edit SQLite.
            // Only use returned drawing GUID, if actual API gives a GUID.
            string key = "";
            if (Guid.TryParse(addResult, out _)) key = addResult;
            if (key.Length == 0)
                key = FindDrawingGuid(context.OrthoProject, outDwg);
            if (!Guid.TryParse(key, out _))
            {
                log.AppendLine("STEP3_VERSION=SKIPPED_NO_VERIFIED_DRAWING_GUID");
                log.AppendLine("Register returned a non-GUID. Do NOT guess GUID or edit DCF manually.");
            }
            else
            {
                context.SetVersionMethod.Invoke(context.OrthoProject, new object?[] {key, 1});
                log.AppendLine("STEP3_SET_ORTHO_DWG_VERSION=CALLED_WITH_DRAWING_GUID");
            }

            // STEP 4: final native verification, never infer from file existence.
            object?[] validity = {outDwg, context.OrthoProject, ""};
            object? val = context.ValidateMethod.Invoke(null, validity);
            bool accepted = val is bool verified && verified;
            log.AppendLine("STEP4_NATIVE_IS_VALID_ORTHO=" + (accepted ? "TRUE" : "FALSE"));
            log.AppendLine("STEP4_VALIDATION_REASON=" + (validity[2]?.ToString() ?? ""));
            log.AppendLine("FINAL_STATUS=" + (accepted
                ? "REGISTERED_DWG_VALIDATED_NO_VIEW_YET"
                : "TEST_DWG_REQUIRES_INSPECTION_NO_VIEW_YET"));
            ed.WriteMessage("\nREV14 project Ortho registration test: " +
                (accepted ? "Native validation accepted drawing" : "Not yet valid; see report"));
            ed.WriteMessage("\nOutput: " + outDwg);
            ed.WriteMessage("\nNO TOP VIEW GENERATED. This is only native drawing registration.");
        }
        catch (TargetInvocationException ex)
        {
            log.AppendLine("ERROR=" + (ex.InnerException ?? ex));
            ed.WriteMessage("\nREV14 native call failed: " + (ex.InnerException?.Message ?? ex.Message));
        }
        catch (System.Exception ex)
        {
            log.AppendLine("ERROR=" + ex);
            ed.WriteMessage("\nREV14 native test stopped: " + ex.Message);
        }
        finally
        {
            // After invoking AddPnPDrawingFile, do not remove a possibly
            // registered project file without resolving the project state.
            if (created && !registrationInvoked && File.Exists(outDwg))
            {
                try
                {
                    File.Delete(outDwg);
                    log.AppendLine("CLEANUP=deleted unregistered test DWG");
                }
                catch (System.Exception ex) { log.AppendLine("CLEANUP_ERROR=" + ex.Message); }
            }
            try
            {
                File.WriteAllText(report, log.ToString(), new UTF8Encoding(false));
                ed.WriteMessage("\nREV14 report: " + report);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nREV14 could not save report: " + ex.Message);
                ed.WriteMessage("\n" + log.ToString());
            }
        }
    }

    private sealed record NativeContext(string ProjectRoot, string OrthoFolder,
        object OrthoProject, MethodInfo? AddMethod,
        MethodInfo? SetVersionMethod, MethodInfo? ValidateMethod);

    private static NativeContext LocateContext()
    {
        const BindingFlags allPublic = BindingFlags.Public | BindingFlags.Instance;
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Type plant = assemblies.Select(a => a.GetType(
            "Autodesk.ProcessPower.PlantInstance.PlantApplication", false))
            .FirstOrDefault(t => t != null) ??
            throw new InvalidOperationException("PlantApplication not loaded.");
        object project = plant.GetProperty("CurrentProject", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null) ?? throw new InvalidOperationException("No active Plant 3D project.");
        string root = project.GetType().GetProperty("ProjectFolderPath")?
            .GetValue(project)?.ToString() ?? "";
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Current project directory unavailable: " + root);
        object parts = project.GetType().GetProperty("ProjectParts")?
            .GetValue(project) ?? throw new InvalidOperationException("ProjectParts missing.");
        object ortho = parts.GetType().GetProperty("Item")?
            .GetValue(parts, new object[] {"Ortho"}) ??
            throw new InvalidOperationException("Current project has no Ortho part.");
        MethodInfo? getPath = ortho.GetType().GetMethod("GetOrthoPath",
            allPublic, null, Type.EmptyTypes, null);
        string folder = getPath?.Invoke(ortho, null)?.ToString() ?? "";
        if (folder.Length == 0) throw new InvalidOperationException("GetOrthoPath returned no Ortho destination.");
        if (!Path.IsPathRooted(folder)) folder = Path.Combine(root, folder);
        folder = Path.GetFullPath(folder);
        Type t = ortho.GetType();
        MethodInfo? add = t.GetMethod("AddPnPDrawingFile", allPublic, null,
            new[] {typeof(string), typeof(string), typeof(string),
                typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType()}, null);
        MethodInfo? version = t.GetMethod("SetOrthoDwgVersion", allPublic, null,
            new[] {typeof(string), typeof(int)}, null);

        Type util = assemblies.Select(a => a.GetType(
            "Autodesk.ProcessPower.P3dProjectParts.OrthoDocUtils", false))
            .FirstOrDefault(x => x != null) ??
            throw new InvalidOperationException("OrthoDocUtils assembly not loaded.");
        MethodInfo? validate = util.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "IsValidOrthoDrawing" || m.ReturnType != typeof(bool))
                    return false;
                ParameterInfo[] p = m.GetParameters();
                return p.Length == 3 && p[0].ParameterType == typeof(string) &&
                    p[1].ParameterType.IsInstanceOfType(ortho) &&
                    p[2].ParameterType == typeof(string).MakeByRefType();
            });
        return new NativeContext(root, folder, ortho, add, version, validate);
    }

    private static string FindDrawingGuid(object orthoPart, string filename)
    {
        try
        {
            MethodInfo? method = orthoPart.GetType().GetMethod("GetPnPDrawingFiles",
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method?.Invoke(orthoPart, null) is not IEnumerable drawings) return "";
            foreach (object? item in drawings)
            {
                if (item == null) continue;
                string? filenameValue = Property(item, "FileName") ??
                    Property(item, "RelativeFilePath") ?? Property(item, "Name");
                if (string.IsNullOrEmpty(filenameValue) ||
                    !string.Equals(Path.GetFileName(filenameValue), Path.GetFileName(filename),
                        StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string key in new[] {"Guid", "PnPDrawingGuid", "DrawingGuid"})
                {
                    string? guid = Property(item, key);
                    if (Guid.TryParse(guid, out _)) return guid!;
                }
            }
        }
        catch { /* Return unverified GUID and do not set version. */ }
        return "";
    }

    private static string? Property(object item, string name)
    {
        try
        {
            PropertyInfo? prop = item.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(item)?.ToString();
        }
        catch { return null; }
    }

    private static string Describe(double x, double y, double z) =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", x, y, z);
}
