using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
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
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
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
    // REV98: retain the full Autodesk project source while only
    // projecting the exact Active DWG tag. Prevents losing Create
    // eligibility when returning from another manually searched route.
    private static System.Windows.Forms.Timer? _activeProjectProjectionTimer;
    private static int _activeProjectProjectionSerial;
    private static System.Windows.Forms.Timer? _quietShowAllTimer;
    private static int _quietShowAllSerial;
    private static Size _lastDialogClientSize = Size.Empty;
    private static int _fallbackRowLeft = -1;
    private static int _fallbackTextboxLeft = -1;
    private static int _fallbackRowTop = -1;
    private static string _destination = "";
    // REV78: remember the explicitly entered custom folder for the SAME
    // native ISO output source while Plant 3D remains open. Do not copy to
    // a destination belonging to a different project/style or when cleared.
    private static string _rememberedDestination = "";
    private static string _rememberedSource = "";
    private static bool _destinationEditedThisDialog;
    private static string? _source;
    private static Document? _document;

    // REV107: The source 3D DWG may be CLOSED while ISO generation and
    // custom copying continue. Document?.Editor is NOT safe on a closed
    // AutoCAD document: it throws Autodesk eNotApplicable. Only log to
    // whichever document is NOW active, and silently skip when none is
    // available. Never let a diagnostic kill the background copy monitor.
    private static void SafeLog(string message)
    {
        try
        {
            Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager.MdiActiveDocument?
                .Editor.WriteMessage(message);
        }
        catch (Exception)
        {
            // An MDI document can disappear or deactivate between read
            // and WriteMessage. Logging must be best effort only.
        }
    }

    private static int _noWindowTicks;
    private static DateTime _launchUtc;
    // REV54: snapshot the REAL Autodesk native line selection at Create
    // so one batch cannot accidentally copy/link unrelated DWGs.
    private static readonly HashSet<string> _selectedIsoTags =
        new(StringComparer.OrdinalIgnoreCase);
    private static Task<CopyReport>? _copyTask;
    // REV33: background worker reports each completed DWG immediately
    // without calling any AutoCAD APIs from the worker thread.
    private static readonly ConcurrentQueue<string> _copyProgress = new();
    private static string _copyState = "Not started";
    private static CopyReport? _lastCopyReport;
    private static bool _nativeSeen;
    // Written by the UI watcher, read by the background file watcher.
    // A completed native Results dialog is the first reliable signal
    // that Autodesk won't generate another late split sheet in this run.
    private static volatile bool _resultsSeen;
    private static DateTime _nativeClosedAt;
    // REV125: diagnostic-only observation of Autodesk's existing status
    // tray notice. Never hide, replay, delay, or replace it. In particular,
    // do NOT change TRAYNOTIFY; it controls OTHER Autodesk notifications.
    private static DateTime _nativePopupObservedAt;
    private static DateTime _resultsFirstObservedAt;
    private static DateTime _customFirstCopiedAt;
    private static DateTime _customBatchVerifiedAt;
    private static DateTime _lastPopupProbeAt;
    private static string _nativePopupWindowType = "(not detected)";
    // REV64: native dialog close is NOT proof that the user clicked Create.
    // Cancel and top-right X must never start a 10-minute background scan.
    private static bool _createRequested;
    // REV50: follow the real native Overwrite-if-existing checkbox.
    // REV49 could miss Autodesk's managed checkbox when its HWND had no
    // caption, and then silently skip the preexisting custom DWG.
    // The default is still OFF, and an ambiguous/unknown state NEVER grants
    // overwrite permission.
    private static bool _overwriteRequested;
    private static CheckBox? _nativeOverwriteBox;
    private static Button? _nativeCreateButton;
    private static IntPtr _nativeOverwriteHandle;
    private static CreateClickPreflightFilter? _createPreflightFilter;
    private static bool _swallowCreateMouseUp;
    // REV79: full-batch Create from ANY filtered view. Selection intent
    // is the exact tag ledger, not the visible native subset.
    private static System.Windows.Forms.Timer? _autoCreateTimer;
    private static int _autoCreateSerial;
    private static bool _autoCreatePending;
    private static bool _autoCreateSubmitting;
    private static string[] _autoCreateTags = Array.Empty<string>();
    // REV80: canceled/failed auto Create must NEVER be mistaken for a
    // successful native Create after dialog Close; otherwise a network
    // DWG watcher can run for up to ten minutes without an ISO job.
    private static bool _batchPreparationFailed;
    private static Button? _nativeCancelButton;
    private static Form? _nativeParentForm;
    private static bool _overwriteFound;
    private static string _overwriteSource = "not detected";
    private static System.Windows.Forms.Timer? _overwriteTracker;
    private const uint BM_GETCHECK = 0x00F0;
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static bool IsOverwriteCaption(string? text)
    {
        string value = (text ?? "").Replace("&", "").Trim();
        return value.StartsWith("Overwrite if existing",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TrackManagedOverwrite(CheckBox box)
    {
        if (ReferenceEquals(_nativeOverwriteBox, box))
        {
            _overwriteRequested = box.Checked;
            _overwriteFound = true;
            return;
        }

        if (_nativeOverwriteBox is { IsDisposed: false } previous)
            previous.CheckedChanged -= OnNativeOverwriteChanged;
        _nativeOverwriteBox = box;
        _overwriteSource = "managed checkbox";
        _overwriteFound = true;
        _overwriteRequested = box.Checked;
        box.CheckedChanged += OnNativeOverwriteChanged;
    }

    private static void OnNativeOverwriteChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _nativeOverwriteBox) &&
            _nativeOverwriteBox is { IsDisposed: false } box)
        {
            _overwriteRequested = box.Checked;
            _overwriteFound = true;
            SafeLog(
                "\nSPTC REV50: Native overwrite " +
                (_overwriteRequested ? "ON" : "OFF") +
                " (checkbox event).");
        }
    }

    private static IEnumerable<Control> EnumerateManagedControls(
        Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in EnumerateManagedControls(child))
                yield return nested;
        }
    }

    private static void CaptureNativeOverwrite(IntPtr dialog)
    {
        if (dialog == IntPtr.Zero)
            return;

        // Prefer a managed WinForms CheckBox, even when its own native HWND
        // has no caption. Enumerate the entire managed controls hierarchy
        // of the ORIGINAL Autodesk Production Iso dialog.
        Control? root = Control.FromHandle(dialog);
        if (root != null && !root.IsDisposed)
        {
            foreach (Control control in EnumerateManagedControls(root))
            {
                if (control is CheckBox box && !box.IsDisposed &&
                    IsOverwriteCaption(box.Text))
                {
                    TrackManagedOverwrite(box);
                    break;
                }
            }
        }

        if (_nativeOverwriteBox is { IsDisposed: false } active)
        {
            _overwriteFound = true;
            _overwriteRequested = active.Checked;
            return;
        }

        // Some Plant 3D builds expose the checkbox as a native BUTTON.
        // Read BM_GETCHECK only for the precise Overwrite checkbox, NEVER
        // a random Button or the Create DWF checkbox.
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            Control? child = Control.FromHandle(hwnd);
            if (child is CheckBox box && !box.IsDisposed &&
                IsOverwriteCaption(box.Text))
            {
                TrackManagedOverwrite(box);
                return false;
            }
            if (!IsOverwriteCaption(WindowText(hwnd)))
                return true;

            if (child is CheckBox direct && !direct.IsDisposed)
                TrackManagedOverwrite(direct);
            else
            {
                _nativeOverwriteHandle = hwnd;
                _overwriteRequested = SendMessage(
                    hwnd, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt32() == 1;
                _overwriteFound = true;
                _overwriteSource = "native BUTTON";
            }
            return false;
        }, IntPtr.Zero);
    }

    // REV78 safety preflight: the Selected:N footer counts user-saved tag
    // intent across manual searches. Autodesk's native Create button uses
    // ONLY its REAL currently checked rows. REV77's Current Drawing view
    // can show "Selected: 6" while only A-08 is native-checked: clicking
    // Create then produces just one ISO. Never silently treat 6 as 1.
    private static string[] NativeCheckedRouteNames(IntPtr dialog)
    {
        var names = new List<string>();
        int bestItems = -1;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            Control? control = Control.FromHandle(hwnd);
            if (control is CheckedListBox list && !list.IsDisposed &&
                list.Height > 100 && list.Items.Count > bestItems)
            {
                bestItems = list.Items.Count;
                names.Clear();
                for (int i = 0; i < list.Items.Count; i++)
                    if (list.GetItemChecked(i))
                    {
                        string tag = list.GetItemText(list.Items[i]).Trim();
                        if (tag.Length > 0) names.Add(tag);
                    }
            }
            else if (control is ListView view && !view.IsDisposed &&
                view.CheckBoxes && view.Height > 100 &&
                view.Items.Count > bestItems)
            {
                bestItems = view.Items.Count;
                names.Clear();
                foreach (ListViewItem item in view.CheckedItems)
                {
                    string tag = item.Text.Trim();
                    if (tag.Length > 0) names.Add(tag);
                }
            }
            return true;
        }, IntPtr.Zero);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryGetCreateSelectionMismatch(
        IntPtr dialog, out string warning)
    {
        warning = "";
        if (dialog == IntPtr.Zero || !IsWindow(dialog))
            return false;

        string[] saved = NativeIsoLineSelectOverlay.CaptureSelectedTags(dialog);
        if (saved.Length == 0) return false;

        string[] native = NativeCheckedRouteNames(dialog);
        var current = new HashSet<string>(
            native, StringComparer.OrdinalIgnoreCase);
        var wanted = new HashSet<string>(
            saved, StringComparer.OrdinalIgnoreCase);
        string[] missing = saved.Where(t => !current.Contains(t)).ToArray();
        string[] unexpected = native.Where(t => !wanted.Contains(t)).ToArray();
        // REV79: also block extra native checks left over from another
        // search. Only the exact saved route set may be generated.
        if (missing.Length == 0 && unexpected.Length == 0)
            return false;

        warning = "You selected " + saved.Length +
            " line number(s) across searches, but only " +
            native.Length + " are currently checked in Autodesk's " +
            "native ISO list.\n\n" +
            "Missing from current Create selection: " +
            string.Join(", ", missing.Take(4)) +
            (missing.Length > 4 ? ", ..." : "") +
            "\n\nSwitch to Show All Project, verify every " +
            "required checkbox and that Create is enabled, then click " +
            "Create again. No ISO was started by this click.";
        return true;
    }

    // REV101: keep the single-line Active VIEW visible while repairing
    // the ORIGINAL 432-row Autodesk list behind it. Only reveal Show All
    // after exact checked tags AND native Create have settled. This avoids
    // exposing the transient A-08 unchecked/rechecked state (the blink).
    private static void StopQuietShowAll()
    {
        _quietShowAllSerial++;
        try { _quietShowAllTimer?.Stop(); _quietShowAllTimer?.Dispose(); }
        catch (Exception) { }
        _quietShowAllTimer = null;
    }

    private static void BeginQuietShowAll(
        IntPtr dialog, IReadOnlyCollection<string> requiredTags)
    {
        StopQuietShowAll();
        int serial = _quietShowAllSerial;
        string[] expected = requiredTags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var wanted = new HashSet<string>(
            expected, StringComparer.OrdinalIgnoreCase);
        int ticks = 0, attempts = 0, stable = 0;
        bool nudgedCreateOnce = false;
        var timer = new System.Windows.Forms.Timer { Interval = 180 };
        _quietShowAllTimer = timer;

        void Cancel(string reason)
        {
            if (serial != _quietShowAllSerial) return;
            StopQuietShowAll();
            NativeIsoToolbarClickFilter.MarkActiveLinesDisplayed(dialog);
            SafeLog(
                "\nSPTC REV101: Show All kept Active view: " + reason +
                ". No partial ISO created.");
        }

        timer.Tick += (_, _) =>
        {
            if (serial != _quietShowAllSerial || _native != dialog ||
                !IsWindow(dialog))
            {
                try { timer.Stop(); timer.Dispose(); } catch { }
                return;
            }
            if (++ticks > 55)
            {
                Cancel("native selection/Create not stable within " +
                    "the guarded recovery window");
                return;
            }
            try
            {
                if (!ProjectFolderLinePicker.ActiveDwgView ||
                    !NativeIsoLineSelectOverlay.HasCompleteUnfilteredProjectSource(
                        dialog, allowActivePresentation: true))
                {
                    Cancel("complete native source was replaced");
                    return;
                }

                string[] checkedTags = NativeCheckedRouteNames(dialog);
                bool exact = checkedTags.Length == wanted.Count &&
                    new HashSet<string>(checkedTags,
                        StringComparer.OrdinalIgnoreCase).SetEquals(wanted);
                if (!exact)
                {
                    stable = 0;
                    nudgedCreateOnce = false;
                    // REV111: the Active projection's checked state is
                    // durable user intent, but may not yet be committed to
                    // Autodesk's original full-source list. Commit ONE
                    // missing native route per timer tick, without removing
                    // the projected view or forcing the Create button.
                    // Bound retries by the requested route count.
                    int maxAttempts = Math.Min(45,
                        Math.Max(4, expected.Length * 3 + 3));
                    if (attempts < maxAttempts)
                    {
                        attempts++;
                        NativeIsoLineSelectOverlay.TryApplySavedBatchToNative(
                            dialog, expected, out _, out _,
                            allowActivePresentation: true);
                    }
                    return;
                }

                // REV111: require the real native Create state to match
                // the exact saved selection before exposing Show All.
                // Zero checked routes must leave Create disabled; positive
                // selections must be accepted by Autodesk itself.
                bool nativeCreateReady = expected.Length == 0
                    ? _nativeCreateButton is { IsDisposed: false,
                                               Enabled: false }
                    : _nativeCreateButton is { IsDisposed: false,
                                               Enabled: true };
                if (!nativeCreateReady)
                {
                    stable = 0;
                    if (expected.Length > 0 && !nudgedCreateOnce)
                    {
                        nudgedCreateOnce = true;
                        NativeIsoLineSelectOverlay
                            .TryNudgeNativeBatchCreateEligibility(
                                dialog, expected,
                                allowActivePresentation: true);
                    }
                    return; // do not fabricate Create.Enabled
                }

                if (++stable < 3) return;
                StopQuietShowAll();
                ProjectFolderLinePicker.ClearVisibleProjection();
                NativeIsoToolbarClickFilter.MarkAllLinesRestored(dialog);
                NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                NativeIsoLineSelectOverlay.UpdateDisplayedCount();
                SafeLog(
                    "\nSPTC REV106: Show All stable " + expected.Length +
                    "/" + expected.Length + ", Create.Enabled=" +
                    (_nativeCreateButton?.Enabled.ToString() ?? "unknown") +
                    ", Active overlay removed without native list refresh.");
            }
            catch (Exception ex)
            {
                Cancel("native selection check: " +
                    ex.GetBaseException().Message);
            }
        };
        timer.Start();
    }

    // REV98: do not run Autodesk's native text filter on Active view.
    // Native search A-08 produces five substring rows and discards the
    // other saved route selections, leaving Create=False. Instead restore
    // the already-supported complete project list, revalidate the exact
    // saved native set, and show a one-row projection of A-08 over it.
    // The ORIGINAL Autodesk Create stays responsible for generation.
    private static void StopActiveProjectProjection()
    {
        try
        {
            _activeProjectProjectionTimer?.Stop();
            _activeProjectProjectionTimer?.Dispose();
        }
        catch (Exception) { }
        _activeProjectProjectionTimer = null;
        _activeProjectProjectionSerial++;
    }

    private static bool BeginActiveProjectProjection(
        IntPtr dialog, IReadOnlyList<string> activeTags,
        string[] selectedBefore)
    {
        StopActiveProjectProjection();
        string[] exactTags = activeTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (exactTags.Length == 0)
            return false;

        // REV109: displaying an Active-DWG filter is a READ-ONLY view
        // operation. It must NEVER wait for or repeatedly repair checked
        // routes selected elsewhere (e.g. "002"), which caused dozens
        // of REV94 commits, continuous blinking and a hidden Active view.
        // Native checkbox state is validated separately before Create.
        // With the complete original source already loaded, render now.
        int projectRows = NativeIsoLineSelectOverlay
            .KnownProjectWideRowCount(dialog);
        int sourceRows = NativeIsoLineSelectOverlay
            .NativeDisplayedCount(dialog) ?? 0;
        if (projectRows > 0 && sourceRows >= projectRows &&
            NativeIsoLineSelectOverlay.IsNativeSearchBlank(dialog) &&
            !ProjectFolderLinePicker.HasVisibleProjection)
        {
            try
            {
                if (ProjectFolderLinePicker.ShowExactActiveDwgTags(
                    exactTags, _document,
                    preserveOtherCheckedNativeRoutes: true))
                {
                    NativeIsoLineSelectOverlay.CompleteActiveProjectionScope(
                        dialog, exactTags);
                    SafeLog("\nSPTC REV109: instant read-only Active " +
                        "projection of " +
                        ProjectFolderLinePicker.VisibleLineCount + "/" +
                        exactTags.Length + " physical route(s); " +
                        sourceRows + " native rows untouched. " +
                        "No route rechecks/checkbox replay.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                SafeLog("\nSPTC REV109: Active projection failed: " +
                    ex.GetBaseException().Message);
            }
            NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
            return false;
        }

        int serial = _activeProjectProjectionSerial;
        string[] expected = selectedBefore
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        int knownRows = NativeIsoLineSelectOverlay
            .KnownProjectWideRowCount(dialog);
        int ticks = 0, stable = 0;
        string lastSignature = "";
        var timer = new System.Windows.Forms.Timer { Interval = 180 };
        _activeProjectProjectionTimer = timer;

        void Stop(string? error = null, bool abortScope = false)
        {
            try { timer.Stop(); timer.Dispose(); }
            catch (Exception) { }
            if (!ReferenceEquals(_activeProjectProjectionTimer, timer))
                return;
            _activeProjectProjectionTimer = null;
            _activeProjectProjectionSerial++;
            if (abortScope && _native == dialog)
                NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
            if (!string.IsNullOrEmpty(error))
                SafeLog(
                    "\nSPTC REV98 Active projection: " + error);
        }

        timer.Tick += (_, _) =>
        {
            if (serial != _activeProjectProjectionSerial ||
                _native != dialog || !IsWindow(dialog))
            {
                Stop();
                return;
            }

            if (++ticks > 100)
            {
                Stop("Could not confirm full native project source; " +
                    "Active view not changed. Native Create was not forced.",
                    abortScope: true);
                return;
            }

            try
            {
                if (ProjectFolderLinePicker.HasVisibleProjection ||
                    !NativeIsoLineSelectOverlay.IsNativeSearchBlank(dialog))
                {
                    Stop("Native source changed during Active restore; " +
                        "canceled stale projection.", abortScope: true);
                    return;
                }

                int count = NativeIsoLineSelectOverlay
                    .NativeDisplayedCount(dialog) ?? 0;
                if (count == 0 || (knownRows > 0 && count < knownRows))
                    return;

                string signature = NativeIsoLineSelectOverlay
                    .NativeRouteNameSignature(dialog);
                if (string.IsNullOrEmpty(signature) ||
                    !signature.Equals(lastSignature, StringComparison.Ordinal))
                {
                    lastSignature = signature;
                    stable = 0;
                    return;
                }
                if (++stable < 3) return;

                // REV109: never synthesize a native checkbox event while
                // the user is only switching the Active VIEW. Older logic
                // tried to restore an unrelated checked route on every
                // 180ms tick and blocked the Active list indefinitely.
                string[] checkedNative = NativeCheckedRouteNames(dialog);
                if (!ProjectFolderLinePicker.ShowExactActiveDwgTags(
                    exactTags, _document,
                    preserveOtherCheckedNativeRoutes: true))
                {
                    Stop("No eligible Active DWG routes found in complete " +
                        "Autodesk native project list.", abortScope: true);
                    return;
                }

                NativeIsoLineSelectOverlay.CompleteActiveProjectionScope(
                    dialog, exactTags);
                SafeLog(
                    "\nSPTC REV109: delayed read-only Active view showing " +
                    ProjectFolderLinePicker.VisibleLineCount + "/" +
                    exactTags.Length + " physical route(s) over complete " +
                    count + "-row native source; native checked=" +
                    checkedNative.Length + ", saved selections=" +
                    expected.Length + ", Create.Enabled=" +
                    (_nativeCreateButton?.Enabled.ToString() ?? "unknown") +
                    ". No native checkbox writes or route reindexing.");
                Stop();
            }
            catch (Exception ex)
            {
                Stop("Native project restore exception: " +
                    ex.GetBaseException().Message, abortScope: true);
            }
        };
        timer.Start();
        return true; // REV109: accepted guarded delayed Active projection.
    }

    private static void StopAutoCreate()
    {
        try { _autoCreateTimer?.Stop(); _autoCreateTimer?.Dispose(); }
        catch (Exception) { }
        _autoCreateTimer = null;
        _autoCreateSerial++;
        _autoCreatePending = false;
        _autoCreateSubmitting = false;
        _autoCreateTags = Array.Empty<string>();
    }

    private static void AbortAutoCreate()
    {
        StopAutoCreate();
        _batchPreparationFailed = true;
        _createRequested = false;
        _selectedIsoTags.Clear();
    }

    private static void OnNativeCancelAction(object? sender, EventArgs e)
    {
        // Abort immediately, not at the next one-second dialog poll.
        // A canceled or closed dialog cannot authorize custom copying.
        if (_autoCreatePending)
        {
            AbortAutoCreate();
            NativeIsoLineSelectOverlay.CancelScopeSwitch(_native);
            SafeLog(
                "\nSPTC REV80: canceled pending ISO batch selection; " +
                "auto-create timer stopped; no custom-copy watcher.");
        }
    }

    private static void OnNativeFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (_autoCreatePending)
            OnNativeCancelAction(sender, e);
    }

    private static void ShowAutoCreateFailure(string problem)
    {
        // Failure is terminal for THIS attempted click; do not allow a
        // delayed native MouseDown/Click to flag a nonexistent ISO run.
        _batchPreparationFailed = true;
        _createRequested = false;
        SafeLog(
            "\nSPTC REV80: Batch Create not started: " + problem);
        try
        {
            MessageBox.Show(_overlay,
                "Could not safely prepare all selected ISO line numbers.\n\n" +
                problem + "\n\nThe original Create dialog remains open. " +
                "No partial ISO batch was submitted.",
                "SPTC Production ISO", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception) { }
    }

    // REV79: automatic one-click Create even with Current Drawing,
    // manual text search, or a folder projection still showing. Restore
    // the authoritative native Autodesk project-wide source, check ALL
    // saved user-selected tags by exact name, validate the native checked
    // set (never just Selected:N) and invoke original Create exactly once.
    private static void BeginAutomaticBatchCreate(IntPtr dialog)
    {
        StopQuietShowAll();
        StopActiveProjectProjection();
        if (_autoCreatePending || _autoCreateSubmitting ||
            dialog == IntPtr.Zero || dialog != _native ||
            _nativeCreateButton is not { IsDisposed: false } nativeCreate)
            return;

        string[] requested = NativeIsoLineSelectOverlay
            .CaptureSelectedTags(dialog)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length == 0) return;

        StopAutoCreate();
        _batchPreparationFailed = false;
        _autoCreatePending = true;
        _autoCreateTags = requested;
        int serial = _autoCreateSerial;
        _createRequested = false;

        // Message filter has already swallowed the user's original click.
        // Defer native UI changes until the mouse-up message is drained.
        try
        {
            nativeCreate.BeginInvoke((MethodInvoker)(() =>
            {
                if (_autoCreateSerial != serial) return;
                if (!_autoCreatePending || _native != dialog ||
                    !IsWindow(dialog))
                {
                    AbortAutoCreate();
                    return;
                }

                // REV108: Remember whether the ORIGINAL Autodesk search
                // was filtered before clearing it. TextChanged already
                // initiates a native project-list rebuild; a second
                // synchronous Project-icon click causes double refresh,
                // selection rollback (e.g. 5/6 Missing:01A) and blinking.
                bool nativeSearchWasFiltered =
                    !NativeIsoLineSelectOverlay.IsNativeSearchBlank(dialog);
                int knownNativeProjectRows =
                    NativeIsoLineSelectOverlay.KnownProjectWideRowCount(dialog);
                bool deferredProjectRefreshPending = false;
                try
                {
                    NativeIsoLineSelectOverlay.BeginScopeSwitch(dialog);
                    ProjectFolderLinePicker.PrepareForNativeModeChange();
                    NativeIsoExactLineFilter.End(restore: true);
                    if (!ActiveDwgTags.ClearOneTag(dialog, _document))
                    {
                        AbortAutoCreate();
                        NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                        ShowAutoCreateFailure(
                            "Autodesk's native line filter could not be cleared.");
                        return;
                    }

                    // REV100: when all native rows are already present
                    // (e.g. Show All after a full-source Active projection),
                    // do not click Autodesk's Project icon again. That
                    // unnecessary rebuild intermittently clears A-08 even
                    // though all six were checked before the user clicked.
                    bool alreadyFull = NativeIsoLineSelectOverlay
                        .HasCompleteUnfilteredProjectSource(dialog);
                    // If the user was in a manual search, clearing the
                    // textbox has ALREADY told Autodesk to load all routes.
                    // Do not also click the Project toolbar in the same UI
                    // turn. A bounded delayed fallback remains below.
                    deferredProjectRefreshPending =
                        !alreadyFull && nativeSearchWasFiltered;
                    bool refreshed = !alreadyFull &&
                        !deferredProjectRefreshPending &&
                        NativeIsoToolbarClickFilter
                            .TryRefreshNativeProjectList(dialog);
                    NativeIsoToolbarClickFilter.MarkAllLinesRestored(dialog);
                    SafeLog(
                        "\nSPTC REV108: Create preparing " +
                        requested.Length + " saved route(s); native " +
                        "source already complete=" + alreadyFull +
                        ", searchClearInitiated=" + nativeSearchWasFiltered +
                        ", immediateProjectRefresh=" + refreshed +
                        ", deferredFallback=" +
                        deferredProjectRefreshPending + ".");
                }
                catch (Exception ex)
                {
                    AbortAutoCreate();
                    NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                    ShowAutoCreateFailure(ex.GetBaseException().Message);
                    return;
                }

                int polls = 0;
                int stableSamples = 0;
                int nativeRebuildRetries = 0;
                bool deferredFallbackAttempted = false;
                int matched = 0;
                int checkedCount = 0;
                int afterAppliedPolls = 0;
                int readySamples = 0;
                bool appliedOnce = false;
                bool nudgedOnce = false;
                bool alternateRepairAttempted = false;
                string lastNativeSignature = "";
                // REV80: one native selection write, not a 432-checkbox
                // reconciliation repeated 35 times on the UI thread.
                // Scope preparation is bounded and fully canceled on X/Esc.
                var timer = new System.Windows.Forms.Timer { Interval = 180 };
                _autoCreateTimer = timer;

                void Fail(string issue)
                {
                    AbortAutoCreate();
                    NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                    ShowAutoCreateFailure(issue);
                }

                timer.Tick += (_, _) =>
                {
                    if (_autoCreateSerial != serial)
                    {
                        try { timer.Stop(); timer.Dispose(); }
                        catch (Exception) { }
                        return;
                    }
                    if (!_autoCreatePending || _native != dialog ||
                        !IsWindow(dialog) ||
                        _nativeCreateButton is not { IsDisposed: false } create)
                    {
                        AbortAutoCreate();
                        return;
                    }
                    // REV93: a 432-line project can take over 5 seconds
                    // to rebuild. Keep this UI timer bounded and cancelable
                    // while allowing the native source up to ~17 seconds.
                    if (++polls > 95)
                    {
                        var nativeAtTimeout = new HashSet<string>(
                            NativeCheckedRouteNames(dialog),
                            StringComparer.OrdinalIgnoreCase);
                        string missingAtTimeout = string.Join(", ",
                            requested.Where(tag => !nativeAtTimeout.Contains(tag)));
                        Fail("Found " + matched + "/" +
                            requested.Length + " route(s), native checked=" +
                            checkedCount + "/" + requested.Length +
                            ", missing=" + missingAtTimeout +
                            ", Autodesk Create.Enabled=" + create.Enabled +
                            ". Canceled the batch (no background watcher).");
                        return;
                    }
                    try
                    {
                        string[] current = NativeIsoLineSelectOverlay
                            .CaptureSelectedTags(dialog);
                        if (current.Length != requested.Length ||
                            !new HashSet<string>(
                                current, StringComparer.OrdinalIgnoreCase)
                                .SetEquals(requested))
                        {
                            Fail("Saved selection changed during native " +
                                "list reconstruction.");
                            return;
                        }

                        // REV108: avoid both premature checkbox commits and
                        // competing Autodesk list reloads. The text-filter
                        // clear itself may take several UI turns to restore
                        // the complete native project source.
                        int currentNativeRows = NativeIsoLineSelectOverlay
                            .NativeDisplayedCount(dialog) ?? 0;
                        bool fullNativeSource = currentNativeRows > 0 &&
                            (knownNativeProjectRows <= 0 ||
                             currentNativeRows >= knownNativeProjectRows) &&
                            NativeIsoLineSelectOverlay.IsNativeSearchBlank(dialog);

                        if (deferredProjectRefreshPending && !fullNativeSource &&
                            !deferredFallbackAttempted && polls >= 24)
                        {
                            deferredFallbackAttempted = true;
                            bool fallback = NativeIsoToolbarClickFilter
                                .TryRefreshNativeProjectList(dialog);
                            SafeLog(
                                "\nSPTC REV108: bounded Project fallback " +
                                "after cleared search remained incomplete " +
                                "(rows=" + currentNativeRows + "/" +
                                knownNativeProjectRows + ", invoked=" +
                                fallback + ").");
                            stableSamples = 0;
                            lastNativeSignature = "";
                            return;
                        }
                        if (!fullNativeSource) return;

                        // Wait for the native original-list names to
                        // stabilize. No selection writes while list changes.
                        string nativeSignature = NativeIsoLineSelectOverlay
                            .NativeRouteNameSignature(dialog);
                        if (string.IsNullOrEmpty(nativeSignature)) return;
                        if (!nativeSignature.Equals(lastNativeSignature,
                                StringComparison.Ordinal))
                        {
                            if (appliedOnce)
                            {
                                // REV108: Autodesk occasionally completes
                                // a final native row swap AFTER our first
                                // check commit. Retry only after the new
                                // entire source settles; never submit a
                                // partial checked batch or forge Create.
                                if (++nativeRebuildRetries > 2)
                                {
                                    Fail("Autodesk rebuilt its checklist " +
                                        "repeatedly during ISO preparation.");
                                    return;
                                }
                                appliedOnce = false;
                                afterAppliedPolls = 0;
                                readySamples = 0;
                                nudgedOnce = false;
                                alternateRepairAttempted = false;
                                SafeLog(
                                    "\nSPTC REV108: native rows replaced " +
                                    "after checkbox commit; waiting for " +
                                    "stable full-source rebuild #" +
                                    nativeRebuildRetries + ".");
                            }
                            lastNativeSignature = nativeSignature;
                            stableSamples = 0;
                            return;
                        }
                        // REV108: native names can stabilize before Autodesk
                        // finishes deferred checked-state updates. Hold the
                        // list read-only for ~1 second before FIRST commit.
                        if (++stableSamples < 6)
                            return;

                        if (!appliedOnce)
                        {
                            bool exact = NativeIsoLineSelectOverlay
                                .TryApplySavedBatchToNative(dialog, requested,
                                    out matched, out checkedCount);
                            if (!exact || checkedCount != requested.Length)
                                return;

                            appliedOnce = true;
                            afterAppliedPolls = 0;
                            // REV82: keep the plugin's selection/recovery guard
                            // until the exact native batch AND Create are ready.
                            // Releasing it here allowed the 800ms selection
                            // monitor to race Autodesk's checkbox refresh.
                            SafeLog(
                                "\nSPTC REV82: Full native batch applied " +
                                matched + "/" + requested.Length +
                                "; guarded verification before Create.");
                            return;
                        }

                        afterAppliedPolls++;
                        string[] verified = NativeCheckedRouteNames(dialog);
                        checkedCount = verified.Length;
                        if (checkedCount != requested.Length ||
                            !new HashSet<string>(
                                verified, StringComparer.OrdinalIgnoreCase)
                                .SetEquals(requested))
                        {
                            readySamples = 0;
                            // REV115: repeated REV94 simulated clicks on
                            // the same missing tag (e.g. 002) temporarily
                            // produce Checked=True and then Autodesk resets
                            // the item again, causing endless 3/4 -> 4/4
                            // oscillation. Do not replay native mouse clicks.
                            // Try exactly ONE original CheckedListBox managed
                            // ItemCheck correction after a stable full-source
                            // mismatch, then observe without further writes.
                            if (afterAppliedPolls > 5)
                            {
                                var found = new HashSet<string>(
                                    verified, StringComparer.OrdinalIgnoreCase);
                                string[] missing = requested
                                    .Where(t => !found.Contains(t)).ToArray();
                                if (!alternateRepairAttempted)
                                {
                                    alternateRepairAttempted = true;
                                    bool corrected = NativeIsoLineSelectOverlay
                                        .TryCommitOneMissingNativeBatchWithoutMouse(
                                            dialog, requested,
                                            out string correctedTag,
                                            out int correctedChecked);
                                    SafeLog(
                                        "\nSPTC REV115: guarded single native " +
                                        "ItemCheck repair, route=" +
                                        (correctedTag.Length == 0
                                            ? "(none)" : correctedTag) +
                                        ", result=" + corrected +
                                        ", checked=" + correctedChecked +
                                        "/" + requested.Length +
                                        ", previouslyMissing=" +
                                        string.Join(", ", missing) + ".");
                                    // Whether Autodesk accepted the action
                                    // or deferred it, do not force a second
                                    // click in the same source epoch.
                                    afterAppliedPolls = 0;
                                    return;
                                }

                                Fail("Native checked selection unstable (" +
                                    checkedCount + "/" + requested.Length +
                                    ") after one guarded non-mouse repair. " +
                                    "Missing: " + string.Join(", ", missing) +
                                    ". Native Create was not invoked.");
                                return;
                            }
                            return;
                        }

                        if (!create.Enabled)
                        {
                            if (!nudgedOnce)
                            {
                                nudgedOnce = true;
                                NativeIsoLineSelectOverlay
                                    .TryNudgeNativeBatchCreateEligibility(
                                        dialog, requested);
                            }
                            if (afterAppliedPolls >= 7)
                                Fail("All " + requested.Length +
                                    " routes are checked, but native " +
                                    "Autodesk Create remains disabled.");
                            return;
                        }

                        // REV115: require the exact native batch and real
                        // Create eligibility to persist across multiple
                        // settled UI polls after any native correction.
                        // Do not submit on an instant Checked=True snapshot.
                        if (++readySamples < 6) return;
                        // REV82: end recovery guard at the last possible point,
                        // after two exact native 6/6 + enabled samples.
                        // No timer tick can intervene before native PerformClick.
                        NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                        string[] finalChecked = NativeCheckedRouteNames(dialog);
                        if (!new HashSet<string>(
                            finalChecked, StringComparer.OrdinalIgnoreCase)
                            .SetEquals(requested) ||
                            finalChecked.Length != requested.Length ||
                            !create.Enabled)
                        {
                            Fail("Native Create eligibility changed during " +
                                "the final REV82 handoff.");
                            return;
                        }
                        // Submit ONCE only when exact native check set and
                        // Autodesk eligibility both remain verified.
                        try { timer.Stop(); timer.Dispose(); }
                        catch (Exception) { }
                        _autoCreateTimer = null;
                        _autoCreatePending = false;
                        _batchPreparationFailed = false;
                        _autoCreateSubmitting = true;
                        SafeLog(
                            "\nSPTC REV82: verified exact native batch " +
                            verified.Length + "/" + requested.Length +
                            ", Create=True; invoking native Create once.");
                        try { create.PerformClick(); }
                        finally { _autoCreateSubmitting = false; }
                    }
                    catch (Exception ex)
                    {
                        Fail("Automatic batch validation exception: " +
                            ex.GetBaseException().Message);
                    }
                };
                timer.Start();
            }));
        }
        catch (Exception ex)
        {
            AbortAutoCreate();
            ShowAutoCreateFailure(ex.GetBaseException().Message);
        }
    }

    private static void ShowCreateSelectionWarning(string warning)
    {
        SafeLog(
            "\nSPTC REV78: blocked Create: " +
            warning.Replace("\r", "").Replace("\n", " "));
        try
        {
            MessageBox.Show(
                _overlay, warning, "SPTC ISO Selection Mismatch",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception) { }
    }

    private sealed class CreateClickPreflightFilter : IMessageFilter
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_COMMAND = 0x0111;

        public bool PreFilterMessage(ref Message m)
        {
            if (_native == IntPtr.Zero ||
                _nativeCreateButton is not { IsDisposed: false } create ||
                !IsWindow(_native) || _autoCreateSubmitting)
                return false;

            bool buttonMessage = m.HWnd == create.Handle;
            bool mouseDown = buttonMessage &&
                (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK);
            bool buttonKey = buttonMessage &&
                (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN) &&
                ((int)m.WParam == (int)Keys.Enter ||
                 (int)m.WParam == (int)Keys.Space);
            // WinForms default-button Enter/Access key may send BN_CLICKED
            // via WM_COMMAND to the parent rather than to the button.
            bool parentCommand = m.HWnd == _native &&
                m.Msg == WM_COMMAND &&
                m.LParam == create.Handle &&
                (((long)m.WParam >> 16) & 0xffff) == 0;

            if (buttonMessage && m.Msg == WM_LBUTTONUP &&
                _swallowCreateMouseUp)
            {
                _swallowCreateMouseUp = false;
                return true;
            }
            if (!mouseDown && !buttonKey && !parentCommand)
                return false;

            if (!_autoCreatePending &&
                !TryGetCreateSelectionMismatch(_native, out _))
            {
                // A NEW explicit valid native Create action after a prior
                // failed batch resets the failure latch. The filter is run
                // before the button's WinForms MouseDown/Click handlers.
                _batchPreparationFailed = false;
                return false;
            }

            if (mouseDown) _swallowCreateMouseUp = true;
            _createRequested = false;
            BeginAutomaticBatchCreate(_native);
            return true;
        }
    }

    // REV54: gather ONLY checked route names from the original Autodesk
    // ISO checklist before the native Create dialog closes. The folder
    // projection is not authoritative; prefer the native largest list.
    private static void CaptureSelectedIsoTags(IntPtr dialog)
    {
        if (dialog == IntPtr.Zero) return;
        var best = new List<string>();
        int bestSize = -1;

        EnumChildWindows(dialog, (hwnd, _) =>
        {
            Control? control = Control.FromHandle(hwnd);
            if (control is CheckedListBox list &&
                !list.IsDisposed && list.Items.Count > bestSize &&
                list.Height > 100)
            {
                var selected = new List<string>();
                for (int i = 0; i < list.Items.Count; i++)
                    if (list.GetItemChecked(i))
                    {
                        string tag = list.GetItemText(list.Items[i]).Trim();
                        if (tag.Length > 0) selected.Add(tag);
                    }
                if (selected.Count > 0)
                {
                    best = selected;
                    bestSize = list.Items.Count;
                }
            }
            else if (control is ListView view &&
                     !view.IsDisposed && view.CheckBoxes &&
                     view.Items.Count > bestSize && view.Height > 100)
            {
                var selected = view.CheckedItems.Cast<ListViewItem>()
                    .Select(i => i.Text.Trim())
                    .Where(s => s.Length > 0).ToList();
                if (selected.Count > 0)
                {
                    best = selected;
                    bestSize = view.Items.Count;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (best.Count == 0) return; // preserve old behavior if not identified
        _selectedIsoTags.Clear();
        foreach (string tag in best)
            _selectedIsoTags.Add(tag);
        NativeIsoResultsOverlay.SetExpectedIsoTags(best);
        SafeLog(
            "\nSPTC REV54: Native ISO selected lines=" + best.Count +
            " (custom copies limited to these routes).");
    }

    private static bool MatchesCurrentIsoBatch(
        string dwgFileName, IReadOnlyCollection<string> tags)
    {
        if (tags.Count == 0) return true;
        string stem = Path.GetFileNameWithoutExtension(dwgFileName);
        foreach (string tag in tags)
        {
            if (stem.Equals(tag, StringComparison.OrdinalIgnoreCase))
                return true;
            if (stem.StartsWith(tag + " (", StringComparison.OrdinalIgnoreCase) &&
                stem.EndsWith(")", StringComparison.Ordinal) &&
                int.TryParse(stem.Substring(tag.Length + 2,
                    stem.Length - tag.Length - 3), out int seq) && seq > 0)
                return true;
            if (stem.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(stem.Substring(tag.Length + 1), out int seq2) &&
                seq2 > 0)
                return true;
        }
        return false;
    }

    private static void CaptureBeforeNativeCreate(object? sender, EventArgs e)
    {
        // REV64: treat only an actual Create button action as authorization
        // to monitor/copy. Never infer Create merely from dialog closure.
        if (_createRequested) return;
        // REV80: a FAILED automatic attempt can trigger a delayed
        // WinForms Create.MouseDown/Click notification. Never convert
        // that stale event into a legitimate ISO generation request.
        if (_batchPreparationFailed && !_autoCreateSubmitting)
            return;
        // The ordinary Create button is intercepted before its native
        // handler whenever a filtered view hides saved tags. An automatic
        // invocation is permitted ONLY after all requested names have been
        // verified in Autodesk's original native checked list.
        if (_native != IntPtr.Zero &&
            TryGetCreateSelectionMismatch(_native, out _))
        {
            SafeLog(
                "\nSPTC REV79: unverified direct native Create action; " +
                "full saved selection was not captured for custom copying.");
            return;
        }
        _createRequested = true;
        if (_native != IntPtr.Zero && IsWindow(_native))
        {
            CaptureNativeOverwrite(_native);
            CaptureSelectedIsoTags(_native);
        }
        SafeLog(
            "\nSPTC REV64: Create requested; custom overwrite=" +
            (_overwriteRequested ? "YES" : "NO") +
            " (source: " + _overwriteSource + ").");
    }

    private static void OnNativeCreateKeyDown(object? sender, KeyEventArgs e)
    {
        // WinForms default-button activation by Enter/Space may not raise
        // MouseDown. Capture before Autodesk's own Click closes the form.
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            CaptureBeforeNativeCreate(sender, e);
    }

    private static void TrackNativeCancelClose(IntPtr dialog)
    {
        // REV80: don't leave a preparation timer alive after Cancel/X/Esc.
        // FormClosed also covers closing via Alt+F4 and Autodesk's X.
        if (_nativeParentForm is null &&
            Control.FromHandle(dialog) is Form nativeForm &&
            !nativeForm.IsDisposed)
        {
            _nativeParentForm = nativeForm;
            nativeForm.FormClosed += OnNativeFormClosed;
        }
        if (_nativeCancelButton is { IsDisposed: false }) return;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is Button button &&
                !button.IsDisposed &&
                WindowText(hwnd).Trim().Equals("Cancel",
                    StringComparison.OrdinalIgnoreCase))
            {
                _nativeCancelButton = button;
                button.MouseDown += OnNativeCancelAction;
                button.Click += OnNativeCancelAction;
                return false;
            }
            return true;
        }, IntPtr.Zero);
    }

    private static void TrackNativeCreateButton(IntPtr dialog)
    {
        if (_nativeCreateButton is { IsDisposed: false })
            return;

        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is Button button &&
                !button.IsDisposed &&
                WindowText(hwnd).Trim().Equals("Create",
                    StringComparison.OrdinalIgnoreCase))
            {
                _nativeCreateButton = button;
                // MouseDown runs BEFORE native Click/Close. Include keyboard
                // and programmatic Click so non-mouse activation still works.
                button.MouseDown += CaptureBeforeNativeCreate;
                button.KeyDown += OnNativeCreateKeyDown;
                button.Click += CaptureBeforeNativeCreate;
                if (_createPreflightFilter is null)
                {
                    _createPreflightFilter = new CreateClickPreflightFilter();
                    System.Windows.Forms.Application.AddMessageFilter(_createPreflightFilter);
                }
                return false;
            }
            return true;
        }, IntPtr.Zero);
    }


    private static void StartOverwriteTracker(IntPtr dialog)
    {
        CaptureNativeOverwrite(dialog);
        TrackNativeCreateButton(dialog);
        TrackNativeCancelClose(dialog);
        if (_overwriteTracker != null) return;
        _overwriteTracker = new System.Windows.Forms.Timer
        {
            Interval = 180
        };
        _overwriteTracker.Tick += (_, _) =>
        {
            if (_native != dialog || _native == IntPtr.Zero)
                return;
            CaptureNativeOverwrite(dialog);
            TrackNativeCreateButton(dialog);
            TrackNativeCancelClose(dialog);
        };
        _overwriteTracker.Start();
    }

    private static void DetachNativeOverwriteTracker()
    {
        try { _overwriteTracker?.Stop(); _overwriteTracker?.Dispose(); }
        catch (Exception) { }
        _overwriteTracker = null;

        if (_createPreflightFilter != null)
        {
            System.Windows.Forms.Application.RemoveMessageFilter(_createPreflightFilter);
            _createPreflightFilter = null;
        }
        _swallowCreateMouseUp = false;
        // A failed/canceled auto-preparation never authorizes a watcher.
        if (_batchPreparationFailed) _createRequested = false;
        StopAutoCreate();
        if (_nativeCancelButton is { IsDisposed: false } cancel)
        {
            cancel.MouseDown -= OnNativeCancelAction;
            cancel.Click -= OnNativeCancelAction;
        }
        _nativeCancelButton = null;
        if (_nativeParentForm is { IsDisposed: false } form)
            form.FormClosed -= OnNativeFormClosed;
        _nativeParentForm = null;
        if (_nativeCreateButton is { IsDisposed: false } create)
        {
            create.MouseDown -= CaptureBeforeNativeCreate;
            create.KeyDown -= OnNativeCreateKeyDown;
            create.Click -= CaptureBeforeNativeCreate;
        }
        _nativeCreateButton = null;
        if (_nativeOverwriteBox is { IsDisposed: false } box)
            box.CheckedChanged -= OnNativeOverwriteChanged;
        _nativeOverwriteBox = null;
        _nativeOverwriteHandle = IntPtr.Zero;
        // DO NOT reset the last confirmed flag here: the copy starts
        // after the native Create dialog has closed.
    }

    private sealed record CustomCopyOutput(string Path, bool Existing);
    private sealed record CopyReport(
        int Copied, int Skipped, int OriginalsDeleted,
        int OriginalsRetained, string Destination,
        CustomCopyOutput[] Outputs,
        bool AllCustomFilesVerified, string[] MissingRoutes,
        string[] UnprocessedNativeFiles);

    // REV122: Autodesk owns the existing Isometric Creation Complete
    // notification. We do not issue a second SPTC taskbar balloon.
    // Custom-copy readiness is tracked independently in the status log;
    // on completion the copy worker and timer are released automatically.
    // The original Autodesk balloon timing is not overridden.

    public static void Launch(Document doc)
    {
        if (_copyTask is { IsCompleted: false })
        {
            doc.Editor.WriteMessage("\nSPTC: A background ISO export is still running.");
            return;
        }
        Detach();
        // REV48: clear only the previous results links, not the approved
        // REV47 custom DWG copier or Autodesk's native results UI.
        NativeIsoResultsOverlay.Reset();
        _selectedIsoTags.Clear();
        DetachNativeOverwriteTracker();
        _overwriteRequested = false;
        _overwriteFound = false;
        _overwriteSource = "not detected";
        _copyTask = null;
        while (_copyProgress.TryDequeue(out _)) { }
        _copyState = "Waiting for Production Iso dialog";
        _lastCopyReport = null;
        _document = doc;
        _source = null;
        _destination = "";
        _destinationEditedThisDialog = false;
        _launchUtc = DateTime.UtcNow;
        _noWindowTicks = 0;
        _nativeSeen = false;
        _resultsSeen = false;
        _nativeClosedAt = DateTime.MinValue;
        _nativePopupObservedAt = DateTime.MinValue;
        _resultsFirstObservedAt = DateTime.MinValue;
        _customFirstCopiedAt = DateTime.MinValue;
        _customBatchVerifiedAt = DateTime.MinValue;
        _lastPopupProbeAt = DateTime.MinValue;
        _nativePopupWindowType = "(not detected)";
        _createRequested = false;
        _batchPreparationFailed = false;
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

    // REV125: observation only. The Autodesk ISO completion balloon may
    // be a native/custom window whose title isn't exposed via Win32;
    // absence of a match MUST NOT be interpreted as proof it did not
    // appear. We inspect only top-level windows owned by this AutoCAD
    // process, at most once each 1.5 seconds while custom copying.
    private static void ProbeNativeIsoCompletionBalloon()
    {
        if (string.IsNullOrWhiteSpace(_destination) ||
            !_createRequested || _nativePopupObservedAt != DateTime.MinValue ||
            _nativeClosedAt == DateTime.MinValue ||
            (DateTime.UtcNow - _lastPopupProbeAt).TotalMilliseconds < 1500)
            return;
        _lastPopupProbeAt = DateTime.UtcNow;
        try
        {
            uint processId = (uint)Process.GetCurrentProcess().Id;
            string? kind = null;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint owner);
                if (owner != processId) return true;
                string caption = WindowText(hwnd);
                if (caption.Contains("Isometric Creation Complete",
                        StringComparison.OrdinalIgnoreCase) ||
                    caption.Contains("All isometrics created successfully",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var className = new StringBuilder(128);
                    GetClassName(hwnd, className, className.Capacity);
                    kind = className.ToString();
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (kind is null) return;
            _nativePopupObservedAt = DateTime.UtcNow;
            _nativePopupWindowType = kind;
            SafeLog("\nSPTC REV125: native ISO completion balloon " +
                "observed (read-only), windowClass=" + kind +
                ", customTransferPending=" +
                (_copyTask is { IsCompleted: false }) + ".");
        }
        catch (Exception ex)
        {
            SafeLog("\nSPTC REV125: native balloon observation unavailable: " +
                ex.GetBaseException().Message);
        }
    }

    private static string EventClock(DateTime value) =>
        value == DateTime.MinValue ? "(not observed)" :
        value.ToLocalTime().ToString("HH:mm:ss.fff");

    private static void OnTick(object? sender, EventArgs e)
    {
        try
        {
            ProbeNativeIsoCompletionBalloon();
            // REV33: the user sees each destination DWG as soon as its
            // atomic copy finishes; the batch watcher may remain alive to
            // collect the rest of a multi-line Production Iso job.
            while (_copyProgress.TryDequeue(out string? savedName))
            {
                // REV57: include an existing destination skipped by the
                // safe no-overwrite policy, without claiming it was copied.
                bool existing = savedName.StartsWith(
                    "existing:", StringComparison.OrdinalIgnoreCase);
                string actualName = existing
                    ? savedName.Substring("existing:".Length)
                    : savedName;
                string resultPath = Path.Combine(_destination, actualName);
                NativeIsoResultsOverlay.ReportCopied(resultPath, existing);
                if (!existing && _customFirstCopiedAt == DateTime.MinValue)
                    _customFirstCopiedAt = DateTime.UtcNow;
                _copyState = (existing ? "Existing " : "Custom saved ") +
                    actualName + "; monitoring remaining ISOs";
                SafeLog(
                    "\nSPTC REV57: Custom ISO " +
                    (existing ? "already exists: " : "copied: ") + resultPath);
            }

            if (_copyTask is not null && _copyTask.IsCompleted)
            {
                Task<CopyReport> finished = _copyTask;
                _copyTask = null;
                // REV107: reconcile all verified custom files before
                // stopping our status monitor. Files may have been copied
                // correctly even if their UI notifications were interrupted
                // by a source DWG Close/Open during background generation.
                if (finished.Status == TaskStatus.RanToCompletion)
                {
                    foreach (CustomCopyOutput output in finished.Result.Outputs)
                    {
                        try
                        {
                            NativeIsoResultsOverlay.ReportCopied(
                                output.Path, output.Existing);
                        }
                        catch (Exception ex)
                        {
                            SafeLog("\nSPTC REV107: deferred Results link " +
                                "refresh: " + ex.GetBaseException().Message);
                        }
                    }
                }
                // REV122: Detach closes this UI watch timer. The async
                // copier already disposed its FileSystemWatcher in
                // CopyFilesAsync.finally, and _copyTask is now null.
                // A new SPTC_PRODISO run is no longer blocked.
                // REV124: only after the worker has finished staging,
                // hashing, and publishing its custom files may the
                // Results UI hide original links for fully copied routes.
                // This is a UI-only operation, not disk deletion.
                if (finished.Status == TaskStatus.RanToCompletion)
                    NativeIsoResultsOverlay.FinalizeCustomTransferView();
                Detach();
                if (finished.IsFaulted)
                {
                    _copyState = "Custom transfer failed — watcher stopped";
                    SafeLog("\nSPTC REV122: " + _copyState + ": " +
                        finished.Exception?.GetBaseException().Message);
                }
                else if (finished.IsCanceled)
                {
                    _copyState = "Custom transfer canceled — watcher stopped";
                    SafeLog("\nSPTC REV122: " + _copyState);
                }
                else
                {
                    _lastCopyReport = finished.Result;
                    if (_lastCopyReport.AllCustomFilesVerified)
                        _customBatchVerifiedAt = DateTime.UtcNow;
                    _copyState = _lastCopyReport.AllCustomFilesVerified
                        ? "Custom transfer verified — watcher stopped"
                        : "Custom transfer incomplete — watcher stopped";
                    SafeLog(
                        $"\nSPTC REV124: {_lastCopyReport.Copied} DWG(s) copied to {_lastCopyReport.Destination}; " +
                        $"skipped={_lastCopyReport.Skipped}, verifiedComplete={_lastCopyReport.AllCustomFilesVerified}, " +
                        $"missingRoutes={string.Join(", ", _lastCopyReport.MissingRoutes)}, " +
                        $"pendingNativeDWGs={string.Join(", ", _lastCopyReport.UnprocessedNativeFiles)}. " +
                        "Original native DWGs preserved; only verified Results rows hide original links. " +
                        "Custom ISO watcher stopped; next SPTC_PRODISO is ready. " +
                        "Autodesk controls native completion notification." +
                        "\nSPTC REV125 timeline: nativePopup=" +
                        EventClock(_nativePopupObservedAt) +
                        ", resultsObserved=" +
                        EventClock(_resultsFirstObservedAt) +
                        ", firstCustomCopy=" +
                        EventClock(_customFirstCopiedAt) +
                        ", verifiedBatch=" +
                        EventClock(_customBatchVerifiedAt) +
                        ". No native notification settings changed.");
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
                    StartOverwriteTracker(native);
                    attachWatch.Stop();

                    SafeLog(
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
                        if (_quietShowAllTimer is not null)
                            return false; // ignore repeat clicks mid-validation

                        // REV98: cancel any unfinished Active projection on
                        // a new user-selected scope, and capture the full
                        // original native project list before touching views.
                        StopActiveProjectProjection();
                        if (showActive)
                            NativeIsoLineSelectOverlay.RememberUnfilteredProjectRows(
                                native);
                        // REV65: snapshot CHECKED tag names, never item
                        // indices, before the native toolbar changes mode.
                        // Autodesk can replace its original checklist rows.
                        string[] selectedBefore =
                            NativeIsoLineSelectOverlay.CaptureSelectedTags(native);

                        // REV112: Show All is a VIEW change. REV111 waited for
                        // native checkbox/Create recovery before closing the
                        // Active presentation. When Autodesk reset a checked
                        // route, the guarded timer timed out and trapped the
                        // user in Show Active (often Create.Enabled=False).
                        // Always reveal Autodesk's full source immediately;
                        // reconcile by exact saved TAGS on the visible native
                        // list, with the existing bounded recovery routine.
                        if (!showActive &&
                            ProjectFolderLinePicker.ActiveDwgView &&
                            NativeIsoLineSelectOverlay
                                .HasCompleteUnfilteredProjectSource(
                                    native, allowActivePresentation: true))
                        {
                            string[] nativeChecked =
                                NativeCheckedRouteNames(native);
                            var saved = new HashSet<string>(
                                selectedBefore, StringComparer.OrdinalIgnoreCase);
                            bool exact = nativeChecked.Length == saved.Count &&
                                new HashSet<string>(nativeChecked,
                                    StringComparer.OrdinalIgnoreCase)
                                    .SetEquals(saved);
                            bool createReady = saved.Count == 0
                                ? _nativeCreateButton is { IsDisposed: false,
                                                           Enabled: false }
                                : _nativeCreateButton is { IsDisposed: false,
                                                           Enabled: true };

                            StopQuietShowAll();
                            ProjectFolderLinePicker.ClearVisibleProjection();
                            NativeIsoToolbarClickFilter.MarkAllLinesRestored(
                                native);

                            if (exact && createReady)
                            {
                                NativeIsoLineSelectOverlay.CancelScopeSwitch(
                                    native);
                                NativeIsoLineSelectOverlay.UpdateDisplayedCount();
                                SafeLog(
                                    "\nSPTC REV112: Show All immediate; " +
                                    "native " + nativeChecked.Length + "/" +
                                    saved.Count + " exact checked and " +
                                    "Autodesk Create state valid. No native " +
                                    "checkbox replay or Project reload.");
                            }
                            else
                            {
                                // REV112: recover only after the user's full
                                // native list is visible. This avoids repeated
                                // attempts to check hidden source rows while
                                // the Active projection is on screen.
                                // Create remains Autodesk-controlled; the
                                // existing guarded Create preflight rejects
                                // partial selections.
                                NativeIsoLineSelectOverlay
                                    .ScheduleModeChangeRecovery(
                                        native, selectedBefore);
                                SafeLog(
                                    "\nSPTC REV112: Show All immediate (" +
                                    nativeChecked.Length + " native checked, " +
                                    saved.Count + " saved, Create.Enabled=" +
                                    (_nativeCreateButton?.Enabled.ToString()
                                        ?? "unknown") +
                                    "); bounded native synchronization " +
                                    "started on visible All Project list.");
                            }
                            return true;
                        }

                        // REV68: forbid programmatic native checkbox events
                        // during toolbar scope changes from being counted as
                        // intentional user unchecks. The A-08 tag must remain
                        // selected while its checklist is being rebuilt.
                        NativeIsoLineSelectOverlay.EndProjectRedrawBatch();
                        NativeIsoLineSelectOverlay.BeginScopeSwitch(native);

                        // A previous folder projection may still be present.
                        // Remove it so the user's pointer reaches Autodesk's
                        // genuine line list, never an overlaid WinForms copy.
                        ProjectFolderLinePicker.ClearVisibleProjection();

                        if (!showActive)
                        {
                            // REV100: Active is only an exact-tag
                            // presentation of the full native project source.
                            // If all six checks AND original Create are
                            // already valid, switching to Show All must
                            // NEVER click Project again or recheck rows.
                            NativeIsoExactLineFilter.End(restore: true);
                            bool cleared = ActiveDwgTags.ClearOneTag(
                                native, _document);
                            if (!cleared)
                            {
                                NativeIsoLineSelectOverlay.CancelScopeSwitch(native);
                                return false;
                            }

                            bool fullSource = NativeIsoLineSelectOverlay
                                .HasCompleteUnfilteredProjectSource(native);
                            var wanted = new HashSet<string>(
                                selectedBefore, StringComparer.OrdinalIgnoreCase);
                            string[] nativeChecked = fullSource
                                ? NativeCheckedRouteNames(native)
                                : Array.Empty<string>();
                            bool nativeExact = fullSource &&
                                nativeChecked.Length == wanted.Count &&
                                new HashSet<string>(nativeChecked,
                                    StringComparer.OrdinalIgnoreCase)
                                    .SetEquals(wanted);

                            if (nativeExact &&
                                _nativeCreateButton is { IsDisposed: false,
                                                         Enabled: true })
                            {
                                // No Autodesk source mutation: preserve exact
                                // checked routes and existing Create state.
                                NativeIsoLineSelectOverlay.CancelScopeSwitch(native);
                                NativeIsoLineSelectOverlay.UpdateDisplayedCount();
                                SafeLog(
                                    "\nSPTC REV100: Show All native fast path; " +
                                    nativeChecked.Length + "/" + wanted.Count +
                                    " checked, Create.Enabled=True, " +
                                    "no project refresh or checkbox replay.");
                                return true;
                            }

                            // Fallback only when the source/selection really
                            // needs reconstruction. A full source with a
                            // missing tag does not need an additional native
                            // Project click; the guarded repair will validate
                            // exact names before any checkbox changes.
                            bool recoveryScheduled = false;
                            if (!fullSource)
                                NativeIsoLineSelectOverlay.BeginProjectRedrawBatch(
                                    native);
                            try
                            {
                                bool refreshed = !fullSource &&
                                    NativeIsoToolbarClickFilter
                                        .TryRefreshNativeProjectList(native);
                                SafeLog(
                                    "\nSPTC REV100: Show All recovery (fullSource=" +
                                    fullSource + ", refresh=" + refreshed +
                                    ", checked=" + nativeChecked.Length + "/" +
                                    wanted.Count + ").");
                                NativeIsoLineSelectOverlay.UpdateDisplayedCount();
                                NativeIsoLineSelectOverlay
                                    .ScheduleModeChangeRecovery(
                                        native, selectedBefore);
                                recoveryScheduled = true;
                                return true;
                            }
                            finally
                            {
                                if (!recoveryScheduled)
                                    NativeIsoLineSelectOverlay.EndProjectRedrawBatch();
                            }
                        }

                        // REV71: log separately the cost of the first
                        // physical current-DWG model scan. This is the
                        // existing scan, not an added model/database pass.
                        if (!ActiveDwgTags.HasScanned)
                        {
                            var scanWatch = Stopwatch.StartNew();
                            ActiveDwgTags.Scan(_document);
                            scanWatch.Stop();
                            SafeLog(
                                "\nSPTC REV71: first active DWG tag scan " +
                                scanWatch.ElapsedMilliseconds + " ms.");
                        }

                        // REV105: multiple physical line tags per Active
                        // DWG are valid; reject only when the read-only
                        // Plant scan found NONE.
                        string[] physicalTags =
                            ActiveDwgTags.CurrentTags.ToArray();
                        if (physicalTags.Length == 0)
                        {
                            SafeLog(
                                "\nSPTC REV105: No verified physical pipe " +
                                "line numbers in active DWG. Native list " +
                                "left unchanged.");
                            NativeIsoLineSelectOverlay.CancelScopeSwitch(native);
                            return false;
                        }

                        // REV98: Active DWG is a pure exact-tag projection
                        // of the original COMPLETE project source. We must
                        // NOT set Autodesk's native search to A-08: it
                        // reindexes the list to five substring matches and
                        // disables native Create on a later filter return.
                        // Existing saved route selections remain checked in
                        // the real 432-row list behind this one-row view.
                        NativeIsoExactLineFilter.End(restore: true);
                        if (!ActiveDwgTags.ClearOneTag(native, _document))
                        {
                            NativeIsoLineSelectOverlay.CancelScopeSwitch(native);
                            return false;
                        }
                        // REV99: when coming from an already-complete
                        // unfiltered project list, clicking the native
                        // Project toolbar again causes a costly rebuild and
                        // invalidates Create eligibility/checkbox events.
                        // Do it only after a manual search or partial source.
                        int knownProjectRows =
                            NativeIsoLineSelectOverlay.KnownProjectWideRowCount(
                                native);
                        int displayedNativeRows =
                            NativeIsoLineSelectOverlay.NativeDisplayedCount(
                                native) ?? 0;
                        bool alreadyFullSource =
                            NativeIsoLineSelectOverlay.IsNativeSearchBlank(native) &&
                            knownProjectRows > 0 &&
                            displayedNativeRows >= knownProjectRows;
                        bool projectRefresh = !alreadyFullSource &&
                            NativeIsoToolbarClickFilter.TryRefreshNativeProjectList(
                                native);
                        NativeIsoToolbarClickFilter.MarkAllLinesRestored(native);
                        SafeLog(
                            "\nSPTC REV105: restoring All Project native " +
                            "selection for " + physicalTags.Length +
                            " exact Active DWG tag(s) (refresh=" +
                            projectRefresh + "): " +
                            string.Join(", ", physicalTags));
                        return BeginActiveProjectProjection(
                            native, physicalTags, selectedBefore);
                    });

                }

                // Keep native overwrite state synchronized; reading one
                // checkbox is small, no PnP/database or file operations.
                CaptureNativeOverwrite(native);

                // Path discovery only; never enumerate network DWGs on the UI thread.
                if (string.IsNullOrEmpty(_source))
                {
                    _source = ReadSourcePath(native);
                    if (!string.IsNullOrEmpty(_source))
                    {
                        // Only restore an explicit destination previously
                        // used for this very same native output path.
                        // Never automatically reuse another project's folder.
                        if (!_destinationEditedThisDialog &&
                            _source.Equals(_rememberedSource,
                                StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(_rememberedDestination))
                        {
                            _destination = _rememberedDestination;
                            if (_destinationBox is { IsDisposed: false } box)
                                box.Text = _destination;
                            SafeLog(
                                "\nSPTC REV78: restored last custom location " +
                                "for this ISO output source.");
                        }
                        else if (_destinationEditedThisDialog)
                        {
                            _rememberedSource = _source;
                            _rememberedDestination = _destination;
                        }
                        SafeLog(
                            "\nSPTC ISO detected native source: " + _source);
                    }
                }
                return;
            }

            if (_native != IntPtr.Zero)
            {
                DetachNativeOverwriteTracker();
                RemoveOverlay();

                // REV80: failure/cancellation is not a native generation
                // request, even if Autodesk emitted a delayed button Click.
                if (_batchPreparationFailed)
                    _createRequested = false;

                // REV64: Cancel / X / Esc closes exactly the same Autodesk
                // dialog as Create. Never run the custom copier if Create
                // was not explicitly requested. This closes all UI timers,
                // folder scans and native hooks, and prevents stale DWG
                // enumeration after the ISO selection was abandoned.
                if (!_createRequested)
                {
                    Detach();
                    NativeIsoResultsOverlay.Reset();
                    _selectedIsoTags.Clear();
                    while (_copyProgress.TryDequeue(out _)) { }
                    _nativeClosedAt = DateTime.MinValue;
                    _copyState = "Canceled before Create — no ISO copy started";
                    SafeLog(
                        "\nSPTC REV64: Production Iso canceled; " +
                        "all plugin monitors stopped. No DWG copy started.");
                    return;
                }

                _nativeClosedAt = DateTime.UtcNow;
                _copyState = "Create requested; watching for completed DWGs";
                // REV107: retain only the ISO batch snapshot (source,
                // destination, selected tags, overwrite). The originating
                // modeling Document is no longer required after Create and
                // may be closed or replaced during background generation.
                _document = null;
            }

            // Autodesk may display a notification first and open this dialog
            // only after the user clicks the notification. Do not require that.
            IntPtr results = FindDialog("Isometric Creation Results");
            if (results != IntPtr.Zero)
            {
                _resultsSeen = true;
                if (_resultsFirstObservedAt == DateTime.MinValue)
                    _resultsFirstObservedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(_destination))
                    NativeIsoResultsOverlay.BeginSession(_destination);
                if (_copyTask is null &&
                    !string.IsNullOrWhiteSpace(_destination) &&
                    !string.IsNullOrWhiteSpace(_source))
                    CopyCompletedCore();
            }

            // Start background detection immediately after the original Create dialog
            // closes; copying itself waits for a stable, unlocked output DWG.
            if (_nativeClosedAt != DateTime.MinValue && _copyTask is null)
            {
                if (!string.IsNullOrWhiteSpace(_destination) && string.IsNullOrWhiteSpace(_source))
                {
                    Detach();
                    _copyState = "Error: Native source folder was not detected";
                    SafeLog("\nSPTC ISO: " + _copyState +
                        ". Run SPTC_ISOSTATUS for details.");
                    return;
                }
                if (!string.IsNullOrWhiteSpace(_destination))
                {
                    CopyCompletedCore();
                    return;
                }
                Detach();
                _copyState = "Custom location empty; default output retained";
                SafeLog("\nSPTC ISO: " + _copyState);
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
            // REV107: AutoCAD is allowed to close/switch the modeling
            // document mid-ISO. A transient native UI/document exception
            // must not discard the batch-copy task or its Results links.
            SafeLog("\nSPTC REV107: ISO monitor UI transient: " +
                ex.GetBaseException().Message);
            if (_createRequested &&
                (_copyTask != null ||
                 _nativeClosedAt != DateTime.MinValue))
            {
                EnsureMonitor();
                return;
            }
            Detach();
            _copyState = "Error: " + ex.Message;
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
                SafeLog(
                    "\nSPTC REV27: Active filter verification inconclusive: " +
                    note + " Inspect the native line list before creating an ISO.");
            }
            else
                SafeLog(
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
                SafeLog(
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
            _destinationBox.TextChanged += (_, _) =>
            {
                _destination = _destinationBox.Text.Trim();
                _destinationEditedThisDialog = true;
                if (!string.IsNullOrWhiteSpace(_source))
                {
                    _rememberedSource = _source;
                    _rememberedDestination = _destination;
                }
            };
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
                    Description = "Choose FINAL destination for completed Production Isos",
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
            // REV44: remove Autodesk's startup search hint without clearing
            // actual line filters or user-entered search text.
            ActiveDwgTags.ClearInitialSearchHint(dialog);
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
                        // Native controls may finalize the cue banner after
                        // initial attachment. Recheck only the known default
                        // prompt on the first layout/update; user text is safe.
                        ActiveDwgTags.ClearInitialSearchHint(dialog);
                    }
                }
            };
            _layoutWatch.Start();

            SafeLog(
                "\nSPTC REV19: Native Production Iso dialog found; custom location row tracks Revision number when resized.");
        }
        catch (Exception ex)
        {
            SafeLog("\nSPTC: Unable to attach field: " + ex.Message);
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
        // REV43: release native search masking handlers when this ISO dialog
        // closes, so reopening starts with Autodesk's normal empty filter.
        ActiveDwgTags.ResetAutoFilterDisplay();
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
        StopQuietShowAll();
        StopActiveProjectProjection();
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

    // REV52: Autodesk may produce "A-08 (2).dwg" at its ORIGINAL
    // Production Iso location when a previous output remains open. The
    // CUSTOM location has a separate naming/locking policy: normalize
    // only Autodesk's FINAL numeric suffix. Never change/rename a native
    // Autodesk ISO source file.
    private static string CustomDestinationName(
        string nativeFileName, out bool numberedNativeSource)
    {
        string stem = Path.GetFileNameWithoutExtension(nativeFileName);
        string extension = Path.GetExtension(nativeFileName);
        numberedNativeSource = false;

        // Match " (1)", " (2)", ... only at the very end of the stem.
        // Preserve embedded parentheses or component IDs in real tags.
        int end = stem.Length - 1;
        if (end <= 3 || stem[end] != ')')
            return nativeFileName;

        int open = stem.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0 || open + 3 > end)
            return nativeFileName;

        string number = stem.Substring(open + 2, end - (open + 2));
        if (!int.TryParse(number, out int sequence) ||
            sequence < 1 || sequence > 10000 ||
            !number.All(char.IsDigit))
            return nativeFileName;

        string baseName = stem.Substring(0, open);
        if (baseName.Length == 0)
            return nativeFileName;

        numberedNativeSource = true;
        return baseName + extension;
    }

    // REV51: AutoCAD holds DWGs open while editing them. Do not attempt to
    // overwrite a custom DWG that another AutoCAD document/process has open.
    // An actual OS sharing violation is distinct from ACL, disk-full, or
    // unauthorized-access errors; only sharing/lock conflicts should cause
    // the requested numbered filename fallback.
    private static bool IsSharingViolation(IOException ex)
    {
        int code = ex.HResult & 0xFFFF;
        return code == 32 || code == 33; // ERROR_SHARING_VIOLATION / LOCK_VIOLATION
    }

    private static bool IsCustomDwgOpenOrLocked(string destination)
    {
        if (!File.Exists(destination)) return false;

        try
        {
            // Probe ONLY the custom destination, never Autodesk's original
            // native output. Dispose the test handle before publishing.
            using var probe = new FileStream(
                destination, FileMode.Open, FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return true;
        }
    }

    private static string MoveSnapshotToNextNumber(
        string completeTempPath, string originalDestination)
    {
        string folder = Path.GetDirectoryName(originalDestination) ??
            throw new InvalidOperationException("No custom destination folder.");
        string stem = Path.GetFileNameWithoutExtension(originalDestination);
        string extension = Path.GetExtension(originalDestination);

        // Match Autodesk-style numbering: A-08 (1).dwg, A-08 (2).dwg, ...
        // An existing numbered output is never overwritten, even when
        // the original native Overwrite checkbox is enabled.
        for (int n = 1; n <= 10000; n++)
        {
            string candidate = Path.Combine(
                folder, stem + " (" + n + ")" + extension);
            if (File.Exists(candidate))
                continue;

            try
            {
                // Atomic creation: handles a concurrent copy that claims the
                // same numbered file between Exists and Move.
                File.Move(completeTempPath, candidate);
                return candidate;
            }
            catch (IOException ex) when (
                File.Exists(candidate) &&
                ((ex.HResult & 0xFFFF) == 80 ||
                 (ex.HResult & 0xFFFF) == 183 ||
                 IsSharingViolation(ex)))
            {
                continue;
            }
        }

        throw new IOException(
            "All custom ISO numbered filenames (1)-(10000) are occupied.");
    }

    private static string PublishCustomSnapshot(
        string completeTempPath, string originalDestination, bool overwrite)
    {
        if (File.Exists(originalDestination) &&
            IsCustomDwgOpenOrLocked(originalDestination))
            return MoveSnapshotToNextNumber(
                completeTempPath, originalDestination);

        try
        {
            // REV47/REV50 normal behavior: overwrite only if the user
            // checked Autodesk's native Overwrite if existing checkbox.
            File.Move(completeTempPath, originalDestination, overwrite);
            return originalDestination;
        }
        catch (IOException ex) when (
            File.Exists(originalDestination) && IsSharingViolation(ex))
        {
            // A drawing can become locked AFTER our probe and before Move.
            // Retain the complete temporary snapshot and publish it safely
            // under the next free numbered name without touching the open
            // original DWG.
            return MoveSnapshotToNextNumber(
                completeTempPath, originalDestination);
        }
    }

    // REV113: stage current-batch native DWGs in the custom folder,
    // verify published bytes, then delete ONLY identical, unlocked source
    // DWGs. Never delete unrelated project/native files.
    //
    // REV47: safely snapshot ONLY finished native Production Iso DWGs.
    // REV46 held an EXCLUSIVE source FileStream open while copying, which
    // could conflict with Plant 3D's subsequent write/open operations and
    // plausibly trigger eFileSharingViolation. It also used a new _copyN
    // name each time the source DWG was rewritten. Both are removed.
    //
    // Wait for at least 4 seconds of stability PER FILE and 5 seconds of
    // silence for ALL Autodesk native output DWGs before taking a snapshot.
    // Read source with cooperative sharing, never FileShare.None, and
    // verify the source's length/modified time again BEFORE publishing the
    // atomically completed destination.
    //
    // Each source path is processed at most once per ISO run. If the
    // destination name already exists, KEEP that user file and skip this
    // copy; do not overwrite or invent _copy1, _copy2, etc. Use an empty
    // destination folder for a fresh run if overwrite was not requested.
    // REV117: Autodesk's native Results history and its deferred drawing-
    // opening commands may still reference the original native ISO paths
    // AFTER the custom DWG has been published. For safety, never delete
    // those source DWGs here. Keep the verified custom copy workflow.
    // A true custom-only PHYSICAL output requires a supported Autodesk
    // native destination configuration, not post-generation deletion.

    private static async Task<CopyReport> CopyFilesAsync(
        string source, string targetRoot, DateTime sinceUtc,
        bool overwriteExisting, IReadOnlyCollection<string> selectedTags)
    {
        int copied = 0, skipped = 0;
        // REV117: original native files must remain until Autodesk is
        // finished with every deferred open/history reference. We cannot
        // establish that point reliably from a copied DWG's file locks.
        // Never schedule any source-file deletion.
        // REV107: worker-owned ledger survives AutoCAD MDI document
        // changes. On completion replay every verified custom result,
        // even if some real-time UI notifications were missed.
        var completedOutputs = new List<CustomCopyOutput>();
        var processedFiles = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var observed = new Dictionary<string,
            (long Size, long Modified, DateTime SeenAt)>(
            StringComparer.OrdinalIgnoreCase);
        DateTime start = DateTime.UtcNow;
        DateTime lastProcessed = start;
        // REV58: the 25-second idle shutdown could happen during a long
        // Autodesk ISO batch, leaving the final route (e.g. 01E) uncopied.
        // Track actual per-line completion, not an arbitrary gap between
        // native DWG writes. Only successful copied or intentionally skipped
        // files count as finished.
        var expectedTags = new HashSet<string>(
            selectedTags.Where(tag => !string.IsNullOrWhiteSpace(tag)),
            StringComparer.OrdinalIgnoreCase);
        var completedTags = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        void MarkRouteFinished(string dwg)
        {
            if (expectedTags.Count == 0) return;
            string stem = Path.GetFileNameWithoutExtension(dwg);
            // Exact match outranks a possible native split-sheet suffix;
            // this prevents a tag such as A-011-1 being mistaken for A-011.
            string? exact = expectedTags.FirstOrDefault(tag =>
                stem.Equals(tag, StringComparison.OrdinalIgnoreCase));
            string? matched = exact ?? expectedTags
                .Where(tag => MatchesCurrentIsoBatch(
                    dwg, new[] { tag }))
                .OrderByDescending(tag => tag.Length)
                .FirstOrDefault();
            if (matched != null) completedTags.Add(matched);
        }

        // REV121: check every matching, newly generated native DWG rather
        // than relying on a single completed sheet per route. Read source
        // file names only; never delete native or custom content.
        string[] PendingCurrentNativeDWGs()
        {
            if (!Directory.Exists(source))
                return new[] { "(native output folder unavailable)" };
            return Directory.EnumerateFiles(
                    source, "*.dwg", SearchOption.TopDirectoryOnly)
                .Where(file => !processedFiles.Contains(file) &&
                    MatchesCurrentIsoBatch(
                        Path.GetFileName(file), selectedTags))
                .Where(file =>
                {
                    try
                    {
                        var info = new FileInfo(file);
                        return info.Exists &&
                            info.LastWriteTimeUtc >=
                                sinceUtc.AddSeconds(-10);
                    }
                    catch (IOException) { return true; }
                    catch (UnauthorizedAccessException) { return true; }
                })
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        long lastNativeChangeTicks = start.Ticks;
        // REV104: cap SOURCE directory enumeration to one sweep per
        // three seconds even if Autodesk emits dozens of FileSystemWatcher
        // notifications while saving a DWG. Continuous SMB enumerations
        // compete with 3D model editing; the async copy remains off the
        // AutoCAD UI thread and verified copies are still surfaced.
        DateTime nextSourceSweepUtc = DateTime.MinValue;

        using var wake = new System.Threading.SemaphoreSlim(0, 1);
        void Signal()
        {
            System.Threading.Interlocked.Exchange(
                ref lastNativeChangeTicks, DateTime.UtcNow.Ticks);
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
                    watcher?.Dispose();
                    watcher = null;
                }
            }

            // Preserve REV33's maximum monitoring horizon. We can observe
            // several ISO sheets created asynchronously by Autodesk.
            while ((DateTime.UtcNow - start).TotalMinutes < 10)
            {
                TimeSpan untilSweep = nextSourceSweepUtc - DateTime.UtcNow;
                if (untilSweep > TimeSpan.Zero)
                    await Task.Delay(untilSweep).ConfigureAwait(false);
                nextSourceSweepUtc = DateTime.UtcNow.AddSeconds(3);
                try
                {
                    if (!Directory.Exists(source))
                        throw new DirectoryNotFoundException(
                            "Native Production Iso output folder is unavailable: " +
                            source);

                    foreach (string file in Directory.EnumerateFiles(
                        source, "*.dwg", SearchOption.TopDirectoryOnly))
                    {
                        if (processedFiles.Contains(file) ||
                            !MatchesCurrentIsoBatch(Path.GetFileName(file),
                                selectedTags))
                            continue;

                        string? temp = null;
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.LastWriteTimeUtc < sinceUtc.AddSeconds(-10))
                                continue;

                            var version =
                                (Size: info.Length,
                                 Modified: info.LastWriteTimeUtc.Ticks);
                            if (version.Size <= 0)
                                continue;

                            DateTime now = DateTime.UtcNow;
                            if (!observed.TryGetValue(file, out var first) ||
                                first.Size != version.Size ||
                                first.Modified != version.Modified)
                            {
                                observed[file] = (
                                    version.Size, version.Modified, now);
                                System.Threading.Interlocked.Exchange(
                                    ref lastNativeChangeTicks, now.Ticks);
                                continue;
                            }

                            // Avoid accessing source DWGs while Plant 3D may
                            // still be finishing native save/reopen passes.
                            if ((now - first.SeenAt).TotalSeconds < 4 ||
                                (now - new DateTime(
                                    System.Threading.Interlocked.Read(
                                        ref lastNativeChangeTicks),
                                    DateTimeKind.Utc)).TotalSeconds < 5)
                                continue;

                            Directory.CreateDirectory(targetRoot);

                            // REV52: Source "A-08 (2).dwg" maps to CUSTOM
                            // "A-08.dwg", rather than copying the native
                            // suffix. The two folders must be independent.
                            string customName = CustomDestinationName(
                                Path.GetFileName(file), out bool sourceNumbered);
                            string destination = Path.Combine(
                                targetRoot, customName);

                            // User-requested special case: when Autodesk
                            // numbered the ORIGINAL native output, a CLOSED
                            // existing CUSTOM base DWG is replaced even if
                            // the native checkbox was not selected. When the
                            // custom DWG is OPEN/locked, REV51 still emits
                            // " (1)", " (2)", ... without overwriting it.
                            // Unnumbered native sources retain REV51/REV47
                            // checkbox and skip semantics.
                            bool allowCustomOverwrite =
                                overwriteExisting || sourceNumbered;
                            if (File.Exists(destination) && !allowCustomOverwrite &&
                                !IsCustomDwgOpenOrLocked(destination))
                            {
                                // Preserve REV47's skip-on-collision behavior
                                // when existing DWG is NOT open/locked.
                                // REV51 creates a numbered copy only if the
                                // requested existing file is in use.
                                processedFiles.Add(file);
                                skipped++;
                                lastProcessed = now;
                                MarkRouteFinished(Path.GetFileName(file));
                                // REV57: a safe skipped output still exists
                                // in the custom folder. Report it as an
                                // EXISTING clickable result, not a new copy.
                                completedOutputs.Add(
                                    new CustomCopyOutput(destination, true));
                                _copyProgress.Enqueue(
                                    "existing:" + Path.GetFileName(destination));
                                continue;
                            }

                            // NO exclusive source lock: the previous
                            // FileShare.None handle could block Autodesk
                            // while it was generating/saving the DWG.
                            using var input = new FileStream(
                                file, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete,
                                128 * 1024, FileOptions.SequentialScan);
                            if (input.Length != version.Size)
                                continue;

                            temp = destination + ".sptc-part-" +
                                   Guid.NewGuid().ToString("N");
                            await using (var output = new FileStream(
                                temp, FileMode.CreateNew, FileAccess.Write,
                                FileShare.None, 128 * 1024,
                                FileOptions.SequentialScan))
                            {
                                await input.CopyToAsync(output, 128 * 1024)
                                    .ConfigureAwait(false);
                                await output.FlushAsync().ConfigureAwait(false);
                            }

                            // REV113: compare source and staged DWG bytes
                            // before final publication or source deletion.
                            input.Position = 0;
                            byte[] sourceHash = SHA256.HashData(input);
                            using (var staged = new FileStream(
                                temp, FileMode.Open, FileAccess.Read,
                                FileShare.Read))
                            {
                                if (staged.Length != version.Size ||
                                    !CryptographicOperations.FixedTimeEquals(
                                        sourceHash, SHA256.HashData(staged)))
                                    throw new IOException(
                                        "Custom ISO staging checksum mismatch.");
                            }

                            // Reject a snapshot if Autodesk edited the DWG
                            // while we were reading it. Never publish a
                            // potentially incomplete or mid-write copy.
                            var after = new FileInfo(file);
                            if (after.Length != version.Size ||
                                after.LastWriteTimeUtc.Ticks != version.Modified ||
                                input.Length != version.Size)
                            {
                                observed.Remove(file);
                                System.Threading.Interlocked.Exchange(
                                    ref lastNativeChangeTicks,
                                    DateTime.UtcNow.Ticks);
                                continue;
                            }

                            // The fully copied .sptc-part file lives in the
                            // same destination folder. Publish only after
                            // verifying source stability. The explicit native
                            // Overwrite option permits replacing the CUSTOM
                            // file, never an original project/native ISO DWG.
                            string published = PublishCustomSnapshot(
                                temp, destination, allowCustomOverwrite);
                            temp = null;

                            // Verify the published pathname, including
                            // numbered outputs when the base DWG was locked.
                            using (var publishedRead = new FileStream(
                                published, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete))
                            {
                                if (publishedRead.Length != version.Size ||
                                    !CryptographicOperations.FixedTimeEquals(
                                        sourceHash, SHA256.HashData(publishedRead)))
                                    throw new IOException(
                                        "Published custom ISO checksum mismatch.");
                            }
                            // REV117: preserve this exact native source,
                            // because Autodesk may open it again later from
                            // the Results dialog or deferred completion UI.
                            // A verified custom copy is still delivered.
                            processedFiles.Add(file);
                            copied++;
                            lastProcessed = DateTime.UtcNow;
                            MarkRouteFinished(Path.GetFileName(file));
                            // REV51: report the ACTUAL numbered output so
                            // its clickable file link appears in Results.
                            completedOutputs.Add(
                                new CustomCopyOutput(published, false));
                            _copyProgress.Enqueue(Path.GetFileName(published));
                        }
                        catch (IOException)
                        {
                            // Source DWG may still be writing, or a custom
                            // target path may be unavailable for reasons other
                            // than a normal sharing lock. Retry without
                            // altering original native DWGs.
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Retry a locked/network project file later.
                        }
                        finally
                        {
                            if (temp is not null)
                            {
                                try { if (File.Exists(temp)) File.Delete(temp); }
                                catch (IOException) { }
                                catch (UnauthorizedAccessException) { }
                            }
                        }
                    }
                }
                catch (IOException)
                {
                    if ((DateTime.UtcNow - start).TotalMinutes > 2 &&
                        copied == 0 && skipped == 0)
                        throw;
                }
                catch (UnauthorizedAccessException) { throw; }

                // REV58: Never stop while a selected line is still missing.
                // Autodesk can have gaps of 25+ seconds between ISO sheets.
                // Once ALL expected native line groups are processed, allow
                // a 12-second quiet window for late split sheets. If the
                // native selection snapshot was unavailable, conservatively
                // keep watching longer rather than dropping the last DWG.
                bool allKnownTagsFinished = expectedTags.Count > 0 &&
                    expectedTags.All(completedTags.Contains);
                double idleSeconds = (DateTime.UtcNow - lastProcessed)
                    .TotalSeconds;
                // REV121: the early native Autodesk success balloon is
                // not a custom-copy readiness signal. Require the entire
                // native DWG directory to settle for a bounded 20 seconds
                // and inspect every recent source sheet before announcing
                // a verified custom batch. Never change native notifications.
                double sourceQuietSeconds = (DateTime.UtcNow -
                    new DateTime(System.Threading.Interlocked.Read(
                        ref lastNativeChangeTicks),
                        DateTimeKind.Utc)).TotalSeconds;
                bool batchQuiet = (allKnownTagsFinished &&
                        idleSeconds >= 20 && sourceQuietSeconds >= 20) ||
                    (expectedTags.Count == 0 &&
                     (copied > 0 || skipped > 0) && idleSeconds >= 120 &&
                     sourceQuietSeconds >= 20);
                if (batchQuiet)
                {
                    try
                    {
                        if (PendingCurrentNativeDWGs().Length == 0 &&
                            completedOutputs.All(item =>
                                File.Exists(item.Path)))
                            break;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                // REV124: the Results window reports that the Autodesk
                // generation job ended. If a selected route DID NOT
                // generate its new DWGs, waiting the whole ten-minute
                // horizon cannot fix the missing native source. After
                // 45 seconds of both source and destination silence,
                // release the monitor and report an INCOMPLETE batch.
                // This is explicitly NOT a successful ISO completion.
                if (_resultsSeen && sourceQuietSeconds >= 45 &&
                    idleSeconds >= 45)
                {
                    SafeLog("\nSPTC REV124: Autodesk Results observed, " +
                        "but current native DWGs are missing or incomplete; " +
                        "stopping custom watcher after settled source. " +
                        "Original source files preserved.");
                    break;
                }

                // REV104: FileSystemWatcher is a wake hint, never a
                // request to hammer the SMB source once per notification.
                // The sweep-rate gate above enforces a 3-second minimum;
                // no native ISO job/copy operation is run on AutoCAD's UI.
                await wake.WaitAsync(TimeSpan.FromMilliseconds(1600))
                    .ConfigureAwait(false);
            }
        }
        finally { watcher?.Dispose(); }

        string[] missingRoutes = expectedTags
            .Where(tag => !completedTags.Contains(tag))
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] pendingNative;
        try { pendingNative = PendingCurrentNativeDWGs(); }
        catch (Exception ex) when (ex is IOException ||
                                   ex is UnauthorizedAccessException)
        {
            pendingNative = new[] { "(native source scan unavailable)" };
        }

        // REV121: "Ready" must mean every selected route has a custom
        // result, every relevant source DWG was handled, no old custom
        // DWG was substituted for a newly generated one, all output paths
        // exist, and Autodesk's source directory remained quiet.
        bool sourceSettled = (DateTime.UtcNow -
            new DateTime(System.Threading.Interlocked.Read(
                ref lastNativeChangeTicks),
                DateTimeKind.Utc)).TotalSeconds >= 20;
        bool verifiedComplete = expectedTags.Count > 0 &&
            missingRoutes.Length == 0 &&
            pendingNative.Length == 0 &&
            skipped == 0 && copied > 0 &&
            sourceSettled &&
            completedOutputs.All(item => File.Exists(item.Path));

        return new CopyReport(
            copied, skipped, 0, copied + skipped,
            targetRoot, completedOutputs.ToArray(),
            verifiedComplete, missingRoutes, pendingNative);
    }

    private static void EnsureMonitor()
    {
        // REV104: after Create, only background-copy progress/cleanup
        // needs this AutoCAD UI timer; native selection monitors have
        // already been detached. Give modeling input priority over
        // frequent Win32 window enumeration and Editor.WriteMessage.
        if (_watch != null)
        {
            _watch.Interval = 3500;
            return;
        }
        _watch = new System.Windows.Forms.Timer { Interval = 3500 };
        _watch.Tick += OnTick;
        _watch.Start();
    }

    public static void Status(Document doc)
    {
        doc.Editor.WriteMessage(
            "\nSPTC ISO STATUS" +
            "\nNative output: " + (_source ?? "(not detected)") +
            "\nCustom folder: " + (string.IsNullOrEmpty(_destination) ? "(empty)" : _destination) +
            "\nCustom transfer state: " + _copyState +
            "\nResults dialog observed: " + _resultsSeen +
            "\nREV125 native completion notice: " +
                EventClock(_nativePopupObservedAt) +
                ", window class=" + _nativePopupWindowType +
            "\nFirst native Results dialog: " +
                EventClock(_resultsFirstObservedAt) +
            "\nFirst new custom DWG: " +
                EventClock(_customFirstCopiedAt) +
            "\nFull custom verification: " +
                EventClock(_customBatchVerifiedAt) +
            (_lastCopyReport is null ? "" :
                $"\nLast custom transfer: {_lastCopyReport.Copied} copied, " +
                $"{_lastCopyReport.Skipped} existing/skipped, " +
                $"verified complete={_lastCopyReport.AllCustomFilesVerified}, " +
                $"missing routes={string.Join(", ", _lastCopyReport.MissingRoutes)}, " +
                $"pending native DWGs={string.Join(", ", _lastCopyReport.UnprocessedNativeFiles)}; " +
                "originals retained"));
    }

    // Preserve the public command entry point but never rely on its
    // source Document after Create. The modeling DWG may be closed.
    public static void CopyCompleted(Document doc) => CopyCompletedCore();

    private static void CopyCompletedCore()
    {
        if (_copyTask is { IsCompleted: false })
        {
            SafeLog("\nSPTC ISO: Background export is already running.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_destination))
        {
            _copyState = "Custom location not specified";
            SafeLog("\nSPTC ISO: " + _copyState);
            return;
        }
        if (string.IsNullOrWhiteSpace(_source))
        {
            _copyState = "Native source not detected";
            SafeLog("\nSPTC ISO: " + _copyState +
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
            // REV48: this light UI-only watcher attaches clickable custom
            // links within Autodesk's existing Results dialog. It remains
            // active after the REV47 copy task completes.
            NativeIsoResultsOverlay.BeginSession(destination);
            bool allowOverwrite = _overwriteFound && _overwriteRequested;
            SafeLog(
                "\nSPTC REV50: Custom overwrite=" +
                (allowOverwrite ? "YES" : "NO") +
                " (source: " + _overwriteSource + ").");
            string[] selectedTags = _selectedIsoTags.ToArray();
            NativeIsoResultsOverlay.SetExpectedIsoTags(selectedTags);
            _copyTask = Task.Run(() => CopyFilesAsync(
                source, destination, _launchUtc, allowOverwrite,
                selectedTags));
            EnsureMonitor();
            SafeLog(
                "\nSPTC REV117: Monitoring verified custom DWG transfer; native ISO originals retained for Autodesk Results." +
                " Custom destination overwrite=" + (_overwriteRequested ? "YES" : "NO") + ".");
        }
        catch (Exception ex)
        {
            _copyState = "Error: " + ex.Message;
            SafeLog("\nSPTC ISO export: " + _copyState);
        }
    }
}
