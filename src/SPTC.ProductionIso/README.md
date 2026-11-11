# SPTC Production Iso REV25 — Responsive Folder Dropdown and Vertical Resize Grip

**Baseline:** REV24. All earlier revisions remain untouched.

## Fixed: 3-second blank folder popup
When the user clicked the folder arrow in REV24, `ToggleFolderMenu` synchronously called `ProjectFolderLineCatalog.Initialize`, which reads Piping project drawing records and PnPDataLinks metadata. On a large/network project, this blocked the CAD UI and temporarily displayed an unpainted blank 248×239 folder popup.

REV25 changes the workflow:
- The dropdown paints `All Project` plus a **Loading folders...** message immediately.
- Only immediate directory names under the current project's physical **Plant 3D Drawings** folder are enumerated, asynchronously, using a background **filesystem-only** task. No DWG enumeration or database work is done just to open the menu.
- Folder metadata is loaded from `ProjectFolderPath` on the Plant 3D UI thread, then only a plain directory path string is passed to the worker. **Autodesk project APIs and PnPDataLinks are never called from a worker thread.**
- Only first-level folder names are shown; no nested subfolders appear. A physical folder that is not registered with Plant 3D will be rejected safely when selected.
- **PnP registration lookup and live line filtering are deferred until the user selects a particular folder**. The embedded progress panel is displayed before the expensive UI-thread registration call.
- No automatic startup folder scan is enabled.

A mapped-drive directory listing may still take time, but it no longer blocks painting or interaction inside the Production Iso dialog. If the physical `Plant 3D Drawings` directory cannot be resolved, the menu reports this via AutoCAD F2 rather than scanning unrelated folders.

## Fixed: vertical resize-grip height
REV20–24 positioned the gray Display lines/Output settings draggable divider using `_initialLeftHeight` captured when the native dialog first opened. Consequently the vertical bar became too short or extended beyond the group when the user stretched the dialog vertically.

REV25:
- Recomputes the bar's top/bottom from the **current** native Display lines GroupBox rectangle.
- Leaves a margin at the bottom of the section and stops before the native dialog's bottom buttons.
- Refreshes when the native group box moves or resizes, not only when the outer window width changes.
- Skips redundant Win32 `SetWindowPos` calls when bar geometry has not changed, reducing needless UI repaint work.

## Preserved
- REV24 original native checklist selection-event forwarding and Create eligibility checks.
- REV23 exact active/all line toggle.
- REV17 Select All (N), REV15 on-filter-row folder arrow, and on-demand REV16 folder scanning.
- REV19 responsive Revision/Custom Save Location alignment.
- REV05 automatic copying of completed Production Iso DWGs to the custom location.
- No changes to native ISO configuration, Project Manager database, existing drawings, or the default ISO output.

## Test REV25
1. Completely exit Plant 3D 2025. Restart and `NETLOAD` REV25 `SPTC.ProductionIso.dll`.
2. Open `SPTC_PRODISO`. Click the folder ▾ arrow. The dropdown should show **All Project** and **Loading folders...** immediately, then only direct subfolders; the CAD dialog should remain responsive. No project DB indexing occurs merely from opening the list.
3. Select one known folder. Only at that point should the existing progress/Cancel view and PnP verification begin.
4. Stretch the entire window vertically (taller and shorter) and check that the gray draggable divider stays inside the full height of Display lines, ending above the bottom of that group.
5. Drag the divider horizontally and verify native line-number list/checkmarks, filter/funnel/toolbar positions and the Custom Save Location row remain correctly aligned.
6. Select one known-good line and generate one test ISO, verifying the resulting DWG arrives in the custom location when supplied.

**Status: Experimental until runtime-tested on the user's specific Plant 3D 2025 project.** A GitHub build verifies C# compilation only; Autodesk native dialogs and network database behavior vary by installation.
