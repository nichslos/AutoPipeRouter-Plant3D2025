using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private static Stopwatch? _activeFilterWatch;
    private static bool _readyReported;
    // REV72: only a read-only health check runs after exact Active-DWG
    // validation. No recurring Items.RemoveAt(), SetItemChecked() or
    // Results/selection reconciliation for an unchanged native row.
    private static bool _steadyExact;
    private static object? _steadyItem;
    private static int _steadyNativeRowCount;
    private static int _steadyExactIndex;
    // REV46: one bounded alternative text query; still validate and retain
    // ONLY the exact full line number from Autodesk's original checklist.
    private static bool _fallbackApplied;
    private static string _fallbackQuery = "";

    public static void Begin(IntPtr dialog, string tag, Document? document)
    {
        End(restore: false);
        _dialog = dialog;
        _tag = tag.Trim();
        _document = document;
        _attempts = 0;
        _diagnosticWritten = false;
        _fallbackApplied = false;
        _fallbackQuery = "";
        _readyReported = false;
        _steadyExact = false;
        _steadyItem = null;
        _steadyNativeRowCount = 0;
        _steadyExactIndex = -1;
        _activeFilterWatch = Stopwatch.StartNew();

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
            // The user changed the search manually. Drop ONLY our Active
            // projection and return the native Autodesk list to the user;
            // no original checked-list item was ever removed.
            if (ProjectFolderLinePicker.ActiveDwgView)
                ProjectFolderLinePicker.ClearVisibleProjection();
            IntPtr oldDialog = _dialog;
            End(restore: false);
            NativeIsoLineSelectOverlay.ResumeNativeSelectionPolling(oldDialog);
            return;
        }

        _working = true;
        try
        {
            if (!TryFindNativeList(_dialog, out var box, out var view))
            {
                RetryOrRollback("Original native checkbox list unavailable.");
                return;
            }
            // REV77 intentionally DOES NOT remove native list items: it
            // preserves Autodesk's source row-index -> line data mapping.
            // That mapping was broken by REV31's Items.RemoveAt pruning,
            // explaining checked=False even after two real native clicks.
            if (box is { IsDisposed: true } ||
                view is { IsDisposed: true })
                return;

            var rows = box != null
                ? Enumerable.Range(0, box.Items.Count)
                    .Select(i => (Index: i,
                        Text: box.GetItemText(box.Items[i]).Trim(),
                        Item: box.Items[i]!)).ToList()
                : Enumerable.Range(0, view!.Items.Count)
                    .Select(i => (Index: i,
                        Text: view.Items[i].Text.Trim(),
                        Item: (object)view.Items[i])).ToList();
            var exact = rows.Where(r => r.Text.Equals(
                _tag, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exact.Length != 1)
            {
                if (_steadyExact)
                {
                    ProjectFolderLinePicker.ClearVisibleProjection();
                    _steadyExact = false;
                    _steadyItem = null;
                    NativeIsoLineSelectOverlay.ResumeNativeSelectionPolling(
                        _dialog);
                }
                RetryOrRollback(exact.Length == 0
                    ? "No exact native line item for " + _tag + "."
                    : "Ambiguous duplicate exact native rows for " + _tag + ".");
                return;
            }

            if (_steadyExact)
            {
                // Idempotent read-only health check. If Autodesk replaces
                // the native rows or moves the exact item to another index,
                // rebuild the small projection using the CURRENT indices.
                if (ProjectFolderLinePicker.ActiveDwgView &&
                    rows.Count == _steadyNativeRowCount &&
                    exact[0].Index == _steadyExactIndex &&
                    ReferenceEquals(exact[0].Item, _steadyItem))
                    return;

                ProjectFolderLinePicker.ClearVisibleProjection();
                _steadyExact = false;
                _steadyItem = null;
                NativeIsoLineSelectOverlay.ResumeNativeSelectionPolling(_dialog);
            }

            // The exact active route is displayed using the EXISTING
            // folder-style native-backed projection: a one-row visible list
            // delegates mouse/keyboard selection to the REAL Autodesk row
            // at its unchanged index in the original broad-filter results.
            // All unrelated substring matches remain in the original list
            // but unchecked, never passed to ISO creation.
            if (!ProjectFolderLinePicker.ShowExactActiveDwgTags(
                new[] { _tag }, _document))
            {
                RetryOrRollback(
                    "Native-backed exact view could not bind " + _tag + ".");
                return;
            }

            _attempts = 0;
            _steadyExact = true;
            _steadyItem = exact[0].Item;
            _steadyExactIndex = exact[0].Index;
            _steadyNativeRowCount = rows.Count;
            if (_timer != null) _timer.Interval = 2500;

            // This completes the current-DWG mode transition. No more
            // synthetic checkbox-click attempts on a reindexed native list.
            NativeIsoLineSelectOverlay.CompleteActiveProjectionScope(
                _dialog, _tag);

            if (!_readyReported)
            {
                _readyReported = true;
                _activeFilterWatch?.Stop();
                _document?.Editor.WriteMessage(
                    "\nSPTC REV77: exact native-backed active route " + _tag +
                    " ready in " +
                    (_activeFilterWatch?.ElapsedMilliseconds ?? 0) +
                    " ms; " + rows.Count +
                    " original Autodesk result row(s) retained.");
            }
        }
        catch (Exception ex)
        {
            Rollback("REV77 native-backed exact view failed: " +
                ex.GetBaseException().Message);
        }
        finally { _working = false; }
    }

    private static void RetryOrRollback(string reason)
    {
        int tries = ++_attempts;
        int limit = _fallbackApplied ? 6 : 4;
        if (tries < limit)
        {
            if (_timer != null) _timer.Interval = 300;
            return;
        }

        // REV46: A full native text query such as
        // CAP_BF_BND_TEE_RTEE+S-011 can temporarily return no Autodesk
        // rows. Try its distinctive final route suffix once, then compare
        // the ORIGINAL native row against the FULL name. Never generate
        // a line item or accept another route's substring match.
        if (!_fallbackApplied)
        {
            _fallbackApplied = true;
            string candidate = FallbackTerm(_tag);
            if (candidate.Length >= 4 &&
                ActiveDwgTags.TryApplyActiveSearchFallback(
                    _dialog, _tag, candidate, _document))
            {
                _fallbackQuery = candidate;
                _attempts = 0;
                if (_timer != null) _timer.Interval = 250;
                return;
            }
        }

        string extra = _fallbackQuery.Length > 0
            ? " The suffix search '" + _fallbackQuery +
              "' also failed to return the exact registered ISO tag."
            : " No safe alternate native search was available.";
        Rollback(reason + extra +
            " Confirm this full Line Number Tag is registered in " +
            "Production Iso for the active project.");
    }

    private static string FallbackTerm(string fullTag)
    {
        int split = fullTag.LastIndexOfAny(new[] { '+', '_', '/', '\\' });
        if (split >= 0 && fullTag.Length - split - 1 >= 4)
            return fullTag.Substring(split + 1);
        // For tags without separators, try a short distinctive tail.
        // Never search on a single digit or broad one/two-character term.
        if (fullTag.Length >= 10)
            return fullTag.Substring(fullTag.Length - 8);
        return "";
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
        if (ProjectFolderLinePicker.ActiveDwgView)
            ProjectFolderLinePicker.ClearVisibleProjection();
        End(restore: false);
        // When an active exact match cannot be verified, release the
        // selection-scope guard rather than leaving the native UI frozen.
        NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
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
        _steadyExact = false;
        _steadyItem = null;
        _steadyNativeRowCount = 0;
        _steadyExactIndex = -1;

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
