# SPTC Support Drawings — REV 02

**Target:** AutoCAD Plant 3D 2025 with a **.NET 8** runtime, Windows x64.

This is original prototype code for the kind of automated support drawing export shown in the example video. It does not reproduce or include commercial third-party source code.

## Commands

| Command | Purpose |
| --- | --- |
| \`SPTC_SUPPORTEXPORT\` | Select the objects that form one support, choose an output DWG filename, and export. |
| \`SPTC_SUPPORTBATCH\` | Select support block inserts, review a checked list, choose a destination and file prefix, export selected supports as separate DWGs. |
| \`SPTC_SUPPORTMANAGER\` | Alias to open the same batch selection and export interface. |

## REV 02 enhancements

- A3-style paper-space drawing with four **live 3D model viewports**: TOP, FRONT, SIDE, and ISO.
- Dimension-style **overall envelope callouts** for X, Y, and Z on orthographic views. Callouts are paper-space graphics using the support's world-axis-aligned bounding box; **not associative fabrication dimensions**. They are not guaranteed to align precisely with visible edges in viewport projections.
- Compact drawing-side **object type / layer / count schedule**. All types and counts are included in \`*_object_inventory.csv\`. This schedule is **not a materials BOM**.
- An optional \`*_attribute_material_register.csv\`, only when a selected block contains one of the recognized literal attributes \`PART_NO\`, \`PARTNO\`, \`ITEM_NO\`, \`ITEM\`, \`DESCRIPTION\`, \`DESC\`, \`MATERIAL\`, \`MAT\`, \`MATERIAL_GRADE\`, \`GRADE\`, \`LENGTH\`, \`CUT_LENGTH\`, \`QTY\`, \`QUANTITY\`. All entries come directly from block attributes. Missing data is left blank; no material or quantities are inferred. This is **not** a calculated cut list.
- Batch dialog with Select All / Clear, selection review, output-folder picker, and filename prefix.

## Install

1. Open the GitHub Actions successful run for **Build SPTC Support Drawings DLL**; download the REV 02 build artifact.
2. Extract \`SPTC.SupportDrawings.dll\`.
3. Make a backup and open a **test** drawing in Plant 3D 2025.
4. Enter \`NETLOAD\` and select the extracted DLL. If you previously loaded REV 01 in the same Plant 3D process, **restart Plant 3D first** to avoid older assembly/command registration.
5. Use \`SPTC_SUPPORTEXPORT\` or \`SPTC_SUPPORTBATCH\` / \`SPTC_SUPPORTMANAGER\`.
6. Open the exported DWG, switch to its paper-space layout, and check the four viewports, annotations, schedule, and CSV(s).

## Critical engineering limitations

- The output DWG still contains **3D geometry shown through viewports**, not flattened 2D linework.
- Overall callouts are non-associative, envelope-based *world-axis* sizes, not dimension entities snapped to actual projected features. View arrows, dimensions, origin and scale must be checked.
- No automatic cut lengths, hole dimensions, welding symbols, load or structural checks, or validated fabrication BOM.
- Native/custom Plant 3D/proxy objects may not WBLOCK cleanly; check exported views and object visibility. Source drawing is not modified.
- The sheet is an A3-shaped title block in paper space; plot configuration/ISO A3 device setup may still be required.
- Batch mode treats each selected **BLOCK INSERT** as one support. It does not automatically discover unblocked support assemblies.
- No native Plant 3D project drawing registration or hidden-line extraction is provided.
- **Build success is not proof of runtime correctness or engineering accuracy.** Test in your installed Plant 3D 2025 before production use.

## Recommended REV 03

Feature detection on actual support components, dimensional associativity / 2D projected linework, configurable DWG template, fuller attribute-based material takeoff, and a dimension review dialog.
