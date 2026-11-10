using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    // REV15: compact folder arrow in the native Filter ROW, immediately
    // left of Autodesk's original funnel, never on top of that funnel.
    // Its menu stays a CHILD of the native dialog, not a modal popup.
    private static UserControl? _filterHost;
    private static Button? _filterButton;
    private static UserControl? _choicesHost;
    private static Control? _folderAnchor;
    private static ListBox? _choiceList;
    private static UserControl? _lineHost;
    private static CheckedListBox? _folderLineList;
    private static CheckedListBox? _nativeChecked;
    private static ListView? _nativeView;
    private static readonly List<NativeEntry> VisibleEntries = new();
    private static bool _updating;
    private static string _folder = "";
    private static bool _activeDwgView;
    public static bool ActiveDwgView => _activeDwgView;
    // REV16: cancel is cooperative and executed between PnP database calls.
    private static CancellationTokenSource? _scanCancellation;
    private static long _scanSerial;
    private static UserControl? _scanPanel;
    private static Label? _scanStatus;
    private static ProgressBar? _scanBar;

    // REV18: build a read-only per-folder line-tag cache automatically after
    // the Production Iso dialog opens. The warm-up runs cooperatively on the
    // Plant 3D UI thread and is canceled when the dialog closes.
    private static System.Windows.Forms.Timer? _precacheStart;
    private static CancellationTokenSource? _precacheCts;
    private static Task? _precacheTask;
    private static long _precacheSerial;
    private static bool _resumePrecache;

    private sealed record FolderMenuChoice(string Label, string? Key)
    {
        public override string ToString() => Label;
    }

    public static bool FolderActive => !string.IsNullOrWhiteSpace(_folder);
    public static bool HasVisibleProjection => FolderActive || ActiveDwgView;

    // REV17: number actually shown in the folder-specific checked list.
    // Do not count all registered DWGs, hidden project routes, or selections.
    public static int? VisibleLineCount =>
        HasVisibleProjection && _folderLineList is { IsDisposed: false }
            ? _folderLineList.Items.Count
            : null;

    public static void Attach(IntPtr dialog, Document? document)
    {
        if (dialog == IntPtr.Zero) return;
        if (_dialog == dialog && _filterHost is { IsDisposed: false })
        {
            _document = document;
            return;
        }
        Hide();
        _dialog = dialog;
        _document = document;

        _filterHost = new UserControl
        {
            Size = new Size(24, 23),
            BackColor = SystemColors.Control,
            TabStop = false
        };
        _filterButton = new Button
        {
            Text = "▾",
            Size = new Size(23, 22),
            Location = Point.Empty,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9, FontStyle.Bold),
            TabStop = false,
            AccessibleName = "Plant 3D Drawings folder selector",
            FlatStyle = FlatStyle.Standard
        };
        _filterButton.Click += (_, _) =>
        {
            if (_filterButton != null)
                ToggleFolderMenu(_filterButton);
        };
        _filterHost.Controls.Add(_filterButton);
        _ = _filterHost.Handle;
        SetParent(_filterHost.Handle, dialog);
        if (GetParent(_filterHost.Handle) != dialog)
        {
            Hide();
            document?.Editor.WriteMessage(
                "\nSPTC REV15: Folder arrow could not attach; native filter remains usable.");
            return;
        }

        var tip = new ToolTip();
        tip.SetToolTip(_filterButton, "Plant 3D Drawings folders (direct children only)");
        _filterHost.Disposed += (_, _) => tip.Dispose();
        Position(dialog);
        _filterHost.Show();
    }

    public static void Position(IntPtr dialog)
    {
        if (_dialog != dialog) return;
        if (_filterHost is { IsDisposed: false })
            PositionFilterButton(dialog);
        if (_choicesHost is { IsDisposed: false })
            PositionMenu();
        if (HasVisibleProjection && _lineHost is { IsDisposed: false })
            PositionList(dialog);
        if (_scanPanel is { IsDisposed: false })
            PositionScanPanel();
    }

    private static void PositionFilterButton(IntPtr dialog)
    {
        if (_filterHost == null || _filterHost.IsDisposed ||
            !GetClientRect(dialog, out RECT bounds)) return;

        // REV21: Anchor our folder arrow to the RIGHT EDGE of the resizable
        // Display lines GroupBox, not the old/fixed-width native search edit.
        // REV20 widened the group but left the funnel behind, causing the
        // user to see two arrows at opposite ends of the filter row.
        int x = (int)(bounds.Width * 0.25) - 22;
        int y = (int)(bounds.Height * 0.145);
        if (TryFindNativeFilterEdit(dialog, out RECT edit))
            y = edit.Top + 1;

        if (TryFindDisplayLinesGroup(dialog, out RECT group))
        {
            // Rightmost 28px are reserved for Autodesk's funnel icon.
            // The custom folder dropdown occupies the preceding 24px.
            x = group.Right - 57;
            if (!TryFindNativeFilterEdit(dialog, out _))
                y = group.Top + 43;
        }
        else if (TryFindNativeFilterEdit(dialog, out RECT editFallback))
            x = editFallback.Right - 54;

        x = Math.Clamp(x, 12, Math.Max(12, bounds.Width - 50));
        y = Math.Clamp(y, 42, Math.Max(42, bounds.Height - 40));
        SetWindowPos(_filterHost.Handle, IntPtr.Zero, x, y, 24, 23,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private static bool TryFindDisplayLinesGroup(IntPtr dialog, out RECT groupBounds)
    {
        groupBounds = default;
        RECT matched = default;
        bool found = false;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is GroupBox box &&
                box.Text.Trim().Equals("Display lines", StringComparison.OrdinalIgnoreCase) &&
                GetWindowRect(hwnd, out RECT r))
            {
                POINT a = new() { X = r.Left, Y = r.Top };
                POINT b = new() { X = r.Right, Y = r.Bottom };
                if (ScreenToClient(dialog, ref a) && ScreenToClient(dialog, ref b))
                {
                    matched = new RECT { Left = a.X, Top = a.Y, Right = b.X, Bottom = b.Y };
                    found = true;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        groupBounds = matched;
        return found;
    }

    private static bool TryFindNativeFilterEdit(IntPtr dialog, out RECT client)
    {
        client = default;
        if (!GetWindowRect(dialog, out RECT dlg)) return false;
        var matches = new List<(RECT Rect, int Score)>();
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            // Never inspect the folder arrow itself as a possible input.
            if (hwnd == _filterHost?.Handle || hwnd == _filterButton?.Handle)
                return true;
            if (!GetWindowRect(hwnd, out RECT rect) ||
                rect.Width < 110 || rect.Height < 16 || rect.Height > 45)
                return true;

            double cx = (rect.Left + rect.Right) * 0.5 - dlg.Left;
            double cy = (rect.Top + rect.Bottom) * 0.5 - dlg.Top;
            if (cx < 30 || cx > Math.Min(300, dlg.Width * 0.40) ||
                cy < 60 || cy > 170)
                return true;

            Control? c = Control.FromHandle(hwnd);
            var cls = new StringBuilder(128);
            GetClassName(hwnd, cls, cls.Capacity);
            if (c is not TextBoxBase &&
                !cls.ToString().Contains("EDIT", StringComparison.OrdinalIgnoreCase))
                return true;

            matches.Add((rect, rect.Width - Math.Abs((int)cy - 105)));
            return true;
        }, IntPtr.Zero);
        if (matches.Count == 0) return false;

        RECT r = matches.OrderByDescending(m => m.Score).First().Rect;
        POINT a = new() { X = r.Left, Y = r.Top };
        POINT b = new() { X = r.Right, Y = r.Bottom };
        if (!ScreenToClient(dialog, ref a) || !ScreenToClient(dialog, ref b))
            return false;
        client = new RECT { Left = a.X, Top = a.Y, Right = b.X, Bottom = b.Y };
        return true;
    }

    /// <summary>
    /// Starts a one-time read-ahead of each registered Plant 3D Drawings folder.
    /// Folder selection later reuses completed results. This is deliberately
    /// delayed until after the native Create Production Iso dialog has painted.
    /// </summary>
    public static void BeginAutomaticPrecache()
    {
        if (_dialog == IntPtr.Zero || _document is null) return;
        if (_precacheTask is { IsCompleted: false }) return;
        SchedulePrecache(800);
    }

    private static void SchedulePrecache(int delayMs)
    {
        try { _precacheStart?.Stop(); _precacheStart?.Dispose(); } catch { }
        _precacheStart = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(200, delayMs)
        };
        _precacheStart.Tick += (_, _) =>
        {
            try { _precacheStart?.Stop(); _precacheStart?.Dispose(); } catch { }
            _precacheStart = null;
            StartPrecache();
        };
        _precacheStart.Start();
    }

    private static void StartPrecache()
    {
        if (_dialog == IntPtr.Zero || _document is null ||
            _precacheTask is { IsCompleted: false })
            return;

        var cts = new CancellationTokenSource();
        _precacheCts = cts;
        long serial = ++_precacheSerial;
        Document doc = _document;
        _precacheTask = RunPrecacheAsync(doc, cts, serial);
    }

    private static async Task RunPrecacheAsync(
        Document doc, CancellationTokenSource cts, long serial)
    {
        try
        {
            // Initial yield allows the native dialog and plugin controls to
            // become interactive before we access PnP project metadata.
            await Task.Delay(40, cts.Token);
            if (!ProjectFolderLineCatalog.Ready)
                ProjectFolderLineCatalog.Initialize(doc);
            if (!ProjectFolderLineCatalog.Ready)
            {
                doc.Editor.WriteMessage(
                    "\nSPTC REV18: Automatic folder cache unavailable: " +
                    ProjectFolderLineCatalog.Diagnostic);
                return;
            }

            var folders = ProjectFolderLineCatalog.Folders;
            doc.Editor.WriteMessage(
                $"\nSPTC REV18: Preloading line tags for {folders.Count} folders; " +
                "Production Iso remains usable.");

            foreach (var folder in folders)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (_dialog == IntPtr.Zero || _precacheSerial != serial) return;
                if (ProjectFolderLineCatalog.IsCached(folder.Key)) continue;

                await Task.Delay(35, cts.Token);
                var result = await ProjectFolderLineCatalog.GetLiveTagsAsync(
                    folder.Key, null, cts.Token);
                if (!result.Note.StartsWith("Verified ", StringComparison.OrdinalIgnoreCase))
                {
                    // Don't retry the same broken PnP query in a tight loop.
                    doc.Editor.WriteMessage(
                        "\nSPTC REV18 cache skipped " + folder.Key + ": " + result.Note);
                }
            }
            if (!cts.IsCancellationRequested && _dialog != IntPtr.Zero)
                doc.Editor.WriteMessage(
                    $"\nSPTC REV18: Folder cache ready for " +
                    $"{ProjectFolderLineCatalog.CachedFolderCount} folders.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            doc.Editor.WriteMessage(
                "\nSPTC REV18 cache: " + ex.GetBaseException().Message);
        }
        finally
        {
            if (_precacheSerial == serial)
            {
                _precacheTask = null;
                _precacheCts = null;
                if (_resumePrecache && _dialog != IntPtr.Zero)
                {
                    _resumePrecache = false;
                    SchedulePrecache(400);
                }
            }
            cts.Dispose();
        }
    }

    private static void PausePrecacheForSelection()
    {
        // Folder clicks take priority over warming unrequested folders.
        try { _precacheStart?.Stop(); _precacheStart?.Dispose(); } catch { }
        _precacheStart = null;
        try { _precacheCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private static void ResumePrecacheAfterSelection()
    {
        // REV22: REV20 explicitly disabled automatic project-wide precaching.
        // Do NOT restart the REV18 warmup after a manual folder selection;
        // that caused unexpected extra scans/lag during ISO list loading.
        return;
        /*
        if (_dialog == IntPtr.Zero) return;
        _resumePrecache = true;
        if (_precacheTask is null || _precacheTask.IsCompleted)
        {
            _resumePrecache = false;
            SchedulePrecache(400);
        }
        // Otherwise the canceled warm-up's finally block restarts it.
        */
    }

    private static void StopPrecache()
    {
        _resumePrecache = false;
        try { _precacheStart?.Stop(); _precacheStart?.Dispose(); } catch { }
        _precacheStart = null;
        try { _precacheCts?.Cancel(); } catch (ObjectDisposedException) { }
        _precacheCts = null;
        _precacheSerial++;
    }

    // The arrow is inside the native Filter row. The original funnel remains
    // available; we never issue a synthetic click on its underlying control.
    // The embedded menu does not dismiss the native modal dialog.
    public static void ToggleFolderMenu(Control anchor)
    {
        if (_dialog == IntPtr.Zero || _document is null) return;
        if (_choicesHost is { IsDisposed: false })
        {
            HideFolderMenu();
            return;
        }
        _folderAnchor = anchor;

        // Show a real child control inside the existing native window.
        _choicesHost = new UserControl
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Control,
            Size = new Size(248, 239)
        };
        var header = new Label
        {
            Text = "Plant 3D Drawings folders",
            Location = new Point(8, 5),
            Size = new Size(205, 23),
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
        };
        var close = new Button
        {
            Text = "×",
            Size = new Size(25, 23),
            Location = new Point(216, 3),
            TabStop = false
        };
        close.Click += (_, _) => HideFolderMenu();
        _choiceList = new ListBox
        {
            Location = new Point(7, 32),
            Size = new Size(233, 198),
            IntegralHeight = false,
            HorizontalScrollbar = true,
            Font = SystemFonts.MessageBoxFont
        };
        _choicesHost.Controls.Add(header);
        _choicesHost.Controls.Add(close);
        _choicesHost.Controls.Add(_choiceList);

        _ = _choicesHost.Handle;
        SetParent(_choicesHost.Handle, _dialog);
        if (GetParent(_choicesHost.Handle) != _dialog)
        {
            HideFolderMenu();
            _document.Editor.WriteMessage("\nSPTC REV14: Could not attach folder choices to the native dialog.");
            return;
        }

        _choiceList.Items.Add(new FolderMenuChoice("All Project (no folder filter)", ""));
        _choiceList.SelectedIndexChanged += (_, _) =>
        {
            if (_choiceList?.SelectedItem is not FolderMenuChoice choice || choice.Key is null) return;
            // Remove the child menu before changing the native line view.
            HideFolderMenu();
            if (choice.Key.Length == 0)
                ClearFolder();
            else
                ApplyFolder(choice.Key);
        };
        PositionMenu();
        _choicesHost.Show();
        _choicesHost.BringToFront();

        Cursor.Current = Cursors.WaitCursor;
        try
        {
            if (!ProjectFolderLineCatalog.Ready)
                ProjectFolderLineCatalog.Initialize(_document);

            if (_choiceList is null || _choiceList.IsDisposed) return;
            if (!ProjectFolderLineCatalog.Ready)
            {
                _choiceList.Items.Add(new FolderMenuChoice(
                    "Folder data unavailable — run SPTC_FOLDERDIAG", null));
                _document.Editor.WriteMessage(
                    "\nSPTC REV14 folder lookup: " + ProjectFolderLineCatalog.Diagnostic);
            }
            else
            {
                foreach (var choice in ProjectFolderLineCatalog.Folders)
                {
                    // REV15: show exactly the immediate Project Manager
                    // folder name; no nested paths and no extra DWG count suffix.
                    _choiceList.Items.Add(new FolderMenuChoice(choice.Caption, choice.Key));
                }
                if (ProjectFolderLineCatalog.Folders.Count == 0)
                    _choiceList.Items.Add(new FolderMenuChoice("(No registered folders found)", null));
            }
            _choiceList.ClearSelected();
        }
        catch (Exception ex)
        {
            _document.Editor.WriteMessage("\nSPTC REV14 folder menu: " + ex.GetBaseException().Message);
            if (_choiceList is { IsDisposed: false })
                _choiceList.Items.Add(new FolderMenuChoice("Unable to read folders — SPTC_FOLDERDIAG", null));
        }
        finally { Cursor.Current = Cursors.Default; }
    }

    private static void PositionMenu()
    {
        if (_dialog == IntPtr.Zero || _choicesHost is null ||
            _choicesHost.IsDisposed || _folderAnchor is null ||
            _folderAnchor.IsDisposed ||
            !GetWindowRect(_folderAnchor.Handle, out RECT screen) ||
            !GetClientRect(_dialog, out RECT dialogBounds))
            return;

        POINT corner = new POINT { X = screen.Left, Y = screen.Top };
        if (!ScreenToClient(_dialog, ref corner)) return;

        int width = _choicesHost.Width;
        int height = _choicesHost.Height;
        // Open BELOW the filter row, covering the native line list only while
        // the user is choosing a folder. Never open above the native toolbar.
        int x = Math.Clamp(corner.X - 155, 5, Math.Max(5, dialogBounds.Width - width - 5));
        int y = Math.Clamp(corner.Y + 27, 105,
            Math.Max(105, dialogBounds.Height - height - 35));

        // HWND_TOP without NOZORDER ensures the temporary dropdown stays above
        // the native checked list while open.
        SetWindowPos(_choicesHost.Handle, IntPtr.Zero, x, y, width, height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private static void HideFolderMenu()
    {
        UserControl? old = _choicesHost;
        _choicesHost = null;
        _choiceList = null;
        _folderAnchor = null;
        try { old?.Dispose(); } catch { }
    }

    // REV16: This is an async event handler but PnP API calls remain on the
    // WinForms/Plant 3D UI thread. The catalog periodically awaits between
    // drawing batches; no Autodesk API object crosses worker threads.
    private static async void ApplyFolder(string folder)
    {
        if (_document is null || _dialog == IntPtr.Zero) return;
        CancelPendingScan();
        bool cacheHit = ProjectFolderLineCatalog.IsCached(folder);
        if (!cacheHit)
            PausePrecacheForSelection();
        var cts = new CancellationTokenSource();
        _scanCancellation = cts;
        long serial = ++_scanSerial;
        // Already verified folders load instantly; no progress popup.
        if (!cacheHit)
            ShowScanPanel(folder, cts);

        try
        {
            var progress = new Progress<ProjectFolderLineCatalog.ScanProgress>(state =>
            {
                if (serial != _scanSerial || _scanPanel is null ||
                    _scanPanel.IsDisposed || cts.IsCancellationRequested)
                    return;
                if (_scanStatus != null)
                    _scanStatus.Text = $"Scanning {state.Done} / {state.Total} DWGs\n" +
                        $"{state.TagsFound} line tags verified";
                if (_scanBar != null)
                {
                    _scanBar.Maximum = Math.Max(1, state.Total);
                    _scanBar.Value = Math.Clamp(state.Done, 0, _scanBar.Maximum);
                }
            });

            ProjectFolderLineCatalog.FolderLines result =
                await ProjectFolderLineCatalog.GetLiveTagsAsync(
                    folder, progress, cts.Token);

            if (cts.IsCancellationRequested || serial != _scanSerial ||
                _dialog == IntPtr.Zero || _document is null)
                return;

            _document.Editor.WriteMessage("\nSPTC REV16 " + folder + ": " + result.Note);
            if (!result.Note.StartsWith("Verified ", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(result.Note, "SPTC Folder Filter",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Only after a COMPLETE verified scan touch the temporary list.
            ActiveDwgTags.ClearOneTag(_dialog, null);
            _folder = "";
            DestroyProjection();

            if (!TryBindNativeList(_dialog, out List<NativeEntry> nativeRows))
            {
                MessageBox.Show(
                    "Original Production Iso line checkboxes could not be found.\n" +
                    "Choose Project (second icon) and retry. No drawing data was changed.",
                    "SPTC Folder Filter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var allowed = new HashSet<string>(result.Tags, StringComparer.OrdinalIgnoreCase);
            var visible = nativeRows.Where(x => allowed.Contains(x.Tag)).ToList();
            if (allowed.Count > 0 &&
                allowed.Any(tag => !visible.Any(x => tag.Equals(x.Tag, StringComparison.OrdinalIgnoreCase))))
            {
                MessageBox.Show(
                    $"Folder has {allowed.Count} verified line tag(s), but not all " +
                    "appear in the currently loaded Production Iso list.\n" +
                    "Click the Project icon, then select this folder again.",
                    "SPTC Folder Filter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Clear hidden selections to avoid generating unexpected ISOs.
            foreach (var row in nativeRows)
                if (!allowed.Contains(row.Tag)) SetNativeChecked(row, false);

            VisibleEntries.AddRange(visible);
            _folder = folder;
            BuildProjection(visible);
            PositionList(_dialog);
            // REV17: reflect the just-completed folder's visible line count.
            NativeIsoLineSelectOverlay.UpdateDisplayedCount();
            _document.Editor.WriteMessage(
                $"\nSPTC REV16 folder selected: {folder}; {visible.Count} lines shown. " +
                "Underlying project data unchanged.");
        }
        catch (OperationCanceledException)
        {
            _document?.Editor.WriteMessage("\nSPTC REV16: Folder scan canceled; original line list retained.");
        }
        catch (Exception ex)
        {
            _document?.Editor.WriteMessage(
                "\nSPTC REV16 folder filter: " + ex.GetBaseException().Message);
        }
        finally
        {
            if (serial == _scanSerial)
            {
                _scanCancellation = null;
                HideScanPanel();
            }
            cts.Dispose();
            if (!cacheHit)
                ResumePrecacheAfterSelection();
        }
    }

    private static void ShowScanPanel(string folder, CancellationTokenSource cts)
    {
        HideScanPanel();
        if (_dialog == IntPtr.Zero) return;

        _scanPanel = new UserControl
        {
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle,
            Size = new Size(292, 112)
        };
        _scanStatus = new Label
        {
            Text = "Checking registered DWGs:\n" + folder,
            Location = new Point(9, 7),
            Size = new Size(272, 42),
            AutoEllipsis = true
        };
        _scanBar = new ProgressBar
        {
            Location = new Point(9, 54),
            Size = new Size(272, 15),
            Style = ProgressBarStyle.Continuous,
            Minimum = 0, Maximum = 1
        };
        var cancel = new Button
        {
            Text = "Cancel",
            Location = new Point(199, 76),
            Size = new Size(80, 26),
            TabStop = false
        };
        cancel.Click += (_, _) => cts.Cancel();
        _scanPanel.Controls.Add(_scanStatus);
        _scanPanel.Controls.Add(_scanBar);
        _scanPanel.Controls.Add(cancel);
        _ = _scanPanel.Handle;
        SetParent(_scanPanel.Handle, _dialog);
        if (GetParent(_scanPanel.Handle) != _dialog)
        {
            HideScanPanel();
            cts.Cancel();
            return;
        }
        PositionScanPanel();
        _scanPanel.Show();
        _scanPanel.BringToFront();
    }

    private static void PositionScanPanel()
    {
        if (_dialog == IntPtr.Zero || _scanPanel is null ||
            _scanPanel.IsDisposed || !GetClientRect(_dialog, out RECT bounds))
            return;
        int x = Math.Clamp(24, 3, Math.Max(3, bounds.Width - _scanPanel.Width - 8));
        int y = Math.Clamp(153, 60, Math.Max(60, bounds.Height - _scanPanel.Height - 35));
        SetWindowPos(_scanPanel.Handle, IntPtr.Zero, x, y,
            _scanPanel.Width, _scanPanel.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private static void HideScanPanel()
    {
        UserControl? previous = _scanPanel;
        _scanPanel = null;
        _scanStatus = null;
        _scanBar = null;
        try { previous?.Dispose(); } catch { }
    }

    private static void CancelPendingScan()
    {
        // Increment the serial to invalidate progress/result callbacks from
        // previous folder selections; cancellation is cooperative.
        _scanSerial++;
        try { _scanCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        _scanCancellation = null;
        HideScanPanel();
    }

    private static bool TryBindNativeList(IntPtr dialog, out List<NativeEntry> result)
    {
        result = new List<NativeEntry>();
        CheckedListBox? cb = null;
        ListView? lv = null;
        int bestCount = -1;

        // REV22: look ONLY inside Autodesk's actual Display lines GroupBox.
        // The old "leftmost 34% of entire dialog" detection failed when
        // the user widened that panel with REV20's draggable splitter.
        IntPtr groupHandle = IntPtr.Zero;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is GroupBox box &&
                box.Text.Trim().Equals("Display lines",
                    StringComparison.OrdinalIgnoreCase))
            {
                groupHandle = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (groupHandle == IntPtr.Zero) return false;

        EnumChildWindows(groupHandle, (hwnd, _) =>
        {
            Control? c = Control.FromHandle(hwnd);
            if (c is CheckedListBox list && list.Height > 100 &&
                list.Items.Count > bestCount)
            {
                cb = list;
                lv = null;
                bestCount = list.Items.Count;
            }
            else if (c is ListView view && view.CheckBoxes &&
                     view.Height > 100 && view.Items.Count > bestCount)
            {
                cb = null;
                lv = view;
                bestCount = view.Items.Count;
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

    /// <summary>
    /// REV22: exact-match current-DWG view. Never search by substring:
    /// line tags such as ...-6 and ...-66666 must not match each other.
    /// A temporary checked-list projection forwards each choice to the
    /// original native ISO line item, leaving Autodesk's records untouched.
    /// </summary>
    public static bool ShowExactActiveDwgTags(
        IReadOnlyList<string> actualTags, Document? doc)
    {
        if (_dialog == IntPtr.Zero || actualTags.Count == 0)
            return false;

        ClearVisibleProjection();
        if (!TryBindNativeList(_dialog, out List<NativeEntry> nativeRows))
        {
            doc?.Editor.WriteMessage(
                "\nSPTC REV22: Could not bind the native line list; no active filter applied.");
            return false;
        }

        var allowed = new HashSet<string>(actualTags, StringComparer.OrdinalIgnoreCase);
        var matches = nativeRows.Where(x => allowed.Contains(x.Tag)).ToList();
        // Do not apply a partial or misleading list. The Project scope might
        // not have finished loading, or the tag might not be registered.
        if (matches.Count == 0 ||
            allowed.Any(t => !matches.Any(x =>
                x.Tag.Equals(t, StringComparison.OrdinalIgnoreCase))))
        {
            doc?.Editor.WriteMessage(
                "\nSPTC REV22: Current physical line tag(s) not fully available " +
                "in the native ISO line list. Use Project mode and try again: " +
                string.Join(", ", allowed));
            return false;
        }

        // Important: don't generate ISOs for old lines hidden by this overlay.
        foreach (NativeEntry row in nativeRows)
        {
            if (!allowed.Contains(row.Tag) && GetNativeChecked(row))
                SetNativeChecked(row, false);
        }

        VisibleEntries.AddRange(matches);
        _activeDwgView = true;
        BuildProjection(matches);
        if (_lineHost is null)
        {
            _activeDwgView = false;
            return false;
        }
        PositionList(_dialog);
        NativeIsoLineSelectOverlay.UpdateDisplayedCount();
        doc?.Editor.WriteMessage(
            "\nSPTC REV22: Showing exact current DWG line tags (" +
            matches.Count + "): " + string.Join(", ", actualTags));
        return true;
    }

    /// <summary>
    /// Safely remove a temporary native-backed projection before Autodesk
    /// rebuilds the line list on Drawing/Project mode changes. Avoid stale
    /// item indices and transient blank overlays while the list loads.
    /// </summary>
    public static void ClearVisibleProjection()
    {
        _activeDwgView = false;
        _folder = "";
        DestroyProjection();
        NativeIsoLineSelectOverlay.UpdateDisplayedCount();
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
        if (!HasVisibleProjection || _folderLineList is null || _folderLineList.IsDisposed)
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
        CancelPendingScan();
        if (_dialog != IntPtr.Zero)
            ActiveDwgTags.ClearOneTag(_dialog, null);
        ClearVisibleProjection();
        _document?.Editor.WriteMessage("\nSPTC REV22: Original native line list restored.");
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
        StopPrecache();
        CancelPendingScan();
        HideFolderMenu();
        _folder = "";
        _activeDwgView = false;
        DestroyProjection();
        try { _filterHost?.Dispose(); } catch { }
        _filterHost = null;
        _filterButton = null;
        _dialog = IntPtr.Zero;
        _document = null;
        ProjectFolderLineCatalog.Reset();
    }
}
