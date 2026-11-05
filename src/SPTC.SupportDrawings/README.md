# SPTC Support Drawings — REV 06

Target: AutoCAD Plant 3D 2025, Windows x64, compatible **.NET 8** host installation.

## REV 06 correction — user-reported `eKeyNotFound`

User test case: `SPTC_SUPPORT2D` on two **Structure Member** objects (angle steel support) in AutoCAD Plant 3D 2025. REV05 reported `eKeyNotFound`.

### Suspected cause and fix

The 2D DWG exporter creates custom `SPTC_2D_TOP`, `SPTC_2D_FRONT`, and `SPTC_2D_SIDE` layers **inside an independent destination DWG Database**.

REV05 set `new Line { Layer = layerName }` **before** attaching that line to the new output database. When the line is not database-resident, AutoCAD may resolve the layer name against the editor's currently open source database. Because the output-only layer name may not exist there, this can produce `eKeyNotFound`.

REV06 now:
1. Creates/looks up the `LayerTableRecord` **ObjectId in the destination DWG**.
2. Creates and appends each projected line to the destination model space.
3. Calls `transaction.AddNewlyCreatedDBObject(line, true)`.
4. Assigns `line.LayerId = layerId` **after attachment**, not by the layer's name.

This is a **targeted hypothesis-based fix** supported by Autodesk's documented layer API behavior. Verification on the reported Plant 3D drawing is still required.

Also included:
- Explicit `*_REV06_ERROR.txt` diagnostics with managed exception stack traces on projection or fallback failures.
- Isolated transient object `Explode()` disposal errors so they are recorded as warnings instead of aborting an otherwise successful extraction.
- Existing REV05 curve sampling, geometry warnings and explicit 3D viewport fallback preserved.
- All commands retained: `SPTC_SUPPORTEXPORT`, `SPTC_SUPPORTBATCH`, `SPTC_SUPPORTMANAGER`, `SPTC_SUPPORTINSPECT`, `SPTC_SUPPORT2D`, `SPTC_SUPPORT2DBATCH`.

## Install

1. Download the GitHub Actions artifact **SPTC-SupportDrawings-Plant3D2025-REV06** from a successful build.
2. Extract `SPTC.SupportDrawings.dll`.
3. **Close and restart Plant 3D 2025** to unload REV05 (same .NET assembly identity).
4. Open a backed-up test DWG and enter `NETLOAD`, selecting REV06.
5. Run `SPTC_SUPPORT2D` on the same **two structural steel members**.
6. Inspect the output DWG's **model space**, the `*_projection_report.csv`, and confirm the three layers exist. If actual 2D geometry cannot be extracted, inspect the explicitly named `*_3D_VIEWPORT_FALLBACK.dwg` instead.
7. If REV06 still fails, share `*_REV06_ERROR.txt`. It contains the managed call stack needed to identify the remaining failing operation. Use `SPTC_SUPPORTINSPECT` for a separate detailed class audit.

## Engineering limitations

- Native Plant 3D Structure Member geometries are not automatically transformed into exact fabrication-profile geometry. The ordinary AutoCAD Curve API and nondestructive `Explode()` paths may or may not expose useful edges.
- `ImpCurve` sampling is approximate (96 parameter intervals), not exact arc/profile geometry or a fabrication tolerance. The output does **not** remove hidden lines.
- The 3D viewport fallback is **not** a 2D drawing and its name clearly says `3D_VIEWPORT_FALLBACK`.
- No authoritative part BOM, welds, hole sizes, cut lengths, fabrication-scale dimensions, or native Ortho project view authoring is included.
- Automatic cloud compilation checks assembly validity but **cannot validate runtime correctness** in your specific Plant 3D installation.
- Existing compatibility: compile against AutoCAD 2025 .NET 8 API. Do not substitute the separately uploaded .NET 10 SDK references.

## Rollback

The separately versioned REV05 and REV04 artifacts remain available on their prior GitHub Actions runs. Restart Plant 3D before loading a different SPTC.SupportDrawings DLL version.
