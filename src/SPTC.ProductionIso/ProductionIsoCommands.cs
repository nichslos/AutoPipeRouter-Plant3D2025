using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SPTC.ProductionIso;

// Separate command and project. Does not modify existing SPTC_SUPPORT2D commands.
public sealed class ProductionIsoCommands
{
    [CommandMethod("SPTC_PRODISO", CommandFlags.Modal)]
    public void Open()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        AcadApp.ShowModelessDialog(new ProductionIsoForm(doc));
    }
}
