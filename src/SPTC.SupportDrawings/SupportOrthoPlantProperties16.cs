using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.16: Read the exact Plant 3D project Data Manager "Size" field
/// for selected native Structure Members. Previous REV22.15 object metadata
/// remains as fallback. The project database is NEVER modified.
/// Autodesk Plant 3D: Piping.DataLinksManager.FindAcPpRowId(id) and
/// GetAllProperties(rowId,true) expose Structure Member.Size.
/// </summary>
internal static class SupportOrthoPlantProperties16
{
    internal sealed record Result(
        SupportOrthoSelection15.Summary Summary,
        int CandidateMembers,
        int LinkedRows,
        int MembersWithSize,
        IReadOnlyList<string> Diagnostics);

    internal static Result Inspect(Database db,IReadOnlyCollection<ObjectId> selected)
    {
        var previous=SupportOrthoSourceReader15.Inspect(db,selected);
        var messages=new List<string>();
        var sizes=previous.MemberSizes.ToList();
        var evidence=previous.Evidence.ToList();
        int candidateMembers=0,linkedRows=0,foundSize=0;
        object? manager=null;
        try
        {
            Type? plantApp=AppDomain.CurrentDomain.GetAssemblies()
                .Select(a=>a.GetType(
                    "Autodesk.ProcessPower.PlantInstance.PlantApplication",false))
                .FirstOrDefault(t=>t!=null);
            if(plantApp==null)
                throw new InvalidOperationException(
                    "PlantApplication type not loaded in this CAD session");
            object plantProject=plantApp.GetProperty(
                "CurrentProject",BindingFlags.Static|BindingFlags.Public)?
                .GetValue(null) ??
                throw new InvalidOperationException("Plant 3D project unavailable");
            object projectParts=plantProject.GetType()
                .GetProperty("ProjectParts")?.GetValue(plantProject) ??
                throw new InvalidOperationException("ProjectParts unavailable");
            object piping=projectParts.GetType().GetProperty("Item")?
                .GetValue(projectParts,new object[]{"Piping"}) ??
                throw new InvalidOperationException("Piping project part unavailable");
            manager=piping.GetType().GetProperty("DataLinksManager")?
                .GetValue(piping) ??
                throw new InvalidOperationException(
                    "Piping DataLinksManager is not available");
        }
        catch(System.Exception ex)
        {
            messages.Add("PNP_DLM_UNAVAILABLE="+ex.GetType().Name+
                " "+ex.Message);
        }
        MethodInfo? findRow=null,getProperties=null;
        if(manager!=null)
        {
            findRow=manager.GetType().GetMethods(
                BindingFlags.Public|BindingFlags.Instance).FirstOrDefault(m=>
            {
                var args=m.GetParameters();
                return m.Name=="FindAcPpRowId" && args.Length==1 &&
                       args[0].ParameterType==typeof(ObjectId);
            });
            getProperties=manager.GetType().GetMethods(
                BindingFlags.Public|BindingFlags.Instance).FirstOrDefault(m=>
            {
                var args=m.GetParameters();
                return m.Name=="GetAllProperties" &&
                       args.Length==2 && args[0].ParameterType==typeof(int) &&
                       args[1].ParameterType==typeof(bool);
            });
            if(findRow==null||getProperties==null)
                messages.Add("PNP_DLM_SIGNATURE_NOT_FOUND");
        }

        using(var tr=db.TransactionManager.StartTransaction())
        {
            foreach(ObjectId id in selected.Distinct())
            {
                try
                {
                    if(id.IsNull||!id.IsValid||id.IsErased)continue;
                    if(tr.GetObject(id,OpenMode.ForRead,false) is not Entity e)continue;
                    var entityName=e.GetType().Name;
                    string rxName="";
                    try{rxName=id.ObjectClass?.Name??"";}catch{}
                    if(!entityName.Contains("StructureMember",
                        StringComparison.OrdinalIgnoreCase)&&
                       !rxName.Contains("StructureMember",
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    candidateMembers++;
                    if(manager==null||findRow==null||getProperties==null)
                        continue;

                    int row=Convert.ToInt32(findRow.Invoke(manager,new object[]{id}));
                    if(row<=0)
                    {
                        messages.Add("PNP_NO_ROW handle="+id.Handle);
                        continue;
                    }
                    linkedRows++;
                    if(getProperties.Invoke(manager,new object[]{row,true})
                        is not IEnumerable properties)
                    {
                        messages.Add("PNP_PROPERTIES_EMPTY handle="+id.Handle);
                        continue;
                    }

                    string exactSize="",longDescription="",className="";
                    foreach(object pair in properties)
                    {
                        if(pair==null)continue;
                        string name=pair.GetType().GetProperty("Key")?
                            .GetValue(pair)?.ToString()?.Trim()??"";
                        string value=pair.GetType().GetProperty("Value")?
                            .GetValue(pair)?.ToString()?.Trim()??"";
                        if(string.Equals(name,"Size",
                            StringComparison.OrdinalIgnoreCase))
                            exactSize=value;
                        else if(string.Equals(name,"LongDescriptionSize",
                                StringComparison.OrdinalIgnoreCase)||
                            string.Equals(name,"Long Description (Size)",
                                StringComparison.OrdinalIgnoreCase))
                            longDescription=value;
                        else if(string.Equals(name,"ClassName",
                                StringComparison.OrdinalIgnoreCase))
                            className=value;
                    }
                    // The selected entity must itself be a StructureMember.
                    // Never interpret an unrelated pipe's "Size" as steel size.
                    string? clean=SupportOrthoMemberMetadata.SectionFromAttribute(
                        "SIZE",exactSize);
                    if(clean==null)
                    {
                        messages.Add("PNP_SIZE_MISSING handle="+id.Handle+
                            " row="+row+
                            " description="+SupportOrthoMemberMetadata.Clean(
                                longDescription));
                        continue;
                    }
                    sizes.Add(clean);
                    foundSize++;
                    evidence.Add("PNP_STRUCTURE_SIZE handle="+id.Handle+
                        " row="+row+" class="+className+" SIZE="+clean);
                }
                catch(TargetInvocationException ex)
                {
                    messages.Add("PNP_READ_ERROR handle="+id.Handle+
                        " "+(ex.InnerException?.GetType().Name??ex.GetType().Name)+
                        ": "+(ex.InnerException?.Message??ex.Message));
                }
                catch(System.Exception ex)
                {
                    messages.Add("PNP_READ_ERROR handle="+id.Handle+" "+
                        ex.GetType().Name+": "+ex.Message);
                }
            }
        }
        var updated=SupportOrthoSelection15.Build(
            previous.SelectedCount,previous.BlockCount,
            previous.StructuralMemberCount,
            previous.BlockNames,sizes,evidence,
            previous.Warnings.Concat(messages));
        return new Result(updated,candidateMembers,linkedRows,foundSize,messages);
    }
}
