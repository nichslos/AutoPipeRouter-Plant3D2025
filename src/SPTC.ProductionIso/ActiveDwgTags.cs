using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.ProductionIso;

/// <summary>
/// REV08: read the live P3dLineGroup Tag linked to Plant 3D piping objects
/// in THIS open DWG. No changes to PnP database or model objects.
/// When exactly one active tag is verified, use Autodesk's own native text
/// filter to hide old line numbers; never remove entries from the source list.
/// </summary>
internal static class ActiveDwgTags
{
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hwnd, string text);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    private static readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase);
    public static int Count => Tags.Count;
    public static IReadOnlyList<string> CurrentTags => Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();
    public static int CheckedEntities { get; private set; }
    public static int LinkedEntities { get; private set; }
    public static string LastDiagnostic { get; private set; } = "Not scanned";
    private static IntPtr _lastAppliedDialog = IntPtr.Zero;

    public static void Scan(Document doc)
    {
        Tags.Clear();
        CheckedEntities = 0;
        LinkedEntities = 0;
        _lastAppliedDialog = IntPtr.Zero;
        try
        {
            object? dlm = ResolveDataLinksManager();
            if (dlm == null)
            {
                LastDiagnostic = "Piping DataLinksManager unavailable; no route filter applied.";
                doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
                return;
            }

            Type dlmType = dlm.GetType();
            MethodInfo? find = dlmType.GetMethods().FirstOrDefault(
                m => m.Name == "FindAcPpRowId" && m.GetParameters().Length == 1 &&
                     m.GetParameters()[0].ParameterType == typeof(ObjectId));
            MethodInfo? related = dlmType.GetMethods().FirstOrDefault(
                m => m.Name == "GetRelatedRowIds" && m.GetParameters().Length == 4 &&
                     m.GetParameters()[0].ParameterType == typeof(string));
            MethodInfo? properties = dlmType.GetMethods().FirstOrDefault(
                m => m.Name == "GetAllProperties" && m.GetParameters().Length == 2);

            if (find == null || related == null || properties == null)
            {
                LastDiagnostic = "The Plant 3D DataLinks API is different on this installation; native list left unchanged.";
                doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
                return;
            }

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                int inspected = 0;
                foreach (ObjectId id in model)
                {
                    if (++inspected > 20000) break;
                    if (!id.IsValid || id.IsErased) continue;
                    Entity? e;
                    try { e = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                    catch { continue; }
                    if (e == null || e.IsErased) continue;

                    // Restrict to actual Plant 3D model objects. Ignore CAD lines,
                    // xrefs, blocks, miscellaneous metadata and orphan PnP group rows.
                    string typeName = e.GetType().FullName ?? "";
                    string dxfName = id.ObjectClass?.DxfName ?? "";
                    bool plantPart = typeName.StartsWith("Autodesk.ProcessPower.PnP3dObjects.", StringComparison.OrdinalIgnoreCase)
                        || dxfName.StartsWith("ACPP", StringComparison.OrdinalIgnoreCase);
                    if (!plantPart) continue;
                    CheckedEntities++;

                    try
                    {
                        object? rowObj = find.Invoke(dlm, new object[] { id });
                        int rowId = Convert.ToInt32(rowObj);
                        if (rowId <= 0) continue;

                        // Component -> current line-group membership, not stale
                        // group records that merely remain in the project DB.
                        object? groupIds = related.Invoke(dlm,
                            new object[] { "P3dLineGroupPartRelationship", "Part", rowId, "LineGroup" });
                        bool linked = false;
                        if (groupIds is IEnumerable enumerated)
                        {
                            foreach (object? group in enumerated)
                            {
                                if (group == null) continue;
                                int groupId = Convert.ToInt32(group);
                                if (groupId <= 0) continue;
                                string? tag = PropertyFromRow(properties.Invoke(dlm, new object[] { groupId, false }), "Tag");
                                if (string.IsNullOrWhiteSpace(tag)) continue;
                                if (tag.Equals("Unassigned", StringComparison.OrdinalIgnoreCase)) continue;
                                Tags.Add(tag.Trim());
                                linked = true;
                            }
                        }

                        // Some models expose the inherited LineNumberTag directly
                        // on the component. This is only used when group lookup
                        // returned no usable Tag.
                        if (!linked)
                        {
                            string? tag = PropertyFromRow(properties.Invoke(dlm, new object[] { rowId, true }), "LineNumberTag");
                            if (!string.IsNullOrWhiteSpace(tag) &&
                                !tag.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                            {
                                Tags.Add(tag.Trim());
                                linked = true;
                            }
                        }
                        if (linked) LinkedEntities++;
                    }
                    catch
                    {
                        // Nonpiping Plant entities may lack the relationship.
                        // Never delete or modify data when no tag can be found.
                    }
                }
                tr.Commit();
            }

            LastDiagnostic = $"Scanned {CheckedEntities} Plant objects; found {LinkedEntities} linked objects, {Count} live route tag(s).";
            doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
            if (Count > 0)
                doc.Editor.WriteMessage("\nSPTC REV08 actual tags: " + string.Join(", ", CurrentTags));
        }
        catch (Exception ex)
        {
            Tags.Clear();
            LastDiagnostic = "Read-only scan failed: " + ex.Message;
            doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
        }
    }

    private static object? ResolveDataLinksManager()
    {
        // Plant-specific DLLs exist inside Plant 3D, not in the GitHub build
        // runner. Reflection avoids additional local installation or project edits.
        Type? app = FindPlantType("Autodesk.ProcessPower.PlantInstance.PlantApplication");
        if (app == null) return null;
        object? project = app.GetProperty("CurrentProject", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        object? parts = project?.GetType().GetProperty("ProjectParts")?.GetValue(project);
        object? piping = parts?.GetType().GetProperty("Item")?.GetValue(parts, new object[] { "Piping" });
        return piping?.GetType().GetProperty("DataLinksManager")?.GetValue(piping);
    }

    private static Type? FindPlantType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? match = assembly.GetType(fullName, false);
            if (match != null) return match;
        }
        try
        {
            return Assembly.Load("PnPProjectManagerMgd").GetType(fullName, false);
        }
        catch { return null; }
    }

    private static string? PropertyFromRow(object? values, string key)
    {
        if (values is not IEnumerable list) return null;
        foreach (object? entry in list)
        {
            if (entry == null) continue;
            Type t = entry.GetType();
            string? name = t.GetProperty("Key")?.GetValue(entry)?.ToString();
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase)) continue;
            return t.GetProperty("Value")?.GetValue(entry)?.ToString();
        }
        return null;
    }

    public static bool ApplyOneTag(IntPtr dialog, Document? doc)
    {
        if (Count != 1 || dialog == IntPtr.Zero)
        {
            doc?.Editor.WriteMessage(
                $"\nSPTC REV08: Found {Count} active tags. Exact native filter applies only when there is one verified route; no list modified.");
            return false;
        }
        if (!GetWindowRect(dialog, out RECT dlg)) return false;

        string tag = Tags.First();
        IntPtr candidate = IntPtr.Zero;
        int score = int.MinValue;

        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT rect) || rect.Width < 70 || rect.Height < 15 || rect.Height > 48)
                return true;

            // Top-left filter field immediately below Drawing / Project icons.
            int cx = (rect.Left + rect.Right) / 2 - dlg.Left;
            int cy = (rect.Top + rect.Bottom) / 2 - dlg.Top;
            if (cx < 25 || cx > dlg.Width * 0.33 || cy < 75 || cy > 175) return true;

            var cls = new StringBuilder(128);
            GetClassName(hwnd, cls, cls.Capacity);
            Control? c = Control.FromHandle(hwnd);
            bool edit = c is TextBoxBase ||
                        cls.ToString().Contains("EDIT", StringComparison.OrdinalIgnoreCase);
            if (!edit) return true;

            int quality = rect.Width - Math.Abs(cy - 120);
            if (quality > score)
            {
                candidate = hwnd;
                score = quality;
            }
            return true;
        }, IntPtr.Zero);

        if (candidate == IntPtr.Zero)
        {
            doc?.Editor.WriteMessage("\nSPTC REV08: Native filter textbox was not detected. No change made.");
            return false;
        }

        if (_lastAppliedDialog == dialog)
            return true;

        try
        {
            Control? control = Control.FromHandle(candidate);
            if (control is TextBoxBase textbox)
                textbox.Text = tag;
            else if (!SetWindowText(candidate, tag))
                return false;

            _lastAppliedDialog = dialog;
            doc?.Editor.WriteMessage("\nSPTC REV08: Native line filter set to current DWG tag: " + tag);
            return true;
        }
        catch (Exception ex)
        {
            doc?.Editor.WriteMessage("\nSPTC REV08 filter: " + ex.Message);
            return false;
        }
    }
}
