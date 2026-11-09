using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.14 read-only source-model inspection. Never edits a block,
/// explodes structural geometry, or fabricates steel sizes from extents.
/// Exact size evidence is gathered from declared attributes, recognized
/// member identifiers/layers, and accessible structure-member properties.
/// </summary>
internal static class SupportOrthoSourceReader
{
    internal static SupportOrthoMemberMetadata.Summary Inspect(
        Database db,IReadOnlyCollection<ObjectId> selected)
    {
        var blocks=new List<string>();
        var sizes=new List<string>();
        var evidence=new List<string>();
        var warnings=new List<string>();
        using var tr=db.TransactionManager.StartTransaction();
        foreach(ObjectId id in selected.Distinct())
        {
            try
            {
                if(id.IsNull||!id.IsValid||id.IsErased)continue;
                if(tr.GetObject(id,OpenMode.ForRead,false) is not Entity entity)continue;
                string handle=id.Handle.ToString();
                if(entity is BlockReference block)
                {
                    string name=EffectiveName(tr,block);
                    blocks.Add(name);
                    evidence.Add("BLOCK:"+handle+"="+name);
                    AddNameSize(name,"block-name "+handle,sizes,evidence);
                    AddAttributes(tr,block,handle,sizes,evidence);
                    InspectDefinition(tr,block.BlockTableRecord,handle,
                        new HashSet<ObjectId>(),0,sizes,evidence,warnings);
                }
                else
                {
                    // Only declared structural-member attributes, layer
                    // patterns, and explicit section-named properties are used.
                    InspectMember(entity,handle,sizes,evidence,warnings);
                }
            }
            catch(System.Exception ex)
            {
                warnings.Add("SELECT:"+id.Handle+" "+ex.GetType().Name);
            }
        }
        tr.Commit();
        return SupportOrthoMemberMetadata.Summarize(
            blocks,sizes,evidence,warnings);
    }

    private static string EffectiveName(Transaction tr,BlockReference block)
    {
        try
        {
            if(block.IsDynamicBlock &&
               tr.GetObject(block.DynamicBlockTableRecord,
                   OpenMode.ForRead,false) is BlockTableRecord canonical &&
               !string.IsNullOrWhiteSpace(canonical.Name))
                return canonical.Name;
        }
        catch { /* standard block name is the safe fallback */ }
        return string.IsNullOrWhiteSpace(block.Name)?
            "<UNNAMED BLOCK>":block.Name;
    }

    private static void AddNameSize(string? value,string provenance,
        List<string> sizes,List<string> evidence)
    {
        foreach(string size in SupportOrthoMemberMetadata.SectionsFromName(value))
        {
            sizes.Add(size);
            evidence.Add("SIZE:"+provenance+"="+size);
        }
    }

    private static void AddAttribute(string name,string value,string provenance,
        List<string> sizes,List<string> evidence)
    {
        string? size=SupportOrthoMemberMetadata.SectionFromAttribute(name,value);
        if(size!=null)
        {
            sizes.Add(size);
            evidence.Add("SIZE:"+provenance+"/"+name+"="+size);
        }
    }

    private static void AddAttributes(
        Transaction tr,BlockReference block,string provenance,
        List<string> sizes,List<string> evidence)
    {
        foreach(ObjectId id in block.AttributeCollection)
        {
            if(tr.GetObject(id,OpenMode.ForRead,false) is AttributeReference a)
                AddAttribute(a.Tag??"",a.TextString??"",
                    "block-attr "+provenance,sizes,evidence);
        }
        try
        {
            foreach(DynamicBlockReferenceProperty prop in
                block.DynamicBlockReferencePropertyCollection)
            {
                string value=prop.Value?.ToString()??"";
                AddAttribute(prop.PropertyName,value,
                    "dynamic-property "+provenance,sizes,evidence);
            }
        }
        catch { /* native/non-dynamic blocks need not offer these */ }
    }

    private static void InspectDefinition(Transaction tr,ObjectId defId,
        string path,HashSet<ObjectId> ancestry,int depth,
        List<string> sizes,List<string> evidence,List<string> warnings)
    {
        if(depth>5){warnings.Add("BLOCK_DEPTH_LIMIT:"+path);return;}
        if(!ancestry.Add(defId))
        {warnings.Add("BLOCK_CYCLE:"+path);return;}
        try
        {
            if(tr.GetObject(defId,OpenMode.ForRead,false) is not BlockTableRecord def)
                return;
            if(def.IsFromExternalReference || def.IsFromOverlayReference)
            {warnings.Add("XREF_CONTENT_NOT_INSPECTED:"+path);return;}
            foreach(ObjectId childId in def)
            {
                try
                {
                    if(tr.GetObject(childId,OpenMode.ForRead,false) is not Entity e)
                        continue;
                    string childPath=path+"/"+childId.Handle;
                    if(e is AttributeDefinition a)
                    {
                        AddAttribute(a.Tag??"",a.TextString??"",
                            "block-definition "+childPath,sizes,evidence);
                        continue;
                    }
                    if(e is BlockReference nested)
                    {
                        string name=EffectiveName(tr,nested);
                        AddNameSize(name,"nested-block-name "+childPath,sizes,evidence);
                        AddAttributes(tr,nested,childPath,sizes,evidence);
                        InspectDefinition(tr,nested.BlockTableRecord,childPath,
                            ancestry,depth+1,sizes,evidence,warnings);
                    }
                    else InspectMember(e,childPath,sizes,evidence,warnings);
                }
                catch(System.Exception ex)
                {warnings.Add("BLOCK_CHILD:"+childId.Handle+" "+ex.GetType().Name);}
            }
        }
        finally {ancestry.Remove(defId);}
    }

    private static void InspectMember(Entity member,string path,
        List<string> sizes,List<string> evidence,List<string> warnings)
    {
        // A C100/L75 designation on a steel member layer is explicit
        // CAD metadata, unlike a geometric bounding-box estimate.
        AddNameSize(member.Layer,"member-layer "+path,sizes,evidence);

        string rt=member.GetType().Name;
        string rx="";
        try{rx=member.ObjectId.ObjectClass?.Name??"";}catch{}
        bool isStructure=rt.Contains("StructureMember",
            StringComparison.OrdinalIgnoreCase)||
            rx.Contains("StructureMember",StringComparison.OrdinalIgnoreCase);
        if(!isStructure)return;
        foreach(string property in new[]{
            "SectionSize","Section","SectionName","Profile","ProfileSize",
            "MemberSize","Size","StructuralSize","Shape"})
        {
            try
            {
                var prop=member.GetType().GetProperty(property,
                    BindingFlags.Public|BindingFlags.Instance);
                if(prop==null||prop.GetIndexParameters().Length>0)continue;
                string value=prop.GetValue(member)?.ToString()??"";
                AddAttribute(property,value,
                    "structure-member-property "+path,sizes,evidence);
            }
            catch(System.Exception ex)
            {warnings.Add("MEMBER_PROPERTY:"+property+"/"+path+" "+ex.GetType().Name);}
        }
    }
}
