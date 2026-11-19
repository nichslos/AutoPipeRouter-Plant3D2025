using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SPTC.ProductionIso;

/// <summary>
/// REV126: Original top-left Autodesk scope buttons are three DISTINCT
/// actions. Current Drawing (#1), All Project (#2), and Selected Lines (#3)
/// must never be treated as a binary toggle. The native Filter dropdown
/// (#4) and funnel/search button (#5) are outside this click interception.
/// Detect actual live icon bounds, not hardcoded route names/positions.
/// </summary>
internal sealed class NativeIsoToolbarClickFilter : IMessageFilter
{
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_CANCELMODE = 0x001F;
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private readonly IntPtr _dialog;
    private readonly Func<int, bool> _selectScope;
    private int _currentMode = 2;
    private int _pressedMode;
    private bool _disposed;
    private bool _busy;
    private static NativeIsoToolbarClickFilter? _active;

    private NativeIsoToolbarClickFilter(IntPtr dialog, Func<int, bool> selectScope)
    {
        _dialog = dialog;
        _selectScope = selectScope;
        System.Windows.Forms.Application.AddMessageFilter(this);
    }

    public static void Attach(IntPtr dialog, Func<int, bool> selectScope)
    {
        if (dialog == IntPtr.Zero) return;
        if (_active is { _disposed: false } previous && previous._dialog == dialog)
            return;
        Detach();
        _active = new NativeIsoToolbarClickFilter(dialog, selectScope);
    }

    public static void MarkAllLinesRestored(IntPtr dialog)
    {
        if (_active is { _disposed: false } current && current._dialog == dialog)
            current._currentMode = 2;
    }

    public static void MarkActiveLinesDisplayed(IntPtr dialog)
    {
        // REV101: on a guarded Show All repair timeout, the Active
        // projection remains visible. Next toolbar click must be Show All.
        if (_active is { _disposed: false } current && current._dialog == dialog)
            current._currentMode = 1;
    }

    // REV38: The old SPTC top-toolbar mouse filter deliberately intercepts
    // Drawing/Project/Filter clicks and turns all three into Active/All.
    // After native exact filtering, the original Autodesk checklist may
    // remain scoped to only the last matching two rows. The folder picker
    // cannot show 183 verified tags if it can see only those two rows.
    //
    // Restore Autodesk's actual PROJECT-wide source by invoking its existing
    // SECOND (Project) toolbar action directly, not by sending synthetic
    // mouse messages. We verify the toolbar's geometry before invocation.
    // Never fabricate route items or force-enable Create.
    public static bool TryRefreshNativeProjectList(IntPtr dialog)
    {
        if (dialog == IntPtr.Zero) return false;
        IntPtr group = IntPtr.Zero;
        RECT groupRect = default;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is GroupBox g &&
                g.Text.Trim().Equals("Display lines",
                    StringComparison.OrdinalIgnoreCase) &&
                GetWindowRect(hwnd, out RECT rect))
            {
                group = hwnd;
                groupRect = rect;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (group == IntPtr.Zero) return false;

        // Prefer native ToolStrip: its original Click handler can repopulate
        // the project-wide checklist without our mouse-message filter.
        ToolStrip? nativeStrip = null;
        var buttons = new List<(Button Control, int X)>();
        EnumChildWindows(group, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT rect)) return true;
            int cy = (rect.Top + rect.Bottom) / 2;
            int cx = (rect.Left + rect.Right) / 2;
            if (cy < groupRect.Top + 8 || cy > groupRect.Top + 52 ||
                cx < groupRect.Left + 50 || cx > groupRect.Right - 4)
                return true;

            Control? c = Control.FromHandle(hwnd);
            if (c is ToolStrip strip &&
                strip.Items.OfType<ToolStripItem>().Count(i => i.Available) >= 3)
            {
                nativeStrip = strip;
                return false;
            }
            if (c is Button button && button.Visible && button.Enabled &&
                rect.Width >= 16 && rect.Width <= 45 &&
                rect.Height >= 16 && rect.Height <= 43)
                buttons.Add((button, cx));
            return true;
        }, IntPtr.Zero);

        try
        {
            if (nativeStrip is { IsDisposed: false } strip)
            {
                var icons = strip.Items.OfType<ToolStripItem>()
                    .Where(i => i.Available && i.Enabled &&
                                i is ToolStripButton).ToList();
                if (icons.Count >= 3)
                {
                    icons[1].PerformClick();
                    MarkAllLinesRestored(dialog);
                    return true;
                }
            }

            // Some versions expose three ordinary WinForms icon Buttons.
            // Only use a geometrically coherent row of exactly three.
            var ordered = buttons.OrderBy(x => x.X).ToList();
            if (ordered.Count == 3 &&
                ordered[2].X - ordered[0].X < 125)
            {
                ordered[1].Control.PerformClick();
                MarkAllLinesRestored(dialog);
                return true;
            }
        }
        catch (Exception)
        {
            // Leave the original line list as-is on unsupported dialogs.
            // The caller will validate coverage before offering an ISO.
        }
        return false;
    }

    public bool PreFilterMessage(ref Message message)
    {
        if (_disposed) return false;
        int msg = message.Msg;
        if (msg == WM_CANCELMODE)
        {
            _pressedMode = 0;
            return false;
        }
        if (msg != WM_LBUTTONDOWN && msg != WM_LBUTTONUP &&
            msg != WM_LBUTTONDBLCLK)
            return false;

        Point position = Control.MousePosition;
        int mode = TopToolbarButtonAt(position.X, position.Y);

        if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONDBLCLK)
        {
            if (mode == 0) return false;
            _pressedMode = mode;
            return true;
        }

        int pressed = _pressedMode;
        _pressedMode = 0;
        if (pressed == 0) return false;
        if (mode != pressed) return true; // release outside selected icon

        if (_busy) return true;
        _busy = true;
        try
        {
            // Explicit requested mode, NOT !_showingActive.
            // Clicking #2 twice must remain All, not switch to Active.
            if (_selectScope(pressed))
                _currentMode = pressed;
        }
        catch (Exception) { /* preserve Autodesk dialog */ }
        finally { _busy = false; }
        return true;
    }

    // Enumerate the ACTUAL three native controls inside Display lines.
    // The filter dropdown (#4) and funnel (#5) are on the lower row,
    // and are never captured by this handler.
    private int TopToolbarButtonAt(int screenX, int screenY)
    {
        if (!GetWindowRect(_dialog, out RECT dialog) ||
            screenX < dialog.Left || screenX >= dialog.Right ||
            screenY < dialog.Top || screenY >= dialog.Bottom)
            return 0;

        IntPtr group = IntPtr.Zero;
        EnumChildWindows(_dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is GroupBox g &&
                g.Text.Trim().Equals("Display lines",
                    StringComparison.OrdinalIgnoreCase))
            {
                group = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (group == IntPtr.Zero || !GetWindowRect(group, out RECT box))
            return 0;

        // Restrict to the native top-toolbar strip, not the search row.
        if (screenY < box.Top + 7 || screenY >= box.Top + 55 ||
            screenX < box.Left || screenX >= box.Right)
            return 0;

        var nativeButtons = new List<(RECT Bounds, int MiddleX, int MiddleY)>();
        int stripMode = 0;
        EnumChildWindows(group, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT r)) return true;
            int centerY = (r.Top + r.Bottom) / 2;
            if (centerY < box.Top + 8 || centerY >= box.Top + 54)
                return true;

            Control? c = Control.FromHandle(hwnd);
            if (c is ToolStrip strip && strip.Visible &&
                !strip.IsDisposed && r.Width >= 54)
            {
                var buttons = strip.Items.OfType<ToolStripButton>()
                    .Where(item => item.Available && item.Enabled)
                    .OrderBy(item => item.Bounds.Left).ToArray();
                if (buttons.Length >= 3)
                {
                    Point local = strip.PointToClient(
                        new Point(screenX, screenY));
                    ToolStripItem? hit = strip.GetItemAt(local);
                    for (int index = 0; index < 3; index++)
                        if (ReferenceEquals(hit, buttons[index]))
                        {
                            stripMode = index + 1;
                            return false;
                        }
                }
            }

            if (c is ButtonBase button && button.Visible &&
                button.Enabled && r.Width >= 16 && r.Width <= 45 &&
                r.Height >= 16 && r.Height <= 44)
                nativeButtons.Add((r,
                    (r.Left + r.Right) / 2, centerY));
            return true;
        }, IntPtr.Zero);
        if (stripMode != 0) return stripMode;

        // Separate WinForms buttons: select the coherent row of 3 by
        // their live HWND geometry rather than assuming a fixed X offset.
        var row = nativeButtons.OrderBy(b => b.MiddleX).ToArray();
        if (row.Length != 3 ||
            row[2].MiddleX - row[0].MiddleX > 135 ||
            row.Max(b => b.MiddleY) - row.Min(b => b.MiddleY) > 12)
            return 0;
        for (int i = 0; i < 3; i++)
        {
            RECT r = row[i].Bounds;
            if (screenX >= r.Left && screenX < r.Right &&
                screenY >= r.Top && screenY < r.Bottom)
                return i + 1;
        }
        return 0;
    }

    public static void Detach()
    {
        NativeIsoToolbarClickFilter? previous = _active;
        _active = null;
        if (previous == null) return;
        previous._disposed = true;
        previous._pressedMode = 0;
        System.Windows.Forms.Application.RemoveMessageFilter(previous);
    }
}
