# SPTC Production Iso REV22 — Responsive Native Toolbar and Exact Active Lines

**Baseline:** REV21; prior revision branches and test builds remain untouched.

## User issues addressed
1. **Top toolbar icons do not follow Display lines resizing.** REV20–21 repositioned the line-number checklist and filter row but left Autodesk's three Drawing/Project/Filter toolbar icons at their original positions. REV22 detects the three actual icon controls (or their shared ToolStrip/panel), records their original offset from the Display lines right edge, and repositions them whenever the user drags the splitter or resizes the native window. Click events remain Autodesk's original events.
2. **Temporary blank/overlapping UI while line numbers load.** The active/folder list projection is discarded immediately when the native toolbar switches scope, before Autodesk rebuilds its native list. Stale checkbox indices are never used during that refresh. The former REV18 full-project folder precache is still disabled; importantly, REV22 also prevents a subsequent manual folder scan from restarting that background precache inadvertently.
3. **Old registered line numbers still appear in active drawing view.** The old text-filter implementation matched prefixes/substrings. For example, typing `CAP_BLIND+SOURCE_MODEL_XY45DEGROT_S-011-6` could also match `...-66666`. REV22 no longer writes any auto-detected line tag into the native search textbox. Instead it creates a temporary **EXACT-match** view over Autodesk's original checklist, forwarding user check/uncheck operations to the real native items. Hidden outdated rows are deselected to avoid exporting unrequested ISOs.

## Which route is considered active?
- At the first user request for the active-DWG view, REV22 scans only **physical Plant 3D components in Model Space** of the current DWG. It no longer performs this model scan during the initial `SPTC_PRODISO` command, improving startup responsiveness.
- REV22 preferentially reads each physical component's **LineNumberTag** property (the same attribute exposed in Plant 3D Properties), instead of relying on old `P3dLineGroupPartRelationship` group tags. For components that do not expose the property, the relationship is a fallback only.
- Only the **exact** verified tag(s) from the current DWG are shown. If the model truly contains multiple distinct current line numbers, both are shown rather than guessing which to hide. No pipe tags or PnP rows are deleted/renamed.
- If native line items have not yet loaded or a model tag is not in the current native list, the plugin keeps the original native list rather than showing a misleading partial subset.

## Other unchanged features
- REV05 automatic copying of completed ISO DWGs to Custom Save Location.
- REV06 Select All and REV17 `Select All (N)` counter.
- REV12/19 aligned, stretching Custom Save Location and Browse field.
- REV15 first-level Plant 3D Drawings folder dropdown on the filter row.
- REV16 on-demand folder scan with progress and Cancel, **no automatic startup cache scanning** (REV20).
- Native Production Iso generation and original Autodesk source/output locations; `IsoConfig.xml`, Project Manager and piping databases are not changed.

## Test REV22
1. Fully close and restart AutoCAD Plant 3D 2025, then `NETLOAD` REV22's `SPTC.ProductionIso.dll`.
2. Open the DWG whose current piping parts have the correct new `Line Number Tag` in **Properties**.
3. Run `SPTC_PRODISO`, confirm original unfiltered list appears quickly.
4. Drag the border between Display lines and Output settings to the right. Confirm the three top icons move to the right with the widened panel, and the filter dropdown/funnel stay aligned.
5. Click a top Drawing/Project icon once; confirm **only exact active line tags** appear inside the actual Line Numbers area and the search field stays blank.
6. Click again; the full list returns. Repeat, checking the toggle remains functional.
7. If old names still appear, run `SPTC_DWGTAGS` in a blank command line (outside the dialog) and press F2. This reports the actual physical-part tags and separate direct-versus-relationship counts. A physically present old tag must be corrected in the model separately, rather than hidden by a guess.
8. Optionally choose a folder and test on-demand scan and `Select All (N)`. Generate only **one** known-good ISO until the checkbox behavior is verified.

## Caveats
Native Autodesk dialog overlays, toolbar layouts and Plant 3D component properties are undocumented for this extension. Successful GitHub compilation verifies the DLL builds, **not** that this UI/runtime behavior is correct on every Autodesk installation. If the toolbar buttons do not move or the list remains incorrect, please send a screenshot and `SPTC_DWGTAGS` output. REV21 remains available.

No changes are made to the actual model geometry, PnP records, XML, project structure, or native ISO settings.
