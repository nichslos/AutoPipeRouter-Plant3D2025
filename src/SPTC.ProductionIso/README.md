# SPTC Production Iso REV20 — Manual Folder Scan and Resizable Line Numbers

**Baseline:** REV19. All previous revision branches and DLLs remain unchanged.

## Requested changes
**1. No startup folder scanning.** REV18's automatic folder precache is **not launched** when Create Production Iso opens. The native dialog should become available without extra project-wide background PnP scans. The dropdown still uses the existing REV16 on-demand incremental scan with progress and Cancel when a folder is actually selected. A completed folder is cached only within the current dialog, so selecting it again does not rescan.

**2. Wider, draggable left panel.** REV20 adds a narrow vertical resize grip on the boundary between the native **Display lines** and **Output settings** group boxes. Drag the grip right to widen the Line Numbers list and show long pipeline names without relying on horizontal scrolling; drag left to reduce it. When the dialog is very wide, the left pane starts wider automatically.

The grip is enabled only if the running AutoCAD Plant 3D 2025 dialog exposes the two original **Display lines** and **Output settings** sections as sibling WinForms GroupBoxes. Both are required. If Autodesk uses a different native UI layout, the plugin leaves the original controls alone rather than guessing, and the drag grip will be unavailable. This is a safe fallback; please report the screen behavior in that case.

The implementation keeps the native original line checklist (not a copy). It tries both regular WinForms parent layout and the fixed-left-column TableLayoutPanel case, preserving space for Output settings. Any nested line-list container grows with the left border. The Custom Save Location field is repositioned in sync with the native Revision number row during resizing, retaining REV19 alignment.

## Preserved
- REV05 automatic copying of completed ISOs to Custom Save Location.
- REV06 Select All and REV17 **Select All (N)** counter.
- REV09–11 Active DWG / All line-number toolbar toggle.
- REV15 first-level drawing-folder dropdown in the Line Numbers filter row.
- REV16 folder scan with progress/Cancel on **user selection only**.
- REV19 responsive Custom Save Location label, textbox and Browse.
- Original project data, default ISO settings, PnP tables and model DWGs unchanged.

## Test
1. Close Plant 3D completely, restart and NETLOAD REV20 `SPTC.ProductionIso.dll`.
2. Open a drawing and run `SPTC_PRODISO`. Verify the normal list appears without an automatic folder scan.
3. Look at the border between **Display lines** and **Output settings**. On supported native UI versions, a thin resize grip should appear; drag right to widen the Line Numbers list.
4. Confirm long line names become visible without horizontal scrolling and the Output settings controls (Iso Style, Revision, Custom Save Location, Browse) remain usable.
5. Stretch the entire dialog width and adjust the divider again. Verify the Custom Save Location textbox remains aligned with Revision and resizes.
6. Choose a folder from the dropdown; scanning should begin **only then**. Check that Select All (N) continues to count the displayed items.
7. Generate one known-good ISO to verify the selected native line checkbox and custom export location. Avoid generating all project ISOs until the line selection is confirmed.

**Runtime limitation:** GitHub compilation is not a Plant 3D GUI runtime test. Native dialog control structures differ among Autodesk builds. If the divider fails to appear or is unstable, send a screenshot and AutoCAD F2 output; the original REV19 build remains available.
