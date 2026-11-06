# SPTC Support Drawings — REV13 (No hard-coded source models)

**Target:** AutoCAD Plant 3D 2025, Windows x64, .NET 8 runtime.

## Requirement: model and project independence

The native Ortho commands and planning tools must work on **any project-registered Plant 3D model currently open in the editor**, not on one test project, sample DWG, zone name or AutoCAD entity handle.

REV13 avoids fixed references to examples. Its input contract is:

| Input | Runtime source (not hard-coded) |
|---|---|
| Current Plant 3D project | `PlantApplication.CurrentProject` |
| Source model | `Application.DocumentManager.MdiActiveDocument.Database.Filename` |
| Validation part | Active project's `ProjectParts["Piping"]` (a Plant 3D API-defined part name) |
| Source DWG validity | `OrthoDocUtils.IsValid3dDrawing` against the active Piping project part |
| Support members | Each new editor selection, handles resolved from that drawing only |
| Cube boundary | Union of currently selected entities' geometric extents |
| OrthoCube margin | 10% of each axis span with 10 mm per-side minimum, in source drawing units |
| Report file name | Active drawing basename and **first selected handle**, plus revision |
| Native Ortho command | Still interactive `PLANTORTHOCREATE` from the current source DWG |

**No model-specific constants are used** for project names, source DWG names, folder paths, zones, or support handles. `"Piping"` and `"Ortho"` are *required Plant 3D project-part keys*, not user project names. `TOP`, `FRONT`, and `SIDE` are view types, not model identifiers.

## Evidence from uploaded REV12 report

The most recent `SPTC_ORTHO_AUTOPLAN_REV12.json` shows a **valid registered source** in the active project, a test model `TEST3.dwg`, and two selected handles `3EC` / `3EA`. These were *observed values from one test*, not constants in the plugin. Another DWG, project or support selection will yield different paths, handles and geometry.

The test's actual support was about **302.233 x 75 x 227.233 mm** and its padded planning cube about **362.680 x 95 x 272.680 mm**. Those coordinates are not in source code and will not be reused when another model is selected.

## REV13 changes

1. Removed the last fixed-project diagnostic message directing users to a sample project.
2. `SPTC_ORTHOAUTOPLAN` derives the initial JSON report name from the current source DWG and first selected handle. Example **format**: `SPTC_<ACTIVE-DWG>_<HANDLE>_ORTHO_AUTOPLAN_REV13.json`.
3. Each plan records `SourceSelectionMode: ACTIVE_DOCUMENT_AND_CURRENT_SELECTION`, `GeneratedAtUtc`, `SourceModelName` and `Source3dModel` to make it obvious which model/selection created it.
4. `SPTC_SUPPORT2D` prints the actual active model and project-registration status rather than assuming a fixed source drawing. It still starts native Plant 3D Ortho UI; it does **not** auto-select the project Ortho DWG or OrthoCube.
5. Added smoke tests for multiple source DWGs and different support handles. GitHub build also rejects source code references to previous test-model and project names.

## Commands retained

- `SPTC_ORTHOAUTOPLAN`: reads current selection/model and produces model-specific **plan JSON** (read-only).
- `SPTC_ORTHOAUTOPROBE`: read-only native API diagnostics.
- `SPTC_SUPPORT2D`: uses current source model to launch interactive `PLANTORTHOCREATE`.
- `SPTC_SUPPORT2DLINEWORK`, `SPTC_SUPPORT2DBATCH`: standalone experimental projected-linework DWGs (not native project Ortho).
- `SPTC_SUPPORTEXPORT`, `SPTC_SUPPORTBATCH`, `SPTC_SUPPORTMANAGER`, `SPTC_SUPPORTINSPECT`: existing drawing utilities.

## Test with any source model

1. Restart Plant 3D 2025 and `NETLOAD` the REV13 `SPTC.SupportDrawings.dll`.
2. Open **any** 3D model under the active Plant 3D project (e.g., a second test DWG).
3. Run `SPTC_ORTHOAUTOPLAN`, select members of that one support, and save the suggested JSON name.
4. Check `Source3dModel`, `SourceModelName`, `SelectedSupportHandles`, `SourceRegistrationVerified`, and the cube coordinates. They must come from that **current** model.
5. Switch to a different registered source DWG, repeat, and compare the two reports. They must be independent.
6. `SPTC_SUPPORT2D` will display its active model registration before launching native Ortho UI; it will not use an earlier model or saved JSON.

## Important remaining limitation

REV13 **does not automatically generate or register a native Ortho DWG without interaction**. In the supplied .NET 8 Plant 3D 2025 Ortho DLLs, the public `PlantOrthoView.CreateOrthos` path still calls `Editor.Drag` and the standard drawing-selection workflow uses a modal dialog. Avoid blind UI scripting or invoking private `GenerateOrthos` against an active production project without validated initialization and rollback.

Models are dynamic; the **A3 landscape sheet and Top/Front/Side view arrangement are currently preset drafting choices**, not tailored to all fabrication templates. Review these assumptions separately.

See also `docs/Plant3D_2025_Ortho_API_DLL_Analysis_REV10.md`.
