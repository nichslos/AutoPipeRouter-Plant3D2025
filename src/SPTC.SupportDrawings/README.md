# SPTC Support Drawings — REV 01

**Purpose:** prototype of the automatic pipe-support drawing workflow shown in the supplied CAE Technology video. This is original code, not a copy of the commercial extension.

**Target:** AutoCAD Plant 3D 2025 with the **.NET 8**-based runtime, Windows x64.

## Commands

- **SPTC_SUPPORTEXPORT** — select all objects that form **one** support; choose the output \`.dwg\` filename. Produces a self-contained drawing with the selected 3D geometry and four paper-space viewports (**top, front, side, ISO**), title area, envelope sizes, and a companion \`_object_inventory.csv\`.
- **SPTC_SUPPORTBATCH** — select multiple independent **block references**, each block holding one support assembly; choose an output folder. Exports one drawing and CSV per block. Other selected loose objects are ignored in batch mode.

## Installation and first test

1. Download the completed GitHub Actions build artifact and extract **SPTC.SupportDrawings.dll**.
2. Make a backup of the Plant 3D model drawing and open a test drawing.
3. Type \`NETLOAD\`, select the DLL.
4. Type \`SPTC_SUPPORTEXPORT\`, select a small modeled support, specify output \`.dwg\`.
5. Open the exported DWG and switch to its paper-space Layout (typically Layout1). Check the four live viewports and the accompanying CSV.

## Limitations that matter

This prototype creates a **DWG with a selected 3D model and paper-space viewports**, not exploded or flattened 2D manufacturing linework. It does not infer welds, bolt details, hole diameters, cut lengths, detailed dimensions, material grades, or an authoritative fabrication BOM. Its CSV is an **object/layer inventory**, NOT a material takeoff. The overall dimensions are axis-aligned bounding envelope sizes in source DWG units.

Native Plant 3D custom/proxy entities may not always clone successfully to a standalone DWG; test your modeled support types. Source models are not altered. Generated output is not registered as a native Plant 3D Ortho project drawing. The exported DWG's paper size/plot settings may require manual A3 setup. Physical views show live 3D geometry and therefore need inspection for hidden lines / plotting. All engineering checks remain manual.

**Future revisions**: actual 2D vector projections, automatic part dimensions, template support, attribute-based BOM, group detection, and a searchable GUI supporting batch export.
