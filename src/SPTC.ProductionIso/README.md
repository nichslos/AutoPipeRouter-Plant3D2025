# SPTC Production Iso — REV05 (automatic custom-folder copy)

## Behavior
`SPTC_PRODISO` opens Autodesk's original **Create Production Iso** dialog and attaches a **Custom Save Location** field immediately below Revision Number.

**REV05** begins monitoring for new ISO DWGs as soon as the native Create Production Iso dialog closes. It also recognizes when the **Isometric Creation Results** dialog first appears. **Opening or closing Results is no longer required to start the copy.** ISO generation can take additional time after the Create dialog closes, so the background worker waits for stable and unlocked DWG(s) before exporting.

Network directory enumeration, file inspection and copying are run entirely on a worker thread, not on AutoCAD Plant 3D's UI thread.

**Important:** These are EXPORT COPIES. Autodesk still generates and registers the original ISO in the project-configured `ProdIsos\Drawings` folder. This plugin does not change the actual native-generation directory.

## Install / test
1. Completely close and restart Plant 3D 2025 to unload any earlier REV02/03/04 DLL.
2. Extract the REV05 DLL and run `NETLOAD` on `SPTC.ProductionIso.dll`.
3. Run `SPTC_PRODISO`; select one pipe line and choose the custom folder with the injected Browse button.
4. Click native **Create**.
5. Once the native output DWG is stable, REV05 should copy it to the custom folder automatically. No action on the completion toast or Results dialog is required.
6. For a status readout, run `SPTC_ISOSTATUS` after the native dialogs are dismissed. It reports the detected source path, selected destination, whether Results was observed and background copy state.
7. `SPTC_ISOCOPY` remains available to manually start/restart a background copy if needed.

## Copy safety / limitations
- No modifications to Project Manager, `Project.xml`, `IsoPart.xml`, `IsoConfig.xml`, the source DWGs or ISO style settings.
- Only recent DWGs in the native `ProdIsos\Drawings` folder are considered; this uses file modification times compared against the session start. Concurrent users generating in the same style folder may cause unrelated new DWGs to be exported.
- Each output DWG must have a stable size and modification timestamp over multiple polls and must be unlockable.
- Destination files are never overwritten; `_copyN` is added to the name if an output file already exists.
- The background file watcher can poll for up to 10 minutes. If Plant 3D generates no DWG during that period, no output is copied.
- Native UI injection into a third-party dialog is an unsupported experimental technique; monitor Plant 3D responsiveness when testing. A successful GitHub compilation does not establish runtime correctness.
