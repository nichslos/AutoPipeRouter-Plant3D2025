using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SPTC.ProductionIso;

// REV06: experimental non-destructive child overlay in Autodesk's
// existing Isometric Creation Results dialog. We do NOT alter any
// Autodesk TreeView item, native hyperlink, registered path or project data.
internal static class ResultIsoLinkOverlay
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int size);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT bounds);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT bounds);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);

    private const uint TVM_GETNEXTITEM = 0x110A;
    private const uint TVM_GETITEMRECT = 0x1104;
    private const int TVGN_LASTVISIBLE = 0x000A;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOZORDER = 0x0004;

    private static IntPtr _parent;
    private static UserControl? _panel;
    private static LinkLabel? _pathLink;
    private static LinkLabel? _folderLink;
    private static Label? _label;
    private static ToolTip? _tooltip;
    private static string _fileToOpen = "";
    private static string _folderToOpen = "";
    private static int _fileCount;

    public static void ShowOrUpdate(IntPtr resultDialog, IReadOnlyList<string> copiedFiles, string customFolder)
    {
        if (resultDialog == IntPtr.Zero || copiedFiles.Count < 1 || !IsWindow(resultDialog))
        {
            Hide();
            return;
        }

        if (_parent != resultDialog || _panel == null || _panel.IsDisposed)
        {
            Hide();
            _parent = resultDialog;
            try
            {
                CreateControls(resultDialog);
            }
            catch
            {
                Hide(); // Native UI differs; copies still work in REV05 manner.
                return;
            }
        }

        // The file hyperlink is displayed only after a real file has been copied.
        // All copied paths are produced by the successful background export.
        _fileCount = copiedFiles.Count;
        _fileToOpen = copiedFiles[0];
        _folderToOpen = customFolder;

        if (_label != null)
            _label.Text = _fileCount == 1 ? "Custom ISO (SPTC Export):" :
                $"Custom ISO ({_fileCount} copied DWGs):";

        if (_pathLink != null)
        {
            _pathLink.Text = _fileCount == 1
                ? _fileToOpen
                : customFolder;
            _tooltip?.SetToolTip(_pathLink, _fileCount == 1
                ? "Open copied ISO DWG: " + _fileToOpen
                : "Open folder containing " + _fileCount + " copied ISO DWGs: " + customFolder);
        }

        Position(resultDialog);
    }

    private static void CreateControls(IntPtr parent)
    {
        _panel = new UserControl
        {
            BackColor = SystemColors.Window,
            Height = 48,
            Width = 680
        };

        _label = new Label
        {
            Text = "Custom ISO (SPTC Export):",
            AutoSize = false,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Location = new Point(2, 0),
            Height = 19
        };
        _pathLink = new LinkLabel
        {
            Text = "",
            AutoSize = false,
            LinkBehavior = LinkBehavior.AlwaysUnderline,
            LinkColor = Color.FromArgb(0, 88, 191),
            Location = new Point(2, 20),
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        _folderLink = new LinkLabel
        {
            Text = "Open custom folder",
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Location = new Point(2, 42),
            Visible = false
        };
        _tooltip = new ToolTip();

        _pathLink.LinkClicked += (_, _) =>
        {
            string target = _fileCount == 1 ? _fileToOpen : _folderToOpen;
            OpenOutput(target);
        };
        _folderLink.LinkClicked += (_, _) => OpenOutput(_folderToOpen);

        _panel.Controls.Add(_label);
        _panel.Controls.Add(_pathLink);
        _panel.Controls.Add(_folderLink);
        _panel.Resize += (_, _) =>
        {
            if (_label != null) _label.Width = Math.Max(80, _panel.Width - 4);
            if (_pathLink != null) _pathLink.Width = Math.Max(80, _panel.Width - 4);
        };

        // A managed WinForms child HWND is added to the existing Results window.
        // Original Autodesk controls and URLs are not replaced or modified.
        _ = _panel.Handle;
        SetParent(_panel.Handle, parent);
        // SetParent can return NULL on success when the original parent was NULL.
        // Verify the actual parent instead of testing its return value.
        if (GetParent(_panel.Handle) != parent)
            throw new InvalidOperationException("Could not attach custom ISO link to Results dialog.");

        Position(parent);
        _panel.Show();
    }

    private static void OpenOutput(string target)
    {
        try
        {
            if (!File.Exists(target) && !Directory.Exists(target))
            {
                MessageBox.Show("Custom ISO file or folder is not available:\n" + target,
                    "SPTC Production Iso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Unable to open custom ISO:\n" + ex.Message,
                "SPTC Production Iso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void Position(IntPtr dialog)
    {
        if (_panel == null || _panel.IsDisposed || !GetClientRect(dialog, out RECT dialogRect))
            return;

        int left = 76;
        int top = 0;
        int availableWidth = Math.Max(200, dialogRect.Width - left - 40);

        // Try to place below the actual last visible Results TreeView row.
        // Native Results may render "File:" and "Open drawing folder" links
        // as two extra text lines below the last TreeView item; keep clear.
        if (TryLastResultRow(dialog, out int lastRowBottom))
            top = Math.Max(lastRowBottom + 48, (int)(dialogRect.Height * 0.49));
        else
            top = (int)(dialogRect.Height * 0.49);

        // Avoid overlapping the bottom action-button row.
        top = Math.Min(top, dialogRect.Height - 95);
        top = Math.Max(120, top);

        SetWindowPos(_panel.Handle, IntPtr.Zero,
            left, top, availableWidth, 45,
            SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOZORDER);
    }

    private static bool TryLastResultRow(IntPtr results, out int lastBottom)
    {
        lastBottom = 0;
        IntPtr tree = IntPtr.Zero;

        EnumChildWindows(results, (hwnd, _) =>
        {
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString().Contains("SysTreeView32", StringComparison.OrdinalIgnoreCase) &&
                GetClientRect(hwnd, out RECT bounds) && bounds.Width > 250 && bounds.Height > 160)
            {
                tree = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (tree == IntPtr.Zero || !GetWindowRect(tree, out RECT treeBounds))
            return false;

        IntPtr item = SendMessage(tree, TVM_GETNEXTITEM, new IntPtr(TVGN_LASTVISIBLE), IntPtr.Zero);
        if (item == IntPtr.Zero) return false;

        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
        try
        {
            // Win32 TVM_GETITEMRECT requires the item's HTREEITEM in RECT.left.
            Marshal.WriteIntPtr(buffer, item);
            if (SendMessage(tree, TVM_GETITEMRECT, IntPtr.Zero, buffer) == IntPtr.Zero)
                return false;

            RECT itemRect = Marshal.PtrToStructure<RECT>(buffer);
            var treeOrigin = new POINT { X = treeBounds.Left, Y = treeBounds.Top };
            if (!ScreenToClient(results, ref treeOrigin)) return false;
            lastBottom = treeOrigin.Y + itemRect.Bottom;
            return lastBottom > 40;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static void Hide()
    {
        _parent = IntPtr.Zero;
        try { _panel?.Dispose(); } catch { }
        try { _tooltip?.Dispose(); } catch { }
        _panel = null;
        _pathLink = null;
        _folderLink = null;
        _label = null;
        _tooltip = null;
        _fileCount = 0;
        _fileToOpen = "";
        _folderToOpen = "";
    }
}
