using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.17 read-only source-member anchors. Each member's exact Plant PnP
/// Size comes from REV22.16 successfully tested DataLinksManager evidence;
/// its model-space leader point is the midpoint of the ACTUAL member entity
/// GeometricExtents. No anonymous assembly centroid is used as a substitute.
/// </summary>
internal static class SupportOrthoMemberSource17
{
    internal sealed record Result(
        IReadOnlyList<SupportOrthoMemberCalloutPlan17.Member> Members,
        IReadOnlyList<string> Diagnostics);

    internal static Result Read(Database db,IReadOnlyCollection<ObjectId> selected,
        SupportOrthoPlantProperties16.Result plantRead)
    {
        var sizes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string evidence in plantRead.Summary.Evidence)
        {
            const string marker="PNP_STRUCTURE_SIZE handle=";
            if(!evidence.StartsWith(marker,StringComparison.Ordinal))continue;
            int idEnd=evidence.IndexOf(' ',marker.Length);
            int sizeStart=evidence.IndexOf(" SIZE=",StringComparison.Ordinal);
            if(idEnd<=marker.Length||sizeStart<0||sizeStart+6>=evidence.Length)
                continue;
            string handle=evidence.Substring(marker.Length,idEnd-marker.Length);
            string size=evidence.Substring(sizeStart+6).Trim();
            if(!string.IsNullOrWhiteSpace(size))sizes[handle]=size;
        }
        var found=new List<SupportOrthoMemberCalloutPlan17.Member>();
        var diagnostics=new List<string>();
        using var tr=db.TransactionManager.StartTransaction();
        foreach(ObjectId id in selected.Distinct())
        {
            try
            {
                if(id.IsNull||!id.IsValid||id.IsErased)continue;
                if(tr.GetObject(id,OpenMode.ForRead,false) is not Entity e)continue;
                string typ=e.GetType().Name;
                string nativeName="";
                try{nativeName=id.ObjectClass?.Name??"";}catch{}
                if(!typ.Contains("StructureMember",StringComparison.OrdinalIgnoreCase)&&
                   !nativeName.Contains("StructureMember",StringComparison.OrdinalIgnoreCase))
                    continue;
                string handle=id.Handle.ToString();
                if(!sizes.TryGetValue(handle,out string? size))
                {
                    diagnostics.Add("MISSING_PROJECT_SIZE="+handle);
                    continue;
                }
                Extents3d bb=e.GeometricExtents;
                double x=(bb.MinPoint.X+bb.MaxPoint.X)*.5;
                double y=(bb.MinPoint.Y+bb.MaxPoint.Y)*.5;
                double z=(bb.MinPoint.Z+bb.MaxPoint.Z)*.5;
                if(!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(z))
                {
                    diagnostics.Add("INVALID_CENTER="+handle);
                    continue;
                }
                found.Add(new SupportOrthoMemberCalloutPlan17.Member(handle,
                    size,x,y,z));
            }
            catch(System.Exception ex)
            {
                diagnostics.Add("INVALID_MEMBER="+id.Handle+" "+
                    ex.GetType().Name+":"+ex.Message);
            }
        }
        tr.Commit();
        return new Result(found.OrderBy(m=>m.Handle,
            StringComparer.OrdinalIgnoreCase).ToArray(),diagnostics);
    }
}
