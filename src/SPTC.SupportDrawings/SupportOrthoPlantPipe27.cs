using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.27: read ONLY user-selected native Plant 3D Pipe entities from
/// source Piping PnP or their exposed Properties palette descriptors.
/// The line tag and BOP must be explicitly available; COP, TOP and
/// nominal pipe sizes never stand in for BOP. Project each long pipe's
/// clipped bounding-box intersection with the compact OrthoCube.
/// </summary>
internal static class SupportOrthoPlantPipe27
{
    internal sealed record Pipe(string Handle,string LineTag,string BopLabel,
        string LineSource,string BopSource,double X,double Y,double Z,
        string AnchorSource);

    internal sealed record Result(IReadOnlyList<Pipe> Pipes,int Candidates,
        int LinkedRows,IReadOnlyList<string> Diagnostics);

    internal static Result Read(Database db,
        IReadOnlyCollection<ObjectId> selection,BoundingBox3 cube)
    {
        var output=new List<Pipe>();
        var notes=new List<string>();
        int candidates=0,linked=0;
        object? dlm=null;
        MethodInfo? findRow=null,getProperties=null;
        try
        {
            Type app=AppDomain.CurrentDomain.GetAssemblies()
                .Select(a=>a.GetType(
                    "Autodesk.ProcessPower.PlantInstance.PlantApplication",
                    throwOnError:false))
                .FirstOrDefault(t=>t!=null)??
                throw new InvalidOperationException("PlantApplication unavailable");
            object project=app.GetProperty("CurrentProject",
                BindingFlags.Static|BindingFlags.Public)?.GetValue(null)??
                throw new InvalidOperationException("No active Plant project");
            object parts=project.GetType().GetProperty("ProjectParts")?
                .GetValue(project)??
                throw new InvalidOperationException("ProjectParts unavailable");
            object piping=parts.GetType().GetProperty("Item")?
                .GetValue(parts,new object[]{"Piping"})??
                throw new InvalidOperationException("Piping part unavailable");
            dlm=piping.GetType().GetProperty("DataLinksManager")?.GetValue(piping);
            if(dlm==null)throw new InvalidOperationException(
                "Piping DataLinksManager unavailable");
            findRow=dlm.GetType().GetMethods(BindingFlags.Instance|
                BindingFlags.Public).FirstOrDefault(m=>
            {
                var p=m.GetParameters();
                return m.Name=="FindAcPpRowId"&&p.Length==1&&
                    p[0].ParameterType==typeof(ObjectId);
            });
            getProperties=dlm.GetType().GetMethods(BindingFlags.Instance|
                BindingFlags.Public).FirstOrDefault(m=>
            {
                var p=m.GetParameters();
                return m.Name=="GetAllProperties"&&p.Length==2&&
                    p[0].ParameterType==typeof(int)&&
                    p[1].ParameterType==typeof(bool);
            });
        }
        catch(System.Exception ex)
        {
            notes.Add("PIPE_DATALINKS_UNAVAILABLE="+ex.GetType().Name+
                ":"+ex.Message);
        }

        using var tr=db.TransactionManager.StartTransaction();
        foreach(ObjectId id in selection.Distinct())
        {
            try
            {
                if(id.IsNull||!id.IsValid||id.IsErased)continue;
                if(tr.GetObject(id,OpenMode.ForRead,false) is not Entity e)
                    continue;
                string type=e.GetType().Name;
                string rx="";
                try{rx=id.ObjectClass?.Name??"";}catch{}
                string handle=id.Handle.ToString();
                if(!SupportOrthoPipeLabel27.IsNativePipeType(type)&&
                   !SupportOrthoPipeLabel27.IsNativePipeType(rx))
                {
                    notes.Add("PIPE_SKIPPED_NOT_NATIVE_PIPE="+handle+
                        " TYPE="+type+" RX="+rx);
                    continue;
                }
                candidates++;
                int row=0;
                var values=new Dictionary<string,string>(
                    StringComparer.OrdinalIgnoreCase);
                if(dlm!=null&&findRow!=null&&getProperties!=null)
                {
                    try
                    {
                        row=Convert.ToInt32(findRow.Invoke(
                            dlm,new object[]{id}));
                        if(row>0&&getProperties.Invoke(dlm,
                            new object[]{row,true}) is IEnumerable pairs)
                        {
                            linked++;
                            foreach(object? pair in pairs)
                            {
                                if(pair==null)continue;
                                string key=pair.GetType().GetProperty("Key")?
                                    .GetValue(pair)?.ToString()??"";
                                string value=pair.GetType().GetProperty("Value")?
                                    .GetValue(pair)?.ToString()??"";
                                if(!string.IsNullOrWhiteSpace(key))
                                    values[key]=value;
                            }
                        }
                    }
                    catch(System.Exception ex)
                    {
                        notes.Add("PIPE_PNP_READ_ERROR="+handle+
                            ":"+ex.GetType().Name+":"+ex.Message);
                    }
                }
                if(row<=0)
                {
                    notes.Add("PIPE_SKIPPED_NO_PNP_ROW="+handle);
                    continue;
                }
                // Property palette fields (e.g. computed Bottom of Pipe)
                // may not appear in GetAllProperties(row,true). Read ONLY
                // matching explicit property descriptors from the entity.
                try
                {
                    foreach(PropertyDescriptor prop in TypeDescriptor.GetProperties(e))
                    {
                        string normalized=SupportOrthoSupportLabel24.Normalize(
                            prop.DisplayName);
                        string internalName=SupportOrthoSupportLabel24.Normalize(
                            prop.Name);
                        bool requested=new[]{"LINENUMBERTAG","LINENUMBER",
                                "LINETAG","PIPINGLINENUMBERTAG",
                                "BOTTOMOFPIPE","BOTTOMOFPIPEELEVATION","BOP"}
                            .Any(k=>k==normalized||k==internalName);
                        if(!requested)continue;
                        string value=prop.GetValue(e)?.ToString()??"";
                        if(string.IsNullOrWhiteSpace(value))continue;
                        if(!values.ContainsKey(prop.Name)||
                           string.IsNullOrWhiteSpace(values[prop.Name]))
                            values[prop.Name]=value;
                        if(!values.ContainsKey(prop.DisplayName)||
                           string.IsNullOrWhiteSpace(values[prop.DisplayName]))
                            values[prop.DisplayName]=value;
                    }
                }
                catch(System.Exception ex)
                {
                    notes.Add("PIPE_PROPERTY_PALETTE_READ_ERROR="+handle+
                        ":"+ex.GetType().Name);
                }
                // Some exposed Plant objects implement direct public
                // Top/Bottom/LineNumberTag properties rather than descriptors.
                foreach(string propertyName in new[]{
                    "LineNumberTag","LineNumber","BottomOfPipe","BOP"})
                {
                    try
                    {
                        var prop=e.GetType().GetProperty(propertyName,
                            BindingFlags.Public|BindingFlags.Instance|
                            BindingFlags.IgnoreCase);
                        string value=prop?.GetValue(e)?.ToString()??"";
                        if(!string.IsNullOrWhiteSpace(value)&&
                           (!values.ContainsKey(propertyName)||
                            string.IsNullOrWhiteSpace(values[propertyName])))
                            values[propertyName]=value;
                    }
                    catch{}
                }

                var parsed=SupportOrthoPipeLabel27.Evaluate(values);
                if(parsed.LineSource=="LINE_NUMBER_TAG_NOT_AVAILABLE"&&
                   parsed.BopSource=="BOP_NOT_AVAILABLE")
                {
                    notes.Add("PIPE_SKIPPED_NO_LINE_OR_BOP="+handle+
                        " ROW="+row);
                    continue;
                }

                Extents3d bounds=e.GeometricExtents;
                double x0=Math.Max(bounds.MinPoint.X,cube.MinX);
                double x1=Math.Min(bounds.MaxPoint.X,cube.MaxX);
                double y0=Math.Max(bounds.MinPoint.Y,cube.MinY);
                double y1=Math.Min(bounds.MaxPoint.Y,cube.MaxY);
                double z0=Math.Max(bounds.MinPoint.Z,cube.MinZ);
                double z1=Math.Min(bounds.MaxPoint.Z,cube.MaxZ);
                if(x0>x1||y0>y1||z0>z1)
                {
                    notes.Add("PIPE_SKIPPED_NOT_IN_COMPACT_CUBE="+handle);
                    continue;
                }
                double x=(x0+x1)*.5,y=(y0+y1)*.5,z=(z0+z1)*.5;
                if(!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(z))
                {
                    notes.Add("PIPE_SKIPPED_INVALID_ANCHOR="+handle);
                    continue;
                }
                output.Add(new Pipe(handle,parsed.LineTag,parsed.BopText,
                    parsed.LineSource,parsed.BopSource,x,y,z,
                    "BOUNDS_CLIPPED_TO_NATIVE_ORTHOCUBE"));
                notes.Add("PIPE_PNP_ACCEPTED="+handle+" ROW="+row+
                    " LINE_TAG="+parsed.LineTag+
                    " LINE_SOURCE="+parsed.LineSource+
                    " "+parsed.BopText+" BOP_SOURCE="+parsed.BopSource+
                    " ANCHOR="+x+","+y+","+z);
            }
            catch(System.Exception ex)
            {
                notes.Add("PIPE_ENTITY_ERROR="+id.Handle+":"+
                    ex.GetType().Name+":"+ex.Message);
            }
        }
        tr.Commit();
        return new Result(output.OrderBy(x=>x.Handle,
            StringComparer.OrdinalIgnoreCase).ToArray(),
            candidates,linked,notes);
    }
}
