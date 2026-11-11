# SPTC Production Iso REV30 — Real Native Checkbox in Active DWG Filter

**Baseline:** REV29. Earlier revision branches and DLLs remain unchanged.

## Reported bug
With REV29, clicking any of the three top-left toolbar buttons shows the exact active DWG line (e.g. `A-08`) but selecting its checkbox is unreliable; the highlighted route is not genuinely checked in the underlying Autodesk list, so **Create** remains disabled.

## Root cause
REV24–26 and REV29 use `ProjectFolderLinePicker.ShowExactActiveDwgTags` to position a **second, temporary `CheckedListBox`** directly over the native Autodesk Production Iso checklist. Although its line labels look correct, a click goes to the plugin's proxy and then gets relayed to a different, obscured Autodesk row. Repeated attempts at synthetic mouse events and delayed `ItemCheck` forwarding did not reliably satisfy Autodesk's actual Create-button validation.

## REV30 fix
- **Keep the REV24-style three-toolbar-button Active / All toggle:** one click activates the current-drawing filter, the next clears it, subsequent clicks alternate. The original Autodesk toolbar's separate Drawing/Project/Selection commands are still intercepted in this SPTC dialog, as in REV24/REV29.
- **Do not create the temporary proxy checkbox list when Active DWG mode is selected.** Instead, identify the original native Filter textbox inside the live, resizable **Display lines** group and put the one verified physical Line Number Tag there. The original **Autodesk checklist stays visible and clickable**. The genuine native checkbox event can enable the genuine Create button, subject to Autodesk validation.
- **Clear the original Filter textbox** when Active mode is toggled off. This restores unfiltered lines from the currently loaded native scope; it cannot by itself switch Autodesk's Drawing/Project scope. Full Project restoration is not guaranteed when the native scope was Drawing-only.
- **Safety for multiple routes:** a single Autodesk native filter text cannot represent several nonmatching exact tags. When the current DWG contains zero or several verified physical line tags, REV30 refuses to pretend a single active route exists. The native list is left unchanged with an F2 diagnostic. No pipe/tag/database edits are made.
- **Resizer-safe Select All (N):** the original line checklist is now found inside the actual resized Display lines group rather than assuming it occupies less than 33% of the entire dialog width. Users can click the genuine checkbox manually (preferred for initial testing).
- The original checked list remains the sole source of selection truth; **Create is never force-enabled**.

## Unchanged
- REV25 vertical splitter resizing and first-level Plant 3D Drawings folder filtering. The folder-specific projection is *unchanged* and may retain its earlier relay limitations; REV30 specifically fixes the **active-DWG toolbar filter** reported in the screenshot.
- REV05 custom completed-ISO DWG copying, REV19 aligned Custom Save Location, Select All (N), on-demand folder scanning.
- No project/database, piping model, IsoConfig.xml, or default production ISO output changes.
- No startup lag-specific changes (as requested earlier).

## Test checklist
1. Fully exit AutoCAD Plant 3D 2025 and reopen; `NETLOAD` REV30 `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO`. Confirm the native all-lines list appears at startup.
3. Click an upper-left button once. The Filter textbox should now visibly contain the actual single physical line tag, e.g. `A-08`, and Autodesk's **original** line checklist should narrow.
4. **Click the checkbox beside `A-08`, not just its text.** Verify the tick stays and the original Create button becomes enabled if the route is eligible.
5. Click the same upper-left button again. The search text should clear and the native unfiltered list should return. Note that the original native list scope remains whatever it was before filtering.
6. Test a second Active → Unfiltered cycle, then generate **one** known-good ISO if Create is genuinely enabled, checking custom output copying if specified.

## If a fault remains
Close the dialog, press F2 and share any `SPTC REV30:` / `SPTC REV27:` / `SPTC REV29:` diagnostic lines along with a screenshot showing the Filter textbox and checkmark. If the original Filter field cannot be identified in your Autodesk build, the callback safely declines to apply the filter instead of drawing a second, misleading checklist.

**Build versus runtime:** GitHub Actions compilation confirms the C# build only. Active-line checkbox behavior must be verified in the actual Plant 3D 2025 dialog.
