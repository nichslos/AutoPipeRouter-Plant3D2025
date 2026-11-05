# AutoPipeRouter — Plant 3D 2025

C# .NET 8 (x64) automatic pipe-routing **prototype** for AutoCAD Plant 3D 2025.

## Commands

- `SPTC_ROUTEPREVIEW` — select two 3D points and an XYZ axis order; creates an ordinary AutoCAD 3D polyline centerline.
- `SPTC_AUTOROUTE` — creates the same centerline, then attempts to pass it to Plant 3D's native `PLANTCONVERTLINETOPIPE` command. Pipe/fitting generation must be verified inside Plant 3D.

**Not yet obstacle-avoiding:** no clash detection, minimum fitting tangent lengths, port direction checks, or topology optimization. Always test on a backup drawing. A successful compilation does **not** establish that the plugin's routing commands work in Plant 3D.

## Get the compiled DLL (online)

1. Open the **Actions** tab of this private GitHub repository.
2. Select **Build AutoPipeRouter DLL**, choose **Run workflow**, or inspect the run created after a push to `main`.
3. When the Windows build succeeds, download the **AutoPipeRouter-Plant3D2025-net8** artifact from its run page.
4. Extract `AutoPipeRouter.dll` from the downloaded ZIP. In a compatible Plant 3D 2025 test drawing, type `NETLOAD` and select that DLL.

## Build locally

Install the .NET 8 SDK, then run:

```powershell
dotnet run --project tests/GeometrySmokeTests/GeometrySmokeTests.csproj -c Release
dotnet build src/SPTC.AutoRoute/SPTC.AutoRoute.csproj -c Release
```

DLL: `src/SPTC.AutoRoute/bin/Release/net8.0-windows/AutoPipeRouter.dll`.

Build references: Autodesk's `AutoCAD.NET` NuGet package version `25.0.1` (compilation only). The repo does **not** include Autodesk binary reference files. Confirm the actual installed 2025 update uses .NET 8; some later Plant 3D 2025 updates target .NET 10. Native Plant 3D conversion requires Plant 3D, not just AutoCAD.
