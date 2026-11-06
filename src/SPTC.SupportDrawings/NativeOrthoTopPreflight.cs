using System;
using System.Collections;
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
/// REV17 TOP-view bridge preflight. Read-only against the Plant project and DWGs.
/// Examines the source, live selection, valid registered Ortho target and actual
/// paper size, and inspects the exact runtime native Ortho API boundary.
/// Critically, DOES NOT call CreateOrthos, GenerateOrthos or Editor.Drag.
/// </summary>
public sealed class NativeOrthoTopPreflight
{
    [CommandMethod("SPTC_ORTHO17_TOP_PREFLIGHT", CommandFlags.Modal)]
    public void Run()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        string source = doc.Database.Filename ?? "";
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            ed.WriteMessage("\nREV17: Save and open a registered source 3D model.");
            return;
        }
        NativeOrthoProjectCheck.Result sourceCheck = NativeOrthoProjectCheck.Inspect(source);
        if (!sourceCheck.SourceRegistrationVerified)
        {
            ed.WriteMessage("\nREV17: Source drawing not validated: " + sourceCheck.RegistrationStatus);
            return;
        }
        PromptSelectionResult selected = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nREV17: select structural members of ONE support in current model: "
        });
        if (selected.Status != PromptStatus.OK) return;

        var handles = new List<string>();
        var skipped = new List<string>();
        BoundingBox3? bounds = null;
        using (Transaction tx = doc.Database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selected.Value.GetObjectIds().Distinct())
            {
                try
                {
                    if (tx.GetObject(id, OpenMode.ForRead, false) is not Entity member) continue;
                    Extents3d e = member.GeometricExtents;
                    var box = new BoundingBox3(
                        e.MinPoint.X, e.MinPoint.Y, e.MinPoint.Z,
                        e.MaxPoint.X, e.MaxPoint.Y, e.MaxPoint.Z);
                    bounds = bounds.HasValue ? bounds.Value.Include(box) : box;
                    handles.Add(id.Handle.ToString());
                }
                catch (System.Exception ex)
                {
                    skipped.Add(id.Handle + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
        if (!bounds.HasValue || handles.Count == 0 || skipped.Count > 0)
        {
            ed.WriteMessage("\nREV17: All selected support members need readable 3D extents.");
            foreach (string warning in skipped.Take(10)) ed.WriteMessage("\n" + warning);
            return;
        }
        double unit = doc.Database.Insunits.ToString() switch
        {
            "Millimeters" => 1, "Centimeters" => 10, "Meters" => 1000,
            "Inches" => 25.4, "Feet" => 304.8, _ => 0
        };
        if (unit <= 0)
        {
            ed.WriteMessage("\nREV17: Unsupported source model units. No plan generated.");
            return;
        }
        NativeOrthoLayout.Plan geometry;
        try { geometry = NativeOrthoLayout.Build(bounds.Value, unit); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV17: Source bounds invalid: " + ex.Message);
            return;
        }
        OrthoPart part;
        try { part = ResolveOrthoPart(); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV17: Ortho project part unavailable: " + ex.Message);
            return;
        }

        using var filePicker = new WinForms.OpenFileDialog
        {
            Title = "REV17 READ ONLY - select the WORKING, registered Plant 3D native Ortho DWG",
            Filter = "AutoCAD Ortho Drawing (*.dwg)|*.dwg",
            InitialDirectory = Directory.Exists(Path.Combine(part.OrthoRoot, "DWGs"))
                ? Path.Combine(part.OrthoRoot, "DWGs") : part.OrthoRoot,
            CheckFileExists = true, Multiselect = false, RestoreDirectory = true
        };
        if (filePicker.ShowDialog() != WinForms.DialogResult.OK) return;
        string target = Path.GetFullPath(filePicker.FileName);
        if (!NativeOrthoRegistrationNames.IsInsideProjectDirectory(target, part.ProjectRoot) ||
            !NativeOrthoRegistrationNames.IsInsideProjectDirectory(target, part.OrthoRoot))
        {
            ed.WriteMessage("\nREV17: Selected DWG is outside the registered Ortho directory.");
            return;
        }

        string targetStatus = ValidateExistingOrtho(part.OrthoProject, target);
        if (targetStatus != "VALID_REGISTERED_ORTHO")
        {
            ed.WriteMessage("\nREV17: Target is not a verified project Ortho: " + targetStatus);
            return;
        }

        string sheetStatus;
        double paperWidthMm = 0, paperHeightMm = 0;
        string paperLayoutName = "";
        try
        {
            (paperWidthMm, paperHeightMm, paperLayoutName) = ReadPaper(target);
            sheetStatus = "READ_FROM_NATIVE_ORTHO_DWG";
        }
        catch (System.Exception ex)
        {
            sheetStatus = "PAPER_SIZE_UNAVAILABLE: " + ex.Message;
        }

        NativeOrthoTopViewPlan.TopPlan? proposed = null;
        if (paperWidthMm > 0 && paperHeightMm > 0)
        {
            try { proposed = NativeOrthoTopViewPlan.Build(geometry, paperWidthMm, paperHeightMm); }
            catch (System.Exception ex) { sheetStatus = "PAPER_LAYOUT_INVALID: " + ex.Message; }
        }

        var api = ProbeNativeApi();
        string proposedName = NativeOrthoLayout.SuggestedPlanFileName(source, handles, "REV17")
            .Replace("ORTHO_AUTOPLAN", "TOP_PREFLIGHT", StringComparison.Ordinal);
        using var save = new WinForms.SaveFileDialog
        {
            Title = "REV17 - Save read-only native TOP Ortho API and placement report",
            Filter = "JSON report (*.json)|*.json",
            FileName = proposedName,
            AddExtension = true, DefaultExt = "json", OverwritePrompt = true
        };
        if (save.ShowDialog() != WinForms.DialogResult.OK) return;

        var payload = new
        {
            Revision = "REV17",
            Mode = "NATIVE_TOP_VIEW_READ_ONLY_PREFLIGHT",
            Source3dModel = source,
            SourceRegistrationVerified = sourceCheck.SourceRegistrationVerified,
            SelectedSupportHandles = handles,
            OriginalSupportBounds = geometry.OriginalBounds,
            CalculatedOrthoCubeBounds = geometry.PaddedCube,
            OrthoCubePerSideMarginSourceUnits = new
            {
                X = geometry.PaddingX, Y = geometry.PaddingY, Z = geometry.PaddingZ
            },
            TargetNativeOrthoDwg = target,
            TargetNativeRegistration = targetStatus,
            SheetStatus = sheetStatus,
            SheetLayout = paperLayoutName,
            SheetWidthMm = paperWidthMm,
            SheetHeightMm = paperHeightMm,
            TopViewDirection = new[] {0, 0, 1},
            TopViewUp = new[] {0, 1, 0},
            CandidateTopView = proposed,
            NativeApi = api,
            NativeOrthoCubeActuallyInitialized = false,
            NativeTopViewGenerated = false,
            ProjectModified = false,
            RemainingBlocker = "Public PlantOrthoView.CreateOrthos calls ProcessViewportPlacement/Editor.Drag; no verified unattended placement API was found. Unsupported private GenerateOrthos is NOT invoked.",
            NextSafeIntegration = "Confirm this report shows a registered working Ortho target and its correct paper size. Build a guarded native session adapter only when viewport placement can be invoked safely with a fixed insertion point."
        };
        try
        {
            string json = JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(save.FileName, json, new UTF8Encoding(false));
            ed.WriteMessage("\nREV17 TOP native preflight saved: " + save.FileName);
            ed.WriteMessage("\nCurrent model: " + Path.GetFileName(source));
            ed.WriteMessage("\nRegistered Ortho target: " + Path.GetFileName(target));
            ed.WriteMessage("\nNative drawing and selection unchanged.");
            if (proposed != null)
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\nProposed TOP 1:{0} at paper ({1:0.##},{2:0.##}) mm; placement is NOT yet automatic.",
                    proposed.CandidateScaleDenominator, proposed.PaperCenterX, proposed.PaperCenterY));
            else
                ed.WriteMessage("\nNo verified sheet size found; review the report.");
        }
        catch (System.Exception ex) { ed.WriteMessage("\nREV17 report failure: " + ex); }
    }

    private sealed record OrthoPart(string ProjectRoot, string OrthoRoot, object OrthoProject);

    private static OrthoPart ResolveOrthoPart()
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        Type plant = loaded.Select(a => a.GetType(
            "Autodesk.ProcessPower.PlantInstance.PlantApplication", false))
            .FirstOrDefault(t => t != null) ??
            throw new InvalidOperationException("Native PlantApplication is unavailable.");
        object project = plant.GetProperty("CurrentProject", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null) ?? throw new InvalidOperationException("No active project.");
        string root = project.GetType().GetProperty("ProjectFolderPath")?.GetValue(project)?
            .ToString() ?? "";
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Project folder not found.");
        object parts = project.GetType().GetProperty("ProjectParts")?.GetValue(project) ??
            throw new InvalidOperationException("ProjectParts unavailable.");
        object ortho = parts.GetType().GetProperty("Item")?.GetValue(parts, new object[] {"Ortho"}) ??
            throw new InvalidOperationException("Native Ortho project part unavailable.");
        MethodInfo? getFolder = ortho.GetType().GetMethod("GetOrthoPath",
            BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        string folder = getFolder?.Invoke(ortho, null)?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Ortho folder not found.");
        if (!Path.IsPathRooted(folder)) folder = Path.Combine(root, folder);
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder) ||
            !NativeOrthoRegistrationNames.IsInsideProjectDirectory(folder, root))
            throw new InvalidOperationException("Native Ortho folder is not inside current project.");
        return new OrthoPart(root, folder, ortho);
    }

    private static string ValidateExistingOrtho(object ortho, string target)
    {
        try
        {
            MethodInfo? list = ortho.GetType().GetMethod("GetPnPDrawingFiles",
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (list?.Invoke(ortho, null) is not IEnumerable files)
                return "NO_PROJECT_DRAWING_LIST";
            bool registered = false;
            foreach (object? file in files)
            {
                if (file == null) continue;
                string? name = GetString(file, "FileName") ??
                    GetString(file, "RelativeFilePath") ?? GetString(file, "Name");
                if (string.Equals(Path.GetFileName(name), Path.GetFileName(target),
                    StringComparison.OrdinalIgnoreCase))
                {
                    registered = true;
                    break;
                }
            }
            if (!registered) return "NOT_FOUND_IN_PROJECT_ORTHO_DRAWINGS";
            Type util = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Autodesk.ProcessPower.P3dProjectParts.OrthoDocUtils", false))
                .FirstOrDefault(x => x != null) ??
                throw new InvalidOperationException("OrthoDocUtils not loaded.");
            MethodInfo? validation = util.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "IsValidOrthoDrawing" || m.ReturnType != typeof(bool)) return false;
                    ParameterInfo[] ps = m.GetParameters();
                    return ps.Length == 3 && ps[0].ParameterType == typeof(string) &&
                        ps[1].ParameterType.IsInstanceOfType(ortho) &&
                        ps[2].ParameterType == typeof(string).MakeByRefType();
                });
            if (validation == null) return "NO_NATIVE_VALIDATION_API";
            object?[] pars = {target, ortho, ""};
            return validation.Invoke(null, pars) is bool valid && valid ?
                "VALID_REGISTERED_ORTHO" : "NATIVE_REJECTED_ORTHO: " + pars[2];
        }
        catch (System.Exception ex)
        {
            return "NATIVE_ORTHO_VALIDATION_ERROR: " + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static string? GetString(object target, string prop)
    {
        try { return target.GetType().GetProperty(prop)?.GetValue(target)?.ToString(); }
        catch { return null; }
    }

    private static (double w, double h, string name) ReadPaper(string filename)
    {
        using var db = new Database(false, true);
        db.ReadDwgFile(filename, FileShare.ReadWrite, true, null);
        db.CloseInput(true);
        using Transaction tx = db.TransactionManager.StartTransaction();
        var dictionary = (DBDictionary)tx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        double bestArea = 0, width = 0, height = 0;
        string bestLayout = "";
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (tx.GetObject(entry.Value, OpenMode.ForRead) is not Layout layout ||
                string.Equals(layout.LayoutName, "Model", StringComparison.OrdinalIgnoreCase))
                continue;
            Point2d paper = layout.PlotPaperSize;
            double unitScale = string.Equals(layout.PlotPaperUnits.ToString(), "Inches",
                StringComparison.OrdinalIgnoreCase) ? 25.4 : 1.0;
            double w = paper.X * unitScale, h = paper.Y * unitScale;
            if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0) continue;
            double area = w * h;
            if (area > bestArea)
            {
                bestArea = area;
                width = w;
                height = h;
                bestLayout = layout.LayoutName;
            }
        }
        if (bestArea <= 0) throw new InvalidOperationException("No configured paper-space plot layout found.");
        return (width, height, bestLayout);
    }

    private static object ProbeNativeApi()
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        Type? typeView = loaded.Select(a => a.GetType(
            "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoView", false))
            .FirstOrDefault(t => t != null);
        Type? typeCube = loaded.Select(a => a.GetType(
            "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoCube", false))
            .FirstOrDefault(t => t != null);
        const BindingFlags pub = BindingFlags.Public | BindingFlags.Instance;
        const BindingFlags non = BindingFlags.NonPublic | BindingFlags.Instance;

        MethodInfo[] create = typeView?.GetMethods(pub)
            .Where(m => m.Name == "CreateOrthos").ToArray() ?? Array.Empty<MethodInfo>();
        MethodInfo[] generate = typeView?.GetMethods(non)
            .Where(m => m.Name == "GenerateOrthos").ToArray() ?? Array.Empty<MethodInfo>();
        MethodInfo[] cubeInit = typeCube?.GetMethods(pub)
            .Where(m => m.Name == "InitializeCube").ToArray() ?? Array.Empty<MethodInfo>();

        return new
        {
            ViewType = typeView?.AssemblyQualifiedName ?? "NOT_LOADED",
            CubeType = typeCube?.AssemblyQualifiedName ?? "NOT_LOADED",
            ViewConstructors = typeView?.GetConstructors()
                .Select(c => c.ToString()).ToArray() ?? Array.Empty<string>(),
            CubeConstructors = typeCube?.GetConstructors()
                .Select(c => c.ToString()).ToArray() ?? Array.Empty<string>(),
            PublicCreateOrthos = create.Select(m => m.ToString()).ToArray(),
            NonPublicGenerateOrthos = generate.Select(m => m.ToString()).ToArray(),
            PublicInitializeCube = cubeInit.Select(m => m.ToString()).ToArray(),
            ExecutedCreateOrthos = false,
            ExecutedPrivateGenerateOrthos = false,
            UnattendedViewportPlacementVerified = false
        };
    }
}
