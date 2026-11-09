using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SPTC.ProductionIso;

public sealed class ProductionIsoCommands
{
    [CommandMethod("SPTC_PRODISO", CommandFlags.Modal)]
    public void Open()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc is not null)
            NativeIsoDialogEnhancer.Launch(doc);
    }

    [CommandMethod("SPTC_ISOCOPY", CommandFlags.Modal)]
    public void Copy()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc is not null)
            NativeIsoDialogEnhancer.CopyCompleted(doc);
    }
}
