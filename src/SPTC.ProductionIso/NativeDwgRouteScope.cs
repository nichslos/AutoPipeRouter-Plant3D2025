using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.ProductionIso;

/// <summary>
/// REV07: Scope native Production Iso's existing SELECTION FILTER to actual
/// model-space Plant 3D piping objects from the currently open DWG.
/// No PnP project table updates, no object modification, no destructive edits.
/// </summary>
internal static class NativeDwgRouteScope
{
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumChildProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder output, int max);

    private const uint BM_CLICK = 0x00F5;
    public static int CurrentObjectCount { get; private set; }
    public static bool SelectionPrepared { get; private set; }
    private static bool _filterApplied;

    public static void Reset()
    {
        CurrentObjectCount = 0;
        SelectionPrepared = false;
        _filterApplied = false;
    }

    /// <summary>
    /// Collect only live Plant 3D model-space entities (not erased objects, project
    /// line-group records, or entities in other DWGs). AutoCAD handles selection.
    /// </summary>
    public static bool Prepare(Document doc)
    {
        Reset();
        try
        {
            var ids = new List<ObjectId>();
            using (Transaction transaction = doc.Database.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)transaction.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)transaction.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in model)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    DBObject obj;
                    try { obj = transaction.GetObject(id, OpenMode.ForRead); }
                    catch { continue; }
                    if (obj is not Entity entity || entity.IsErased) continue;
                    string typeName = entity.GetType().FullName ?? "";
                    string dxf = id.ObjectClass?.DxfName ?? "";
                    // Native Plant 3D pipe/valve/fitting types usually live in
                    // Autodesk.ProcessPower.PnP3dObjects. Prefer these rather than
                    // selecting all geometry, XREFs, equipment, or annotations.
                    bool pipingPart = typeName.StartsWith("Autodesk.ProcessPower.PnP3dObjects.", StringComparison.OrdinalIgnoreCase)
                        || (dxf.StartsWith("ACPP", StringComparison.OrdinalIgnoreCase) && !dxf.Contains("SUPPORT", StringComparison.OrdinalIgnoreCase));
                    if (!pipingPart) continue;
                    ids.Add(id);
                    if (ids.Count > 20000)
                    {
                        doc.Editor.WriteMessage("\nSPTC DWG scope: More than 20,000 Plant objects; automatic selection disabled to avoid lag.");
                        return false;
                    }
                }
                transaction.Commit();
            }

            if (ids.Count == 0)
            {
                doc.Editor.WriteMessage("\nSPTC DWG scope: No selectable Plant 3D objects found in model space. Native list unchanged.");
                return false;
            }
            doc.Editor.SetImpliedSelection(ids.ToArray());
            CurrentObjectCount = ids.Count;
            SelectionPrepared = true;
            doc.Editor.WriteMessage($"\nSPTC DWG scope: Prepared {ids.Count} live model-space objects from {doc.Name}.");
            return true;
        }
        catch (Exception ex)
        {
            doc.Editor.WriteMessage("\nSPTC DWG scope: " + ex.Message);
            return false;
        }
    }

    public static bool TryApplyFilter(IntPtr nativeDialog)
    {
        if (!SelectionPrepared || nativeDialog == IntPtr.Zero) return false;
        if (!GetWindowRect(nativeDialog, out RECT bounds)) return false;

        // First attempt: the native line selector is a managed ToolStrip.
        // Invoke the "Selection" / "Filter" item directly to keep Autodesk's
        // internal line-to-component mapping intact.
        bool invoked = false;
        EnumChildWindows(nativeDialog, (hwnd, _) =>
        {
            Control? c = Control.FromHandle(hwnd);
            if (c is not ToolStrip strip) return true;
            foreach (ToolStripItem item in strip.Items)
            {
                string desc = (item.ToolTipText + " " + item.Text + " " + item.Name).ToLowerInvariant();
                if (item.Enabled && desc.Contains("filter") &&
                    (desc.Contains("selection") || desc.Contains("line") || desc.Contains("select")))
                {
                    item.PerformClick();
                    invoked = true;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        if (invoked) { _filterApplied = true; return true; }

        // Second attempt: three small WinForms buttons at the top of Display
        // Lines, from left to right: Drawing, Project, Filter. Avoid clicking
        // any control unless *exactly three* matching buttons are found.
        var buttons = new List<(IntPtr Handle, int X, int Y)>();
        EnumChildWindows(nativeDialog, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT r)) return true;
            int x = r.Left + r.Width / 2 - bounds.Left;
            int y = r.Top + r.Height / 2 - bounds.Top;
            if (x < 90 || x > 260 || y < 26 || y > 112 ||
                r.Width < 17 || r.Width > 38 || r.Height < 17 || r.Height > 38)
                return true;
            var cls = new StringBuilder(128);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString().Contains("BUTTON", StringComparison.OrdinalIgnoreCase))
                buttons.Add((hwnd, x, y));
            return true;
        }, IntPtr.Zero);

        // Check all three are approximately aligned and adjacent.
        buttons = buttons.OrderBy(b => b.X).ToList();
        if (buttons.Count == 3 &&
            Math.Abs(buttons[0].Y - buttons[1].Y) < 12 &&
            Math.Abs(buttons[1].Y - buttons[2].Y) < 12 &&
            buttons[1].X - buttons[0].X < 45 &&
            buttons[2].X - buttons[1].X < 45)
        {
            SendMessage(buttons[2].Handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            _filterApplied = true;
            return true;
        }
        return false;
    }

    public static bool FilterApplied => _filterApplied;
}
