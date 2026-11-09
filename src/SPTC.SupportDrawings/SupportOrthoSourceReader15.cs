using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.15: read-only supplement to REV22.14. Handles selected BlockReference
/// AND separate native structure members or ordinary entities with explicit
/// section metadata. Never guesses profile from geometric extents.
/// </summary>
internal static class SupportOrthoSourceReader15
{
    private static readonly Regex ExplicitPair=new(
        @"^\s*(?<key>SECTION(?:_SIZE|_NAME|_TYPE)?|MEMBER_SIZE|PROFILE(?:_SIZE)?|CHANNEL_SIZE|ANGLE_SIZE|PLATE_SIZE|STEEL_SIZE|STRUCTURAL_SIZE|SIZE|SHAPE(?:_SIZE)?)\s*[:=]\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled|RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);

    internal static SupportOrthoSelection15.Summary Inspect(
        Database db,IReadOnlyCollection<ObjectId> selected)
    {
        if(selected==null||selected.Count==0)
            throw new ArgumentException("REV22.15 source selection must not be empty.");
        var original=SupportOrthoSourceReader.Inspect(db,selected);
        var sizes=original.MemberSizes.ToList();
        var evidence=original.Evidence.ToList();
        var warnings=original.Warnings.ToList();
        int blockCount=0,structuralCount=0,selectedCount=0;
        using var tr=db.TransactionManager.StartTransaction();
        foreach(var id in selected.Distinct())
        {
            if(id.IsNull||!id.IsValid||id.IsErased)continue;
            try
            {
                if(tr.GetObject(id,OpenMode.ForRead,false) is not Entity entity)
                    continue;
                selectedCount++;
                string handle=id.Handle.ToString();
                if(entity is BlockReference block)
                {
                    blockCount++;
                    InspectInsertedBlockAttributes(tr,block,handle,
                        sizes,evidence,warnings);
                }
                string type=entity.GetType().Name;
                string rx="";
                try{rx=id.ObjectClass?.Name??"";}catch{}
                bool structure=type.Contains("StructureMember",StringComparison.OrdinalIgnoreCase)||
                    rx.Contains("StructureMember",StringComparison.OrdinalIgnoreCase);
                if(structure)structuralCount++;

                // Typed CAD metadata survives independent selection of the
                // member objects, without requiring an outer BlockReference.
                InspectXdata(entity,handle,sizes,evidence,warnings);
                InspectExtensionDictionary(tr,entity,handle,sizes,evidence,warnings);
                if(structure)
                    InspectNativeStructureProperties(entity,handle,sizes,evidence,warnings);
            }
            catch(System.Exception ex)
            {warnings.Add("SOURCE15:"+id.Handle+" "+ex.GetType().Name);}
        }
        tr.Commit();
        return SupportOrthoSelection15.Build(
            selectedCount,blockCount,structuralCount,
            original.BlockName=="NOT A BLOCK REFERENCE"?
                Array.Empty<string>():new[]{original.BlockName},
            sizes,evidence,warnings);
    }

    private static string NormalizePropertyKey(string value) =>
        Regex.Replace((value??"").Trim(),
            @"([a-z0-9])([A-Z])","$1_$2")
            .Replace('-','_').Replace(' ','_').ToUpperInvariant();

    private static void AddTyped(string key,string? value,string location,
        List<string> sizes,List<string> evidence)
    {
        string canonical=NormalizePropertyKey(key);
        // ProfileName is a recognized typed structural description.
        if(canonical=="PROFILE_NAME")canonical="PROFILE";
        string? actual=SupportOrthoMemberMetadata.SectionFromAttribute(canonical,value);
        if(actual==null)return;
        sizes.Add(actual);
        evidence.Add("SIZE:"+location+"/"+key+"="+actual);
    }

    // Treat SectionSize / SectionName / MemberSize in normal AutoCAD
    // AttributeReferences and dynamic block properties as typed metadata.
    private static void InspectInsertedBlockAttributes(
        Transaction tr,BlockReference block,string handle,
        List<string> sizes,List<string> evidence,List<string> warnings)
    {
        try
        {
            foreach(ObjectId id in block.AttributeCollection)
                if(tr.GetObject(id,OpenMode.ForRead,false) is AttributeReference attr)
                    AddTyped(attr.Tag??"",attr.TextString,
                        "block-attribute "+handle,sizes,evidence);
            try
            {
                foreach(DynamicBlockReferenceProperty prop in
                    block.DynamicBlockReferencePropertyCollection)
                    AddTyped(prop.PropertyName,prop.Value?.ToString(),
                        "dynamic-block "+handle,sizes,evidence);
            }
            catch(System.Exception ex)
            {warnings.Add("DYNAMIC-BLOCK:"+handle+" "+ex.GetType().Name);}
        }
        catch(System.Exception ex)
        {warnings.Add("BLOCK-ATTR:"+handle+" "+ex.GetType().Name);}
    }

    private static void InspectXdata(Entity entity,string handle,
        List<string> sizes,List<string> evidence,List<string> warnings)
    {
        try
        {
            using var data=entity.XData;
            if(data==null)return;
            foreach(TypedValue token in data)
            {
                if(token.TypeCode!=1000||token.Value is not string line)continue;
                Match matched=ExplicitPair.Match(line);
                if(!matched.Success)continue;
                AddTyped(matched.Groups["key"].Value,matched.Groups["value"].Value,
                    "xdata "+handle,sizes,evidence);
            }
        }
        catch(System.Exception ex)
        {warnings.Add("XDATA:"+handle+" "+ex.GetType().Name);}
    }

    private static void InspectExtensionDictionary(Transaction tr,
        Entity entity,string handle,List<string> sizes,
        List<string> evidence,List<string> warnings)
    {
        try
        {
            ObjectId dictId=entity.ExtensionDictionary;
            if(dictId.IsNull||!dictId.IsValid)return;
            if(tr.GetObject(dictId,OpenMode.ForRead,false) is not DBDictionary dict)return;
            foreach(DBDictionaryEntry pair in dict)
            {
                if(!SupportOrthoMemberMetadata.IsSizeProperty(
                    NormalizePropertyKey(pair.Key)))continue;
                if(tr.GetObject(pair.Value,OpenMode.ForRead,false) is not Xrecord rec)
                    continue;
                using var data=rec.Data;
                if(data==null)continue;
                foreach(TypedValue tv in data)
                {
                    if(tv.Value is not string value||string.IsNullOrWhiteSpace(value))continue;
                    AddTyped(pair.Key,value,"extension "+handle,sizes,evidence);
                    break;
                }
            }
        }
        catch(System.Exception ex)
        {warnings.Add("EXTENSION:"+handle+" "+ex.GetType().Name);}
    }

    private static void InspectNativeStructureProperties(
        Entity entity,string handle,
        List<string> sizes,List<string> evidence,List<string> warnings)
    {
        // Plant structure objects vary by installed version; inspect only
        // exact public property names that explicitly mean structural size.
        foreach(string key in new[]{"SectionSize","SectionName","Section",
            "MemberSize","Size","Profile","ProfileName","ProfileSize",
            "StructuralSize","Shape","ShapeSize"})
        {
            try
            {
                var prop=entity.GetType().GetProperty(key,
                    BindingFlags.Public|BindingFlags.Instance);
                if(prop==null||prop.GetIndexParameters().Length>0||
                   !prop.CanRead)continue;
                object? value=prop.GetValue(entity);
                if(value==null)continue;
                if(value is string str)
                    AddTyped(key,str,"native-structure "+handle,sizes,evidence);
                else
                {
                    // Avoid arbitrary GetType().ToString() or object IDs.
                    string printable=value.ToString()??"";
                    if(SupportOrthoMemberMetadata.SectionsFromName(printable).Count>0)
                        AddTyped(key,printable,"native-structure "+handle,sizes,evidence);
                }
            }
            catch(System.Exception ex)
            {warnings.Add("NATIVE-SECTION:"+handle+"/"+key+" "+ex.GetType().Name);}
        }
    }
}
