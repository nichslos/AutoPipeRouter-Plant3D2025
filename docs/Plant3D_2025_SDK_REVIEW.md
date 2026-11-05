# AutoCAD Plant 3D 2025 SDK reference review

Reviewed on 2026-11-05 from the user's supplied `sdk.zip` (ZIP CRC passed). **Only original analysis is recorded here; Autodesk binaries, sample code, and documentation are not committed.**

## SDK composition

The archive's root folder is `AutoCAD Plant 3D 2025 SDK`. Relevant components:

| SDK location | Reason for use |
| --- | --- |
| `inc/AcMgd.dll`, `inc/AcDbMgd.dll`, `inc/AcCoreMgd.dll` | AutoCAD managed API for geometry, documents, layouts and database entities |
| `inc-x64/PnP3dObjectsMgd.dll` | Managed Plant 3D object model |
| `inc-x64/PnP3dDataLinkManager.dll` | Plant 3D model/data linkage |
| `inc-x64/PnP3dPartsRepository.dll` | Plant 3D parts repository |
| `inc-x64/PnPDataLinks.dll` and `PnPDataObjects.dll` | Project metadata/data objects |
| `inc-x64/PnPProjectManagerMgd.dll` | Plant 3D project access |
| `inc-x64/PnP3dPipeUI.dll` | Piping interaction and support-related APIs |
| `docs/plantsdk_dev.chm`, `docs/plantsdk_ref.chm` | Local SDK developer and API documentation |
| `samples/Piping/PipeSupport` | Support creation, conversion, editing, attach/detach examples |
| `samples/Piping/PipeRouting` | Pipe and fitting creation, connections, routing |
| `samples/Piping/CreatePipeline` | Native Plant 3D pipeline and connectivity APIs |

The PipeSupport sample declares commands `SupportScriptCreate`, `SupportSpecCreate`, `SupportBlockCreate`, `SupportSelectionCreate`, `SupportConvert`, `SupportAttach`, `SupportDetach`, `SupportEdit`, `SupportRegister`.

The PipeRouting sample declares `PipeRoute`, `FittingAdd`, `FittingEdit`, `FittingSubstitute`.

## Framework compatibility warning

**Do not replace the current working .NET 8 plugin references with this SDK's assemblies without matching the host runtime.**

- The SDK's `inc/AcMgd.dll`, `inc/AcDbMgd.dll`, `inc/AcCoreMgd.dll`, `inc-x64/PnP3dObjectsMgd.dll`, and `PnP3dDataLinkManager.dll` contain `.NETCoreApp,Version=v10.0` target markers.
- The earlier application assemblies supplied for the Plant 3D 2025 installation contain `.NETCoreApp,Version=v8.0` markers and differ in binary size/content.
- Several bundled dotNet sample projects explicitly target `net10.0-windows`.
- The existing successfully compiled and user-tested `SPTC.SupportDrawings.dll` targets `net8.0-windows`. Preserve this working path unless host compatibility is confirmed.

**Therefore:** The ZIP can guide API design but must not be assumed to be a build-compatible reference package for the user's active .NET 8 runtime. For genuine native-support integration, obtain **matching .NET 8 Plant 3D runtime reference assemblies from the installed Plant 3D build** or confirm that the local installation has moved to .NET 10 and intentionally migrate the project.

## Recommended native support development path

1. Keep `SPTC_SUPPORTEXPORT`, `SPTC_SUPPORTBATCH`, and `SPTC_SUPPORTMANAGER` working.
2. Inspect native support entities, ownership, metadata and geometry through Plant 3D managed APIs; keep generic AutoCAD-entity fallback.
3. Export support identification and *verified* member properties when the schema actually provides them. Do not infer material grade, welding requirements, hole dimensions, or fabrication BOM quantities.
4. Develop actual 2D projections and CAD dimension entities separately, with real-world visual comparison against a representative support DWG.
5. Run automated geometry checks, compile on GitHub Actions, and test `NETLOAD` inside Plant 3D before calling a revision verified.

**Do not commit SDK DLLs or copied Autodesk sample sources to GitHub without verifying Autodesk's redistribution rights.** The existing public NuGet build for basic AutoCAD API remains the safe baseline pending native Plant 3D reference compatibility.
