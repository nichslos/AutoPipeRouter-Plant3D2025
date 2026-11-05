using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace SPTC.AutoRoute;

/// <summary>
/// REV 01 — basic two-point orthogonal route.
/// Preview is an ordinary AutoCAD 3D polyline; conversion uses Plant 3D's
/// native PLANTCONVERTLINETOPIPE command and requires live integration testing.
/// </summary>
public sealed class AutoRouteCommands : IExtensionApplication
{
    private const string PreviewLayer = "SPTC_ROUTE_CENTERLINE";

    public void Initialize()
    {
        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\nSPTC AutoRoute REV 01 loaded. Commands: SPTC_ROUTEPREVIEW, SPTC_AUTOROUTE.");
    }

    public void Terminate() { }

    [CommandMethod("SPTC_ROUTEPREVIEW", CommandFlags.Modal)]
    public void RoutePreview() => CreateRoute(convertToPlantPipe: false);

    [CommandMethod("SPTC_AUTOROUTE", CommandFlags.Modal)]
    public void RouteAndConvert() => CreateRoute(convertToPlantPipe: true);

    private static void CreateRoute(bool convertToPlantPipe)
    {
        Document? document = Application.DocumentManager.MdiActiveDocument;
        if (document == null) return;
        Editor editor = document.Editor;

        try
        {
            var startResult = editor.GetPoint("\nSelect route START point (use object snaps for ports): ");
            if (startResult.Status != PromptStatus.OK) return;

            var endOptions = new PromptPointOptions("\nSelect route END point: ")
            {
                BasePoint = startResult.Value,
                UseBasePoint = true
            };
            var endResult = editor.GetPoint(endOptions);
            if (endResult.Status != PromptStatus.OK) return;

            var options = new PromptKeywordOptions(
                "\nRoute axis order [XYZ/XZY/YXZ/YZX/ZXY/ZYX] <XYZ>: ")
            {
                AllowNone = true
            };
            foreach (string axisOrder in new[] { "XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX" })
                options.Keywords.Add(axisOrder);

            var choice = editor.GetKeywords(options);
            if (choice.Status == PromptStatus.Cancel) return;
            if (choice.Status != PromptStatus.OK && choice.Status != PromptStatus.None) return;
            string axis = choice.Status == PromptStatus.None ? "XYZ" : choice.StringResult;

            RoutePoint start = FromCadPoint(startResult.Value);
            RoutePoint end = FromCadPoint(endResult.Value);
            IReadOnlyList<RoutePoint> route = RoutePlanner.Build(start, end, axis);

            ObjectId centerlineId = AddCenterline(document.Database, route);
            editor.WriteMessage($"\nCreated 3D centerline: {route.Count} vertices, {route.Count - 1} legs, order {axis}.");

            if (!convertToPlantPipe)
            {
                editor.WriteMessage("\nPREVIEW ONLY: standard AutoCAD 3D polyline; no Plant pipe created.");
                editor.WriteMessage("\nCheck the path before running SPTC_AUTOROUTE.");
                return;
            }

            try
            {
                SelectionSet selectedCenterline = SelectionSet.FromObjectIds(new[] { centerlineId });
                editor.Command("_.PLANTCONVERTLINETOPIPE", selectedCenterline, "");
                editor.WriteMessage("\nPlant conversion command returned. Verify created pipe, fittings, spec, and connections.");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nAutomatic conversion could not complete: {ex.Message}");
                editor.WriteMessage("\nThe 3D centerline has been retained. Run PLANTCONVERTLINETOPIPE manually, select it, and press Enter.");
                editor.WriteMessage("\nCheck current Plant 3D spec, pipe size, and fitting availability.");
            }
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage($"\nSPTC AutoRoute stopped: {ex.Message}");
        }
    }

    private static RoutePoint FromCadPoint(Point3d p) => new(p.X, p.Y, p.Z);
    private static Point3d ToCadPoint(RoutePoint p) => new(p.X, p.Y, p.Z);

    private static ObjectId AddCenterline(Database database, IReadOnlyList<RoutePoint> route)
    {
        using Transaction transaction = database.TransactionManager.StartTransaction();

        var layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        ObjectId layerId;
        if (layers.Has(PreviewLayer))
        {
            layerId = layers[PreviewLayer];
        }
        else
        {
            layers.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = PreviewLayer,
                Color = Color.FromColorIndex(ColorMethod.ByAci, 4)
            };
            layerId = layers.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
        }

        var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var vertices = new Point3dCollection(route.Select(ToCadPoint).ToArray());
        var polyline = new Polyline3d(Poly3dType.SimplePoly, vertices, false)
        {
            LayerId = layerId
        };
        ObjectId polylineId = modelSpace.AppendEntity(polyline);
        transaction.AddNewlyCreatedDBObject(polyline, true);
        transaction.Commit();
        return polylineId;
    }
}
