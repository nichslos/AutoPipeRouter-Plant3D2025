# SPTC Production Iso REV13 — Plant 3D Drawings folder line filter (test build)

## Based on
User-confirmed working **REV12**. No earlier revisions are modified. REV05 custom-location automatic output copy, REV06 Select All, REV09–11 active/full top-button toggle, and REV12 field alignment are all retained.

## New UI
- A small dropdown arrow is attached over the native **Line Numbers filter funnel**.
- Clicking it shows **All Project (no folder filter)** followed by registered project folders under **Plant 3D Drawings**.
- Parent folders include their child folders' drawings.
- Selecting a folder shows only the verified live line numbers from its registered DWGs.
- Returning to **All Project** removes the folder-specific list, restoring the actual Autodesk line list.

## How the selection works
The plugin does NOT delete/replace items in Autodesk's native line-list collection. Instead, a transient checked-list overlay renders the folder lines and forwards every check/uncheck to the corresponding **original native line number checkbox**. The native Production Iso generator continues to use its original selection objects.

REV13 looks up:
1. Project-registered DWGs from **Piping.GetPnPDrawingFiles** and the **PnPDrawings** records.
2. Drawing-to-line-group relationships via **P3dDrawingLineGroupRelationship**.
3. Live component memberships through **PnPDataLinks** and **P3dLineGroupPartRelationship**, ignoring old line-group rows with no actual components in the selected DWG.

If drawing ID matching, native list detection, or the PnP API differs from expectations, filtering does **not** start. The folder menu shows a diagnostic or a warning. Network file enumeration, XML edits, drawing modifications, and SQL writes are not used.

## Test
1. Exit Plant 3D 2025 completely and restart. NETLOAD REV13 `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO`, and confirm the original unfiltered project line list appears.
3. Click the small dropdown at the **right of Line Numbers Filter** (existing funnel position), and inspect the folders.
4. Choose a folder that contains known registered 3D piping drawings.
5. Check the shown line numbers. Select All should affect only visible folder lines.
6. Reopen the dropdown and choose **All Project**. The normal native line list should reappear.
7. Only after verifying correct checkbox selection, try generating **one test ISO**. Existing auto-copy remains unchanged.

If the menu reports missing relationships or the displayed lines are wrong, close the dialog and send the AutoCAD command-line diagnostic. Do not run bulk ISO generation from an unverified filter.

## Scope / limitations
- Folder names are taken from registered **file paths**. If Project Manager displays a custom folder alias different from the actual storage folder name, the dropdown may display the physical name in this experimental version.
- Empty Project Manager folders without registered DWGs are not listed.
- The folder filter requires that the native underlying list contain the project-wide routes. If it is in Drawing-only or other filtered mode, choose the native **Project** icon first.
- The custom folder list appears only if Autodesk's underlying Line Numbers control can be identified as a WinForms checked list/list view; otherwise the UI refuses to modify it.
- Folder queries are bounded (120 DWGs, 10,000 relationships) to avoid blocking the CAD interface on shared network projects.
- Does not modify Project Manager, Project.xml, IsoPart.xml, IsoConfig.xml, native Isometric output locations, drawings, or PnP data.

**Experimental.** Cloud compilation confirms build validity, not runtime correctness in Plant 3D 2025.
