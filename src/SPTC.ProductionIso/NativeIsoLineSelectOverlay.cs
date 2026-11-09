using System;
using System.Collections.Generic;
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
    private const uint LVM_SETITEMSTATE = 0x102B;
    private const uint LVM_GETEXTENDEDLISTVIEWSTYLE = 0x1037;
    private const long LVS_EX_CHECKBOXES = 0x00000004;
    private const uint LVIS_STATEIMAGEMASK = 0xF000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static UserControl? _host;
    private static CheckBox? _toggle;
    private static IntPtr _dialog;
    private static bool _setting;

    public static void Attach(IntPtr dialog)
    {
        if (_host != null && !_host.IsDisposed && _dialog == dialog)
            return;

        Hide();
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

    private static void ToggleChanged(object? sender, EventArgs e)
    {
        if (_setting || _toggle == null || _dialog == IntPtr.Zero) return;
        bool selected = _toggle.Checked;

        try
        {
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
            checkedList.BeginUpdate();
            try
            {
                for (int i = 0; i < checkedList.Items.Count; i++)
                    if (checkedList.GetItemChecked(i) != selected)
                        checkedList.SetItemChecked(i, selected);
            }
            finally { checkedList.EndUpdate(); }
            return true;
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

    private static void SetTreeChecked(TreeNode node, bool selected)
    {
        node.Checked = selected;
        foreach (TreeNode child in node.Nodes)
            SetTreeChecked(child, selected);
    }

    private static bool IsLeftLineList(IntPtr dialog, IntPtr candidate)
    {
        if (!GetWindowRect(dialog, out RECT dlg) ||
            !GetWindowRect(candidate, out RECT rect)) return false;
        if (rect.Width < 90 || rect.Height < 80) return false;

        // Filter out output settings, buttons, dialogs and invisible tiny controls.
        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;
        return cx > dlg.Left + 10 &&
               cx < dlg.Left + dlg.Width * 0.33 &&
               cy > dlg.Top + 100 && cy < dlg.Bottom - 80;
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
        _dialog = IntPtr.Zero;
        try { _host?.Dispose(); } catch { }
        _host = null;
        _toggle = null;
        _setting = false;
    }
}
