# SPTC Support Drawings — REV 03

**Target:** AutoCAD Plant 3D 2025 with the **.NET 8** runtime, Windows x64.

REV 03 adds **read-only support entity auditing** and a native-class identification *candidate* report while keeping all REV 02 drawing export features. This is an original development prototype for the workflow shown in the example video, **not** a copy of the commercial tool.

## Commands

| Command | Operation |
|---|---|
| `SPTC_SUPPORTEXPORT` | Select one support assembly, choose the DWG output, export support model, four model viewports, envelope annotations and CSV sidecars. |
| `SPTC_SUPPORTBATCH` | Export selected support **block references** through the selection/filter dialog (one DWG per selected block). |
| `SPTC_SUPPORTMANAGER` | Alias to open the batch review/export dialog. |
| **`SPTC_SUPPORTINSPECT`** | **New:** Select support objects or support block inserts and save a detailed, read-only `*.csv` object-class audit. |

### New REV 03 audit fields

The inspection report saves: selected entity handle, source block instance handle when relevant, managed type, underlying RX object class, DXF name, CAD layer, block name, geometry scope, entity classification candidate, selected-object bounding extents when available, explicit block attributes, and the source DWG drawing units.

- **SELECTED** rows refer to the actual selected objects in the source drawing.
- **BLOCK_DEFINITION_MEMBER** rows reflect **one level of nested block definition entities** for a selected block, with no instance-transformed extents; they **do not** represent a full flattened instance tree.
- Class labels such as `PLANT_STRUCTURE_MEMBER_CANDIDATE`, `PLANT_SUPPORT_CANDIDATE`, and `SUPPORT_NAMING_CANDIDATE` are string-based **hints**, not a validated link to Plant 3D PnP support identity.
- No fabrication material or native Plant 3D database values are inferred from class or layer naming.
- The command does **not** change the source DWG.

Single and batch exports automatically attempt to write an additional `*_entity_audit.csv` next to each DWG (in addition to REV 02 `*_object_inventory.csv` and the optional `*_attribute_material_register.csv`).

## Installation

1. Download **SPTC-SupportDrawings-Plant3D2025-REV03** from the successful GitHub Actions build and extract `SPTC.SupportDrawings.dll`.
2. **Restart** Plant 3D if REV 01/REV 02 was previously loaded (a .NET assembly with the same identity cannot reliably be replaced in an active application session).
3. Open a backup Plant 3D 2025 test drawing.
4. Type `NETLOAD`, select the REV 03 DLL.
5. Test `SPTC_SUPPORTINSPECT` with one native pipe support, one structural member, and one regular AutoCAD block, then examine the CSV. Test the existing export commands and inspect the generated DWG + CSV outputs.

### .NET/SDK compatibility

The separate Autodesk SDK ZIP uploaded for reference contained several .NET 10-targeted managed assemblies. The working REV 02 application assemblies and plugin are **.NET 8**, so REV 03 **does not** reference incompatible .NET 10 SDK binaries or redistribute Autodesk DLLs. It uses the AutoCAD 2025 .NET API package already employed by REV 02.

Actual native Plant 3D support ownership, support-to-pipe associations, part properties and authoritative BOM retrieval require the matching **.NET 8 Plant 3D managed assemblies** from the installed application and real-model validation. Until those are available, REV 03 is an API-discovery/diagnostic step.

## Engineering limitations retained from REV 02

- The drawing shows the selected **3D model through four paper-space viewports**, not projected 2D manufacturing geometry.
- Overall annotations indicate **axis-aligned envelope extents**, not associative fabrication dimensions or precise view-part measurement chains.
- Inventory shows entity types and layers; attribute register records explicitly stored values only. Neither is a validated material BOM.
- Batch mode only treats each selected block insert as one support, not arbitrary loose-member assemblies.
- Some native Plant 3D proxy/custom objects may fail to WBLOCK or expose geometric extents.
- A successful cloud build cannot confirm in-application operation. Verify on actual supports and keep REV 02 available for rollback.

## Next engineering milestone

Use real entity-audit results from REV 03 to map support types to the compatible Plant 3D API, then implement true projected 2D linework, dimension features, and checked fabrication schedules in a later revision.
