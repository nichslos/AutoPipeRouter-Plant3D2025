# SPTC Support Drawings — REV 11 (Source Registration + Tight OrthoCube)

**Target:** AutoCAD Plant 3D 2025, Windows x64, the .NET 8-based installation.

## Grounding: actual REV09 / REV10 test files

The user's `SPTC_Ortho_API_Probe_REV09.txt` and `SPTC_ORTHO_AUTOPLAN_REV10.json`, captured in Plant 3D 2025, establish:

- Active project `TOSHIBA`, source model under `Plant 3D Models/03_HOLD/SAMPLE_2ND STAGE.dwg`.
- Two selected structural support objects with AutoCAD handles **8D1** and **8D2**.
- Support bounding dimensions **1224.392 × 75 × 1149.392 mm** (X/Y/Z), all extents readable.
- Loaded `PnP3dOrthoDrawingsUI` and `PnP3dOrthoProjectPart` version **16.0.58.1102**, running .NET 8.
- Native `PlantOrthoView.CreateOrthos(...)` and `OrthoDocUtils.IsValid3dDrawing(...)` exposed; `OrthoProject.AddPnPDrawingFile(...)` also exposed.
- REV10 source registration flag was **false (not checked)**, not confirmation the file was invalid.
- REV10's single 122.439 mm margin on **all axes** expanded a **75 mm-thick support** to a **319.88 mm OrthoCube Y span**. That risks capturing neighboring model geometry.

## REV11 corrections

**Command:** `SPTC_ORTHOAUTOPLAN` now creates `SPTC_ORTHO_AUTOPLAN_REV11.json`.

1. Uses **axis-specific 10% padding**, with a minimum of **10 mm** on each side of each axis. This avoids excessive clearance perpendicular to narrow support members.
2. Calls the installed read-only `OrthoDocUtils.IsValid3dDrawing(string, Project, out string)` via reflection **only when the matching public static method exists**. Reports `SourceRegistrationVerified` and `SourceRegistrationStatus`, without fabricating registration status.
3. Reads the current project's **Ortho project part** and available project Ortho drawing names where the public API allows it.
4. Retains selected handles, model/cube extents, top/front/side view directions, A3 paper positions and candidate scales. Stores **X/Y/Z margin fields** separately instead of one ambiguous margin.
5. Leaves all model objects, project drawing records and native Ortho view definitions untouched.

For the **supplied support's geometry** in millimeters, expected per-side padding is approximately X=122.439 mm, Y=10 mm, Z=114.939 mm, giving a proposed OrthoCube dimension of approximately **1469.27 × 95 × 1379.27 mm**. Candidate A3 scales remain Top 1:10, Front 1:20 and Side 1:20.

**This does not guarantee isolation of exactly two supports:** nearby objects inside the cube may still appear in a native Ortho view; cube volume alone does not provide an object whitelist.

## How to test

1. Restart Plant 3D 2025 and load REV11 `SPTC.SupportDrawings.dll` with `NETLOAD`.
2. Open the **actual registered** source project DWG in a backup of the `TOSHIBA` project.
3. Run `SPTC_ORTHOAUTOPLAN`, select the exact two structural support members, press Enter, and save `SPTC_ORTHO_AUTOPLAN_REV11.json`.
4. Inspect `SourceRegistrationVerified`, `SourceRegistrationStatus`, `OrthoProjectPart`, X/Y/Z clearance, and candidate scales.
5. If registration is reported false, read `SourceRegistrationStatus` before attempting native generation. The folder name `03_HOLD` alone does **not** establish registration status.
6. The existing `SPTC_ORTHOAUTOPROBE` can still be used for expanded API signatures.

## Native Ortho automation limitation

**The requested one-command creation, automatic Ortho drawing selection, OrthoCube positioning and unattended view placement are still NOT implemented.** Static inspection of the uploaded Ortho DLLs indicates the public `PlantOrthoView.CreateOrthos()` path still invokes `Editor.Drag`, and project drawing selection has a modal dialog. The non-public `GenerateOrthos()` has an explicit insertion-point argument, but invoking it without verified internal session state poses project corruption risks.

REV11 is a validated **preflight improvement** for a future isolated, backed-up native Ortho adapter, not a replacement for Plant 3D's interactive `PLANTORTHOCREATE`.

## Commands retained

`SPTC_SUPPORT2D` (interactive Plant native Ortho), `SPTC_SUPPORT2DLINEWORK` (standalone, non-native linework), `SPTC_SUPPORT2DBATCH` (standalone non-native batch), `SPTC_ORTHOAUTOPROBE` (read-only Ortho APIs), `SPTC_ORTHOAUTOPLAN` (REV11 source validation and cube plan), `SPTC_SUPPORTEXPORT`, `SPTC_SUPPORTBATCH`, `SPTC_SUPPORTMANAGER`, `SPTC_SUPPORTINSPECT`.

Refer also to `docs/Plant3D_2025_Ortho_API_DLL_Analysis_REV10.md`.
