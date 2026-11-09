using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    private static bool _nativeSeen;
    private static bool _resultsSeen;
    private static DateTime _nativeClosedAt;
    private static readonly Dictionary<string, (long Size, long Modified)> Before =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Launch(Document doc)
    {
        Detach();
        _document = doc;
        _source = null;
        _destination = "";
        Before.Clear();
        _noWindowTicks = 0;
        _nativeSeen = false;
        _resultsSeen = false;
        _nativeClosedAt = DateTime.MinValue;
        _watch = new System.Windows.Forms.Timer { Interval = 300 };
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

                string? current = ReadSourcePath(native);
                if (current != null && !current.Equals(_source, StringComparison.OrdinalIgnoreCase))
                {
                    _source = current;
                    Before.Clear();
                    foreach (var item in Snapshot(current)) Before[item.Key] = item.Value;
                    _document?.Editor.WriteMessage("\nSPTC ISO source: " + current);
                }
                PositionOverlay(native);
                return;
            }

            if (_native != IntPtr.Zero)
            {
                // Native window dismissed after Create or Cancel. Keep timer running
                // until Autodesk's asynchronous Results window is closed.
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
                // The results dialog has closed. Native ISO generation has completed.
                Detach();
                if (!string.IsNullOrWhiteSpace(_destination))
                {
                    _document?.Editor.WriteMessage("\nSPTC ISO: Results closed; exporting completed drawings...");
                    if (_document != null) CopyCompleted(_document);
                }
                else _document?.Editor.WriteMessage("\nSPTC ISO: No custom folder; ISO remains in default project folder.");
                return;
            }

            if (!_nativeSeen && ++_noWindowTicks > 150)
            {
                Detach();
                return;
            }

            // A user can cancel the native dialog; no export is performed.
            // Do not time out during normal ISO generation, which can be lengthy.
            if (_nativeClosedAt != DateTime.MinValue &&
                (DateTime.UtcNow - _nativeClosedAt).TotalMinutes > 30)
            {
                Detach();
                _document?.Editor.WriteMessage(
                    "\nSPTC ISO: Results dialog was not observed; no automatic copy performed. Use SPTC_ISOCOPY.");
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

    private static Dictionary<string, (long Size, long Modified)> Snapshot(string folder)
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return result;
        try
        {
            foreach (string path in Directory.EnumerateFiles(folder, "*.dwg", SearchOption.AllDirectories))
            {
                try
                {
                    var f = new FileInfo(path);
                    result[path] = (f.Length, f.LastWriteTimeUtc.Ticks);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    private static bool NestedOrEqual(string child, string parent)
    {
        child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
               child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static void CopyCompleted(Document doc)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_source) || !Directory.Exists(_source))
                throw new InvalidOperationException("Native Production Iso source folder was not detected. No files copied.");
            if (string.IsNullOrWhiteSpace(_destination))
            {
                doc.Editor.WriteMessage("\nSPTC ISO: No custom folder selected; default output unchanged.");
                return;
            }
            string source = Path.GetFullPath(_source), targetRoot = Path.GetFullPath(_destination);
            if (NestedOrEqual(source, targetRoot) || NestedOrEqual(targetRoot, source))
                throw new InvalidOperationException("Custom destination must be outside the native source and its parent folders.");

            int copied = 0, skipped = 0;
            foreach (var entry in Snapshot(source))
            {
                if (Before.TryGetValue(entry.Key, out var older) && older == entry.Value)
                    continue;
                string relative = Path.GetRelativePath(source, entry.Key);
                string target = Path.Combine(targetRoot, relative);
                try
                {
                    using (var lockCheck = new FileStream(entry.Key, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (File.Exists(target))
                    {
                        string name = Path.GetFileNameWithoutExtension(target);
                        string ext = Path.GetExtension(target);
                        int n = 1;
                        do { target = Path.Combine(Path.GetDirectoryName(target)!, name + "_copy" + n++ + ext); }
                        while (File.Exists(target) && n < 100000);
                    }
                    File.Copy(entry.Key, target, overwrite: false);
                    copied++;
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
            doc.Editor.WriteMessage($"\nSPTC ISO: Copied {copied} DWG(s), skipped {skipped}. Originals were not modified.");
        }
        catch (Exception ex)
        {
            doc.Editor.WriteMessage("\nSPTC ISO export: " + ex.Message);
        }
    }
}
