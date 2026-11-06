# SPTC Support Drawings — REV21.1: One-Click Auto-Fit Group Anchor

**REV21 runtime evidence:** The user clicked paper point (115, 370) mm on an A1 portrait sheet (594×841 mm). Native internal `GenerateOrthos` signature preflight passed, but the FRONT view's calculated position fell inside the bottom reserved margin. The original REV21 safety guard correctly stopped **before creating any native views**.

**REV21.1 fix:** Continue using the same separate `SPTC_ORTHO21_ONECLICK_INTERNAL_TEST` command, but allow the user to click **anywhere inside the physical paper sheet**. If the selected TOP position would push FRONT or SIDE off the allowed paper/title-block area, calculate the feasible TOP-anchor interval from **all three projected frame dimensions** and shift the entire group to the closest feasible position. No additional placement click is requested. Maintain the same relative TOP/FRONT/SIDE offsets, common scale, clipping checks, collision checks, and native registration checks.

For the previously failing anchor (115,370), the group will move **upward together**; the logged effective TOP anchor will differ from the clicked point. This deliberate translation is displayed in the command line and logged as `STEP5_ONE_CLICK_EFFECTIVE_TOP_ANCHOR`, `STEP5_ONE_CLICK_GROUP_TRANSLATION`, `STEP5_ONE_CLICK_AUTO_SHIFTED`, and the feasible anchor ranges. A click outside physical paper or an impossible-to-fit group still fails closed before native calls. The conservative right/bottom keepout is an approximation; inspect the actual title block after creation.

**How to test:** Restart Plant 3D 2025; NETLOAD REV21.1; open a **copied test project**, registered source model and empty native Project Manager Ortho seed; run `SPTC_ORTHO21_ONECLICK_INTERNAL_TEST`; select support and seed; confirm `REV21 TEST`; click **once** near the previously failing (115,370) location. If the API generates views, save the new `_ORTHO_R21ONCE_` drawing and test native **Update View** on all TOP/FRONT/SIDE views. Upload the new `_REV21_RESULT.txt` and a screenshot.

**Still experimental:** Autodesk's internal `GenerateOrthos` is unsupported and may fail even with valid placement. This patch fixes the observed preflight refusal only. Never use in production until model links, visual geometry, and Update View are proven. REV19.4 and REV20 remain unchanged and available as fallback.

---

# SPTC Support Drawings — REV21: One-Click Placement Test

**Experimental and unsupported: COPY OF PROJECT ONLY.** Do not run this against your production Plant 3D project.

REV20 succeeded in creating and registering native TOP, FRONT, SIDE views but called the public \`PlantOrthoView.CreateOrthos\` three times, which prompted for **three placement clicks**. REV21 is a separate test command that attempts the same work from **one** paper-space TOP anchor.

## Run \`SPTC_ORTHO21_ONECLICK_INTERNAL_TEST\`

1. Start AutoCAD Plant 3D 2025 and use the **copied test project**. Close the blank native Ortho seed's DWG tab.
2. \`NETLOAD\` the REV21 DLL, open/save any registered 3D model containing the support, run \`SPTC_ORTHO21_ONECLICK_INTERNAL_TEST\`.
3. Select the steel members belonging to one support, then choose a Project Manager-created, **empty and registered** native Ortho seed.
4. Read the explicit unsupported-API warning, approve, and type **\`REV21 TEST\`** to authorize creating a new registered experimental Ortho DWG.
5. On the paper-space sheet **click exactly once** near the displayed TOP anchor point. FRONT and SIDE positions are calculated automatically from the selected point, the actual paper size, and a shared view scale. For A1 portrait (594×841 mm), the default TOP suggestion is approximately (166, 601) mm.
6. If validation fails, inspect \`*_REV21_RESULT.txt\`. Leave the failed registered test DWG in the copied project for diagnosis. Do not delete project files or edit \`Ortho.dcf\` manually.
7. If three native views appear, save the drawing, inspect the title block and projections, and run **\`PLANTORTHOUPDATE\` → All** (or Update View on each view). **Only mark a test successful if model links and Update View work.** Upload the result log and an Ortho sheet screenshot.

## Internal implementation and risk

The public \`CreateOrthos\` API triggers \`ProcessViewportPlacement\` / \`Editor.Drag\` per call and has **no insertion point**. REV21 experiments with the **NON-PUBLIC** \`PlantOrthoView.GenerateOrthos\` entry point, requiring exactly 16 parameters including a **paper-space Viewport ObjectId** and explicit paper-space \`Point3d ptIns\`. It refuses to invoke a method whose *full runtime parameter signature* differs from the analyzed AutoCAD Plant 3D 2025 DLL.

The code creates three proposed paper viewports in the new registered Ortho DWG and invokes internal generation once for each view using calculated centers. It verifies the working database, model bounds, actual OrthoCube extents and the registered native view catalog after each call. Since this is a non-public internal method, its initialization and required state **might not match the standalone call**; the first runtime test may fail or create an incomplete native view. The code logs the failure and stops; it does not silently delete registered drawings or patch the Project Manager database. Do not treat a compiled DLL as evidence that Update View works.

**Fallback:** The fully validated REV19.4 TOP-only command and the working REV20 three-click TOP/FRONT/SIDE command remain **unchanged**.

---

# SPTC Support Drawings — REV20: Native TOP + FRONT + SIDE

**REV19.4 is preserved as the confirmed working one-command TOP-only baseline.** Its original command remains \`SPTC_ORTHO19_TOP_ONECMD_TEST\`. The user confirmed native TOP creation without a placement click and Update View.

## New isolated experiment

Command: \`SPTC_ORTHO20_THREEVIEW_ONECMD_TEST\`.

Run it **only in a copied test project** with a registered source 3D drawing and a native Project Manager-created *empty* Ortho seed. Select one support, choose the seed and type \`REV20 TEST\` to authorize a new, uniquely named project-registered native Ortho DWG. The test reuses REV19.4's verified read/share, registration and target-document command-context strategy. It then initializes a real native OrthoCube for each view and calls only the **public PlantOrthoView.CreateOrthos** API sequentially for TOP (look +Z, up +Y), FRONT (look −Y, up +Z) and SIDE (look +X, up +Z). Each newly registered view is verified against the native project view catalog before proceeding.

The sheet planner reads the **actual title-block paper size** and calculates one common standard scale, plus non-overlapping *proposed* TOP-above-FRONT / SIDE-next-to-FRONT positions. The public CreateOrthos signature does not accept insertion coordinates. **Native placement may not follow those proposals**; REV20 does not claim otherwise and never moves linked native viewport objects. It probes paper-space viewports after generation, logging possible overlaps and conservative right/bottom title-block keepout conflicts. These warnings require visual verification of the actual template.

If any view fails, REV20 stops subsequent view generation and preserves the registered partially completed test DWG for inspection; it never edits Ortho.dcf or calls private GenerateOrthos.

**Test steps:** Restart Plant 3D 2025; NETLOAD the new DLL; open your registered 3D model in the copied project; run \`SPTC_ORTHO20_THREEVIEW_ONECMD_TEST\`; select your support; pick your EMPTY registered Ortho seed; approve and type \`REV20 TEST\`. Open \`*_ORTHO_R20THREE_*.dwg\`, inspect TOP/FRONT/SIDE geometry, orientation, placement and title-block clearance. Save and test **Update View** individually or \`PLANTORTHOUPDATE\` → All. Send \`*_REV20_RESULT.txt\` and a screenshot.

**Runtime status:** Pending user validation. GitHub compilation does not prove that all three native views are correctly positioned or updateable.

---

# SPTC Support Drawings — REV19.4: Working Database Synchronization

## REV19.4 — Working database mismatch diagnosis and scoped synchronization

**Observed in REV19.3 runtime:** Entered the registered target Ortho document command context (`IsApplicationContext=False`), the active Document matched the registered target, but `HostApplicationServices.WorkingDatabase` did **not** pass a `ReferenceEquals` comparison with the target document database. The command stopped before native OrthoCube initialization. This result alone does not prove that the underlying AutoCAD database was wrong: different .NET wrappers may represent the same native drawing.

**Change:** First log the working and target database filenames. Compare **normalized full DWG paths**, not only .NET wrapper object identity. Only when the paths differ, set `HostApplicationServices.WorkingDatabase = targetDoc.Database` in the verified target document command context. Immediately verify the effective working database filename. Stop if it still differs. After the operation, synchronize working database to whichever AutoCAD drawing is active so a previous source-drawing database is not left active while the generated Ortho drawing is the active tab. Keep exact REV19.2 native OrthoCube bounds validation, copied-test-project consent, public native CreateOrthos only, and the prior exception logging guard.

**Why:** Autodesk's managed API documentation says the working database supplies drawing context for many DB operations and should stay synchronized with document switching. Reference: https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_HostApplicationServices_WorkingDatabase.html

**Retest only in copied project:** Restart AutoCAD Plant 3D 2025; NETLOAD REV19.4; open any registered source 3D model, save it; run `SPTC_ORTHO19_TOP_ONECMD_TEST`, select the support, choose the native registered **empty** Ortho seed, and type `REV19 TEST`. Upload the fresh `_REV19_RESULT.txt` and a TOP view screenshot if generated. Do not select an earlier failed clone or populated REV18 output as the seed.

**Key new log entries:** `STEP5_WORKING_DB_BEFORE`, `STEP5_EXPECTED_WORKING_DB`, `STEP5_WORKING_DB_WRAPPER_EQUALS`, `STEP5_WORKING_DB_SYNC_ENTERED` (only if filenames differ), `STEP5_WORKING_DB_EFFECTIVE`, `STEP5_WORKING_DB_VERIFIED_TARGET=True`, and `STEP5_NATIVE_CUBE_EXTENTS_MATCHED=True` (if native cube works). If the cube still returns sentinel values, report and diagnose rather than bypass the geometry check.

**Status:** REV19.4 is unverified in the Plant 3D runtime until the user tests the compiled build. REV18 automatic TOP generation and Update View remain the confirmed working baseline. No private API or direct `Ortho.dcf` editing is introduced.

## REV19.3 — Fix native OrthoCube initialization after switching drawings

**Observed:** REV19.2 created/registered/opened the native Ortho clone and calculated the same valid padded support bounds as REV18, but `PlantOrthoCube.InitializeCube(Extents3d)` returned uninitialized `Extents` with `Min=(+1e20,+1e20,+1e20)` and `Max=(-1e20,-1e20,-1e20)`. These are empty-envelope sentinel values, not structural support geometry. The previous code correctly stopped before generating a TOP view.

**Likely root cause:** REV19 uses `CommandFlags.Session` (AutoCAD application context) to switch drawings. REV18 successfully ran as a document-context command inside an already open native Ortho drawing. The REV19.2 session code switched `MdiActiveDocument` but called the native OrthoCube API before entering the newly opened drawing's **document command context**.

**Change:** REV19.3 retains the registered source/seed checks, all unique native drawing registration behavior, and the strict cube dimension comparison. After opening/activating the new registered Ortho drawing, it uses **`DocumentManager.ExecuteInCommandContextAsync`** to schedule the native cube initializer and public `CreateOrthos` on AutoCAD's target document context. It logs and **fails closed** if the document is not active or its working database does not match the target. All delegate exceptions are caught and written to `_REV19_RESULT.txt`; no direct editor writes to the inactive source document.

**Important:** The new context handoff is an *unverified hypothesis* until tested in the actual Plant 3D 2025 runtime. It neither bypasses geometry validation nor modifies the confirmed REV18 command. The public TOP API may still show native dialogs depending on project state.

**Test:** Restart Plant 3D, load the REV19.3 DLL with `NETLOAD`, open the registered 3D source in your fully copied test project, run `SPTC_ORTHO19_TOP_ONECMD_TEST`, select one support, select an empty native Project Manager seed under `Orthos/DWGs`, and enter `REV19 TEST`. Inspect the new unique `_ORTHO_R19AUTO_` drawing and upload the new `_REV19_RESULT.txt`.

**Key new log fields:**
- `STEP5_TARGET_COMMAND_CONTEXT_REQUESTED`
- `STEP5_TARGET_COMMAND_CONTEXT_ENTERED`
- `STEP5_TARGET_CONTEXT_IS_APPLICATION` (expected `False`)
- `STEP5_TARGET_CONTEXT_ACTIVE_DOC_MATCH` (expected `True`)
- `STEP5_TARGET_CONTEXT_WORKING_DB_MATCH` (expected `True`)
- `STEP5_NATIVE_CUBE_EXTENTS_MATCHED` (expected `True`)
- `STEP8_PUBLIC_CREATEORTHOS_RETURNED` if native TOP generation completes.

Never delete a failed clone directly from the project folder or edit `Ortho.dcf` manually. Do not reuse a previous failed `_ORTHO_R19AUTO_` DWG as the empty seed. Test only in the copied project.

Autodesk documentation: https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_HostApplicationServices_WorkingDatabase.html ; https://blog.autodesk.io/creating-a-sample-autocad-plugin-with-net-80-and-ccli-for-autocad-2025/

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
