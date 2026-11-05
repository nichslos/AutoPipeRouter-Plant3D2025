# SPTC Support Drawings — REV 09 (Native Ortho automation readiness)

Target: AutoCAD Plant 3D 2025, Windows x64, compatible .NET 8 host.

## User requirement

One-click SPTC_SUPPORT2D should:
1. Identify the source 3D model and selected support members.
2. Automatically create/select a **project-registered Ortho DWG** with no drawing selection dialog.
3. Automatically set OrthoCube extents to the support and orientation/scale to the desired view.
4. Generate, insert and save a native Plant 3D orthographic view, without manual placement.
5. Eventually batch-generate Top/Front/Side views, dimension and material schedules.

**REV09 does not claim this is complete.** The native PLANTORTHOCREATE command remains interactive, as documented by Autodesk. Blind keystroke/click automation or writing directly into Plant project SQLite files would put the project at risk and is deliberately not implemented.

## SDK evidence

The user supplied sdk.zip, which includes inc-x64/PnP3dOrthoProjectPart.dll, but **not PnP3dOrthoDrawingsUI.dll**. The latter is reported by Autodesk developer forum users as containing PlantOrthoView.CreateOrthos(). The signature has been discussed, but third-party developers report unresolved automatic *viewport insertion point* control. The generic AutoCAD .NET API does not provide a documented, complete replacement for the Plant 3D Ortho pipeline. Some supplied SDK binaries target .NET 10 and must not be used as .NET 8 references.

## Added REV09 diagnostic

**SPTC_ORTHOAUTOPROBE** is a new, read-only command:

1. Open the **registered source Plant 3D model DWG**, with the project active.
2. Run SPTC_ORTHOAUTOPROBE.
3. Select the same pipe-support steel members you want automatically projected.
4. Save the report as SPTC_Ortho_API_Probe_REV09.txt.
5. The report contains source drawing path, source-unit label, selected bounds plus 10% suggested OrthoCube padding, loaded Ortho assembly names and public method signatures, installed Ortho DLL filenames/versions, and available **read-only** Ortho project information.

If PnP3dOrthoDrawingsUI methods are absent from loaded-assembly listings, first open Plant 3D's normal **Create Ortho View** dialog, cancel it, and run the probe again. The probe **does not load unknown DLLs, write to project files, create an Ortho DWG, or click any dialogs**.

## Retained commands

- SPTC_SUPPORT2D: legacy REV08 interactive launch of PLANTORTHOCREATE (requires choose/create DWG, OrthoCube, place view).
- SPTC_SUPPORT2DLINEWORK: standalone experimental REV07 solid/curve projected lines, **not a native Ortho drawing**.
- SPTC_SUPPORT2DBATCH: standalone block batch 2D linework, **not native Ortho batch**.
- SPTC_SUPPORTEXPORT, SPTC_SUPPORTBATCH, SPTC_SUPPORTMANAGER: existing 3D viewport sheets and CSV.
- SPTC_SUPPORTINSPECT: existing support object-class audit.

## What is necessary before removing the dialogs

1. Verify actual **matching .NET 8** installed Plant 3D ortho runtime DLL(s), including PnP3dOrthoDrawingsUI.dll.
2. Identify a safe, public project Ortho drawing creation/registration API and view creation API.
3. Verify that CreateOrthos() (or another supported API) can **commit a native project-linked view at a specified point** without starting an interactive placement prompt. If it cannot, further runtime integration is needed; do not substitute guessed UI controls.
4. Test against a copy of the user's Plant 3D 2025 project, including view updates and model-to-ortho dependencies.

## Setup and test

- Download the successful SPTC-SupportDrawings-Plant3D2025-REV09 GitHub Actions artifact.
- Restart Plant 3D to unload the prior SPTC.SupportDrawings.dll and NETLOAD REV09.
- Run SPTC_ORTHOAUTOPROBE and inspect the TXT report.
- Keep REV08 for rollback. REV09 should not change project DWG geometry while gathering diagnostics.

## Official and community references

- [Autodesk — To Create an Orthographic View](https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-09E67A9E-45F7-4392-BFB6-EF1ADAB1D46B.htm)
- [Autodesk — PLANTORTHOCREATE](https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-EBBC59CB-38BB-479A-97A9-BB7606E3426C.htm)
- [Autodesk Community — Create an orthographic view via C# API](https://forums.autodesk.com/t5/autocad-plant-3d-forum/create-an-orthographic-view-via-the-c-api/m-p/13629352)
- [Autodesk — SDK required to update native Ortho views](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Is-it-possible-to-update-AutoCAD-Plant-3D-ortho-views-by-the-API-of-AutoCAD-or-ACC-API.html)
