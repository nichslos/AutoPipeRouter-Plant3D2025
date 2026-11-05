# SPTC Support Drawings — REV 08

**Host:** AutoCAD Plant 3D 2025, 64-bit, compatible .NET 8 installation.

## Change requested

The user wants the workflow shown in the example automatic export video: select support members in the live Plant 3D 3D model, **generate a project-native orthographic drawing DWG**, and eventually batch-create fully detailed support sheets. REV07's standalone projected-linework DWG was **not** a native Plant 3D Ortho and did not maintain project ortho view dependencies.

REV08 changes **`SPTC_SUPPORT2D` into a supported, INTERACTIVE handoff** to the native Plant 3D `PLANTORTHOCREATE` command. This uses the Plant 3D model and its OrthoCube/Ortho drawing generation engine, not our custom projection DWG. The user selects support members for reference, chooses a new or existing project Ortho DWG, positions/resizes the OrthoCube around them, selects a view/scale, confirms Create, and places the view. Plant 3D creates the resulting native orthographic view.

**Important: It is a user-assisted handoff, NOT automatic one-click batch ortho drawing generation.** The selected members define guidance coordinates that are printed in the command line but the native OrthoCube is not automatically filtered to only those objects. Geometry of nearby pipes/supports may also appear if included in the OrthoCube region. No command-line scripting of undocumented interactive Ortho dialogs has been attempted. Full video-like unattended batch requires verified, matching **Plant 3D Ortho SDK APIs** and integration testing in the exact installed version.

## Commands

| Command | REV08 behavior |
| --- | --- |
| **`SPTC_SUPPORT2D`** | **NEW:** select steel/support objects in the *project source model*, print their combined extents, then launch **`PLANTORTHOCREATE`** interactively after the managed command returns. |
| `SPTC_SUPPORT2DLINEWORK` | Legacy REV07 line/BRep projection into a **standalone, NON-native** DWG; available as an explicitly separate diagnostic tool. |
| `SPTC_SUPPORT2DBATCH` | Legacy standalone 2D linework block-batch export; **NOT** native Plant 3D Ortho batch. |
| `SPTC_SUPPORTEXPORT` | Existing 3D model viewport sheet plus reports. |
| `SPTC_SUPPORTBATCH` / `SPTC_SUPPORTMANAGER` | Existing block batch viewport sheet export. |
| `SPTC_SUPPORTINSPECT` | Existing CAD/native class and metadata audit CSV. |

## How to test REV08

1. Save a backup of your project and confirm the source DWG is registered under **Plant 3D Drawings** in Project Manager. A standalone drawing outside the project is not an Ortho source.
2. Restart Plant 3D 2025 if REV07 was loaded (same plugin assembly identity). Use `NETLOAD` to load REV08.
3. From the **source 3D model drawing**, type `SPTC_SUPPORT2D`.
4. Select the two Structure Member angle-steel support objects and press Enter.
5. REV08 reports their combined bounding coordinates and queues native `PLANTORTHOCREATE`.
6. In **Select Orthographic Drawing**, choose **Create New** or an existing **Orthographic DWG in the current project**.
7. In Plant 3D **Ortho Editor**, use the OrthoCube to frame the support (selection alone does not exclude nearby objects), select **Front** or **Top** or **Side** and scale, press **Create**.
8. Place the native projected view onto the drawing sheet (paper-space viewport insertion point). Generate other views using Plant 3D's Ortho View tools. The output now lives under the Project Manager **Orthographic DWG** tab.
9. Verify exact source geometry and hidden lines in the native Ortho; add dimensions, notes, and BOM through Plant 3D's tools as needed.

## Compatibility and limitations

- This build still uses the same successfully compiling AutoCAD 2025 .NET 8 managed API; we do **not** redistribute Autodesk SDK DLLs.
- `PLANTORTHOCREATE` is a **Plant 3D-specific** command and requires its loaded toolset, a compatible project-registered source model drawing and Plant 3D's Ortho editor environment. Generic AutoCAD alone is not enough.
- The deferred call uses `Document.SendStringToExecute("_.PLANTORTHOCREATE ", true, false, false)` so it launches after `SPTC_SUPPORT2D` finishes. The OrthoCube creation dialog and placement must be completed interactively. Runtime verification in the user's exact Plant 3D setup is still required.
- This revision does **not** programmatically create project Ortho drawing records, set OrthoCube coordinates, place views, trigger hidden-line generation unattended, automatically generate a material BOM, or batch-create fabricated support drawings. Do not claim it duplicates the commercial extension.
- `SPTC_SUPPORT2DLINEWORK` and `SPTC_SUPPORT2DBATCH` remain experimental standalone extraction tools and are not project Ortho DWGs.

## Autodesk sources

- [AutoCAD Plant 3D 2025: To Create an Orthographic View](https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-09E67A9E-45F7-4392-BFB6-EF1ADAB1D46B.htm)
- [AutoCAD Plant 3D 2025: PLANTORTHOCREATE](https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-EBBC59CB-38BB-479A-97A9-BB7606E3426C.htm)
- [Autodesk: Need Plant 3D SDK for Ortho API updates](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Is-it-possible-to-update-AutoCAD-Plant-3D-ortho-views-by-the-API-of-AutoCAD-or-ACC-API.html)
