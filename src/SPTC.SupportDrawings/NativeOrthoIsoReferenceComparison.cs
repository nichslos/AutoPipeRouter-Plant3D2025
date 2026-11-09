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
using WinForms = System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.6: read-only native reference capture and read-only comparison with
/// REV22.5-generated isometric viewports. Does not modify drawing/project or
/// rely on undisclosed Autodesk native API calls.
/// </summary>
public sealed class NativeOrthoIsoReferenceComparison
{
    private static readonly string[] Names={"NW","NE","SW","SE"};
    private const string Header="SPTC_REV22_6_NATIVE_ISO_REFERENCE_V1";

    [CommandMethod("SPTC_ORTHO22_6_CAPTURE_NATIVE_ISO",CommandFlags.Modal)]
    public void Capture()
    {
        Document? doc=AcadApp.DocumentManager.MdiActiveDocument;
        if(doc==null)return;
        Editor ed=doc.Editor;
        if(!TrySavedDwg(doc,ed,out string dwg))return;
        ed.WriteMessage("\nREV22.6: READ ONLY. Capture FOUR isometric viewports "+
            "created MANUALLY through Plant 3D's native Ortho Editor. "+
            "The command does not create or change views.");
        if(!TrySelectFour(doc,out var views))return;
        string report=Path.Combine(Path.GetDirectoryName(dwg)!,
            Path.GetFileNameWithoutExtension(dwg)+
            "_REV22_6_NATIVE_REFERENCE_"+DateTime.Now.ToString(
                "yyyyMMdd_HHmmss",CultureInfo.InvariantCulture)+".txt");
        var lines=new List<string>{
            Header,
            "REFERENCE_DRAWING="+dwg,
            "ASSUMPTION=Selected NW NE SW SE were created using native Ortho Editor presets",
            "NOTICE=View labels were supplied by the operator, not detected from metadata",
            "NOTICE=This is viewport camera and roll data, not a geometric linework comparison"
        };
        foreach(var v in views)
            lines.Add(Serialize(v));
        try
        {
            File.WriteAllLines(report,lines,new UTF8Encoding(false));
            ed.WriteMessage("\nREV22.6: Native reference saved: "+report);
        }
        catch(System.Exception ex)
        {
            ed.WriteMessage("\nREV22.6: Could not write reference TXT: "+ex.Message);
        }
    }

    [CommandMethod("SPTC_ORTHO22_6_COMPARE_NATIVE_ISO",CommandFlags.Modal)]
    public void Compare()
    {
        Document? doc=AcadApp.DocumentManager.MdiActiveDocument;
        if(doc==null)return;
        Editor ed=doc.Editor;
        if(!TrySavedDwg(doc,ed,out string dwg))return;
        using var pick=new WinForms.OpenFileDialog
        {
            Title="REV22.6 - Select CAPTURED NATIVE ISO reference TXT",
            Filter="REV22.6 native reference TXT (*.txt)|*.txt",
            InitialDirectory=Path.GetDirectoryName(dwg)!,
            CheckFileExists=true,RestoreDirectory=true
        };
        if(pick.ShowDialog()!=WinForms.DialogResult.OK)return;
        IReadOnlyDictionary<string,NativeOrthoIsoReferenceMath.Pose> native;
        try{ native=ParseNativeReference(pick.FileName); }
        catch(System.Exception ex)
        {
            ed.WriteMessage("\nREV22.6: Invalid reference file. "+ex.Message);
            return;
        }
        ed.WriteMessage("\nREV22.6: READ ONLY. Select the FOUR plugin-generated "+
            "NW, NE, SW, SE viewport BORDERS, in order.");
        if(!TrySelectFour(doc,out var actual))return;

        var text=new StringBuilder(3000);
        text.AppendLine("SPTC_REV22_6_NATIVE_REFERENCE_COMPARISON");
        text.AppendLine("REFERENCE_FILE="+Path.GetFullPath(pick.FileName));
        text.AppendLine("GENERATED_DRAWING="+dwg);
        text.AppendLine("READ_ONLY_NO_DRAWING_OR_PROJECT_MUTATIONS=True");
        text.AppendLine("COMPARE=NORMALIZED_VIEW_DIRECTION_AND_MODULO_360_TWIST");
        text.AppendLine("CAMERA_TOLERANCE_DEG=0.1");
        text.AppendLine("ROLL_TOLERANCE_DEG=0.5");
        text.AppendLine("NOTE=Scale mismatch is informational, not an orientation failure.");
        text.AppendLine("NOTE=This test does not verify native IDwgViewDef.ViewType or output linework.");
        bool all=true;
        foreach(var v in actual)
        {
            var reference=native[v.Name];
            var c=NativeOrthoIsoReferenceMath.Compare(reference,v);
            text.AppendLine("VIEW="+v.Name);
            text.AppendLine("NATIVE_HANDLE="+reference.Handle);
            text.AppendLine("GENERATED_HANDLE="+v.Handle);
            text.AppendLine("NATIVE_DIRECTION="+Fmt(reference.Dx)+","+
                Fmt(reference.Dy)+","+Fmt(reference.Dz));
            text.AppendLine("GENERATED_DIRECTION="+Fmt(v.Dx)+","+
                Fmt(v.Dy)+","+Fmt(v.Dz));
            text.AppendLine("CAMERA_SEPARATION_DEGREES="+Fmt(c.CameraSeparationDegrees));
            text.AppendLine("CAMERA_MATCH="+c.CameraMatches);
            text.AppendLine("NATIVE_TWIST_DEGREES="+Fmt(
                reference.TwistRad*180/Math.PI));
            text.AppendLine("GENERATED_TWIST_DEGREES="+Fmt(v.TwistRad*180/Math.PI));
            text.AppendLine("ROLL_DIFFERENCE_DEGREES="+Fmt(c.RollDifferenceDegrees));
            text.AppendLine("ROLL_MATCH="+c.RollMatches);
            text.AppendLine("NATIVE_CUSTOM_SCALE="+Fmt(reference.CustomScale));
            text.AppendLine("GENERATED_CUSTOM_SCALE="+Fmt(v.CustomScale));
            text.AppendLine("SCALE_MATCH="+c.ScaleMatches);
            text.AppendLine("VIEWPORT_CAMERA_AND_ROLL_MATCH="+
                (c.OrientationMatches?"PASS":"FAIL"));
            all &=c.OrientationMatches;
        }
        text.AppendLine("ALL_FOUR_CAMERA_AND_ROLL_MATCH="+(all?"PASS":"FAIL"));
        text.AppendLine("VISUAL_LINEWORK_AND_NATIVE_PRESET_CHECK_REQUIRED=True");

        string report=Path.Combine(Path.GetDirectoryName(dwg)!,
            Path.GetFileNameWithoutExtension(dwg)+
            "_REV22_6_COMPARE_"+DateTime.Now.ToString(
                "yyyyMMdd_HHmmss",CultureInfo.InvariantCulture)+".txt");
        try
        {
            File.WriteAllText(report,text.ToString(),new UTF8Encoding(false));
            ed.WriteMessage("\nREV22.6: Comparison "+(all?"CAMERA+ROLL PASS":"MISMATCH")+
                ". See "+report);
        }
        catch(System.Exception ex)
        {
            ed.WriteMessage("\nREV22.6: Could not write comparison TXT: "+ex.Message);
            ed.WriteMessage("\n"+text.ToString());
        }
    }

    private static bool TrySavedDwg(Document doc,Editor ed,out string dwg)
    {
        dwg=doc.Database.Filename??"";
        if(string.IsNullOrWhiteSpace(dwg)||!File.Exists(dwg))
        {
            ed.WriteMessage("\nREV22.6: Open and save a DWG first.");
            return false;
        }
        dwg=Path.GetFullPath(dwg);
        if(doc.Database.TileMode)
        {
            ed.WriteMessage("\nREV22.6: Switch to the registered Ortho PAPER layout first.");
            return false;
        }
        return true;
    }

    private static bool TrySelectFour(Document doc,
        out List<NativeOrthoIsoReferenceMath.Pose> views)
    {
        views=new List<NativeOrthoIsoReferenceMath.Pose>(4);
        var used=new HashSet<ObjectId>();
        foreach(string name in Names)
        {
            var opts=new PromptEntityOptions(
                "\nREV22.6: select "+name+" VIEWPORT BORDER (Esc to stop): ");
            opts.AddAllowedClass(typeof(Viewport),false);
            opts.SetRejectMessage("\nSelect a paper-space Viewport border, not steel linework.");
            PromptEntityResult p=doc.Editor.GetEntity(opts);
            if(p.Status!=PromptStatus.OK)return false;
            if(!used.Add(p.ObjectId))
            {
                doc.Editor.WriteMessage("\nREV22.6: Duplicate viewport selected.");
                return false;
            }
            try
            {
                using Transaction tr=doc.Database.TransactionManager.StartTransaction();
                if(tr.GetObject(p.ObjectId,OpenMode.ForRead) is not Viewport vp)
                    throw new InvalidOperationException("Selected object is not a viewport.");
                Vector3d d=vp.ViewDirection;
                views.Add(new NativeOrthoIsoReferenceMath.Pose(
                    name,vp.Handle.ToString(),d.X,d.Y,d.Z,
                    vp.TwistAngle,vp.CustomScale));
            }
            catch(System.Exception ex)
            {
                doc.Editor.WriteMessage("\nREV22.6: Read-only viewport audit failed: "+ex.Message);
                return false;
            }
        }
        return true;
    }

    private static string Serialize(NativeOrthoIsoReferenceMath.Pose v) =>
        "VIEW|"+v.Name+"|"+v.Handle+"|"+
        Fmt(v.Dx)+"|"+Fmt(v.Dy)+"|"+Fmt(v.Dz)+"|"+
        Fmt(v.TwistRad)+"|"+Fmt(v.CustomScale);

    private static IReadOnlyDictionary<string,NativeOrthoIsoReferenceMath.Pose>
        ParseNativeReference(string path)
    {
        string[] lines=File.ReadAllLines(path,Encoding.UTF8);
        if(lines.Length<5||!string.Equals(lines[0],Header,StringComparison.Ordinal))
            throw new InvalidDataException("Missing REV22.6 reference header.");
        var result=new Dictionary<string,NativeOrthoIsoReferenceMath.Pose>(
            StringComparer.Ordinal);
        foreach(string line in lines.Where(x=>x.StartsWith("VIEW|",StringComparison.Ordinal)))
        {
            string[] p=line.Split('|');
            if(p.Length!=8||!Names.Contains(p[1],StringComparer.Ordinal)||
                string.IsNullOrWhiteSpace(p[2])||result.ContainsKey(p[1]))
                throw new InvalidDataException("Unexpected view name/format or duplicate.");
            double Num(int i)=>double.Parse(p[i],NumberStyles.Float,
                CultureInfo.InvariantCulture);
            var v=new NativeOrthoIsoReferenceMath.Pose(
                p[1],p[2],Num(3),Num(4),Num(5),Num(6),Num(7));
            // Validate all directional and rotation fields now.
            NativeOrthoIsoReferenceMath.Compare(v,v);
            result.Add(v.Name,v);
        }
        if(result.Count!=4||Names.Any(x=>!result.ContainsKey(x)))
            throw new InvalidDataException("Reference must contain exactly NW NE SW SE.");
        return result;
    }

    private static string Fmt(double x)=>
        x.ToString("R",CultureInfo.InvariantCulture);
}
