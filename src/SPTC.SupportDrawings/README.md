# SPTC Support Drawings — REV18: Real Native OrthoCube + Public TOP Generation Experiment

**Host:** Autodesk AutoCAD Plant 3D 2025, Windows x64, .NET 8.

## Established working baseline

- REV13 demonstrated dynamic model names / selected support geometry across `TEST3.dwg` and `TEST4.dwg`.
- REV16 created a genuine native Plant 3D Ortho project drawing by cloning a **blank wizard-created Ortho seed**, registering the new drawing through `AddPnPDrawingFile` in the `Orthos/DWGs` folder. The user confirmed that **New View** and **Update View** both worked in that clone.
- REV17 confirmed native project source/target validity and the actual A1 **portrait** `Title Block` layout, **594 × 841 mm**, including a TOP scale candidate of **1:2** for the selected support. REV17 was **read-only**.

## NEW: SPTC_ORTHO18_TOP_API_TEST

This is an **experimental test in a COPIED/BACKED-UP PROJECT ONLY**, not the final unattended Ortho generator. It uses the **public** Plant 3D Ortho APIs and explicitly acknowledges the last interactive obstacle.

### Execution stages

1. Start in a **NEW and EMPTY** `_ORTHO_R16CLONE_` native DWG, opened in the project's `Orthos/DWGs` via Project Manager, with the real paper-space layout active. **DO NOT** run it on the existing working REV16 drawing containing a native TOP view.
2. Select the fresh `SPTC_*_TOP_PREFLIGHT_REV17.json`. The report provides the dynamic 3D source path, support OrthoCube bounds, candidate TOP scale, and paper dimensions.
3. Select where to save a `*_REV18_TOP_API_RESULT.txt` diagnostic log, preferably outside the project. The log is updated before every native call for post-crash analysis.
4. Verify the report is REV17 TOP preflight, the current project still recognizes its source DWG through `OrthoDocUtils.IsValid3dDrawing`, the active Ortho target is inside the current project's native Ortho root, and that the current A1 layout matches the reported dimensions.
5. Check the current drawing's registered GUID and native `GetAllViewNamesInOrthoDwgByGuid` output. **Fail closed** if the clone has any existing native views, cannot be identified, or if any required API is missing.
6. Instantiate a **real** `PlantOrthoCube` through its public constructor and call `InitializeCube(Extents3d)`. Compare the resulting native `Extents` against the REV17 bounds; request a native solid with `MakeCubeCopy()`. This is the first actual native OrthoCube initialization in the automation work.
7. A GUI warning offers a **safe exit after cube verification**. Choosing **No** saves the cube-validation log and does **NOT** generate any view. Choosing **Yes**, then typing **`TOP API TEST`**, explicitly authorizes the next API test in the copied project.
8. If authorized, instantiate public `PlantOrthoView(bool,bool)` and call its **public** `CreateOrthos(StringCollection, viewName, scale, width, height, depth, Top, "TopView", Vector3d, Vector3d, Point3d, Point3d, Solid3d)` with the current source/cube parameters.
9. **Expect interactive Plant 3D UI**: the constructor may show the native Ortho drawing-selection modal, and `CreateOrthos` still runs `ProcessViewportPlacement` / `Editor.Drag`. If prompted for Ortho drawing, only select the currently opened **fresh R16 clone**. If prompted for viewport insertion, choose a safe location in the A1 paper-space layout. Cancel immediately if the command targets the wrong drawing.
10. Save the test Ortho and confirm **New View** and **Update View** work. Upload the diagnostic TXT. The log distinguishes API entered/returned from actual verified model-linked output; no automatic success is claimed.

### Run sequence

- First, in the copied project, create a fresh empty native seed through Project Manager, then use `SPTC_ORTHO16CLONE_TEST` to generate another registered, **empty** Ortho clone. Do not select the already populated, verified REV16 Ortho.
- Run `SPTC_ORTHO17_TOP_PREFLIGHT` again if the source model or A1 drawing setup changed. The earlier REV17 report can be used for the same unchanged source geometry and same A1 sheet size, even if it referenced another REV16 clone.
- Restart Plant 3D and `NETLOAD` REV18 `SPTC.SupportDrawings.dll`.
- Open the **new** empty REV16 clone in paper space and run `SPTC_ORTHO18_TOP_API_TEST`.
- Choose REV17 JSON and a diagnostic TXT destination. The first stage verifies the actual native OrthoCube.
- Choose **No** to stop safely, or **Yes** + type **TOP API TEST** to attempt native TOP generation. This second stage is **high-risk experimental** and may display standard Plant dialogs.
- After completion, confirm the native view is model-linked and **Update View** succeeds.

## Important safeguards

- Native project read operations precede any generation calls; source model and target Ortho drawing are from the **active project**, never hard-coded examples.
- Enforces a new empty REV16 clone by workflow marker and real native registered-view lookup.
- Does **not** edit `Ortho.dcf`, invoke the non-public `GenerateOrthos`, send synthetic keystrokes, overwrite files, or assume the constructor/dialogs are silent.
- The selected OrthoCube **does not whitelist individual objects**: other source model components inside the cube may appear in the generated view.
- The REV17 candidate paper center is **not automatically applied**: the public API still calls a viewport jig. No claim is made that the project now has no-dialog generation.
- The `PlantOrthoView(bool,bool)` boolean arguments have not been verified as a supported constructor contract, and Autodesk can change them; the test uses `false,false` and records constructor failures. That is why this command is **not enabled in the production `SPTC_SUPPORT2D` path**.

## Expected log interpretation

| Log item | Meaning |
|---|---|
| `STEP1_NATIVE_CUBE_INITIALIZE_RETURNED` | Native cube initializer completed |
| `STEP2_NATIVE_CUBE_EXTENTS_MATCHED=TRUE` | Native cube bounds agree with model geometry |
| `STEP3_NATIVE_CUBE_SOLID_CREATED=TRUE` | Generated real native Ortho cube solid in memory |
| `FINAL_STATUS=NATIVE_CUBE_PASS_GENERATION_NOT_AUTHORIZED` | User stopped safely before view writes |
| `STEP4_PUBLIC_PLANTORTHOVIEW_CONSTRUCTOR_RETURNED` | Public constructor returned; project session **not yet proven** |
| `STEP5_PUBLIC_CREATEORTHOS_ENTERED... ` | Public native TOP generation invoked; may prompt for placement |
| `STEP5_PUBLIC_CREATEORTHOS_RETURNED` | Native API returned, **not proof of an updateable view** |
| `EXCEPTION_NATIVE` | Autodesk public API failed; include complete exception in next review |

## Retained commands

`SPTC_ORTHO16CLONE_TEST` — validated native project drawing registration; `SPTC_ORTHO17_TOP_PREFLIGHT` — read-only sheet/TOP plan; `SPTC_SUPPORT2D` — existing interactive native command; `SPTC_ORTHOAUTOPLAN` and earlier diagnostics — unchanged.

## Source evidence

- `docs/Plant3D_2025_Ortho_API_DLL_Analysis_REV10.md` — static compiled Ortho API call path supplied by user's Autodesk DLL.
- Autodesk developer public API code example: https://forums.autodesk.com/t5/autocad-plant-3d-forum/create-an-orthographic-view-via-the-c-api/m-p/13629352
- Plant 3D official workflow: https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-09E67A9E-45F7-4392-BFB6-EF1ADAB1D46B.htm
