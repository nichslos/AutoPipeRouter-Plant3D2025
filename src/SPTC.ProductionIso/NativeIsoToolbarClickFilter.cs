using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SPTC.ProductionIso;

/// <summary>
/// REV23: One deterministic Active/All toggle for the three upper-left
/// toolbar icons inside the augmented Production Iso dialog.
///
/// REV22 allowed BOTH the native Autodesk toolbar Click and our delayed
/// projection callback to change the same list. Native scope changes could
/// rebuild the list underneath the projection, yielding zero rows or old
/// entries that reappeared later.
///
/// For recognized upper toolbar button mouse clicks, intercept BOTH the
/// mouse-down and mouse-up messages and execute the SPTC Active/All toggle
/// once; do not also tell Autodesk to rebuild its list. This is a deliberate
/// plugin-only override of those original mode buttons. Outside the toolbar,
/// all native Production Iso input/messages are untouched.
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
    private readonly Func<bool, bool> _toggle;
    private bool _showingActive;
    private bool _pressed;
    private bool _disposed;
    private bool _busy;
    private static NativeIsoToolbarClickFilter? _active;

    private NativeIsoToolbarClickFilter(IntPtr dialog, Func<bool, bool> toggle)
    {
        _dialog = dialog;
        _toggle = toggle;
        System.Windows.Forms.Application.AddMessageFilter(this);
    }

    public static void Attach(IntPtr dialog, Func<bool, bool> toggle)
    {
        if (dialog == IntPtr.Zero) return;
        if (_active is { _disposed: false } previous && previous._dialog == dialog)
            return;
        Detach();
        _active = new NativeIsoToolbarClickFilter(dialog, toggle);
    }

    public static void MarkAllLinesRestored(IntPtr dialog)
    {
        if (_active is { _disposed: false } current && current._dialog == dialog)
            current._showingActive = false;
    }

    public bool PreFilterMessage(ref Message message)
    {
        if (_disposed) return false;
        int msg = message.Msg;
        if (msg == WM_CANCELMODE)
        {
            _pressed = false;
            return false;
        }
        if (msg != WM_LBUTTONDOWN && msg != WM_LBUTTONUP &&
            msg != WM_LBUTTONDBLCLK)
            return false;

        Point pos = Control.MousePosition;
        bool overToolbar = IsOriginalToolbarButton(pos.X, pos.Y);

        if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONDBLCLK)
        {
            if (!overToolbar) return false;
            // Swallow native down: the corresponding up must not trigger
            // Autodesk's own Drawing/Project list regeneration.
            _pressed = true;
            return true;
        }

        if (!_pressed) return false;
        _pressed = false;
        if (!overToolbar) return true; // canceled drag outside the button

        if (_busy) return true;
        _busy = true;
        try
        {
            bool nextShowingActive = !_showingActive;
            // The user gets one change per full click; no 300/900ms timers,
            // no second WinForms Click handler, and no overlapping native
            // requery that could replace our filtered line list.
            if (_toggle(nextShowingActive))
                _showingActive = nextShowingActive;
        }
        catch (Exception)
        {
            // Do not crash the native dialog. A failed active projection
            // leaves its original native line list available.
        }
        finally { _busy = false; }
        return true;
    }

    private bool IsOriginalToolbarButton(int screenX, int screenY)
    {
        if (!GetWindowRect(_dialog, out RECT dialog) ||
            screenX < dialog.Left || screenX > dialog.Right ||
            screenY < dialog.Top || screenY > dialog.Bottom)
            return false;

        // NativeIsoLinePaneResizer tracks the actual 3 icon HWNDs after drag.
        if (NativeIsoLinePaneResizer.IsTopToolbarPoint(screenX, screenY))
            return true;

        // Fall back to live WinForms child controls within the Display lines
        // GroupBox. Do not use a percentage of the whole dialog width:
        // that was wrong when REV20 widened the left panel.
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
            return false;
        if (screenX < box.Left + 55 || screenX > box.Right - 5 ||
            screenY < box.Top + 9 || screenY > box.Top + 62)
            return false;

        bool found = false;
        EnumChildWindows(group, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT r) ||
                screenX < r.Left || screenX > r.Right ||
                screenY < r.Top || screenY > r.Bottom)
                return true;
            int width = r.Width, height = r.Height;
            if (height < 15 || height > 48 || width < 15 || width > 180)
                return true;

            Control? c = Control.FromHandle(hwnd);
            if (c is ButtonBase || c is ToolStrip)
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void Detach()
    {
        NativeIsoToolbarClickFilter? previous = _active;
        _active = null;
        if (previous == null) return;
        previous._disposed = true;
        previous._pressed = false;
        System.Windows.Forms.Application.RemoveMessageFilter(previous);
    }
}
