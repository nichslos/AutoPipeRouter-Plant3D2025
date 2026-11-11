# SPTC Production Iso REV28 — Restore Native Drawing / Project Buttons

**Baseline:** REV27. Earlier REV01–REV27 revision branches and builds are unchanged.

## Reported problem
After the user selected an Active DWG line in REV27, clicking again did **not** restore all project routes. Only two drawing-scope entries (for example `A-008` and `A-08`) appeared, and the dialog still lagged.

## Verified cause
Official Autodesk Plant 3D documentation defines the original top three **Display lines** buttons:
- **Drawing (first)** — only line numbers present in the current drawing;
- **Project (second)** — all line numbers in the entire project;
- **Filter (third)** — line numbers in the current selection set.

REV23–27 installed a global WinForms message filter that intercepted **all three buttons**, bypassing their original native actions. The plugin treated the buttons as a single Active/All toggle and tried to restore All by clearing the native *search text*, which never changed the underlying Autodesk **drawing/project scope**. Consequently the list could get stuck showing two current-drawing tags.

## REV28 fix
- **Stop installing** the SPTC toolbar click interceptor. Drawing, Project and Filter now have their original Autodesk documented functions and their original native clicks load the corresponding line-number lists.
- Keep Active DWG exact-tag filtering **separate** from the original mode buttons. Added a compact **DWG** checkbox beside `Select All (N)`, with explanatory tooltip.
- Check **DWG** to perform a one-time read-only inspection of the actual Plant 3D piping components in the currently opened DWG, then place the unique verified physical `LineNumberTag` in Autodesk's **original native Filter textbox**. The line-number checklist and Create button remain Autodesk's original controls, not a duplicate proxy list.
- Uncheck **DWG** to clear only the native Filter text. To return to *every line in the entire project* (not just the opened drawing), **click Autodesk's second/Project toolbar button**. This is not the same as unchecking DWG because the Autodesk mode itself determines which list is loaded.
- If the actual physical model contains 0 or 2+ unique current route tags, the supplemental DWG filter declines to guess a single route; the native list is left unchanged and a diagnostic is logged in F2.
- A nonblocking 60ms paint-yield occurs before the on-demand physical-tag inspection. There is **no startup-wide PnP scan**, and Autodesk API calls remain on the CAD UI thread.
- When the user changes the native Filter text manually or the Autodesk mode button changes it, the DWG checkbox resets to reflect the native field.
- `Select All (N)` still reads visible original line items. Its UI polling has been reduced from 850ms to 1600ms, and the dialog layout watch from 180ms to 450ms, avoiding unnecessary UI-thread/window enumeration while the project loads.
- The original native Create ISO button is never forcibly enabled.

## Preserved
- REV25 adaptive vertical line-pane splitter.
- Folder dropdown at the native Filter row; only first-level physical Plant 3D Drawings folder names shown, folder scanning only on request.
- REV19 dynamically aligned and stretched Custom Save Location controls.
- REV05 automatic copy of finished Production ISO DWGs to custom location.
- No model, database, project setup, or IsoConfig.xml changes.

## Test REV28
1. Close Plant 3D completely, reopen it and NETLOAD REV28's `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO` and wait until the native dialogue appears.
3. Click the **second upper-left icon (Project)**. The original native project scope should show the full project line-number list, rather than just `A-008` and `A-08`.
4. Check the **DWG** checkbox near `Select All (N)`. The original native Filter field should contain the unique physical tag in the current opened DWG and the native line checklist should narrow accordingly.
5. Check that actual original checkbox next to that tag, and verify Create becomes enabled if Autodesk permits the ISO.
6. Uncheck **DWG** to clear text; click the **second Project icon** to get the full project list. Repeat Project → DWG → Project at least three times.
7. If the list is still slow or shows too few routes, click the original **Project** button while the `DWG` checkbox is **off**. If that still takes 5–8 seconds, compare with running native `PLANTPRODUCTIONISO` without SPTC. In F2, inspect `SPTC REV27: Native dialog detected after N ms; enhancements attached in M ms.` These timings distinguish Autodesk's native loading from the extra plugin UI attachment.
8. Test one known-good ISO, confirming the copied output arrives in the selected custom location if provided.

**Limitations:** The physical LineNumberTag scan occurs on the first DWG checkbox click and may briefly pause for a slow network-backed PnP lookup; it does not run automatically on dialog startup. The original Plant 3D Project-mode list might also require its own project database query on demand. Cloud compilation is not a runtime test.

**Important:** This revision deliberately changes the UI interaction: the top Autodesk icons are native source selectors again, while the supplemental DWG checkbox is the explicit active-tag filter. This prevents the previous mode/desynchronization and restores normal project-wide selection.
