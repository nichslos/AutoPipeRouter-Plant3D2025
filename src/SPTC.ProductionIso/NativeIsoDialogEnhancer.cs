using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;

namespace SPTC.ProductionIso;

// Experimental Win32 child-control attachment to Autodesk's existing dialog.
// Never edits Project Setup, project files, native dialog code, or original DWGs.
internal static class NativeIsoDialogEnhancer
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsProc cb, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rectangle rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private static System.Windows.Forms.Timer? _watch;
    private static IntPtr _native = IntPtr.Zero;
    private static UserControl? _overlay;
    private static TextBox? _destinationBox;
    private static string _destination = "";
    private static string? _source;
    private static Document? _document;
    private static int _noWindowTicks;
    private static DateTime _launchUtc;
    private static Task<CopyReport>? _copyTask;
    private static bool _nativeSeen;
    private static bool _resultsSeen;
    private static DateTime _nativeClosedAt;
    private sealed record CopyReport(int Copied, int Skipped, string Destination);

    public static void Launch(Document doc)
    {
        if (_copyTask is { IsCompleted: false })
        {
            doc.Editor.WriteMessage("\nSPTC: A background ISO export is still running.");
            return;
        }
        Detach();
        _copyTask = null;
        _document = doc;
        _source = null;
        _destination = "";
        _launchUtc = DateTime.UtcNow;
        _noWindowTicks = 0;
        _nativeSeen = false;
        _resultsSeen = false;
        _nativeClosedAt = DateTime.MinValue;
        _watch = new System.Windows.Forms.Timer { Interval = 1500 };
        _watch.Tick += OnTick;
        _watch.Start();
        doc.SendStringToExecute("_.PLANTPRODUCTIONISO ", true, false, false);
        doc.Editor.WriteMessage("\nSPTC: Opening original Production Iso dialog. Custom Save Location will appear below Revision Number.");
    }

    private static string WindowText(IntPtr hwnd)
    {
        int size = GetWindowTextLength(hwnd);
        var buffer = new StringBuilder(Math.Max(size + 2, 128));
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static IntPtr FindDialog(string exactTitle)
    {
        IntPtr result = IntPtr.Zero;
        uint process = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid == process &&
                WindowText(handle).Equals(exactTitle, StringComparison.OrdinalIgnoreCase))
            {
                result = handle;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        try
        {
            // File IO runs in Task.Run; UI timer only checks window visibility.
            if (_copyTask is not null)
            {
                if (!_copyTask.IsCompleted) return;
                Task<CopyReport> completed = _copyTask;
                _copyTask = null;
                Detach();
                if (completed.IsFaulted)
                    _document?.Editor.WriteMessage("\nSPTC ISO export error: " + completed.Exception?.GetBaseException().Message);
                else if (completed.IsCanceled)
                    _document?.Editor.WriteMessage("\nSPTC ISO export canceled.");
                else
                {
                    CopyReport report = completed.Result;
                    _document?.Editor.WriteMessage(
                        $"\nSPTC ISO: {report.Copied} DWG(s) copied to {report.Destination}; {report.Skipped} skipped. Original DWGs unchanged.");
                }
                return;
            }

            IntPtr native = FindDialog("Create Production Iso");
            if (native != IntPtr.Zero)
            {
                _nativeSeen = true;
                _noWindowTicks = 0;
                if (_native != native)
                {
                    _native = native;
                    Attach(native);
                }

                // Read native folder once; do NOT scan the network on this UI timer.
                if (_source is null)
                {
                    _source = ReadSourcePath(native);
                    if (_source is not null)
                        _document?.Editor.WriteMessage("\nSPTC ISO native source: " + _source);
                }
                return;
            }

            if (_native != IntPtr.Zero)
            {
                RemoveOverlay();
                _nativeClosedAt = DateTime.UtcNow;
            }

            if (FindDialog("Isometric Creation Results") != IntPtr.Zero)
            {
                _resultsSeen = true;
                return;
            }

            if (_resultsSeen)
            {
                if (!string.IsNullOrWhiteSpace(_destination) && _document != null)
                    CopyCompleted(_document);
                else
                {
                    Detach();
                    _document?.Editor.WriteMessage("\nSPTC ISO: Custom folder empty; native default output preserved.");
                }
                return;
            }

            if (!_nativeSeen && ++_noWindowTicks > 40)
            {
                Detach();
                return;
            }

            if (_nativeClosedAt != DateTime.MinValue &&
                (DateTime.UtcNow - _nativeClosedAt).TotalMinutes > 30)
            {
                Detach();
                _document?.Editor.WriteMessage("\nSPTC ISO: No Results dialog detected; export not started.");
            }
        }
        catch (Exception ex)
        {
            Detach();
            _document?.Editor.WriteMessage("\nSPTC ISO monitor: " + ex.Message);
        }
    }

    private static string? ReadSourcePath(IntPtr dialog)
    {
        string? source = null;
        EnumChildWindows(dialog, (handle, _) =>
        {
            string text = WindowText(handle).Trim();
            if (text.Contains("ProdIsos", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("Drawings", StringComparison.OrdinalIgnoreCase))
            {
                int root = text.IndexOf(@":\", StringComparison.Ordinal);
                if (root > 0)
                {
                    int begin = root - 1;
                    string value = text.Substring(begin).Trim().Trim('"');
                    if (Path.IsPathRooted(value))
                    {
                        source = value;
                        return false;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);
        return source;
    }

    private static void Attach(IntPtr dialog)
    {
        try
        {
            _overlay = new UserControl
            {
                BackColor = SystemColors.Control,
                Size = new Size(510, 34)
            };
            var label = new Label
            {
                Text = "Custom Save Location:",
                Location = new Point(0, 7),
                Width = 141,
                AutoSize = false
            };
            _destinationBox = new TextBox { Text = _destination, Location = new Point(142, 3), Height = 24 };
            _destinationBox.TextChanged += (_, _) => _destination = _destinationBox.Text.Trim();
            var browse = new Button { Text = "...", Width = 31, Height = 25 };
            browse.Click += (_, _) =>
            {
                using var picker = new FolderBrowserDialog
                {
                    Description = "Choose destination for COPIES of completed Production Isos",
                    ShowNewFolderButton = true
                };
                if (Directory.Exists(_destination)) picker.SelectedPath = _destination;
                if (picker.ShowDialog() == DialogResult.OK)
                    _destinationBox.Text = picker.SelectedPath;
            };
            _overlay.Controls.Add(label);
            _overlay.Controls.Add(_destinationBox);
            _overlay.Controls.Add(browse);
            _overlay.Resize += (_, _) =>
            {
                int width = Math.Max(100, _overlay.Width - 181);
                _destinationBox.Width = width;
                browse.Location = new Point(146 + width, 2);
            };
            // Attach a managed child UI into the original Win32 dialog HWND.
            // No overwrite of the native window procedure and no project configuration edits.
            _ = _overlay.Handle;
            SetParent(_overlay.Handle, dialog);
            PositionOverlay(dialog);
            _overlay.Show();
            _document?.Editor.WriteMessage("\nSPTC: Original Production Iso dialog located; custom folder field attached.");
        }
        catch (Exception ex)
        {
            _document?.Editor.WriteMessage("\nSPTC: Unable to attach field: " + ex.Message);
            Detach();
        }
    }

    private static void PositionOverlay(IntPtr dialog)
    {
        if (_overlay == null || _overlay.IsDisposed || !GetClientRect(dialog, out Rectangle bounds))
            return;
        // Coordinates relative to the native dialog client area. UI can vary with scaling.
        int x = (int)(bounds.Width * 0.29);
        int y = (int)(bounds.Height * 0.384);
        int w = Math.Max(300, bounds.Width - x - 15);
        SetWindowPos(_overlay.Handle, IntPtr.Zero, x, y, w, 34, 0x0004 | 0x0040);
    }

    private static void RemoveOverlay()
    {
        _native = IntPtr.Zero;
        try { _overlay?.Dispose(); } catch { }
        _overlay = null;
        _destinationBox = null;
    }

    private static void Detach()
    {
        _watch?.Stop();
        _watch?.Dispose();
        _watch = null;
        RemoveOverlay();
    }

    // Runs exclusively on a worker thread. No Autodesk API calls occur here.
    private static CopyReport CopyFiles(string source, string targetRoot, DateTime sinceUtc)
    {
        int copied = 0, skipped = 0;
        // Restrict to the native source folder only, not its entire project directory.
        // LastWriteTime is a best-effort filter: avoid concurrent ISO generation in this folder.
        foreach (string file in Directory.EnumerateFiles(source, "*.dwg", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < sinceUtc.AddSeconds(-5)) continue;
                using (var readLock = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }

                string relative = Path.GetRelativePath(source, file);
                string destination = Path.Combine(targetRoot, relative);
                string directory = Path.GetDirectoryName(destination)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(destination))
                {
                    string stem = Path.GetFileNameWithoutExtension(destination);
                    string ext = Path.GetExtension(destination);
                    int n = 1;
                    while (File.Exists(destination))
                    {
                        if (n > 100000) throw new IOException("Too many duplicate output names.");
                        destination = Path.Combine(directory, stem + "_copy" + n++ + ext);
                    }
                }
                File.Copy(file, destination, false);
                copied++;
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        return new CopyReport(copied, skipped, targetRoot);
    }

    private static void EnsureMonitor()
    {
        if (_watch != null) return;
        _watch = new System.Windows.Forms.Timer { Interval = 1500 };
        _watch.Tick += OnTick;
        _watch.Start();
    }

    public static void CopyCompleted(Document doc)
    {
        if (_copyTask is { IsCompleted: false })
        {
            doc.Editor.WriteMessage("\nSPTC ISO: Export is already running in the background.");
            return;
        }
        if (_source is null || string.IsNullOrWhiteSpace(_destination))
        {
            doc.Editor.WriteMessage("\nSPTC ISO: Missing source or custom destination; no copy attempted.");
            Detach();
            return;
        }
        try
        {
            string source = Path.GetFullPath(_source);
            string destination = Path.GetFullPath(_destination);
            // Never copy into/from a parent folder to prevent recursion.
            if (NestedOrEqual(source, destination) || NestedOrEqual(destination, source))
                throw new InvalidOperationException("The custom folder must be separate from the native ISO folder.");
            // Validation and recursive enumeration are on the worker, not the CAD UI thread.
            DateTime start = _launchUtc;
            _copyTask = Task.Run(() => CopyFiles(source, destination, start));
            EnsureMonitor();
            doc.Editor.WriteMessage("\nSPTC ISO: Background copy started. Plant 3D remains responsive.");
        }
        catch (Exception ex)
        {
            doc.Editor.WriteMessage("\nSPTC ISO copy not started: " + ex.Message);
            Detach();
        }
    }
}
