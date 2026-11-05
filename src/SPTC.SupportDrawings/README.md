# SPTC Support Drawings — REV 04

**Target:** AutoCAD Plant 3D 2025, x64, **.NET 8-based installation**.

REV 04 adds an explicit **2D orthographic linework export** for supported geometric edges. It preserves all REV 03 drawing commands and audits. This is a **limited geometry prototype**, not a complete support fabrication detailing solution.

## Commands

| Command | Description |
| --- | --- |
| `SPTC_SUPPORTEXPORT` | Existing support A3-style DWG: cloned 3D support, four live 3D viewports, overall envelope callouts, and sidecar inventories. |
| `SPTC_SUPPORTBATCH` | Existing batch export: select block inserts, review and export one DWG for each checked block. |
| `SPTC_SUPPORTMANAGER` | Existing batch manager alias. |
| `SPTC_SUPPORTINSPECT` | Existing class/handle/layer/property audit CSV for identifying actual support entity types. |
| **`SPTC_SUPPORT2D`** | **New:** select one support's *supported edge geometry* or supporting blocks and write true 2D AutoCAD `LINE` objects into top, front and side **model-space** views. |
| **`SPTC_SUPPORT2DBATCH`** | **New:** select separate support blocks, choose them in the batch manager, and write one 2D linework DWG and report per supported block. |

## 2D export technical scope

The tool projects from the source's world X/Y/Z coordinates into:

- TOP: X horizontal, Y vertical.
- FRONT: X horizontal, Z vertical.
- SIDE: Y horizontal, Z vertical.

It recursively expands ordinary AutoCAD block references up to 8 levels, applying each nested instance's transformation, with recursion detection. It supports edges from:

- `LINE`
- `POLYLINE` with **straight**, non-bulged segments (curved bulges skipped with a warning)
- simple straight-segment `POLYLINE3D`
- `3DFACE` (all face perimeter edges, including potentially hidden edges)

Each drawing contains three separated model-space projected views, labeled layers `SPTC_2D_TOP`, `SPTC_2D_FRONT`, `SPTC_2D_SIDE`, and a warning/title. It exports a CSV file named `*_projection_report.csv` that counts extracted edges, resulting LINE entities, collapsed edges, and unsupported source objects/segments.

**Important limitations:**

- **AutoCAD 3D solids, regions, surfaces, arcs/circles, Plant 3D custom support objects, pipe bodies, and proxy/ACIS geometry are NOT automatically converted to 2D edges.** They are recorded as unsupported. If no convertible linework exists, the command stops without creating a false/empty drawing.
- Projected 3DFACE perimeter edges may be *hidden* in a true 3D view. This command has **no hidden-line removal or silhouette computation**, so the result is **not** a fabrication-ready orthographic drawing.
- The geometry comes from actual supported 3D edges, **not** bounding-box guessed shapes. However no projections of unsupported solids are inferred.
- The overall projected linework envelopes are informative labels, **not associative dimension entities** or verified part dimensions.
- Projected geometry stays in source DWG units. It does not automatically set up an A3 paper-space layout or plot scale for these 2D-only DWGs.
- No steel profile recognition, weld symbols, bolt/hole/plate detailing, cut lists, structural checks, authoritative Plant 3D BOM or native project drawing registration.
- When batch processing, each selected ordinary block insert is treated as a separate support.
- Success of the GitHub compilation and math tests does not establish performance on your Plant 3D custom object types.

## Installation and test

1. Download the **SPTC-SupportDrawings-Plant3D2025-REV04** artifact from the successful GitHub Actions workflow.
2. Extract `SPTC.SupportDrawings.dll`.
3. **Restart Plant 3D 2025** if any earlier SPTC.SupportDrawings DLL was loaded.
4. Open a backup test drawing. `NETLOAD` the REV 04 DLL.
5. Test `SPTC_SUPPORT2D` using a simple 3D line-based or 3DFACE-based support. Check all three resulting model-space views and `_projection_report.csv`.
6. Select one real native Plant 3D pipe support and test `SPTC_SUPPORTINSPECT` to generate the entity audit; share that CSV and a representative DWG for the native support projection integration.

## Framework compatibility

The earlier provided actual Plant 3D 2025 application assemblies target .NET 8; the uploaded SDK archive contains multiple .NET 10-targeted assemblies. REV 04 keeps the functioning .NET 8 AutoCAD API baseline and does not upload Autodesk SDK binaries to GitHub. Native Plant support solid-edge extraction will require a matching .NET 8 Plant 3D SDK or a carefully validated host/runtime migration. See `docs/Plant3D_2025_SDK_REVIEW.md`.

## Next milestone

Obtain `*_entity_audit.csv` for a representative pipe support and a sample model (DWG or native support object), map its actual Plant 3D entity type, then implement *true solid edge/hidden-line extraction* using an appropriate API or native orthographic workflow and associative fabrication dimensions after verification.
