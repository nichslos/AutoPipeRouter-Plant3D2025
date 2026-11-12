using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

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
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    private const uint WM_CLOSE = 0x0010;
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
    private static string _customFolder = "";
    private static readonly List<string> CopiedPaths = new();
    // REV54: separate scroll area reserved below the original results pane.
    private static IntPtr _nativeResultsPane;
    private static IntPtr _nativePaneParent;
    private static int _nativeLeft, _nativeTop, _nativeRightMargin, _nativeBottomMargin;
    private static readonly HashSet<string> ExpectedIsoTags = new(StringComparer.OrdinalIgnoreCase);
    // REV49: preserve links if Autodesk's Results form is closed then
    // reopened via its original DWG output. Poll only while this ISO
    // session is active; no secondary results window is ever created.
    private static string? _pendingOpen;
    private static DateTime _pendingCloseTime;

    public static void Reset()
    {
        try { _timer?.Stop(); _timer?.Dispose(); } catch { }
        _timer = null;
        DisposePanel();
        _dialog = IntPtr.Zero;
        _customFolder = "";
        CopiedPaths.Clear();
        ExpectedIsoTags.Clear();
        _nativeResultsPane = IntPtr.Zero;
        _nativePaneParent = IntPtr.Zero;
        _pendingOpen = null;
        _pendingCloseTime = default;
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

        // Unlike REV48, keep watching after the FIRST results window
        // closes so links return if Autodesk reopens its results panel.
        _timer = new System.Windows.Forms.Timer { Interval = 650 };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        OnTick();
    }

    // Restrict links/copies to the verified current native ISO selection.
    public static void SetExpectedIsoTags(IEnumerable<string> tags)
    {
        ExpectedIsoTags.Clear();
        foreach (string tag in tags)
            if (!string.IsNullOrWhiteSpace(tag))
                ExpectedIsoTags.Add(tag.Trim());
    }

    public static bool IsExpectedIsoOutput(string fileName)
    {
        if (ExpectedIsoTags.Count == 0) return true;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        foreach (string tag in ExpectedIsoTags)
        {
            if (stem.Equals(tag, StringComparison.OrdinalIgnoreCase)) return true;
            if (stem.StartsWith(tag + " (", StringComparison.OrdinalIgnoreCase) &&
                stem.EndsWith(")", StringComparison.Ordinal) &&
                int.TryParse(stem.Substring(tag.Length + 2,
                    stem.Length - tag.Length - 3), out int number) && number > 0)
                return true;
            if (stem.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(stem.Substring(tag.Length + 1), out int another) && another > 0)
                return true;
        }
        return false;
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
            !IsExpectedIsoOutput(Path.GetFileName(full)) ||
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
        if (_timer == null)
            return;

        IntPtr current = FindResults();
        if (_panel is { IsDisposed: false } &&
            (_dialog == IntPtr.Zero || !IsWindow(_dialog) ||
             current != _dialog))
        {
            DisposePanel();
            _dialog = IntPtr.Zero;
        }

        // REV49: clicking a custom DWG sends WM_CLOSE first. Wait until
        // the original Autodesk modal Results dialog really disappears
        // before opening a DWG in this SAME Plant 3D session.
        if (_pendingOpen is { } path)
        {
            if (current == IntPtr.Zero &&
                DateTime.UtcNow - _pendingCloseTime >
                    TimeSpan.FromMilliseconds(200))
            {
                _pendingOpen = null; // exactly once
                OpenDrawingInPlant(path);
                return;
            }

            // If a nonstandard Autodesk results window refuses to close,
            // never force a DWG open behind the still-modal dialog.
            if (DateTime.UtcNow - _pendingCloseTime >
                TimeSpan.FromSeconds(8))
                _pendingOpen = null;
            return;
        }

        if (current != IntPtr.Zero)
        {
            if (_panel is null)
                Attach(current);
            else
                Position();
        }
        // Do NOT stop this session's timer after the first Results window
        // closes. Reopened native result forms get a fresh overlay showing
        // the SAME verified custom files. Launch() resets the session.
    }

    private static void Attach(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out RECT area) ||
            area.Width < 350 || area.Height < 260)
            return;

        try
        {
            if (!CaptureNativeResultsPane(hwnd))
                return; // unrecognized native UI: never cover original rows
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
            if (GetParent(_panel.Handle) != hwnd)
                throw new InvalidOperationException("Could not attach custom results.");
            _panel.Show();
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


    // REV54: detect the original large, scrollable white native Results pane
    // using bounds, not the variable Y of the last child/PCF. Its height is
    // reserved for a separate custom scroll section underneath it.
    private static bool CaptureNativeResultsPane(IntPtr dialog)
    {
        if (!GetClientRect(dialog, out RECT client))
            return false;

        double best = double.MaxValue;
        IntPtr selected = IntPtr.Zero;
        int left = 0, top = 0, right = 0, bottom = 0;

        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT screen))
                return true;

            POINT a = new() { X = screen.Left, Y = screen.Top };
            POINT b = new() { X = screen.Right, Y = screen.Bottom };
            if (!ScreenToClient(dialog, ref a) ||
                !ScreenToClient(dialog, ref b))
                return true;

            int w = b.X - a.X, h = b.Y - a.Y;
            if (w < client.Width * 0.65 ||
                h < client.Height * 0.48 ||
                a.Y < 25 || a.Y > client.Height * 0.33 ||
                a.X < 5 || a.X > client.Width * 0.20 ||
                b.Y > client.Height - 25)
                return true;

            // The original results region is near 20px left, 75px top,
            // filling the white interior above the bottom command buttons.
            double score =
                Math.Abs(a.X - 24) * 1.5 +
                Math.Abs(a.Y - 45) * 1.8 +
                Math.Abs(b.X - (client.Width - 23)) +
                Math.Abs(b.Y - (client.Height - 60)) * 0.8;
            Control? managed = Control.FromHandle(hwnd);
            if (managed is TreeView || managed is ListView)
                score -= 30;

            if (score < best)
            {
                best = score;
                selected = hwnd;
                left = a.X; top = a.Y;
                right = client.Width - b.X;
                bottom = client.Height - b.Y;
            }
            return true;
        }, IntPtr.Zero);

        if (selected == IntPtr.Zero)
            return false; // safe: do not cover the original native links

        _nativeResultsPane = selected;
        _nativePaneParent = GetParent(selected);
        _nativeLeft = left;
        _nativeTop = top;
        _nativeRightMargin = right;
        _nativeBottomMargin = bottom;
        return _nativePaneParent != IntPtr.Zero;
    }

    private static void Position()
    {
        if (_panel is not { IsDisposed: false } panel ||
            _dialog == IntPtr.Zero ||
            _nativeResultsPane == IntPtr.Zero ||
            _nativePaneParent == IntPtr.Zero ||
            !IsWindow(_nativeResultsPane) ||
            !GetClientRect(_dialog, out RECT client))
            return;

        int fullHeight = Math.Max(0,
            client.Height - _nativeTop - _nativeBottomMargin);
        int width = Math.Max(0,
            client.Width - _nativeLeft - _nativeRightMargin);
        if (fullHeight < 220 || width < 300)
        {
            panel.Hide();
            return;
        }

        // The native results list receives its own shorter viewport,
        // with scrollbars still managed by Autodesk. The custom list gets
        // the reserved bottom strip, so six or sixty original results will
        // NEVER be covered by custom links.
        int reserve = Math.Min(155, fullHeight / 3);
        int nativeHeight = fullHeight - reserve - 5;

        if (GetWindowRect(_nativeResultsPane, out RECT originalScreen))
        {
            POINT relative = new() {
                X = originalScreen.Left, Y = originalScreen.Top };
            if (ScreenToClient(_nativePaneParent, ref relative))
                SetWindowPos(_nativeResultsPane, IntPtr.Zero,
                    relative.X, relative.Y, width, nativeHeight,
                    SWP_NOACTIVATE | SWP_NOZORDER);
        }

        SetWindowPos(panel.Handle, IntPtr.Zero,
            _nativeLeft, _nativeTop + nativeHeight + 5,
            width, reserve - 5,
            SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);
        panel.Show();
        panel.BringToFront();
        foreach (Control item in panel.Controls)
            item.Width = Math.Max(150, width - 22);
    }

    // REV49: just like Autodesk's original DWG result link, close the
    // current modal results dialog BEFORE opening the custom DWG.
    // Process.Start on the file from inside the modal form (REV48) deferred
    // opening until the user manually closed the Results dialog.
    private static void OpenDrawing(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The custom DWG is missing.", path);
        if (_pendingOpen != null)
            return;

        IntPtr hwnd = _dialog;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            throw new InvalidOperationException(
                "The Autodesk results dialog is no longer available.");

        _pendingOpen = path;
        _pendingCloseTime = DateTime.UtcNow;

        // Post asynchronously to avoid modal WinForms re-entrancy during
        // the LinkClicked handler. Do not close any other dialog/window.
        if (!PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
        {
            _pendingOpen = null;
            throw new InvalidOperationException(
                "The Autodesk results dialog could not be closed.");
        }
    }

    // REV50: Use Autodesk's managed document-opening API instead of
    // sending the unsupported -OPEN command. AutoCAD's official method is
    // DocumentCollectionExtension.Open(DocumentCollection,string,bool).
    // The lightweight NuGet compile stub lacks that extension, so resolve
    // it from the REAL AutoCAD runtime assembly using reflection.
    // This code runs only after the native modal Results form has closed.
    private static void OpenDrawingInPlant(string path)
    {
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "The custom DWG is missing.", path);

            var documents = AcadApp.DocumentManager;
            var existing = documents
                .Cast<Autodesk.AutoCAD.ApplicationServices.Document>()
                .FirstOrDefault(doc => string.Equals(
                    Path.GetFullPath(doc.Name),
                    Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                documents.MdiActiveDocument = existing;
                return;
            }

            // Autodesk runtime API is installed with Plant 3D even though
            // AutoCAD.NET 25.0.1 reference package does not expose all
            // DocumentCollection extension methods to GitHub CI.
            Type? extensionType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(
                    "Autodesk.AutoCAD.ApplicationServices.DocumentCollectionExtension",
                    throwOnError: false))
                .FirstOrDefault(type => type is not null);

            if (extensionType is null)
                throw new MissingMethodException(
                    "Autodesk runtime DocumentCollectionExtension unavailable.");

            var method = extensionType
                .GetMethods(System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "Open")
                        return false;
                    var parameters = m.GetParameters();
                    return parameters.Length == 3 &&
                           parameters[0].ParameterType.IsInstanceOfType(documents) &&
                           parameters[1].ParameterType == typeof(string) &&
                           parameters[2].ParameterType == typeof(bool);
                });

            if (method is null)
                throw new MissingMethodException(
                    "Autodesk runtime DocumentCollectionExtension.Open was not found.");

            object? result = method.Invoke(
                null, new object[] { documents, path, false });
            if (result is Autodesk.AutoCAD.ApplicationServices.Document opened)
                documents.MdiActiveDocument = opened;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not open the custom DWG in Plant 3D:\n" +
                ex.GetBaseException().Message,
                "SPTC Custom ISO Link", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
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
        _nativeResultsPane = IntPtr.Zero;
        _nativePaneParent = IntPtr.Zero;
    }
}
