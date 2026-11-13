using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
    private static bool _resultsSeen;
    private static DateTime _nativeClosedAt;
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
            _document?.Editor.WriteMessage(
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

    private static void ShowAutoCreateFailure(string problem)
    {
        _document?.Editor.WriteMessage(
            "\nSPTC REV79: Batch Create not started: " + problem);
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
                if (!_autoCreatePending || _autoCreateSerial != serial ||
                    _native != dialog || !IsWindow(dialog))
                    return;

                try
                {
                    NativeIsoLineSelectOverlay.BeginScopeSwitch(dialog);
                    ProjectFolderLinePicker.PrepareForNativeModeChange();
                    NativeIsoExactLineFilter.End(restore: true);
                    if (!ActiveDwgTags.ClearOneTag(dialog, _document))
                    {
                        StopAutoCreate();
                        NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                        ShowAutoCreateFailure(
                            "Autodesk's native line filter could not be cleared.");
                        return;
                    }

                    bool refreshed =
                        NativeIsoToolbarClickFilter.TryRefreshNativeProjectList(
                            dialog);
                    NativeIsoToolbarClickFilter.MarkAllLinesRestored(dialog);
                    _document?.Editor.WriteMessage(
                        "\nSPTC REV79: Create clicked with " +
                        requested.Length + " saved route(s); restoring " +
                        "All Project native selection (refresh=" +
                        refreshed + ").");
                }
                catch (Exception ex)
                {
                    StopAutoCreate();
                    NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                    ShowAutoCreateFailure(ex.GetBaseException().Message);
                    return;
                }

                int polls = 0;
                int stableSamples = 0;
                int matched = 0;
                int checkedCount = 0;
                string lastNativeSignature = "";
                var timer = new System.Windows.Forms.Timer { Interval = 200 };
                _autoCreateTimer = timer;

                timer.Tick += (_, _) =>
                {
                    if (_autoCreateSerial != serial || !_autoCreatePending ||
                        _native != dialog || !IsWindow(dialog) ||
                        _nativeCreateButton is not { IsDisposed: false } create)
                    {
                        StopAutoCreate();
                        return;
                    }
                    if (++polls > 35) // bounded: no background looping
                    {
                        string issue = "Only " + matched + " of " +
                            requested.Length + " selected route(s) appeared " +
                            "in the original Autodesk list, or native " +
                            "Create remained disabled.";
                        StopAutoCreate();
                        NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                        ShowAutoCreateFailure(issue);
                        return;
                    }

                    try
                    {
                        // If the user changed their saved selection during
                        // the native list rebuild, do not submit a stale
                        // batch. The Create click applies ONLY to the exact
                        // user selection captured when they pressed it.
                        string[] current = NativeIsoLineSelectOverlay
                            .CaptureSelectedTags(dialog);
                        if (current.Length != requested.Length ||
                            !new HashSet<string>(
                                current, StringComparer.OrdinalIgnoreCase)
                                .SetEquals(requested))
                        {
                            StopAutoCreate();
                            NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                            ShowAutoCreateFailure(
                                "Selected line numbers changed during Create.");
                            return;
                        }

                        // Wait for the Autodesk list names to remain stable,
                        // instead of clicking Create while a native
                        // Filter.TextChanged refresh is still in progress.
                        string nativeSignature = NativeIsoLineSelectOverlay
                            .NativeRouteNameSignature(dialog);
                        if (string.IsNullOrEmpty(nativeSignature))
                            return;
                        if (!nativeSignature.Equals(lastNativeSignature,
                                StringComparison.Ordinal))
                        {
                            lastNativeSignature = nativeSignature;
                            stableSamples = 0;
                            return;
                        }
                        if (++stableSamples < 2)
                            return;

                        bool exact = NativeIsoLineSelectOverlay
                            .TryApplySavedBatchToNative(dialog, requested,
                                out matched, out checkedCount);
                        if (!exact || checkedCount != requested.Length ||
                            !create.Enabled)
                            return;

                        // Second native UI turn proves the full checked set
                        // remained stable after Autodesk selection handlers.
                        if (++stableSamples < 4)
                            return;
                        string[] verified = NativeCheckedRouteNames(dialog);
                        if (verified.Length != requested.Length ||
                            !new HashSet<string>(
                                verified, StringComparer.OrdinalIgnoreCase)
                                .SetEquals(requested))
                            return;

                        // End the guard: PerformClick calls the ORIGINAL
                        // Autodesk Create handler with its real checked
                        // rows now containing ALL selected route names.
                        try { timer.Stop(); timer.Dispose(); } catch (Exception) { }
                        _autoCreateTimer = null;
                        _autoCreatePending = false;
                        _autoCreateSubmitting = true;
                        NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                        _document?.Editor.WriteMessage(
                            "\nSPTC REV79: verified full native Create batch " +
                            "(" + verified.Length + " selected route(s)); " +
                            "submitting original Autodesk Create.");
                        try { create.PerformClick(); }
                        finally { _autoCreateSubmitting = false; }
                    }
                    catch (Exception ex)
                    {
                        StopAutoCreate();
                        NativeIsoLineSelectOverlay.CancelScopeSwitch(dialog);
                        ShowAutoCreateFailure(
                            "Native Create validation failed: " +
                            ex.GetBaseException().Message);
                    }
                };
                timer.Start();
            }));
        }
        catch (Exception ex)
        {
            StopAutoCreate();
            ShowAutoCreateFailure(ex.GetBaseException().Message);
        }
    }

    private static void ShowCreateSelectionWarning(string warning)
    {
        _document?.Editor.WriteMessage(
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
                return false;

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
        _document?.Editor.WriteMessage(
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
        // The ordinary Create button is intercepted before its native
        // handler whenever a filtered view hides saved tags. An automatic
        // invocation is permitted ONLY after all requested names have been
        // verified in Autodesk's original native checked list.
        if (_native != IntPtr.Zero &&
            TryGetCreateSelectionMismatch(_native, out _))
        {
            _document?.Editor.WriteMessage(
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
        _document?.Editor.WriteMessage(
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
        StopAutoCreate();
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

    private sealed record CopyReport(int Copied, int Skipped, string Destination);

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
        _createRequested = false;
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
                // REV57: include an existing destination skipped by the
                // safe no-overwrite policy, without claiming it was copied.
                bool existing = savedName.StartsWith(
                    "existing:", StringComparison.OrdinalIgnoreCase);
                string actualName = existing
                    ? savedName.Substring("existing:".Length)
                    : savedName;
                string resultPath = Path.Combine(_destination, actualName);
                NativeIsoResultsOverlay.ReportCopied(resultPath, existing);
                _copyState = (existing ? "Existing " : "Copied ") +
                    actualName + "; monitoring remaining ISOs";
                _document?.Editor.WriteMessage(
                    "\nSPTC REV57: Custom ISO " +
                    (existing ? "already exists: " : "copied: ") + resultPath);
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
                    StartOverwriteTracker(native);
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

                        // REV65: snapshot CHECKED tag names, never item
                        // indices, before the native toolbar changes mode.
                        // Autodesk can replace its original checklist rows.
                        string[] selectedBefore =
                            NativeIsoLineSelectOverlay.CaptureSelectedTags(native);
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
                            // REV74: Autodesk rebuilds the ORIGINAL list
                            // several times when switching Current -> All.
                            // Hold its native checkbox redraw while the
                            // project data and exact tag checks reconcile,
                            // so the temporary unchecked frame isn't shown.
                            NativeIsoLineSelectOverlay.BeginProjectRedrawBatch(native);
                            bool recoveryScheduled = false;
                            try
                            {
                                // Restore original native row objects before
                                // Autodesk's Filter and Project list rebuild.
                                NativeIsoExactLineFilter.End(restore: true);
                                bool cleared = ActiveDwgTags.ClearOneTag(
                                    native, _document);
                                if (cleared)
                                {
                                    bool refreshed =
                                        NativeIsoToolbarClickFilter.TryRefreshNativeProjectList(native);
                                    _document.Editor.WriteMessage(
                                        "\nSPTC REV74: All Project native refresh=" +
                                        refreshed + "; restoring selected route tags.");
                                    NativeIsoLineSelectOverlay.UpdateDisplayedCount();
                                    NativeIsoLineSelectOverlay.ScheduleModeChangeRecovery(
                                        native, selectedBefore);
                                    recoveryScheduled = true;
                                }
                                else
                                    NativeIsoLineSelectOverlay.CancelScopeSwitch(native);
                                return cleared;
                            }
                            finally
                            {
                                // Failed/missing native list must NEVER stay
                                // frozen. Successful recovery releases it on
                                // completion (or bounded timeout / Cancel).
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
                            _document.Editor.WriteMessage(
                                "\nSPTC REV71: first active DWG tag scan " +
                                scanWatch.ElapsedMilliseconds + " ms.");
                        }

                        if (ActiveDwgTags.Count != 1)
                        {
                            _document.Editor.WriteMessage(
                                "\nSPTC REV30: Native active filtering needs exactly " +
                                "one verified physical line tag. Found " +
                                ActiveDwgTags.Count + ". Full list was left unchanged.");
                            NativeIsoLineSelectOverlay.CancelScopeSwitch(native);
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
                                "\nSPTC REV71: Exact match requested for " +
                                ActiveDwgTags.CurrentTags[0] +
                                "; waiting for native exact-filter validation.");
                            // REV71: do NOT schedule 350ms mode-recovery
                            // timer for Active DWG. NativeIsoExactLineFilter
                            // notifies the selection overlay the moment the
                            // single original Autodesk row is validated.
                            // This eliminates the additional three-poll
                            // recovery delay and competing checkbox writes.
                        }
                        else
                            NativeIsoLineSelectOverlay.CancelScopeSwitch(native);
                        return applied;
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
                            _document?.Editor.WriteMessage(
                                "\nSPTC REV78: restored last custom location " +
                                "for this ISO output source.");
                        }
                        else if (_destinationEditedThisDialog)
                        {
                            _rememberedSource = _source;
                            _rememberedDestination = _destination;
                        }
                        _document?.Editor.WriteMessage(
                            "\nSPTC ISO detected native source: " + _source);
                    }
                }
                return;
            }

            if (_native != IntPtr.Zero)
            {
                DetachNativeOverwriteTracker();
                RemoveOverlay();

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
                    _document?.Editor.WriteMessage(
                        "\nSPTC REV64: Production Iso canceled; " +
                        "all plugin monitors stopped. No DWG copy started.");
                    return;
                }

                _nativeClosedAt = DateTime.UtcNow;
                _copyState = "Create requested; watching for completed DWGs";
            }

            // Autodesk may display a notification first and open this dialog
            // only after the user clicks the notification. Do not require that.
            IntPtr results = FindDialog("Isometric Creation Results");
            if (results != IntPtr.Zero)
            {
                _resultsSeen = true;
                if (!string.IsNullOrWhiteSpace(_destination))
                    NativeIsoResultsOverlay.BeginSession(_destination);
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
    private static async Task<CopyReport> CopyFilesAsync(
        string source, string targetRoot, DateTime sinceUtc,
        bool overwriteExisting, IReadOnlyCollection<string> selectedTags)
    {
        int copied = 0, skipped = 0;
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
        long lastNativeChangeTicks = start.Ticks;

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
                            processedFiles.Add(file);
                            copied++;
                            lastProcessed = DateTime.UtcNow;
                            MarkRouteFinished(Path.GetFileName(file));
                            // REV51: report the ACTUAL numbered output so
                            // its clickable file link appears in Results.
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
                if ((allKnownTagsFinished && idleSeconds >= 12) ||
                    (expectedTags.Count == 0 &&
                     (copied > 0 || skipped > 0) && idleSeconds >= 120))
                    break;

                // FileSystemWatcher is a hint, not a guarantee. SMB drives
                // are supported with a low-cost periodic fallback.
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
            // REV48: this light UI-only watcher attaches clickable custom
            // links within Autodesk's existing Results dialog. It remains
            // active after the REV47 copy task completes.
            NativeIsoResultsOverlay.BeginSession(destination);
            bool allowOverwrite = _overwriteFound && _overwriteRequested;
            _document?.Editor.WriteMessage(
                "\nSPTC REV50: Custom overwrite=" +
                (allowOverwrite ? "YES" : "NO") +
                " (source: " + _overwriteSource + ").");
            string[] selectedTags = _selectedIsoTags.ToArray();
            NativeIsoResultsOverlay.SetExpectedIsoTags(selectedTags);
            _copyTask = Task.Run(() => CopyFilesAsync(
                source, destination, _launchUtc, allowOverwrite,
                selectedTags));
            EnsureMonitor();
            doc.Editor.WriteMessage(
                "\nSPTC ISO: Monitoring completed DWGs for custom copy (background)." +
                " Custom destination overwrite=" + (_overwriteRequested ? "YES" : "NO") + ".");
        }
        catch (Exception ex)
        {
            _copyState = "Error: " + ex.Message;
            doc.Editor.WriteMessage("\nSPTC ISO export: " + _copyState);
        }
    }
}
