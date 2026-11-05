# REV10 Native Plant 3D 2025 Ortho API Inspection — plant3d-dll.zip

**Status:** Static API and CIL analysis only. **No new automatic Ortho creation DLL is claimed.** This technical report follows the user's request to bypass the drawing-selection dialog, size an OrthoCube from selected structural supports, and create and place project-linked views unattended.

## Uploaded assembly inventory

The user-provided `plant3d-dll.zip` contains 240 DLLs, including these previously missing managed Ortho components:

| Assembly | Assembly version | Target framework |
|---|---|---|
| `PnP3dOrthoDrawingsUI.dll` | 16.0.58.1102 | .NET 8 |
| `PnP3dOrthoProjectPart.dll` | 16.0.58.1102 | .NET 8 |
| `PnP3dOrthoUtilMgd.dll` | 16.0.58.1102 | .NET 8 |
| `PnP3dObjectsMgd.dll` | 16.0.58.1102 | .NET 8 |
| `PnPProjectManagerMgd.DLL` | 16.0.58.1102 | .NET 8 |

These have .NET 8 runtime markers and are *potentially* compatible with the existing REV09 .NET 8 plugin; verify assembly versions on the user's active Plant 3D installation before loading anything new.

**Do not redistribute or commit the Autodesk assemblies** without verifying license terms. The analysis below includes only public metadata and observed call structure.

## Relevant public methods discovered in PnP3dOrthoDrawingsUI.dll

Type `Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoView`:

```csharp
public PlantOrthoView(bool arg0, bool arg1);
public void CreateView();
public void CreateOrthoFromCube();
public void CreateOrthos(
    System.Collections.Specialized.StringCollection selectedModels,
    string strViewName,
    double scale,
    double viewportWidth,
    double viewportHeight,
    double viewportDepth,
    ViewType viewType,
    string strViewType,
    Autodesk.AutoCAD.Geometry.Vector3d viewDir,
    Autodesk.AutoCAD.Geometry.Vector3d upVector,
    Autodesk.AutoCAD.Geometry.Point3d lowerLeftPt,
    Autodesk.AutoCAD.Geometry.Point3d upperRightPt,
    Autodesk.AutoCAD.DatabaseServices.Solid3d cubeSolid);
```

Type `Autodesk.ProcessPower.PnP3dOrthoDrawingsUI.PlantOrthoCube`:

```csharp
public void InitializeCube(Autodesk.AutoCAD.DatabaseServices.Extents3d bounds);
public void ReInitializeCube(Autodesk.AutoCAD.DatabaseServices.Extents3d bounds);
public Autodesk.AutoCAD.DatabaseServices.Extents3d Extents { get; }
public Autodesk.AutoCAD.Geometry.Point3d Position { get; set; }
public double Length { get; set; }
public double Width { get; set; }
public double Height { get; set; }
public Autodesk.AutoCAD.DatabaseServices.Solid3d MakeCubeCopy();
```

There is also an Ortho project part `Autodesk.ProcessPower.P3dProjectParts.OrthoProject`, used by Plant 3D to validate Ortho drawings and maintain associated project data.

## Critical CIL call-path finding

The methods' **actual compiled call structure** was inspected from the uploaded DLL metadata and bodies (not inferred solely from method names).

`PlantOrthoView::.ctor(bool,bool)` → `InitializeProjectData(bool,bool)`

`InitializeProjectData` → private `SelectOrthoDwg(ref Document,bool)`

`SelectOrthoDwg` → constructs `frmSelectOrthoDwg` and invokes **`Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog`**. The method can also call `Editor.GetKeywords` and interacts with project document registration / checkout.

`PlantOrthoView.CreateOrthoFromCube()` → `CreateOrthos(...)`

**`PlantOrthoView.CreateOrthos(...)` → private `ProcessViewportPlacement(...)` → `Editor.Drag(Jig)`**.

Only after `ProcessViewportPlacement` does `CreateOrthos` call private `GenerateOrthos(...)`.

Therefore, **calling the public `CreateOrthos` API does not, by itself, bypass the viewport-placement prompt**, and constructing a `PlantOrthoView` without further control may trigger the drawing selection dialog. The reported behavior is consistent with Autodesk documentation for `PLANTORTHOCREATE` and community testing.

A relevant internal method is:

```csharp
// NON-PUBLIC API - NOT a stable/supported integration contract:
GenerateOrthos(
  StringCollection dwg3dFiles, string strViewName, string strViewType,
  Point3d lowerLeftPt, Point3d upperRightPt, ViewType viewType,
  Point3d ptIns, Vector3d viewDir, Vector3d upVector, ObjectId viewportId,
  double viewportWidth, double viewportHeight, double viewportDepth,
  double scale, double viewportRotation, Solid3d cubeSolid);
```

This has explicit insertion-point and viewport arguments, but it is **non-public**, and relies on initialized project/drawing/session state. Invoking it via reflection without that state can corrupt an Ortho project drawing or fail unpredictably. It must not be called blindly.

## Practical implementation decision

**Do not patch REV09 by sending keystrokes to the Ortho dialog, assuming the `CreateOrthos` public method is silent, or writing project-registration database records by hand.**

To implement the requested video-like behavior, safely establish and test the following isolated functions in a **copy of a registered Plant 3D project**:

1. **Project target**: identify the current Plant 3D project and supported create/register Ortho DWG APIs. New Ortho DWG should be given a unique name, without overwriting an existing drawing.
2. **Source and extents**: validate the selected source model is project-registered and compute bounds from actual structural support entities, expanded with explicit clearance.
3. **Native OrthoCube**: instantiate and initialize `PlantOrthoCube` from the computed extents, with explicit Top/Front/Side direction and scale.
4. **View placement**: establish a version-appropriate API path that accepts a **specified insertion point** without `Editor.Drag`. The available public `CreateOrthos` does not meet this condition. If non-public `GenerateOrthos` is considered, first verify every required session field and its lifecycle using an isolated test project and obtain approval for unsupported API reliance.
5. **Transaction/save**: validate source link, full view generation, output sheet geometry, update behavior and rollback. Never claim success purely from DLL compilation.

**Current user-facing REV09 commands remain unchanged**: `SPTC_SUPPORT2D` uses the interactive native Ortho workflow. `SPTC_ORTHOAUTOPROBE` is the read-only runtime inspection tool. No new production DLL claiming automatic dialog bypass was built as a result of this archive inspection.

## Autodesk documentation

- [Plant 3D 2025 PLANTORTHOCREATE](https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-EBBC59CB-38BB-479A-97A9-BB7606E3426C.htm)
- [Plant 3D 2025 Create Orthographic View](https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-09E67A9E-45F7-4392-BFB6-EF1ADAB1D46B.htm)
- [Autodesk Community — Create an orthographic view via C# API (viewport placement concern)](https://forums.autodesk.com/t5/autocad-plant-3d-forum/create-an-orthographic-view-via-the-c-api/m-p/13629352)
