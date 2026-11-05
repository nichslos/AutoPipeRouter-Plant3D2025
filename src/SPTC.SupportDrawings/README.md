# SPTC Support Drawings — REV 07

**Target:** AutoCAD Plant 3D 2025, Windows x64, compatible .NET 8 host installation.

## Evidence and purpose

The user tested REV06 `SPTC_SUPPORT2D` using two native steel Structure Members and uploaded `SUPPORT_2D_PROJECTION_projection_report.csv` plus the output DWG. The CSV showed:

- **192** extracted 3D segments, obtained from two `ImpCurve` objects sampled at 96 points/intervals each.
- **384** resulting 2D LINE entities across top/front/side, **192** collapsed view projections.
- **4** unsupported/skipped items: `Solid3d`, two `DBPoint` objects, and the parent `Pipe`.
- **2** approximation records, corresponding to `ImpCurve` centerline/curve sampling.

**Meaning:** REV06 created 2D linework but did not extract the actual native solid profile. The DWG is not yet an acceptable steel fabrication drawing.

## New REV07 solid geometry path

REV07 attempts the Autodesk-approved approach for native Plant 3D objects:

1. Read the source entities without modifying the source drawing.
2. Continue preserving ordinary `LINE`, straight polylines, `3DFACE`, and supported `Curve` extraction.
3. On a `Solid3d`, including transient solids produced by a non-destructive `Explode` of native `Pipe` or support objects, construct an AutoCAD **BRep** object.
4. Enumerate each actual BRep edge. Straight edges are projected using the exact edge endpoints. Curved edges are approximated with **49 sample points per edge** (not a manufacturing tolerance). Any unextractable edges are included in the diagnostic CSV.
5. Project extracted 3D edge segments into actual model-space `LINE` entities in top, front, and side views.

This is a substantial improvement over projecting a mere bounding box or sample centerlines, but **there is no hidden-line removal, no deduplication of overlapping edges, and no view-based silhouette detection**. All BRep edges, including ordinarily hidden edges, may appear. Some transient Plant 3D `Solid3d` objects may not expose BRep topology to the generic .NET API and will remain unsupported.

## Existing commands retained

- `SPTC_SUPPORTEXPORT`: existing 3D support view-sheet export.
- `SPTC_SUPPORTBATCH`: existing checked support block batch export.
- `SPTC_SUPPORTMANAGER`: existing support batch manager.
- `SPTC_SUPPORTINSPECT`: existing entity/metadata audit CSV.
- `SPTC_SUPPORT2D`: now attempts native solid BRep boundary edge extraction.
- `SPTC_SUPPORT2DBATCH`: now attempts native solid BRep boundary edge extraction per selected block.

The projection report still records geometry omissions and approximations. When no real 2D geometry is obtainable, the plugin attempts a separately named `*_3D_VIEWPORT_FALLBACK.dwg` rather than masquerading as a 2D drawing. The REV06 `*_REV07_ERROR.txt` diagnostic stack trace facility is preserved with the version name updated.

## Install and test

1. Download the GitHub Actions artifact `SPTC-SupportDrawings-Plant3D2025-REV07` after a successful Windows build.
2. Close and restart Plant 3D 2025, then load the new `SPTC.SupportDrawings.dll` with `NETLOAD`.
3. Select the same two structural steel members using `SPTC_SUPPORT2D`.
4. Compare the new `_projection_report.csv` with the REV06 report. Look for `Solid3d` BRep warnings and increased edge counts, while noting the projection of hidden edges.
5. Open the output DWG in model space and visually check whether the actual angle steel profile appears in all three views.
6. If BRep extraction fails or produces incomplete geometry, provide the new report and `*_REV07_ERROR.txt` (if created) so we can validate native Plant 3D geometry access.

## Not yet supported

This is not an automatic fabrication drafting system equivalent to the video. No native Ortho project registration, hidden-line filtering, assembly hierarchy, section callouts, dimension chains, hole tables, material cutting lengths, weld symbols, or verified BOM is generated. Physical material quantities must not be inferred from these sampled line entities. The initial Plant 3D SDK ZIP uploaded for reference includes several .NET 10 assemblies; do not link them into this working .NET 8 build.

**Engineering approval requires comparing the real model, a verified drawing, and precise dimensions — successful compilation is not sufficient.**

## Autodesk references

- [Autodesk Developer Blog — Extracting the raw Geometry from Plant3d Objects](https://blog.autodesk.io/extracting-the-raw-geometry-from-plant3d-objects/)
- [Autodesk — BRep Edge Class](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_BoundaryRepresentation_Edge.html)
- [Autodesk — Curve3d.GetSamplePoints](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_Geometry_Curve3d_GetSamplePoints_double_double_double.html)
