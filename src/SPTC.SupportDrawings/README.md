# SPTC Support Drawings — REV14 (Native Ortho DWG Registration: TEST ONLY)

**Host:** AutoCAD Plant 3D 2025, x64, .NET 8.

## Objective and what actually works

The user wants `SPTC_SUPPORT2D` to generate native project-linked Ortho views without selecting an Ortho drawing, manually positioning the OrthoCube, or placing the viewport. REV13 has already confirmed **model-independent source detection and OrthoCube sizing** in separate source DWGs with different selected support members.

**REV14 provides the first writable native Ortho integration experiment. It does NOT generate a finished Ortho view yet.** We intentionally added a separate test-only command rather than risk breaking the existing `SPTC_SUPPORT2D` behavior.

## NEW: SPTC_ORTHO14REGISTER_TEST (backup project only)

This explicitly opt-in command tries to create a **new, uniquely named native Ortho project DWG** in a COPIED test Plant 3D project:

1. Read the active Plant 3D project, source model filename, and current support selection (never hard-code model/project names, paths, handles, or coordinates).
2. Require native `OrthoDocUtils.IsValid3dDrawing` to confirm that the active 3D source DWG belongs to the current project's Piping project part.
3. Compute the support OrthoCube envelope using per-axis 10% margin (minimum 10 mm on each side).
4. Resolve the Ortho project part and native destination via `OrthoProject.GetOrthoPath()`. Reject any destination outside the active project or missing public creation APIs.
5. Ask the user to select the actual configured **Ortho DWT template** for their COPIED project (file selection is for this isolated test; final automatic template discovery is not implemented).
6. Explicitly confirm a COPIED TEST PROJECT and type `TEST PROJECT` to authorize writes. Refusing/canceling leaves the project unchanged.
7. Create a uniquely named DWG from the chosen DWT using AutoCAD's side database. Existing files are never overwritten.
8. Register the new DWG through `OrthoProject.AddPnPDrawingFile(sFile, sParentGuid, sTitle, ref fileAlreadyExists, ref illegalXrefs)`, using empty parent GUID for the Ortho project root.
9. Try to set Ortho version metadata using public `OrthoProject.SetOrthoDwgVersion(string, int)` with **version 1** and a confirmed GUID. If the registration API does not supply a verifiable drawing GUID, it skips this operation rather than guessing or editing project databases directly.
10. Check `OrthoDocUtils.IsValidOrthoDrawing()`. Save a result TXT beside the generated DWG with the returned GUID, native creation API statuses, version-set outcome, and validation reason.

**This is experimental and cannot be called production-safe until proven against the specific Plant 3D 2025 project.** The project database could be partially registered if a native API throws. In that case the test DWG and metadata are left in place for investigation; the plugin does not attempt unverified automatic cleanup.

## How to test safely

1. **Make a complete independent copy of the Plant 3D project**, including its original .dcf project databases, Plant 3D Models, Ortho drawings, project setup, and templates. Work ONLY in the copy. The code cannot prove a backup exists and relies on the operator's explicit confirmation.
2. In Plant 3D Project Manager, open that **copied project** and one registered source 3D DWG.
3. Restart Plant 3D 2025, run `NETLOAD` and select REV14 `SPTC.SupportDrawings.dll`.
4. Run `SPTC_ORTHO14REGISTER_TEST`, select the steel members of ONE support, press Enter.
5. In the DWT picker, select the **Ortho template configured in Project Setup → Ortho DWG Settings → Title Block and Display**. Do not use an unrelated model DWT.
6. Read the backup-project warning, choose YES *only if it is a cloned test project*, and type `TEST PROJECT`.
7. Inspect the new Ortho drawing in **Project Manager → Orthographic DWG**. Try opening it and check if `PLANTORTHOCREATE` / **New View** is enabled; do NOT interpret a lone DWG as a valid native Ortho.
8. Upload the generated `*_REV14_RESULT.txt` and report whether the drawing opens and is recognized by Plant 3D.

The output DWG name uses the **active model**, **first selected support handle**, UTC timestamp and short random suffix; no fixed model or hard-coded project is used.

## Why the drawing version matters

A 2026 Autodesk Community investigation reports that copying a DWT into a new DWG and adding it to the Plant 3D Ortho project can leave `Ortho.dcf.PnPDrawings.DwgVersion` unset, resulting in *Command disabled (earlier version)* when attempting to create a native Ortho view. The user's installed 2025 DLL exposes a public `SetOrthoDwgVersion(string,int)` method; REV14 tests that API rather than patching the DCF database manually. Version 1 follows that reported case; its suitability for the user's 2025 project must be verified by native drawing validation.

The exact type / semantic contract of the method's string input is not fully documented. REV14 uses only a returned GUID or one exposed on the registered drawing object. If it cannot confirm a GUID, it **does not** call the version API.

## Retained commands

- `SPTC_SUPPORT2D` — still interactive `PLANTORTHOCREATE`; unchanged to avoid damaging current workflow.
- `SPTC_ORTHOAUTOPLAN` — source-model and support-specific OrthoCube/TOP-FRONT-SIDE planning JSON.
- `SPTC_ORTHOAUTOPROBE` — read-only SDK and Plant 3D project inspection.
- `SPTC_SUPPORT2DLINEWORK`, `SPTC_SUPPORT2DBATCH` — standalone experimental linework DWGs, not native Ortho.
- `SPTC_SUPPORTEXPORT`, `SPTC_SUPPORTBATCH`, `SPTC_SUPPORTMANAGER`, `SPTC_SUPPORTINSPECT` — previous sheet, manager and audit utilities.

## Next stage, only after REV14 registration test passes

Use matching project-native view definition and placement APIs to generate ONE TOP view from the selected source. The public `PlantOrthoView.CreateOrthos` method still calls interactive `Editor.Drag`. The private `GenerateOrthos` method is not a supported API; do not invoke it blindly. Do not implement fake 2D linework as a native Ortho or claim the video-style automation is working until the new view can be updated from the source model.

## References

- Autodesk Plant 3D official: https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-09E67A9E-45F7-4392-BFB6-EF1ADAB1D46B.htm
- Autodesk Community accepted native project drawing API: https://forums.autodesk.com/t5/autocad-plant-3d-forum/create-new-dwg-from-command-line-with-parameter/m-p/11231563
- Autodesk Community Ortho DWG version investigation: https://forums.autodesk.com/t5/net-forum/plant-3d-creating-ortho-drawings-command-disabled-earlier/td-p/14013434
- Internal source inspection: `docs/Plant3D_2025_Ortho_API_DLL_Analysis_REV10.md`.
