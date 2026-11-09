# SPTC Production Iso REV08 — Verified Current-DWG Line Tags (experimental)

## Baseline and changes
This is the next numbered revision **REV08**, based on REV07 (including REV06 Select All and REV05 background ISO export). Earlier revisions remain untouched.

REV07's PickFirst/third-toolbar-Filter method proved unreliable: the line list became empty. REV08 instead scans real Plant 3D model-space components in the currently open DWG and reads their **P3dLineGroup** relationship and **Tag** through the Plant 3D DataLinksManager. The code uses runtime reflection, because the native Plant 3D assemblies are unavailable on the GitHub build runner. Plant-specific runtime behavior needs testing in Plant 3D 2025.

For **exactly one verified live line tag** (for example `A-008`) REV08 attempts to place that tag into Autodesk's own Line Numbers **Filter** textbox. This should hide stale list entries without editing or deleting their project records. The "DWG Only" button re-applies the exact-text filter; it no longer uses the third native Filter button or AutoCAD PickFirst selection.

For **zero or multiple verified tags**, REV08 does **not** guess or potentially hide valid routes. It leaves the native list alone; the button is disabled and `SPTC_DWGTAGS` prints the detected tags and diagnostics.

## How to test
1. Completely close Plant 3D so previous NETLOADed DLLs are unloaded, then reopen Plant 3D 2025.
2. Load REV08 `SPTC.ProductionIso.dll` with `NETLOAD`.
3. Open the DWG with the five components from the example whose current Properties report **Line Number Tag: A-008**.
4. Run `SPTC_DWGTAGS` and press F2. Confirm that the command reports **A-008** as the only active tag.
5. Run `SPTC_PRODISO`. The original Create Production Iso dialog should open with Select All and DWG Only preserved. Check whether the Line Numbers list contains only A-008. If needed click DWG Only.
6. If A-08 and A-088 remain, send the F2 output and a screenshot of the dialog. The native filter control is undocumented; it might require a different event hook.
7. Do not run Select All or generate ISO drawings until the route filter is verified.

## Preserved
- REV05 automatic copy of finished DWGs to the selected custom folder.
- REV06 Select All checkbox in the original Production Iso dialog.
- Autodesk Drawing and Project line-list buttons, original generated ISO paths, native result links.
- No changes to Project Manager, Project.xml, IsoPart.xml, IsoConfig.xml, drawings, piping models or PnP database.

## Limitations
- Experimental: compilation success does not verify the native Plant 3D runtime DataLinksManager reflection or text-filter event.
- The exact filter is enabled only for one active route. Multi-route DWGs need a separate verified UI mapping to filter multiple disjoint tag names safely.
- The underlying project line-group records remain available; this is a **display-only** filter.
- If problems occur, restart Plant 3D and reload REV07 or the confirmed working REV06.
