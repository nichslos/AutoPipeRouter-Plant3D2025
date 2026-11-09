# SPTC Production Iso — REV04 performance fix (test build)

## Primary fix
REV03 performed recursive file enumeration on a mapped network drive (L:) in AutoCAD Plant 3D's UI thread, including scanning before native generation. This can make Plant 3D appear frozen.

REV04 does not scan network folders on the UI thread. Native window checks run at 1500 ms intervals instead of 300 ms. After native **Isometric Creation Results** closes, DWG enumeration and copying run on a background worker. Results are reported on the command line after the worker finishes.

## Usage
1. Exit AutoCAD Plant 3D completely to unload older DLLs.
2. In Plant 3D 2025 run `NETLOAD` and load the REV04 `SPTC.ProductionIso.dll`.
3. Run `SPTC_PRODISO`. In Autodesk's original dialog, choose **Custom Save Location** under Revision Number.
4. Select the line number(s), generate Production Iso normally, and wait until it finishes.
5. Close the Isometric Creation Results dialog; copying to the custom location then starts in the background.
6. View the command line message `SPTC ISO: ... DWG(s) copied`.

If the custom folder is blank, no copying occurs and native ISO output remains in the default project folder.

## Important limitations
The original Production Iso remains in the default `ProdIsos\Drawings` folder and remains project-registered. Custom output is a copy; no direct per-run native generation override has been verified for Plant 3D 2025.

This technique attaches an experimental WinForms child control to Autodesk's native dialog. Runtime testing is required, and it may be affected by Plant 3D updates or UI scaling. The REV04 build aims to remove the most likely performance regression but is not yet runtime-validated.

No changes are made to Project Manager, shared XML configurations, or existing DWGs. Do not run concurrent ISO jobs in the same output folder: changed file detection uses timestamps.
