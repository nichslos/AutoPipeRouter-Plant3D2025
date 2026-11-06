using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
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
/// REV18 BACKUP-PROJECT-ONLY native public-API TOP view experiment.
/// Uses real PlantOrthoCube.InitializeCube and PUBLIC PlantOrthoView.CreateOrthos.
/// IMPORTANT: the latter can open native drawing dialogs and Editor.Drag,
/// so this does NOT claim unattended placement or invoke private GenerateOrthos.
/// </summary>
public sealed class NativeOrthoTopGenerationTest
{
    [CommandMethod("SPTC_ORTHO18_TOP_API_TEST", CommandFlags.Modal)]
    public void Run()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        string target = Path.GetFullPath(doc.Database.Filename ?? "");
        if (!File.Exists(target) ||
            !Path.GetFileName(target).Contains("_ORTHO_R16CLONE_", StringComparison.OrdinalIgnoreCase))
        {
            ed.WriteMessage("\nREV18 STOP: Open a NEW, EMPTY REV16 seed-clone Ortho DWG from a COPIED Plant 3D project. Do not run on your working verified view.");
            return;
        }
        if (doc.Database.TileMode)
        {
            ed.WriteMessage("\nREV18 STOP: Switch the target Ortho drawing to a paper-space layout before running the test.");
            return;
        }

        using var planDialog = new WinForms.OpenFileDialog
        {
            Title = "REV18: select a CURRENT REV17 TOP_PREFLIGHT JSON for the same source support",
            Filter = "REV17 JSON (*.json)|*.json",
            CheckFileExists = true, RestoreDirectory = true
        };
        if (planDialog.ShowDialog() != WinForms.DialogResult.OK) return;
        string planPath = Path.GetFullPath(planDialog.FileName);

        using var reportDialog = new WinForms.SaveFileDialog
        {
            Title = "REV18: Save diagnostic outside the Plant 3D project",
            FileName = Path.GetFileNameWithoutExtension(target) + "_REV18_TOP_API_RESULT.txt",
            Filter = "Text report (*.txt)|*.txt",
            AddExtension = true, DefaultExt = "txt",
            OverwritePrompt = true
        };
        if (reportDialog.ShowDialog() != WinForms.DialogResult.OK) return;
        string report = Path.GetFullPath(reportDialog.FileName);
        var log = new StringBuilder(5000);
        void Log(string message)
        {
            log.AppendLine(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                " " + message);
            // Persist before entering any native command/constructor:
            // an AutoCAD crash still leaves the last attempted step.
            try { File.WriteAllText(report, log.ToString(), new UTF8Encoding(false)); }
            catch { ed.WriteMessage("\nREV18 could not persist log; see command output."); }
        }

        Log("REV18 BACKUP-ONLY NATIVE PUBLIC TOP VIEW API EXPERIMENT");
        Log("TARGET_ORTHO_DWG=" + target);
        Log("PLAN_JSON=" + planPath);
        Log("PROJECT_DATABASE_NOT_DIRECTLY_EDITED=TRUE");

        try
        {
            using JsonDocument plan = JsonDocument.Parse(File.ReadAllText(planPath));
            JsonElement root = plan.RootElement;
            if (root.GetProperty("Revision").GetString() != "REV17" ||
                root.GetProperty("Mode").GetString() != "NATIVE_TOP_VIEW_READ_ONLY_PREFLIGHT" ||
                root.GetProperty("TargetNativeRegistration").GetString() != "VALID_REGISTERED_ORTHO")
                throw new InvalidOperationException("REV17 plan must be valid and source/target preflight must have passed.");
            string model = root.GetProperty("Source3dModel").GetString() ?? "";
            if (!File.Exists(model) || !NativeOrthoProjectCheck.Inspect(model).SourceRegistrationVerified)
                throw new InvalidOperationException("The report's source model is not a currently registered Piping DWG.");

            var bound = root.GetProperty("CalculatedOrthoCubeBounds");
            double minX = bound.GetProperty("MinX").GetDouble();
            double minY = bound.GetProperty("MinY").GetDouble();
            double minZ = bound.GetProperty("MinZ").GetDouble();
            double maxX = bound.GetProperty("MaxX").GetDouble();
            double maxY = bound.GetProperty("MaxY").GetDouble();
            double maxZ = bound.GetProperty("MaxZ").GetDouble();
            if (!(maxX > minX && maxY > minY && maxZ > minZ) ||
                new[] {minX,minY,minZ,maxX,maxY,maxZ}.Any(x => !double.IsFinite(x)))
                throw new InvalidOperationException("The REV17 OrthoCube has invalid or non-finite bounds.");

            JsonElement candidate = root.GetProperty("CandidateTopView");
            double scale = candidate.GetProperty("CandidateScale").GetDouble();
            if (!(scale > 0 && scale <= 1.0) || !double.IsFinite(scale))
                throw new InvalidOperationException("Invalid TOP projection scale.");
            double paperW = root.GetProperty("SheetWidthMm").GetDouble();
            double paperH = root.GetProperty("SheetHeightMm").GetDouble();
            if (!(paperW > 0 && paperH > 0))
                throw new InvalidOperationException("REV17 did not read actual Ortho paper dimensions.");
            (double currentW, double currentH) = CurrentPaper(doc.Database);
            if (Math.Abs(currentW - paperW) > 1 || Math.Abs(currentH - paperH) > 1)
                throw new InvalidOperationException("Target Ortho sheet dimensions differ from REV17 JSON. Re-run REV17 with this drawing.");

            PlantTarget project = GetTargetPart();
            if (!NativeOrthoRegistrationNames.IsInsideProjectDirectory(target, project.OrthoFolder) ||
                !NativeOrthoRegistrationNames.IsInsideProjectDirectory(target, project.ProjectFolder))
                throw new InvalidOperationException("Active Ortho DWG is outside the active Plant 3D project.");

            string viewStatus = FindViewStatus(project.OrthoProject, target);
            if (viewStatus != "EMPTY_REGISTERED_ORTHO")
                throw new InvalidOperationException("The Ortho target is not verified as EMPTY: " + viewStatus);
            if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(doc.Database.Filename),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active Ortho document changed unexpectedly.");
            Log("SOURCE_MODEL=" + model);
            Log("TARGET_REGISTRATION=" + viewStatus);
            Log("SHEET_MM=" + currentW.ToString("0.###", CultureInfo.InvariantCulture) +
                "x" + currentH.ToString("0.###", CultureInfo.InvariantCulture));
            Log("CUBE_LOWER=" + Coord(minX,minY,minZ));
            Log("CUBE_UPPER=" + Coord(maxX,maxY,maxZ));
            Log("TOP_VIEW_SCALE=" + scale.ToString("0.########", CultureInfo.InvariantCulture));

            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            Type cubeType = FindType(loaded,
                "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoCube") ??
                throw new InvalidOperationException("Native PlantOrthoCube is not loaded.");
            Type viewType = FindType(loaded,
                "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoView") ??
                throw new InvalidOperationException("Native PlantOrthoView is not loaded.");
            object cube = Activator.CreateInstance(cubeType) ??
                throw new InvalidOperationException("Could not construct native PlantOrthoCube.");
            try
            {
                var init = cubeType.GetMethod("InitializeCube",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] {typeof(Extents3d)}, null) ??
                    throw new InvalidOperationException("Public InitializeCube(Extents3d) is absent.");
                var extents = new Extents3d(
                    new Point3d(minX,minY,minZ), new Point3d(maxX,maxY,maxZ));
                Log("STEP1_NATIVE_CUBE_INITIALIZE_ENTERED");
                init.Invoke(cube, new object[] {extents});
                Log("STEP1_NATIVE_CUBE_INITIALIZE_RETURNED");

                PropertyInfo? extProperty = cubeType.GetProperty("Extents",
                    BindingFlags.Public | BindingFlags.Instance);
                if (extProperty?.GetValue(cube) is not Extents3d actual ||
                    !Close(actual.MinPoint.X,minX) || !Close(actual.MinPoint.Y,minY) ||
                    !Close(actual.MinPoint.Z,minZ) || !Close(actual.MaxPoint.X,maxX) ||
                    !Close(actual.MaxPoint.Y,maxY) || !Close(actual.MaxPoint.Z,maxZ))
                    throw new InvalidOperationException("Native OrthoCube extents did not match the REV17 plan.");
                Log("STEP2_NATIVE_CUBE_EXTENTS_MATCHED=TRUE");

                MethodInfo makeCopy = cubeType.GetMethod("MakeCubeCopy",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    Type.EmptyTypes, null) ??
                    throw new InvalidOperationException("Native MakeCubeCopy() is unavailable.");
                Log("STEP3_MAKE_NATIVE_CUBE_SOLID_ENTERED");
                object? solidValue = makeCopy.Invoke(cube, null);
                if (solidValue is not Solid3d solid)
                    throw new InvalidOperationException("Native OrthoCube did not return a Solid3d.");
                using (solid)
                {
                    Log("STEP3_NATIVE_CUBE_SOLID_CREATED=TRUE");
                    ed.WriteMessage("\nREV18: native OrthoCube initialized and verified, with matching 3D bounds.");

                    MethodInfo? create = viewType.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "CreateOrthos" &&
                            m.GetParameters().Length == 13);
                    ConstructorInfo? ctor = viewType.GetConstructor(
                        new[] {typeof(bool), typeof(bool)});
                    if (create == null || ctor == null)
                        throw new InvalidOperationException("The expected native public OrthoView constructor or CreateOrthos API is absent.");

                    Type directionEnum = create.GetParameters()[6].ParameterType;
                    if (!directionEnum.IsEnum ||
                        !Enum.GetNames(directionEnum).Any(x =>
                            string.Equals(x, "Top", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("The installed Ortho ViewType enum has no TOP member.");
                    object top = Enum.Parse(directionEnum, "Top", true);
                    Log("PUBLIC_TOP_API=" + create);
                    Log("PUBLIC_VIEW_CONSTRUCTOR=" + ctor);
                    Log("PUBLIC_API_STILL_INTERACTIVE=TRUE");
                    Log("PRIVATE_GENERATEORTHOS_NOT_USED=TRUE");

                    var answer = WinForms.MessageBox.Show(
                        "REV18 PUBLIC PLANT 3D TOP ORTHO API TEST\n\n" +
                        "Project: " + project.ProjectFolder + "\n" +
                        "Current Ortho: " + target + "\n" +
                        "Source 3D model: " + model + "\n" +
                        "Native cube initialized; scale: 1:" +
                        Math.Round(1.0 / scale).ToString(CultureInfo.InvariantCulture) +
                        "\n\nIMPORTANT: Only continue in a fully COPIED TEST project.\n" +
                        "This method can open native modal drawing-selection dialogs AND\n" +
                        "the interactive viewport-placement jig. You may need to place TOP\n" +
                        "manually. It is NOT fully unattended.\n\n" +
                        "This will attempt to MODIFY the current Ortho DWG.\n" +
                        "Do you want to attempt the public TOP generation API?",
                        "SPTC REV18 - BACKUP-ONLY NATIVE ORTHO API",
                        WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning,
                        WinForms.MessageBoxDefaultButton.Button2);
                    if (answer != WinForms.DialogResult.Yes)
                    {
                        Log("FINAL_STATUS=NATIVE_CUBE_PASS_GENERATION_NOT_AUTHORIZED");
                        ed.WriteMessage("\nREV18: Stopped after native cube verification. No Ortho view generated.");
                        return;
                    }
                    PromptResult confirm = ed.GetString(new PromptStringOptions(
                        "\nType TOP API TEST to authorize the public native call in the COPIED project: ")
                    {AllowSpaces = true});
                    if (confirm.Status != PromptStatus.OK ||
                        !string.Equals(confirm.StringResult.Trim(), "TOP API TEST", StringComparison.Ordinal))
                    {
                        Log("FINAL_STATUS=NO_GENERATION_CONFIRMATION");
                        return;
                    }

                    string name = "SPTC_TOP_R18_" +
                        DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
                    var sources = new StringCollection {model};
                    var args = new object[]
                    {
                        sources, name, scale,
                        maxX-minX, maxY-minY, maxZ-minZ,
                        top, "TopView",
                        new Vector3d(0,0,1), new Vector3d(0,1,0),
                        new Point3d(minX,minY,minZ), new Point3d(maxX,maxY,maxZ), solid
                    };

                    // This constructor may display native Ortho selection UI;
                    // it is NOT assumed to be silent. Preserve diagnostics.
                    Log("STEP4_PUBLIC_PLANTORTHOVIEW_CONSTRUCTOR_ENTERED");
                    object orthoView = ctor.Invoke(new object[] {false, false});
                    Log("STEP4_PUBLIC_PLANTORTHOVIEW_CONSTRUCTOR_RETURNED");
                    try
                    {
                        string current = AcadApp.DocumentManager.MdiActiveDocument?.Database.Filename ?? "";
                        if (!string.Equals(Path.GetFullPath(current), target,
                            StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "Native constructor switched to a different drawing. Stop to protect the expected target.");

                        // Uses ONLY the public SDK method. This can launch Editor.Drag.
                        // Does not invoke non-public GenerateOrthos or simulate clicks.
                        Log("STEP5_PUBLIC_CREATEORTHOS_ENTERED_MAY_REQUIRE_MANUAL_VIEWPORT_PLACEMENT");
                        create.Invoke(orthoView, args);
                        Log("STEP5_PUBLIC_CREATEORTHOS_RETURNED");
                        string after = FindViewStatus(project.OrthoProject, target);
                        Log("POST_CALL_ORTHO_VIEW_CATALOG=" + after);
                        ed.WriteMessage("\nREV18 public API returned. Check the new TOP view in Project Manager and test Update View.");
                    }
                    finally
                    {
                        if (orthoView is IDisposable disp) disp.Dispose();
                    }
                }
            }
            finally
            {
                if (cube is IDisposable disposable) disposable.Dispose();
            }
            Log("FINAL_STATUS=PUBLIC_API_RETURNED_MANUAL_UPDATE_TEST_REQUIRED");
        }
        catch (TargetInvocationException ex)
        {
            Log("EXCEPTION_NATIVE=" + (ex.InnerException?.ToString() ?? ex.ToString()));
            ed.WriteMessage("\nREV18 native API failure: " +
                (ex.InnerException?.Message ?? ex.Message));
        }
        catch (System.Exception ex)
        {
            Log("EXCEPTION=" + ex.ToString());
            ed.WriteMessage("\nREV18 stopped: " + ex.Message);
        }
        ed.WriteMessage("\nREV18 report: " + report);
    }

    private sealed record PlantTarget(string ProjectFolder, string OrthoFolder, object OrthoProject);

    private static PlantTarget GetTargetPart()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        Type plant = FindType(loaded,
            "Autodesk.ProcessPower.PlantInstance.PlantApplication") ??
            throw new InvalidOperationException("PlantApplication missing.");
        object project = plant.GetProperty("CurrentProject",
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
            throw new InvalidOperationException("No active Plant 3D project.");
        string root = project.GetType().GetProperty("ProjectFolderPath")?
            .GetValue(project)?.ToString() ?? "";
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Active project folder is missing.");
        object parts = project.GetType().GetProperty("ProjectParts")?
            .GetValue(project) ?? throw new InvalidOperationException("ProjectParts unavailable.");
        object ortho = parts.GetType().GetProperty("Item")?
            .GetValue(parts, new object[] {"Ortho"}) ??
            throw new InvalidOperationException("Active Ortho project part unavailable.");
        string folder = ortho.GetType().GetMethod("GetOrthoPath",
            BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)?
            .Invoke(ortho, null)?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Native Ortho folder unavailable.");
        if (!Path.IsPathRooted(folder)) folder = Path.Combine(root, folder);
        folder = Path.GetFullPath(folder);
        return new PlantTarget(root, folder, ortho);
    }

    private static string FindViewStatus(object ortho, string target)
    {
        try
        {
            const BindingFlags pub = BindingFlags.Public | BindingFlags.Instance;
            MethodInfo? list = ortho.GetType().GetMethod("GetPnPDrawingFiles",
                pub, null, Type.EmptyTypes, null);
            MethodInfo? views = ortho.GetType().GetMethod("GetAllViewNamesInOrthoDwgByGuid",
                pub, null, new[]
                {
                    typeof(string), typeof(Dictionary<string,string>).MakeByRefType()
                }, null);
            if (list?.Invoke(ortho, null) is not IEnumerable drawings ||
                views == null) return "NO_NATIVE_VIEW_CATALOG_API";

            string guid = "";
            foreach (object? drawing in drawings)
            {
                if (drawing == null) continue;
                string? file = ReadProp(drawing, "FileName") ??
                    ReadProp(drawing, "RelativeFilePath") ?? ReadProp(drawing, "Name");
                if (!string.Equals(Path.GetFileName(file), Path.GetFileName(target),
                    StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string key in new[] {"Guid","PnPDrawingGuid","DrawingGuid"})
                {
                    string? val = ReadProp(drawing,key);
                    if (Guid.TryParse(val, out _)) { guid = val!; break; }
                }
                break;
            }
            if (guid.Length == 0) return "TARGET_NOT_REGISTERED_OR_NO_GUID";
            object?[] args = {guid, new Dictionary<string,string>()};
            views.Invoke(ortho, args);
            if (args[1] is not Dictionary<string,string> names)
                return "VIEW_CATALOG_OUTPUT_UNAVAILABLE";
            return names.Count == 0 ? "EMPTY_REGISTERED_ORTHO" :
                "EXISTING_NATIVE_VIEWS:" + string.Join(",",names.Keys.Take(6));
        }
        catch (System.Exception ex)
        {
            return "VIEW_CATALOG_ERROR:" + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static (double width,double height) CurrentPaper(Database db)
    {
        using Transaction tx = db.TransactionManager.StartTransaction();
        var layouts = (DBDictionary)tx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        Layout? current = null;
        string currentName = LayoutManager.Current.CurrentLayout;
        foreach (DBDictionaryEntry item in layouts)
        {
            if (tx.GetObject(item.Value, OpenMode.ForRead) is Layout lay &&
                lay.LayoutName == currentName) { current = lay; break; }
        }
        if (current == null) throw new InvalidOperationException("Current paper-space layout unavailable.");
        Point2d size = current.PlotPaperSize;
        double conversion = current.PlotPaperUnits.ToString() == "Inches" ? 25.4 : 1;
        return (size.X * conversion, size.Y * conversion);
    }

    private static Type? FindType(IEnumerable<Assembly> assemblies, string name)
    {
        foreach (Assembly assembly in assemblies)
        {
            try { Type? t = assembly.GetType(name,false); if (t != null) return t; }
            catch { /* ignore unavailable Autodesk optional assemblies */ }
        }
        return null;
    }

    private static string? ReadProp(object value, string name)
    {
        try { return value.GetType().GetProperty(name)?.GetValue(value)?.ToString(); }
        catch { return null; }
    }

    private static bool Close(double a, double b) =>
        Math.Abs(a-b) <= Math.Max(1e-4,Math.Abs(b)*1e-7);
    private static string Coord(double x,double y,double z) =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###},{2:0.###})",x,y,z);
}
