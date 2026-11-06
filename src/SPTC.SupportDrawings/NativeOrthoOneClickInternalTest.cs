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
/// REV21 isolated one-command native TOP experiment: select the live support in a
/// registered 3D source; clone a verified empty native Ortho Project Manager seed;
/// register, open, initialize the actual native OrthoCube; call only public
/// PlantOrthoView.CreateOrthos to generate a model-linked TOP view.
/// BACKUP/COPIED PROJECT ONLY. It does NOT patch Ortho.dcf or call private APIs.
/// Any Autodesk dialogs from the public API remain host-controlled.
/// </summary>
public sealed class NativeOrthoOneClickInternalTest
{
    [CommandMethod("SPTC_ORTHO21_ONECLICK_INTERNAL_TEST",
        CommandFlags.Modal | CommandFlags.Session)]
    public async void Run()
    {
        Document? sourceDoc = AcadApp.DocumentManager.MdiActiveDocument;
        if (sourceDoc == null) return;
        Editor ed = sourceDoc.Editor;
        string source = Path.GetFullPath(sourceDoc.Database.Filename ?? "");
        if (!File.Exists(source))
        {
            ed.WriteMessage("\nREV21: Open a saved project-registered 3D model before running the test.");
            return;
        }

        NativeOrthoProjectCheck.Result sourceCheck;
        try { sourceCheck = NativeOrthoProjectCheck.Inspect(source); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV21: Native source check failed: " + ex.Message);
            return;
        }
        if (!sourceCheck.SourceRegistrationVerified)
        {
            ed.WriteMessage("\nREV21: Source 3D drawing is not verified project-registered. No changes.");
            return;
        }

        double units = sourceDoc.Database.Insunits.ToString() switch
        {
            "Millimeters" => 1, "Centimeters" => 10, "Meters" => 1000,
            "Inches" => 25.4, "Feet" => 304.8, _ => 0
        };
        if (units <= 0)
        {
            ed.WriteMessage("\nREV21: Unsupported source units; expected a physical unit like Millimeters.");
            return;
        }

        PromptSelectionResult selection = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nREV21: Select all structure entities for ONE support: "
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
            ed.WriteMessage("\nREV21: All selected members must have valid 3D extents; invalid: " + invalidExtents);
            return;
        }

        NativeOrthoLayout.Plan layout;
        try { layout = NativeOrthoLayout.Build(geometry.Value, units); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV21: Invalid support OrthoCube: " + ex.Message);
            return;
        }
        ProjectPart part;
        try { part = CurrentOrthoProject(); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV21: Cannot resolve current Plant Ortho project: " + ex.Message);
            return;
        }

        string initial = Directory.Exists(Path.Combine(part.OrthoFolder, "DWGs")) ?
            Path.Combine(part.OrthoFolder, "DWGs") : part.OrthoFolder;
        using var picker = new WinForms.OpenFileDialog
        {
            Title = "REV21 - Select an EMPTY Project Manager-created native Ortho seed (not DWT)",
            Filter = "AutoCAD drawing (*.dwg)|*.dwg", CheckFileExists = true,
            InitialDirectory = initial, RestoreDirectory = true
        };
        if (picker.ShowDialog() != WinForms.DialogResult.OK) return;
        string seed = Path.GetFullPath(picker.FileName);
        if (!NativeOrthoRegistrationNames.IsInsideProjectDirectory(seed, part.ProjectRoot) ||
            !NativeOrthoRegistrationNames.IsInsideProjectDirectory(seed, part.OrthoFolder))
        {
            ed.WriteMessage("\nREV21: Seed must be within the active project's registered Orthos folder.");
            return;
        }
        if (!IsNativeOrthoValid(part.OrthoPart, seed))
        {
            ed.WriteMessage("\nREV21: Seed failed native OrthoDocUtils.IsValidOrthoDrawing.");
            return;
        }
        string seedState = ViewCatalog(part.OrthoPart, seed);
        if (seedState != "EMPTY_REGISTERED_ORTHO")
        {
            ed.WriteMessage("\nREV21: Seed must be registered and EMPTY; found: " + seedState);
            return;
        }

        string suffix = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..6];
        string file = NativeOrthoRegistrationNames.NewOrthoDrawingFileName(source, handles, suffix)
            .Replace("_ORTHO_R14_", "_ORTHO_R21ONCE_", StringComparison.Ordinal);
        string dest = Path.Combine(Path.GetDirectoryName(seed)!, file);
        if (File.Exists(dest))
        {
            ed.WriteMessage("\nREV21: Destination DWG exists. No changes.");
            return;
        }

        // The public SDK, not this command, decides the final viewport insertion.
        // No false claim that the proposed TOP center is passed into CreateOrthos.
        var popup = WinForms.MessageBox.Show(
            "REV21 - ONE-CLICK INTERNAL NATIVE TOP + FRONT + SIDE EXPERIMENT\n\n" +
            "SOURCE 3D: " + source + "\n" +
            "SELECTED MEMBERS: " + string.Join(", ", handles) + "\n" +
            "NATIVE EMPTY SEED: " + seed + "\n" +
            "NEW NATIVE ORTHO: " + dest + "\n\n" +
            "A NEW drawing will be created and registered to the ACTIVE Plant project.\n" +
            "ONE anchor click will position three proposed native Ortho views.\n" +
            "EXPERIMENTAL: this invokes Autodesk's NON-PUBLIC GenerateOrthos API.\n" +
            "Native view links, viewport state and Update View are NOT guaranteed.\n" +
            "Do not use your production project. Retain all test logs and drawings.\n\n" +
            "ONLY authorize in a separate COPIED/BACKED-UP TEST PROJECT.\n" +
            "Do you want to run this experiment?",
            "SPTC REV21 UNSUPPORTED INTERNAL API - COPIED TEST PROJECT ONLY",
            WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning,
            WinForms.MessageBoxDefaultButton.Button2);
        if (popup != WinForms.DialogResult.Yes) return;
        PromptResult typed = ed.GetString(new PromptStringOptions(
            "\nType REV21 TEST to register a NEW Ortho DWG in this copied project: ")
        { AllowSpaces = true });
        if (typed.Status != PromptStatus.OK ||
            !string.Equals(typed.StringResult.Trim(), "REV21 TEST", StringComparison.Ordinal))
        {
            ed.WriteMessage("\nREV21: Cancelled before any project write.");
            return;
        }

        string report = Path.Combine(Path.GetDirectoryName(dest)!,
            Path.GetFileNameWithoutExtension(dest) + "_REV21_RESULT.txt");
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
            Log("SPTC_REV21_ONE_COMMAND_NATIVE_TOP_TEST");
            Log("PROJECT=" + part.ProjectRoot);
            Log("SOURCE_MODEL=" + source);
            Log("SOURCE_HANDLES=" + string.Join(",", handles));
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
                // REV21.1: The FileShare overload can throw eFileSharingViolation
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
            NativeOrthoThreeViewPlan.Plan candidate =
                NativeOrthoThreeViewPlan.Build(layout, paperWidth, paperHeight);
            Log("STEP4_SHEET_LAYOUT=" + paperLayout);
            Log("STEP4_SHEET_MM=" + Point(paperWidth,paperHeight,0));
            Log("STEP4_COMMON_SCALE=1:" + candidate.Denominator);
            foreach(var slot in candidate.Views)
                Log("PROPOSED_" + slot.Name + "_CENTER=" +
                    Point(slot.CenterX,slot.CenterY,0) +
                    "_TILE=" + Point(slot.W,slot.H,0));
            Log("PROPOSED_LAYOUT_NOT_ENFORCED_BY_NATIVE_API=True");
            Log("PUBLIC_API_HAS_NO_INSERTION_POINT_ARGUMENT=TRUE");

            // REV21.3: A Session command can activate a different DWG without
            // entering that drawing's document/command execution context.
            // REV21.2 yielded sentinel +/-1e20 Extents after InitializeCube,
            // whereas REV18 succeeded when invoked within the Ortho document.
            // Schedule all native cube + view generation in target document
            // context, preserving the exact strict extents validation.
            Log("STEP5_SESSION_CONTEXT_IS_APPLICATION=" +
                AcadApp.DocumentManager.IsApplicationContext);
            Log("STEP5_TARGET_COMMAND_CONTEXT_REQUESTED");
            await AcadApp.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                // REV21.4: distinguish managed wrapper object identity from
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
                            "target after command-context synchronization. See REV21.4 log.");

            // REV21 ISOLATED EXPERIMENT: Non-public GenerateOrthos requires a
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
            var resolved=new List<(NativeOrthoThreeViewPlan.Slot Slot,object ViewEnum,string TypeName)>();
            foreach(var slot in candidate.Views)
            {
                string selected=slot.EnumName;
                if(slot.Name=="SIDE"&&!available.Any(x=>x.Equals(selected,StringComparison.OrdinalIgnoreCase)) &&
                    available.Any(x=>x.Equals("Side",StringComparison.OrdinalIgnoreCase)))
                    selected="Side";
                if(!available.Any(x=>x.Equals(selected,StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Native ViewType "+selected+" missing for "+slot.Name);
                resolved.Add((slot,Enum.Parse(nativeEnum,selected,true),
                    selected=="Side"?"SideView":slot.TypeName));
                Log("STEP5_"+slot.Name+"_ENUM="+selected);
            }
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
                    "This Plant 3D release is NOT safe for the REV21 experiment.");
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
                "\nREV21.1: click ONCE anywhere inside the Ortho sheet for TOP placement. "+
                "FRONT and SIDE will follow. If the three-view group does not fit "+
                "at that position, it will shift together to the nearest safe area.");
            PromptPointResult placement=targetDoc.Editor.GetPoint(
                new PromptPointOptions("\nREV21 ONE CLICK: Pick TOP group anchor in paper space: "));
            if(placement.Status!=PromptStatus.OK)
                throw new InvalidOperationException("REV21 cancelled: no group anchor point picked.");
            Point3d anchor=placement.Value;
            if(!double.IsFinite(anchor.X)||!double.IsFinite(anchor.Y)||
                !double.IsFinite(anchor.Z))
                throw new InvalidOperationException("REV21 received non-finite placement point.");
            Log("STEP5_ONE_CLICK_TOP_ANCHOR="+Point(anchor.X,anchor.Y,anchor.Z));
            Log("STEP5_ONE_CLICK_COUNT=1");

            // REV21.1: preserve the one-click workflow and translate the
            // complete group when the requested TOP anchor is too close to an
            // edge. The strict sheet/title-block and collision guards remain.
            // Log the translation BEFORE the first unsupported native call.
            var adjustedGroup=NativeOrthoOneClickPlan.BuildAdjusted(
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
                    "\nREV21.1: moved the complete three-view group to fit "+
                    "the sheet. Effective TOP center: ("+
                    adjustedGroup.EffectiveTopX.ToString("0.#",CultureInfo.InvariantCulture)+
                    ", "+adjustedGroup.EffectiveTopY.ToString("0.#",CultureInfo.InvariantCulture)+
                    "). See REV21_RESULT.txt for exact offset.");
            var planned=new List<(NativeOrthoThreeViewPlan.Slot Slot,
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
                        string viewName="SPTC_"+label+"_R21_"+stamp;
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
                            object[] args={
                                new StringCollection{source},viewName,item.TypeName,
                                new Point3d(bounds.MinX,bounds.MinY,bounds.MinZ),
                                new Point3d(bounds.MaxX,bounds.MaxY,bounds.MaxZ),
                                item.ViewEnum,item.Center,
                                new Vector3d(item.Slot.Dx,item.Slot.Dy,item.Slot.Dz),
                                new Vector3d(item.Slot.Ux,item.Slot.Uy,item.Slot.Uz),
                                viewportId,bounds.SizeX,bounds.SizeY,bounds.SizeZ,
                                candidate.Scale,0.0,solid
                            };
                            Log("STEP8_"+label+"_PRIVATE_GENERATEORTHOS_ENTERED");
                            generate.Invoke(nativeView,args);
                            Log("STEP8_"+label+"_PRIVATE_GENERATEORTHOS_RETURNED");
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
            Log("STEP10_TOP_FRONT_SIDE_NATIVE_REGISTERED=True");
            Log("STEP10_REV21_GROUP_PLACED_WITH_ONE_CLICK=True");
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
                (newFrames.Count>=3?"INSPECTED":"INCONCLUSIVE_VERIFY_VISUALLY"));
            Log("NATIVE_ORTHO_VIEW_COUNT="+completed.Count);
            Log("UPDATE_VIEW_MANUAL_TEST_REQUIRED=TRUE");
            Log("FINAL_STATUS=THREE_NATIVE_VIEWS_REGISTERED_CHECK_PLACEMENT_AND_UPDATE");
            SafeWriteToActiveEditor(
                "REV21: TOP, FRONT, SIDE native views registered. SAVE; verify placement and Update View.");


                }
                catch (TargetInvocationException ex)
                {
                    Log("NATIVE_DOCUMENT_CONTEXT_EXCEPTION=" +
                        (ex.InnerException?.ToString() ?? ex.ToString()));
                    SafeWriteToActiveEditor("REV21 native document-context API error: " +
                        (ex.InnerException?.Message ?? ex.Message));
                }
                catch (System.Exception ex)
                {
                    Log("NATIVE_DOCUMENT_CONTEXT_ERROR=" + ex);
                    SafeWriteToActiveEditor("REV21 native document-context error: " +
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
            SafeWriteToActiveEditor("REV21 native API error: " + (ex.InnerException?.Message ?? ex.Message));
        }
        catch (System.Exception ex)
        {
            Log("ERROR=" + ex);
            SafeWriteToActiveEditor("REV21 stopped: " + ex.Message);
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
            SafeWriteToActiveEditor("REV21 RESULT: " + report);
        }
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
