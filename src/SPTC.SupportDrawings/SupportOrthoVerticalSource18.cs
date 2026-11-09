using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.18: read the native Structure Member geometry orientation and the
/// corresponding project-linked Top of Steel property. Never edits source.
/// </summary>
internal static class SupportOrthoVerticalSource18
{
    internal sealed record Member(
        string Handle,string Size,double X,double Y,double Z,
        bool IsVertical,string DirectionSource,string? TosText,string TosSource);

    internal sealed record Result(
        IReadOnlyList<Member> Members,int VerticalCount,int PropertyTosCount,
        int ApproximateTosCount,IReadOnlyList<string> Diagnostics);

    internal static Result Read(Database db,
        IReadOnlyCollection<ObjectId> selected,
        SupportOrthoMemberSource17.Result original)
    {
        var byHandle=original.Members.ToDictionary(x=>x.Handle,
            StringComparer.OrdinalIgnoreCase);
        var results=new List<Member>();
        var diagnostics=new List<string>();
        object? dlm=null;
        MethodInfo? findRow=null,getProperties=null;
        try
        {
            var app=AppDomain.CurrentDomain.GetAssemblies()
                .Select(a=>a.GetType(
                    "Autodesk.ProcessPower.PlantInstance.PlantApplication",false))
                .FirstOrDefault(t=>t!=null) ??
                throw new InvalidOperationException("PlantApplication unavailable");
            object project=app.GetProperty("CurrentProject",
                BindingFlags.Public|BindingFlags.Static)?.GetValue(null) ??
                throw new InvalidOperationException("No active Plant project");
            object parts=project.GetType().GetProperty("ProjectParts")?
                .GetValue(project) ??
                throw new InvalidOperationException("No ProjectParts");
            object piping=parts.GetType().GetProperty("Item")?
                .GetValue(parts,new object[]{"Piping"}) ??
                throw new InvalidOperationException("Piping part unavailable");
            dlm=piping.GetType().GetProperty("DataLinksManager")?.GetValue(piping);
            if(dlm!=null)
            {
                findRow=dlm.GetType().GetMethods(BindingFlags.Public|
                    BindingFlags.Instance).FirstOrDefault(m=>
                    {
                        var args=m.GetParameters();
                        return m.Name=="FindAcPpRowId"&&
                            args.Length==1&&
                            args[0].ParameterType==typeof(ObjectId);
                    });
                getProperties=dlm.GetType().GetMethods(BindingFlags.Public|
                    BindingFlags.Instance).FirstOrDefault(m=>
                    {
                        var args=m.GetParameters();
                        return m.Name=="GetAllProperties"&&
                            args.Length==2&&
                            args[0].ParameterType==typeof(int)&&
                            args[1].ParameterType==typeof(bool);
                    });
            }
        }
        catch(System.Exception ex)
        {diagnostics.Add("PNP_TOS_SOURCE_UNAVAILABLE="+ex.GetType().Name);}
        using var tr=db.TransactionManager.StartTransaction();
        foreach(var id in selected.Distinct())
        {
            if(id.IsNull||!id.IsValid||id.IsErased)continue;
            try
            {
                if(tr.GetObject(id,OpenMode.ForRead,false) is not Entity entity)
                    continue;
                string handle=id.Handle.ToString();
                if(!byHandle.TryGetValue(handle,out var source))continue;
                Extents3d bb=entity.GeometricExtents;
                (double X,double Y,double Z)? start=PointProperty(entity,
                    new[]{"StartPoint","StartPosition","PositionStart","Start"});
                (double X,double Y,double Z)? end=PointProperty(entity,
                    new[]{"EndPoint","EndPosition","PositionEnd","End"});
                string? explicitTos=ReadDeclared(entity,
                    new[]{"TopOfSteel","Top of Steel","Top_Of_Steel"});
                if(explicitTos==null&&
                   dlm!=null&&findRow!=null&&getProperties!=null)
                {
                    try
                    {
                        int row=Convert.ToInt32(findRow.Invoke(dlm,new object[]{id}));
                        if(row>0 && getProperties.Invoke(
                            dlm,new object[]{row,true}) is IEnumerable props)
                        {
                            foreach(object pair in props)
                            {
                                if(pair==null)continue;
                                string key=pair.GetType().GetProperty("Key")?
                                    .GetValue(pair)?.ToString()??"";
                                if(Normalize(key)!="TOPOFSTEEL")continue;
                                string text=pair.GetType().GetProperty("Value")?
                                    .GetValue(pair)?.ToString()??"";
                                if(!string.IsNullOrWhiteSpace(text))
                                {
                                    explicitTos=text;
                                    break;
                                }
                            }
                        }
                    }
                    catch(System.Exception ex)
                    {diagnostics.Add("PNP_TOS_ERROR="+handle+":"+ex.GetType().Name);}
                }
                // Explicit Start / End X,Y,Z can also be exposed as
                // individual Properties palette fields rather than Point3d.
                start ??=PointComponents(entity,"Start");
                end ??=PointComponents(entity,"End");
                var status=SupportOrthoVerticalMember18.Evaluate(
                    bb.MaxPoint.X-bb.MinPoint.X,
                    bb.MaxPoint.Y-bb.MinPoint.Y,
                    bb.MaxPoint.Z-bb.MinPoint.Z,
                    start,end,explicitTos,bb.MaxPoint.Z);
                results.Add(new Member(handle,source.Size,
                    source.X,source.Y,source.Z,status.IsVertical,
                    status.DirectionSource,status.TopOfSteelText,
                    status.TopOfSteelSource));
                diagnostics.Add("MEMBER_VERTICAL handle="+handle+
                    " VERTICAL="+status.IsVertical+
                    " DIRECTION_SOURCE="+status.DirectionSource+
                    " TOS_SOURCE="+status.TopOfSteelSource+
                    " TOS="+(status.TopOfSteelText??"UNAVAILABLE"));
            }
            catch(System.Exception ex)
            {diagnostics.Add("MEMBER_TOS_READ_ERROR="+id.Handle+
                ":"+ex.GetType().Name+":"+ex.Message);}
        }
        tr.Commit();
        return new Result(results.OrderBy(m=>m.Handle,
            StringComparer.OrdinalIgnoreCase).ToArray(),
            results.Count(x=>x.IsVertical),
            results.Count(x=>x.TosSource=="PLANT_TOP_OF_STEEL_PROPERTY"),
            results.Count(x=>x.TosSource=="GEOMETRIC_TOP_APPROX"),
            diagnostics);
    }

    private static string Normalize(string? s) =>
        new string((s??"").Where(char.IsLetterOrDigit).ToArray())
            .ToUpperInvariant();

    private static string? ReadDeclared(object obj,string[] names)
    {
        foreach(string name in names)
        {
            try
            {
                var property=obj.GetType().GetProperty(name,
                    BindingFlags.Instance|BindingFlags.Public|
                    BindingFlags.IgnoreCase);
                var value=property?.GetValue(obj)?.ToString();
                if(!string.IsNullOrWhiteSpace(value))return value;
            }
            catch { }
        }
        try
        {
            foreach(PropertyDescriptor pd in TypeDescriptor.GetProperties(obj))
            {
                if(!names.Any(x=>Normalize(x)==Normalize(pd.DisplayName)||
                    Normalize(x)==Normalize(pd.Name)))continue;
                string? v=pd.GetValue(obj)?.ToString();
                if(!string.IsNullOrWhiteSpace(v))return v;
            }
        }
        catch { }
        return null;
    }

    private static (double X,double Y,double Z)? PointProperty(
        object entity,string[] names)
    {
        foreach(string name in names)
        {
            try
            {
                var p=entity.GetType().GetProperty(name,
                    BindingFlags.Public|BindingFlags.Instance);
                object? value=p?.GetValue(entity);
                if(value is Point3d pt)
                    return(pt.X,pt.Y,pt.Z);
                if(value is Vector3d vec)
                    return(vec.X,vec.Y,vec.Z);
            }
            catch { }
        }
        return null;
    }

    private static (double X,double Y,double Z)? PointComponents(
        object entity,string stem)
    {
        string? xs=ReadDeclared(entity,new[]{stem+"X",stem+" X"});
        string? ys=ReadDeclared(entity,new[]{stem+"Y",stem+" Y"});
        string? zs=ReadDeclared(entity,new[]{stem+"Z",stem+" Z"});
        if(SupportOrthoVerticalMember18.TryParseTos(xs,out double x)&&
           SupportOrthoVerticalMember18.TryParseTos(ys,out double y)&&
           SupportOrthoVerticalMember18.TryParseTos(zs,out double z))
            return(x,y,z);
        return null;
    }
}
