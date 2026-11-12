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
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(
        IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;

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
    // REV25: filesystem-only, asynchronous first-level folder enumeration.
    // Opening the menu does NOT initialize PnPDrawings or read line groups.
    private static CancellationTokenSource? _menuLoadCancel;
    private static int _menuLoadVersion;
    private static UserControl? _lineHost;
    private static CheckedListBox? _folderLineList;
    private static CheckedListBox? _nativeChecked;
    private static ListView? _nativeView;
    private static readonly List<NativeEntry> VisibleEntries = new();
    private static bool _updating;
    // REV53: Shift+click on the FOLDER projection checks/unchecks the entire
    // inclusive range using the first ordinary-clicked row as its anchor.
    // CheckedListBox does not support WinForms MultiExtended selection, so
    // drive its actual checkbox states and the real Autodesk native rows.
    private static int _shiftAnchor = -1;
    private static int _shiftEndPending = -1;
    // REV26: ItemCheck fires before a CheckedListBox commits its own state.
    // Do NOT re-enter the Autodesk checklist from inside that event.
    // Every posted relay captures its projection generation so stale async
    // callbacks cannot change native rows after an Active/All toggle.
    private static long _projectionGeneration;
    private static System.Windows.Forms.Timer? _nativeCommitTimer;
    private static bool _committingNative;
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
        // Populate BEFORE showing the window, so its first painted frame
        // is never an empty gray panel while folder discovery begins.
        _choiceList.Items.Add(new FolderMenuChoice("Loading folders...", null));
        PositionMenu();
        _choicesHost.Show();
        _choicesHost.BringToFront();

        // No PnP calls on menu open. Enumerate only physical direct
        // folder names on a background filesystem-only task.
        _menuLoadCancel = new CancellationTokenSource();
        int loadVersion = ++_menuLoadVersion;
        _ = FillFolderNamesAsync(_menuLoadCancel.Token, loadVersion);
    }

    private static async Task FillFolderNamesAsync(
        CancellationToken token, int loadVersion)
    {
        try
        {
            IReadOnlyList<string> names =
                await ProjectFolderLineCatalog.ReadDirectFolderNamesAsync(token);

            if (token.IsCancellationRequested || loadVersion != _menuLoadVersion ||
                _choiceList is null || _choiceList.IsDisposed)
                return;

            _choiceList.BeginUpdate();
            try
            {
                // REV32: only top-level folders represented by registered
                // Piping DWG paths under the Plant 3D Drawings project tree.
                // PnP membership is validated when a folder is selected.
                while (_choiceList.Items.Count > 1)
                    _choiceList.Items.RemoveAt(1);

                foreach (string name in names)
                    _choiceList.Items.Add(new FolderMenuChoice(name, name));

                if (names.Count == 0)
                    _choiceList.Items.Add(new FolderMenuChoice(
                        "(No registered drawing subfolders)", null));
                _choiceList.ClearSelected();
                _document?.Editor.WriteMessage(
                    "\nSPTC REV32: Folder menu loaded " + names.Count +
                    " top-level folder(s) from registered Plant 3D Piping DWGs.");
            }
            finally { _choiceList.EndUpdate(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || loadVersion != _menuLoadVersion ||
                _choiceList is null || _choiceList.IsDisposed)
                return;
            while (_choiceList.Items.Count > 1)
                _choiceList.Items.RemoveAt(1);
            _choiceList.Items.Add(new FolderMenuChoice(
                "Folder list unavailable — see F2 log", null));
            _document?.Editor.WriteMessage(
                "\nSPTC REV32: Registered drawing folder list failed: " +
                ex.GetBaseException().Message);
        }
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
        _menuLoadVersion++;
        try { _menuLoadCancel?.Cancel(); } catch (ObjectDisposedException) { }
        _menuLoadCancel?.Dispose();
        _menuLoadCancel = null;
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
            // REV25: the quick dropdown intentionally does NOT load PnP.
            // Initialize only after a specific folder is chosen, with the
            // progress panel already visible and painted. All Autodesk API
            // reflection/database calls remain on Plant 3D's UI thread.
            if (!ProjectFolderLineCatalog.Ready)
            {
                await Task.Delay(50, cts.Token);
                ProjectFolderLineCatalog.Initialize(_document);
            }

            if (!ProjectFolderLineCatalog.Ready ||
                !ProjectFolderLineCatalog.Folders.Any(choice =>
                    choice.Key.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    "This physical folder is not a verified registered " +
                    "Plant 3D drawing folder. No ISO lines were changed.\n" +
                    ProjectFolderLineCatalog.Diagnostic,
                    "SPTC Folder Filter", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

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

            // REV38: restore the complete Autodesk Project source first.
            // Do not fabricate native rows or force-enable Create.
            NativeIsoExactLineFilter.End(restore: true);
            ActiveDwgTags.ClearOneTag(_dialog, null);
            ClearVisibleProjection();

            bool projectRefreshed =
                NativeIsoToolbarClickFilter.TryRefreshNativeProjectList(_dialog);
            NativeIsoToolbarClickFilter.MarkAllLinesRestored(_dialog);

            var allowed = new HashSet<string>(result.Tags,
                StringComparer.OrdinalIgnoreCase);
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<NativeEntry> nativeRows = new();
            bool nativeBound = false;

            // REV39: Autodesk regenerates the Project-wide ISO checklist on
            // its UI thread. REV38 sampled it after a fixed 200 ms, sometimes
            // seeing only the two Active-DWG rows from the previous filter.
            // Poll only the ORIGINAL native checklist for up to 4.15 seconds;
            // stop as soon as every verified folder tag has been repopulated.
            // No PnP or project database writes, no generated/imaginary rows.
            for (int attempt = 0; attempt < 17; attempt++)
            {
                await Task.Delay(attempt == 0 ? 150 : 250, cts.Token);
                if (cts.IsCancellationRequested || serial != _scanSerial ||
                    _dialog == IntPtr.Zero || _document is null)
                    return;

                nativeBound = TryBindNativeList(_dialog, out nativeRows);
                if (nativeBound)
                {
                    available = new HashSet<string>(
                        nativeRows.Select(x => x.Tag),
                        StringComparer.OrdinalIgnoreCase);
                    if (allowed.All(tag => available.Contains(tag)))
                        break;
                }

                // If the native Project command could not be invoked, do not
                // repeatedly wait on an unchanged active-only source.
                if (!projectRefreshed)
                    break;
            }

            if (!nativeBound)
            {
                MessageBox.Show(
                    "Autodesk's original Production Iso line checklist is not available.\n" +
                    "Close and reopen Production Iso, then select the folder again. " +
                    "No drawing or project records were changed.",
                    "SPTC Folder Filter", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var missing = allowed.Where(tag => !available.Contains(tag)).ToArray();

            _document.Editor.WriteMessage(
                "\nSPTC REV38 folder native Project refresh=" +
                projectRefreshed + "; verified=" + allowed.Count +
                "; native ISO list=" + nativeRows.Count +
                "; missing verified tags=" + missing.Length + ".");

            if (missing.Length > 0)
            {
                // We must NOT present incomplete folder coverage as safe.
                // PnP drawing tags are not themselves proof that a line is
                // eligible for ISO generation in the current style/project.
                MessageBox.Show(
                    $"Folder has {allowed.Count} verified line tag(s), but " +
                    $"{missing.Length} are not in Autodesk's currently loaded " +
                    $"Production Iso checklist ({nativeRows.Count} rows).\n" +
                    "Examples: " + string.Join(", ", missing.Take(5)) + "\n" +
                    "No partial folder selection was applied. " +
                    "Try reopening Production Iso in Project mode, or check " +
                    "whether the missing lines are eligible for the ISO style.",
                    "SPTC Folder Filter", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var visible = nativeRows
                .Where(x => allowed.Contains(x.Tag)).ToList();
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

    // REV27: verify the ORIGINAL Autodesk checklist only. Active DWG mode
    // no longer uses the visual copy; selecting a native row must be enough
    // to enable native Create when the underlying ISO validation permits it.
    public static bool VerifyNativeExactLineFilter(string expectedTag, out string note)
    {
        note = "";
        if (_dialog == IntPtr.Zero)
        {
            note = "Production Iso dialog no longer exists.";
            return false;
        }

        if (!TryBindNativeList(_dialog, out List<NativeEntry> entries))
        {
            note = "Original Autodesk checklist not available yet.";
            return false;
        }

        if (entries.Count == 0)
        {
            note = "Original Autodesk checklist shows zero routes.";
            return false;
        }

        string[] mismatched = entries.Where(x =>
            !x.Tag.Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Tag).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5).ToArray();
        note = $"Native entries: {entries.Count}; expected: {expectedTag}.";
        if (mismatched.Length > 0)
            note += " Other entries: " + string.Join(", ", mismatched);
        return mismatched.Length == 0 &&
            entries.Any(x => x.Tag.Equals(expectedTag, StringComparison.OrdinalIgnoreCase));
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
    public static void PrepareForNativeModeChange()
    {
        // Cancel an unfinished manual folder scan so a stale completion
        // cannot rebuild its temporary list after Autodesk changes mode.
        CancelPendingScan();
        ClearVisibleProjection();
    }

    public static void ClearVisibleProjection()
    {
        _activeDwgView = false;
        _folder = "";
        DestroyProjection();
        NativeIsoLineSelectOverlay.UpdateDisplayedCount();
    }

    // REV24: A programmatic .SetItemChecked() changes the icon but does not
    // necessarily fire Autodesk's native mouse/selection handlers, which can
    // keep Create disabled. For USER selections only, deliver a genuine
    // mouse down/up pair directly to the verified original native row.
    // The source checked-list item's text and final checked state are verified.
    private static bool SetNativeChecked(NativeEntry entry, bool check,
        bool userAction = false)
    {
        if (_nativeChecked != null)
        {
            CheckedListBox list = _nativeChecked;
            if (list.IsDisposed || entry.Index < 0 ||
                entry.Index >= list.Items.Count ||
                !list.GetItemText(list.Items[entry.Index]).Trim()
                    .Equals(entry.Tag, StringComparison.OrdinalIgnoreCase))
                return false;

            if (userAction)
            {
                if (!ClickNativeCheckedRow(list, entry.Index, check))
                    return false;
                ScheduleNativeSelectionCommit();
                return list.GetItemChecked(entry.Index) == check;
            }

            // Preserve REV23 fast programmatic unchecking for hidden old rows.
            list.SetItemChecked(entry.Index, check);
            return list.GetItemChecked(entry.Index) == check;
        }

        if (_nativeView != null)
        {
            ListView list = _nativeView;
            if (list.IsDisposed || entry.Index < 0 ||
                entry.Index >= list.Items.Count ||
                !list.Items[entry.Index].Text.Trim()
                    .Equals(entry.Tag, StringComparison.OrdinalIgnoreCase))
                return false;
            var item = list.Items[entry.Index];
            if (userAction)
            {
                // For ListView, the real item's Checked + Selected state
                // triggers its normal ItemChecked/SelectedIndexChanged events.
                item.Checked = check;
                item.Selected = true;
                list.EnsureVisible(entry.Index);
                ScheduleNativeSelectionCommit();
            }
            else item.Checked = check;
            return item.Checked == check;
        }

        return false;
    }

    private static bool ClickNativeCheckedRow(
        CheckedListBox list, int index, bool desired)
    {
        if (list.IsDisposed || !list.IsHandleCreated ||
            index < 0 || index >= list.Items.Count)
            return false;

        try
        {
            // REV41: the native selected row must be known before the
            // ItemCheck and Click events. Folder projection covers this
            // Autodesk list, so a click in the visible proxy does not select
            // a native row by itself. REV39 changed only the check state,
            // causing a checked icon with Create still disabled.
            if (desired)
            {
                if (list.SelectedIndex == index)
                    list.ClearSelected();
                list.SelectedIndex = index;
            }

            bool before = list.GetItemChecked(index);
            if (before == desired)
                return true;

            int oldTopIndex = list.TopIndex;
            try
            {
                list.TopIndex = index;
                Rectangle rect = list.GetItemRectangle(index);
                if (rect.Height <= 0) return false;
                int x = Math.Clamp(rect.Left + 12, 5, Math.Max(5, list.ClientSize.Width - 6));
                int y = rect.Top + rect.Height / 2;
                if (y < 0 || y >= list.ClientSize.Height)
                    return false;

                IntPtr location = new IntPtr(
                    ((y & 0xFFFF) << 16) | (x & 0xFFFF));

                // The original Click/ItemCheck/SelectedIndexChanged handlers
                // now run as with an ordinary click on the native checklist.
                // Native checkbox may require two clicks if CheckOnClick=false.
                for (int attempt = 0; attempt < 2 &&
                     list.GetItemChecked(index) != desired; attempt++)
                {
                    SendMessage(list.Handle, WM_LBUTTONDOWN,
                        new IntPtr(MK_LBUTTON), location);
                    SendMessage(list.Handle, WM_LBUTTONUP, IntPtr.Zero, location);
                }

                if (list.GetItemChecked(index) != desired)
                {
                    // Safe fallback to the old mechanism if an Autodesk
                    // custom control ignores synthetic mouse messages.
                    list.SetItemChecked(index, desired);
                }

                if (list.GetItemChecked(index) == desired && desired &&
                    list.SelectedIndex != index)
                    list.SelectedIndex = index;

                return list.GetItemChecked(index) == desired;
            }
            finally
            {
                if (!list.IsDisposed && list.Items.Count > 0)
                    list.TopIndex = Math.Clamp(oldTopIndex, 0,
                        list.Items.Count - 1);
            }
        }
        catch (Exception ex)
        {
            _document?.Editor.WriteMessage(
                "\nSPTC REV24: Native checkbox interaction failed: " +
                ex.GetBaseException().Message);
            return false;
        }
    }

    private static void ScheduleNativeSelectionCommit()
    {
        // Single reusable debounce timer; Select All may affect hundreds of
        // lines and must not allocate one timer per checked checkbox.
        if (_nativeCommitTimer is null)
        {
            _nativeCommitTimer = new System.Windows.Forms.Timer { Interval = 220 };
            _nativeCommitTimer.Tick += (_, _) =>
            {
                _nativeCommitTimer?.Stop();
                VerifyNativeCreateState();
            };
        }
        _nativeCommitTimer.Stop();
        _nativeCommitTimer.Start();
    }

    private static void VerifyNativeCreateState()
    {
        if (_committingNative || !HasVisibleProjection || _dialog == IntPtr.Zero)
            return;
        _committingNative = true;
        try
        {
            int selected = VisibleEntries.Count(GetNativeChecked);
            if (selected < 1) return;

            Control? create = null;
            EnumChildWindows(_dialog, (hwnd, _) =>
            {
                if (Control.FromHandle(hwnd) is Button b &&
                    string.Equals(b.Text.Trim(), "Create",
                        StringComparison.OrdinalIgnoreCase))
                {
                    create = b;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (create is null || create.Enabled) return;

            // Already-checked native row may need its SelectedIndexChanged
            // event after the ItemCheck event has fully committed.
            if (_nativeChecked != null && !_nativeChecked.IsDisposed)
            {
                NativeEntry first = VisibleEntries.First(GetNativeChecked);
                if (_nativeChecked.SelectedIndex == first.Index)
                    _nativeChecked.ClearSelected();
                _nativeChecked.SelectedIndex = first.Index;
            }
            else if (_nativeView != null && !_nativeView.IsDisposed)
            {
                NativeEntry first = VisibleEntries.First(GetNativeChecked);
                _nativeView.Items[first.Index].Selected = true;
            }

            // We MUST NOT force Create.Enabled=true. Autodesk may reject
            // generation for reasons beyond the checkmarks (style, scope,
            // pipeline validation). Make the discrepancy diagnosable.
            if (!create.Enabled && _nativeChecked is { IsDisposed: false } list)
            {
                // Final one-shot repair: an already-checked row might have
                // been selected before our overlay was attached. Replaying a
                // real native checkbox OFF then ON gives Autodesk's handlers
                // the full mouse/ItemCheck/selection event sequence while
                // preserving the intended final checked state.
                NativeEntry first = VisibleEntries.First(GetNativeChecked);
                if (ClickNativeCheckedRow(list, first.Index, false))
                    ClickNativeCheckedRow(list, first.Index, true);
            }

            if (!create.Enabled)
                _document?.Editor.WriteMessage(
                    "\nSPTC REV24: Native Create is still disabled despite " +
                    selected + " checked native ISO line(s). " +
                    "No forced Create override; verify Autodesk's original line " +
                    "source and other Production Iso eligibility conditions.");
        }
        finally { _committingNative = false; }
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

        // REV53: SHIFT + click a second line to apply the checked state
        // of the anchor line to every line in between. This uses the same
        // deferred, verified native checkbox relay as folder Select All.
        // Plain clicking remains the approved REV41 single-line behavior.
        _folderLineList.MouseDown += (_, mouse) =>
        {
            if (mouse.Button != MouseButtons.Left ||
                _folderLineList is null || _folderLineList.IsDisposed)
                return;

            CheckedListBox list = _folderLineList;
            int index = list.IndexFromPoint(mouse.Location);
            if (index < 0 || index >= VisibleEntries.Count)
                return;

            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift &&
                _shiftAnchor >= 0 && _shiftAnchor < VisibleEntries.Count &&
                _shiftAnchor != index)
            {
                int anchorIndex = _shiftAnchor;
                bool checkedState = list.GetItemChecked(anchorIndex);
                int endIndex = index;
                long generation = _projectionGeneration;
                NativeEntry[] snapshot = VisibleEntries.ToArray();

                // Cancel just the standard endpoint checkbox flip in the
                // upcoming ItemCheck. The range commits after MouseDown
                // and Click/ItemCheck have returned to the UI message loop.
                _shiftEndPending = endIndex;
                try
                {
                    list.BeginInvoke((MethodInvoker)(() =>
                    {
                        if (generation != _projectionGeneration ||
                            list.IsDisposed ||
                            !ReferenceEquals(list, _folderLineList))
                            return;

                        _shiftEndPending = -1;
                        ApplyShiftCheckedRange(
                            list, snapshot, generation,
                            anchorIndex, endIndex, checkedState);
                    }));
                }
                catch (InvalidOperationException)
                {
                    _shiftEndPending = -1;
                }
            }
            else
            {
                _shiftEndPending = -1;
                _shiftAnchor = index;
            }
        };

        // REV26: Let the projected checkbox commit its click first. The
        // REV24 synchronous call could re-enter native ItemCheck while this
        // proxy ItemCheck was still pending; its false return canceled the
        // visible checkbox, leaving the selected line highlighted in blue
        // but impossible to check (Create remained disabled).
        _folderLineList.ItemCheck += (_, e) =>
        {
            // REV53: Shift+click is a range operation, not a toggle of
            // only the clicked endpoint. Leave the endpoint unchanged
            // until the deferred range update has committed.
            if (!_updating && _shiftEndPending == e.Index)
            {
                e.NewValue = e.CurrentValue;
                return;
            }
            if (_updating || e.Index < 0 || e.Index >= VisibleEntries.Count ||
                _folderLineList == null || _folderLineList.IsDisposed)
                return;

            CheckedListBox displayed = _folderLineList;
            int displayIndex = e.Index;
            NativeEntry nativeRow = VisibleEntries[displayIndex];
            bool desired = e.NewValue == CheckState.Checked;
            long generation = _projectionGeneration;

            try
            {
                displayed.BeginInvoke((MethodInvoker)(() =>
                {
                    if (generation != _projectionGeneration ||
                        displayed.IsDisposed ||
                        !ReferenceEquals(displayed, _folderLineList) ||
                        displayIndex >= displayed.Items.Count ||
                        displayIndex >= VisibleEntries.Count ||
                        !VisibleEntries[displayIndex].Equals(nativeRow))
                        return;

                    // Another user click may have superseded this request
                    // before its deferred dispatch ran.
                    if (displayed.GetItemChecked(displayIndex) != desired)
                        return;

                    // REV41: the REV39 programmatic SetItemChecked path
                    // updates the check icon but can leave Autodesk's native
                    // Create eligibility disabled for ONE folder line. Send
                    // the checked/unchecked action through the ORIGINAL
                    // Autodesk control's mouse/ItemCheck handlers *after*
                    // the projected ItemCheck has committed. A native row
                    // selection is primed BEFORE the click (see helper).
                    // Preserve the existing bulk Select All handler intact.
                    if (SetNativeChecked(nativeRow, desired, userAction: true))
                    {
                        ScheduleNativeSelectionCommit();
                        _document?.Editor.WriteMessage(
                            "\nSPTC REV41 folder individual native event: " +
                            nativeRow.Tag + " requested=" + desired +
                            " actual=" + GetNativeChecked(nativeRow));
                        return;
                    }

                    // Never show a false checked state that Create will not
                    // honor. Revert the visible checkbox to the actual native
                    // state and write a diagnostic rather than faking Create.
                    bool actual = GetNativeChecked(nativeRow);
                    _updating = true;
                    try { displayed.SetItemChecked(displayIndex, actual); }
                    finally { _updating = false; }
                    _document?.Editor.WriteMessage(
                        "\nSPTC REV26: Could not synchronize native line " +
                        nativeRow.Tag + " (requested=" + desired +
                        ", actual=" + actual + "). Original Create was not overridden.");
                }));
            }
            catch (InvalidOperationException)
            {
                // The native dialog closed while the event was being queued.
                // Do not mutate Autodesk controls from an invalid handle.
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
        // An already-checked underlying line can have missing native Create
        // eligibility state after overlay creation. Revalidate it once.
        if (entries.Any(GetNativeChecked))
            ScheduleNativeSelectionCommit();
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

    // REV53: apply only the contiguous folder range, never unrelated
    // project lines or the full Select All state. The final changed native
    // row is clicked through Autodesk's real ItemCheck/selection handlers,
    // which keeps the original Create eligibility in sync (REV41).
    private static void ApplyShiftCheckedRange(
        CheckedListBox displayed, NativeEntry[] rows, long generation,
        int anchorIndex, int endIndex, bool desired)
    {
        if (generation != _projectionGeneration ||
            !HasVisibleProjection ||
            displayed.IsDisposed ||
            !ReferenceEquals(displayed, _folderLineList) ||
            rows.Length != displayed.Items.Count ||
            rows.Length != VisibleEntries.Count)
            return;

        int start = Math.Min(anchorIndex, endIndex);
        int end = Math.Max(anchorIndex, endIndex);
        if (start < 0 || end >= rows.Length)
            return;

        for (int i = start; i <= end; i++)
            if (!rows[i].Equals(VisibleEntries[i]))
                return;

        // Advance visible checks immediately without firing the ordinary
        // one-checkbox relay for every row in the range.
        _updating = true;
        try
        {
            for (int i = start; i <= end; i++)
                displayed.SetItemChecked(i, desired);
        }
        finally { _updating = false; }

        // Commit programmatic native items first, then a real native mouse
        // event on the LAST row that actually changes. This also covers
        // cases where the clicked endpoint was already checked.
        int lastChanged = -1;
        for (int i = start; i <= end; i++)
            if (GetNativeChecked(rows[i]) != desired)
                lastChanged = i;

        bool allSucceeded = true;
        for (int i = start; i <= end; i++)
        {
            if (GetNativeChecked(rows[i]) == desired)
                continue;
            if (!SetNativeChecked(rows[i], desired,
                userAction: i == lastChanged))
            {
                allSucceeded = false;
                break;
            }
        }

        // Never display a checked item unless the REAL Autodesk checked
        // list accepted it. Do not force-enable native Create.
        _updating = true;
        try
        {
            for (int i = start; i <= end; i++)
                displayed.SetItemChecked(i, GetNativeChecked(rows[i]));
        }
        finally { _updating = false; }

        if (!allSucceeded)
            _document?.Editor.WriteMessage(
                "\nSPTC REV53: Shift range could not synchronize every " +
                "native ISO line; displayed states reflect Autodesk.");
        else
        {
            ScheduleNativeSelectionCommit();
            _document?.Editor.WriteMessage(
                "\nSPTC REV53: Folder Shift range " +
                rows[start].Tag + " through " + rows[end].Tag +
                (desired ? " checked." : " unchecked."));
        }
    }

    public static bool TrySelectAllVisible(bool value)
    {
        if (!HasVisibleProjection || _folderLineList is null || _folderLineList.IsDisposed)
            return false;

        CheckedListBox displayed = _folderLineList;
        long generation = _projectionGeneration;
        NativeEntry[] targetRows = VisibleEntries.ToArray();

        _updating = true;
        try
        {
            for (int i = 0; i < targetRows.Length; i++)
                displayed.SetItemChecked(i, value);
        }
        finally { _updating = false; }

        // REV26: Same deferred native event path for Select All as for a
        // single row click; never send native mouse messages recursively from
        // the proxy's CheckedChanged/ItemCheck event stack.
        try
        {
            displayed.BeginInvoke((MethodInvoker)(() =>
            {
                if (generation != _projectionGeneration ||
                    displayed.IsDisposed ||
                    !ReferenceEquals(displayed, _folderLineList))
                    return;

                bool allSucceeded = true;
                for (int i = 0; i < targetRows.Length; i++)
                {
                    if (i >= displayed.Items.Count ||
                        i >= VisibleEntries.Count ||
                        !VisibleEntries[i].Equals(targetRows[i]))
                    {
                        allSucceeded = false;
                        break;
                    }
                    bool clickNative = i == targetRows.Length - 1;
                    if (!SetNativeChecked(targetRows[i], value,
                        userAction: clickNative))
                    {
                        allSucceeded = false;
                        break;
                    }
                }

                // Always mirror actual native check states to the UI. This
                // prevents apparent selection when Autodesk cannot accept it.
                _updating = true;
                try
                {
                    for (int i = 0; i < targetRows.Length &&
                         i < displayed.Items.Count; i++)
                        displayed.SetItemChecked(i, GetNativeChecked(targetRows[i]));
                }
                finally { _updating = false; }

                if (!allSucceeded)
                    _document?.Editor.WriteMessage(
                        "\nSPTC REV26: Select All could not commit all native " +
                        "ISO checkboxes; the visible list was restored to the " +
                        "actual original selection.");
                else
                    ScheduleNativeSelectionCommit();
            }));
            return true;
        }
        catch (InvalidOperationException) { return false; }
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
        _projectionGeneration++;
        VisibleEntries.Clear();
        _nativeChecked = null;
        _nativeView = null;
        try { _lineHost?.Dispose(); } catch { }
        _lineHost = null;
        _folderLineList = null;
        _shiftAnchor = -1;
        _shiftEndPending = -1;
        _updating = false;
    }

    public static void Hide()
    {
        try { _nativeCommitTimer?.Stop(); _nativeCommitTimer?.Dispose(); }
        catch { }
        _nativeCommitTimer = null;
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
