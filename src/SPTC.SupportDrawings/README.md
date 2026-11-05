# SPTC Support Drawings — REV 05

**Host:** AutoCAD Plant 3D 2025, x64, installed .NET 8-based runtime.

REV 05 responds to actual user test failures of REV 04, which reported `Unsupported: ImpCurve, Pipe` for a single support and the `424242` block in batch mode.

## Commands (all retained)

- `SPTC_SUPPORTEXPORT`: Existing 3D support sheet / DWG with multiple viewports, overall bounding envelope and sidecar CSVs.
- `SPTC_SUPPORTBATCH`: Existing checked block support selection and batch 3D support sheet export.
- `SPTC_SUPPORTMANAGER`: Existing batch manager.
- `SPTC_SUPPORTINSPECT`: Existing metadata/class audit CSV.
- `SPTC_SUPPORT2D`: **REV05** attempt actual 2D model-space orthographic linework extraction.
- `SPTC_SUPPORT2DBATCH`: **REV05** batch 2D attempt for each selected support block.

## Changes for reported REV04 error

The 2D extractor now tries, in order:

1. Exact supported line/face/polyline geometry, as in REV04.
2. The general AutoCAD `Curve` API where the CAD object supports it. Its points are sampled into **96 chord segments per curve**, and the projected linework is **approximate**. A warning is written to the `*_projection_report.csv` file.
3. An in-memory, nondestructive `Entity.Explode` attempt for otherwise unsupported objects. Supported pieces are extracted while unsupported pieces are recorded. It does **not** modify, explode or replace source drawing objects.
4. If nothing yields real 2D segments, the command records a diagnostic projection report and attempts to generate a **different type of output**: `*_3D_VIEWPORT_FALLBACK.dwg` (plus its existing inventory and audit files). This is a **3D model in viewports**, not flattened 2D output, and is visibly labeled as such.

**The appearance of a `Pipe` object does not guarantee that AutoCAD can explode it into actual 2D line geometry.** The tool never fabricates a rectangle from the bounding box and calls it a pipe. Plant 3D-native support and pipe solids require an approved solid projection / Plant orthographic workflow to create true manufacturing views.

### Output naming

When actual 2D supported linework exists:
- `SUPPORT_2D_PROJECTION.dwg` or `..._2D.dwg`
- `*_projection_report.csv` (contains counts for approximate curve sampling, skipped entities, collapsed edges)

When actual 2D supported linework does **not** exist:
- A diagnostic `*_projection_report.csv` is still created.
- If `SheetExporter.Export` can clone the selected native objects, it creates `*_3D_VIEWPORT_FALLBACK.dwg` and standard REV03 sidecar CSVs. The original requested `*_2D.dwg` **is not created**.
- If even the native model clone fails, it prints the error without writing a false drawing.

In batch mode, completion prints the distinct counts: **actual 2D DWGs**, **3D visual fallback DWGs**, and **failed/skipped**. An output is never overwritten automatically.

## Installation & test

1. Download the **SPTC-SupportDrawings-Plant3D2025-REV05** artifact from the completed GitHub Actions run, extract `SPTC.SupportDrawings.dll`.
2. **Close and reopen Plant 3D** to unload REV04.
3. Open a backed-up test piping/support DWG and `NETLOAD` the new DLL.
4. Run `SPTC_SUPPORT2D` on an `ImpCurve` element, then separately on a `Pipe`. Confirm the command reports either actual 2D linework or the explicit 3D visual fallback.
5. Run `SPTC_SUPPORT2DBATCH`, select the same support block `424242`. Inspect the DWG and the CSV. Check the new actual/fallback counters.
6. Run `SPTC_SUPPORTINSPECT` on the same support and share `*_entity_audit.csv` if further support solids extraction is required.

## Important limitations

- A sampled `Curve` is an approximation: not equivalent to exact NURBS, arc or solid contour data; **not appropriate for fabrication dimensions**.
- Exploded pieces may omit geometry and do not implement hidden-line removal.
- Actual pipe solids, Plant structure members, and other custom entities may not be decomposable by AutoCAD's generic `Explode` API.
- REV05 does not invoke the native Plant 3D Ortho API, nor does it automatically create true support-to-pipe fabrication drawings from unsupported solids.
- All earlier support commands are retained; drawing output and support identity should be checked inside your installed Plant 3D version. Compilation alone is not enough.
- The package keeps the functioning .NET 8 SDK references. Previously supplied alternate Plant 3D SDK ZIP contains multiple .NET 10-targeted binaries, which must not be mixed into this build.

## Longer-term solution for the example video's result

Use the native Plant 3D orthographic drawing generation system (or an appropriately tested AutoCAD solid/surface `FLATSHOT` conversion), coupled to support geometry classification and an engineering drawing template. Accurate material cutting lengths and dimensioning require model-specific verification and matching Autodesk API references. This is the intended engineering roadmap, **not** a REV05 capability claim.
