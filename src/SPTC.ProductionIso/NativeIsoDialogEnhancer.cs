using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WinRect rect);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref WinPoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint { public int X, Y; }

    private static System.Windows.Forms.Timer? _watch;
    private static IntPtr _native = IntPtr.Zero;
    private static UserControl? _overlay;
    private static Label? _destinationLabel;
    private static TextBox? _destinationBox;
    private static Button? _destinationBrowse;
    // REV19: inexpensive layout watcher; it only relayouts if native client
    // dimensions change. It never queries PnP databases or project files.
    private static System.Windows.Forms.Timer? _layoutWatch;
    private static System.Windows.Forms.Timer? _activeFilterVerify;
    private static Size _lastDialogClientSize = Size.Empty;
    private static int _fallbackRowLeft = -1;
    private static int _fallbackTextboxLeft = -1;
    private static int _fallbackRowTop = -1;
    private static string _destination = "";
    private static string? _source;
    private static Document? _document;
    private static int _noWindowTicks;
    private static DateTime _launchUtc;
    private static Task<CopyReport>? _copyTask;
    // REV33: background worker reports each completed DWG immediately
    // without calling any AutoCAD APIs from the worker thread.
    private static readonly ConcurrentQueue<string> _copyProgress = new();
    private static string _copyState = "Not started";
    private static CopyReport? _lastCopyReport;
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
        while (_copyProgress.TryDequeue(out _)) { }
        _copyState = "Waiting for Production Iso dialog";
        _lastCopyReport = null;
        _document = doc;
        _source = null;
        _destination = "";
        _launchUtc = DateTime.UtcNow;
        _noWindowTicks = 0;
        _nativeSeen = false;
        _resultsSeen = false;
        _nativeClosedAt = DateTime.MinValue;
        // REV09: discover active DWG line-group tags but do not filter
        // the native list until the user explicitly clicks a top mode icon.
        // REV22: do not scan every model component while opening the dialog.
        // Read current piping objects on the first requested active-DWG view.
        ActiveDwgTags.Invalidate();
        // REV27: 1.5 s polling alone could defer enhancement placement.
        // Detect the native window promptly without touching project data.
        _watch = new System.Windows.Forms.Timer { Interval = 150 };
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
            // REV33: the user sees each destination DWG as soon as its
            // atomic copy finishes; the batch watcher may remain alive to
            // collect the rest of a multi-line Production Iso job.
            while (_copyProgress.TryDequeue(out string? savedName))
            {
                _copyState = "Copied " + savedName + "; monitoring remaining ISOs";
                _document?.Editor.WriteMessage(
                    "\nSPTC REV33: Custom ISO ready: " +
                    Path.Combine(_destination, savedName));
            }

            if (_copyTask is not null && _copyTask.IsCompleted)
            {
                Task<CopyReport> finished = _copyTask;
                _copyTask = null;
                Detach();
                if (finished.IsFaulted)
                {
                    _copyState = "Error: " + finished.Exception?.GetBaseException().Message;
                    _document?.Editor.WriteMessage("\nSPTC ISO export: " + _copyState);
                }
                else if (finished.IsCanceled)
                {
                    _copyState = "Canceled";
                    _document?.Editor.WriteMessage("\nSPTC ISO export canceled.");
                }
                else
                {
                    _lastCopyReport = finished.Result;
                    _copyState = "Completed";
                    _document?.Editor.WriteMessage(
                        $"\nSPTC ISO: {_lastCopyReport.Copied} DWG(s) copied to {_lastCopyReport.Destination}; " +
                        $"{_lastCopyReport.Skipped} skipped. Original project files unchanged.");
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
                    // Tell apart Autodesk's own dialog load from plugin UI
                    // attachment time when a project feels slow.
                    var elapsedToNative = DateTime.UtcNow - _launchUtc;
                    var attachWatch = Stopwatch.StartNew();
                    _native = native;
                    Attach(native);
                    attachWatch.Stop();

                    _document?.Editor.WriteMessage(
                        "\nSPTC REV27: Native dialog detected after " +
                        elapsedToNative.TotalMilliseconds.ToString("F0") +
                        " ms; enhancements attached in " +
                        attachWatch.ElapsedMilliseconds + " ms.");

                    // Once the dialog exists, use slower monitoring. No need
                    // to enumerate desktop HWNDs 6 times/second thereafter.
                    if (_watch != null) _watch.Interval = 1000;

                    // REV30: Keep the REV24 three-button Active/All toggle
                    // but STOP showing a second CheckedListBox for Active DWG.
                    // A proxy checkbox cannot reliably enable Autodesk's
                    // native Create button. Instead filter the ORIGINAL
                    // Autodesk line list using its own Filter textbox.
                    // The native row stays click/checkbox/validation-capable.
                    NativeIsoToolbarClickFilter.Attach(native, showActive =>
                    {
                        if (_document is null) return false;

                        // A previous folder projection may still be present.
                        // Remove it so the user's pointer reaches Autodesk's
                        // genuine line list, never an overlaid WinForms copy.
                        ProjectFolderLinePicker.ClearVisibleProjection();

                        if (!showActive)
                        {
                            // REV31: put ORIGINAL native row objects back
                            // BEFORE Autodesk's Filter is cleared. The
                            // Create button still uses its original list.
                            NativeIsoExactLineFilter.End(restore: true);
                            bool cleared = ActiveDwgTags.ClearOneTag(native, _document);
                            if (cleared)
                                NativeIsoLineSelectOverlay.UpdateDisplayedCount();
                            return cleared;
                        }

                        if (!ActiveDwgTags.HasScanned)
                            ActiveDwgTags.Scan(_document);

                        if (ActiveDwgTags.Count != 1)
                        {
                            _document.Editor.WriteMessage(
                                "\nSPTC REV30: Native active filtering needs exactly " +
                                "one verified physical line tag. Found " +
                                ActiveDwgTags.Count + ". Full list was left unchanged.");
                            return false;
                        }

                        bool applied = ActiveDwgTags.ApplyOneTag(native, _document);
                        if (applied)
                        {
                            // REV31: Autodesk's native Filter does a broad
                            // substring match (A-08 also returns A-088 and
                            // CAP...+A-088). Reversibly remove nonexact rows
                            // from the SAME original checked-list control.
                            NativeIsoExactLineFilter.Begin(
                                native, ActiveDwgTags.CurrentTags[0], _document);
                            _document.Editor.WriteMessage(
                                "\nSPTC REV31: Exact match requested for " +
                                ActiveDwgTags.CurrentTags[0] +
                                "; preserving the real Autodesk checklist/Create.");
                        }
                        return applied;
                    });

                }

                // Path discovery only; never enumerate network DWGs on the UI thread.
                if (string.IsNullOrEmpty(_source))
                {
                    _source = ReadSourcePath(native);
                    if (!string.IsNullOrEmpty(_source))
                        _document?.Editor.WriteMessage("\nSPTC ISO detected native source: " + _source);
                }
                return;
            }

            if (_native != IntPtr.Zero)
            {
                RemoveOverlay();
                _nativeClosedAt = DateTime.UtcNow;
                _copyState = "Native dialog closed; watching for completed DWGs";
            }

            // Autodesk may display a notification first and open this dialog
            // only after the user clicks the notification. Do not require that.
            IntPtr results = FindDialog("Isometric Creation Results");
            if (results != IntPtr.Zero)
            {
                _resultsSeen = true;
                if (_copyTask is null && _document is not null &&
                    !string.IsNullOrWhiteSpace(_destination) && !string.IsNullOrWhiteSpace(_source))
                    CopyCompleted(_document);
            }

            // Start background detection immediately after the original Create dialog
            // closes; copying itself waits for a stable, unlocked output DWG.
            if (_nativeClosedAt != DateTime.MinValue && _copyTask is null)
            {
                if (!string.IsNullOrWhiteSpace(_destination) && string.IsNullOrWhiteSpace(_source))
                {
                    Detach();
                    _copyState = "Error: Native source folder was not detected";
                    _document?.Editor.WriteMessage("\nSPTC ISO: " + _copyState +
                        ". Run SPTC_ISOSTATUS for details.");
                    return;
                }
                if (!string.IsNullOrWhiteSpace(_destination) && _document is not null)
                {
                    CopyCompleted(_document);
                    return;
                }
                Detach();
                _copyState = "Custom location empty; default output retained";
                _document?.Editor.WriteMessage("\nSPTC ISO: " + _copyState);
                return;
            }

            if (!_nativeSeen && ++_noWindowTicks > 40)
            {
                Detach();
                _copyState = "Native Production Iso dialog was not found";
            }
        }
        catch (Exception ex)
        {
            Detach();
            _copyState = "Error: " + ex.Message;
            _document?.Editor.WriteMessage("\nSPTC ISO monitor: " + ex.Message);
        }
    }

    // REV27: native filter remains the authority. If the Autodesk filter
    // doesn't narrow its OWN list to the exact tag, restore All mode instead
    // of presenting stale/partial route names as safe to generate.
    private static void VerifyNativeFilteredLineLater(IntPtr dialog, string tag)
    {
        CancelFilterVerification();
        var timer = new System.Windows.Forms.Timer { Interval = 650 };
        _activeFilterVerify = timer;
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_activeFilterVerify, timer)) return;
            CancelFilterVerification();
            if (_native != dialog || ProjectFolderLinePicker.HasVisibleProjection)
                return;

            if (!ProjectFolderLinePicker.VerifyNativeExactLineFilter(tag, out string note))
            {
                // Do NOT clear the user's native filter automatically here.
                // Autodesk may still be loading entries after the first
                // repaint; clearing would cause the old routes to reappear
                // even though the user is actively trying to select a line.
                _document?.Editor.WriteMessage(
                    "\nSPTC REV27: Active filter verification inconclusive: " +
                    note + " Inspect the native line list before creating an ISO.");
            }
            else
                _document?.Editor.WriteMessage(
                    "\nSPTC REV27: Native active route " + tag +
                    " displayed in the original selectable Autodesk checklist.");
            NativeIsoLineSelectOverlay.UpdateDisplayedCount();
        };
        timer.Start();
    }

    private static void CancelFilterVerification()
    {
        try { _activeFilterVerify?.Stop(); } catch { }
        try { _activeFilterVerify?.Dispose(); } catch { }
        _activeFilterVerify = null;
    }

    private static void VerifyFilterHasVisibleLines(IntPtr native)
    {
        CancelFilterVerification();
        var timer = new System.Windows.Forms.Timer { Interval = 900 };
        _activeFilterVerify = timer;
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_activeFilterVerify, timer)) return;
            CancelFilterVerification();
            if (_native != native || ProjectFolderLinePicker.FolderActive)
                return;

            int? count = NativeIsoLineSelectOverlay.NativeDisplayedCount(native);
            if (count != 0) return; // no read or one/more results: leave native UI alone

            // Only undo a failed automatic filter; no line tags, DWGs,
            // drawing-folder data or project databases are modified.
            if (ActiveDwgTags.ClearOneTag(native, _document))
            {
                NativeIsoToolbarClickFilter.MarkAllLinesRestored(native);
                NativeIsoLineSelectOverlay.UpdateDisplayedCount();
                _document?.Editor.WriteMessage(
                    "\nSPTC REV21: Current DWG tag did not match a line in the " +
                    "native ISO list. Cleared the search filter so routes remain visible.");
            }
        };
        timer.Start();
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
                Size = new Size(510, 34),
                TabStop = false
            };
            _destinationLabel = new Label
            {
                Text = "Custom Save Location:",
                Location = new Point(0, 7),
                Width = 141,
                Height = 22,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = SystemFonts.MessageBoxFont
            };
            _destinationBox = new TextBox
            {
                Text = _destination,
                Location = new Point(142, 3),
                Height = 24,
                Font = SystemFonts.MessageBoxFont
            };
            _destinationBox.TextChanged += (_, _) => _destination = _destinationBox.Text.Trim();
            _destinationBrowse = new Button
            {
                Text = "...",
                Width = 31,
                Height = 25,
                TabStop = false
            };
            _destinationBrowse.Click += (_, _) =>
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

            _overlay.Controls.Add(_destinationLabel);
            _overlay.Controls.Add(_destinationBox);
            _overlay.Controls.Add(_destinationBrowse);

            // Attach a managed child UI into the ORIGINAL Production Iso dialog.
            // No edits to native ISO configuration, project data or drawings.
            _ = _overlay.Handle;
            SetParent(_overlay.Handle, dialog);
            PositionOverlay(dialog);
            _overlay.Show();

            ProjectFolderLinePicker.Attach(dialog, _document);
            NativeIsoLineSelectOverlay.Attach(dialog);
            // REV20: avoid REV18's eager scan. Folder metadata/line numbers
            // are queried only AFTER the user chooses a folder.
            // Widen native Display lines through a draggable splitter when
            // Autodesk exposes compatible WinForms sibling group controls.
            NativeIsoLinePaneResizer.Attach(dialog, () => PositionOverlay(dialog));
            PositionOverlay(dialog);

            // Win32 SetParent does not guarantee that the child receives
            // Autodesk's parent-resize messages. Watch only CLIENT DIMENSIONS,
            // then calculate custom field geometry from the native Revision row.
            _lastDialogClientSize = Size.Empty;
            _layoutWatch?.Dispose();
            _layoutWatch = new System.Windows.Forms.Timer { Interval = 450 };
            _layoutWatch.Tick += (_, _) =>
            {
                if (_native != dialog || _overlay is null || _overlay.IsDisposed)
                    return;
                if (GetClientRect(dialog, out Rectangle nativeClient))
                {
                    Size size = new Size(nativeClient.Width, nativeClient.Height);
                    if (_lastDialogClientSize != size)
                    {
                        _lastDialogClientSize = size;
                        NativeIsoLinePaneResizer.Refresh();
                        PositionOverlay(dialog);
                    }
                }
            };
            _layoutWatch.Start();

            _document?.Editor.WriteMessage(
                "\nSPTC REV19: Native Production Iso dialog found; custom location row tracks Revision number when resized.");
        }
        catch (Exception ex)
        {
            _document?.Editor.WriteMessage("\nSPTC: Unable to attach field: " + ex.Message);
            Detach();
        }
    }

    /// <summary>
    /// Determine native Revision label and textbox coordinates without
    /// changing either Autodesk control. This gives stable alignment even
    /// when Windows display scaling or dialog width differs.
    /// </summary>
    private static bool TryRevisionRow(IntPtr dialog, out Rectangle label, out Rectangle edit)
    {
        label = Rectangle.Empty;
        edit = Rectangle.Empty;

        IntPtr nativeLabel = IntPtr.Zero;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (WindowText(hwnd).Trim().StartsWith("Revision number",
                    StringComparison.OrdinalIgnoreCase))
            {
                nativeLabel = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (nativeLabel == IntPtr.Zero || !TryClientRect(dialog, nativeLabel, out Rectangle labelRect))
            return false;

        label = labelRect;
        Rectangle chosen = Rectangle.Empty;
        int bestScore = int.MinValue;
        int labelCenterY = label.Top + label.Height / 2;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (hwnd == nativeLabel || !TryClientRect(dialog, hwnd, out Rectangle rect))
                return true;
            if (rect.Width < 110 || rect.Height < 15 || rect.Height > 43)
                return true;
            if (rect.Left < labelRect.Left + 90 ||
                Math.Abs(rect.Top + rect.Height / 2 - labelCenterY) > 17)
                return true;

            Control? control = Control.FromHandle(hwnd);
            var cls = new StringBuilder(128);
            GetClassName(hwnd, cls, cls.Capacity);
            bool isEdit = control is TextBoxBase ||
                          cls.ToString().Contains("EDIT", StringComparison.OrdinalIgnoreCase);
            if (!isEdit) return true;

            // Native Revision entry is the long textbox on the same row.
            int score = rect.Width * 2 - Math.Abs(rect.Top + rect.Height / 2 - labelCenterY);
            if (score > bestScore)
            {
                bestScore = score;
                chosen = rect;
            }
            return true;
        }, IntPtr.Zero);

        if (chosen.IsEmpty) return false;
        edit = chosen;
        return true;
    }

    private static bool TryClientRect(IntPtr dialog, IntPtr hwnd, out Rectangle clientRect)
    {
        clientRect = Rectangle.Empty;
        if (!GetWindowRect(hwnd, out WinRect rect)) return false;
        var a = new WinPoint { X = rect.Left, Y = rect.Top };
        var b = new WinPoint { X = rect.Right, Y = rect.Bottom };
        if (!ScreenToClient(dialog, ref a) || !ScreenToClient(dialog, ref b))
            return false;
        clientRect = Rectangle.FromLTRB(a.X, a.Y, b.X, b.Y);
        return clientRect.Width > 0 && clientRect.Height > 0;
    }

    private static void PositionOverlay(IntPtr dialog)
    {
        if (_overlay is null || _overlay.IsDisposed ||
            _destinationLabel is null || _destinationBox is null ||
            _destinationBrowse is null ||
            !GetClientRect(dialog, out Rectangle bounds))
            return;

        int rowLeft, fieldLeft, rowTop, rightEdge;
        if (TryRevisionRow(dialog, out Rectangle revisionLabel, out Rectangle revisionInput))
        {
            // Native labels share the exact same LEFT edge. Both native and
            // custom textboxes share the same LEFT edge too.
            rowLeft = revisionLabel.Left;
            fieldLeft = revisionInput.Left;
            rowTop = revisionInput.Bottom + 9;
            rightEdge = revisionInput.Right;
        }
        else
        {
            // Fallback if Autodesk renders labels without child HWNDs.
            // Capture the original REV12 alignment once and keep left edges
            // fixed while stretching the parent. Right edge expands with it.
            if (_fallbackRowLeft < 0)
            {
                _fallbackRowLeft = Math.Max(0, (int)(bounds.Width * 0.29) - 28);
                _fallbackTextboxLeft = _fallbackRowLeft + 142;
                _fallbackRowTop = (int)(bounds.Height * 0.384);
            }
            rowLeft = _fallbackRowLeft;
            fieldLeft = _fallbackTextboxLeft;
            rowTop = _fallbackRowTop;
            rightEdge = bounds.Width - 15;
        }

        int firstField = Math.Max(136, fieldLeft - rowLeft);
        int width = Math.Max(240, rightEdge - rowLeft);
        // Align the right edge of Browse with native Revision number textbox.
        int browseX = Math.Max(firstField + 110, width - 31);
        int actualWidth = Math.Max(width, browseX + 31);

        // Resize BOTH the Win32 child window AND its managed controls.
        // Updating controls directly avoids dependence on a missed Resize event
        // after SetParent/SetWindowPos in Autodesk's native window.
        _overlay.Size = new Size(actualWidth, 34);
        _destinationLabel.Location = new Point(0, 3);
        _destinationLabel.Size = new Size(Math.Max(100, firstField - 5), 26);
        _destinationBox.Location = new Point(firstField, 3);
        _destinationBox.Width = Math.Max(100, browseX - firstField - 5);
        _destinationBrowse.Location = new Point(browseX, 2);

        SetWindowPos(_overlay.Handle, IntPtr.Zero,
            rowLeft, rowTop, actualWidth, 34, 0x0004 | 0x0040);
        NativeIsoLineSelectOverlay.Position(dialog);
        ProjectFolderLinePicker.Position(dialog);
    }

    private static void RemoveOverlay()
    {
        // Dispose timer immediately when the native dialog closes;
        // do not restore rows into a closing or disposed Autodesk window.
        NativeIsoExactLineFilter.End(restore: false);
        CancelFilterVerification();
        try { _layoutWatch?.Stop(); } catch { }
        try { _layoutWatch?.Dispose(); } catch { }
        _layoutWatch = null;
        _lastDialogClientSize = Size.Empty;
        _fallbackRowLeft = -1;
        _fallbackTextboxLeft = -1;
        _fallbackRowTop = -1;
        NativeIsoLinePaneResizer.Hide();
        ProjectFolderLinePicker.Hide();
        NativeIsoToolbarClickFilter.Detach();
        NativeIsoLineSelectOverlay.Hide();
        _native = IntPtr.Zero;
        try { _overlay?.Dispose(); } catch { }
        _overlay = null;
        _destinationLabel = null;
        _destinationBox = null;
        _destinationBrowse = null;
    }

    private static void Detach()
    {
        _watch?.Stop();
        _watch?.Dispose();
        _watch = null;
        RemoveOverlay();
    }

    private static bool NestedOrEqual(string child, string parent)
    {
        child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
               child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // REV33: Event-assisted, safe near-immediate copying. In REV32 the
    // worker polled every 2.5 seconds and then waited 3 more seconds for
    // each source DWG to look stable. A completed ISO could therefore
    // take 5–8+ seconds to appear in Custom Save Location.
    //
    // The FileSystemWatcher wakes the worker promptly when output changes;
    // a 650 ms fallback poll also works on mapped/network drives when
    // filesystem notifications are unavailable or lost.
    //
    // A DWG is copied ONLY after its size and LastWriteTime remain stable
    // for 650 ms AND an exclusive FileStream opens successfully. The file
    // remains locked against writers during the entire copy, and the
    // destination becomes visible atomically after a .sptc-part transfer.
    //
    // No Windows filesystem I/O or copy runs on the Plant 3D UI thread.
    private static async Task<CopyReport> CopyFilesAsync(
        string source, string targetRoot, DateTime sinceUtc)
    {
        int copied = 0, skipped = 0;
        var copiedVersions =
            new Dictionary<string, (long Size, long Modified)>(
                StringComparer.OrdinalIgnoreCase);
        var observed =
            new Dictionary<string, (long Size, long Modified, DateTime SeenAt)>(
                StringComparer.OrdinalIgnoreCase);
        DateTime start = DateTime.UtcNow;
        DateTime lastCopy = start;

        // Coalesce frequent filesystem events; never process a DWG from
        // the FileSystemWatcher callback itself.
        using var wake = new System.Threading.SemaphoreSlim(0, 1);
        void Signal()
        {
            try { wake.Release(); }
            catch (System.Threading.SemaphoreFullException) { }
            catch (ObjectDisposedException) { }
        }

        FileSystemWatcher? watcher = null;
        try
        {
            if (Directory.Exists(source))
            {
                try
                {
                    watcher = new FileSystemWatcher(source, "*.dwg")
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName |
                                       NotifyFilters.LastWrite |
                                       NotifyFilters.Size |
                                       NotifyFilters.CreationTime
                    };
                    FileSystemEventHandler changed = (_, _) => Signal();
                    RenamedEventHandler renamed = (_, _) => Signal();
                    ErrorEventHandler error = (_, _) => Signal();
                    watcher.Created += changed;
                    watcher.Changed += changed;
                    watcher.Renamed += renamed;
                    watcher.Error += error;
                    watcher.EnableRaisingEvents = true;
                }
                catch (Exception ex) when (ex is IOException ||
                                           ex is UnauthorizedAccessException ||
                                           ex is ArgumentException)
                {
                    // SMB/network notifications may be unavailable. Polling
                    // still detects completed ISO files safely.
                    watcher?.Dispose();
                    watcher = null;
                }
            }

            // One batch may generate multiple DWGs. Continue monitoring
            // for 25 seconds after the last successful copy, as in REV32.
            while ((DateTime.UtcNow - start).TotalMinutes < 10)
            {
                try
                {
                    if (!Directory.Exists(source))
                        throw new DirectoryNotFoundException(
                            "Native Production Iso output folder is unavailable: " + source);

                    foreach (string file in Directory.EnumerateFiles(
                        source, "*.dwg", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.LastWriteTimeUtc < sinceUtc.AddSeconds(-10))
                                continue;

                            var version = (info.Length, info.LastWriteTimeUtc.Ticks);
                            if (copiedVersions.TryGetValue(file, out var done) &&
                                done == version)
                                continue;

                            if (!observed.TryGetValue(file, out var first) ||
                                first.Size != version.Item1 ||
                                first.Modified != version.Item2)
                            {
                                observed[file] = (
                                    version.Item1, version.Item2, DateTime.UtcNow);
                                continue;
                            }

                            if ((DateTime.UtcNow - first.SeenAt).TotalMilliseconds < 650)
                                continue;

                            // The exclusive native handle is kept open for
                            // the ENTIRE copy. A still-writing ISO is skipped
                            // and retried; never copy a partial DWG.
                            using var input = new FileStream(
                                file, FileMode.Open, FileAccess.Read,
                                FileShare.None, 128 * 1024, FileOptions.SequentialScan);
                            if (input.Length == 0 || input.Length != version.Item1)
                                continue;

                            Directory.CreateDirectory(targetRoot);
                            string destination = Path.Combine(
                                targetRoot, Path.GetFileName(file));

                            // Never overwrite existing user output. Retain
                            // previous REV32 _copyN collision behavior.
                            if (File.Exists(destination))
                            {
                                string stem = Path.GetFileNameWithoutExtension(file);
                                string extension = Path.GetExtension(file);
                                int n = 1;
                                while (File.Exists(destination))
                                {
                                    if (n > 9999)
                                        throw new IOException(
                                            "Too many existing target copies.");
                                    destination = Path.Combine(
                                        targetRoot, stem + "_copy" + n++ + extension);
                                }
                            }

                            string temp = destination + ".sptc-part";
                            if (File.Exists(temp))
                                File.Delete(temp);

                            try
                            {
                                await using (var output = new FileStream(
                                    temp, FileMode.CreateNew, FileAccess.Write,
                                    FileShare.None, 128 * 1024,
                                    FileOptions.SequentialScan))
                                {
                                    await input.CopyToAsync(output, 128 * 1024)
                                        .ConfigureAwait(false);
                                    await output.FlushAsync().ConfigureAwait(false);
                                }

                                // Same-directory rename gives the final DWG
                                // name only after all copy bytes are flushed.
                                File.Move(temp, destination);
                            }
                            finally
                            {
                                if (File.Exists(temp))
                                    File.Delete(temp);
                            }

                            copiedVersions[file] = version;
                            copied++;
                            lastCopy = DateTime.UtcNow;
                            _copyProgress.Enqueue(Path.GetFileName(destination));
                        }
                        catch (IOException) { skipped++; }
                        catch (UnauthorizedAccessException) { skipped++; }
                    }
                }
                catch (IOException)
                {
                    if ((DateTime.UtcNow - start).TotalMinutes > 2 && copied == 0)
                        throw;
                }
                catch (UnauthorizedAccessException) { throw; }

                if (copied > 0 &&
                    (DateTime.UtcNow - lastCopy).TotalSeconds >= 25)
                    break;

                // An event can wake us instantly, but never depend on it:
                // a short periodic fallback covers SMB drives and missed
                // notifications without blocking the native UI.
                await wake.WaitAsync(TimeSpan.FromMilliseconds(650))
                    .ConfigureAwait(false);
            }
        }
        finally { watcher?.Dispose(); }

        return new CopyReport(copied, skipped, targetRoot);
    }

    private static void EnsureMonitor()
    {
        if (_watch != null) return;
        _watch = new System.Windows.Forms.Timer { Interval = 1500 };
        _watch.Tick += OnTick;
        _watch.Start();
    }

    public static void Status(Document doc)
    {
        doc.Editor.WriteMessage(
            "\nSPTC ISO STATUS" +
            "\nNative output: " + (_source ?? "(not detected)") +
            "\nCustom folder: " + (string.IsNullOrEmpty(_destination) ? "(empty)" : _destination) +
            "\nCopy state: " + _copyState +
            "\nResults dialog observed: " + _resultsSeen +
            (_lastCopyReport is null ? "" :
                $"\nLast copy: {_lastCopyReport.Copied} succeeded, {_lastCopyReport.Skipped} skipped"));
    }

    public static void CopyCompleted(Document doc)
    {
        if (_copyTask is { IsCompleted: false })
        {
            doc.Editor.WriteMessage("\nSPTC ISO: Background export is already running.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_destination))
        {
            _copyState = "Custom location not specified";
            doc.Editor.WriteMessage("\nSPTC ISO: " + _copyState);
            return;
        }
        if (string.IsNullOrWhiteSpace(_source))
        {
            _copyState = "Native source not detected";
            doc.Editor.WriteMessage("\nSPTC ISO: " + _copyState +
                ". No copy started. Run SPTC_ISOSTATUS to check paths.");
            return;
        }
        try
        {
            string source = Path.GetFullPath(_source);
            string destination = Path.GetFullPath(_destination);
            if (NestedOrEqual(source, destination) || NestedOrEqual(destination, source))
                throw new InvalidOperationException("Custom folder must be outside the native ISO output folder.");

            _copyState = "Background watcher started";
            _copyTask = Task.Run(() => CopyFilesAsync(source, destination, _launchUtc));
            EnsureMonitor();
            doc.Editor.WriteMessage("\nSPTC ISO: Monitoring completed DWGs for custom copy (background).");
        }
        catch (Exception ex)
        {
            _copyState = "Error: " + ex.Message;
            doc.Editor.WriteMessage("\nSPTC ISO export: " + _copyState);
        }
    }
}
