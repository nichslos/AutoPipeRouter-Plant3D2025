using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV19 isolated one-command native TOP experiment: select the live support in a
/// registered 3D source; clone a verified empty native Ortho Project Manager seed;
/// register, open, initialize the actual native OrthoCube; call only public
/// PlantOrthoView.CreateOrthos to generate a model-linked TOP view.
/// BACKUP/COPIED PROJECT ONLY. It does NOT patch Ortho.dcf or call private APIs.
/// Any Autodesk dialogs from the public API remain host-controlled.
/// </summary>
public sealed class NativeOrthoTopOneCommandTest
{
    [CommandMethod("SPTC_ORTHO19_TOP_ONECMD_TEST",
        CommandFlags.Modal | CommandFlags.Session)]
    public async void Run()
    {
        Document? sourceDoc = AcadApp.DocumentManager.MdiActiveDocument;
        if (sourceDoc == null) return;
        Editor ed = sourceDoc.Editor;
        string source = Path.GetFullPath(sourceDoc.Database.Filename ?? "");
        if (!File.Exists(source))
        {
            ed.WriteMessage("\nREV19: Open a saved project-registered 3D model before running the test.");
            return;
        }

        NativeOrthoProjectCheck.Result sourceCheck;
        try { sourceCheck = NativeOrthoProjectCheck.Inspect(source); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV19: Native source check failed: " + ex.Message);
            return;
        }
        if (!sourceCheck.SourceRegistrationVerified)
        {
            ed.WriteMessage("\nREV19: Source 3D drawing is not verified project-registered. No changes.");
            return;
        }

        double units = sourceDoc.Database.Insunits.ToString() switch
        {
            "Millimeters" => 1, "Centimeters" => 10, "Meters" => 1000,
            "Inches" => 25.4, "Feet" => 304.8, _ => 0
        };
        if (units <= 0)
        {
            ed.WriteMessage("\nREV19: Unsupported source units; expected a physical unit like Millimeters.");
            return;
        }

        PromptSelectionResult selection = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nREV19: Select all structure entities for ONE support: "
        });
        if (selection.Status != PromptStatus.OK) return;

        var handles = new List<string>();
        BoundingBox3? geometry = null;
        int invalidExtents = 0;
        using (Transaction tr = sourceDoc.Database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selection.Value.GetObjectIds().Distinct())
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity e) continue;
                    Extents3d b = e.GeometricExtents;
                    var current = new BoundingBox3(b.MinPoint.X, b.MinPoint.Y,
                        b.MinPoint.Z, b.MaxPoint.X, b.MaxPoint.Y, b.MaxPoint.Z);
                    geometry = geometry.HasValue ? geometry.Value.Include(current) : current;
                    handles.Add(id.Handle.ToString());
                }
                catch { invalidExtents++; }
            }
        }
        if (!geometry.HasValue || handles.Count == 0 || invalidExtents > 0)
        {
            ed.WriteMessage("\nREV19: All selected members must have valid 3D extents; invalid: " + invalidExtents);
            return;
        }

        NativeOrthoLayout.Plan layout;
        try { layout = NativeOrthoLayout.Build(geometry.Value, units); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV19: Invalid support OrthoCube: " + ex.Message);
            return;
        }
        ProjectPart part;
        try { part = CurrentOrthoProject(); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV19: Cannot resolve current Plant Ortho project: " + ex.Message);
            return;
        }

        string initial = Directory.Exists(Path.Combine(part.OrthoFolder, "DWGs")) ?
            Path.Combine(part.OrthoFolder, "DWGs") : part.OrthoFolder;
        using var picker = new WinForms.OpenFileDialog
        {
            Title = "REV19 - Select an EMPTY Project Manager-created native Ortho seed (not DWT)",
            Filter = "AutoCAD drawing (*.dwg)|*.dwg", CheckFileExists = true,
            InitialDirectory = initial, RestoreDirectory = true
        };
        if (picker.ShowDialog() != WinForms.DialogResult.OK) return;
        string seed = Path.GetFullPath(picker.FileName);
        if (!NativeOrthoRegistrationNames.IsInsideProjectDirectory(seed, part.ProjectRoot) ||
            !NativeOrthoRegistrationNames.IsInsideProjectDirectory(seed, part.OrthoFolder))
        {
            ed.WriteMessage("\nREV19: Seed must be within the active project's registered Orthos folder.");
            return;
        }
        if (!IsNativeOrthoValid(part.OrthoPart, seed))
        {
            ed.WriteMessage("\nREV19: Seed failed native OrthoDocUtils.IsValidOrthoDrawing.");
            return;
        }
        string seedState = ViewCatalog(part.OrthoPart, seed);
        if (seedState != "EMPTY_REGISTERED_ORTHO")
        {
            ed.WriteMessage("\nREV19: Seed must be registered and EMPTY; found: " + seedState);
            return;
        }

        string suffix = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..6];
        string file = NativeOrthoRegistrationNames.NewOrthoDrawingFileName(source, handles, suffix)
            .Replace("_ORTHO_R14_", "_ORTHO_R19AUTO_", StringComparison.Ordinal);
        string dest = Path.Combine(Path.GetDirectoryName(seed)!, file);
        if (File.Exists(dest))
        {
            ed.WriteMessage("\nREV19: Destination DWG exists. No changes.");
            return;
        }

        // The public SDK, not this command, decides the final viewport insertion.
        // No false claim that the proposed TOP center is passed into CreateOrthos.
        var popup = WinForms.MessageBox.Show(
            "REV19 - AUTOMATIC NATIVE TOP EXPERIMENT\n\n" +
            "SOURCE 3D: " + source + "\n" +
            "SELECTED MEMBERS: " + string.Join(", ", handles) + "\n" +
            "NATIVE EMPTY SEED: " + seed + "\n" +
            "NEW NATIVE ORTHO: " + dest + "\n\n" +
            "A NEW drawing will be created and registered to the ACTIVE Plant project.\n" +
            "Then the PUBLIC CreateOrthos API will be attempted, using the calculated\n" +
            "TOP OrthoCube. REV18 succeeded without a viewport click in this environment.\n" +
            "Plant 3D may still show native dialogs or reject a document switch.\n\n" +
            "ONLY authorize in a separate COPIED/BACKED-UP TEST PROJECT.\n" +
            "Do you want to run this experiment?",
            "SPTC REV19 - COPIED TEST PROJECT ONLY",
            WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning,
            WinForms.MessageBoxDefaultButton.Button2);
        if (popup != WinForms.DialogResult.Yes) return;
        PromptResult typed = ed.GetString(new PromptStringOptions(
            "\nType REV19 TEST to register a NEW Ortho DWG in this copied project: ")
        { AllowSpaces = true });
        if (typed.Status != PromptStatus.OK ||
            !string.Equals(typed.StringResult.Trim(), "REV19 TEST", StringComparison.Ordinal))
        {
            ed.WriteMessage("\nREV19: Cancelled before any project write.");
            return;
        }

        string report = Path.Combine(Path.GetDirectoryName(dest)!,
            Path.GetFileNameWithoutExtension(dest) + "_REV19_RESULT.txt");
        var log = new StringBuilder(6000);
        void Log(string msg)
        {
            log.AppendLine(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                " " + msg);
            try { File.WriteAllText(report, log.ToString(), new UTF8Encoding(false)); }
            catch { /* command window is still available for diagnostics */ }
        }

        bool cloned = false, registrationAttempted = false;
        Document? targetDoc = null;
        try
        {
            Log("SPTC_REV19_ONE_COMMAND_NATIVE_TOP_TEST");
            Log("PROJECT=" + part.ProjectRoot);
            Log("SOURCE_MODEL=" + source);
            Log("SOURCE_HANDLES=" + string.Join(",", handles));
            Log("EMPTY_NATIVE_SEED=" + seed);
            Log("NEW_CLONE=" + dest);
            Log("NATIVE_CUBE_MIN=" + Point(layout.PaddedCube.MinX,
                layout.PaddedCube.MinY,layout.PaddedCube.MinZ));
            Log("NATIVE_CUBE_MAX=" + Point(layout.PaddedCube.MaxX,
                layout.PaddedCube.MaxY,layout.PaddedCube.MaxZ));
            Log("UNATTENDED_VIEWPORT_PLACEMENT_EXPECTED_FROM_REV18=TRUE");
            Log("PUBLIC_API_ONLY=TRUE");

            // REV16-proven native DWG seed clone; never clone a DWT.
            using (var db = new Database(false, true))
            {
                // REV19.1: The FileShare overload can throw eFileSharingViolation
                // when the native Project Manager Ortho seed is open in Plant 3D.
                // Autodesk recommends the FileOpenMode API for shared read access.
                // CloseInput materializes the lazy load before the SaveAs clone.
                db.ReadDwgFile(seed, FileOpenMode.OpenForReadAndAllShare, true, null);
                db.CloseInput(true);
                db.SaveAs(dest, DwgVersion.Current);
            }
            cloned = File.Exists(dest);
            if (!cloned) throw new IOException("Native seed clone did not create DWG.");
            Log("STEP1_NATIVE_SEED_CLONED=PASS");

            MethodInfo add = part.OrthoPart.GetType().GetMethod("AddPnPDrawingFile",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] {typeof(string),typeof(string),typeof(string),
                    typeof(bool).MakeByRefType(),typeof(bool).MakeByRefType()}, null) ??
                throw new MissingMethodException("OrthoProject.AddPnPDrawingFile is unavailable.");
            object?[] addArgs = { dest, "", Path.GetFileNameWithoutExtension(dest), false, false };
            registrationAttempted = true;
            Log("STEP2_NATIVE_ADD_PNP_DRAWING_ENTERED");
            object? guid = add.Invoke(part.OrthoPart, addArgs);
            Log("STEP2_ADD_RETURN=" + guid);
            Log("STEP2_ALREADY_EXISTS=" + addArgs[3]);
            Log("STEP2_ILLEGAL_XREFS=" + addArgs[4]);
            if (!Guid.TryParse(guid?.ToString(), out _) ||
                addArgs[3] is true || addArgs[4] is true)
                throw new InvalidOperationException("Native Ortho project registration did not return a new valid drawing GUID.");
            if (!IsNativeOrthoValid(part.OrthoPart, dest))
                throw new InvalidOperationException("Native registration returned a GUID, but OrthoDocUtils rejected new DWG.");
            if (ViewCatalog(part.OrthoPart, dest) != "EMPTY_REGISTERED_ORTHO")
                throw new InvalidOperationException("New DWG is not proven empty after native registration.");
            Log("STEP2_PROJECT_REGISTERED_EMPTY_ORTHO=PASS");

            // Session command can switch AutoCAD documents. Do not close or
            // edit the source 3D model; retain it for native model linking.
            Log("STEP3_DOCUMENT_OPEN_ENTERED");
            targetDoc = AcadApp.DocumentManager.Open(dest, false);
            if (targetDoc == null)
                throw new InvalidOperationException("AutoCAD returned no document for new native Ortho DWG.");
            AcadApp.DocumentManager.MdiActiveDocument = targetDoc;
            string active = AcadApp.DocumentManager.MdiActiveDocument?.Database.Filename ?? "";
            if (!string.Equals(Path.GetFullPath(active), Path.GetFullPath(dest),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Document switching did not activate new Ortho DWG.");
            Log("STEP3_ORTHO_DOCUMENT_ACTIVE=PASS");

            // Find an actual configured paper-space layout from the cloned seed.
            // Layout changes are managed in the command's active session context.
            string paperLayout = FindPaperLayout(targetDoc.Database);
            if (string.IsNullOrWhiteSpace(paperLayout))
                throw new InvalidOperationException("No valid paper-space layout in native Ortho DWG.");
            if (!string.Equals(LayoutManager.Current.CurrentLayout, paperLayout,
                StringComparison.OrdinalIgnoreCase))
            {
                using (DocumentLock locked = targetDoc.LockDocument())
                    LayoutManager.Current.CurrentLayout = paperLayout;
            }
            if (targetDoc.Database.TileMode)
                throw new InvalidOperationException("AutoCAD remains in model space after paper layout selection.");
            (double paperWidth, double paperHeight) = CurrentPaper(targetDoc.Database, paperLayout);
            NativeOrthoTopViewPlan.TopPlan candidate =
                NativeOrthoTopViewPlan.Build(layout, paperWidth, paperHeight);
            Log("STEP4_SHEET_LAYOUT=" + paperLayout);
            Log("STEP4_SHEET_MM=" + Point(paperWidth,paperHeight,0));
            Log("STEP4_TOP_SCALE=1:" + candidate.CandidateScaleDenominator);
            Log("PROPOSED_CENTER_MM=" + candidate.PaperCenterX.ToString("0.###",
                CultureInfo.InvariantCulture) + "," +
                candidate.PaperCenterY.ToString("0.###", CultureInfo.InvariantCulture));
            Log("PUBLIC_API_HAS_NO_INSERTION_POINT_ARGUMENT=TRUE");

            // REV19.3: A Session command can activate a different DWG without
            // entering that drawing's document/command execution context.
            // REV19.2 yielded sentinel +/-1e20 Extents after InitializeCube,
            // whereas REV18 succeeded when invoked within the Ortho document.
            // Schedule all native cube + view generation in target document
            // context, preserving the exact strict extents validation.
            Log("STEP5_SESSION_CONTEXT_IS_APPLICATION=" +
                AcadApp.DocumentManager.IsApplicationContext);
            Log("STEP5_TARGET_COMMAND_CONTEXT_REQUESTED");
            await AcadApp.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    Log("STEP5_TARGET_COMMAND_CONTEXT_ENTERED");
                    bool appContext = AcadApp.DocumentManager.IsApplicationContext;
                    bool activeMatches = ReferenceEquals(
                        AcadApp.DocumentManager.MdiActiveDocument, targetDoc);
                    bool workingMatches = ReferenceEquals(
                        HostApplicationServices.WorkingDatabase, targetDoc.Database);
                    Log("STEP5_TARGET_CONTEXT_IS_APPLICATION=" + appContext);
                    Log("STEP5_TARGET_CONTEXT_ACTIVE_DOC_MATCH=" + activeMatches);
                    Log("STEP5_TARGET_CONTEXT_WORKING_DB_MATCH=" + workingMatches);
                    if (appContext || !activeMatches || !workingMatches)
                        throw new InvalidOperationException(
                            "Native Ortho requires the opened target drawing's " +
                            "document command context and matching working database. " +
                            "See REV19.3 STEP5 context diagnostics.");

            // REV18-verified native OrthoCube and native Solid3d creation.
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            Type cubeClass = FindType(loaded,
                "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoCube") ??
                throw new InvalidOperationException("PlantOrthoCube type has not been loaded by Plant 3D.");
            Type viewClass = FindType(loaded,
                "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoView") ??
                throw new InvalidOperationException("PlantOrthoView type has not been loaded by Plant 3D.");
            object cube = Activator.CreateInstance(cubeClass) ??
                throw new InvalidOperationException("Could not construct native PlantOrthoCube.");
            try
            {
                MethodInfo init = cubeClass.GetMethod("InitializeCube",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] {typeof(Extents3d)}, null) ??
                    throw new MissingMethodException("PlantOrthoCube.InitializeCube(Extents3d)");
                var bounds = layout.PaddedCube;
                var expected = new Extents3d(
                    new Point3d(bounds.MinX,bounds.MinY,bounds.MinZ),
                    new Point3d(bounds.MaxX,bounds.MaxY,bounds.MaxZ));
                // REV19.2: native OrthoCube may change the requested extents.
                // Log both native and requested bounds before deciding whether it
                // remains safe to generate a model-linked TOP view. Do not bypass
                // this geometry validation until actual native values are reviewed.
                Log("STEP5_EXPECTED_CUBE_MIN=" +
                    Point(bounds.MinX,bounds.MinY,bounds.MinZ));
                Log("STEP5_EXPECTED_CUBE_MAX=" +
                    Point(bounds.MaxX,bounds.MaxY,bounds.MaxZ));
                Log("STEP5_NATIVE_CUBE_INITIALIZE_ENTERED");
                init.Invoke(cube, new object[] {expected});
                Log("STEP5_NATIVE_CUBE_INITIALIZE_RETURNED");
                object? extentsValue = cubeClass.GetProperty("Extents")?.GetValue(cube);
                if (extentsValue is not Extents3d got)
                {
                    Log("STEP5_ACTUAL_CUBE_EXTENTS=UNAVAILABLE");
                    throw new InvalidOperationException(
                        "Native OrthoCube.Extents did not return an Extents3d.");
                }
                Log("STEP5_ACTUAL_CUBE_MIN=" +
                    Point(got.MinPoint.X,got.MinPoint.Y,got.MinPoint.Z));
                Log("STEP5_ACTUAL_CUBE_MAX=" +
                    Point(got.MaxPoint.X,got.MaxPoint.Y,got.MaxPoint.Z));
                Log("STEP5_CUBE_MIN_DELTA=" +
                    Point(got.MinPoint.X-bounds.MinX,
                          got.MinPoint.Y-bounds.MinY,
                          got.MinPoint.Z-bounds.MinZ));
                Log("STEP5_CUBE_MAX_DELTA=" +
                    Point(got.MaxPoint.X-bounds.MaxX,
                          got.MaxPoint.Y-bounds.MaxY,
                          got.MaxPoint.Z-bounds.MaxZ));
                bool cubeMatches =
                    Near(got.MinPoint.X,bounds.MinX) &&
                    Near(got.MinPoint.Y,bounds.MinY) &&
                    Near(got.MinPoint.Z,bounds.MinZ) &&
                    Near(got.MaxPoint.X,bounds.MaxX) &&
                    Near(got.MaxPoint.Y,bounds.MaxY) &&
                    Near(got.MaxPoint.Z,bounds.MaxZ);
                Log("STEP5_NATIVE_CUBE_EXTENTS_MATCHED=" + cubeMatches);
                if (!cubeMatches)
                    throw new InvalidOperationException(
                        "Native OrthoCube differs from requested support bounds. See STEP5 actual and delta coordinates in REV19 log.");
                Log("STEP5_NATIVE_CUBE_INITIALIZED_AND_VALIDATED=PASS");

                MethodInfo copy = cubeClass.GetMethod("MakeCubeCopy",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    Type.EmptyTypes, null) ??
                    throw new MissingMethodException("PlantOrthoCube.MakeCubeCopy");
                if (copy.Invoke(cube, null) is not Solid3d solid)
                    throw new InvalidOperationException("Native OrthoCube did not produce Solid3d.");
                using (solid)
                {
                    Log("STEP6_NATIVE_CUBE_SOLID=PASS");
                    if (AcadApp.DocumentManager.MdiActiveDocument != targetDoc)
                        throw new InvalidOperationException("Active document changed before native CreateOrthos.");
                    if (ViewCatalog(part.OrthoPart, dest) != "EMPTY_REGISTERED_ORTHO")
                        throw new InvalidOperationException("Another native view appeared before generation. Refusing duplicate.");
                    MethodInfo create = viewClass.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "CreateOrthos" &&
                            m.GetParameters().Length == 13) ??
                        throw new MissingMethodException("Public PlantOrthoView.CreateOrthos(13 args)");
                    ConstructorInfo ctor = viewClass.GetConstructor(
                        new[] {typeof(bool),typeof(bool)}) ??
                        throw new MissingMethodException("Public PlantOrthoView(bool,bool)");
                    Type typeOfView = create.GetParameters()[6].ParameterType;
                    if (!typeOfView.IsEnum || !Enum.GetNames(typeOfView)
                        .Any(n => string.Equals(n,"Top",StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Public Ortho ViewType has no TOP value.");
                    object top = Enum.Parse(typeOfView, "Top", true);
                    string viewName = "SPTC_TOP_R19_" +
                        DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
                    var sources = new StringCollection {source};
                    object[] args =
                    {
                        sources, viewName, candidate.CandidateScale,
                        bounds.SizeX,bounds.SizeY,bounds.SizeZ,top,"TopView",
                        new Vector3d(0,0,1),new Vector3d(0,1,0),
                        new Point3d(bounds.MinX,bounds.MinY,bounds.MinZ),
                        new Point3d(bounds.MaxX,bounds.MaxY,bounds.MaxZ),solid
                    };

                    // Real observed REV18 result: no viewport placement click, but
                    // this may differ by template/project/session. Log separately.
                    Log("STEP7_PLANTORTHOVIEW_CTOR_ENTERED");
                    object nativeView = ctor.Invoke(new object[] {false,false});
                    Log("STEP7_PLANTORTHOVIEW_CTOR_RETURNED");
                    try
                    {
                        if (!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, targetDoc))
                            throw new InvalidOperationException("Native constructor selected an unexpected drawing. Generation cancelled.");
                        Log("STEP8_PUBLIC_CREATEORTHOS_ENTERED");
                        create.Invoke(nativeView,args);
                        Log("STEP8_PUBLIC_CREATEORTHOS_RETURNED");
                    }
                    finally
                    {
                        if (nativeView is IDisposable disposable) disposable.Dispose();
                    }
                    string actualViews = ViewCatalog(part.OrthoPart, dest);
                    Log("STEP9_NATIVE_VIEW_CATALOG=" + actualViews);
                    Log("NATIVE_ORTHO_VIEW_CREATED=" +
                        actualViews.StartsWith("EXISTING_NATIVE_VIEWS:",StringComparison.Ordinal));
                    Log("UPDATE_VIEW_MANUAL_TEST_REQUIRED=TRUE");
                    Log("FINAL_STATUS=TOP_API_RETURNED_CHECK_UPDATE_AND_GEOMETRY");
                    SafeWriteToActiveEditor("REV19: TOP native API returned. Inspect sheet, SAVE and test Update View.");
                }
            }
            finally { if (cube is IDisposable disposeCube) disposeCube.Dispose(); }

                }
                catch (TargetInvocationException ex)
                {
                    Log("NATIVE_DOCUMENT_CONTEXT_EXCEPTION=" +
                        (ex.InnerException?.ToString() ?? ex.ToString()));
                    SafeWriteToActiveEditor("REV19 native document-context API error: " +
                        (ex.InnerException?.Message ?? ex.Message));
                }
                catch (System.Exception ex)
                {
                    Log("NATIVE_DOCUMENT_CONTEXT_ERROR=" + ex);
                    SafeWriteToActiveEditor("REV19 native document-context error: " +
                        ex.Message);
                }
                return Task.CompletedTask;
            }, null);
            Log("STEP5_TARGET_COMMAND_CONTEXT_COMPLETED");
        }
        catch (TargetInvocationException ex)
        {
            Log("NATIVE_EXCEPTION=" + (ex.InnerException?.ToString() ?? ex.ToString()));
            SafeWriteToActiveEditor("REV19 native API error: " + (ex.InnerException?.Message ?? ex.Message));
        }
        catch (System.Exception ex)
        {
            Log("ERROR=" + ex);
            SafeWriteToActiveEditor("REV19 stopped: " + ex.Message);
        }
        finally
        {
            // Only a clone not offered to the native registration API is safe to
            // delete. Never silently delete an API-registered Ortho drawing.
            if (cloned && !registrationAttempted && File.Exists(dest))
            {
                try { File.Delete(dest); Log("CLEANUP=UNREGISTERED_CLONE_REMOVED"); }
                catch (System.Exception ex) { Log("CLEANUP_FAILED=" + ex.Message); }
            }
            // This is a Session command: the original source Editor may no longer
            // have an active AutoCAD command context after switching to Ortho.
            // Never let eNotApplicable from WriteMessage mask the real exception.
            SafeWriteToActiveEditor("REV19 RESULT: " + report);
        }
    }

    private static void SafeWriteToActiveEditor(string message)
    {
        try
        {
            Document? active = AcadApp.DocumentManager.MdiActiveDocument;
            active?.Editor.WriteMessage("\n" + message);
        }
        catch (System.Exception)
        {
            // An AutoCAD session command may not have a document prompt context.
            // Diagnostic evidence is persisted by Log() independently.
            System.Diagnostics.Trace.WriteLine(message);
        }
    }

    private sealed record ProjectPart(string ProjectRoot, string OrthoFolder, object OrthoPart);

    private static ProjectPart CurrentOrthoProject()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies();
        Type app = FindType(asm,"Autodesk.ProcessPower.PlantInstance.PlantApplication") ??
            throw new InvalidOperationException("PlantApplication not loaded.");
        object plantProject = app.GetProperty("CurrentProject",
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
            throw new InvalidOperationException("No active Plant 3D project.");
        string root = plantProject.GetType().GetProperty("ProjectFolderPath")?
            .GetValue(plantProject)?.ToString() ?? "";
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Active project root was not found.");
        object parts = plantProject.GetType().GetProperty("ProjectParts")?
            .GetValue(plantProject) ?? throw new InvalidOperationException("ProjectParts missing.");
        object ortho = parts.GetType().GetProperty("Item")?
            .GetValue(parts, new object[] {"Ortho"}) ??
            throw new InvalidOperationException("Native Ortho project part missing.");
        string path = ortho.GetType().GetMethod("GetOrthoPath",
            BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)?
            .Invoke(ortho,null)?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Native Ortho directory unavailable.");
        if (!Path.IsPathRooted(path)) path = Path.Combine(root,path);
        path = Path.GetFullPath(path);
        if (!Directory.Exists(path) ||
            !NativeOrthoRegistrationNames.IsInsideProjectDirectory(path,root))
            throw new InvalidOperationException("Native Ortho directory outside active project.");
        return new ProjectPart(root,path,ortho);
    }

    private static bool IsNativeOrthoValid(object ortho, string filename)
    {
        Type util = FindType(AppDomain.CurrentDomain.GetAssemblies(),
            "Autodesk.ProcessPower.P3dProjectParts.OrthoDocUtils") ??
            throw new InvalidOperationException("OrthoDocUtils missing.");
        MethodInfo? validate = util.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name!="IsValidOrthoDrawing" || m.ReturnType!=typeof(bool)) return false;
                ParameterInfo[] ps=m.GetParameters();
                return ps.Length==3 && ps[0].ParameterType==typeof(string) &&
                    ps[1].ParameterType.IsInstanceOfType(ortho) &&
                    ps[2].ParameterType==typeof(string).MakeByRefType();
            });
        if (validate==null) return false;
        object?[] args = {filename,ortho,""};
        return validate.Invoke(null,args) is bool yes && yes;
    }

    private static string ViewCatalog(object ortho,string filename)
    {
        try
        {
            const BindingFlags pub=BindingFlags.Public | BindingFlags.Instance;
            MethodInfo? list=ortho.GetType().GetMethod("GetPnPDrawingFiles",pub,
                null,Type.EmptyTypes,null);
            MethodInfo? names=ortho.GetType().GetMethod("GetAllViewNamesInOrthoDwgByGuid",
                pub,null,new[]
                {
                    typeof(string),typeof(Dictionary<string,string>).MakeByRefType()
                },null);
            if (list?.Invoke(ortho,null) is not IEnumerable files || names==null)
                return "NATIVE_ORTHO_VIEW_CATALOG_UNAVAILABLE";
            foreach (object? entry in files)
            {
                if (entry==null) continue;
                string? n=ReadProperty(entry,"FileName") ??
                    ReadProperty(entry,"RelativeFilePath") ?? ReadProperty(entry,"Name");
                if (!string.Equals(Path.GetFileName(n),Path.GetFileName(filename),
                    StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string key in new[] {"Guid","PnPDrawingGuid","DrawingGuid"})
                {
                    string? guid=ReadProperty(entry,key);
                    if (!Guid.TryParse(guid,out _)) continue;
                    object?[] args={guid,new Dictionary<string,string>()};
                    names.Invoke(ortho,args);
                    if (args[1] is Dictionary<string,string> views)
                        return views.Count==0?"EMPTY_REGISTERED_ORTHO":
                            "EXISTING_NATIVE_VIEWS:" + string.Join(",",views.Keys.Take(8));
                }
                return "REGISTERED_ORTHO_WITHOUT_GUID";
            }
            return "ORTHO_NOT_IN_PROJECT_DRAWING_LIST";
        }
        catch (System.Exception ex)
        {
            return "ORTHO_VIEW_QUERY_ERROR:" + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static string FindPaperLayout(Database db)
    {
        using Transaction tr=db.TransactionManager.StartTransaction();
        var dict=(DBDictionary)tr.GetObject(db.LayoutDictionaryId,OpenMode.ForRead);
        string best="";double area=0;
        foreach (DBDictionaryEntry e in dict)
        {
            if (tr.GetObject(e.Value,OpenMode.ForRead) is not Layout layout ||
                string.Equals(layout.LayoutName,"Model",StringComparison.OrdinalIgnoreCase))
                continue;
            Point2d size=layout.PlotPaperSize;
            double scale=layout.PlotPaperUnits.ToString()=="Inches"?25.4:1;
            double a=size.X*size.Y*scale*scale;
            if (double.IsFinite(a) && a>area)
            {
                best=layout.LayoutName; area=a;
            }
        }
        return best;
    }

    private static (double width,double height) CurrentPaper(Database db,string layoutName)
    {
        using Transaction tr=db.TransactionManager.StartTransaction();
        var dict=(DBDictionary)tr.GetObject(db.LayoutDictionaryId,OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in dict)
        {
            if (tr.GetObject(entry.Value,OpenMode.ForRead) is not Layout paper ||
                paper.LayoutName!=layoutName) continue;
            double factor=paper.PlotPaperUnits.ToString()=="Inches"?25.4:1;
            Point2d size=paper.PlotPaperSize;
            if (!(size.X>0 && size.Y>0)) break;
            return (size.X*factor,size.Y*factor);
        }
        throw new InvalidOperationException("Valid Ortho paper-space layout not found.");
    }

    private static Type? FindType(IEnumerable<Assembly> assemblies,string name)
    {
        foreach (Assembly asm in assemblies)
        {
            try { Type? t=asm.GetType(name,false);if(t!=null)return t; }
            catch { }
        }
        return null;
    }

    private static string? ReadProperty(object value,string property)
    {
        try { return value.GetType().GetProperty(property)?.GetValue(value)?.ToString(); }
        catch { return null; }
    }

    private static bool Near(double a,double b) =>
        Math.Abs(a-b)<=Math.Max(1e-4,Math.Abs(b)*1e-7);

    private static string Point(double x,double y,double z) =>
        string.Format(CultureInfo.InvariantCulture,
            "({0:0.###},{1:0.###},{2:0.###})",x,y,z);
}
