# SPTC Production Iso — REV03 (automatic export test)

## What changed
`SPTC_PRODISO` still opens Autodesk's **original Create Production Iso** dialog and inserts a Custom Save Location textbox and Browse button below Revision Number.

**REV03:** Once the native **Isometric Creation Results** dialog has appeared and is subsequently **closed**, the plugin automatically copies newly generated or updated Production Iso DWGs to the selected custom location. No second export command is required for this workflow.

The original DWG **continues to be generated into the configured default project folder**, and the Results window continues to report that location. The custom folder receives a copy, not a native-registered DWG. This is intentional: a direct per-run native destination override has not been verified in Plant 3D 2025.

## How to use
1. Extract the GitHub Actions artifact and load `SPTC.ProductionIso.dll` with `NETLOAD`.
2. Run `SPTC_PRODISO`.
3. Select **Custom Save Location** or leave it empty.
4. Choose line numbers and click native **Create**.
5. Wait until Autodesk says the ISO generation has completed, then **close the Isometric Creation Results window**.
6. REV03 should automatically copy only new/changed DWGs to the custom folder. Check the AutoCAD command line for a count.

If the custom location was empty, generation stays at the original project path and no copy runs.

## Diagnostic / recovery command
`SPTC_ISOCOPY` still exists as an optional manual retry after the generation finishes. It uses the recorded native source and destination.

## Safety and known limitations
- Does not modify Project Manager, Project.xml, IsoPart.xml, IsoConfig.xml, styles, the original DWGs, or shared project paths.
- Detects the native source path by reading the original dialog's Save Isos to text; this may fail if UI control structure differs. If no native source is detected, the export command reports this instead of guessing.
- Tracks changed DWGs using size and write timestamp, not line numbers; avoid concurrent ISO generation into the same project style.
- Does not overwrite destination files; appends `_copyN` instead.
- Results title must match `Isometric Creation Results`. If native Results is not detected, run `SPTC_ISOCOPY` manually.
- The injected WinForms field is an undocumented experimental UI integration. Compiles on GitHub but still requires runtime validation in Plant 3D 2025.
- **Restart Plant 3D** to unload the old DLL before `NETLOAD` for the new version.
