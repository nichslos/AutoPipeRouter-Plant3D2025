# SPTC Production Iso REV32 — Restore Folder Dropdown

**Baseline:** REV31. All older revision branches, DLLs, and features remain unchanged.

## Problem from user screenshot
Clicking the folder dropdown next to the Line Numbers filter showed:

```
Plant 3D Drawings folders
All Project (no folder filter)
Folder list unavailable — see F2 log
```

In REV25–31, the quick folder list **assumed** that Project Manager's `Plant 3D Drawings` node must correspond to a real physical directory at `<ProjectFolderPath>\Plant 3D Drawings`. In Plant 3D, project drawing hierarchy and the directory layout are not necessarily identical. If that directory does not exist, `Directory.Exists` fails even when registered project folders and DWGs exist.

## REV32 changes (folder discovery only)
- Replace the incorrect physical-directory assumption with the **current project's registered Piping drawing metadata**.
- Call `Piping.GetPnPDrawingFiles` to obtain registered DWG paths, then reuse the existing `FolderOf` hierarchy parser to extract only the **first-level folder under Plant 3D Drawings**. Nested subfolders are not shown independently. Root DWGs do not introduce a fake folder.
- Enumerate names **on demand when the user opens the dropdown**. No project-wide PnPDrawings SQL join, DWG opening, geometry scan, or file-system directory walk occurs merely to populate the folder menu.
- All Plant 3D API/reflection access stays on the CAD UI thread; the code yields to the WinForms message pump every 32 paths (when a synchronization context exists). **No Task.Run with Autodesk objects.**
- If the folder index from a previous user selection is already available, reuse its registered folder names immediately.
- Add `SPTC REV32: Folder menu loaded N top-level folder(s) from registered Plant 3D Piping DWGs.` to AutoCAD F2 output.
- On failure, show `Folder list unavailable — see F2 log` with a more specific `SPTC REV32: Registered drawing folder list failed: ...` diagnostic. No unexpected Windows folders are offered as a fallback.

## Unchanged
- Folder-specific line verification starts **only after choosing a folder** and uses the prior PnP read-only verification and progress/Cancel workflow. No registration/PNP data writes.
- REV31 native exact active-DWG line number view, original checkbox, Select All (N), three top buttons.
- REV25 adaptive resizable line-number pane/vertical grip, REV19 Custom Save Location, automatic copying of completed ISO DWGs.
- No changes to startup lag, Autodesk ISO generation, project file/drawing names, default ISO style/configuration, `IsoConfig.xml` or Project Manager.

## Test
1. Restart Plant 3D 2025 and `NETLOAD` REV32 `SPTC.ProductionIso.dll`; run `SPTC_PRODISO`.
2. Click the folder arrow in the Line Numbers filter row. You should see **All Project** and only the registered *direct child* folders visible under Project Manager → Plant 3D Drawings, not unrelated system directories.
3. After folder names load, F2 should report `SPTC REV32: Folder menu loaded ...`.
4. Select a small folder and confirm line-tag verification runs only for that selection. Check `Select All (N)` reflects the list.
5. Return to All Project and confirm no change to original drawing/project data. Test one known-good ISO separately before any production batch.

**Runtime caveat:** GitHub Actions compilation verifies C# compatibility, not the actual structure of the user's Plant 3D project hierarchy. If the dropdown still cannot list folders, share the complete `SPTC REV32:` line from F2; the exception text will distinguish missing project metadata from missing registered Piping DWG paths.
