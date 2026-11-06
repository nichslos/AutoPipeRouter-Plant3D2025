# SPTC Support Drawings — REV16: native Ortho seed folder and safety correction

**Target:** AutoCAD Plant 3D 2025, x64, .NET 8.

## Verified user result

The user confirmed they can create a native Ortho view in `SPTC_R15dwg.dwg` made through Plant 3D Project Manager. The visible drawing is in the project's **`Orthos/DWGs` subfolder**. The REV14 DWT-derived output failed with:

`Command is disabled in this drawing created in earlier version of Plant 3D`

The earlier REV15 test accidentally selected the REV14-created file as its seed. That clone passed `IsValidOrthoDrawing` but it did not establish that the **New View** command was enabled.

**Neither registration success nor native validator success is sufficient proof that Plant 3D can generate an updateable Ortho view.**

## REV16 improvements

New preferred test command: **`SPTC_ORTHO16CLONE_TEST`**. Legacy **`SPTC_ORTHO15CLONE_TEST`** remains available and executes the same corrected routine.

1. Select model and support objects from the **current drawing**; never hard-code a project, DWG, member handle, or Ortho folder name.
2. Resolve the current project's native Ortho folder via `OrthoProject.GetOrthoPath()`. Allow selected native Ortho seeds in **any subfolder beneath that native Ortho root**, including `Orthos/DWGs`. Reject source paths outside the active project.
3. Require the seed to be listed in the active project's Ortho drawing collection and accepted by `OrthoDocUtils.IsValidOrthoDrawing`.
4. **Fail closed on view definitions:** use the public `OrthoProject.GetAllViewNamesInOrthoDwgByGuid` API with the seed's verified project GUID. Reject seeds with existing views, unknown GUIDs, unavailable view API, or read failures. A screenshot of a working Ortho view is not evidence that a seed is still empty.
5. Ask for explicit confirmation of a separate **COPIED BACKUP PROJECT** and the text `NATIVE ORTHO TEST`. No writes before confirmation.
6. Clone the verified empty native Ortho DWG using `Database.ReadDwgFile` / `SaveAs`; create unique names and never overwrite.
7. Save the clone **beside its native seed** in the actual project drawing folder, rather than putting it directly into the Ortho root.
8. Register it with the public `AddPnPDrawingFile` API, then report `IsValidOrthoDrawing`. Do not call the REV14 version setter, edit `Ortho.dcf`, or call private view generation functions.

The selected native seed **must be empty of generated Ortho views**. If `SPTC_R15dwg` already has a generated view (the screenshot suggests it may), create a new blank Ortho DWG through Project Manager solely for this test. Enter New View to verify functionality, then **cancel** without saving a new view in the blank seed. Select that new verified empty drawing in the REV16 file picker.

### Test procedure (COPIED project only)

1. Back up the full Plant 3D project and open the copy in Plant 3D 2025.
2. From Project Manager → Orthographic Drawings, create a **new empty native Ortho DWG**. Verify it can start `New View` and cancel without creating a view. Make sure no generated views remain.
3. Restart Plant 3D and load the REV16 DLL with `NETLOAD`.
4. Open any **registered source 3D DWG**, run `SPTC_ORTHO16CLONE_TEST`, select the support steel, and choose the **new empty native seed**, often within `Orthos/DWGs`.
5. If the tool prints `NATIVE_DRAWING_GUID_UNAVAILABLE` or `NATIVE_VIEW_API_UNAVAILABLE`, **do not work around that guard**. Capture the exact error for the next read-only probe.
6. Confirm that this is an isolated backup project and enter `NATIVE ORTHO TEST`. The tool creates a uniquely named clone and a `*_REV15_RESULT.txt` report (historical suffix retained).
7. Open the clone via Project Manager and confirm native **New View**, TOP projection and **Update View**. A successful `IsValidOrthoDrawing` alone does not prove the clone is functional.

The existing REV13 plan, REV14 registration test, and `SPTC_SUPPORT2D` remain available; `SPTC_SUPPORT2D` still enters Plant 3D's interactive Ortho creation workflow. REV16 is **not** a finished dialog-free native Ortho generator. The next stage is native TOP-view generation after a cloned drawing is verified to work.

## References

- Autodesk's official remedy for the earlier-version error: https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Command-is-disabled-in-this-drawing-error-message-when-creating-orthographic-views-in-AutoCAD-Plant-3D.html
- Plant 3D native Ortho SDK notes in `docs/Plant3D_2025_Ortho_API_DLL_Analysis_REV10.md`.
