# SPTC Production Iso REV14 — Folder picker no longer closes the native dialog

Based on REV13; all prior revisions remain unchanged.

## Fix
In REV13, the extra dropdown was placed directly over Autodesk's existing filter funnel and used a separate WinForms `ContextMenuStrip` popup. In the user's Plant 3D 2025 runtime, clicking it caused **Create Production Iso** to close instead of showing the folder choices.

REV14 removes that overlapping control and the popup. It adds a distinct **Folders ▾** button beside **Select All** at the bottom of the left panel. Clicking Folders opens a scrollable **child panel inside the existing Autodesk dialog**, not a separate popup/window. Clicking Folders again or the × button closes only this child panel. The Autodesk native filter funnel remains visible and usable.

Folder sources and selection logic are still experimental REV13 behavior:
- All Project (no folder filter) clears the custom folder view.
- Registered Plant 3D Drawings folders are listed with DWG counts.
- Selecting a folder tries to show its verified live line groups in the native-backed checked list.
- If project APIs or the original native checked list cannot be resolved, REV14 reports diagnostics rather than altering the project.

## Test safely
1. Completely restart Plant 3D 2025 to unload the old NETLOADed DLL.
2. `NETLOAD` the REV14 `SPTC.ProductionIso.dll`.
3. Run `SPTC_PRODISO`.
4. Check that **Select All** and **Folders ▾** appear together at the lower left.
5. Click **Folders ▾** and confirm **Create Production Iso stays open** and a folder list appears inside it.
6. If folders appear, select one known folder, inspect its line numbers, then choose **All Project**.
7. Generate only one known-good ISO after native checked-state behavior has been verified.

For troubleshooting use `SPTC_FOLDERDIAG`, then F2 to copy the line-by-line diagnostic.

## Preserved
REV05 background export/copy to custom save location; REV06 Select All; native Drawing/Project Active/All toggle; REV12 aligned Custom Save Location row. No edits to Project Manager, model geometry, PnP database or `IsoConfig.xml`.

## Limitations
Cloud build success alone does not verify Plant 3D's proprietary dialog implementation. REV14 is a test build until verified in the user's 2025 environment. The project folder catalog may return a diagnostic if the installed Piping DataLinks API does not match the expected signatures. No additional executable is required.
