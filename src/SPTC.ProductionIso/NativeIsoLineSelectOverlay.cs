using System;
using System.Collections.Generic;
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
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    // REV34: the native Create eligibility handler may listen to click or
    // selected-row events, not just CheckedListBox.SetItemChecked(ItemCheck).
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;

    private static UserControl? _host;
    private static CheckBox? _toggle;
    private static IntPtr _dialog;
    private static bool _setting;
    // REV17: lightweight UI-only count refresh. No PnP/SQL queries and no
    // changes to native checked states, even when the filter changes.
    private static System.Windows.Forms.Timer? _countTimer;
    private static System.Windows.Forms.Timer? _selectionVerify;
    private static int _generation;
    // REV35: cancel outdated deferred native selection when filter or mode changes.
    private static int _selectionRequest;
    private static bool _selectionPending;

    public static void Attach(IntPtr dialog)
    {
        if (_host != null && !_host.IsDisposed && _dialog == dialog)
            return;

        Hide();
        _generation++;
        _dialog = dialog;
        _host = new UserControl
        {
            BackColor = SystemColors.Control,
            Width = 174,
            Height = 31
        };
        _toggle = new CheckBox
        {
            Text = "Select All",
            AutoSize = false,
            Location = new Point(0, 2),
            Width = 170,
            Height = 25,
            Checked = false,
            Font = SystemFonts.MessageBoxFont
        };
        _toggle.CheckedChanged += ToggleChanged;
        _host.Controls.Add(_toggle);

        _ = _host.Handle;
        SetParent(_host.Handle, dialog);
        if (GetParent(_host.Handle) != dialog)
        {
            Hide();
            return;
        }
        Position(dialog);
        _host.Show();

        UpdateDisplayedCount();
        _countTimer = new System.Windows.Forms.Timer { Interval = 1600 };
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

        SetWindowPos(_host.Handle, IntPtr.Zero, left, top, 174, 29,
            SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);
    }

    // REV17: "Select All (N)" counts the lines currently displayed, not
    // how many are checked. When a folder is active the projected native-backed
    // line list is authoritative; otherwise read the current Autodesk list.
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

        // REV35: do not let the Select All tick remain checked when Autodesk
        // has rejected a programmatic checkbox change or refreshed its list.
        // While a folder projection is active, its own code owns the checks.
        if (!ProjectFolderLinePicker.HasVisibleProjection && !_selectionPending)
        {
            CheckedListBox? native = FindNativeCheckedList(_dialog);
            if (native != null)
                ReflectNativeSelection(native);
        }
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

    private static CheckedListBox? FindNativeCheckedList(IntPtr dialog)
    {
        CheckedListBox? best = null;
        int count = -1;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!IsLeftLineList(dialog, hwnd)) return true;
            if (Control.FromHandle(hwnd) is CheckedListBox list &&
                !list.IsDisposed && list.Items.Count > count)
            {
                best = list;
                count = list.Items.Count;
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static void ReflectNativeSelection(CheckedListBox native)
    {
        if (_toggle == null || _toggle.IsDisposed ||
            native.IsDisposed || native.Items.Count == 0)
            return;
        bool all = true;
        for (int i = 0; i < native.Items.Count; i++)
            if (!native.GetItemChecked(i)) { all = false; break; }
        if (_toggle.Checked != all)
        {
            _setting = true;
            try { _toggle.Checked = all; }
            finally { _setting = false; }
        }
    }

    // REV35: updates MUST use the same native mouse click that manually
    // enables Autodesk's Create button. A direct SetItemChecked updates
    // visuals but may be rejected or not commit Autodesk's selection model.
    // Work in bounded UI-thread batches to keep large projects responsive.
    private static bool StartNativeSelection(CheckedListBox native, bool desired)
    {
        if (native.IsDisposed || !native.IsHandleCreated ||
            native.Items.Count == 0)
            return false;

        object[] rows = new object[native.Items.Count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = native.Items[i];

        int generation = _generation;
        int request = ++_selectionRequest;
        _selectionPending = true;
        try
        {
            native.BeginInvoke((MethodInvoker)(() =>
                ApplyNativeClickBatch(native, rows, desired,
                    generation, request, 0, 0)));
            return true;
        }
        catch (InvalidOperationException)
        {
            _selectionPending = false;
            return false;
        }
    }

    private static void ApplyNativeClickBatch(
        CheckedListBox native, object[] rows, bool desired,
        int generation, int request, int from, int failures)
    {
        if (generation != _generation || request != _selectionRequest ||
            _dialog == IntPtr.Zero || native.IsDisposed)
            return;

        // Autodesk can rebuild the native list after the user changes
        // the filter. Never click a different route with a stale index.
        if (native.Items.Count != rows.Length)
        {
            FinishNativeSelection(native, desired, failures + 1, request);
            return;
        }

        int to = Math.Min(rows.Length, from + 24);
        for (int i = from; i < to; i++)
        {
            if (!ReferenceEquals(native.Items[i], rows[i]))
            {
                failures++;
                to = rows.Length; // abandon the stale snapshot
                break;
            }
            if (native.GetItemChecked(i) != desired &&
                !ClickNativeRow(native, i, desired))
                failures++;
        }

        if (to < rows.Length && failures == 0)
        {
            try
            {
                native.BeginInvoke((MethodInvoker)(() =>
                    ApplyNativeClickBatch(native, rows, desired,
                        generation, request, to, failures)));
                return;
            }
            catch (InvalidOperationException) { failures++; }
        }

        FinishNativeSelection(native, desired, failures, request);
    }

    private static void FinishNativeSelection(
        CheckedListBox native, bool desired, int failures, int request)
    {
        if (request != _selectionRequest || native.IsDisposed) return;
        _selectionPending = false;

        int matched = 0;
        for (int i = 0; i < native.Items.Count; i++)
            if (native.GetItemChecked(i) == desired)
                matched++;

        // Highlight the actual selected row, like clicking the line number
        // manually does. Keep native ISO eligibility validation untouched.
        if (desired && matched > 0)
        {
            int first = FirstCheckedIndex(native);
            if (first >= 0)
            {
                if (native.SelectedIndex == first)
                    native.ClearSelected();
                native.SelectedIndex = first;
                ScheduleNativeSelectionCommit(native);
            }
        }

        ReflectNativeSelection(native);
        if (failures > 0 || matched != native.Items.Count)
        {
            AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\nSPTC REV35: Select All native clicks applied to " +
                matched + "/" + native.Items.Count +
                " displayed ISO lines (desired=" + desired +
                "). Incomplete selection; check the original line list. " +
                "Native Create was not forced.");
        }
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
            // REV35: the visible Autodesk list is authoritative. The old
            // SetItemChecked loop could leave Select All ticked while A-08
            // was UNCHECKED (and Create disabled). Replay the real native
            // checkbox clicks instead of programmatically setting states.
            return StartNativeSelection(checkedList, selected);
        }

        if (listView != null && listView.Items.Count > 0)
        {
            listView.BeginUpdate();
            try
            {
                foreach (ListViewItem item in listView.Items) item.Checked = selected;
            }
            finally { listView.EndUpdate(); }
            if (selected)
            {
                foreach (ListViewItem item in listView.Items)
                {
                    if (!item.Checked) continue;
                    if (item.Selected) item.Selected = false;
                    item.Selected = true;
                    break;
                }
            }
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

    // REV34: this is the SAME original Autodesk CheckedListBox. A scripted
    // state change raises ItemCheck, but does not produce all of the native
    // selection/mouse events that the Create eligibility handler may require.
    // Only intervene when Autodesk's Create is still disabled and we have a
    // verified checked row. Never bypass the original button validation.
    private static void ScheduleNativeSelectionCommit(CheckedListBox list)
    {
        if (_dialog == IntPtr.Zero || list.IsDisposed || !list.IsHandleCreated)
            return;

        int generation = _generation;
        IntPtr dialog = _dialog;
        try
        {
            list.BeginInvoke((MethodInvoker)(() =>
            {
                if (_generation != generation || _dialog != dialog ||
                    list.IsDisposed || !list.IsHandleCreated)
                    return;

                Button? create = FindNativeCreateButton(dialog);
                if (create is null || create.Enabled) return;
                int first = FirstCheckedIndex(list);
                if (first < 0) return;

                // Mouse-clicking the native line selects its row in addition
                // to checking it. Setting the checked state alone does not.
                if (list.SelectedIndex == first)
                    list.ClearSelected();
                list.SelectedIndex = first;
                if (create.Enabled) return;

                // Let Autodesk finish any deferred validation callback
                // before trying the native mouse-event fallback.
                try { _selectionVerify?.Stop(); _selectionVerify?.Dispose(); }
                catch { }
                var timer = new System.Windows.Forms.Timer { Interval = 180 };
                _selectionVerify = timer;
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (ReferenceEquals(_selectionVerify, timer))
                        _selectionVerify = null;
                    timer.Dispose();

                    if (_generation != generation || _dialog != dialog ||
                        list.IsDisposed || !list.IsHandleCreated)
                        return;

                    Button? nativeCreate = FindNativeCreateButton(dialog);
                    if (nativeCreate is null || nativeCreate.Enabled)
                        return;

                    int checkedRow = FirstCheckedIndex(list);
                    if (checkedRow < 0) return;

                    // Autodesk's manual checkbox click works, even where
                    // programmatic SetItemChecked left Create disabled.
                    // Replay OFF then ON on only one already-checked row.
                    // Every other checked line is left untouched.
                    if (ClickNativeRow(list, checkedRow, false))
                        ClickNativeRow(list, checkedRow, true);
                };
                timer.Start();
            }));
        }
        catch (InvalidOperationException)
        {
            // Native window closed between Select All and deferred commit.
        }
    }

    private static int FirstCheckedIndex(CheckedListBox list)
    {
        for (int i = 0; i < list.Items.Count; i++)
            if (list.GetItemChecked(i)) return i;
        return -1;
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

    // The same original native-list click mechanism already used by the
    // REV26 folder projection; it produces the application's normal WinForms
    // mouse/check/selection callbacks and verifies the desired final state.
    private static bool ClickNativeRow(CheckedListBox list, int index, bool desired)
    {
        if (list.IsDisposed || !list.IsHandleCreated ||
            index < 0 || index >= list.Items.Count)
            return false;

        int previousTop = list.TopIndex;
        try
        {
            list.TopIndex = index;
            Rectangle r = list.GetItemRectangle(index);
            if (r.Height <= 0) return false;
            int x = Math.Clamp(r.Left + 12, 5,
                Math.Max(5, list.ClientSize.Width - 6));
            int y = r.Top + r.Height / 2;
            if (y < 0 || y >= list.ClientSize.Height)
                return false;

            IntPtr point = new IntPtr(((y & 0xffff) << 16) |
                                       (x & 0xffff));
            for (int attempt = 0; attempt < 2 &&
                 list.GetItemChecked(index) != desired; attempt++)
            {
                SendMessage(list.Handle, WM_LBUTTONDOWN,
                    new IntPtr(MK_LBUTTON), point);
                SendMessage(list.Handle, WM_LBUTTONUP,
                    IntPtr.Zero, point);
            }

            // REV35: DO NOT fall back to SetItemChecked. That is precisely
            // what left Select All visually selected while the native line
            // remained unchecked in REV34. Report failure and let the user
            // click manually if the native window rejects the click.

            if (desired && list.GetItemChecked(index))
                list.SelectedIndex = index;
            return list.GetItemChecked(index) == desired;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (!list.IsDisposed && list.Items.Count > 0)
                list.TopIndex = Math.Clamp(previousTop, 0, list.Items.Count - 1);
        }
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
        _selectionRequest++;
        _selectionPending = false;
        try { _selectionVerify?.Stop(); _selectionVerify?.Dispose(); }
        catch { }
        _selectionVerify = null;
        _dialog = IntPtr.Zero;
        try { _countTimer?.Stop(); } catch { }
        try { _countTimer?.Dispose(); } catch { }
        _countTimer = null;
        try { _host?.Dispose(); } catch { }
        _host = null;
        _toggle = null;
        _setting = false;
    }
}
