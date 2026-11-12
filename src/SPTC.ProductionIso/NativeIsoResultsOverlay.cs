using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SPTC.ProductionIso;

/// <summary>
/// REV48: show links to actual custom-copy DWG files INSIDE the existing
/// Autodesk "Isometric Creation Results" dialog, without modifying
/// Autodesk's original result nodes/links or replacing the dialog.
/// </summary>
internal static class NativeIsoResultsOverlay
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private static IntPtr _dialog;
    private static Panel? _panel;
    private static Label? _title;
    private static LinkLabel? _folderLink;
    private static System.Windows.Forms.Timer? _timer;
    private static DateTime _deadline;
    private static string _customFolder = "";
    private static readonly List<string> CopiedPaths = new();
    private static bool _attachedOnce;

    public static void Reset()
    {
        try { _timer?.Stop(); _timer?.Dispose(); } catch { }
        _timer = null;
        DisposePanel();
        _dialog = IntPtr.Zero;
        _customFolder = "";
        CopiedPaths.Clear();
        _attachedOnce = false;
    }

    /// <summary>
    /// Start looking for the original Results window after the Production
    /// Iso dialog closes. No custom dialog is ever opened.
    /// </summary>
    public static void BeginSession(string? customFolder)
    {
        if (string.IsNullOrWhiteSpace(customFolder))
            return;

        try { _customFolder = Path.GetFullPath(customFolder); }
        catch (Exception) { return; }

        if (_timer != null)
            return;

        _deadline = DateTime.UtcNow.AddMinutes(2);
        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        OnTick();
    }

    public static void ReportCopied(string path)
    {
        if (string.IsNullOrWhiteSpace(_customFolder))
            return;

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { return; }

        // Only files physically copied into the user's chosen folder during
        // THIS Production Iso run are offered as current-result DWG links.
        if (!full.StartsWith(
                Path.TrimEndingDirectorySeparator(_customFolder) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full) ||
            CopiedPaths.Any(p => p.Equals(full, StringComparison.OrdinalIgnoreCase)))
            return;

        CopiedPaths.Add(full);
        RefreshLinks();
    }

    private static IntPtr FindResults()
    {
        IntPtr match = IntPtr.Zero;
        uint currentPid = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != currentPid)
                return true;

            var text = new StringBuilder(120);
            GetWindowText(hwnd, text, text.Capacity);
            if (!text.ToString().Equals("Isometric Creation Results",
                    StringComparison.OrdinalIgnoreCase))
                return true;

            match = hwnd;
            return false;
        }, IntPtr.Zero);
        return match;
    }

    private static void OnTick()
    {
        if (_timer == null) return;

        IntPtr current = FindResults();
        if (_panel is { IsDisposed: false } &&
            (_dialog == IntPtr.Zero || !IsWindow(_dialog) ||
             current != _dialog))
        {
            DisposePanel();
            _dialog = IntPtr.Zero;
        }

        if (current != IntPtr.Zero)
        {
            if (_panel is null)
            {
                if (_attachedOnce)
                    return; // never attach to a second/stale results window
                Attach(current);
            }
            else
                Position();
        }
        else if (_attachedOnce || DateTime.UtcNow > _deadline)
        {
            // Keep the existing REV47 copying task independent of the
            // lifetime of this small, UI-only results overlay.
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            _timer = null;
        }
    }

    private static void Attach(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out RECT area) ||
            area.Width < 350 || area.Height < 260)
            return;

        try
        {
            _dialog = hwnd;
            _panel = new Panel
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoScroll = true,
                TabStop = false
            };
            _title = new Label
            {
                Text = "Custom Save Location:",
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                AutoSize = false,
                Height = 20,
                Location = new Point(0, 0),
                ForeColor = SystemColors.WindowText
            };
            _panel.Controls.Add(_title);
            _folderLink = MakeLink(
                "Open custom drawing folder in Windows Explorer",
                () => OpenFolder(_customFolder));
            _panel.Controls.Add(_folderLink);

            // Child controls live inside Autodesk's ORIGINAL results dialog.
            // We never alter its native output rows, links or buttons.
            _ = _panel.Handle;
            SetParent(_panel.Handle, hwnd);
            _panel.Show();
            _attachedOnce = true;
            RefreshLinks();
            Position();
        }
        catch (Exception)
        {
            DisposePanel();
            _dialog = IntPtr.Zero;
        }
    }

    private static LinkLabel MakeLink(string caption, Action click)
    {
        var link = new LinkLabel
        {
            AutoSize = false,
            Height = 20,
            Text = caption,
            LinkColor = Color.Blue,
            ActiveLinkColor = Color.Blue,
            VisitedLinkColor = Color.Blue,
            Font = SystemFonts.MessageBoxFont,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
            TabStop = true
        };
        link.LinkClicked += (_, _) =>
        {
            try { click(); }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not open the custom DWG destination: " +
                    ex.GetBaseException().Message,
                    "SPTC Custom ISO Link", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        };
        return link;
    }

    private static void RefreshLinks()
    {
        if (_panel is not { IsDisposed: false } panel ||
            _folderLink is null || _title is null)
            return;

        // Rebuild only OUR own custom result links; native Autodesk result
        // nodes and their original DWG link remain entirely untouched.
        for (int i = panel.Controls.Count - 1; i >= 0; i--)
        {
            Control c = panel.Controls[i];
            if (ReferenceEquals(c, _title) ||
                ReferenceEquals(c, _folderLink))
                continue;
            panel.Controls.RemoveAt(i);
            c.Dispose();
        }

        int y = 21;
        if (CopiedPaths.Count == 0)
        {
            var pending = new Label
            {
                Text = "Custom DWG copy pending (the original ISO remains above).",
                AutoSize = false,
                Height = 20,
                Location = new Point(0, y),
                ForeColor = SystemColors.GrayText,
                Font = SystemFonts.MessageBoxFont
            };
            panel.Controls.Add(pending);
            y += 23;
        }
        else
        {
            foreach (string path in CopiedPaths)
            {
                string captured = path;
                LinkLabel fileLink = MakeLink(
                    "File: " + captured, () => OpenDrawing(captured));
                fileLink.Location = new Point(0, y);
                fileLink.Tag = captured;
                fileLink.MouseEnter += (_, _) =>
                {
                    // Show the untruncated full DWG path on hover.
                    var tip = new ToolTip();
                    tip.SetToolTip(fileLink, captured);
                    fileLink.Disposed += (_, _) => tip.Dispose();
                };
                panel.Controls.Add(fileLink);
                y += 21;
            }
        }

        _folderLink.Location = new Point(0, y);
        y += 24;

        if (GetClientRect(_dialog, out RECT bounds))
        {
            int w = Math.Max(160, bounds.Width - 116);
            _title.Width = w;
            _folderLink.Width = w;
            foreach (Control item in panel.Controls)
                item.Width = w;
        }
        Position();
    }

    private static void Position()
    {
        if (_panel is not { IsDisposed: false } panel ||
            _dialog == IntPtr.Zero ||
            !GetClientRect(_dialog, out RECT rect))
            return;

        int width = Math.Max(180, rect.Width - 105);
        int height = Math.Min(
            170, 24 + (Math.Max(1, CopiedPaths.Count) * 23) + 27);

        // Anchor immediately below the visible original output-tree text
        // whenever the standard WinForms TreeView is available. Otherwise,
        // place the links in the large white results area of the SAME form.
        int top = Math.Min(337, Math.Max(220, rect.Height - 188));
        int treeBottom = FindLastNativeResultRowBottom();
        if (treeBottom > 0 && treeBottom + 8 + height < rect.Height - 62)
            top = treeBottom + 8;
        top = Math.Min(top, Math.Max(110, rect.Height - height - 63));

        // Leave the original native results text untouched. Our small
        // white panel sits below it, never over the bottom control buttons.
        SetWindowPos(panel.Handle, IntPtr.Zero,
            74, top, width, height,
            SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);
        panel.BringToFront();

        if (_title != null && !_title.IsDisposed)
            _title.Width = Math.Max(100, width - 16);
        if (_folderLink != null && !_folderLink.IsDisposed)
            _folderLink.Width = Math.Max(100, width - 16);
        foreach (Control control in panel.Controls)
            control.Width = Math.Max(100, width - 16);
    }

    private static int FindLastNativeResultRowBottom()
    {
        int bestBottom = -1;
        EnumChildWindows(_dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is not TreeView tree ||
                tree.IsDisposed || tree.Nodes.Count == 0)
                return true;

            // Exclude unrelated small TreeViews and toolbar controls.
            if (!GetWindowRect(hwnd, out RECT bounds) ||
                bounds.Height < 100 || bounds.Width < 300)
                return true;

            try
            {
                foreach (TreeNode node in VisibleNodes(tree.Nodes))
                {
                    if (!node.IsVisible || node.Bounds.IsEmpty)
                        continue;
                    POINT point = new()
                    {
                        X = 0,
                        Y = tree.PointToScreen(
                            new Point(0, node.Bounds.Bottom)).Y
                    };
                    // Convert screen Y to the native dialog client Y.
                    point.X = bounds.Left;
                    if (ScreenToClient(_dialog, ref point))
                        bestBottom = Math.Max(bestBottom, point.Y);
                }
            }
            catch (Exception) { }
            return true;
        }, IntPtr.Zero);
        return bestBottom;
    }

    private static IEnumerable<TreeNode> VisibleNodes(TreeNodeCollection roots)
    {
        foreach (TreeNode root in roots)
        {
            yield return root;
            if (root.IsExpanded)
                foreach (TreeNode item in VisibleNodes(root.Nodes))
                    yield return item;
        }
    }

    private static void OpenDrawing(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The custom DWG is missing.", path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        // Standard Explorer folder link, like Autodesk's original results UI.
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true
        });
    }

    private static void DisposePanel()
    {
        try { _panel?.Dispose(); } catch { }
        _panel = null;
        _title = null;
        _folderLink = null;
    }
}
