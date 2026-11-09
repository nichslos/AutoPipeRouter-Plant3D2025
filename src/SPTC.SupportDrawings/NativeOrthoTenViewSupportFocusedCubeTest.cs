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
/// REV22.11 isolated actual viewport paper-size experiment: select support in a
/// registered 3D source; clone a verified empty native Ortho Project Manager seed;
/// register, open, initialize ten native OrthoCubes and call unsupported internal
/// GenerateOrthos for ten views using one clicked paper-space anchor.
/// BACKUP/COPIED PROJECT ONLY. Never directly patch Ortho.dcf.
/// Any Autodesk dialogs from the public API remain host-controlled.
/// </summary>
public sealed class NativeOrthoTenViewSupportFocusedCubeTest
{
    [CommandMethod("SPTC_ORTHO22_11_SUPPORT_CUBE_TEST",
        CommandFlags.Modal | CommandFlags.Session)]
    public async void Run()
    {
        Document? sourceDoc = AcadApp.DocumentManager.MdiActiveDocument;
        if (sourceDoc == null) return;
        Editor ed = sourceDoc.Editor;
        string source = Path.GetFullPath(sourceDoc.Database.Filename ?? "");
        if (!File.Exists(source))
        {
            ed.WriteMessage("\nREV22.11: Open a saved project-registered 3D model before running the test.");
            return;
        }

        NativeOrthoProjectCheck.Result sourceCheck;
        try { sourceCheck = NativeOrthoProjectCheck.Inspect(source); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV22.11: Native source check failed: " + ex.Message);
            return;
        }
        if (!sourceCheck.SourceRegistrationVerified)
        {
            ed.WriteMessage("\nREV22.11: Source 3D drawing is not verified project-registered. No changes.");
            return;
        }

        double units = sourceDoc.Database.Insunits.ToString() switch
        {
            "Millimeters" => 1, "Centimeters" => 10, "Meters" => 1000,
            "Inches" => 25.4, "Feet" => 304.8, _ => 0
        };
        if (units <= 0)
        {
            ed.WriteMessage("\nREV22.11: Unsupported source units; expected a physical unit like Millimeters.");
            return;
        }

        // REV22.11: Keep Original mode available as an exact fallback.
        // SupportFocus mode calculates the XY cube only from STRUCTURAL
        // support selection, not full long-pipe GeometricExtents.
        var modePrompt=new PromptKeywordOptions(
            "\nREV22.11 OrthoCube mode [SupportFocus/Original] <SupportFocus>: ")
        { AllowNone=true };
        modePrompt.Keywords.Add("SupportFocus");
        modePrompt.Keywords.Add("Original");
        modePrompt.Keywords.Default="SupportFocus";
        PromptResult modeChoice=ed.GetKeywords(modePrompt);
        if(modeChoice.Status!=PromptStatus.OK&&
           modeChoice.Status!=PromptStatus.None)return;
        bool supportFocus=!string.Equals(
            modeChoice.StringResult,"Original",StringComparison.OrdinalIgnoreCase);
        PromptSelectionResult selection = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = supportFocus
                ? "\nREV22.11: Select STRUCTURAL SUPPORT members ONLY (do not include long pipes): "
                : "\nREV22.11 ORIGINAL: Select all structure entities for ONE support: "
        });
        if (selection.Status != PromptStatus.OK) return;

        var handles = new List<string>();
        BoundingBox3? geometry = null;
        int invalidExtents = 0;
        int misselectedPipeEntities = 0;
        using (Transaction tr = sourceDoc.Database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in selection.Value.GetObjectIds().Distinct())
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity e) continue;
                    string selectedType=e.GetType().Name;
                    if(supportFocus &&
                       selectedType.EndsWith("Pipe",StringComparison.OrdinalIgnoreCase))
                    {
                        misselectedPipeEntities++;
                        continue;
                    }
                    Extents3d b = e.GeometricExtents;
                    var current = new BoundingBox3(b.MinPoint.X, b.MinPoint.Y,
                        b.MinPoint.Z, b.MaxPoint.X, b.MaxPoint.Y, b.MaxPoint.Z);
                    geometry = geometry.HasValue ? geometry.Value.Include(current) : current;
                    handles.Add(id.Handle.ToString());
                }
                catch { invalidExtents++; }
            }
        }
        if (!geometry.HasValue || handles.Count == 0 ||
            invalidExtents > 0 || misselectedPipeEntities > 0)
        {
            ed.WriteMessage("\nREV22.11: Select valid structural support entities only. "+
                "Invalid extents="+invalidExtents+
                " accidentally selected pipe objects="+misselectedPipeEntities);
            return;
        }

        // Explicit pipe selection is OPTIONAL: these pipes already exist in
        // the source DWG model references. They contribute ONLY local Z.
        // A selected pipe spanning kilometers must never enlarge X/Y.
        var localPipeBounds=new List<BoundingBox3>();
        SupportFocusedCubePlanner.Result? focusPlan=null;
        double focusXYClearanceMm=100.0,focusPipeReachMm=500.0;
        if(supportFocus)
        {
            var clearanceInput=new PromptDoubleOptions(
                "\nREV22.11 XY clearance around structural support (mm) <100>: ")
            { AllowNone=true,AllowNegative=false,AllowZero=true,
              DefaultValue=100.0 };
            PromptDoubleResult clearanceAnswer=ed.GetDouble(clearanceInput);
            if(clearanceAnswer.Status==PromptStatus.Cancel)return;
            if(clearanceAnswer.Status==PromptStatus.OK)
                focusXYClearanceMm=clearanceAnswer.Value;
            var reachInput=new PromptDoubleOptions(
                "\nREV22.11 Maximum local pipe reach above/below support (mm) <500>: ")
            { AllowNone=true,AllowNegative=false,AllowZero=true,
              DefaultValue=500.0 };
            PromptDoubleResult reachAnswer=ed.GetDouble(reachInput);
            if(reachAnswer.Status==PromptStatus.Cancel)return;
            if(reachAnswer.Status==PromptStatus.OK)
                focusPipeReachMm=reachAnswer.Value;
            ed.WriteMessage("\nREV22.11: OPTIONAL select long pipe objects crossing "+
                "the support. Their full LENGTH will not enlarge OrthoCube. "+
                "Press Enter to skip pipes.");
            PromptSelectionResult pipeSelection=ed.GetSelection(
                new PromptSelectionOptions
                {
                    MessageForAdding="\nREV22.11: Select nearby pipe route(s), or Enter to skip: "
                });
            if(pipeSelection.Status==PromptStatus.OK)
            {
                int invalidPipes=0;
                var selectedPipes=pipeSelection.Value.GetObjectIds().Distinct().ToArray();
                using(Transaction tr=sourceDoc.Database.TransactionManager.StartTransaction())
                {
                    foreach(ObjectId pipeId in selectedPipes)
                    {
                        try
                        {
                            if(tr.GetObject(pipeId,OpenMode.ForRead,false) is not Entity pipeEntity)
                            { invalidPipes++;continue; }
                            Extents3d pb=pipeEntity.GeometricExtents;
                            localPipeBounds.Add(new BoundingBox3(
                                pb.MinPoint.X,pb.MinPoint.Y,pb.MinPoint.Z,
                                pb.MaxPoint.X,pb.MaxPoint.Y,pb.MaxPoint.Z));
                        }
                        catch{invalidPipes++;}
                    }
                }
                if(invalidPipes>0 || localPipeBounds.Count==0)
                {
                    ed.WriteMessage("\nREV22.11: Invalid pipe extents, no project files changed.");
                    return;
                }
            }
        }

        NativeOrthoLayout.Plan layout;
        try
        {
            if(supportFocus)
            {
                focusPlan=SupportFocusedCubePlanner.Build(
                    geometry.Value,localPipeBounds,units,
                    focusXYClearanceMm,focusPipeReachMm);
                if(localPipeBounds.Count>0 && focusPlan.PipesAccepted==0)
                    throw new InvalidOperationException(
                        "None of the selected pipes intersects the support-focused "+
                        "footprint and vertical reach. Reselect pipes or adjust clearance.");
                layout=NativeOrthoLayout.Build(focusPlan.FocusBounds,units);
            }
            else layout=NativeOrthoLayout.Build(geometry.Value,units);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV22.11: Invalid support OrthoCube: " + ex.Message);
            return;
        }
        ProjectPart part;
        try { part = CurrentOrthoProject(); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV22.11: Cannot resolve current Plant Ortho project: " + ex.Message);
            return;
        }

        string initial = Directory.Exists(Path.Combine(part.OrthoFolder, "DWGs")) ?
            Path.Combine(part.OrthoFolder, "DWGs") : part.OrthoFolder;
        using var picker = new WinForms.OpenFileDialog
        {
            Title = "REV22.11 - Select an EMPTY Project Manager-created native Ortho seed (not DWT)",
            Filter = "AutoCAD drawing (*.dwg)|*.dwg", CheckFileExists = true,
            InitialDirectory = initial, RestoreDirectory = true
        };
        if (picker.ShowDialog() != WinForms.DialogResult.OK) return;
        string seed = Path.GetFullPath(picker.FileName);
        if (!NativeOrthoRegistrationNames.IsInsideProjectDirectory(seed, part.ProjectRoot) ||
            !NativeOrthoRegistrationNames.IsInsideProjectDirectory(seed, part.OrthoFolder))
        {
            ed.WriteMessage("\nREV22.11: Seed must be within the active project's registered Orthos folder.");
            return;
        }
        if (!IsNativeOrthoValid(part.OrthoPart, seed))
        {
            ed.WriteMessage("\nREV22.11: Seed failed native OrthoDocUtils.IsValidOrthoDrawing.");
            return;
        }
        string seedState = ViewCatalog(part.OrthoPart, seed);
        if (seedState != "EMPTY_REGISTERED_ORTHO")
        {
            ed.WriteMessage("\nREV22.11: Seed must be registered and EMPTY; found: " + seedState);
            return;
        }

        string suffix = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..6];
        string file = NativeOrthoRegistrationNames.NewOrthoDrawingFileName(source, handles, suffix)
            .Replace("_ORTHO_R14_", "_ORTHO_R211FOCUS_", StringComparison.Ordinal);
        string dest = Path.Combine(Path.GetDirectoryName(seed)!, file);
        if (File.Exists(dest))
        {
            ed.WriteMessage("\nREV22.11: Destination DWG exists. No changes.");
            return;
        }

        // The public SDK, not this command, decides the final viewport insertion.
        // No false claim that the proposed TOP center is passed into CreateOrthos.
        var popup = WinForms.MessageBox.Show(
            "REV22.11 - ONE-CLICK ALL TEN NATIVE VIEW TYPES EXPERIMENT\n\n" +
            "SOURCE 3D: " + source + "\n" +
            "SELECTED MEMBERS: " + string.Join(", ", handles) + "\n" +            "CUBE MODE: " + (supportFocus?"SupportFocus":"Original") + "\n" +
            "SELECTED PIPE EXTENT CANDIDATES: " + localPipeBounds.Count + "\n" +
            "SUPPORT FOCUS XY CLEARANCE (mm): " + focusXYClearanceMm + "\n" +
            "LOCAL PIPE Z REACH (mm): " + focusPipeReachMm + "\n" +
            "Long pipes will be CLIPPED by a compact native OrthoCube.\n" +

            "NATIVE EMPTY SEED: " + seed + "\n" +
            "NEW NATIVE ORTHO: " + dest + "\n\n" +
            "A NEW drawing will be created and registered to the ACTIVE Plant project.\n" +
            "ONE anchor click will position TEN proposed native Ortho views.\n" +
            "EXPERIMENTAL: this invokes Autodesk's NON-PUBLIC GenerateOrthos API.\n" +
            "Native view links, viewport state and Update View are NOT guaranteed.\n" +
            "Do not use your production project. Retain all test logs and drawings.\n\n" +
            "ONLY authorize in a separate COPIED/BACKED-UP TEST PROJECT.\n" +
            "Do you want to run this experiment?",
            "SPTC REV22.11 UNSUPPORTED INTERNAL API - COPIED TEST PROJECT ONLY",
            WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning,
            WinForms.MessageBoxDefaultButton.Button2);
        if (popup != WinForms.DialogResult.Yes) return;
        PromptResult typed = ed.GetString(new PromptStringOptions(
            "\nType REV22.11 TEST to register a NEW Ortho DWG in this copied project: ")
        { AllowSpaces = true });
        if (typed.Status != PromptStatus.OK ||
            !string.Equals(typed.StringResult.Trim(), "REV22.11 TEST", StringComparison.Ordinal))
        {
            ed.WriteMessage("\nREV22.11: Cancelled before any project write.");
            return;
        }

        string report = Path.Combine(Path.GetDirectoryName(dest)!,
            Path.GetFileNameWithoutExtension(dest) + "_REV22_11_RESULT.txt");
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
            Log("SPTC_REV22_ONE_COMMAND_NATIVE_TOP_TEST");
            Log("PROJECT=" + part.ProjectRoot);
            Log("SOURCE_MODEL=" + source);
            Log("SOURCE_HANDLES=" + string.Join(",", handles));
            Log("REV22_11_CUBE_MODE="+(supportFocus?"SupportFocus":"Original"));
            Log("REV22_11_SUPPORT_RAW_MIN="+Point(
                geometry.Value.MinX,geometry.Value.MinY,geometry.Value.MinZ));
            Log("REV22_11_SUPPORT_RAW_MAX="+Point(
                geometry.Value.MaxX,geometry.Value.MaxY,geometry.Value.MaxZ));
            Log("REV22_11_SELECTED_PIPE_BOUNDS_COUNT="+localPipeBounds.Count);
            if(focusPlan!=null)
            {
                Log("REV22_11_XY_CLEARANCE_MM="+focusXYClearanceMm);
                Log("REV22_11_VERTICAL_PIPE_REACH_MM="+focusPipeReachMm);
                Log("REV22_11_PIPE_BBOX_ACCEPTED="+focusPlan.PipesAccepted);
                Log("REV22_11_PIPE_BBOX_REJECTED="+focusPlan.PipesRejected);
                Log("REV22_11_PIPE_Z_CLIPPED="+focusPlan.PipesVerticalClipped);
                Log("REV22_11_FOCUS_XY_UNCHANGED_BY_PIPES=True");
                Log("REV22_11_FOCUSED_MIN="+Point(
                    focusPlan.FocusBounds.MinX,
                    focusPlan.FocusBounds.MinY,focusPlan.FocusBounds.MinZ));
                Log("REV22_11_FOCUSED_MAX="+Point(
                    focusPlan.FocusBounds.MaxX,
                    focusPlan.FocusBounds.MaxY,focusPlan.FocusBounds.MaxZ));
            }
            Log("EMPTY_NATIVE_SEED=" + seed);
            Log("NEW_CLONE=" + dest);
            Log("NATIVE_CUBE_MIN=" + Point(layout.PaddedCube.MinX,
                layout.PaddedCube.MinY,layout.PaddedCube.MinZ));
            Log("NATIVE_CUBE_MAX=" + Point(layout.PaddedCube.MaxX,
                layout.PaddedCube.MaxY,layout.PaddedCube.MaxZ));
            Log("ONE_CLICK_ANCHOR_REQUIRED=TRUE");
            Log("PUBLIC_API_ONLY=FALSE");

            // REV16-proven native DWG seed clone; never clone a DWT.
            using (var db = new Database(false, true))
            {
                // REV22.11: The FileShare overload can throw eFileSharingViolation
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
            NativeOrthoTenViewPlan.Plan candidate =
                NativeOrthoTenViewPlan.Build(layout, paperWidth, paperHeight);
            Log("STEP4_SHEET_LAYOUT=" + paperLayout);
            Log("STEP4_SHEET_MM=" + Point(paperWidth,paperHeight,0));
            Log("STEP4_COMMON_SCALE=1:" + candidate.Denominator);
            foreach(var slot in candidate.Views)
                Log("PROPOSED_" + slot.Name + "_CENTER=" +
                    Point(slot.CenterX,slot.CenterY,0) +
                    "_TILE=" + Point(slot.W,slot.H,0));
            Log("PROPOSED_LAYOUT_NOT_ENFORCED_BY_NATIVE_API=True");
            Log("PUBLIC_API_HAS_NO_INSERTION_POINT_ARGUMENT=TRUE");

            // REV22.11: A Session command can activate a different DWG without
            // entering that drawing's document/command execution context.
            // REV22.11 yielded sentinel +/-1e20 Extents after InitializeCube,
            // whereas REV18 succeeded when invoked within the Ortho document.
            // Schedule all native cube + view generation in target document
            // context, preserving the exact strict extents validation.
            Log("STEP5_SESSION_CONTEXT_IS_APPLICATION=" +
                AcadApp.DocumentManager.IsApplicationContext);
            Log("STEP5_TARGET_COMMAND_CONTEXT_REQUESTED");
            await AcadApp.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                // REV22.4: distinguish managed wrapper object identity from
                // the actual DWG identity. If a Session document switch left
                // the working DB on the source DWG, synchronize it to the
                // verified active target before creating any native OrthoCube.
                Database? originalWorkingDatabase = null;
                bool workingDbWasChanged = false;
                try
                {
                    Log("STEP5_TARGET_COMMAND_CONTEXT_ENTERED");
                    bool appContext = AcadApp.DocumentManager.IsApplicationContext;
                    bool activeMatches = ReferenceEquals(
                        AcadApp.DocumentManager.MdiActiveDocument, targetDoc);
                    originalWorkingDatabase = HostApplicationServices.WorkingDatabase;
                    string beforePath = originalWorkingDatabase?.Filename ?? "";
                    string targetPath = targetDoc.Database.Filename ?? "";
                    bool wrapperMatches = ReferenceEquals(
                        originalWorkingDatabase, targetDoc.Database);
                    bool sameDrawing = !string.IsNullOrWhiteSpace(beforePath) &&
                        !string.IsNullOrWhiteSpace(targetPath) &&
                        string.Equals(Path.GetFullPath(beforePath),
                            Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase);
                    Log("STEP5_TARGET_CONTEXT_IS_APPLICATION=" + appContext);
                    Log("STEP5_TARGET_CONTEXT_ACTIVE_DOC_MATCH=" + activeMatches);
                    Log("STEP5_WORKING_DB_BEFORE=" + beforePath);
                    Log("STEP5_EXPECTED_WORKING_DB=" + targetPath);
                    Log("STEP5_WORKING_DB_WRAPPER_EQUALS=" + wrapperMatches);
                    Log("STEP5_TARGET_CONTEXT_WORKING_DB_MATCH=" + sameDrawing);
                    if (appContext || !activeMatches)
                        throw new InvalidOperationException(
                            "Target Ortho document command context is unavailable. " +
                            "Native TOP generation stopped before touching cube geometry.");

                    if (!sameDrawing)
                    {
                        Log("STEP5_WORKING_DB_SYNC_ENTERED");
                        HostApplicationServices.WorkingDatabase = targetDoc.Database;
                        workingDbWasChanged = true;
                        Log("STEP5_WORKING_DB_SYNC_RETURNED");
                    }
                    string effective = HostApplicationServices.WorkingDatabase?.Filename ?? "";
                    bool verifiedWorking = !string.IsNullOrWhiteSpace(effective) &&
                        string.Equals(Path.GetFullPath(effective),
                            Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase);
                    Log("STEP5_WORKING_DB_EFFECTIVE=" + effective);
                    Log("STEP5_WORKING_DB_VERIFIED_TARGET=" + verifiedWorking);
                    if (!verifiedWorking)
                        throw new InvalidOperationException(
                            "AutoCAD working database still differs from native Ortho " +
                            "target after command-context synchronization. See REV22.4 log.");

            // REV22 ISOLATED EXPERIMENT: Non-public GenerateOrthos requires a
            // preexisting native paper-space Viewport ObjectId and insertion
            // Point3d. Exact signature is verified before taking the one click.
            // Unsupported calls are limited to a NEW registered Ortho clone in
            // the user-confirmed COPIED test project.
            Assembly[] loaded=AppDomain.CurrentDomain.GetAssemblies();
            Type cubeType=FindType(loaded,
                "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoCube") ??
                throw new InvalidOperationException("Native PlantOrthoCube unavailable.");
            Type viewType=FindType(loaded,
                "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoView") ??
                throw new InvalidOperationException("Native PlantOrthoView unavailable.");
            MethodInfo init=cubeType.GetMethod("InitializeCube",
                BindingFlags.Public|BindingFlags.Instance,null,
                new[]{typeof(Extents3d)},null) ??
                throw new MissingMethodException("Native InitializeCube(Extents3d)");
            MethodInfo makeSolid=cubeType.GetMethod("MakeCubeCopy",
                BindingFlags.Public|BindingFlags.Instance,null,Type.EmptyTypes,null) ??
                throw new MissingMethodException("Native MakeCubeCopy()");
            MethodInfo create=viewType.GetMethods(BindingFlags.Public|BindingFlags.Instance)
                .FirstOrDefault(m=>m.Name=="CreateOrthos"&&m.GetParameters().Length==13) ??
                throw new MissingMethodException("Public CreateOrthos(13 args), required for enum discovery");
            ConstructorInfo ctor=viewType.GetConstructor(new[]{typeof(bool),typeof(bool)}) ??
                throw new MissingMethodException("PlantOrthoView(bool,bool)");
            Type nativeEnum=create.GetParameters()[6].ParameterType;
            if(!nativeEnum.IsEnum)throw new InvalidOperationException("Native ViewType is not an enum.");
            string[] available=Enum.GetNames(nativeEnum);
            Log("STEP5_NATIVE_VIEW_TYPES="+string.Join(",",available));
            // REV22.11: native Edit View string parser is queried before
            // picking stored preset names for all SIX standard views.
            Type nativeUi=FindType(loaded,
                "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.UIUtils") ??
                throw new InvalidOperationException("Native UIUtils missing.");
            MethodInfo parser=nativeUi.GetMethod("GetViewTypeFromName",
                BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static,
                null,new[]{typeof(string)},null) ??
                throw new MissingMethodException("UIUtils.GetViewTypeFromName(string)");
            var resolved=new List<(NativeOrthoTenViewPlan.Slot Slot,object ViewEnum,string TypeName)>();
            foreach(var slot in candidate.Views)
            {
                string selected=slot.EnumName;
                if(slot.Name=="SIDE"&&!available.Any(x=>x.Equals(selected,StringComparison.OrdinalIgnoreCase)) &&
                    available.Any(x=>x.Equals("Side",StringComparison.OrdinalIgnoreCase)))
                    selected="Side";
                if(!available.Any(x=>x.Equals(selected,StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Native ViewType "+selected+" missing for "+slot.Name);
                // Keep proven NW/NE/SW/SE type strings; resolve axial
                // presets against *this installation's* Edit View parser.
                object nativeValue=Enum.Parse(nativeEnum,selected,true);
                string nativeString=NativeOrthoEditorPreset.Resolve(
                    slot.Name,selected=="Side"?"SideView":slot.TypeName);
                if(NativeOrthoAxialEditorPreset.IsAxial(slot.Name))
                {
                    Log("STEP5_"+slot.Name+"_AXIAL_CANDIDATES="+
                        string.Join(",",NativeOrthoAxialEditorPreset.Candidates(
                            slot.Name,slot.TypeName)));
                    nativeString=NativeOrthoAxialEditorPreset.Resolve(
                        slot.Name,slot.TypeName,
                        Convert.ToInt32(nativeValue,CultureInfo.InvariantCulture),
                        candidateLabel=>
                        {
                            try
                            {
                                object? parsedValue=parser.Invoke(
                                    null,new object[]{candidateLabel});
                                return parsedValue==null?(int?)null:
                                    Convert.ToInt32(parsedValue,CultureInfo.InvariantCulture);
                            }
                            catch(TargetInvocationException){ return null; }
                        });
                }
                if(NativeOrthoIsoViewType.IsIsometric(slot.Name)&&
                    (!NativeOrthoIsoViewType.HasExpectedQuadrant(slot.Name,
                        slot.Dx,slot.Dy,slot.Dz)||
                     !string.Equals(selected,slot.Name,StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        "REV22.11 unexpected "+slot.Name+
                        " native enum or camera quadrant, refusing Current View fallback.");
                resolved.Add((slot,nativeValue,nativeString));
                Log("STEP5_"+slot.Name+"_ENUM="+selected+
                    " OLD_TYPE_NAME="+slot.TypeName+
                    " NATIVE_TYPE_NAME="+nativeString+
                    " ISOMETRIC="+NativeOrthoIsoViewType.IsIsometric(slot.Name));
            }
            // REV22.11: verify ALL TEN strings, not just four isometrics.
            // Unknown/native Current fallback is a hard pre-generation failure.
            foreach(var spec in resolved)
            {
                if(NativeOrthoIsoViewType.IsIsometric(spec.Slot.Name) &&
                   !NativeOrthoEditorPreset.IsExactIsoPreset(
                       spec.Slot.Name,spec.TypeName))
                    throw new InvalidOperationException("REV22.11 "+
                        spec.Slot.Name+" invalid saved ISO type: "+spec.TypeName);
                object? parsed=parser.Invoke(null,new object[]{spec.TypeName});
                bool match=parsed!=null &&
                    Convert.ToInt32(parsed,CultureInfo.InvariantCulture)==
                    Convert.ToInt32(spec.ViewEnum,CultureInfo.InvariantCulture);
                Log("STEP5_"+spec.Slot.Name+"_EDITOR_PRESET="+
                    spec.TypeName+" MATCHED_NATIVE_ENUM="+match);
                if(!match)
                    throw new InvalidOperationException("REV22.11 "+
                        spec.Slot.Name+" maps to Current View in native Editor.");
            }
            if(candidate.Views.Count!=10 || resolved.Count!=10)
                throw new InvalidOperationException("REV22 requires ten native views.");
            if(resolved.Select(v=>v.Slot.EnumName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=10)
                throw new InvalidOperationException("REV22 duplicate view-type enum detected.");
            Type[] expectedNativeSignature={
                typeof(StringCollection),typeof(string),typeof(string),
                typeof(Point3d),typeof(Point3d),nativeEnum,typeof(Point3d),
                typeof(Vector3d),typeof(Vector3d),typeof(ObjectId),
                typeof(double),typeof(double),typeof(double),typeof(double),
                typeof(double),typeof(Solid3d)
            };
            var nativeMethods=viewType.GetMethods(BindingFlags.NonPublic|BindingFlags.Instance)
                .Where(m=>m.Name=="GenerateOrthos").ToArray();
            foreach(var m in nativeMethods)
                Log("STEP5_INTERNAL_SIGNATURE="+m);
            MethodInfo? generate=nativeMethods.FirstOrDefault(m=>
                m.GetParameters().Select(p=>p.ParameterType)
                .SequenceEqual(expectedNativeSignature));
            Log("STEP5_UNSUPPORTED_GENERATE_SIGNATURE_MATCH="+(generate!=null));
            if(generate==null)
                throw new MissingMethodException(
                    "Exact internal GenerateOrthos(16 arguments) signature not found. " +
                    "This Plant 3D release is NOT safe for the REV22 experiment.");
            Log("STEP5_UNSUPPORTED_INTERNAL_API=TRUE");
            var bounds=layout.PaddedCube;
            var extents=new Extents3d(
                new Point3d(bounds.MinX,bounds.MinY,bounds.MinZ),
                new Point3d(bounds.MaxX,bounds.MaxY,bounds.MaxZ));
            Log("STEP5_CUBE_EXPECTED_MIN="+Point(bounds.MinX,bounds.MinY,bounds.MinZ));
            Log("STEP5_CUBE_EXPECTED_MAX="+Point(bounds.MaxX,bounds.MaxY,bounds.MaxZ));

            // One click only. This is the TOP viewport's paper-space center;
            // FRONT and SIDE are translated by the same planned offsets.
            targetDoc.Editor.WriteMessage(
                "\nREV22.11: click ONCE inside the Ortho sheet for TOP placement. "+
                "All TEN views will follow in a 2-column x 5-row group. If the group does not fit "+
                "at that position, it will shift together to the nearest safe area.");
            PromptPointResult placement=targetDoc.Editor.GetPoint(
                new PromptPointOptions("\nREV22 ONE CLICK: Pick TOP group anchor in paper space: "));
            if(placement.Status!=PromptStatus.OK)
                throw new InvalidOperationException("REV22 cancelled: no group anchor point picked.");
            Point3d anchor=placement.Value;
            if(!double.IsFinite(anchor.X)||!double.IsFinite(anchor.Y)||
                !double.IsFinite(anchor.Z))
                throw new InvalidOperationException("REV22 received non-finite placement point.");
            Log("STEP5_ONE_CLICK_TOP_ANCHOR="+Point(anchor.X,anchor.Y,anchor.Z));
            Log("STEP5_ONE_CLICK_COUNT=1");

            // REV22.11: preserve the one-click workflow and translate the
            // complete group when the requested TOP anchor is too close to an
            // edge. The strict sheet/title-block and collision guards remain.
            // Log the translation BEFORE the first unsupported native call.
            var adjustedGroup=NativeOrthoTenViewPlan.BuildAdjusted(
                candidate,paperWidth,paperHeight,anchor.X,anchor.Y);
            var group=adjustedGroup.Views;
            Log("STEP5_ONE_CLICK_VALID_TOP_X_RANGE="+
                adjustedGroup.MinimumTopX.ToString("0.###",CultureInfo.InvariantCulture)+
                ","+adjustedGroup.MaximumTopX.ToString("0.###",CultureInfo.InvariantCulture));
            Log("STEP5_ONE_CLICK_VALID_TOP_Y_RANGE="+
                adjustedGroup.MinimumTopY.ToString("0.###",CultureInfo.InvariantCulture)+
                ","+adjustedGroup.MaximumTopY.ToString("0.###",CultureInfo.InvariantCulture));
            Log("STEP5_ONE_CLICK_EFFECTIVE_TOP_ANCHOR="+
                Point(adjustedGroup.EffectiveTopX,adjustedGroup.EffectiveTopY,0));
            Log("STEP5_ONE_CLICK_GROUP_TRANSLATION="+
                Point(adjustedGroup.EffectiveTopX-anchor.X,
                      adjustedGroup.EffectiveTopY-anchor.Y,0));
            Log("STEP5_ONE_CLICK_AUTO_SHIFTED="+adjustedGroup.WasAdjusted);
            if(adjustedGroup.WasAdjusted)
                targetDoc.Editor.WriteMessage(
                    "\nREV22.11: moved the complete ten-view group to fit "+
                    "the sheet. Effective TOP center: ("+
                    adjustedGroup.EffectiveTopX.ToString("0.#",CultureInfo.InvariantCulture)+
                    ", "+adjustedGroup.EffectiveTopY.ToString("0.#",CultureInfo.InvariantCulture)+
                    "). See REV22_RESULT.txt for exact offset.");
            var planned=new List<(NativeOrthoTenViewPlan.Slot Slot,
                object ViewEnum,string TypeName,Point3d Center,double Width,double Height)>();
            foreach(var item in resolved)
            {
                var plannedPosition=group.First(g=>g.Name==item.Slot.Name);
                planned.Add((item.Slot,item.ViewEnum,item.TypeName,
                    new Point3d(plannedPosition.CenterX,plannedPosition.CenterY,0),
                    plannedPosition.PaperFrameWidth,plannedPosition.PaperFrameHeight));
                Log("STEP5_"+item.Slot.Name+"_AUTO_CENTER="+
                    Point(plannedPosition.CenterX,plannedPosition.CenterY,0)+
                    " FRAME="+Point(plannedPosition.PaperFrameWidth,
                        plannedPosition.PaperFrameHeight,0));
            }
            var priorHandles=new HashSet<string>(
                PaperViewports(targetDoc.Database,paperLayout).Select(x=>x.Handle),
                StringComparer.OrdinalIgnoreCase);
            var completed=new List<string>();
            var confirmedFrames=new List<NativeOrthoViewportPaperGuard.Frame>();
            string stamp=DateTime.UtcNow.ToString("HHmmss",CultureInfo.InvariantCulture);
            foreach(var item in planned)
            {
                string label=item.Slot.Name;
                Log("STEP6_"+label+"_BEGIN");
                object cube=Activator.CreateInstance(cubeType) ??
                    throw new InvalidOperationException(label+": failed to instantiate native cube.");
                try
                {
                    init.Invoke(cube,new object[]{extents});
                    if(cubeType.GetProperty("Extents")?.GetValue(cube) is not Extents3d got)
                        throw new InvalidOperationException(label+": no native cube extents.");
                    Log("STEP6_"+label+"_NATIVE_MIN="+
                        Point(got.MinPoint.X,got.MinPoint.Y,got.MinPoint.Z));
                    Log("STEP6_"+label+"_NATIVE_MAX="+
                        Point(got.MaxPoint.X,got.MaxPoint.Y,got.MaxPoint.Z));
                    bool matches=Near(got.MinPoint.X,bounds.MinX)&&
                        Near(got.MinPoint.Y,bounds.MinY)&&Near(got.MinPoint.Z,bounds.MinZ)&&
                        Near(got.MaxPoint.X,bounds.MaxX)&&Near(got.MaxPoint.Y,bounds.MaxY)&&
                        Near(got.MaxPoint.Z,bounds.MaxZ);
                    Log("STEP6_"+label+"_CUBE_EXTENTS_MATCHED="+matches);
                    if(!matches)throw new InvalidOperationException(
                        label+": native OrthoCube differs from selected support bounds.");
                    if(makeSolid.Invoke(cube,null) is not Solid3d solid)
                        throw new InvalidOperationException(label+": native cube did not return Solid3d.");
                    using(solid)
                    {
                        if(!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument,targetDoc))
                            throw new InvalidOperationException(label+": expected native Ortho not active.");
                        if(LayoutManager.Current.CurrentLayout!=paperLayout)
                            throw new InvalidOperationException(label+": native paper layout changed.");
                        string before=ViewCatalog(part.OrthoPart,dest);
                        if(!(completed.Count==0?
                            before=="EMPTY_REGISTERED_ORTHO":CatalogMatches(before,completed)))
                            throw new InvalidOperationException(label+": unexpected native view catalog: "+before);
                        string viewName="SPTC_"+label+"_R22_"+stamp;
                        Log("STEP7_"+label+"_VIEW_NAME="+viewName);
                        Log("STEP7_"+label+"_VIEW_DIRECTION="+
                            Point(item.Slot.Dx,item.Slot.Dy,item.Slot.Dz));
                        Log("STEP7_"+label+"_UP_DIRECTION="+
                            Point(item.Slot.Ux,item.Slot.Uy,item.Slot.Uz));
                        Log("STEP7_"+label+"_CONSTRUCT_NATIVE_ENTERED");
                        object nativeView=ctor.Invoke(new object[]{false,false});
                        Log("STEP7_"+label+"_CONSTRUCT_NATIVE_RETURNED");
                        try
                        {
                            if(!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument,targetDoc))
                                throw new InvalidOperationException(
                                    label+": native constructor switched active document.");
                            // The unsupported method expects an ObjectId of a
                            // native paper viewport already belonging to the
                            // active Ortho layout. NEVER pass ObjectId.Null.
                            ObjectId viewportId=CreateExperimentalViewport(
                                targetDoc.Database,paperLayout,item.Center,
                                item.Width,item.Height,
                                new Vector3d(item.Slot.Dx,item.Slot.Dy,item.Slot.Dz),
                                new Point3d(bounds.MidX,bounds.MidY,bounds.MidZ),
                                candidate.Scale,item.Slot.ModelHmm);
                            Log("STEP7_"+label+"_CREATED_VIEWPORT_ID="+viewportId);
                            if(NativeOrthoIsoViewType.IsIsometric(label))
                            {
                                Vector3d configured=ReadViewportCamera(
                                    targetDoc.Database,viewportId);
                                bool correct=NativeOrthoIsoViewType.MatchesCamera(
                                    label,configured.X,configured.Y,configured.Z,
                                    item.Slot.Dx,item.Slot.Dy,item.Slot.Dz);
                                Log("STEP7_"+label+"_ISOMETRIC_CAMERA_BEFORE="+
                                    Point(configured.X,configured.Y,configured.Z));
                                Log("STEP7_"+label+"_ISOMETRIC_CAMERA_MATCH_BEFORE="+correct);
                                if(!correct)throw new InvalidOperationException(
                                    "REV22.11 "+label+
                                    " pre-generation camera differs from requested isometric preset.");
                            }
                            Log("STEP7_"+label+"_NATIVE_TYPE_STRING="+item.TypeName);
                            object[] args={
                                new StringCollection{source},viewName,item.TypeName,
                                new Point3d(bounds.MinX,bounds.MinY,bounds.MinZ),
                                new Point3d(bounds.MaxX,bounds.MaxY,bounds.MaxZ),
                                item.ViewEnum,item.Center,
                                new Vector3d(item.Slot.Dx,item.Slot.Dy,item.Slot.Dz),
                                new Vector3d(item.Slot.Ux,item.Slot.Uy,item.Slot.Uz),
                                viewportId,item.Width,item.Height,bounds.SizeZ,
                                candidate.Scale,0.0,solid
                            };
                            // REV22.11: REV22.2 passed raw bounds.SizeX/SizeY as
                            // viewportWidth/viewportHeight. Its native call then
                            // changed a planned 76.163 x 19.950 paper viewport
                            // into a 362.680 x 95.000 viewport. The API argument
                            // names and observed equality indicate the requested
                            // paper frame dimensions belong here instead.
                            // Treat this as an EXPERIMENT, validated immediately
                            // against the ACTUAL resulting AutoCAD Viewport.
                            Log("STEP8_"+label+"_NATIVE_REQUESTED_PAPER_FRAME="+
                                Point(item.Width,item.Height,0));
                            Log("STEP8_"+label+"_PRIVATE_GENERATEORTHOS_ENTERED");
                            generate.Invoke(nativeView,args);
                            Log("STEP8_"+label+"_PRIVATE_GENERATEORTHOS_RETURNED");
                            if(NativeOrthoAxialEditorPreset.IsAxial(label))
                            {
                                // REV22.11 ISOLATED AXIAL HALF-TURN experiment:
                                // Manual Update View visibly rotates all SIX
                                // axial projections ~180 degrees. Exact twist
                                // values BEFORE/AFTER were never recorded in
                                // REV22.9. Rotate either 0->180 or 180->0, and
                                // invert BOTH ViewCenter axes in one transaction,
                                // as REV22.7 did for four validated ISO views.
                                // Never alter the NW/NE/SW/SE code branch.
                                var beforeAxial=ReadIsoViewportState(
                                    targetDoc.Database,viewportId);
                                Vector3d axialCameraBefore=ReadViewportCamera(
                                    targetDoc.Database,viewportId);
                                Vector3d axialExpectedCamera=new Vector3d(
                                    item.Slot.Dx,item.Slot.Dy,item.Slot.Dz);
                                bool beforeCameraMatch=
                                    axialCameraBefore.Length>1e-12&&
                                    axialExpectedCamera.Length>1e-12&&
                                    axialCameraBefore.GetNormal().DotProduct(
                                        axialExpectedCamera.GetNormal())>0.99999;
                                Log("STEP8_"+label+"_AXIAL_CAMERA_MATCH_BEFORE="+
                                    beforeCameraMatch);
                                Log("STEP8_"+label+"_AXIAL_TWIST_BEFORE_DEG="+
                                    (beforeAxial.TwistRadians*180/Math.PI).ToString(
                                        "0.######",CultureInfo.InvariantCulture));
                                Log("STEP8_"+label+"_AXIAL_VIEWCENTER_BEFORE="+
                                    Point(beforeAxial.CenterX,beforeAxial.CenterY,0));
                                if(!beforeCameraMatch)
                                    throw new InvalidOperationException(
                                        "REV22.11 "+label+" camera mismatch; refusing to rotate.");
                                var expectedAxial=NativeOrthoAxialHalfTurnGuard.Flip(
                                    beforeAxial);
                                SetIsoViewportPose(
                                    targetDoc.Database,viewportId,expectedAxial);
                                var afterAxial=ReadIsoViewportState(
                                    targetDoc.Database,viewportId);
                                Vector3d axialCameraAfter=ReadViewportCamera(
                                    targetDoc.Database,viewportId);
                                bool afterCameraMatch=
                                    axialCameraAfter.Length>1e-12&&
                                    axialCameraAfter.GetNormal().DotProduct(
                                        axialExpectedCamera.GetNormal())>0.99999;
                                bool axialPass=NativeOrthoAxialHalfTurnGuard.Matches(
                                    expectedAxial,afterAxial)&&afterCameraMatch;
                                Log("STEP8_"+label+"_AXIAL_TWIST_AFTER_DEG="+
                                    (afterAxial.TwistRadians*180/Math.PI).ToString(
                                        "0.######",CultureInfo.InvariantCulture));
                                Log("STEP8_"+label+"_AXIAL_VIEWCENTER_AFTER="+
                                    Point(afterAxial.CenterX,afterAxial.CenterY,0));
                                Log("STEP8_"+label+"_AXIAL_CAMERA_MATCH_AFTER="+
                                    afterCameraMatch);
                                Log("STEP8_"+label+"_AXIAL_FRAME_ROTATION_CHECK="+
                                    (axialPass?"PASS":"FAIL"));
                                if(!axialPass)
                                    throw new InvalidOperationException(
                                        "REV22.11 "+label+
                                        " half-turn/center adjustment failed readback.");
                            }
                            if(NativeOrthoIsoViewType.IsIsometric(label))
                            {
                                // REV22.11 isolated corrective test. REV22.4
                                // observed 180deg twist on NW/NE/SW/SE even
                                // though VIEW_DIRECTION and stored NW/NE/SW/SE
                                // native identifiers were correct. The WCS-Z
                                // projected UP vectors in NativeOrthoTenViewPlan
                                // require ZERO screen twist, not PI radians.
                                // Only newly generated test viewports are edited.
                                // Native Edit View / Update View may restore
                                // its own rotation; verify after regeneration.
                                // REV22.11: REV22.5 merely reset TwistAngle=0
                                // but left ViewCenter in the OLD 180deg view
                                // coordinate system. Thus it rendered geometry
                                // off-screen (empty frames) despite correct camera,
                                // roll and paper size. A native Update View changed
                                // center (347.236,-17.816) -> (-347.236,17.816).
                                // Adjust both properties atomically, ISO only.
                                var oldPose=ReadIsoViewportState(
                                    targetDoc.Database,viewportId);
                                Log("STEP8_"+label+"_ISO_TWIST_BEFORE_DEG="+
                                    (oldPose.TwistRadians*180/Math.PI).ToString(
                                        "0.######",CultureInfo.InvariantCulture));
                                Log("STEP8_"+label+"_ISO_VIEWCENTER_BEFORE="+
                                    Point(oldPose.CenterX,oldPose.CenterY,0));
                                if(!NativeOrthoIsoTwistGuard.IsProjectedWorldUp(
                                    item.Slot.Dx,item.Slot.Dy,item.Slot.Dz,
                                    item.Slot.Ux,item.Slot.Uy,item.Slot.Uz))
                                    throw new InvalidOperationException(
                                        "REV22.11 "+label+" view up differs from expected WCS +Z projection.");
                                var adjusted=NativeOrthoIsoFramingGuard.NormalizeHalfTurn(oldPose);
                                SetIsoViewportPose(targetDoc.Database,viewportId,adjusted);
                                var actualPose=ReadIsoViewportState(
                                    targetDoc.Database,viewportId);
                                Log("STEP8_"+label+"_ISO_TWIST_AFTER_DEG="+
                                    (actualPose.TwistRadians*180/Math.PI).ToString(
                                        "0.######",CultureInfo.InvariantCulture));
                                Log("STEP8_"+label+"_ISO_VIEWCENTER_AFTER="+
                                    Point(actualPose.CenterX,actualPose.CenterY,0));
                                bool frameMatch=NativeOrthoIsoFramingGuard.Matches(
                                    adjusted,actualPose);
                                Log("STEP8_"+label+"_TWIST_NORMALIZED_CHECK="+
                                    (NativeOrthoIsoTwistGuard.IsZeroTwist(
                                        actualPose.TwistRadians)?"PASS":"FAIL"));
                                Log("STEP8_"+label+"_VIEWCENTER_COMPENSATED="+
                                    adjusted.AppliedHalfTurn);
                                Log("STEP8_"+label+"_ISO_FRAME_COORDINATES_CHECK="+
                                    (frameMatch?"PASS":"FAIL"));
                                if(!frameMatch)
                                    throw new InvalidOperationException(
                                        "REV22.11 "+label+
                                        " viewport twist/center correction did not persist.");
                            }
                            var plannedFrame=new NativeOrthoViewportPaperGuard.Frame(
                                label,item.Center.X,item.Center.Y,item.Width,item.Height,
                                candidate.Scale,item.Slot.ModelHmm);
                            var actualFrame=ReadPaperViewportFrame(
                                targetDoc.Database,viewportId,label);
                            Log("STEP8_"+label+"_PLANNED_VIEWPORT="+
                                Point(plannedFrame.X,plannedFrame.Y,0)+" SIZE="+
                                Point(plannedFrame.W,plannedFrame.H,0)+
                                " SCALE="+plannedFrame.Scale);
                            Log("STEP8_"+label+"_ACTUAL_VIEWPORT="+
                                Point(actualFrame.X,actualFrame.Y,0)+" SIZE="+
                                Point(actualFrame.W,actualFrame.H,0)+
                                " SCALE="+actualFrame.Scale+" VIEW_HEIGHT="+actualFrame.ViewHeight);
                            string frameCheck=NativeOrthoViewportPaperGuard.Validate(
                                plannedFrame,actualFrame,paperWidth,paperHeight,confirmedFrames);
                            Log("STEP8_"+label+"_ACTUAL_PAPER_LAYOUT_CHECK="+frameCheck);
                            if(frameCheck!="PASS")
                                throw new InvalidOperationException("REV22.11 "+label+
                                    " failed actual paper-space post-generation guard: "+
                                    frameCheck+". Stop before creating more views; "+
                                    "retain this partially registered copied test DWG.");
                            confirmedFrames.Add(actualFrame);
                            if(NativeOrthoAxialEditorPreset.IsAxial(label))
                            {
                                // REV22.11: read back the ACTUAL persisted
                                // IDwgViewDef.ViewType, not the requested label.
                                string storedAxial=ReadNativeStoredViewType(
                                    nativeView,viewName);
                                bool savedAxialMatch=string.Equals(
                                    storedAxial,item.TypeName,StringComparison.Ordinal);
                                Log("STEP8_"+label+"_SAVED_VIEWDEF_TYPE="+storedAxial);
                                Log("STEP8_"+label+"_SAVED_TYPE_CHECK="+
                                    (savedAxialMatch?"PASS":"FAIL"));
                                if(!savedAxialMatch)
                                    throw new InvalidOperationException(
                                        "REV22.11 "+label+" native Editor preset "+
                                        "not persisted as expected. Requested: "+
                                        item.TypeName+" actual: "+storedAxial);
                            }
                            if(NativeOrthoIsoViewType.IsIsometric(label))
                            {
                                // REV22.11: Edit View uses persisted native
                                // IDwgViewDef.ViewType, not only viewport angle.
                                string storedType=ReadNativeStoredViewType(nativeView,viewName);
                                Log("STEP8_"+label+"_SAVED_VIEWDEF_TYPE="+storedType);
                                if(storedType.StartsWith("UNAVAILABLE:",StringComparison.Ordinal))
                                    Log("STEP8_"+label+"_SAVED_TYPE_CHECK=INCONCLUSIVE");
                                else
                                {
                                    bool exact=NativeOrthoEditorPreset.IsExactIsoPreset(
                                        label,storedType);
                                    Log("STEP8_"+label+"_SAVED_TYPE_CHECK="+(exact?"PASS":"FAIL"));
                                    if(!exact)throw new InvalidOperationException(
                                        "REV22.11 "+label+
                                        " was not saved with the native Ortho Editor "+
                                        "preset identifier: "+storedType);
                                }
                                Vector3d actual=ReadViewportCamera(
                                    targetDoc.Database,viewportId);
                                bool isoCameraMatches=NativeOrthoIsoViewType.MatchesCamera(
                                    label,actual.X,actual.Y,actual.Z,
                                    item.Slot.Dx,item.Slot.Dy,item.Slot.Dz);
                                Log("STEP8_"+label+"_ISOMETRIC_CAMERA_AFTER="+
                                    Point(actual.X,actual.Y,actual.Z));
                                Log("STEP8_"+label+"_ISOMETRIC_CAMERA_MATCH_AFTER="+isoCameraMatches);
                                if(!isoCameraMatches)
                                    throw new InvalidOperationException(
                                        "REV22.11 "+label+
                                        " native generator reverted camera to Current View. "+
                                        "Do NOT trust this experimental test DWG.");
                            }
                        }
                        finally
                        {
                            if(nativeView is IDisposable dispose)dispose.Dispose();
                        }
                        completed.Add(viewName);
                        string after=ViewCatalog(part.OrthoPart,dest);
                        Log("STEP9_"+label+"_VIEW_CATALOG="+after);
                        bool registered=CatalogMatches(after,completed);
                        Log("STEP9_"+label+"_REGISTERED="+registered);
                        if(!registered)throw new InvalidOperationException(
                            label+": internal native generator returned, but the expected " +
                            "model-linked Ortho view was not registered. Stop.");
                    }
                }
                finally { if(cube is IDisposable disposeCube)disposeCube.Dispose(); }
            }
            Log("STEP10_ALL_TEN_NATIVE_VIEWS_REGISTERED=True");
            Log("STEP10_REV22_TEN_GROUP_PLACED_WITH_ONE_CLICK=True");
            var newFrames=PaperViewports(targetDoc.Database,paperLayout)
                .Where(v=>!priorHandles.Contains(v.Handle)).ToList();
            Log("STEP10_NEW_PAPER_VIEWPORT_COUNT="+newFrames.Count);
            bool overlaps=false,possibleTitleBlock=false;
            for(int i=0;i<newFrames.Count;i++)
            {
                var v=newFrames[i];
                Log("STEP10_VIEWPORT="+v.Handle+" CENTER="+Point(v.X,v.Y,0)+
                    " SIZE="+Point(v.W,v.H,0));
                if(v.X+v.W/2>paperWidth*.78||v.Y-v.H/2<paperHeight*.12)
                    possibleTitleBlock=true;
                for(int j=i+1;j<newFrames.Count;j++)
                {
                    var w=newFrames[j];
                    if(v.X-v.W/2<w.X+w.W/2&&w.X-w.W/2<v.X+v.W/2&&
                       v.Y-v.H/2<w.Y+w.H/2&&w.Y-w.H/2<v.Y+v.H/2)
                    {
                        overlaps=true;
                        Log("STEP10_OVERLAPPING_VIEWPORTS="+v.Handle+","+w.Handle);
                    }
                }
            }
            Log("STEP10_ACTUAL_VIEWPORT_OVERLAP_DETECTED="+overlaps);
            Log("STEP10_POSSIBLE_TITLEBLOCK_COLLISION="+possibleTitleBlock);
            Log("STEP10_PLACEMENT_PROBE="+
                (newFrames.Count>=10?"INSPECTED":"INCONCLUSIVE_VERIFY_VISUALLY"));
            Log("NATIVE_ORTHO_VIEW_COUNT="+completed.Count);
            Log("UPDATE_VIEW_MANUAL_TEST_REQUIRED=TRUE");
            Log("FINAL_STATUS=TEN_NATIVE_VIEWS_REGISTERED_CHECK_PLACEMENT_AND_UPDATE");
            SafeWriteToActiveEditor(
                "REV22.11: TEN native views registered. SAVE; verify placement and Update View for each.");


                }
                catch (TargetInvocationException ex)
                {
                    Log("NATIVE_DOCUMENT_CONTEXT_EXCEPTION=" +
                        (ex.InnerException?.ToString() ?? ex.ToString()));
                    SafeWriteToActiveEditor("REV22 native document-context API error: " +
                        (ex.InnerException?.Message ?? ex.Message));
                }
                catch (System.Exception ex)
                {
                    Log("NATIVE_DOCUMENT_CONTEXT_ERROR=" + ex);
                    SafeWriteToActiveEditor("REV22 native document-context error: " +
                        ex.Message);
                }
                finally
                {
                    if (workingDbWasChanged)
                    {
                        // Re-synchronize to whichever drawing is now active.
                        // Do not restore the old source working DB while the
                        // newly registered Ortho DWG remains the active tab.
                        try
                        {
                            Document? active = AcadApp.DocumentManager.MdiActiveDocument;
                            if (active != null)
                                HostApplicationServices.WorkingDatabase = active.Database;
                            else if (originalWorkingDatabase != null)
                                HostApplicationServices.WorkingDatabase = originalWorkingDatabase;
                            Log("STEP5_WORKING_DB_FINAL_SYNC=" +
                                (HostApplicationServices.WorkingDatabase?.Filename ?? "UNKNOWN"));
                        }
                        catch (System.Exception ex)
                        {
                            Log("STEP5_WORKING_DB_FINAL_SYNC_ERROR=" + ex.Message);
                        }
                    }
                }
                return Task.CompletedTask;
            }, null);
            Log("STEP5_TARGET_COMMAND_CONTEXT_COMPLETED");
        }
        catch (TargetInvocationException ex)
        {
            Log("NATIVE_EXCEPTION=" + (ex.InnerException?.ToString() ?? ex.ToString()));
            SafeWriteToActiveEditor("REV22 native API error: " + (ex.InnerException?.Message ?? ex.Message));
        }
        catch (System.Exception ex)
        {
            Log("ERROR=" + ex);
            SafeWriteToActiveEditor("REV22 stopped: " + ex.Message);
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
            SafeWriteToActiveEditor("REV22 RESULT: " + report);
        }
    }

    // REV22.11 read-only API reflection; verified from native DLL metadata:
    // GetDwgSheetFromOrthoView(PlantOrthoView,out IDwgSheetDef) and
    // GetViewDefByViewName(string,IDwgSheetDef,out IDwgViewDef).
    private static string ReadNativeStoredViewType(object orthoView,string viewName)
    {
        try
        {
            Type? nativeUi=FindType(AppDomain.CurrentDomain.GetAssemblies(),
                "Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.UIUtils");
            if(nativeUi==null)return "UNAVAILABLE:UIUtils missing";
            const BindingFlags flags=BindingFlags.Static|
                BindingFlags.NonPublic|BindingFlags.Public;
            MethodInfo? getSheet=nativeUi.GetMethods(flags).FirstOrDefault(m=>
            {
                if(m.Name!="GetDwgSheetFromOrthoView"||
                    m.ReturnType!=typeof(bool))return false;
                ParameterInfo[] args=m.GetParameters();
                return args.Length==2&&
                    args[0].ParameterType.IsInstanceOfType(orthoView)&&
                    args[1].ParameterType.IsByRef;
            });
            if(getSheet==null)return "UNAVAILABLE:Native sheet method missing";
            object?[] sheetArgs={orthoView,null};
            if(getSheet.Invoke(null,sheetArgs) is not bool hasSheet||!hasSheet||
                sheetArgs[1]==null)
                return "UNAVAILABLE:Native sheet definition unavailable";
            object sheet=sheetArgs[1]!;
            MethodInfo? getDef=nativeUi.GetMethods(flags).FirstOrDefault(m=>
            {
                if(m.Name!="GetViewDefByViewName"||
                    m.ReturnType!=typeof(bool))return false;
                ParameterInfo[] args=m.GetParameters();
                return args.Length==3&&args[0].ParameterType==typeof(string)&&
                    args[1].ParameterType.IsInstanceOfType(sheet)&&
                    args[2].ParameterType.IsByRef;
            });
            if(getDef==null)return "UNAVAILABLE:Native view method missing";
            object?[] viewArgs={viewName,sheet,null};
            if(getDef.Invoke(null,viewArgs) is not bool hasView||!hasView||
                viewArgs[2]==null)
                return "UNAVAILABLE:Saved native view definition not found";
            object nativeDef=viewArgs[2]!;
            PropertyInfo? viewType=nativeDef.GetType().GetProperty(
                "ViewType",BindingFlags.Public|BindingFlags.NonPublic|
                BindingFlags.Instance);
            if(viewType==null)
                viewType=nativeDef.GetType().GetInterfaces()
                    .Select(t=>t.GetProperty("ViewType"))
                    .FirstOrDefault(p=>p!=null);
            return viewType?.GetValue(nativeDef)?.ToString() ??
                "UNAVAILABLE:Native ViewType inaccessible";
        }
        catch(System.Exception ex)
        {
            return "UNAVAILABLE:"+
                ((ex is TargetInvocationException wrapped?
                    wrapped.InnerException:ex)?.Message??ex.Message);
        }
    }

    // REV22.11: read ONLY the real viewport after Autodesk's internal
    // generation. A plan-time frame is not a reliable paper-space result.
    private static NativeOrthoViewportPaperGuard.Frame ReadPaperViewportFrame(
        Database db,ObjectId viewportId,string name)
    {
        if(viewportId.IsNull||!viewportId.IsValid)
            throw new InvalidOperationException("REV22.11 invalid generated viewport ID.");
        using Transaction tr=db.TransactionManager.StartTransaction();
        if(tr.GetObject(viewportId,OpenMode.ForRead,false) is not Viewport vp)
            throw new InvalidOperationException("REV22.11 viewport missing after generation.");
        var frame=new NativeOrthoViewportPaperGuard.Frame(name,
            vp.CenterPoint.X,vp.CenterPoint.Y,vp.Width,vp.Height,
            vp.CustomScale,vp.ViewHeight);
        tr.Commit();
        return frame;
    }

    // REV22.11: this is the only new drawing mutation. Its scope is the
    // freshly created ISO viewport in an isolated copied test Ortho DWG.
    // The Autodesk native ViewDef is not edited directly.
    // REV22.11: change only newly created isometric paper-space viewports.
    // Do not edit Plant native IDwgViewDef or Ortho.dcf.
    private static NativeOrthoIsoFramingGuard.ViewState ReadIsoViewportState(
        Database db,ObjectId viewportId)
    {
        if(viewportId.IsNull||!viewportId.IsValid)
            throw new InvalidOperationException("REV22.11 generated ISO viewport id invalid.");
        using Transaction tr=db.TransactionManager.StartTransaction();
        if(tr.GetObject(viewportId,OpenMode.ForRead,false) is not Viewport vp)
            throw new InvalidOperationException("REV22.11 generated ISO viewport missing.");
        var center=vp.ViewCenter;
        var result=new NativeOrthoIsoFramingGuard.ViewState(
            center.X,center.Y,vp.TwistAngle);
        tr.Commit();
        return result;
    }

    private static void SetIsoViewportPose(Database db,ObjectId viewportId,
        NativeOrthoIsoFramingGuard.Adjustment adjusted)
    {
        if(viewportId.IsNull||!viewportId.IsValid)
            throw new InvalidOperationException("REV22.11 generated ISO viewport id invalid.");
        using Transaction tr=db.TransactionManager.StartTransaction();
        if(tr.GetObject(viewportId,OpenMode.ForWrite,false) is not Viewport vp)
            throw new InvalidOperationException("REV22.11 generated ISO viewport missing.");
        vp.TwistAngle=adjusted.TwistRadians;
        vp.ViewCenter=new Point2d(adjusted.CenterX,adjusted.CenterY);
        tr.Commit();
    }

    private static Vector3d ReadViewportCamera(Database db,ObjectId viewportId)
    {
        if(viewportId.IsNull||!viewportId.IsValid)
            throw new InvalidOperationException("REV22.11 invalid native viewport id.");
        using Transaction tr=db.TransactionManager.StartTransaction();
        if(tr.GetObject(viewportId,OpenMode.ForRead,false) is not Viewport vp)
            throw new InvalidOperationException("REV22.11 native viewport no longer exists.");
        Vector3d dir=vp.ViewDirection;
        tr.Commit();
        return dir;
    }

    private static ObjectId CreateExperimentalViewport(
        Database db,string layoutName,Point3d paperCenter,
        double frameWidth,double frameHeight,Vector3d viewDirection,
        Point3d viewTarget,double scale,double modelHeight)
    {
        if(frameWidth<=0||frameHeight<=0||scale<=0||modelHeight<=0)
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        using Transaction tr=db.TransactionManager.StartTransaction();
        var layouts=(DBDictionary)tr.GetObject(db.LayoutDictionaryId,OpenMode.ForRead);
        if(!layouts.Contains(layoutName))
            throw new InvalidOperationException("Native paper-space layout disappeared.");
        var lay=(Layout)tr.GetObject(layouts.GetAt(layoutName),OpenMode.ForRead);
        var paper=(BlockTableRecord)tr.GetObject(lay.BlockTableRecordId,OpenMode.ForWrite);
        using var viewport=new Viewport
        {
            CenterPoint=paperCenter,
            Width=frameWidth,Height=frameHeight,
            ViewDirection=viewDirection,
            ViewTarget=viewTarget,
            ViewCenter=Point2d.Origin,
            ViewHeight=modelHeight,
            CustomScale=scale
        };
        ObjectId id=paper.AppendEntity(viewport);
        tr.AddNewlyCreatedDBObject(viewport,true);
        viewport.On=true;
        tr.Commit();
        if(id.IsNull||!id.IsValid)
            throw new InvalidOperationException("New native paper viewport has invalid ObjectId.");
        return id;
    }

    private sealed record PaperViewport(string Handle,double X,double Y,double W,double H);
    private static List<PaperViewport> PaperViewports(Database db,string layoutName)
    {
        var items=new List<PaperViewport>();
        try
        {
            using Transaction tr=db.TransactionManager.StartTransaction();
            var layouts=(DBDictionary)tr.GetObject(db.LayoutDictionaryId,OpenMode.ForRead);
            if(!layouts.Contains(layoutName))return items;
            var lay=(Layout)tr.GetObject(layouts.GetAt(layoutName),OpenMode.ForRead);
            var ps=(BlockTableRecord)tr.GetObject(lay.BlockTableRecordId,OpenMode.ForRead);
            foreach(ObjectId id in ps)
                if(tr.GetObject(id,OpenMode.ForRead) is Viewport vp &&
                    vp.Width>0&&vp.Height>0)
                    items.Add(new PaperViewport(vp.Handle.ToString(),
                        vp.CenterPoint.X,vp.CenterPoint.Y,vp.Width,vp.Height));
        }
        catch(System.Exception)
        {
            // Optional placement probe; native view catalog is authoritative.
        }
        return items;
    }
    private static bool CatalogMatches(string status,IReadOnlyCollection<string> expected)
    {
        const string prefix="EXISTING_NATIVE_VIEWS:";
        if(!status.StartsWith(prefix,StringComparison.Ordinal))return false;
        string[] names=status[prefix.Length..].Split(',',
            StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(names.Length!=expected.Count)return false;
        var set=new HashSet<string>(names,StringComparer.OrdinalIgnoreCase);
        return expected.All(set.Contains);
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
                            "EXISTING_NATIVE_VIEWS:" + string.Join(",",views.Keys);
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
