using System;
using System.Collections.Generic;
using System.Linq;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SPTC.ProductionIso;

// Select-all UI extension for Autodesk's original Create Production Iso dialog.
// Does not modify ISO setup, registered output paths, or the REV05 copier.
internal static class NativeIsoLineSelectOverlay
{
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVITEM
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int iGroup;
    }

    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder cls, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    private const uint LVM_GETITEMCOUNT = 0x1004;
    private const uint LB_GETCOUNT = 0x018B;
    private const uint LVM_SETITEMSTATE = 0x102B;
    private const uint LVM_GETEXTENDEDLISTVIEWSTYLE = 0x1037;
    private const long LVS_EX_CHECKBOXES = 0x00000004;
    private const uint LVIS_STATEIMAGEMASK = 0xF000;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static UserControl? _host;
    private static CheckBox? _toggle;
    private static Label? _selectedCountLabel;
    private static IntPtr _dialog;
    private static bool _setting;
    // REV17: lightweight UI-only count refresh. No PnP/SQL queries and no
    // changes to native checked states, even when the filter changes.
    private static System.Windows.Forms.Timer? _countTimer;
    // REV36: diagnostics and deferred actual native selection, no fake Win32 clicks.
    private static int _generation;
    private static System.Windows.Forms.Timer? _firstRowRetry;
    // REV65: one bounded, UI-thread reconciliation after a native
    // All/Current/Folder mode switch. Never force Create.Enabled.
    private static System.Windows.Forms.Timer? _modeRecoveryTimer;
    private static int _modeRecoverySerial;
    // REV66: preserve selected ROUTE NAMES across Autodesk's manual filter
    // rebuilds. The old REV65 counter only counted current visible rows,
    // and Autodesk dropped checked state while replacing its filtered list.
    private static readonly HashSet<string> SessionSelectedTags =
        new(StringComparer.OrdinalIgnoreCase);
    private static CheckedListBox? _trackedNativeList;
    private static TextBoxBase? _trackedNativeSearch;
    private static int _gestureIndex = -1;
    private static string _gestureTag = "";
    // REV67: a Shift range can check several native rows in ONE gesture.
    // Remember pre-click state for EVERY visible tag, not only clicked row.
    private static Dictionary<string, bool>? _gestureBefore;
    // REV76: distinguish an actual checkbox-area click/Space from mere
    // row highlighting. Only explicit checks can trigger native fallback.
    private static bool _gestureCheckboxAction;
    private static bool _initialNativeSelectionImported;
    private static bool _restoringSelection;
    // REV68: distinguish a programmatic toolbar/filter transition from
    // a user's checkbox click. Transition and delayed events must NEVER
    // remove a previously selected tag such as A-08.
    private static bool _scopeSwitchPending;
    // REV86: never let the 800ms counter poll repeatedly rewrite native
    // checkmarks while Autodesk is rebuilding the All Project checklist.
    // Selection restoration is owned solely by the bounded mode transition.
    private static bool _suspendPeriodicNativeWrites;
    private static int _selectionEpoch;
    private static string _lastNativeRowSignature = "";
    private static string _lastRestoredRowSignature = "";
    private static int _stableNativePolls;
    // REV70: once the exact current-DWG row is verified and restored,
    // suppress periodic writes to that unchanged native checkbox row.
    // Screen Snip temporarily suspends the competing UI timers, which
    // explains why it looked steady after capture. This latch removes
    // that timer competition without changing the saved Selected count.
    private static string _settledActiveRoute = "";
    // REV73: a separate, ONE-SHOT native checkbox commit AFTER Autodesk's
    // Filter.TextChanged/index remapping has settled. No repeating
    // checked/unchecked writes: stops blink and validates Create normally.
    private static System.Windows.Forms.Timer? _activeCommitTimer;
    private static int _activeCommitSerial;
    // REV74: batch Autodesk's SHOW ALL checklist redraw until its
    // project-wide rows have been refreshed and exact tag checks restored.
    // Prevents a temporary unchecked frame from flashing on-screen.
    // Only the original native CheckedListBox is suspended.
    private static CheckedListBox? _projectRedrawBatchList;
    // REV84: unfiltered Autodesk list size observed before Active-DWG mode.
    // Do not restore checks against a temporary 5-row search result.
    private static int _lastProjectWideRowCount;

    public static void BeginProjectRedrawBatch(IntPtr dialog)
    {
        EndProjectRedrawBatch();
        if (_dialog != dialog || dialog == IntPtr.Zero) return;
        CheckedListBox? list = FindNativeCheckedList(dialog);
        if (list is null || list.IsDisposed || !list.IsHandleCreated)
            return;
        try
        {
            list.BeginUpdate();
            _projectRedrawBatchList = list;
        }
        catch (Exception) { _projectRedrawBatchList = null; }
    }

    public static void EndProjectRedrawBatch()
    {
        CheckedListBox? list = _projectRedrawBatchList;
        _projectRedrawBatchList = null;
        if (list is null) return;
        try
        {
            if (!list.IsDisposed)
                list.EndUpdate();
        }
        catch (Exception) { }
    }

    private static void CancelActiveCommit()
    {
        try { _activeCommitTimer?.Stop(); _activeCommitTimer?.Dispose(); }
        catch (Exception) { }
        _activeCommitTimer = null;
        _activeCommitSerial++;
    }

    public static void Attach(IntPtr dialog)
    {
        if (_host != null && !_host.IsDisposed && _dialog == dialog)
            return;

        Hide();
        _generation++;
        _dialog = dialog;
        _lastProjectWideRowCount = 0;
        _suspendPeriodicNativeWrites = false;
        _host = new UserControl
        {
            BackColor = SystemColors.Control,
            Width = 294,
            Height = 31
        };
        _toggle = new CheckBox
        {
            Text = "Select All",
            AutoSize = false,
            Location = new Point(0, 2),
            Width = 149,
            Height = 25,
            Checked = false,
            Font = SystemFonts.MessageBoxFont
        };
        _toggle.CheckedChanged += ToggleChanged;
        _host.Controls.Add(_toggle);
        _selectedCountLabel = new Label
        {
            Text = "Selected: 0",
            Location = new Point(154, 4),
            Width = 136,
            Height = 23,
            AutoSize = false,
            Font = SystemFonts.MessageBoxFont,
            TextAlign = ContentAlignment.MiddleRight,
            // REV75: keep the Selected counter at the far right of the
            // resizable Line Numbers pane, not next to Select All.
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _host.Controls.Add(_selectedCountLabel);

        _ = _host.Handle;
        SetParent(_host.Handle, dialog);
        if (GetParent(_host.Handle) != dialog)
        {
            Hide();
            return;
        }
        Position(dialog);
        _host.Show();

        EnsureSelectionTracking();
        UpdateDisplayedCount();
        _countTimer = new System.Windows.Forms.Timer { Interval = 800 };
        _countTimer.Tick += (_, _) => UpdateDisplayedCount();
        _countTimer.Start();
    }

    public static void Position(IntPtr dialog)
    {
        if (_host == null || _host.IsDisposed || _dialog != dialog ||
            !GetClientRect(dialog, out RECT dialogRect))
            return;

        // Left "Display lines" panel, directly below the line number list.
        // Use the bottom edge of the list when it can be discovered.
        int left = 30;
        int top = Math.Max(150, dialogRect.Height - 150);
        if (TryFindLineListBounds(dialog, out RECT listBounds))
        {
            left = Math.Max(18, listBounds.Left);
            top = Math.Min(dialogRect.Height - 116, listBounds.Bottom + 5);
        }

        // REV75: the native line-list width grows/shrinks with the
        // horizontally stretched "Display lines" pane. Previously this
        // host was capped at 294px, so "Selected: N" stayed fixed near
        // Select All while the native scrollbar moved to the right.
        // Size the host to the LIVE native list width instead.
        int available = Math.Max(160, dialogRect.Width - left - 10);
        int width = TryFindLineListBounds(dialog, out RECT currentBounds)
            ? Math.Clamp(currentBounds.Width, 160, available)
            : Math.Min(294, available);

        // Resize the hosting HWND BEFORE setting child bounds. WinForms
        // Anchor=Right adjusts child positions during parent resizing;
        // laying children out first would apply the delta twice.
        SetWindowPos(_host.Handle, IntPtr.Zero, left, top, width, 29,
            SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);

        // Keep Select All LEFT; align Selected N RIGHT (just before the
        // native list's right edge/scrollbar). Allow enough width for
        // 3+ digit totals without overlapping the checkbox at normal sizes.
        if (_toggle != null && _selectedCountLabel != null)
        {
            const int rightPadding = 9;
            int textWidth = TextRenderer.MeasureText(
                _selectedCountLabel.Text,
                _selectedCountLabel.Font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int desiredLabelWidth = Math.Max(88, textWidth + 6);
            int labelWidth = Math.Min(desiredLabelWidth,
                Math.Max(51, width - 109 - rightPadding));
            int labelLeft = Math.Max(0, width - rightPadding - labelWidth);
            _toggle.Width = Math.Min(149, Math.Max(75, labelLeft - 5));
            _selectedCountLabel.SetBounds(
                labelLeft, 4, labelWidth, 23);
        }
    }

    // REV17: "Select All (N)" counts the lines currently displayed, not
    // how many are checked. When a folder is active the projected native-backed
    // line list is authoritative; otherwise read the current Autodesk list.
    // REV89: snapshot the original UNFILTERED Autodesk row count before
    // Active DWG sets the native search text. Show All must wait for the
    // complete project source, never act on the intermediate five A-08
    // substring matches. No file, database or model scans are performed.
    public static void RememberUnfilteredProjectRows(IntPtr dialog)
    {
        if (_dialog != dialog || dialog == IntPtr.Zero ||
            ProjectFolderLinePicker.HasVisibleProjection ||
            _scopeSwitchPending)
            return;
        EnsureSelectionTracking();
        if (_trackedNativeSearch is { IsDisposed: false } search &&
            !string.IsNullOrWhiteSpace(search.Text))
            return;
        CheckedListBox? list = FindNativeCheckedList(dialog);
        if (list is { IsDisposed: false } && list.Items.Count > 0)
        {
            _lastProjectWideRowCount = Math.Max(
                _lastProjectWideRowCount, list.Items.Count);
            WriteDiagnostic("REV89: remembered native All Project rows=" +
                _lastProjectWideRowCount + ".");
        }
    }

    public static void UpdateDisplayedCount()
    {
        if (_dialog == IntPtr.Zero || _toggle is null ||
            _toggle.IsDisposed || _host is null || _host.IsDisposed)
            return;

        int? count = ProjectFolderLinePicker.VisibleLineCount ??
                     ReadNativeLineCount(_dialog);
        string caption = count.HasValue
            ? $"Select All ({count.Value})"
            : "Select All";
        if (!string.Equals(_toggle.Text, caption, StringComparison.Ordinal))
            _toggle.Text = caption;

        // REV66: the actual selected route TAGS persist across search
        // views. Current screen may show 0 checks even though earlier
        // searches selected other routes. Folder projections retain their
        // existing visible-only selection semantics for ISO safety.
        EnsureSelectionTracking();
        // REV84: remember only a real project-wide (blank-search) row count.
        if (!_scopeSwitchPending &&
            !ProjectFolderLinePicker.HasVisibleProjection &&
            _trackedNativeSearch is { IsDisposed: false } search &&
            string.IsNullOrWhiteSpace(search.Text) &&
            _trackedNativeList is { IsDisposed: false } unfiltered)
            _lastProjectWideRowCount = Math.Max(
                _lastProjectWideRowCount, unfiltered.Items.Count);
        ReconcileVisibleNativeSelection();
        // REV92: Select All is LOCAL TO THE CURRENT VIEW. It means every
        // visible line in this Active/Folder/All filter is checked, NOT
        // every route remembered across other filters. During a scope
        // transition the native list is incomplete: clear the visual
        // Select All state without generating an unchecked event.
        RefreshScopeSelectAll();
        // REV92: Selected:N is GLOBAL ACROSS FILTERS. Never make it the
        // count of native rows currently in the filtered view.
        int? checkedCount = SessionSelectedTags.Count;
        if (_selectedCountLabel is { IsDisposed: false } selectedLabel)
        {
            string selectedText = checkedCount.HasValue
                ? "Selected: " + checkedCount.Value
                : "Selected: —";
            if (selectedLabel.Text != selectedText)
                selectedLabel.Text = selectedText;
        }

        // REV42: native Active-DWG scope may disable the search edit control
        // after initial attachment; keep the original input editable during
        // normal dialog use without changing its search text or route data.
        ActiveDwgTags.EnsureNativeSearchEditable(_dialog);
    }

    // REV98: expose the already-observed native project-wide source size.
    // Never use an intermediate five-substring-result list as project data.
    public static int KnownProjectWideRowCount(IntPtr dialog) =>
        dialog != IntPtr.Zero && dialog == _dialog
            ? _lastProjectWideRowCount : 0;

    public static bool IsNativeSearchBlank(IntPtr dialog)
    {
        if (dialog == IntPtr.Zero || dialog != _dialog) return false;
        EnsureSelectionTracking();
        return _trackedNativeSearch is { IsDisposed: false } search &&
            string.IsNullOrWhiteSpace(search.Text);
    }

    // REV21: read only the actual Autodesk line list to validate an active
    // tag filter. Never infer a route from the search textbox contents.
    public static int? NativeDisplayedCount(IntPtr dialog) =>
        ReadNativeLineCount(dialog);

    private static int? ReadNativeLineCount(IntPtr dialog)
    {
        int best = -1;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!IsLeftLineList(dialog, hwnd)) return true;

            Control? control = Control.FromHandle(hwnd);
            int count = -1;
            if (control is CheckedListBox checkedBox)
                count = checkedBox.Items.Count;
            else if (control is ListBox listBox)
                count = listBox.Items.Count;
            else if (control is ListView listView && listView.CheckBoxes)
                count = listView.Items.Count;
            else
            {
                var cls = new StringBuilder(128);
                GetClassName(hwnd, cls, cls.Capacity);
                string className = cls.ToString();
                if (className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
                    count = SendMessage(hwnd, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
                else if (className.Equals("ListBox", StringComparison.OrdinalIgnoreCase))
                    count = SendMessage(hwnd, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
            }

            if (count > best)
                best = count;
            return true;
        }, IntPtr.Zero);
        return best >= 0 ? best : null;
    }

    // REV65: read only the original Autodesk checked-list UI. These
    // queries are in-memory; no PnP/SQL, file or model scan.
    private static CheckedListBox? FindNativeCheckedList(IntPtr dialog)
    {
        CheckedListBox? best = null;
        int mostItems = -1;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!IsLeftLineList(dialog, hwnd)) return true;
            if (Control.FromHandle(hwnd) is CheckedListBox list &&
                !list.IsDisposed && list.Height > 100 &&
                list.Items.Count > mostItems)
            {
                best = list;
                mostItems = list.Items.Count;
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static int? ReadNativeCheckedCount(IntPtr dialog)
    {
        var list = FindNativeCheckedList(dialog);
        if (list != null)
        {
            int count = 0;
            for (int i = 0; i < list.Items.Count; i++)
                if (list.GetItemChecked(i)) count++;
            return count;
        }
        // If Autodesk exposes a native WinForms ListView instead, count
        // its genuine checked items; don't infer from a search textbox.
        ListView? best = null;
        int most = -1;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!IsLeftLineList(dialog, hwnd)) return true;
            if (Control.FromHandle(hwnd) is ListView view &&
                view.CheckBoxes && !view.IsDisposed &&
                view.Items.Count > most)
            {
                best = view;
                most = view.Items.Count;
            }
            return true;
        }, IntPtr.Zero);
        return best?.CheckedItems.Count;
    }

    // REV66: tag-keyed selection persists for the lifetime of THIS dialog
    // only. Never retain check states by Autodesk's disposable row indices.
    private static List<string> ReadCheckedTags(CheckedListBox list)
    {
        var names = new List<string>();
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (!list.GetItemChecked(i)) continue;
            string name = list.GetItemText(list.Items[i]).Trim();
            if (name.Length > 0) names.Add(name);
        }
        return names;
    }

    private static void DetachSelectionTracking()
    {
        if (_trackedNativeList is { IsDisposed: false } old)
        {
            old.MouseDown -= OnNativeLineMouseDown;
            old.MouseUp -= OnNativeLineMouseUp;
            old.KeyDown -= OnNativeLineKeyDown;
            old.KeyUp -= OnNativeLineKeyUp;
            old.ItemCheck -= OnNativeLineItemCheck;
        }
        if (_trackedNativeSearch is { IsDisposed: false } edit)
            edit.TextChanged -= OnSearchFilterChanged;
        _trackedNativeList = null;
        _trackedNativeSearch = null;
        _gestureIndex = -1;
        _gestureTag = "";
        _gestureBefore = null;
        _gestureCheckboxAction = false;
    }

    private static void EnsureSelectionTracking()
    {
        if (_dialog == IntPtr.Zero) return;
        CheckedListBox? list = FindNativeCheckedList(_dialog);
        if (list == null || list.IsDisposed)
            return;

        if (!ReferenceEquals(_trackedNativeList, list))
        {
            DetachSelectionTracking();
            _trackedNativeList = list;
            list.MouseDown += OnNativeLineMouseDown;
            list.MouseUp += OnNativeLineMouseUp;
            list.KeyDown += OnNativeLineKeyDown;
            list.KeyUp += OnNativeLineKeyUp;
            list.ItemCheck += OnNativeLineItemCheck;
            // REV67: ONLY import checked rows when opening this dialog.
            // Autodesk reuses checkbox INDEX state when it rebuilds a filtered
            // list, which can mark unrelated names as checked. Importing
            // those as NEW selections caused the 08 search to select all.
            if (!_initialNativeSelectionImported && list.Items.Count > 0)
            {
                foreach (string tag in ReadCheckedTags(list))
                    SessionSelectedTags.Add(tag);
                _initialNativeSelectionImported = true;
            }
            _lastNativeRowSignature = "";
            _lastRestoredRowSignature = "";
            _stableNativePolls = 0;
            _settledActiveRoute = "";
        }

        if (_trackedNativeSearch == null)
        {
            if (!GetWindowRect(list.Handle, out RECT listRect)) return;
            TextBoxBase? edit = null;
            EnumChildWindows(_dialog, (hwnd, _) =>
            {
                if (Control.FromHandle(hwnd) is not TextBoxBase candidate ||
                    candidate.IsDisposed ||
                    !GetWindowRect(hwnd, out RECT rect))
                    return true;
                // Search edit is directly ABOVE the native line list.
                // Never hook Revision / Custom Location on the right.
                bool above = rect.Bottom <= listRect.Top + 3 &&
                    rect.Bottom >= listRect.Top - 135;
                bool aligned = rect.Left < listRect.Right &&
                    rect.Right > listRect.Left;
                if (above && aligned && rect.Height <= 44 &&
                    rect.Width >= 80)
                {
                    edit = candidate;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (edit != null)
            {
                _trackedNativeSearch = edit;
                edit.TextChanged += OnSearchFilterChanged;
            }
        }
    }

    private static void OnSearchFilterChanged(object? sender, EventArgs e)
    {
        // REV68: reject deferred click commits from a previous query.
        // Search is a view change, NOT an uncheck action.
        _selectionEpoch++;
        _lastNativeRowSignature = "";
        _lastRestoredRowSignature = "";
        _stableNativePolls = 0;
        _settledActiveRoute = "";
        CancelActiveCommit();
        // A manual search after the scope transition is a new explicit
        // operation and may use the existing manual-filter reconciliation.
        if (!_scopeSwitchPending) _suspendPeriodicNativeWrites = false;
        // A manual search may interrupt the Show All recovery. Never
        // hold the native list in BeginUpdate after that interaction.
        if (!_scopeSwitchPending) EndProjectRedrawBatch();
        // REV72: a manual query change resumes the regular selection
        // tracker. Active-only idle mode must never affect a new search.
        if (_countTimer is { Enabled: false }) _countTimer.Start();
        _gestureIndex = -1;
        _gestureTag = "";
        _gestureBefore = null;
        _gestureCheckboxAction = false;
    }

    public static void BeginScopeSwitch(IntPtr dialog)
    {
        if (_dialog != dialog || dialog == IntPtr.Zero) return;
        _scopeSwitchPending = true;
        _suspendPeriodicNativeWrites = true;
        RefreshScopeSelectAll();
        _settledActiveRoute = "";
        CancelActiveCommit();
        // Re-enable regular monitoring before the All/Current button
        // changes the native checklist.
        if (_countTimer is { Enabled: false }) _countTimer.Start();
        // REV71: abort any obsolete recovery window when changing scope.
        // Active mode will be finalized directly when its exact native
        // route is verified, not via 3 x 350ms repaint polling.
        try { _modeRecoveryTimer?.Stop(); _modeRecoveryTimer?.Dispose(); }
        catch (Exception) { }
        _modeRecoveryTimer = null;
        _modeRecoverySerial++;
        _selectionEpoch++;
        _gestureIndex = -1;
        _gestureTag = "";
        _gestureBefore = null;
        _gestureCheckboxAction = false;
        _lastNativeRowSignature = "";
        _lastRestoredRowSignature = "";
        _stableNativePolls = 0;
        // A deferred Select All first-row retry belongs to the OLD list;
        // cancel it before Autodesk replaces its native line rows.
        try { _firstRowRetry?.Stop(); _firstRowRetry?.Dispose(); }
        catch (Exception) { }
        _firstRowRetry = null;
    }

    public static void CancelScopeSwitch(IntPtr dialog)
    {
        if (_dialog != dialog || dialog == IntPtr.Zero) return;
        _scopeSwitchPending = false;
        _settledActiveRoute = "";
        CancelActiveCommit();
        EndProjectRedrawBatch();
        if (_countTimer is { Enabled: false }) _countTimer.Start();
        _lastNativeRowSignature = "";
        _stableNativePolls = 0;
    }

    // REV71: single-shot fast path for the Active-DWG view. The exact
    // filter already verified the original native Autodesk list has ONE
    // matching tag, so no second stabilization timer is needed. This
    // method NEVER creates a fake row, scans files or changes the ledger.
    public static void CompleteActiveScopeAfterExactFilter(
        IntPtr dialog, string exactTag)
    {
        if (dialog == IntPtr.Zero || dialog != _dialog ||
            !_scopeSwitchPending ||
            ProjectFolderLinePicker.HasVisibleProjection ||
            string.IsNullOrWhiteSpace(exactTag) ||
            !ActiveDwgTags.IsNativeFilterCurrent(dialog))
            return;

        CheckedListBox? list = FindNativeCheckedList(dialog);
        if (list is null || list.IsDisposed || list.Items.Count != 1 ||
            !list.GetItemText(list.Items[0]).Trim().Equals(
                exactTag, StringComparison.OrdinalIgnoreCase))
            return;

        // REV73: do NOT immediately check an Autodesk item while the
        // native Filter may still be committing its own checkbox/index
        // state. REV72 latched that premature checkbox value and then
        // stopped monitoring, leaving A-08 unchecked and Create disabled.
        // Let the native list settle, then do at most ONE guarded native
        // checkbox click, never a periodic check/uncheck loop.
        _settledActiveRoute = exactTag;
        _scopeSwitchPending = false;
        _lastNativeRowSignature = "";
        _stableNativePolls = 0;
        // Refresh the visible count just ONCE, then stop the 800ms
        // periodic enumeration and state reconciliation while the
        // verified single Active-DWG row stays on-screen. Real mouse/
        // keyboard check events update Selected: N immediately.
        UpdateDisplayedCount();
        _countTimer?.Stop();
        ScheduleFinalActiveCheckboxCommit(dialog, exactTag);
    }

    // REV73: SINGLE deferred native check for an exact, user-selected
    // current-DWG tag. Existing Autodesk control and Click handlers remain
    // authoritative; never assign Create.Enabled or invent a route row.
    // REV76: Autodesk can invalidate a checked native item after the
    // exact Current Drawing filter appears. A single early click (REV73)
    // may be lost, leaving "Selected: N" correct while its original
    // checkbox is unchecked and Autodesk's Create button stays disabled.
    // Verify AFTER committing, permit one bounded corrective native
    // event if necessary, and never enable Create directly.
    private static void ScheduleFinalActiveCheckboxCommit(
        IntPtr dialog, string exactTag)
    {
        CancelActiveCommit();
        int serial = _activeCommitSerial;
        int generation = _generation;
        int samples = 0;
        int ticks = 0;
        int checkAttempts = 0;
        bool eligibilityReplayed = false;
        bool awaitingNativeCommit = false;
        string previousName = "";
        bool previousChecked = false;
        object? previousRow = null;

        var timer = new System.Windows.Forms.Timer { Interval = 180 };
        _activeCommitTimer = timer;
        void Stop()
        {
            try { timer.Stop(); timer.Dispose(); }
            catch (Exception) { }
            if (ReferenceEquals(_activeCommitTimer, timer))
                _activeCommitTimer = null;
        }
        timer.Tick += (_, _) =>
        {
            if (_dialog != dialog || _generation != generation ||
                serial != _activeCommitSerial ||
                !ActiveDwgTags.IsNativeFilterCurrent(dialog) ||
                ProjectFolderLinePicker.HasVisibleProjection)
            {
                Stop();
                return;
            }
            if (++ticks > 14)
            {
                WriteDiagnostic("REV76: " + exactTag +
                    " native check validation timed out; " +
                    "use All Project if Create is disabled.");
                Stop();
                return;
            }

            CheckedListBox? list = FindNativeCheckedList(dialog);
            if (list is null || list.IsDisposed || !list.IsHandleCreated ||
                list.Items.Count != 1)
                return;
            object row = list.Items[0]!;
            string name = list.GetItemText(row).Trim();
            if (!name.Equals(exactTag, StringComparison.OrdinalIgnoreCase))
                return;
            bool checkedNow = list.GetItemChecked(0);

            // Native TextChanged and Items.RemoveAt can briefly replace
            // the same-looking route row. Require two successive samples
            // of the same ORIGINAL row and checked state before clicking.
            if (!ReferenceEquals(row, previousRow) ||
                !name.Equals(previousName, StringComparison.Ordinal) ||
                checkedNow != previousChecked)
            {
                previousRow = row;
                previousName = name;
                previousChecked = checkedNow;
                samples = 0;
                return;
            }
            if (++samples < 2) return;

            bool desired = SessionSelectedTags.Contains(exactTag);
            Button? create = FindNativeCreateButton(dialog);
            if (!desired)
            {
                // A route not selected by the user must stay unchecked.
                // Never turn on a line just to enable Autodesk Create.
                WriteDiagnostic("REV76: active " + exactTag +
                    " has no saved selection; native checked=" + checkedNow);
                Stop();
                return;
            }

            if (checkedNow && create?.Enabled == true)
            {
                WriteDiagnostic("REV76: active " + exactTag +
                    " verified checked=True, Create=True.");
                Stop();
                return;
            }

            if (awaitingNativeCommit)
            {
                // The prior click was not accepted into Autodesk's
                // checked selection or Create eligibility; one further
                // native correction is allowed, not an endless blink.
                awaitingNativeCommit = false;
            }

            if (!checkedNow && checkAttempts < 2)
            {
                checkAttempts++;
                _restoringSelection = true;
                _scopeSwitchPending = true;
                _selectionEpoch++;
                try
                {
                    ClickNativeCheckedRow(list, 0, exactTag);
                }
                catch (Exception ex)
                {
                    WriteDiagnostic("REV76 native click: " +
                        ex.GetBaseException().Message);
                }
                finally
                {
                    _restoringSelection = false;
                    _scopeSwitchPending = false;
                    _selectionEpoch++;
                }
                awaitingNativeCommit = true;
                previousChecked = list.GetItemChecked(0);
                samples = 0;
                WriteDiagnostic("REV76: active " + exactTag +
                    " check attempt " + checkAttempts +
                    ", checked=" + previousChecked +
                    ", Create=" +
                    (FindNativeCreateButton(dialog)?.Enabled.ToString() ??
                     "unknown"));
                return;
            }

            if (checkedNow && create is { Enabled: false } &&
                !eligibilityReplayed)
            {
                // A visible check icon without native Create eligibility
                // needs an actual Autodesk ItemCheck/Click transition.
                // Replay it only ONCE for this exact tagged item.
                eligibilityReplayed = true;
                _restoringSelection = true;
                _scopeSwitchPending = true;
                _selectionEpoch++;
                try
                {
                    list.SetItemChecked(0, false);
                    if (list.Items.Count == 1 &&
                        list.GetItemText(list.Items[0]).Trim().Equals(
                            exactTag, StringComparison.OrdinalIgnoreCase))
                        ClickNativeCheckedRow(list, 0, exactTag);
                }
                catch (Exception ex)
                {
                    WriteDiagnostic("REV76 Create validation: " +
                        ex.GetBaseException().Message);
                }
                finally
                {
                    _restoringSelection = false;
                    _scopeSwitchPending = false;
                    _selectionEpoch++;
                }
                previousChecked = list.GetItemChecked(0);
                samples = 0;
                awaitingNativeCommit = true;
                return;
            }

            if (ticks >= 8 || (checkAttempts >= 2 && !checkedNow) ||
                (eligibilityReplayed && checkedNow))
            {
                WriteDiagnostic("REV76 final active " + exactTag +
                    ": native checked=" + checkedNow +
                    ", Create=" + (create?.Enabled.ToString() ?? "unknown") +
                    ". No Create override.");
                Stop();
            }
        };
        timer.Start();
    }

    // REV77: exactly verified active line is presented by a native-backed
    // projection WITHOUT deleting/reindexing Autodesk checklist entries.
    // No active checkbox click timers are required: projection delegates
    // a genuine user click to the original Autodesk row's existing index.
    public static bool IsSessionTagSelected(string tag) =>
        !string.IsNullOrWhiteSpace(tag) && SessionSelectedTags.Contains(tag);

    public static void CompleteActiveProjectionScope(
        IntPtr dialog, string exactTag)
    {
        if (_dialog != dialog || dialog == IntPtr.Zero ||
            !ProjectFolderLinePicker.ActiveDwgView ||
            !ProjectFolderLinePicker.HasVisibleProjection ||
            string.IsNullOrWhiteSpace(exactTag))
            return;

        CancelActiveCommit();
        _scopeSwitchPending = false;
        _settledActiveRoute = "";
        _lastNativeRowSignature = "";
        _stableNativePolls = 0;
        _countTimer?.Stop();
        UpdateDisplayedCount();
        WriteDiagnostic("REV77: native-backed exact active view " +
            exactTag + "; original checklist row indices preserved.");
    }

    public static void ResumeNativeSelectionPolling(IntPtr dialog)
    {
        if (_dialog != dialog || dialog == IntPtr.Zero) return;
        _settledActiveRoute = "";
        CancelActiveCommit();
        if (_countTimer is { Enabled: false }) _countTimer.Start();
    }

    private static void BeginUserGesture(CheckedListBox list, int index)
    {
        // Never classify the guarded SPTC native commit as a USER gesture.
        if (_restoringSelection || _scopeSwitchPending) return;
        CancelActiveCommit();
        if (!ReferenceEquals(_trackedNativeList, list) ||
            index < 0 || index >= list.Items.Count) return;
        _gestureIndex = index;
        _gestureTag = list.GetItemText(list.Items[index]).Trim();
        _gestureCheckboxAction = false;
        _gestureBefore = new Dictionary<string, bool>(
            StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < list.Items.Count; i++)
        {
            string tag = list.GetItemText(list.Items[i]).Trim();
            if (tag.Length > 0)
                _gestureBefore[tag] = list.GetItemChecked(i);
        }
    }

    private static void OnNativeLineMouseDown(
        object? sender, MouseEventArgs e)
    {
        if (sender is not CheckedListBox list ||
            e.Button != MouseButtons.Left) return;
        BeginUserGesture(list, list.IndexFromPoint(e.Location));
        if (_gestureIndex >= 0 && e.X >= 0 && e.X <= 27)
            _gestureCheckboxAction = true;
    }

    private static void OnNativeLineKeyDown(
        object? sender, KeyEventArgs e)
    {
        if (sender is CheckedListBox list &&
            e.KeyCode == Keys.Space)
        {
            BeginUserGesture(list, list.SelectedIndex);
            if (_gestureIndex >= 0) _gestureCheckboxAction = true;
        }
    }

    private static void OnNativeLineItemCheck(
        object? sender, ItemCheckEventArgs e)
    {
        if (_restoringSelection || _scopeSwitchPending ||
            sender is not CheckedListBox list ||
            !ReferenceEquals(list, _trackedNativeList) ||
            _gestureIndex < 0 ||
            e.Index < 0 || e.Index >= list.Items.Count ||
            ProjectFolderLinePicker.HasVisibleProjection)
            return;

        // A single Shift-click can fire ItemCheck for five different
        // indices. Treat ALL these native events as one user gesture.
        string tag = list.GetItemText(list.Items[e.Index]).Trim();
        if (tag.Length == 0) return;

        if (e.NewValue == CheckState.Checked)
            SessionSelectedTags.Add(tag);
        else if (e.NewValue == CheckState.Unchecked)
            SessionSelectedTags.Remove(tag);
    }

    private static void CommitUserGesture(CheckedListBox list)
    {
        int clicked = _gestureIndex;
        string clickedTag = _gestureTag;
        Dictionary<string, bool>? before = _gestureBefore;
        bool checkboxAction = _gestureCheckboxAction;
        int commitEpoch = _selectionEpoch;
        // End the active gesture NOW: any deferred native checkbox refresh
        // must never be classified as a new user check/uncheck.
        _gestureIndex = -1;
        _gestureTag = "";
        _gestureBefore = null;
        _gestureCheckboxAction = false;
        if (clicked < 0) return;

        // Autodesk can commit Shift-range check state after MouseUp.
        // Compare the checked state of every VISIBLE tag against its
        // pre-click state. Update the saved ledger only for real deltas.
        try
        {
            list.BeginInvoke((MethodInvoker)(() =>
            {
                if (!ReferenceEquals(_trackedNativeList, list) ||
                    list.IsDisposed || _scopeSwitchPending ||
                    commitEpoch != _selectionEpoch ||
                    ProjectFolderLinePicker.HasVisibleProjection)
                    return;

                for (int i = 0; i < list.Items.Count; i++)
                {
                    string tag = list.GetItemText(list.Items[i]).Trim();
                    if (tag.Length == 0 || before == null ||
                        !before.TryGetValue(tag, out bool wasChecked))
                        continue;
                    bool nowChecked = list.GetItemChecked(i);
                    if (nowChecked == wasChecked) continue;
                    if (nowChecked) SessionSelectedTags.Add(tag);
                    else SessionSelectedTags.Remove(tag);
                }

                // The clicked row is always an explicitly initiated user
                // action; capture its final committed state even if native
                // handlers fired before our MouseDown event registration.
                if (clicked < list.Items.Count &&
                    list.GetItemText(list.Items[clicked]).Trim().Equals(
                        clickedTag, StringComparison.OrdinalIgnoreCase))
                {
                    if (list.GetItemChecked(clicked))
                        SessionSelectedTags.Add(clickedTag);
                    else
                        SessionSelectedTags.Remove(clickedTag);
                }
                // REV76: if Autodesk swallowed a REAL user's check
                // of the only active native route, use one checked-row
                // event on the SAME route. Never act on a row highlight,
                // Search/Scope refresh, or a user-initiated uncheck.
                if (checkboxAction && clicked == 0 &&
                    list.Items.Count == 1 &&
                    ActiveDwgTags.IsNativeFilterCurrent(_dialog) &&
                    before != null &&
                    before.TryGetValue(clickedTag, out bool previouslyChecked) &&
                    !previouslyChecked && !list.GetItemChecked(0) &&
                    list.GetItemText(list.Items[0]).Trim().Equals(
                        clickedTag, StringComparison.OrdinalIgnoreCase))
                {
                    _restoringSelection = true;
                    _scopeSwitchPending = true;
                    _selectionEpoch++;
                    try
                    {
                        ClickNativeCheckedRow(list, 0, clickedTag);
                    }
                    catch (Exception ex)
                    {
                        WriteDiagnostic("REV76 manual active click: " +
                            ex.GetBaseException().Message);
                    }
                    finally
                    {
                        _restoringSelection = false;
                        _scopeSwitchPending = false;
                        _selectionEpoch++;
                    }
                    if (list.Items.Count == 1 &&
                        list.GetItemChecked(0))
                        SessionSelectedTags.Add(clickedTag);
                    else
                        WriteDiagnostic("REV76: native active route " +
                            clickedTag + " rejected checkbox click.");
                }

                _lastNativeRowSignature = "";
                _stableNativePolls = 0;
                UpdateSelectedLabel();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private static void OnNativeLineMouseUp(
        object? sender, MouseEventArgs e)
    {
        if (sender is CheckedListBox list &&
            e.Button == MouseButtons.Left)
            CommitUserGesture(list);
    }

    private static void OnNativeLineKeyUp(
        object? sender, KeyEventArgs e)
    {
        if (sender is CheckedListBox list &&
            e.KeyCode == Keys.Space)
            CommitUserGesture(list);
    }

    public static void RecordBulkNativeSelection(CheckedListBox list)
    {
        EnsureSelectionTracking();
        // Called AFTER a user-selected Select All/Clear All operation has
        // committed to the real native list. Only change route tags that
        // actually appear in the current filter; keep hidden selections.
        if (list.IsDisposed || _scopeSwitchPending ||
            !ReferenceEquals(_trackedNativeList, list))
            return;
        // REV92: native checkboxes outside the visible Folder/Active
        // projection are deliberately cleared for safe Create eligibility.
        // They are NOT a user uncheck and must not erase saved selections
        // belonging to another filter.
        var scoped = ProjectFolderLinePicker.HasVisibleProjection
            ? ProjectFolderLinePicker.GetVisibleTagNames()
            : null;
        for (int i = 0; i < list.Items.Count; i++)
        {
            string tag = list.GetItemText(list.Items[i]).Trim();
            if (tag.Length == 0 ||
                (scoped != null && !scoped.Contains(tag))) continue;
            if (list.GetItemChecked(i))
                SessionSelectedTags.Add(tag);
            else
                SessionSelectedTags.Remove(tag);
        }
        UpdateSelectedLabel();
    }

    public static void RestrictSessionSelectionTo(
        IEnumerable<string> visibleFolderTags)
    {
        // Folder mode intentionally clears outside-folder native checks.
        // Match that safety behavior instead of resurrecting old hidden
        // routes when the user switches back to All Project.
        SessionSelectedTags.IntersectWith(visibleFolderTags);
        UpdateSelectedLabel();
    }

    private static void UpdateSelectedLabel()
    {
        if (_selectedCountLabel is { IsDisposed: false } label)
        {
            string caption = "Selected: " + SessionSelectedTags.Count;
            if (label.Text != caption)
                label.Text = caption;
        }
        RefreshScopeSelectAll();
    }

    // REV92: derived Select All presentation, never an implied selection.
    // Suppress CheckedChanged so moving between filters does not call the
    // bulk selection handler. Only the user's click changes selected tags.
    private static void RefreshScopeSelectAll()
    {
        if (_toggle is not { IsDisposed: false } toggle ||
            _dialog == IntPtr.Zero)
            return;

        int? visible = ProjectFolderLinePicker.VisibleLineCount ??
                       ReadNativeLineCount(_dialog);
        int? checkedVisible = ProjectFolderLinePicker.VisibleCheckedCount ??
                              ReadNativeCheckedCount(_dialog);
        bool scopedAll = !_scopeSwitchPending &&
            visible is > 0 && checkedVisible.HasValue &&
            checkedVisible.Value == visible.Value;
        if (toggle.Checked == scopedAll) return;
        _setting = true;
        try { toggle.Checked = scopedAll; }
        finally { _setting = false; }
    }

    private static void ReconcileVisibleNativeSelection()
    {
        if (_restoringSelection || _scopeSwitchPending ||
            _suspendPeriodicNativeWrites || _gestureIndex >= 0 ||
            ProjectFolderLinePicker.HasVisibleProjection ||
            _trackedNativeList is not { IsDisposed: false } list ||
            list.Items.Count == 0)
            return;

        // REV70: the exact Active-DWG row was already reconciled ONE TIME
        // after Autodesk's list stopped changing. Rewriting its checkbox
        // on each 800ms Selected counter tick causes repeated blinking
        // while the native filter still owns that row. A new manual query,
        // toolbar mode switch or replacement native list resets this latch.
        if (_settledActiveRoute.Length > 0 &&
            ActiveDwgTags.IsNativeFilterCurrent(_dialog) &&
            list.Items.Count == 1 &&
            list.GetItemText(list.Items[0]).Trim().Equals(
                _settledActiveRoute, StringComparison.OrdinalIgnoreCase))
            return;

        // Include the exact native list contents and editable search term.
        // Wait for two matching lightweight polls before restoring. This
        // avoids fighting Autodesk's asynchronous Filter.TextChanged rebuild.
        var signature = new StringBuilder(list.Items.Count * 12);
        signature.Append(_trackedNativeSearch?.Text ?? "").Append('|');
        for (int i = 0; i < list.Items.Count; i++)
            signature.Append(list.GetItemText(list.Items[i])).Append('\u001f');
        string value = signature.ToString();

        if (!value.Equals(_lastNativeRowSignature, StringComparison.Ordinal))
        {
            _lastNativeRowSignature = value;
            _stableNativePolls = 0;
            return;
        }
        if (_stableNativePolls++ < 1)
            return;

        // REV67: Do NOT skip this list forever merely because the text
        // signature is unchanged. Autodesk can later recheck stale INDEX
        // values while the visible names remain identical.
        _restoringSelection = true;
        try
        {
            int firstChecked = -1;
            for (int i = 0; i < list.Items.Count; i++)
            {
                string tag = list.GetItemText(list.Items[i]).Trim();
                bool shouldCheck = SessionSelectedTags.Contains(tag);
                bool isChecked = list.GetItemChecked(i);
                // Enforce BOTH sides of exact tag membership. REV66 only
                // checked remembered tags and left phantom rows selected
                // when Autodesk reused old check indices during searches.
                if (shouldCheck != isChecked)
                    list.SetItemChecked(i, shouldCheck);
                if (firstChecked == -1 && list.GetItemChecked(i))
                    firstChecked = i;
            }
            if (firstChecked < 0 && list.SelectedIndex >= 0)
                list.ClearSelected();
            if (firstChecked >= 0)
            {
                // REV69: do not ClearSelected()+SelectedIndex on EVERY
                // 800ms count tick. That continuous focus oscillation
                // made the one-route Active DWG checkbox visibly blink,
                // despite its checked state and Selected: N being correct.
                // Prime selection at most once when there is NO selected row.
                Button? create = FindNativeCreateButton(_dialog);
                if (create is { Enabled: false } &&
                    list.SelectedIndex < 0)
                    list.SelectedIndex = firstChecked;
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("REV66 restore: " +
                ex.GetBaseException().Message);
        }
        finally { _restoringSelection = false; }
    }

    // In-memory signature of the ORIGINAL native list's tag names,
    // NOT the one-row active/folder projection. Used only for waiting
    // until Autodesk's own All Project refresh has finished.
    public static string NativeRouteNameSignature(IntPtr dialog)
    {
        if (dialog == IntPtr.Zero || dialog != _dialog ||
            ProjectFolderLinePicker.HasVisibleProjection)
            return "";
        CheckedListBox? list = FindNativeCheckedList(dialog);
        if (list == null || list.IsDisposed || list.Items.Count == 0)
            return "";
        var signature = new StringBuilder(list.Items.Count * 18);
        for (int i = 0; i < list.Items.Count; i++)
            signature.Append(list.GetItemText(list.Items[i]).Trim())
                     .Append('\u001f');
        return signature.ToString();
    }

    // REV79: prepare the ORIGINAL Autodesk selection for a single Create
    // click, regardless of the search/Active/Folder view used while the
    // user chose the routes. Never fabricate list items or force Create.
    // Caller first switches to the real All Project source. This routine
    // operates on the existing native WinForms checked rows ONLY.
    // REV80: Autodesk's Create eligibility sometimes updates only after
    // a real native SelectedIndexChanged event. Nudge ONE checked original
    // item once, after all saved tags are verified; never force-enable
    // the Create button, and never synthesize checkbox mouse clicks.
    public static bool TryNudgeNativeBatchCreateEligibility(
        IntPtr dialog, IReadOnlyCollection<string> expectedTags)
    {
        if (_dialog != dialog || dialog == IntPtr.Zero ||
            ProjectFolderLinePicker.HasVisibleProjection)
            return false;
        CheckedListBox? list = FindNativeCheckedList(dialog);
        if (list is null || list.IsDisposed) return false;
        var wanted = new HashSet<string>(expectedTags,
            StringComparer.OrdinalIgnoreCase);
        int index = -1;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (!list.GetItemChecked(i)) continue;
            if (!wanted.Contains(list.GetItemText(list.Items[i]).Trim()))
                return false;
            if (index < 0) index = i;
        }
        if (index < 0) return false;
        try
        {
            // Single focus/selection change, not a repeating checkbox
            // toggle or 432-item reset; preserves exactly checked routes.
            if (list.SelectedIndex == index) list.ClearSelected();
            list.SelectedIndex = index;
            return true;
        }
        catch (Exception ex)
        {
            WriteDiagnostic("REV80 native Create nudge: " +
                ex.GetBaseException().Message);
            return false;
        }
    }

    public static bool TryApplySavedBatchToNative(
        IntPtr dialog, IReadOnlyCollection<string> wantedNames,
        out int matched, out int checkedCount)
    {
        matched = 0;
        checkedCount = 0;
        if (dialog == IntPtr.Zero || dialog != _dialog ||
            ProjectFolderLinePicker.HasVisibleProjection ||
            wantedNames.Count == 0)
            return false;

        CheckedListBox? list = FindNativeCheckedList(dialog);
        if (list == null || list.IsDisposed || list.Items.Count == 0)
            return false;

        var wanted = new HashSet<string>(wantedNames,
            StringComparer.OrdinalIgnoreCase);
        var indices = new List<int>(list.Items.Count);
        var names = new List<string>(list.Items.Count);
        for (int i = 0; i < list.Items.Count; i++)
        {
            string tag = list.GetItemText(list.Items[i]).Trim();
            names.Add(tag);
            if (wanted.Contains(tag))
                indices.Add(i);
        }

        matched = indices.Select(i => names[i])
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (matched != wanted.Count)
            return false;  // native source not ready; do not create subset

        _restoringSelection = true;
        try
        {
            // Clear ONLY extras checked by an old filtered/indexed view.
            // User intent in wantedNames always remains authoritative.
            // REV83: only clear unintended checks here. For requested
            // unchecked items use the genuine Autodesk mouse event path
            // below, so the native model receives the selection.
            for (int i = 0; i < list.Items.Count; i++)
                if (!wanted.Contains(names[i]) && list.GetItemChecked(i))
                    list.SetItemChecked(i, false);
            // Remaining missing checks are committed using the real
            // native click path before counting, below.

            // REV83: native Autodesk state can reset a formerly-active
            // projected route after clearing the exact current-DWG filter.
            // A SetItemChecked checkmark alone does not always commit to
            // Autodesk's underlying selection. Re-commit only any missing
            // requested route through the same original native mouse path
            // used by individual folder/active selection. Never change
            // rows not explicitly requested or allow a partial Create.
            // REV94: avoid six synchronous native mouse events in one UI
            // turn. Autodesk may asynchronously discard the Active route
            // check while handling later clicks (the REV80-REV93 5/6
            // failure). Commit at most ONE missing route per 180ms caller
            // tick; return incomplete until the exact full set is stable.
            foreach (int index in indices)
            {
                if (list.GetItemChecked(index)) continue;
                if (index < 0 || index >= list.Items.Count ||
                    !list.GetItemText(list.Items[index]).Trim().Equals(
                        names[index], StringComparison.OrdinalIgnoreCase))
                    return false;
                bool clicked = ProjectFolderLinePicker
                    .ClickNativeCheckedRow(list, index, true);
                WriteDiagnostic("REV94 staged native route commit: " +
                    names[index] + ", clicked=" + clicked +
                    ", checked=" + list.GetItemChecked(index));
                break;
            }
            // The exact final result is checked again on later timer ticks.
            checkedCount = 0;
            foreach (int i in indices)
                if (list.GetItemChecked(i)) checkedCount++;
            if (checkedCount != wanted.Count) return false;

            // A focused checked row can trigger Autodesk's own native
            // Create eligibility refresh without fake checkbox clicks.
            if (list.SelectedIndex < 0 && indices.Count > 0)
                list.SelectedIndex = indices[0];

            return true;
        }
        catch (Exception ex)
        {
            WriteDiagnostic("REV79: native batch restore failed: " +
                ex.GetBaseException().Message);
            return false;
        }
        finally { _restoringSelection = false; }
    }

    public static string[] CaptureSelectedTags(IntPtr dialog)
    {
        if (dialog != _dialog || dialog == IntPtr.Zero)
            return Array.Empty<string>();
        EnsureSelectionTracking();
        // REV67: never re-import current UI checks on a mode switch.
        // Current UI may have temporary phantom checkboxes after search
        // rebuild. The tag ledger changes ONLY on a user gesture or an
        // explicit verified Select All/Folder operation.
        return SessionSelectedTags.ToArray();
    }

    // REV86: one controlled restoration, never a repeated 432-item write.
    // Autodesk may replace checked-row indices after native Filter.TextChanged.
    // Wait until the full Project checklist is stable, perform one native
    // commit, then ONLY read to verify. If it fails, leave the dialog open
    // and report the issue without blinking the checkbox indefinitely.
    public static void ScheduleModeChangeRecovery(
        IntPtr dialog, IEnumerable<string> selectedBefore)
    {
        if (dialog == IntPtr.Zero || dialog != _dialog) return;

        BeginScopeSwitch(dialog);
        foreach (string name in selectedBefore)
            if (!string.IsNullOrWhiteSpace(name))
                SessionSelectedTags.Add(name);
        var expected = new HashSet<string>(
            SessionSelectedTags, StringComparer.OrdinalIgnoreCase);
        int requiredRows = _lastProjectWideRowCount;

        try { _modeRecoveryTimer?.Stop(); _modeRecoveryTimer?.Dispose(); }
        catch (Exception) { }
        int serial = ++_modeRecoverySerial;
        int generation = _generation;
        int ticks = 0;
        int stable = 0;
        bool redrawReleased = false;
        bool readinessLogged = false;
        int lastObservedRows = 0;
        int confirmed = 0;
        int readsAfterCommit = 0;
        int nativeRebuildRetries = 0;
        const int maxNativeRebuildRetries = 2;
        // REV90: Autodesk can reset the just-checked A-08 when its
        // background route list update finishes. Permit only two
        // controlled recovery attempts, never a repeating checkbox loop.
        // REV87: after Active -> All, Autodesk can show a checked A-08
        // without enabling native Create. The checked icon alone is not
        // proof that the native Create eligibility event was received.
        bool createFocusNudged = false;
        bool createEligibilityReplayed = false;
        int eligibilityValidationPolls = 0;
        bool committed = false;
        string lastNames = "";
        var timer = new System.Windows.Forms.Timer { Interval = 140 };
        _modeRecoveryTimer = timer;

        void Finish(string? reason = null)
        {
            try { timer.Stop(); timer.Dispose(); }
            catch (Exception) { }
            if (ReferenceEquals(_modeRecoveryTimer, timer))
                _modeRecoveryTimer = null;
            if (_dialog != dialog || _generation != generation ||
                serial != _modeRecoverySerial)
                return;
            _scopeSwitchPending = false;
            // REV86 intentionally keeps _suspendPeriodicNativeWrites=true
            // until a new manual search/scope action. Otherwise the 800ms
            // counter immediately recreates the original checkbox blink.
            _lastNativeRowSignature = "";
            _lastRestoredRowSignature = "";
            _stableNativePolls = 0;
            EndProjectRedrawBatch();
            UpdateDisplayedCount();
            if (!string.IsNullOrEmpty(reason))
                WriteDiagnostic("REV90: " + reason);
        }

        timer.Tick += (_, _) =>
        {
            if (_dialog != dialog || _generation != generation ||
                serial != _modeRecoverySerial)
            {
                Finish();
                return;
            }
            // REV88: REV87 stopped after 25 x 140ms (3.5 seconds).
            // Large project lists can take >5 seconds to repopulate.
            // Keep polling asynchronously up to ~12 seconds, but finish
            // immediately as soon as the native list is valid.
            if (++ticks > 86)
            {
                Finish("All Project native list not validated within " +
                    "12 seconds (expected rows=" + requiredRows +
                    ", observed=" + lastObservedRows +
                    "); saved routes retained, Create not forced.");
                return;
            }
            if (ticks == 26 && !readinessLogged)
            {
                readinessLogged = true;
                WriteDiagnostic("REV88: waiting for complete Autodesk " +
                    "All Project list before restoring saved checks.");
            }
            try
            {
                if (ProjectFolderLinePicker.HasVisibleProjection)
                {
                    Finish();
                    return;
                }
                CheckedListBox? list = FindNativeCheckedList(dialog);
                if (list is null || list.IsDisposed || list.Items.Count == 0)
                    return;
                lastObservedRows = list.Items.Count;
                // Even when a native search momentarily contains A-08,
                // the original unfiltered project list is authoritative.
                if (requiredRows > 0 && list.Items.Count < requiredRows)
                    return;

                var names = new StringBuilder();
                var visible = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < list.Items.Count; i++)
                {
                    string tag = list.GetItemText(list.Items[i]).Trim();
                    names.Append(tag).Append('\u001f');
                    visible.Add(tag);
                }
                if (!expected.IsSubsetOf(visible)) return;
                string signature = names.ToString();
                if (!signature.Equals(lastNames, StringComparison.Ordinal))
                {
                    if (committed)
                    {
                        if (nativeRebuildRetries >= maxNativeRebuildRetries)
                        {
                            Finish("Autodesk replaced native rows after " +
                                "bounded REV90 recovery (" +
                                nativeRebuildRetries + " retries).");
                            return;
                        }
                        nativeRebuildRetries++;
                        committed = false;
                        confirmed = 0;
                        readsAfterCommit = 0;
                        createFocusNudged = false;
                        createEligibilityReplayed = false;
                        eligibilityValidationPolls = 0;
                        WriteDiagnostic("REV90: native row collection rebuilt; " +
                            "scheduling one bounded revalidation #" +
                            nativeRebuildRetries + ".");
                    }
                    lastNames = signature;
                    stable = 0;
                    return;
                }
                // REV90: the 432-row names can appear before Autodesk has
                // finished changing their underlying checked state. Wait
                // six identical UI ticks (~0.84s) before first commit.
                if (++stable < 6) return;

                if (!redrawReleased)
                {
                    // REV89: BeginUpdate prevented Autodesk's native
                    // mouse/ItemCheck activation from fully settling.
                    // Re-enable the real checklist BEFORE attempting to
                    // restore checked rows. Keep the scope guard active
                    // to protect the selected route ledger from events.
                    redrawReleased = true;
                    EndProjectRedrawBatch();
                    stable = 0;
                    WriteDiagnostic("REV90: complete native project source " +
                        list.Items.Count + "/" + requiredRows +
                        "; releasing redraw before checkbox commit.");
                    return;
                }

                if (!committed)
                {
                    committed = true;
                    int changed = 0;
                    _restoringSelection = true;
                    try
                    {
                        for (int i = 0; i < list.Items.Count; i++)
                        {
                            string tag = list.GetItemText(list.Items[i]).Trim();
                            bool wanted = expected.Contains(tag);
                            if (list.GetItemChecked(i) == wanted) continue;
                            changed++;
                            if (wanted)
                                ProjectFolderLinePicker.ClickNativeCheckedRow(
                                    list, i, true);
                            else list.SetItemChecked(i, false);
                        }
                    }
                    finally { _restoringSelection = false; }
                    WriteDiagnostic("REV90: Show All native commit #" +
                        (nativeRebuildRetries + 1) + "; " +
                        "changed=" + changed + ", expected=" + expected.Count +
                        ", rows=" + list.Items.Count + ", actual checked=" +
                        string.Join(", ", Enumerable.Range(0, list.Items.Count)
                            .Where(i => list.GetItemChecked(i))
                            .Select(i => list.GetItemText(list.Items[i]).Trim())) + ".");
                    return;
                }

                // Read-only after commit: do not fight Autodesk timers.
                readsAfterCommit++;
                bool exact = true;
                string[] missing = Array.Empty<string>();
                var checkedNames = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < list.Items.Count; i++)
                {
                    string tag = list.GetItemText(list.Items[i]).Trim();
                    bool check = list.GetItemChecked(i);
                    if (check) checkedNames.Add(tag);
                    if (check != expected.Contains(tag)) exact = false;
                }
                if (!exact)
                {
                    confirmed = 0;
                    if (readsAfterCommit >= 5)
                    {
                        missing = expected.Where(t => !checkedNames.Contains(t))
                            .ToArray();
                        if (nativeRebuildRetries < maxNativeRebuildRetries)
                        {
                            nativeRebuildRetries++;
                            committed = false;
                            stable = 0;
                            readsAfterCommit = 0;
                            createFocusNudged = false;
                            createEligibilityReplayed = false;
                            eligibilityValidationPolls = 0;
                            WriteDiagnostic("REV90: Autodesk reset checked " +
                                "state; missing=" + string.Join(", ", missing) +
                                ". Bounded retry #" + nativeRebuildRetries +
                                ", no partial Create.");
                            return;
                        }
                        Finish("Autodesk reset checked state after REV90 " +
                            "bounded retries; missing=" +
                            string.Join(", ", missing) + ".");
                    }
                    return;
                }
                if (++confirmed < 3) return;

                // REV89: native redraw was already released before checkbox
                // commit; Autodesk has now had the chance to process real
                // ItemCheck/Click events and Create eligibility updates.
                // REV87: require Autodesk's own Create eligibility to
                // recognize the saved checked routes. Do not set
                // Create.Enabled, fabricate routes, or submit an ISO here.
                Button? nativeCreate = FindNativeCreateButton(dialog);
                if (nativeCreate is null)
                {
                    Finish("native Create button unavailable after Show All.");
                    return;
                }
                if (!nativeCreate.Enabled && expected.Count > 0)
                {
                    int selectedRow = -1;
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        string tag = list.GetItemText(list.Items[i]).Trim();
                        if (expected.Contains(tag) && list.GetItemChecked(i))
                        {
                            selectedRow = i;
                            break;
                        }
                    }
                    if (selectedRow < 0)
                    {
                        Finish("no saved checked route available to validate Create.");
                        return;
                    }

                    if (!createFocusNudged)
                    {
                        // First try only the original Autodesk selection
                        // event. No checkbox state changes or blink.
                        createFocusNudged = true;
                        if (list.SelectedIndex == selectedRow)
                            list.ClearSelected();
                        list.SelectedIndex = selectedRow;
                        WriteDiagnostic("REV87: Show All native Create " +
                            "eligibility focus nudge: " +
                            list.GetItemText(list.Items[selectedRow]).Trim() +
                            ", Create=" + nativeCreate.Enabled + ".");
                        return;
                    }

                    if (!createEligibilityReplayed)
                    {
                        // Native item checkbox is checked, yet Autodesk
                        // still rejects Create. Replay exactly ONE genuine
                        // native off/on click sequence while BeginUpdate
                        // is active; preserve all other selected routes.
                        createEligibilityReplayed = true;
                        string tag = list.GetItemText(
                            list.Items[selectedRow]).Trim();
                        bool off = false;
                        bool on = false;
                        _restoringSelection = true;
                        try
                        {
                            off = ProjectFolderLinePicker.ClickNativeCheckedRow(
                                list, selectedRow, false);
                            if (off && selectedRow < list.Items.Count &&
                                list.GetItemText(list.Items[selectedRow])
                                    .Trim().Equals(tag,
                                        StringComparison.OrdinalIgnoreCase))
                                on = ProjectFolderLinePicker.ClickNativeCheckedRow(
                                    list, selectedRow, true);
                        }
                        finally { _restoringSelection = false; }
                        WriteDiagnostic("REV87: one-time native Create " +
                            "checkbox event: " + tag +
                            ", off=" + off + ", on=" + on +
                            ", Create=" + nativeCreate.Enabled + ".");
                        confirmed = 0;
                        return;
                    }

                    // Allow Autodesk's asynchronous handlers to settle,
                    // but NEVER toggle the checkbox a second time.
                    if (++eligibilityValidationPolls < 5)
                        return;
                    Finish("native Create remained disabled after the " +
                        "REV87 single eligibility replay; saved routes " +
                        "retained and no ISO was submitted.");
                    return;
                }
                if (!nativeCreate.Enabled)
                {
                    Finish("native Create remains disabled with no saved routes.");
                    return;
                }
                WriteDiagnostic("REV90: Show All native checked routes " +
                    expected.Count + "/" + expected.Count +
                    ", Create.Enabled=True.");
                Finish();
            }
            catch (Exception ex)
            {
                Finish("selection recovery error: " +
                    ex.GetBaseException().Message);
            }
        };
        timer.Start();
    }

    private static void ClickNativeCheckedRow(
        CheckedListBox list, int index, string expectedTag)
    {
        if (!list.IsHandleCreated || list.IsDisposed ||
            index < 0 || index >= list.Items.Count ||
            list.GetItemChecked(index) ||
            !list.GetItemText(list.Items[index]).Trim().Equals(
                expectedTag, StringComparison.OrdinalIgnoreCase))
            return;

        int originalTop = list.TopIndex;
        try
        {
            list.TopIndex = Math.Min(index, list.Items.Count - 1);
            Rectangle rectangle = list.GetItemRectangle(index);
            if (rectangle.Height <= 0 ||
                list.ClientSize.Width < 22 ||
                rectangle.Top < 0 ||
                rectangle.Bottom > list.ClientSize.Height)
                return;

            if (list.SelectedIndex == index) list.ClearSelected();
            list.SelectedIndex = index;
            int x = Math.Clamp(rectangle.Left + 12, 5,
                list.ClientSize.Width - 6);
            int y = rectangle.Top + rectangle.Height / 2;
            IntPtr point = new(((y & 0xFFFF) << 16) |
                               (x & 0xFFFF));
            SendMessage(list.Handle, WM_LBUTTONDOWN,
                new IntPtr(MK_LBUTTON), point);
            SendMessage(list.Handle, WM_LBUTTONUP, IntPtr.Zero, point);
        }
        finally
        {
            if (!list.IsDisposed && list.Items.Count > 0)
                list.TopIndex = Math.Clamp(originalTop, 0,
                    list.Items.Count - 1);
        }
    }

    private static void ToggleChanged(object? sender, EventArgs e)
    {
        if (_setting || _toggle == null || _dialog == IntPtr.Zero) return;
        bool selected = _toggle.Checked;

        try
        {
            // REV13: Select All applies only to the visible folder routes.
            // Do NOT select lines hidden behind the folder projection.
            if (ProjectFolderLinePicker.HasVisibleProjection)
            {
                // REV23: also cover the exact Active DWG projection. REV22
                // wrongly invoked native Select All for ALL project rows,
                // including hidden old routes when Active-only was shown.
                if (ProjectFolderLinePicker.TrySelectAllVisible(selected))
                    return;
                throw new InvalidOperationException(
                    "The visible line selection is no longer available.");
            }
            // REV42: a manually searched single line must use Autodesk's
            // ORIGINAL native click/ItemCheck sequence, just like the
            // verified one-line Active Route. The generic bulk checkbox setter
            // can show a tick while leaving native Create disabled.
            // Never touch an active folder projection with this path.
            if (!ProjectFolderLinePicker.HasVisibleProjection &&
                TrySelectVisibleSingleNativeLine(_dialog, selected))
                return;
            if (ApplySelection(_dialog, selected))
                return;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not access the native line list:\n" + ex.Message +
                "\n\nAlternative: right-click Line Numbers and use Select all lines / Clear all lines.",
                "SPTC Production Iso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _setting = true;
        try { _toggle.Checked = !selected; }
        finally { _setting = false; }

        MessageBox.Show(
            "This Plant 3D dialog uses a line-list control that REV05-based Select All " +
            "could not identify. The original selection was not intentionally changed.\n\n" +
            "You can right-click inside Line Numbers and choose Select all lines " +
            "or Clear all lines. Please send a screenshot so the checkbox can be connected.",
            "SPTC Production Iso", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // REV42: single checked lines in the native Autodesk result list can
    // require a genuine mouse/selection event before Create is enabled.
    // Works both for an exact Active-DWG route and for a manually typed
    // search result (even when that tag is NOT the active DWG tag).
    private static bool TrySelectVisibleSingleNativeLine(
        IntPtr dialog, bool desired)
    {
        CheckedListBox? native = null;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!IsLeftLineList(dialog, hwnd)) return true;
            if (Control.FromHandle(hwnd) is CheckedListBox list &&
                !list.IsDisposed && list.Items.Count == 1)
            {
                native = list;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (native is null || native.IsDisposed || native.Items.Count != 1)
            return false;

        CheckedListBox listBox = native;
        string tag = listBox.GetItemText(listBox.Items[0]).Trim();
        if (string.IsNullOrWhiteSpace(tag))
            return false;
        int generation = _generation;

        try
        {
            listBox.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
            {
                if (!SameNativeSingleLine(listBox, dialog, generation, tag) ||
                    _toggle is null || _toggle.IsDisposed ||
                    _toggle.Checked != desired)
                    return;

                if (!desired)
                {
                    if (listBox.GetItemChecked(0))
                        listBox.SetItemChecked(0, false);
                    VerifyNativeSingleLine(listBox, dialog, generation, tag, false);
                    return;
                }

                Button? create = FindNativeCreateButton(dialog);
                if (listBox.GetItemChecked(0) && create?.Enabled == true)
                {
                    VerifyNativeSingleLine(listBox, dialog, generation, tag, true);
                    return;
                }

                // An already-checked icon with Create disabled needs a NEW
                // native ItemCheck/Click event. Always uncheck it BEFORE
                // posting the mouse click, never click a checked row.
                if (listBox.GetItemChecked(0))
                    listBox.SetItemChecked(0, false);

                listBox.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                {
                    if (!SameNativeSingleLine(listBox, dialog, generation, tag) ||
                        _toggle is null || _toggle.IsDisposed ||
                        !_toggle.Checked)
                        return;

                    if (!listBox.GetItemChecked(0))
                        ClickVisibleNativeSingleRow(listBox);

                    // Observe the final native checkbox and Create eligibility
                    // on another UI turn. Never force Create.Enabled.
                    listBox.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                        VerifyNativeSingleLine(listBox, dialog, generation, tag, true)));
                }));
            }));
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    private static bool SameNativeSingleLine(
        CheckedListBox list, IntPtr dialog, int generation, string tag) =>
        _generation == generation && _dialog == dialog &&
        !ProjectFolderLinePicker.HasVisibleProjection &&
        !list.IsDisposed && list.Items.Count == 1 &&
        list.GetItemText(list.Items[0]).Trim().Equals(
            tag, StringComparison.OrdinalIgnoreCase);

    private static void ClickVisibleNativeSingleRow(CheckedListBox list)
    {
        if (list.IsDisposed || !list.IsHandleCreated || list.Items.Count != 1 ||
            list.GetItemChecked(0))
            return;

        int savedTop = list.TopIndex;
        try
        {
            list.TopIndex = 0;
            Rectangle rect = list.GetItemRectangle(0);
            if (rect.Height <= 0 || list.ClientSize.Width < 22)
                return;

            // REV41 folder behavior: prime the original selected row BEFORE
            // sending its native ItemCheck/Click messages.
            if (list.SelectedIndex == 0)
                list.ClearSelected();
            list.SelectedIndex = 0;

            int x = Math.Clamp(rect.Left + 12, 5, list.ClientSize.Width - 6);
            int y = rect.Top + rect.Height / 2;
            if (y < 0 || y >= list.ClientSize.Height)
                return;

            IntPtr point = new(((y & 0xFFFF) << 16) | (x & 0xFFFF));
            for (int attempt = 0; attempt < 2 &&
                 !list.GetItemChecked(0); attempt++)
            {
                SendMessage(list.Handle, WM_LBUTTONDOWN,
                    new IntPtr(MK_LBUTTON), point);
                SendMessage(list.Handle, WM_LBUTTONUP,
                    IntPtr.Zero, point);
            }
        }
        finally
        {
            if (!list.IsDisposed && list.Items.Count > 0)
                list.TopIndex = Math.Clamp(savedTop, 0, list.Items.Count - 1);
        }
    }

    private static void VerifyNativeSingleLine(
        CheckedListBox list, IntPtr dialog, int generation,
        string tag, bool desired)
    {
        if (!SameNativeSingleLine(list, dialog, generation, tag))
            return;
        bool actual = list.GetItemChecked(0);
        if (_toggle is { IsDisposed: false } toggle)
        {
            _setting = true;
            try { toggle.Checked = actual; }
            finally { _setting = false; }
        }
        // REV45: Autodesk may keep Create enabled after the last
        // checked native row was cleared programmatically.
        if (!desired && !actual)
            EnsureCreateDisabledForEmptyNativeList(list, dialog, "single-line");
        RecordBulkNativeSelection(list);
        Button? create = FindNativeCreateButton(dialog);
        WriteDiagnostic("REV42 single-line search Select All: " +
            tag + ", desired=" + desired + ", checked=" + actual +
            ", CreateEnabled=" +
            (create is null ? "unknown" : create.Enabled.ToString()));
    }

    private static bool ApplySelection(IntPtr dialog, bool selected)
    {
        // Prefer the actual WinForms control's own CheckedListBox event model.
        // This fires ItemCheck for every line, unlike merely drawing checkmarks.
        CheckedListBox? checkedList = null;
        ListView? listView = null;
        TreeView? treeView = null;
        IntPtr nativeList = IntPtr.Zero;
        int best = -1;
        int nativeCount = -1;

        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!IsLeftLineList(dialog, hwnd)) return true;
            Control? control = Control.FromHandle(hwnd);
            if (control is CheckedListBox box && box.Items.Count > best)
            {
                checkedList = box;
                best = box.Items.Count;
            }
            else if (control is ListView lv && lv.CheckBoxes && lv.Items.Count > best)
            {
                listView = lv;
                best = lv.Items.Count;
            }
            else if (control is TreeView tv && tv.CheckBoxes && tv.Nodes.Count > 0)
            {
                treeView = tv;
            }

            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString().Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
            {
                int count = SendMessage(hwnd, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
                long style = SendMessage(hwnd, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero).ToInt64();
                if (count > nativeCount && count > 0 && (style & LVS_EX_CHECKBOXES) != 0)
                {
                    nativeList = hwnd;
                    nativeCount = count;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (checkedList != null && checkedList.Items.Count > 0)
        {
            // REV36: return to REV33's native SetItemChecked mechanism so
            // manual clicks remain functional. Unlike REV34/35, do NOT send
            // simulated Win32 mouse clicks to the original Autodesk list.
            // Run after the plugin checkbox event has returned.
            CheckedListBox native = checkedList;
            int generation = _generation;
            IntPtr parent = _dialog;
            try
            {
                native.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                {
                    if (_generation != generation || _dialog != parent ||
                        native.IsDisposed || native.Items.Count == 0)
                        return;

                    int expectedCount = native.Items.Count;
                    try
                    {
                        // REV37: prepare native selection BEFORE changing any
                        // checkbox. Autodesk's Create eligibility logic expects
                        // an actual selected row while ItemCheck is firing.
                        // REV36 raised Control.OnClick AFTER checking every
                        // row; that extra click unchecked the FIRST line again.
                        // Do NOT raise OnClick or synthesize mouse messages.
                        if (selected && expectedCount > 0)
                        {
                            if (native.SelectedIndex == 0)
                                native.ClearSelected();
                            native.SelectedIndex = 0;
                        }

                        // REV38: the native context-menu "Select all" left
                        // item zero untouched in the user test (436/437);
                        // in filtered mode item zero is the ONLY line. Do
                        // not invoke an opaque menu action and assume every
                        // row changed. Use verified ORIGINAL native item
                        // states. Process the first row LAST, after the other
                        // ItemCheck handlers have settled.
                        native.BeginUpdate();
                        try
                        {
                            for (int i = 1; i < expectedCount &&
                                i < native.Items.Count; i++)
                                if (native.GetItemChecked(i) != selected)
                                    native.SetItemChecked(i, selected);

                            if (native.Items.Count > 0 &&
                                native.GetItemChecked(0) != selected)
                                native.SetItemChecked(0, selected);
                        }
                        finally { native.EndUpdate(); }
                        bool usedNativeMenu = false;

                        // Count/check states only AFTER all native events.
                        // In REV36 count was calculated BEFORE the extra
                        // OnClick, so the log could incorrectly say all
                        // checked even after the first line became unchecked.
                        int checkedCount = 0;
                        int firstChecked = -1;
                        for (int i = 0; i < native.Items.Count; i++)
                        {
                            if (!native.GetItemChecked(i)) continue;
                            checkedCount++;
                            if (firstChecked < 0) firstChecked = i;
                        }

                        // Native selection-change notification is enough;
                        // never invoke Control.OnClick, which can invert the
                        // first checked item in the Autodesk control.
                        if (selected && firstChecked >= 0 &&
                            native.SelectedIndex != firstChecked)
                            native.SelectedIndex = firstChecked;
                        else if (!selected && checkedCount == 0 &&
                                 native.SelectedIndex >= 0)
                            // REV45: stale selected row should not keep Create active.
                            native.ClearSelected();

                        // The overlay tick follows the *real* line states.
                        // It must not claim success when Autodesk ignored
                        // the selection. Never override Create.Enabled.
                        bool allChecked = native.Items.Count > 0 &&
                            checkedCount == native.Items.Count;
                        if (_toggle != null && !_toggle.IsDisposed &&
                            _toggle.Checked != allChecked)
                        {
                            _setting = true;
                            try { _toggle.Checked = allChecked; }
                            finally { _setting = false; }
                        }

                        // REV39: Autodesk may asynchronously revert row zero
                        // AFTER this callback exits. An immediate state check
                        // misses that later native event. Always verify once
                        // on a deferred UI tick; repair only row zero if it
                        // actually differs. Do not synthesize clicks or alter Create.
                        if (native.Items.Count > 0)
                            ScheduleFirstRowRetry(native, parent, generation, selected);
                        RecordBulkNativeSelection(native);

                        Button? create = FindNativeCreateButton(parent);
                        WriteDiagnostic("REV38 Select All: desired=" + selected +
                            ", checked=" + checkedCount + "/" + native.Items.Count +
                            ", firstRowChecked=" + (native.Items.Count > 0 && native.GetItemChecked(0)) +
                            ", selectedIndex=" + native.SelectedIndex +
                            ", nativeContextMenu=" + usedNativeMenu +
                            ", CreateEnabled=" +
                            (create == null ? "unknown" : create.Enabled.ToString()));
                    }
                    catch (Exception ex)
                    {
                        WriteDiagnostic("REV36 select-all failed: " +
                            ex.GetBaseException().Message);
                    }
                }));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        if (listView != null && listView.Items.Count > 0)
        {
            listView.BeginUpdate();
            try
            {
                foreach (ListViewItem item in listView.Items) item.Checked = selected;
            }
            finally { listView.EndUpdate(); }
            return true;
        }

        if (treeView != null && treeView.Nodes.Count > 0)
        {
            treeView.BeginUpdate();
            try
            {
                foreach (TreeNode node in treeView.Nodes)
                    SetTreeChecked(node, selected);
            }
            finally { treeView.EndUpdate(); }
            return true;
        }

        if (nativeList != IntPtr.Zero && nativeCount > 0)
        {
            var item = new LVITEM
            {
                state = (selected ? 2u : 1u) << 12,
                stateMask = LVIS_STATEIMAGEMASK
            };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<LVITEM>());
            try
            {
                Marshal.StructureToPtr(item, pointer, false);
                // -1 applies check state to every item, including scrolled-off rows.
                IntPtr result = SendMessage(nativeList, LVM_SETITEMSTATE, new IntPtr(-1), pointer);
                return result != IntPtr.Zero;
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        return false;
    }

    // REV38: fix the first-row-only rejection without synthesizing mouse
    // clicks or forcing the Create button. The retry is canceled whenever
    // Autodesk closes/recreates the native dialog.
    private static void ScheduleFirstRowRetry(
        CheckedListBox native, IntPtr parent, int generation, bool selected)
    {
        try { _firstRowRetry?.Stop(); _firstRowRetry?.Dispose(); }
        catch { }
        // REV67: delayed row-zero retry belongs ONLY to this native
        // filter result. If Autodesk repopulates the list meanwhile,
        // it must not check an unrelated line by the same row INDEX.
        string firstTagAtSchedule = native.Items.Count > 0
            ? native.GetItemText(native.Items[0]).Trim() : "";
        int countAtSchedule = native.Items.Count;
        string searchAtSchedule = _trackedNativeSearch?.Text ?? "";
        var timer = new System.Windows.Forms.Timer { Interval = 160 };
        _firstRowRetry = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (ReferenceEquals(_firstRowRetry, timer)) _firstRowRetry = null;
            if (_generation != generation || _dialog != parent ||
                native.IsDisposed || native.Items.Count == 0 ||
                native.Items.Count != countAtSchedule ||
                !native.GetItemText(native.Items[0]).Trim().Equals(
                    firstTagAtSchedule, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_trackedNativeSearch?.Text ?? "",
                    searchAtSchedule, StringComparison.Ordinal))
                return;
            try
            {
                if (native.GetItemChecked(0) != selected)
                    native.SetItemChecked(0, selected);

                int actual = 0;
                for (int i = 0; i < native.Items.Count; i++)
                    if (native.GetItemChecked(i)) actual++;

                bool allChecked = native.Items.Count > 0 &&
                                  actual == native.Items.Count;
                // REV45: after the deferred native checkbox events have
                // committed, disable Create if the real list is empty.
                if (!selected && actual == 0)
                    EnsureCreateDisabledForEmptyNativeList(
                        native, parent, "all-project/search");
                if (_toggle is { IsDisposed: false } toggle)
                {
                    _setting = true;
                    try { toggle.Checked = allChecked; }
                    finally { _setting = false; }
                }
                RecordBulkNativeSelection(native);
                Button? create = FindNativeCreateButton(parent);
                WriteDiagnostic("REV38 first-row retry: checked=" +
                    native.GetItemChecked(0) + ", total=" +
                    actual + "/" + native.Items.Count +
                    ", CreateEnabled=" +
                    (create is null ? "unknown" : create.Enabled.ToString()));
            }
            catch (Exception ex)
            {
                WriteDiagnostic("REV38 first-row retry failed: " +
                    ex.GetBaseException().Message);
            }
        };
        timer.Start();
    }

    // REV45: safety-only reconciliation for Autodesk's stale Create state
    // following programmatic Select All -> Clear All (including manual
    // search and Active-DWG one-line filtering). Original native checklist
    // is the sole source of truth. Never enable Create here.
    // Folder projection is excluded because it already works correctly.
    private static void EnsureCreateDisabledForEmptyNativeList(
        CheckedListBox native, IntPtr dialog, string source)
    {
        if (_dialog != dialog || native.IsDisposed ||
            ProjectFolderLinePicker.HasVisibleProjection ||
            native.Items.Count == 0)
            return;

        for (int i = 0; i < native.Items.Count; i++)
        {
            if (native.GetItemChecked(i))
                return;
        }

        try
        {
            // The original Autodesk handler may already disable Create when
            // the stale row selection is cleared. Do this before fallback.
            if (native.SelectedIndex >= 0)
                native.ClearSelected();

            Button? create = FindNativeCreateButton(dialog);
            if (create is null || create.IsDisposed)
                return;

            bool staleEnabled = create.Enabled;
            if (staleEnabled)
                create.Enabled = false; // disable-only; never bypass native validation

            WriteDiagnostic("REV45 zero native lines checked (" + source +
                "); CreateEnabled=" + create.Enabled +
                "; staleEnabledCorrected=" + staleEnabled);
        }
        catch (Exception ex)
        {
            WriteDiagnostic("REV45 empty-selection state: " +
                ex.GetBaseException().Message);
        }
    }

    // Context menu is an optional native path; only the exact native
    // select/clear commands are eligible. Do NOT fabricate or force actions.
    private static bool TryNativeSelectAllMenu(
        CheckedListBox native, bool selected)
    {
        ContextMenuStrip? menu = native.ContextMenuStrip;
        if (menu == null) return false;
        foreach (ToolStripItem item in menu.Items)
        {
            string caption = item.Text.Replace("&", "").Trim();
            string normalized = caption.ToLowerInvariant();
            bool match = selected
                ? normalized == "select all" ||
                  normalized == "select all lines" ||
                  normalized == "check all" ||
                  normalized == "check all lines"
                : normalized == "clear all" ||
                  normalized == "clear all lines" ||
                  normalized == "deselect all" ||
                  normalized == "uncheck all" ||
                  normalized == "uncheck all lines";
            if (!match || !item.Enabled || !item.Available)
                continue;
            item.PerformClick();
            return true;
        }
        return false;
    }

    private static Button? FindNativeCreateButton(IntPtr dialog)
    {
        Button? create = null;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is Button b &&
                b.Text.Trim().Equals("Create", StringComparison.OrdinalIgnoreCase))
            {
                create = b;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return create;
    }

    private static void WriteDiagnostic(string message)
    {
        AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\nSPTC " + message);
    }

    private static void SetTreeChecked(TreeNode node, bool selected)
    {
        node.Checked = selected;
        foreach (TreeNode child in node.Nodes)
            SetTreeChecked(child, selected);
    }

    private static bool IsLeftLineList(IntPtr dialog, IntPtr candidate)
    {
        if (!GetWindowRect(candidate, out RECT rect) ||
            rect.Width < 90 || rect.Height < 80)
            return false;

        // REV30: use the actual Display lines group instead of a fixed
        // percentage of total dialog width. The native line list remains
        // discoverable after dragging the REV20+ vertical divider right.
        IntPtr group = IntPtr.Zero;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is GroupBox box &&
                box.Text.Trim().Equals("Display lines",
                    StringComparison.OrdinalIgnoreCase))
            {
                group = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (group == IntPtr.Zero || !GetWindowRect(group, out RECT bounds))
            return false;

        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;
        return cx > bounds.Left + 4 &&
               cx < bounds.Right - 4 &&
               cy > bounds.Top + 35 &&
               cy < bounds.Bottom - 12;
    }

    private static bool TryFindLineListBounds(IntPtr dialog, out RECT bounds)
    {
        bounds = default;
        RECT bestBounds = default;
        bool found = false;
        int bestHeight = 0;
        if (!GetWindowRect(dialog, out RECT dlg)) return false;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!IsLeftLineList(dialog, hwnd) ||
                !GetWindowRect(hwnd, out RECT r) || r.Height <= bestHeight)
                return true;

            Control? control = Control.FromHandle(hwnd);
            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            string name = cls.ToString();

            if (control is CheckedListBox || control is ListView || control is TreeView ||
                name.Contains("LISTBOX", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ListView", StringComparison.OrdinalIgnoreCase))
            {
                bestHeight = r.Height;
                // Transform Windows screen coordinates to native dialog client coordinates.
                // Dialog includes nonclient frame, so actual client origin is used below.
                bestBounds = r;
                found = true;
            }
            return true;
        }, IntPtr.Zero);

        if (!found) return false;
        bounds = bestBounds;
        // Convert both corners into dialog client coordinates.
        POINT a = new() { X = bounds.Left, Y = bounds.Top };
        POINT b = new() { X = bounds.Right, Y = bounds.Bottom };
        if (!ScreenToClient(dialog, ref a) || !ScreenToClient(dialog, ref b))
            return false;
        bounds.Left = a.X;
        bounds.Top = a.Y;
        bounds.Right = b.X;
        bounds.Bottom = b.Y;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref POINT point);

    public static void Hide()
    {
        _generation++;
        _lastProjectWideRowCount = 0;
        _suspendPeriodicNativeWrites = false;
        CancelActiveCommit();
        EndProjectRedrawBatch();
        try { _firstRowRetry?.Stop(); _firstRowRetry?.Dispose(); }
        catch { }
        _firstRowRetry = null;
        try { _modeRecoveryTimer?.Stop(); _modeRecoveryTimer?.Dispose(); }
        catch { }
        _modeRecoveryTimer = null;
        _modeRecoverySerial++;
        _scopeSwitchPending = false;
        _selectionEpoch++;
        DetachSelectionTracking();
        SessionSelectedTags.Clear();
        _initialNativeSelectionImported = false;
        _settledActiveRoute = "";
        _lastNativeRowSignature = "";
        _lastRestoredRowSignature = "";
        _stableNativePolls = 0;
        _dialog = IntPtr.Zero;
        try { _countTimer?.Stop(); } catch { }
        try { _countTimer?.Dispose(); } catch { }
        _countTimer = null;
        try { _host?.Dispose(); } catch { }
        _host = null;
        _toggle = null;
        _selectedCountLabel = null;
        _setting = false;
    }
}
