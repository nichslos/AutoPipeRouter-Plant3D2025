# SPTC Production Iso — REV02 native-dialog experiment

## Objective
The `SPTC_PRODISO` command opens Autodesk's **original Create Production Iso** window, then attempts to attach **Custom Save Location** and a Browse button directly under **Revision Number**.

It does not replace the native line-number list, Iso Style selector or Create button. The field is attached at runtime with a managed WinForms child control. Autodesk does not document this UI-injection technique, so it is **experimental** and could fail or require position adjustments depending on Plant 3D 2025 updates, display scaling and UI implementation.

## Important generation limitation
Plant 3D still generates Production Iso DWGs to the project-configured native directory. The chosen custom folder is used only to **copy completed DWGs** later; it is **not** a native generation output setting. No edits to Project Manager, IsoConfig.xml, ISO style configuration or original DWG files.

## Usage
1. Compile on GitHub Actions using `build-production-iso.yml`, download `SPTC-ProductionIso-2025-DLL` artifact.
2. Use `NETLOAD` to load `SPTC.ProductionIso.dll` in Plant 3D 2025.
3. Run `SPTC_PRODISO`. The native dialog should open with an added destination field below Revision Number. Choose the destination with its Browse button.
4. Select line number(s) and click Autodesk's native **Create**.
5. **Wait for native ISO generation to finish.**
6. Run `SPTC_ISOCOPY` from the command line. It copies new/modified DWGs from the native source into your custom folder without overwriting originals.

## Cautions
- Not tested within Plant 3D. Successful cloud compilation is not runtime verification.
- Native output directory detection reads a child control of the native dialog; it can fail if Autodesk's window implementation differs. If detection fails, `SPTC_ISOCOPY` stops without copying.
- Do not run concurrent Production Iso jobs in the same source directory. DWG file timestamps and file sizes distinguish changed files; another user's changes could be included.
- Avoid loading an old revision simultaneously. Restart Plant 3D to unload an earlier DLL before NETLOAD of this version.
- No guarantee that a WinForms child control will remain visible or accept input inside Autodesk's modal window.
