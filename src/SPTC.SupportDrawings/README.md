# SPTC Support Drawings — REV 10 (Native Ortho automation staging)

**Host:** AutoCAD Plant 3D 2025, Windows x64, compatible .NET 8 runtime.

## Requested final behavior

One SPTC_SUPPORT2D command must select a support's real project 3D model, bypass Select Orthographic Drawing, automatically define the OrthoCube, create a project-registered native Ortho DWG, generate Top/Front/Side views at scaled fixed paper positions, save them, and eventually add accurate dimensions/material schedules.

**This is not yet implemented in REV10.** Static inspection of the user-provided Plant 3D 2025 .NET 8 Ortho DLLs (16.0.58.1102) shows that public PlantOrthoView.InitializeProjectData calls the modal drawing-selection dialog, and public PlantOrthoView.CreateOrthos calls interactive viewport placement via Editor.Drag. Internal GenerateOrthos has a fixed insertion-point parameter but is non-public and relies on initialized project state. Calling it without verification can corrupt the Ortho project. We do not invoke it in this revision.

## Added REV10 command: SPTC_ORTHOAUTOPLAN

This is a non-destructive, source-model-specific **automatic staging program**. It:

1. Confirms source DWG is saved and its units are supported (mm, cm, m, inches or feet).
2. Prompts for the actual steel members forming **one** support.
3. Reads entity handles and real AutoCAD geometric extents without changing any object.
4. Unions the model extents and builds a padded OrthoCube proposal with 10% clearance on the longest support span.
5. Calculates Top (X/Y), Front (X/Z), Side (Y/Z) view directions and up vectors.
6. Calculates three non-overlapping A3 landscape view tiles and candidate standardized fit scales based on source DWG units.
7. Records a *read-only* current Plant 3D project summary where accessible.
8. Saves an editable **SPTC_ORTHO_AUTOPLAN_REV10.json** file that is suitable for validating and feeding an eventual native automation adapter.

**It does not** register or create a project Ortho DWG, position a native OrthoCube, call internal GenerateOrthos, dismiss dialogs, or generate finished views. Suggested view scales/placements must be confirmed against actual Plant 3D Ortho configuration, title block, units and view content. Project membership is explicitly marked unverified in JSON.

## Commands preserved

- **SPTC_ORTHOAUTOPLAN** (NEW) — machine-readable native Ortho planning parameters.
- **SPTC_ORTHOAUTOPROBE** — inspect actual live Ortho assemblies, project context and available public methods.
- **SPTC_SUPPORT2D** — existing interactive native PLANTORTHOCREATE handoff.
- **SPTC_SUPPORT2DLINEWORK** — diagnostic standalone linework DWG, **not a native project Ortho**.
- **SPTC_SUPPORT2DBATCH** — non-native standalone block linework batch exporter.
- **SPTC_SUPPORTEXPORT**, **SPTC_SUPPORTBATCH**, **SPTC_SUPPORTMANAGER**, **SPTC_SUPPORTINSPECT** — existing project-support utilities.

## How to test

1. Download compiled artifact **SPTC-SupportDrawings-Plant3D2025-REV10** from a successful GitHub Actions run and extract DLL.
2. Restart Plant 3D 2025 before NETLOAD (previous REV09 DLL must be unloaded).
3. Open a backup of your **project-registered source 3D DWG**.
4. Run **SPTC_ORTHOAUTOPLAN**, select both angle-steel support members, press Enter and save **SPTC_ORTHO_AUTOPLAN_REV10.json**.
5. Check that the model path, support handles, cube bounds and Top/Front/Side view proposals correspond to the selected steel support.
6. Run **SPTC_ORTHOAUTOPROBE** and save **SPTC_Ortho_API_Probe_REV09.txt** if not already done.
7. Supply those two reports from your installed application to continue the supported native Ortho adapter. You can still use SPTC_SUPPORT2D to run the manual Plant 3D ortho process.

## Engineering integration plan after the stage is verified

- Confirm source drawing registration using the exact project part in Plant 3D 2025.
- Create/register a unique new Ortho DWG using a validated API or the official template, with rollback.
- Validate how PlantOrthoView initializes its existing Ortho project state without modal SelectOrthoDwg.
- Establish controlled viewport placement without Editor.Drag (public CreateOrthos is interactive). Experimental non-public GenerateOrthos would require strict project backup, a test-only build, and validated state transition.
- Generate one TOP view successfully before expanding to FRONT/SIDE, hidden-line control, real dimensions and material list. Do not represent a plain DWG as project-linked Ortho.

## Safety/compatibility

- Build against AutoCAD 2025 .NET 8 references without committing or distributing user-uploaded proprietary Autodesk Ortho DLLs.
- REV10 does not modify any Plant project/Ortho data during staging.
- Matching .NET 8 Plant 3D Ortho runtime DLLs supplied by the user are documented in docs/Plant3D_2025_Ortho_API_DLL_Analysis_REV10.md.
- **Successful compilation only validates code syntax and isolated geometry; actual native Ortho UI automation is not yet verified or implemented.**
