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
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static UserControl? _host;
    private static CheckBox? _toggle;
    private static IntPtr _dialog;
    private static bool _setting;
    // REV17: lightweight UI-only count refresh. No PnP/SQL queries and no
    // changes to native checked states, even when the filter changes.
    private static System.Windows.Forms.Timer? _countTimer;
    // REV36: diagnostics and deferred actual native selection, no fake Win32 clicks.
    private static int _generation;
    private static System.Windows.Forms.Timer? _firstRowRetry;

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
        var timer = new System.Windows.Forms.Timer { Interval = 160 };
        _firstRowRetry = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (ReferenceEquals(_firstRowRetry, timer)) _firstRowRetry = null;
            if (_generation != generation || _dialog != parent ||
                native.IsDisposed || native.Items.Count == 0)
                return;
            try
            {
                if (native.GetItemChecked(0) != selected)
                    native.SetItemChecked(0, selected);

                // REV39: The exact Active-DWG one-line filter can display
                // a real checkmark while Autodesk still has Create disabled.
                // After the checkbox event settles, replay the ORIGINAL
                // native row's mouse handlers only for that single verified
                // active route, without affecting working Project-wide mode.
                Button? createBefore = FindNativeCreateButton(parent);
                if (selected && native.Items.Count == 1 &&
                    native.GetItemChecked(0) && createBefore is { Enabled: false } &&
                    !ProjectFolderLinePicker.HasVisibleProjection &&
                    ActiveDwgTags.IsNativeFilterCurrent(parent))
                    RecommitExactActiveRow(native);

                int actual = 0;
                for (int i = 0; i < native.Items.Count; i++)
                    if (native.GetItemChecked(i)) actual++;

                bool allChecked = native.Items.Count > 0 &&
                                  actual == native.Items.Count;
                if (_toggle is { IsDisposed: false } toggle)
                {
                    _setting = true;
                    try { toggle.Checked = allChecked; }
                    finally { _setting = false; }
                }
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

    // REV39: only use a native mouse sequence in single-line Active-DWG mode.
    // The original checkbox is directly visible (no folder projection).
    // A checked icon alone may not satisfy Autodesk's own Create action.
    // Do not forge a button state or fabricate ISO line items.
    private static void RecommitExactActiveRow(CheckedListBox native)
    {
        if (native.IsDisposed || !native.IsHandleCreated ||
            native.Items.Count != 1 || !native.GetItemChecked(0))
            return;
        try
        {
            int savedTop = native.TopIndex;
            native.TopIndex = 0;
            try
            {
                Rectangle rect = native.GetItemRectangle(0);
                if (rect.Height <= 0 || native.ClientSize.Width < 22)
                    return;

                int x = Math.Clamp(rect.Left + 11, 5, native.ClientSize.Width - 6);
                int y = rect.Top + rect.Height / 2;
                if (y < 0 || y >= native.ClientSize.Height)
                    return;

                IntPtr location = new(((y & 0xFFFF) << 16) | (x & 0xFFFF));

                // Prepare an actual unchecked-to-checked native event.
                // REV36's extra click on an already checked row toggled the
                // FIRST item off; here the pre-click state is unchecked.
                native.SetItemChecked(0, false);
                if (native.SelectedIndex == 0)
                    native.ClearSelected();
                native.SelectedIndex = 0;

                for (int attempt = 0; attempt < 2 &&
                     !native.GetItemChecked(0); attempt++)
                {
                    SendMessage(native.Handle, WM_LBUTTONDOWN,
                        new IntPtr(MK_LBUTTON), location);
                    SendMessage(native.Handle, WM_LBUTTONUP,
                        IntPtr.Zero, location);
                }
                // Never leave a previously selected route unchecked merely
                // because this native dialog ignored the synthetic event.
                if (!native.GetItemChecked(0))
                    native.SetItemChecked(0, true);
                if (native.SelectedIndex != 0)
                    native.SelectedIndex = 0;

                WriteDiagnostic("REV39 Active single-line native event: checked=" +
                    native.GetItemChecked(0));
            }
            finally
            {
                if (!native.IsDisposed && native.Items.Count > 0)
                    native.TopIndex = Math.Clamp(savedTop, 0, native.Items.Count - 1);
            }
        }
        catch (Exception ex)
        {
            if (!native.IsDisposed && native.Items.Count == 1 &&
                !native.GetItemChecked(0))
                native.SetItemChecked(0, true);
            WriteDiagnostic("REV39 Active single-line event failed: " +
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
        try { _firstRowRetry?.Stop(); _firstRowRetry?.Dispose(); }
        catch { }
        _firstRowRetry = null;
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
