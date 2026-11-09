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

    [CommandMethod("SPTC_DWGTAGS", CommandFlags.Modal)]
    public void DwgTags()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        ActiveDwgTags.Scan(doc);
        doc.Editor.WriteMessage("\nSPTC REV08: " + ActiveDwgTags.LastDiagnostic);
    }

    [CommandMethod("SPTC_ISOSTATUS", CommandFlags.Modal)]
    public void Status()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc is not null)
            NativeIsoDialogEnhancer.Status(doc);
    }

    [CommandMethod("SPTC_ISOCOPY", CommandFlags.Modal)]
    public void Copy()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc is not null)
            NativeIsoDialogEnhancer.CopyCompleted(doc);
    }
}
