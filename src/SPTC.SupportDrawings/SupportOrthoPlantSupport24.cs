using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.24: adds real Plant 3D Piping Support objects to the source
/// annotation list, without changing previous Structure Member extraction.
/// Requires a true PnP Support row/class or a supported native entity type.
/// Never labels generic 3D solids or pipe objects as Pipe Supports.
/// </summary>
internal static class SupportOrthoPlantSupport24
{
    internal sealed record Support(
        string Handle,string Description,double X,double Y,double Z,
        string PnpClass,string Size,string Spec);

    internal sealed record Result(
        IReadOnlyList<Support> Members,
        int CandidateCount,int LinkedRowCount,
        IReadOnlyList<string> Diagnostics);

    internal static Result Read(
        Database db,IReadOnlyCollection<ObjectId> selected)
    {
        var found=new List<Support>();
        var notes=new List<string>();
        int candidates=0,linked=0;

        object? dlm=null;
        MethodInfo? findRow=null,getProps=null;
        try
        {
            Type app=AppDomain.CurrentDomain.GetAssemblies()
                .Select(a=>a.GetType(
                    "Autodesk.ProcessPower.PlantInstance.PlantApplication",
                    throwOnError:false))
                .FirstOrDefault(x=>x!=null) ??
                throw new InvalidOperationException("PlantApplication not loaded");
            object project=app.GetProperty("CurrentProject",
                BindingFlags.Static|BindingFlags.Public)?.GetValue(null) ??
                throw new InvalidOperationException("No active Plant 3D project");
            object parts=project.GetType().GetProperty("ProjectParts")?
                .GetValue(project) ??
                throw new InvalidOperationException("No project parts");
            object piping=parts.GetType().GetProperty("Item")?
                .GetValue(parts,new object[]{"Piping"}) ??
                throw new InvalidOperationException("Piping project part unavailable");
            dlm=piping.GetType().GetProperty("DataLinksManager")?
                .GetValue(piping);
            if(dlm==null)
                throw new InvalidOperationException("Piping DataLinksManager unavailable");
            findRow=dlm.GetType().GetMethods(BindingFlags.Public|
                BindingFlags.Instance).FirstOrDefault(m=>
            {
                var p=m.GetParameters();
                return m.Name=="FindAcPpRowId"&&p.Length==1&&
                    p[0].ParameterType==typeof(ObjectId);
            });
            getProps=dlm.GetType().GetMethods(BindingFlags.Public|
                BindingFlags.Instance).FirstOrDefault(m=>
            {
                var p=m.GetParameters();
                return m.Name=="GetAllProperties"&&p.Length==2&&
                    p[0].ParameterType==typeof(int)&&
                    p[1].ParameterType==typeof(bool);
            });
            if(findRow==null||getProps==null)
                notes.Add("SUPPORT_DATALINKS_SIGNATURE_UNAVAILABLE");
        }
        catch(System.Exception e)
        {
            notes.Add("SUPPORT_DATALINKS_UNAVAILABLE="+e.GetType().Name+
                ": "+e.Message);
        }

        using var tr=db.TransactionManager.StartTransaction();
        foreach(ObjectId id in selected.Distinct())
        {
            try
            {
                if(id.IsNull||!id.IsValid||id.IsErased)continue;
                if(tr.GetObject(id,OpenMode.ForRead,false) is not Entity entity)
                    continue;
                string handle=id.Handle.ToString();
                string typeName=entity.GetType().Name;
                string rxName="";
                try{rxName=id.ObjectClass?.Name??"";}catch{}
                // Structural steel and pipe entities are processed by
                // existing dedicated readers and never here.
                if(typeName.Contains("StructureMember",
                        StringComparison.OrdinalIgnoreCase)||
                   rxName.Contains("StructureMember",
                        StringComparison.OrdinalIgnoreCase)||
                   typeName.EndsWith("Pipe",StringComparison.OrdinalIgnoreCase)||
                   rxName.EndsWith("Pipe",StringComparison.OrdinalIgnoreCase))
                    continue;
                bool nativeSupport=
                    SupportOrthoSupportLabel24.IsSupportClass(typeName)||
                    SupportOrthoSupportLabel24.IsSupportClass(rxName)||
                    typeName.Contains("PipeSupport",
                        StringComparison.OrdinalIgnoreCase)||
                    rxName.Contains("PipeSupport",
                        StringComparison.OrdinalIgnoreCase);

                int row=0;
                IReadOnlyDictionary<string,string> data=
                    new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                if(dlm!=null&&findRow!=null&&getProps!=null)
                {
                    try
                    {
                        row=Convert.ToInt32(findRow.Invoke(
                            dlm,new object[]{id}));
                        if(row>0)
                        {
                            if(getProps.Invoke(dlm,new object[]{row,true})
                                is IEnumerable pairs)
                            {
                                var dict=new Dictionary<string,string>(
                                    StringComparer.OrdinalIgnoreCase);
                                foreach(object? pair in pairs)
                                {
                                    if(pair==null)continue;
                                    string key=pair.GetType().GetProperty("Key")?
                                        .GetValue(pair)?.ToString()??"";
                                    string val=pair.GetType().GetProperty("Value")?
                                        .GetValue(pair)?.ToString()??"";
                                    if(!string.IsNullOrWhiteSpace(key))
                                        dict[key]=val;
                                }
                                data=dict;
                            }
                        }
                    }
                    catch(System.Exception e)
                    {
                        notes.Add("SUPPORT_PNP_READ_ERROR="+handle+
                            " "+e.GetType().Name+":"+e.Message);
                    }
                }
                string? className=SupportOrthoSupportLabel24.Get(data,
                    "ClassName","Class","PnpClassName");
                // In the PnP database, Plant 3D may expose the derived
                // class name in either Entity ObjectClass or GetAllProperties.
                bool rowIsSupport=SupportOrthoSupportLabel24.IsSupportClass(
                    className);
                // The Properties palette can expose derived class Support
                // while GetAllProperties omits Class entirely. In that case
                // the dedicated PipeSupportsSpec + Support part subtype is
                // authoritative support-category evidence.
                string spec=SupportOrthoSupportLabel24.Get(
                    data,"Spec")??"";
                string subtype=SupportOrthoSupportLabel24.Get(
                    data,"PartSubType","Part Subtype")??"";
                bool supportSpec=SupportOrthoSupportLabel24.Normalize(spec)
                    =="PIPESUPPORTSSPEC";
                bool supportSubtype=SupportOrthoSupportLabel24.IsSupportClass(
                    subtype);
                if(!nativeSupport&&!rowIsSupport&&!supportSpec&&!supportSubtype)
                    continue;
                candidates++;
                if(row>0)linked++;
                if(row<=0)
                {
                    notes.Add("SUPPORT_PNP_ROW_MISSING="+handle+
                        " ENTITY="+typeName);
                    continue;
                }
                string? label=SupportOrthoSupportLabel24.ChooseDescription(
                    data);
                if(string.IsNullOrWhiteSpace(label))
                {
                    notes.Add("SUPPORT_DESCRIPTION_MISSING="+handle+
                        " ROW="+row);
                    continue;
                }
                Extents3d b=entity.GeometricExtents;
                double x=(b.MinPoint.X+b.MaxPoint.X)*.5;
                double y=(b.MinPoint.Y+b.MaxPoint.Y)*.5;
                double z=(b.MinPoint.Z+b.MaxPoint.Z)*.5;
                if(!double.IsFinite(x)||!double.IsFinite(y)||
                   !double.IsFinite(z))
                {
                    notes.Add("SUPPORT_EXTENTS_INVALID="+handle);
                    continue;
                }
                found.Add(new Support(handle,label,x,y,z,
                    className??typeName,
                    SupportOrthoSupportLabel24.Get(data,"Size")??"",
                    SupportOrthoSupportLabel24.Get(data,"Spec")??""));
                notes.Add("SUPPORT_PNP_ACCEPTED="+handle+" ROW="+row+
                    " CLASS="+(className??typeName)+
                    " DESCRIPTION="+label);
            }
            catch(System.Exception e)
            {
                notes.Add("SUPPORT_ENTITY_ERROR="+id.Handle+" "+
                    e.GetType().Name+":"+e.Message);
            }
        }
        tr.Commit();
        return new Result(
            found.OrderBy(x=>x.Handle,StringComparer.OrdinalIgnoreCase)
                .ToArray(),candidates,linked,notes);
    }
}
