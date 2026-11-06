# SPTC Support Drawings — REV15 (Native Ortho Seed Clone Test)

**Purpose:** Resolve the exact Plant 3D error **"Command is disabled in this drawing created in earlier version of Plant 3D"** encountered when using the API-registered REV14 Ortho DWG.

## Root cause and evidence

REV14 successfully created an Ortho drawing from a DWT, registered it with `OrthoProject.AddPnPDrawingFile`, returned a GUID, invoked `SetOrthoDwgVersion(...,1)`, and passed `OrthoDocUtils.IsValidOrthoDrawing`. Yet the native `New View` / `PLANTORTHOCREATE` command remained disabled.

The **native registration validator does not establish that a DWG is ready for view creation**. Autodesk Support specifically documents this error for drawings created outside the appropriate Project Manager workflow. Autodesk Community describes an almost identical API-created-Ortho failure: the `Ortho.dcf` / `PnPDrawings` `DwgVersion` value may be NULL for API-created drawings, whereas wizard-created drawings use `1`.

The REV14 `SetOrthoDwgVersion` call did not prevent this failure; we cannot assume its target string was interpreted as intended or that it recreated all wizard initialization. **Do not repeatedly run REV14 on a production project, do not edit `Ortho.dcf` directly, and do not assume REV14's native validity flag proves view creation works.**

## Working manual workaround (recommended before testing REV15)

1. In an isolated **copied project**, create a fresh Ortho DWG through **Plant 3D Project Manager → Orthographic DWG → New Drawing**.
2. Start a native Ortho view using the new drawing and the active registered 3D source. Confirm that the earlier-version warning is gone. If it is not, resolve project setup / installation before running automated experiments.
3. Preserve the API-generated failed DWG unchanged for investigation. Remove/re-add it only via Project Manager if desired after backing up the project.

Autodesk Support recommends removing/readding the affected drawing through Project Manager in the current Plant 3D version, or creating the Ortho drawing there from the outset.

## REV15 experimental command: SPTC_ORTHO15CLONE_TEST

This NEW command implements the **community-reported workaround** of cloning a **native, wizard-created Ortho DWG** rather than a DWT. It is **experimental**, requires a full copied project backup and native manual validation, and does **not** create a TOP view.

- Validate the active project-registered Piping 3D source drawing and select the support members dynamically.
- Select a native **EMPTY** Ortho DWG that you created by the current Plant 3D Project Manager in the same copied project.
- Confirm seed appears in the active Ortho project list and `IsValidOrthoDrawing` accepts it. The seed must separately have been proven capable of native New View; API validation alone is insufficient.
- Require two explicit operator confirmations: GUI Yes and the exact text `NATIVE ORTHO TEST`.
- Use the native DWG as the source to `Database.ReadDwgFile` / `SaveAs` to a unique test DWG, never overwrite.
- Register the clone through the native public Ortho project API, not direct SQLite/DCF editing.
- Preserve the returned drawing GUID and report native validation, but still require the operator to **open the clone and test New View manually**.
- Save `*_REV15_RESULT.txt` beside the new cloned test DWG.
- If project registration begins and fails, leave the attempted drawing for investigation rather than guessing how to roll back metadata.

Do **not** reuse a seed containing existing generated Ortho views or annotations. Cloning live view definitions can duplicate source monikers or internal references. The intended seed is a new blank Ortho drawing created by the current Plant 3D wizard, never a user model or arbitrary DWT.

### Procedure

1. Copy your entire Plant 3D project, including the project .dcf files and all Orthos/Plant 3D Models folders. Open the **copied project** in Plant 3D 2025.
2. In Project Manager, create an empty **native Ortho DWG**, e.g. `ORTHO_NATIVE_SEED.dwg`. Verify that **New View** can be entered in this seed; cancel before generating a view, so the seed remains empty.
3. Restart Plant 3D 2025, `NETLOAD` the REV15 `SPTC.SupportDrawings.dll`. Open any registered 3D model in the copy.
4. Run `SPTC_ORTHO15CLONE_TEST`. Select the support members, then select the **native seed DWG** in the file picker.
5. Read the backup warning, choose Yes and enter **NATIVE ORTHO TEST**. The plugin will clone and register a unique Ortho drawing.
6. In Project Manager → Orthographic DWG, open the **new clone** and try **New View**. Only a successful native OrthoCube dialog, TOP view generation, and successful **Update Ortho View** prove that it works.
7. Upload the generated `*_REV15_RESULT.txt` and report whether **New View** works. The report alone cannot prove GUI command enablement.

### Current limitation

- `SPTC_SUPPORT2D` remains the original interactive native Ortho workflow.
- `SPTC_ORTHO14REGISTER_TEST` remains present for prior diagnostic comparison, but its DWT-derived drawings may trigger the earlier-version warning and it is **not recommended for new tests**.
- `SPTC_ORTHO15CLONE_TEST` clones and registers a DWG but **does not automatically generate or place a TOP view**.
- The correct `SetOrthoDwgVersion` input semantics and required project metadata remain to be established; this test does not invoke that setter blindly.

## References

- https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Command-is-disabled-in-this-drawing-error-message-when-creating-orthographic-views-in-AutoCAD-Plant-3D.html
- https://forums.autodesk.com/t5/net-forum/plant-3d-creating-ortho-drawings-command-disabled-earlier/td-p/14013434
- See `docs/Plant3D_2025_Ortho_API_DLL_Analysis_REV10.md`.
