# SPTC Support Drawings — REV12 (Fix actual native 3D model registration check)

**Host:** AutoCAD Plant 3D 2025, Windows x64, the user's .NET 8 installation.

## Why REV11 returned an API mismatch

The actual user-generated `SPTC_ORTHO_AUTOPLAN_REV11.json` reported:

- `SourceRegistrationVerified: false`
- `SourceRegistrationStatus: "Read-only IsValid3dDrawing(string, Project, out string) API unavailable/incompatible."`
- `OrthoProjectPart: Autodesk.ProcessPower.P3dProjectParts.OrthoProject`

**This did not establish that the 3D model was unregistered.** The REV11 reflection code was passing `PlantApplication.CurrentProject` (a `PlantProject`) to a native method whose parameter is `Autodesk.ProcessPower.ProjectManager.Project`.

Direct inspection of the user-provided .NET 8 Plant 3D 2025 assemblies confirms:

- `Autodesk.ProcessPower.ProjectManager.PlantProject` inherits `System.Object`; it is **not** an Autodesk `Project`.
- `Autodesk.ProcessPower.P3dProjectParts.OrthoProject` derives from Autodesk `Project`.
- `Autodesk.ProcessPower.P3dProjectParts.OrthoDocUtils.IsValid3dDrawing(string, Project, ref string)` is public and static.
- Its compiled body checks the project part type obtained through `PnPProjectUtils.GetProjectPartTypeFromDrawingFile` against **`Piping`**, after validating the DWG.

## REV12 changes

The existing command `SPTC_ORTHOAUTOPLAN` now creates `SPTC_ORTHO_AUTOPLAN_REV12.json`.

- Retrieves the active `PlantProject`, then **separately** requests `CurrentProject.ProjectParts["Piping"]` for 3D model source validation and `ProjectParts["Ortho"]` for an Ortho project summary.
- Matches `IsValid3dDrawing` on its actual signature, then checks the **Piping** project part is assignable to the expected Autodesk `Project` parameter.
- Calls the read-only native API with the **full source DWG path and Piping project part**, collecting its returned `bool` and reason string.
- Records `SourceRegistrationStatus`, `ValidationProjectPart`, and `NativeValidationApiSignature` in the JSON. An API mismatch is now reported distinctly from an actual native `false` validation result.
- Leaves the selected support handles, A3 view plan, axes-specific OrthoCube padding, and all other REV11 behavior intact.
- No external Autodesk DLLs are committed or packaged. The plugin uses the installed native APIs through reflection.

### Geometry from the latest uploaded REV11 JSON

The latest `SAMPLE_2ND STAGE.dwg` selection lists handles `8D1` and `8D2` with a support envelope of approximately **451.850 × 75 × 376.850 mm** and a padded OrthoCube of **542.219 × 95 × 452.219 mm**. Candidate Top view is **1:5**, Front/Side **1:10**.

Earlier REV09/REV10 reports on a file with the same name and handles had *different model extents* (**1224.392 × 75 × 1149.392 mm**). This is not automatically an error, but if the file path and support handles have not intentionally changed, visually check the model and project copy. REV12 always calculates from the **currently selected objects**, never from previous JSON coordinates.

## Test instructions

1. Close and restart Plant 3D 2025, then `NETLOAD` the REV12 DLL.
2. Open the **same source model** in a **backup copy** of the project.
3. Enter `SPTC_ORTHOAUTOPLAN`, select the two support members, and save `SPTC_ORTHO_AUTOPLAN_REV12.json`.
4. Inspect `SourceRegistrationVerified`, `SourceRegistrationStatus`, `ValidationProjectPart`, and `NativeValidationApiSignature`.
5. If the native method returns `false`, check the actual API reason and confirm the source model exists in the project's **Piping** drawing list. Do not interpret a lack of error as successful registration.
6. If `Piping` is unavailable, report the exact project-part result instead of trying to register the DWG automatically.

## Current scope and future native Ortho generator

This remains a read-only **preflight** revision. The requested one-command bypass of the **Select Orthographic Drawing** dialog, native OrthoCube editing, and viewport placement is not implemented. Public `PlantOrthoView.CreateOrthos` invokes interactive placement; its internal, non-public `GenerateOrthos` method cannot safely be called without validated project/session state.

The following established commands remain: `SPTC_SUPPORT2D` (interactive native Ortho), `SPTC_SUPPORT2DLINEWORK`, `SPTC_SUPPORT2DBATCH`, `SPTC_ORTHOAUTOPROBE`, `SPTC_ORTHOAUTOPLAN`, `SPTC_SUPPORTEXPORT`, `SPTC_SUPPORTBATCH`, `SPTC_SUPPORTMANAGER`, and `SPTC_SUPPORTINSPECT`.
