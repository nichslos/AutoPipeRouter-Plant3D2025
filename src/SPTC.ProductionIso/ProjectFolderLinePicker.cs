using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;

namespace SPTC.ProductionIso;

// REV13: Non-destructive folder picker. A projection of verified folder tags
// covers the native line list. Each check is forwarded to the SAME underlying
// Autodesk checkbox, which is what Production Iso reads during generation.
// On "All Project", the projection is removed and the original native list
// remains intact. Never remove/reinsert original Autodesk line-list items.
internal static class ProjectFolderLinePicker
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
    private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumChildProc cb, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr hwnd, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder cls, int size);

    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    private sealed record NativeEntry(int Index, string Tag);
    private static IntPtr _dialog;
    private static Document? _document;
    private static UserControl? _menuHost;
    private static Button? _folderButton;
    private static ToolTip? _toolTip;
    private static UserControl? _lineHost;
    private static CheckedListBox? _folderLineList;
    private static CheckedListBox? _nativeChecked;
    private static ListView? _nativeView;
    private static readonly List<NativeEntry> VisibleEntries = new();
    private static bool _updating;
    private static string _folder = "";

    public static bool FolderActive => !string.IsNullOrWhiteSpace(_folder);

    public static void Attach(IntPtr dialog, Document? document)
    {
        if (dialog == IntPtr.Zero) return;
        if (_dialog == dialog && _menuHost is { IsDisposed: false }) return;
        Hide();
        _dialog = dialog;
        _document = document;
        _menuHost = new UserControl { BackColor = SystemColors.Control, Size = new Size(29, 26) };
        _folderButton = new Button
        {
            Text = "▾",
            FlatStyle = FlatStyle.Standard,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10, FontStyle.Bold),
            Location = Point.Empty,
            Size = new Size(27, 25),
            TabStop = false,
            AccessibleName = "Plant 3D Drawings folder filter"
        };
        _folderButton.Click += (_, _) => OpenFolderMenu();
        _menuHost.Controls.Add(_folderButton);
        _ = _menuHost.Handle;
        SetParent(_menuHost.Handle, dialog);
        if (GetParent(_menuHost.Handle) != dialog)
        {
            Hide();
            return;
        }
        _toolTip = new ToolTip();
        _toolTip.SetToolTip(_folderButton, "Filter line numbers by Plant 3D Drawings project folder");
        Position(dialog);
        _menuHost.Show();
    }

    public static void Position(IntPtr dialog)
    {
        if (_dialog != dialog || _menuHost is null || _menuHost.IsDisposed)
            return;

        // Locate the native line filter textbox; position precisely over its
        // existing funnel/dropdown icon, not the Drawing/Project buttons.
        int x = 175, y = 88;
        if (TryFindNativeFilterEdit(dialog, out RECT edit))
        {
            x = edit.Right - 27;
            y = edit.Top;
        }
        if (GetClientRect(dialog, out RECT bounds))
        {
            x = Math.Clamp(x, 20, bounds.Width - 40);
            y = Math.Clamp(y, 32, Math.Min(175, bounds.Height - 40));
        }

        SetWindowPos(_menuHost.Handle, IntPtr.Zero, x, y, 28, 26,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        if (FolderActive && _lineHost is { IsDisposed: false })
            PositionList(dialog);
    }

    private static bool TryFindNativeFilterEdit(IntPtr dialog, out RECT client)
    {
        client = default;
        if (!GetWindowRect(dialog, out RECT dlg)) return false;
        var matches = new List<(IntPtr Handle, RECT Rect, int Score)>();
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT r) || r.Width < 110 || r.Height < 16 || r.Height > 45)
                return true;
            double x = (r.Left + r.Right) * 0.5 - dlg.Left;
            double y = (r.Top + r.Bottom) * 0.5 - dlg.Top;
            if (x < 30 || x > Math.Min(300, dlg.Width * 0.40) || y < 65 || y > 160)
                return true;

            Control? c = Control.FromHandle(hwnd);
            var cls = new StringBuilder(128);
            GetClassName(hwnd, cls, cls.Capacity);
            if (c is not TextBoxBase &&
                !cls.ToString().Contains("EDIT", StringComparison.OrdinalIgnoreCase))
                return true;
            int score = r.Width - Math.Abs((int)y - 105);
            matches.Add((hwnd, r, score));
            return true;
        }, IntPtr.Zero);

        if (matches.Count == 0) return false;
        RECT rect = matches.OrderByDescending(m => m.Score).First().Rect;
        POINT a = new() { X = rect.Left, Y = rect.Top };
        POINT b = new() { X = rect.Right, Y = rect.Bottom };
        if (!ScreenToClient(dialog, ref a) || !ScreenToClient(dialog, ref b))
            return false;
        client = new RECT { Left = a.X, Top = a.Y, Right = b.X, Bottom = b.Y };
        return true;
    }

    private static void OpenFolderMenu()
    {
        if (_folderButton == null || _document == null) return;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            if (!ProjectFolderLineCatalog.Ready)
                ProjectFolderLineCatalog.Initialize(_document);

            var menu = new ContextMenuStrip { ShowImageMargin = false };
            var all = new ToolStripMenuItem("All Project (no folder filter)")
            {
                Checked = !FolderActive,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
            };
            all.Click += (_, _) => ClearFolder();
            menu.Items.Add(all);
            menu.Items.Add(new ToolStripSeparator());

            if (!ProjectFolderLineCatalog.Ready)
            {
                var diagnostic = new ToolStripMenuItem(ProjectFolderLineCatalog.Diagnostic)
                { Enabled = false };
                menu.Items.Add(diagnostic);
            }
            else
            {
                foreach (var choice in ProjectFolderLineCatalog.Folders)
                {
                    // Display full relative folder paths to distinguish
                    // identically named subfolders under different parents.
                    string label = choice.Key.Replace("\\", " > ") +
                                   $"  ({choice.DrawingCount} DWG)";
                    var item = new ToolStripMenuItem(label)
                    {
                        Tag = choice.Key,
                        Checked = _folder.Equals(choice.Key, StringComparison.OrdinalIgnoreCase)
                    };
                    item.Click += (_, _) => ApplyFolder((string)item.Tag!);
                    menu.Items.Add(item);
                }
                if (ProjectFolderLineCatalog.Folders.Count == 0)
                    menu.Items.Add(new ToolStripMenuItem("(No registered subfolders)") { Enabled = false });
            }
            // Context menu is a genuine Windows popup; dialog UI need not resize.
            menu.Closed += (_, _) => menu.Dispose();
            menu.Show(_folderButton, new Point(0, _folderButton.Height));
        }
        catch (Exception ex)
        {
            _document.Editor.WriteMessage("\nSPTC REV13 folder menu: " + ex.Message);
        }
        finally { Cursor.Current = Cursors.Default; }
    }

    private static void ApplyFolder(string folder)
    {
        if (_document == null || _dialog == IntPtr.Zero) return;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            ProjectFolderLineCatalog.FolderLines result =
                ProjectFolderLineCatalog.GetLiveTags(folder);
            _document.Editor.WriteMessage("\nSPTC REV13 " + folder + ": " + result.Note);
            if (result.Note.StartsWith("Unable", StringComparison.OrdinalIgnoreCase) ||
                result.Note.StartsWith("Folder has", StringComparison.OrdinalIgnoreCase) ||
                result.Note.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(result.Note, "SPTC Folder Filter",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // REV09-11 optional active-route text filter may be enabled.
            // Reset it before reading the project's full native line list.
            ActiveDwgTags.ClearOneTag(_dialog, null);
            _folder = "";
            DestroyProjection();

            if (!TryBindNativeList(_dialog, out List<NativeEntry> nativeRows))
            {
                MessageBox.Show(
                    "Could not identify the original checked line list. Folder filtering has not changed any native entries.\n" +
                    "Choose Project (second top icon) and retry, or run SPTC_FOLDERDIAG.",
                    "SPTC Folder Filter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var allowed = new HashSet<string>(result.Tags, StringComparer.OrdinalIgnoreCase);
            var visible = nativeRows.Where(x => allowed.Contains(x.Tag)).ToList();

            if (allowed.Count > 0 && visible.Count != allowed.Count)
            {
                // Do not pretend that lines from other DWGs are selectable when
                // Autodesk's current Drawing/Filter mode has not loaded them.
                MessageBox.Show(
                    $"This folder has {allowed.Count} verified line number(s), but only {visible.Count} " +
                    "are currently available in the native list.\n" +
                    "Click the second (Project) icon above Line Numbers to load project-wide routes, " +
                    "then choose the folder again.",
                    "SPTC Folder Filter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // All underlying line checks outside this folder must be cleared,
            // otherwise native ISO Create could still export hidden selections.
            foreach (var row in nativeRows)
                if (!allowed.Contains(row.Tag)) SetNativeChecked(row, false);

            VisibleEntries.AddRange(visible);
            _folder = folder;
            BuildProjection(visible);
            PositionList(_dialog);
            _document.Editor.WriteMessage(
                $"\nSPTC REV13 folder selected: {folder}; {visible.Count} line number(s) shown. " +
                "Native project records unchanged.");
        }
        catch (Exception ex)
        {
            _folder = "";
            DestroyProjection();
            _document.Editor.WriteMessage("\nSPTC REV13 folder filter: " + ex.Message);
        }
        finally { Cursor.Current = Cursors.Default; }
    }

    private static bool TryBindNativeList(IntPtr dialog, out List<NativeEntry> result)
    {
        result = new List<NativeEntry>();
        CheckedListBox? cb = null;
        ListView? lv = null;
        int bestCount = -1;
        if (!GetWindowRect(dialog, out RECT window)) return false;

        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT r) || r.Width < 90 || r.Height < 150)
                return true;
            int centerX = (r.Left + r.Right) / 2 - window.Left;
            int centerY = (r.Top + r.Bottom) / 2 - window.Top;
            if (centerX < 10 || centerX > window.Width * 0.34 ||
                centerY < 130 || centerY > window.Height - 70)
                return true;

            Control? c = Control.FromHandle(hwnd);
            if (c is CheckedListBox list && list.Items.Count > bestCount)
            {
                cb = list; lv = null; bestCount = list.Items.Count;
            }
            else if (c is ListView view && view.CheckBoxes && view.Items.Count > bestCount)
            {
                cb = null; lv = view; bestCount = view.Items.Count;
            }
            return true;
        }, IntPtr.Zero);

        _nativeChecked = cb;
        _nativeView = lv;
        if (cb != null)
        {
            for (int i = 0; i < cb.Items.Count; i++)
                result.Add(new NativeEntry(i, cb.GetItemText(cb.Items[i]).Trim()));
            return true;
        }
        if (lv != null)
        {
            for (int i = 0; i < lv.Items.Count; i++)
                result.Add(new NativeEntry(i, lv.Items[i].Text.Trim()));
            return true;
        }
        return false;
    }

    private static bool SetNativeChecked(NativeEntry entry, bool check)
    {
        if (_nativeChecked != null)
        {
            if (entry.Index >= _nativeChecked.Items.Count ||
                !_nativeChecked.GetItemText(_nativeChecked.Items[entry.Index])
                    .Trim().Equals(entry.Tag, StringComparison.OrdinalIgnoreCase))
                return false;
            _nativeChecked.SetItemChecked(entry.Index, check);
            return true;
        }
        if (_nativeView != null)
        {
            if (entry.Index >= _nativeView.Items.Count ||
                !_nativeView.Items[entry.Index].Text.Trim()
                    .Equals(entry.Tag, StringComparison.OrdinalIgnoreCase))
                return false;
            _nativeView.Items[entry.Index].Checked = check;
            return true;
        }
        return false;
    }

    private static bool GetNativeChecked(NativeEntry entry)
    {
        if (_nativeChecked != null)
            return entry.Index < _nativeChecked.Items.Count &&
                   _nativeChecked.GetItemChecked(entry.Index);
        return _nativeView != null && entry.Index < _nativeView.Items.Count &&
               _nativeView.Items[entry.Index].Checked;
    }

    private static void BuildProjection(List<NativeEntry> entries)
    {
        _lineHost = new UserControl { BackColor = SystemColors.Window };
        _folderLineList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Font = SystemFonts.MessageBoxFont
        };
        _updating = true;
        try
        {
            foreach (var entry in entries)
            {
                int i = _folderLineList.Items.Add(entry.Tag);
                _folderLineList.SetItemChecked(i, GetNativeChecked(entry));
            }
        }
        finally { _updating = false; }

        _folderLineList.ItemCheck += (_, e) =>
        {
            if (_updating || e.Index < 0 || e.Index >= VisibleEntries.Count) return;
            bool requested = e.NewValue == CheckState.Checked;
            if (!SetNativeChecked(VisibleEntries[e.Index], requested))
            {
                e.NewValue = e.CurrentValue;
                _document?.Editor.WriteMessage(
                    "\nSPTC REV13: Native line list changed; close folder filter and reopen it.");
            }
        };
        _lineHost.Controls.Add(_folderLineList);
        if (entries.Count == 0)
        {
            var notice = new Label
            {
                Text = "No verified line numbers in this folder",
                Dock = DockStyle.Top,
                BackColor = SystemColors.Info,
                AutoSize = false,
                Height = 35
            };
            _lineHost.Controls.Add(notice);
            notice.BringToFront();
        }
        _ = _lineHost.Handle;
        SetParent(_lineHost.Handle, _dialog);
        if (GetParent(_lineHost.Handle) != _dialog)
        {
            DestroyProjection();
            _folder = "";
            return;
        }
        _lineHost.Show();
    }

    private static void PositionList(IntPtr dialog)
    {
        if (_lineHost == null || _lineHost.IsDisposed || _nativeChecked is null && _nativeView is null)
            return;
        IntPtr handle = _nativeChecked?.Handle ?? _nativeView!.Handle;
        if (!GetWindowRect(handle, out RECT r)) return;
        POINT a = new() { X = r.Left, Y = r.Top };
        POINT b = new() { X = r.Right, Y = r.Bottom };
        if (!ScreenToClient(dialog, ref a) || !ScreenToClient(dialog, ref b)) return;
        SetWindowPos(_lineHost.Handle, IntPtr.Zero, a.X, a.Y,
            Math.Max(80, b.X - a.X), Math.Max(130, b.Y - a.Y),
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public static bool TrySelectAllVisible(bool value)
    {
        if (!FolderActive || _folderLineList is null || _folderLineList.IsDisposed)
            return false;

        _updating = true;
        try
        {
            for (int i = 0; i < VisibleEntries.Count; i++)
            {
                bool success = SetNativeChecked(VisibleEntries[i], value);
                if (!success) return false;
                _folderLineList.SetItemChecked(i, value);
            }
            return true;
        }
        finally { _updating = false; }
    }

    public static void ClearFolder()
    {
        if (_dialog != IntPtr.Zero)
            ActiveDwgTags.ClearOneTag(_dialog, null);
        _folder = "";
        DestroyProjection();
        _document?.Editor.WriteMessage("\nSPTC REV13: All Project lines restored.");
    }

    private static void DestroyProjection()
    {
        VisibleEntries.Clear();
        _nativeChecked = null;
        _nativeView = null;
        try { _lineHost?.Dispose(); } catch { }
        _lineHost = null;
        _folderLineList = null;
        _updating = false;
    }

    public static void Hide()
    {
        _folder = "";
        DestroyProjection();
        try { _menuHost?.Dispose(); } catch { }
        try { _toolTip?.Dispose(); } catch { }
        _menuHost = null;
        _folderButton = null;
        _toolTip = null;
        _dialog = IntPtr.Zero;
        _document = null;
        ProjectFolderLineCatalog.Reset();
    }
}
