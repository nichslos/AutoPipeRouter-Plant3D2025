# SPTC Production Iso 2025 (C# GitHub Actions prototype)

## Scope
This independent plugin adds the `SPTC_PRODISO` command. It opens a custom companion Windows Forms dialog with **Custom Save Location** immediately underneath **Revision Number**, plus Browse. It is not an injected field in the original Autodesk dialog.

Click **1. Open Production Iso** to use Autodesk's native Production Iso line selection and generation. When generation has fully finished, click **2. Copy finished DWGs** to copy newly created/modified DWGs to the separate destination folder. Generation always writes to its normal native project directory first.

The project is read-only relative to Plant 3D Project Manager settings, existing isometric configurations and original drawings. It doesn't modify `IsoConfig.xml` or original DWG contents, and the copied DWGs are not registered as Production Iso documents in Project Manager.

## Build with GitHub Actions
Open the pull request containing these changes and inspect **Build SPTC Production Iso DLL** CI run. After it succeeds, download the `SPTC-ProductionIso-2025-DLL` artifact, extract `SPTC.ProductionIso.dll`, and use Plant 3D 2025's `NETLOAD`. Then run `SPTC_PRODISO`.

Uses .NET 8 and the same AutoCAD.NET package 25.0.1 as the repository's existing SupportDrawings project. Builds occur on GitHub's Windows runner; no .NET SDK or Visual Studio installer is required locally.

## Safety and limitations
- Do not run ISO jobs concurrently in the same source folder: change detection is based on DWG file modification timestamps and size, not line numbers.
- The custom **Revision Number** field is informational. Set the actual revision in the native Autodesk dialog.
- Auto-detection searches upward from the active drawing for a project `Project.xml`; if not detected, enter the native source folder manually.
- Destination must be outside the native source folder and not its parent.
- Existing destination files are never overwritten, instead `_copyN` is appended.
- If a DWG is still in use, copying skips it. Wait until native ISO generation is complete.
- This is an untested Plant 3D runtime prototype. Successful CI compilation alone doesn't prove Autodesk integration.
