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
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV15 isolated test: clone an ORTHO DWG created by Plant 3D's own wizard,
/// NOT a raw DWT. Never modifies a live existing drawing or uses raw DCF writes.
/// The new project registration must be tested in a copied project.
/// </summary>
public sealed class NativeOrthoSeedCloneTest
{
    [CommandMethod("SPTC_ORTHO15CLONE_TEST", CommandFlags.Modal)]
    public void CreateFromNativeOrthoSeed()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        string source = doc.Database.Filename ?? "";
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            ed.WriteMessage("\nREV15: Open a saved 3D source DWG in your COPIED TEST Plant 3D project.");
            return;
        }
        NativeOrthoProjectCheck.Result valid = NativeOrthoProjectCheck.Inspect(source);
        if (!valid.SourceRegistrationVerified)
        {
            ed.WriteMessage("\nREV15: Source model is not confirmed as project-registered: " +
                valid.RegistrationStatus);
            return;
        }

        PromptSelectionResult selected = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nREV15: select support entities in the current 3D source model: "
        });
        if (selected.Status != PromptStatus.OK) return;
        var handles = new List<string>();
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selected.Value.GetObjectIds().Distinct())
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is Entity)
                    handles.Add(id.Handle.ToString());
            }
        }
        if (handles.Count == 0) return;

        NativeInfo info;
        try { info = InspectNativeProject(); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV15 native project inspection failed: " + ex.Message);
            return;
        }
        if (info.Add == null || info.Validate == null || info.List == null)
        {
            ed.WriteMessage("\nREV15: Required public Ortho Project APIs are unavailable. No changes.");
            return;
        }

        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "REV15 TEST - select an EMPTY Ortho DWG created using Plant 3D Project Manager (NOT DWT)",
            Filter = "Orthographic Drawing (*.dwg)|*.dwg",
            CheckFileExists = true,
            RestoreDirectory = true,
            InitialDirectory = info.OrthoFolder
        };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        string seed = Path.GetFullPath(dialog.FileName);
        if (!NativeOrthoRegistrationNames.IsInsideProjectDirectory(seed, info.ProjectRoot) ||
            !string.Equals(Path.GetDirectoryName(seed)?.TrimEnd(Path.DirectorySeparatorChar),
                info.OrthoFolder.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            ed.WriteMessage("\nREV15: Seed must be a registered DWG directly inside this project's Orthos folder.");
            return;
        }
        if (!File.Exists(seed) || string.Equals(seed, source, StringComparison.OrdinalIgnoreCase))
        {
            ed.WriteMessage("\nREV15: Selected seed is invalid.");
            return;
        }

        try
        {
            var registered = info.List.Invoke(info.OrthoProject, null) as IEnumerable;
            bool match = false;
            if (registered != null)
            {
                foreach (object? drawing in registered)
                {
                    if (drawing == null) continue;
                    string? file = ReadPublicString(drawing, "FileName") ??
                        ReadPublicString(drawing, "RelativeFilePath") ??
                        ReadPublicString(drawing, "Name");
                    if (string.Equals(Path.GetFileName(file), Path.GetFileName(seed),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }
            }
            if (!match)
            {
                ed.WriteMessage("\nREV15: Seed is not listed in the active project's Ortho project part.");
                return;
            }
            object?[] test = {seed, info.OrthoProject, ""};
            if (info.Validate.Invoke(null, test) is not bool ok || !ok)
            {
                ed.WriteMessage("\nREV15: Native IsValidOrthoDrawing rejected the seed: " + test[2]);
                return;
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV15: Seed verification failed: " + (ex.InnerException?.Message ?? ex.Message));
            return;
        }

        var answer = WinForms.MessageBox.Show(
            "REV15 EXPERIMENTAL NATIVE ORTHO CLONE\n\n" +
            "ACTIVE PROJECT: " + info.ProjectRoot + "\n" +
            "SOURCE MODEL: " + source + "\n" +
            "NATIVE ORTHO SEED: " + seed + "\n\n" +
            "Seed MUST be an EMPTY Ortho DWG made by Project Manager in Plant 3D 2025.\n" +
            "Confirm it works before trying this experiment.\n" +
            "Only run inside a SEPARATE BACKUP/COPIED TEST PROJECT.\n" +
            "This will ADD an Ortho DWG registration and cannot be automatically rolled back safely.\n" +
            "This experiment DOES NOT generate an Ortho view.\n\n" +
            "Are both the project and native Ortho seed suitable for this test?",
            "SPTC REV15 — BACKUP ONLY", WinForms.MessageBoxButtons.YesNo,
            WinForms.MessageBoxIcon.Warning, WinForms.MessageBoxDefaultButton.Button2);
        if (answer != WinForms.DialogResult.Yes) return;

        PromptResult typed = ed.GetString(new PromptStringOptions(
            "\nType NATIVE ORTHO TEST to authorize copied-project registration: ")
        {AllowSpaces = true});
        if (typed.Status != PromptStatus.OK ||
            !string.Equals(typed.StringResult.Trim(), "NATIVE ORTHO TEST", StringComparison.Ordinal))
        {
            ed.WriteMessage("\nREV15: Cancelled. No project changes.");
            return;
        }

        string unique = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
            "_" + Guid.NewGuid().ToString("N")[..6];
        string name = NativeOrthoRegistrationNames.NewOrthoDrawingFileName(source, handles, unique);
        // Rev15 filename identifies seed-clone test, while REV14 naming remains unchanged.
        name = name.Replace("_ORTHO_R14_", "_ORTHO_R15CLONE_", StringComparison.Ordinal);
        string dest = Path.Combine(info.OrthoFolder, name);
        if (File.Exists(dest)) { ed.WriteMessage("\nREV15: Destination already exists; no changes."); return; }
        string report = Path.Combine(info.OrthoFolder,
            Path.GetFileNameWithoutExtension(dest) + "_REV15_RESULT.txt");
        var log = new StringBuilder();
        log.AppendLine("SPTC REV15 - WIZARD-CREATED ORTHO SEED CLONE EXPERIMENT");
        log.AppendLine("UTC: " + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        log.AppendLine("PROJECT: " + info.ProjectRoot);
        log.AppendLine("SOURCE_MODEL: " + source);
        log.AppendLine("SOURCE_HANDLES: " + string.Join(", ", handles));
        log.AppendLine("WIZARD_SEED: " + seed);
        log.AppendLine("NEW_CLONE: " + dest);
        log.AppendLine("AUTOMATIC_2D_VIEW_GENERATED=FALSE");
        bool copied = false, invoked = false;
        try
        {
            // IMPORTANT: use the full DWG database (with its native Plant metadata),
            // NOT the DWT conversion that caused REV14's earlier-version error.
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(seed, FileShare.Read, true, null);
                db.CloseInput(true);
                db.SaveAs(dest, DwgVersion.Current);
            }
            copied = File.Exists(dest);
            if (!copied) throw new IOException("Native seed clone DWG was not created.");
            log.AppendLine("STEP1_CLONE_NATIVE_ORTHO_SEED=PASS");

            // The public Plant 3D API must register a *new* item in the Ortho part.
            // No manual GUID or SQLite mutation. Native Plant manages project state.
            object?[] args = {dest, "", Path.GetFileNameWithoutExtension(dest), false, false};
            invoked = true;
            object? result = info.Add.Invoke(info.OrthoProject, args);
            log.AppendLine("STEP2_ADD_RESULT=" + result);
            log.AppendLine("STEP2_EXISTS=" + args[3]);
            log.AppendLine("STEP2_ILLEGAL_XREFS=" + args[4]);
            if (args[3] is true || args[4] is true)
                throw new InvalidOperationException("Native API rejected clone registration.");
            log.AppendLine("STEP2_ADD_CALLED=TRUE");

            // Capture a second GUID if the native AddPnPDrawingFile API assigned one.
            string regGuid = result?.ToString() ?? "";
            log.AppendLine("REGISTERED_DRAWING_GUID=" + regGuid);
            if (!Guid.TryParse(regGuid, out _))
                log.AppendLine("WARNING=No confirmed newly assigned project drawing GUID.");

            object?[] state = {dest, info.OrthoProject, ""};
            bool acceptable = info.Validate.Invoke(null, state) is bool yes && yes;
            log.AppendLine("STEP3_NATIVE_IS_VALID_ORTHO=" + acceptable);
            log.AppendLine("STEP3_NATIVE_REASON=" + state[2]);
            log.AppendLine("STEP4_MANUAL_PLANTORTHOCREATE_TEST_REQUIRED=TRUE");
            log.AppendLine("FINAL_STATUS=" + (acceptable ?
                "PROJECT_REGISTRATION_VALID_BUT_COMMAND_NOT_YET_TESTED" :
                "NATIVE_VALIDATION_FAILED_INSPECT_PROJECT"));
            ed.WriteMessage("\nREV15: native clone registered; API validated=" + acceptable);
            ed.WriteMessage("\nOPEN THE NEW DRAWING through Project Manager and try New View.");
            ed.WriteMessage("\nNo Ortho view has been generated by this command.");
        }
        catch (TargetInvocationException ex)
        {
            log.AppendLine("ERROR=" + (ex.InnerException ?? ex));
            ed.WriteMessage("\nREV15 native API failed: " + (ex.InnerException?.Message ?? ex.Message));
        }
        catch (System.Exception ex)
        {
            log.AppendLine("ERROR=" + ex);
            ed.WriteMessage("\nREV15 test stopped: " + ex.Message);
        }
        finally
        {
            if (copied && !invoked && File.Exists(dest))
            {
                try { File.Delete(dest); log.AppendLine("CLEANUP=removed unregistered clone"); }
                catch (System.Exception ex) { log.AppendLine("CLEANUP_ERROR=" + ex.Message); }
            }
            try
            {
                File.WriteAllText(report, log.ToString(), new UTF8Encoding(false));
                ed.WriteMessage("\nREV15 RESULT: " + report);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nREV15 could not save report: " + ex.Message);
                ed.WriteMessage("\n" + log);
            }
        }
    }

    private sealed record NativeInfo(string ProjectRoot, string OrthoFolder,
        object OrthoProject, MethodInfo? Add, MethodInfo? Validate, MethodInfo? List);

    private static NativeInfo InspectNativeProject()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Type plant = assemblies.Select(a => a.GetType(
            "Autodesk.ProcessPower.PlantInstance.PlantApplication", false))
            .FirstOrDefault(t => t != null) ??
            throw new InvalidOperationException("PlantApplication is not loaded.");
        object project = plant.GetProperty("CurrentProject", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null) ?? throw new InvalidOperationException("No active Plant 3D project.");
        string root = project.GetType().GetProperty("ProjectFolderPath")?.GetValue(project)?.ToString() ?? "";
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Project path invalid: " + root);
        object parts = project.GetType().GetProperty("ProjectParts")?.GetValue(project) ??
            throw new InvalidOperationException("ProjectParts unavailable.");
        object ortho = parts.GetType().GetProperty("Item")?
            .GetValue(parts, new object[] {"Ortho"}) ??
            throw new InvalidOperationException("No Ortho project part.");
        string folder = ortho.GetType().GetMethod("GetOrthoPath", BindingFlags.Public | BindingFlags.Instance,
            null, Type.EmptyTypes, null)?.Invoke(ortho, null)?.ToString() ?? "";
        if (string.IsNullOrEmpty(folder))
            throw new InvalidOperationException("Native GetOrthoPath returned empty.");
        if (!Path.IsPathRooted(folder)) folder = Path.Combine(root, folder);
        folder = Path.GetFullPath(folder);
        if (!NativeOrthoRegistrationNames.IsInsideProjectDirectory(folder, root) ||
            !Directory.Exists(folder))
            throw new InvalidOperationException("Invalid Ortho destination outside the active project.");

        Type t = ortho.GetType();
        var flags = BindingFlags.Public | BindingFlags.Instance;
        MethodInfo? add = t.GetMethod("AddPnPDrawingFile", flags, null,
            new[] {typeof(string), typeof(string), typeof(string),
                typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType()}, null);
        MethodInfo? list = t.GetMethod("GetPnPDrawingFiles", flags, null, Type.EmptyTypes, null);
        Type util = assemblies.Select(a => a.GetType(
            "Autodesk.ProcessPower.P3dProjectParts.OrthoDocUtils", false))
            .FirstOrDefault(x => x != null) ??
            throw new InvalidOperationException("OrthoDocUtils not loaded.");
        MethodInfo? validate = util.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m =>
        {
            if (m.Name != "IsValidOrthoDrawing" || m.ReturnType != typeof(bool)) return false;
            ParameterInfo[] p = m.GetParameters();
            return p.Length == 3 && p[0].ParameterType == typeof(string) &&
                p[1].ParameterType.IsInstanceOfType(ortho) &&
                p[2].ParameterType == typeof(string).MakeByRefType();
        });
        return new NativeInfo(root, folder, ortho, add, validate, list);
    }

    private static string? ReadPublicString(object target, string name)
    {
        try { return target.GetType().GetProperty(name)?.GetValue(target)?.ToString(); }
        catch { return null; }
    }
}
