using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;

namespace SPTC.ProductionIso;

/// <summary>
/// REV31. Keep the REAL Autodesk checkbox list (and Create eligibility)
/// while restricting the native text filter's broad substring results to
/// one exact current-DWG Line Number Tag. Never create another checkbox
/// overlay, delete PnP records, or force-enable Create.
///
/// Only the in-memory, unbound WinForms item collection of the original
/// dialog is modified. Removed original item OBJECTS and checked states
/// are remembered for reversible restoration when Active mode is toggled
/// off. A bound/virtual/unrecognized list is left unchanged and the active
/// filter is rolled back safely.
/// </summary>
internal static class NativeIsoExactLineFilter
{
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(
        IntPtr parent, EnumChildProc cb, IntPtr data);

    private sealed record RemovedRow(int Index, object Item, CheckState Check);
    private sealed class PrunedState
    {
        public CheckedListBox? Checked;
        public ListView? View;
        public List<RemovedRow> Removed { get; } = new();
        // Preserve object identity of remaining native rows so a delayed
        // Autodesk repaint never receives stale rows from an older list.
        public List<object> Survivors { get; } = new();
        public int Retained;
    }

    private static IntPtr _dialog;
    private static Document? _document;
    private static string _tag = "";
    private static System.Windows.Forms.Timer? _timer;
    private static PrunedState? _state;
    private static int _attempts;
    private static bool _working;
    private static bool _diagnosticWritten;

    public static void Begin(IntPtr dialog, string tag, Document? document)
    {
        End(restore: false);
        _dialog = dialog;
        _tag = tag.Trim();
        _document = document;
        _attempts = 0;
        _diagnosticWritten = false;

        if (_dialog == IntPtr.Zero || _tag.Length == 0) return;

        // Let Autodesk's native Filter.TextChanged finish refreshing its
        // own items first. Initial UI work is small and database-free.
        _timer = new System.Windows.Forms.Timer { Interval = 160 };
        _timer.Tick += (_, _) => RefreshExact();
        _timer.Start();
    }

    private static void RefreshExact()
    {
        if (_working || _dialog == IntPtr.Zero || _tag.Length == 0) return;
        if (!ActiveDwgTags.IsNativeFilterCurrent(_dialog))
        {
            // REV42: manual editing is allowed. Restore previously pruned
            // native objects only if their saved identity/row-count checks
            // still match. End() already refuses stale restoration after
            // Autodesk has rebuilt the list for the new search term.
            End(restore: true);
            return;
        }

        _working = true;
        try
        {
            if (!TryFindNativeList(_dialog, out var box, out var view))
            {
                RetryOrRollback("Native checkbox list is not available.");
                return;
            }

            if (box != null && box.DataSource != null)
            {
                Rollback("Native list is data-bound; in-memory exact trimming is unsafe.");
                return;
            }
            if (view != null && view.VirtualMode)
            {
                Rollback("Native list uses virtual rows; exact trimming is unsafe.");
                return;
            }

            var rows = box != null
                ? Enumerable.Range(0, box.Items.Count)
                    .Select(i => (Index: i,
                        Text: box.GetItemText(box.Items[i]).Trim(),
                        Item: box.Items[i]!,
                        Check: box.GetItemCheckState(i))).ToList()
                : Enumerable.Range(0, view!.Items.Count)
                    .Select(i => (Index: i,
                        Text: view.Items[i].Text.Trim(),
                        Item: (object)view.Items[i],
                        Check: view.Items[i].Checked
                            ? CheckState.Checked : CheckState.Unchecked)).ToList();

            // Autodesk can briefly have no items during its filter refresh.
            if (rows.Count == 0 || !rows.Any(r =>
                r.Text.Equals(_tag, StringComparison.OrdinalIgnoreCase)))
            {
                RetryOrRollback("No exact native line item for " + _tag + ".");
                return;
            }

            List<(int Index, string Text, object Item, CheckState Check)> extras =
                rows.Where(r => !r.Text.Equals(_tag,
                    StringComparison.OrdinalIgnoreCase)).ToList();

            if (extras.Count > 0)
            {
                // If native TextChanged regenerated the collection, abandon
                // the previous stale snapshot and take a new one.
                var next = new PrunedState { Checked = box, View = view,
                    Retained = rows.Count - extras.Count };
                foreach (var extra in extras)
                    next.Removed.Add(new RemovedRow(
                        extra.Index, extra.Item, extra.Check));
                next.Survivors.AddRange(rows.Where(r =>
                    r.Text.Equals(_tag, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Item));

                if (box != null)
                {
                    box.BeginUpdate();
                    try
                    {
                        foreach (var extra in extras.OrderByDescending(r => r.Index))
                            box.Items.RemoveAt(extra.Index);
                    }
                    finally { box.EndUpdate(); }
                }
                else if (view != null)
                {
                    view.BeginUpdate();
                    try
                    {
                        foreach (var extra in extras.OrderByDescending(r => r.Index))
                            view.Items.RemoveAt(extra.Index);
                    }
                    finally { view.EndUpdate(); }
                }

                _state = next;
                _document?.Editor.WriteMessage(
                    "\nSPTC REV31: Exact native filter " + _tag +
                    " — hidden " + extras.Count +
                    " substring-only line(s); real Autodesk checklist retained.");
            }
            else if (_state is null)
            {
                // Native filter was exact already. No items were mutated.
                _state = new PrunedState { Checked = box, View = view,
                    Retained = rows.Count };
                _state.Survivors.AddRange(rows.Select(r => r.Item));
            }

            _attempts = 0;
            if (_timer != null) _timer.Interval = 1200;
            NativeIsoLineSelectOverlay.UpdateDisplayedCount();
        }
        catch (Exception ex)
        {
            Rollback("Native exact-filter UI update failed: " +
                ex.GetBaseException().Message);
        }
        finally { _working = false; }
    }

    private static void RetryOrRollback(string reason)
    {
        if (++_attempts < 4)
        {
            if (_timer != null) _timer.Interval = 300;
            return;
        }
        Rollback(reason);
    }

    private static void Rollback(string reason)
    {
        if (!_diagnosticWritten)
        {
            _diagnosticWritten = true;
            _document?.Editor.WriteMessage(
                "\nSPTC REV31: " + reason +
                " Native exact view canceled, not showing misleading matches.");
        }
        IntPtr dialog = _dialog;
        Document? doc = _document;
        End(restore: true);
        ActiveDwgTags.ClearOneTag(dialog, doc);
        NativeIsoToolbarClickFilter.MarkAllLinesRestored(dialog);
        NativeIsoLineSelectOverlay.UpdateDisplayedCount();
    }

    /// <summary>
    /// The original exact matching item remains the original Autodesk item.
    /// Insert only the temporarily removed original objects back into their
    /// positions before clearing Autodesk's broad text filter. No changes
    /// are made to the project model, SQL/PnP records, or DWG.
    /// </summary>
    public static void End(bool restore)
    {
        try { _timer?.Stop(); _timer?.Dispose(); } catch { }
        _timer = null;

        PrunedState? state = _state;
        _state = null;
        _dialog = IntPtr.Zero;
        _tag = "";
        _document = null;
        _attempts = 0;

        if (!restore || state == null || state.Removed.Count == 0)
            return;

        try
        {
            if (state.Checked is { IsDisposed: false } box &&
                box.DataSource == null && box.Items.Count == state.Retained &&
                Enumerable.Range(0, box.Items.Count).All(index =>
                    ReferenceEquals(box.Items[index], state.Survivors[index])))
            {
                box.BeginUpdate();
                try
                {
                    foreach (RemovedRow row in state.Removed.OrderBy(x => x.Index))
                    {
                        int index = Math.Min(row.Index, box.Items.Count);
                        box.Items.Insert(index, row.Item);
                        box.SetItemCheckState(index, row.Check);
                    }
                }
                finally { box.EndUpdate(); }
            }
            else if (state.View is { IsDisposed: false, VirtualMode: false } view &&
                     view.Items.Count == state.Retained &&
                     Enumerable.Range(0, view.Items.Count).All(index =>
                         ReferenceEquals(view.Items[index],
                             state.Survivors[index])))
            {
                view.BeginUpdate();
                try
                {
                    foreach (RemovedRow row in state.Removed.OrderBy(x => x.Index))
                    {
                        if (row.Item is ListViewItem item)
                        {
                            item.Checked = row.Check == CheckState.Checked;
                            view.Items.Insert(Math.Min(row.Index,
                                view.Items.Count), item);
                        }
                    }
                }
                finally { view.EndUpdate(); }
            }
        }
        catch (Exception)
        {
            // Autodesk will repopulate from its own data when its Filter is
            // cleared. Never force item restoration into a changed collection.
        }
    }

    private static bool TryFindNativeList(IntPtr dialog,
        out CheckedListBox? checkedList, out ListView? view)
    {
        checkedList = null;
        view = null;
        IntPtr group = IntPtr.Zero;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is GroupBox g &&
                g.Text.Trim().Equals("Display lines",
                    StringComparison.OrdinalIgnoreCase))
            {
                group = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (group == IntPtr.Zero) return false;

        CheckedListBox? bestChecked = null;
        ListView? bestView = null;
        int bestHeight = -1;

        EnumChildWindows(group, (hwnd, _) =>
        {
            Control? c = Control.FromHandle(hwnd);
            if (c is CheckedListBox list && list.Height > 100 &&
                list.Height > bestHeight)
            {
                bestChecked = list;
                bestView = null;
                bestHeight = list.Height;
            }
            else if (c is ListView lv && lv.CheckBoxes &&
                lv.Height > 100 && lv.Height > bestHeight)
            {
                bestChecked = null;
                bestView = lv;
                bestHeight = lv.Height;
            }
            return true;
        }, IntPtr.Zero);

        checkedList = bestChecked;
        view = bestView;
        return checkedList != null || view != null;
    }
}
