# SPTC Support Drawings — REV19.1: Native Seed Sharing Fix

## REV19.1 — Fix for `eFileSharingViolation` on the empty seed

The first REV19 runtime log stopped on `Database.ReadDwgFile(seed, FileShare.Read, true, null)` before creating the native clone. No Ortho view generation occurred. REV19.1 changes **only the seed-read overload** to `Database.ReadDwgFile(seed, FileOpenMode.OpenForReadAndAllShare, true, null)`, followed by `CloseInput(true)` before `SaveAs`. Autodesk documents this open mode as permitting reading a DWG already open for write. The rest of the REV19 native clone/register/TOP sequence remains unchanged.

**Re-test:** Close any open seed drawing tab and save the blank seed first, then restart Plant 3D 2025, `NETLOAD` REV19.1, open the registered 3D source, run the same `SPTC_ORTHO19_TOP_ONECMD_TEST` command, choose the empty native Ortho seed, and type `REV19 TEST` in the backup project. Attach the new `_REV19_RESULT.txt`. If the file-sharing error persists, check other AutoCAD instances, Windows Explorer preview or network file locks; do not edit `Ortho.dcf` or delete the seed.

Source: https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_FileOpenMode.html

**Target:** AutoCAD Plant 3D 2025, Windows x64, .NET 8.

## Key validated user result

The user confirmed REV18's public `PlantOrthoView.CreateOrthos(...)` generated a genuine native TOP view **without any manual viewport-placement click**, and **Update View worked** on the resulting Ortho. The runtime log contained `STEP5_PUBLIC_CREATEORTHOS_RETURNED` and `POST_CALL_ORTHO_VIEW_CATALOG=EXISTING_NATIVE_VIEWS:SPTC_TOP_R18_060123`.

**REV19 integrates the REV16 native seed-cloning pathway and the REV18 public native generation pathway into a separate end-to-end command.** This new integrated sequence has compiled, but its cross-document switch is not yet confirmed in the installed Plant 3D runtime. No claim of production readiness.

## Command

`SPTC_ORTHO19_TOP_ONECMD_TEST` — **COPIED/INDEPENDENT TEST PROJECT ONLY.**

The command is started in the **active, registered 3D model**. Unlike REV18, it does **not** require the user to create an empty REV16 clone manually or save/read a REV17 JSON report.

### What it does

1. Inspect the currently active registered source model and select **one support's steel members**.
2. Compute accurate extents and the per-axis-padded native OrthoCube. Use the source model's physical units dynamically.
3. Choose a native **empty Project Manager-created Ortho seed** (an actual registered DWG under the active project's Ortho root such as `Orthos/DWGs`). Reject raw DWTs, outside-project drawings, native-invalid seeds, or seeds with any registered views.
4. Ask for backup-project consent (**Yes**) and exact command-line phrase **`REV19 TEST`**.
5. Clone the native seed into a **new unique** `SPTC_<model>_<handle>_ORTHO_R19AUTO_<date>_<random>.dwg` alongside the seed (never overwrite), register it via `OrthoProject.AddPnPDrawingFile`, verify `OrthoDocUtils.IsValidOrthoDrawing`, and verify native view catalog reports **zero** views.
6. Use a **Session** command to open and activate the newly registered drawing. Find and switch to its actual paper-space plot layout (e.g. A1 portrait `Title Block`) and read its live configured paper size.
7. Compute the TOP scale candidate from the actual sheet and current support OrthoCube. In the previous test source this was approximately **1:2** for an A1 portrait sheet; no project or test geometry names are hard-coded.
8. Initialize native `PlantOrthoCube` using `InitializeCube(Extents3d)`, compare its extents against the computed support cube, create `Solid3d` via `MakeCubeCopy()`, construct public `PlantOrthoView(false,false)`, and invoke the **public** `CreateOrthos(... Top ...)` against the freshly opened registered drawing.
9. Check the native registered view catalog. Leave the target drawing open for visual inspection, save and manually confirm **Update View**.

### Limits / safeguards

- REV19 needs one support selection, an empty native seed selection, and a copied-project confirmation. **One command ≠ zero prompts.** The *native viewport placement* was reported automatic in REV18, but the newly integrated command is a separate runtime experiment, not yet verified to be dialog-free.
- **No private** `GenerateOrthos`, no source/project name/handle hard-coding, no DCF/SQLite writes, no simulated mouse or keyboard input. The native public API owns all model-link metadata and view generation.
- The public `PlantOrthoView` constructor could still open a native dialog or switch drawings; the code verifies the active document and aborts if it changed unexpectedly.
- The estimated TOP paper center is logged but is **not** passed as an insertion coordinate; the public method does not expose one. View location and title-block clearance must be inspected.
- The OrthoCube limits the projected region, not the exact set of objects to render: nearby 3D objects in the source may appear.
- If a failure occurs after registration, the new registered DWG is left for manual inspection; it is **not** deleted or removed from the project database automatically.
- Before running, **save source model changes**, so the Ortho view links to the same on-disk geometry selected for the extent calculation.
- Previous REV16 / REV17 / REV18 commands remain intact; do not test REV19 on the existing successful REV18 output.

## Installation and test

1. Make a full separate backup/copy of the Plant 3D project. Open the copy in Plant 3D 2025.
2. Ensure a **blank, registered native Ortho drawing** created through Project Manager exists. `SPTC_NATIVE_SEED_01.dwg` is a prior example; any active project/filename works. A seed containing a view is not acceptable.
3. Restart Plant 3D, `NETLOAD` the compiled REV19 `SPTC.SupportDrawings.dll`.
4. Open the registered **source 3D model**, save any changes, and type `SPTC_ORTHO19_TOP_ONECMD_TEST`.
5. Select all support steel members, then press Enter.
6. Choose the **blank native Ortho seed** in the project's Ortho drawing folder.
7. Confirm **Yes** only in the copied test project and type `REV19 TEST`.
8. Check for a newly generated `_ORTHO_R19AUTO_` DWG in Project Manager. The command should try to open it and call the TOP native API, without a manual viewport placement click (to be verified). Do not switch away manually during the API call.
9. In the new DWG, inspect geometry and sheet location, save, and test **Update View**.
10. Upload the `*_REV19_RESULT.txt` log from the same Ortho folder, plus a screenshot of the TOP result. The log is written before each risky native call, so an abnormal exit still leaves a partial report.

### Log meanings

| Entry | Meaning |
|---|---|
| `STEP1_NATIVE_SEED_CLONED=PASS` | New physical DWG written from native empty seed |
| `STEP2_PROJECT_REGISTERED_EMPTY_ORTHO=PASS` | Native Ortho Project registration and validator both succeeded |
| `STEP3_ORTHO_DOCUMENT_ACTIVE=PASS` | Active source switched to the new Ortho drawing |
| `STEP4_SHEET_MM` | Actual selected layout's physical plot sheet size |
| `STEP5_NATIVE_CUBE_INITIALIZED_AND_VALIDATED=PASS` | Real OrthoCube created from selected support extents |
| `STEP6_NATIVE_CUBE_SOLID=PASS` | Native cube solid created in memory |
| `STEP8_PUBLIC_CREATEORTHOS_RETURNED` | Public Ortho generation method returned; output still requires validation |
| `STEP9_NATIVE_VIEW_CATALOG=EXISTING_NATIVE_VIEWS:...` | Native Ortho project registered a view |
| `FINAL_STATUS=TOP_API_RETURNED_CHECK_UPDATE_AND_GEOMETRY` | API returned; native Update View / visible geometry not automatically tested |
| `NATIVE_EXCEPTION` or `ERROR` | Failure details; upload report |

## Future

After **REV19 one-command TOP** succeeds with visible geometry and Update View, the next step is independent FRONT and SIDE views with safe sheet layout positioning, without replacing the proven TOP generator.

References: `docs/Plant3D_2025_Ortho_API_DLL_Analysis_REV10.md`, `NativeOrthoSeedCloneTest.cs`, `NativeOrthoTopGenerationTest.cs`.
