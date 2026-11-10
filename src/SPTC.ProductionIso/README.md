# SPTC Production Iso REV16 — Large-Folder Incremental Scan + Cancel

**Baseline:** REV15. All earlier revisions remain untouched.

## Change
- Remove REV15's fixed **120 DWGs per folder** restriction.
- When a folder is chosen from the filter-row dropdown, inspect its registered Plant 3D DWGs in cancellable increments. The original Autodesk PnP DataLinks methods are invoked **only from the Plant 3D UI thread**, with asynchronous yields between drawings and longer runs of membership checks. They are **not** moved to `Task.Run`, which would risk unsafe cross-thread database access.
- An **embedded progress panel** in the original dialog shows `Scanning n / total DWGs`, how many active line tags have been verified, and **Cancel**.
- Cancel stops scanning at the next cooperative yield; **no incomplete result is applied**. The original Production Iso list remains available.
- Only on a **fully verified** scan does the filter draw the folder's native-backed line list and clear hidden checked routes.
- Successful folder-tag results are cached during that dialog session. No SQL, DWG, Project Manager, or ISO configuration writes.
- Very large or anomalous data (more than 500,000 database row checks in one request) still fails safely with a diagnostic rather than hanging indefinitely. This is **not** a 120-DWG limit.

## Preserved from REV15
- Folder dropdown near the original Line Numbers Filter field.
- Only first-level project folders under Plant 3D Drawings.
- `All Project` restores the native list.
- REV06 Select All.
- Current DWG / All line-number toggle (REV09–11).
- REV12 aligned Custom Save Location.
- REV05 automatic background ISO DWG copying to the custom path.

## Recommended test
1. Completely exit Plant 3D 2025, restart and `NETLOAD` REV16 `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO` and click the arrow on the Line Numbers filter row.
3. Choose the folder previously triggering the **more than 120 DWGs** warning.
4. Watch the incremental progress indicator; test **Cancel** once to confirm the native dialog stays open and the list is not partially filtered.
5. Reopen the dropdown and select that folder again. Confirm that a completed scan produces the expected tags.
6. Choose `All Project` to restore unfiltered native lines.
7. **Do not generate batch production ISOs until you verify the selectable native line checkboxes match the expected folder routes.** Start with one known-good pipeline.

## Caveats
- Cloud compilation confirms API *signatures within this plugin*, not whether Autodesk's runtime reflection-based `PnPDataLinks` access and checked-list detection match your Plant 3D 2025 installation. If the scan reports a relationship or checkbox problem, run `SPTC_FOLDERDIAG`, press F2, and send the output.
- An individual blocking Autodesk database method call cannot be interrupted instantly; Cancel is processed between calls, and database latency depends on the project's server.
- Project folders are derived from the registered Piping drawing paths. Empty folders without DWG registration are not listed.
