using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SPTC.ProductionIso;

/// <summary>
/// REV10: Listen for a user click on Autodesk's top-left Drawing/Project/
/// Filter toolbar AFTER the original dialog is shown. Before that click,
/// leave all native line-number entries visible on startup. Each user click
/// alternates between the verified active-DWG tag and the unfiltered native
/// list by setting/clearing Autodesk's existing text filter after the click.
/// No project records, model objects, or native buttons change.
/// </summary>
internal sealed class NativeIsoToolbarClickFilter : IMessageFilter
{
    private const int WM_LBUTTONUP = 0x0202;
    private static NativeIsoToolbarClickFilter? _active;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int size);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwnd, EnumChildProc proc, IntPtr data);
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);

    private readonly IntPtr _dialog;
    private readonly Func<bool, bool> _toggle;
    private readonly System.Windows.Forms.Timer _delay;
    private readonly List<(Control Control, EventHandler Handler)> _buttonHandlers = new();
    private readonly List<(ToolStripItem Item, EventHandler Handler)> _toolStripHandlers = new();
    private bool _disposed;
    // REV10: false = native unfiltered list; true = active-DWG tag filter.
    // Every intentional toolbar click switches state exactly once.
    private bool _showingActive;

    private NativeIsoToolbarClickFilter(IntPtr dialog, Func<bool, bool> toggle)
    {
        _dialog = dialog;
        _toggle = toggle;
        _delay = new System.Windows.Forms.Timer { Interval = 300 };
        _delay.Tick += (_, _) =>
        {
            _delay.Stop();
            if (_disposed) return;
            try
            {
                bool nextShowingActive = !_showingActive;
                // Commit only if the UI filter was successfully applied/cleared.
                if (_toggle(nextShowingActive))
                    _showingActive = nextShowingActive;
            }
            catch { /* Extension errors must never block native ISO dialog. */ }
        };
        HookManagedButtons();
        System.Windows.Forms.Application.AddMessageFilter(this);
    }

    public static void Attach(IntPtr dialog, Func<bool, bool> toggle)
    {
        if (dialog == IntPtr.Zero) return;
        if (_active is { _disposed: false } active && active._dialog == dialog) return;
        Detach();
        _active = new NativeIsoToolbarClickFilter(dialog, toggle);
    }

    // REV21: If applying the active tag returns an empty native line list,
    // the dialog enhancer clears that filter. Keep the next native toolbar
    // click synchronized with the restored All view.
    public static void MarkAllLinesRestored(IntPtr dialog)
    {
        if (_active is { _disposed: false } current &&
            current._dialog == dialog)
            current._showingActive = false;
    }

    private void Schedule()
    {
        if (_disposed) return;
        // REV11: restoring ALL only clears an existing text filter and can be
        // requested sooner. The native Drawing/Project button may still need
        // its own time to refresh a large or network-backed project list.
        // Keep the slightly longer debounce for applying the active tag
        // after Autodesk's mode switch has had a chance to run.
        _delay.Stop();
        _delay.Interval = _showingActive ? 100 : 300;
        _delay.Start();
    }

    /// <summary>WinForms hooks for the small native mode buttons.</summary>
    private void HookManagedButtons()
    {
        if (!GetWindowRect(_dialog, out RECT dialog)) return;
        EnumChildWindows(_dialog, (hwnd, _) =>
        {
            Control? control = Control.FromHandle(hwnd);
            if (control == null) return true;

            if (control is ToolStrip strip && GetWindowRect(hwnd, out RECT stripRect) &&
                IsTopModeButton(dialog,
                    stripRect.Left + stripRect.Width / 2,
                    stripRect.Top + stripRect.Height / 2))
            {
                foreach (ToolStripItem item in strip.Items)
                {
                    if (!item.Enabled) continue;
                    EventHandler handler = (_, _) => Schedule();
                    item.Click += handler;
                    _toolStripHandlers.Add((item, handler));
                }
            }
            else if (control is ButtonBase && GetWindowRect(hwnd, out RECT r) &&
                IsTopModeButton(dialog, r.Left + r.Width / 2, r.Top + r.Height / 2))
            {
                EventHandler handler = (_, _) => Schedule();
                control.Click += handler;
                _buttonHandlers.Add((control, handler));
            }
            return true;
        }, IntPtr.Zero);
    }

    // Primary fallback for Win32/WPF-driven toolbar clicks that do not surface
    // as managed Button.Click/ToolStripItem.Click events. No mouse messages are
    // consumed or redirected; Autodesk receives and handles the original click.
    public bool PreFilterMessage(ref Message message)
    {
        if (_disposed || message.Msg != WM_LBUTTONUP) return false;
        if (!GetWindowRect(_dialog, out RECT dialog)) return false;
        Point cursor = Control.MousePosition;
        if (IsTopModeButton(dialog, cursor.X, cursor.Y))
            Schedule();
        return false;
    }

    private static bool IsTopModeButton(RECT dialog, int sx, int sy)
    {
        if (dialog.Width < 300 || dialog.Height < 240) return false;
        double x = (sx - dialog.Left) / (double)dialog.Width;
        double y = (sy - dialog.Top) / (double)dialog.Height;

        // Fits three small toolbar icons above the Filter field and to the
        // right of "Display lines"; excludes the text field and main buttons.
        return x >= 0.125 && x <= 0.285 && y >= 0.055 && y <= 0.125;
    }

    public static void Detach()
    {
        NativeIsoToolbarClickFilter? old = _active;
        _active = null;
        if (old is null) return;
        old._disposed = true;
        System.Windows.Forms.Application.RemoveMessageFilter(old);
        old._delay.Stop();
        old._delay.Dispose();

        foreach (var (control, handler) in old._buttonHandlers)
        {
            try { control.Click -= handler; } catch { }
        }
        foreach (var (item, handler) in old._toolStripHandlers)
        {
            try { item.Click -= handler; } catch { }
        }
        old._buttonHandlers.Clear();
        old._toolStripHandlers.Clear();
    }
}
