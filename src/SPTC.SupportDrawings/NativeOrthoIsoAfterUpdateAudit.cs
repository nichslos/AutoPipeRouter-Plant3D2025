using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.4: READ-ONLY on-demand iso viewport audit after native Edit View or
/// Update View. Does not create ortho views, set camera angles, or modify
/// project database. Run twice to compare before/after regeneration.
/// </summary>
public sealed class NativeOrthoIsoAfterUpdateAudit
{
    [CommandMethod("SPTC_ORTHO22_4_ISO_AUDIT", CommandFlags.Modal)]
    public void Run()
    {
        Document? doc=AcadApp.DocumentManager.MdiActiveDocument;
        if(doc==null)return;
        Editor ed=doc.Editor;
        string file=doc.Database.Filename ?? "";
        if(string.IsNullOrWhiteSpace(file)||!File.Exists(file))
        {
            ed.WriteMessage("\nREV22.4: Open and save the native Ortho DWG first.");
            return;
        }
        string directory=Path.GetDirectoryName(Path.GetFullPath(file))!;
        string report=Path.Combine(directory,Path.GetFileNameWithoutExtension(file)+
            "_REV22_4_ISO_AUDIT_"+DateTime.Now.ToString(
                "yyyyMMdd_HHmmss",CultureInfo.InvariantCulture)+".txt");
        var log=new StringBuilder(4000);
        void Write(string message)=>log.AppendLine(message);
        Write("SPTC_REV22_4_READ_ONLY_ISO_VIEW_AUDIT");
        Write("DRAWING="+Path.GetFullPath(file));
        Write("AUDIT_UTC="+DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        Write("READ_ONLY_NO_PROJECT_OR_DWG_MUTATIONS=True");
        Write("NOTE=Viewport labels are USER SELECTED, not inferred from screen position.");
        bool complete=true;
        var used=new HashSet<ObjectId>();
        foreach(string name in new[]{"NW","NE","SW","SE"})
        {
            var opts=new PromptEntityOptions(
                "\nREV22.4: select "+name+" Ortho VIEWPORT BORDER (Esc to stop): ");
            opts.SetRejectMessage("\nClick the actual paper-space viewport border, not model linework.");
            opts.AddAllowedClass(typeof(Viewport),false);
            PromptEntityResult picked=ed.GetEntity(opts);
            if(picked.Status!=PromptStatus.OK)
            {
                Write("AUDIT_"+name+"_SELECTION="+picked.Status);
                complete=false;
                break;
            }
            if(!used.Add(picked.ObjectId))
            {
                ed.WriteMessage("\nREV22.4: You selected the same viewport twice; audit stopped.");
                Write("AUDIT_"+name+"_ERROR=DUPLICATE_VIEWPORT_SELECTION");
                complete=false;
                break;
            }
            try
            {
                using Transaction tr=doc.Database.TransactionManager.StartTransaction();
                if(tr.GetObject(picked.ObjectId,OpenMode.ForRead) is not Viewport vp)
                    throw new InvalidOperationException("Selected entity is not a viewport.");
                Vector3d actual=vp.ViewDirection;
                var expected=name switch
                {
                    "NW" => new Vector3d(-1,1,1),
                    "NE" => new Vector3d(1,1,1),
                    "SW" => new Vector3d(-1,-1,1),
                    _ => new Vector3d(1,-1,1)
                };
                bool match=NativeOrthoIsoViewType.MatchesCamera(name,
                    actual.X,actual.Y,actual.Z,
                    expected.X,expected.Y,expected.Z);
                double twistDegrees=vp.TwistAngle*180.0/Math.PI;
                Write("VIEW="+name);
                Write("HANDLE="+vp.Handle);
                Write("VIEW_DIRECTION="+Fmt(actual.X)+","+Fmt(actual.Y)+","+Fmt(actual.Z));
                Write("EXPECTED_WCS_ISO_DIRECTION="+Fmt(expected.X)+","+
                    Fmt(expected.Y)+","+Fmt(expected.Z));
                Write("CAMERA_WCS_ISO_MATCH="+match);
                Write("VIEW_TWIST_DEG="+Fmt(twistDegrees));
                Write("VIEW_TARGET="+Fmt(vp.ViewTarget.X)+","+
                    Fmt(vp.ViewTarget.Y)+","+Fmt(vp.ViewTarget.Z));
                Write("VIEW_CENTER="+Fmt(vp.ViewCenter.X)+","+Fmt(vp.ViewCenter.Y));
                Write("PAPER_CENTER="+Fmt(vp.CenterPoint.X)+","+
                    Fmt(vp.CenterPoint.Y));
                Write("PAPER_FRAME="+Fmt(vp.Width)+","+Fmt(vp.Height));
                Write("CUSTOM_SCALE="+Fmt(vp.CustomScale));
                Write("VIEW_HEIGHT="+Fmt(vp.ViewHeight));
                Write("IS_ON="+vp.On);
                Write("CAMERA_MATCH_DIAGNOSTIC_ONLY=TRUE");
                tr.Commit();
            }
            catch(System.Exception ex)
            {
                Write("AUDIT_"+name+"_ERROR="+ex);
                complete=false;
                break;
            }
        }
        Write("AUDIT_ALL_FOUR_SELECTED="+complete);
        Write("EDIT_VIEW_NATIVE_PRESET_MUST_BE_VERIFIED_IN_UI=TRUE");
        try
        {
            File.WriteAllText(report,log.ToString(),new UTF8Encoding(false));
            ed.WriteMessage("\nREV22.4: Read-only isometric audit saved to: "+report);
        }
        catch(System.Exception ex)
        {
            ed.WriteMessage("\nREV22.4: Could not save audit file: "+ex.Message);
            ed.WriteMessage("\n"+log.ToString());
        }
    }

    private static string Fmt(double n)=>
        n.ToString("0.######",CultureInfo.InvariantCulture);
}
