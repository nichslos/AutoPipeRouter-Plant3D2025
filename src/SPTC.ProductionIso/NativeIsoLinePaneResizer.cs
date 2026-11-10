using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace SPTC.ProductionIso;

/// <summary>
/// REV20: Resizable Display lines column in native Production Iso.
/// Only manipulates live WinForms layout, not PnP database/ISO settings.
/// Refuses to change layout when Autodesk's two panels cannot be identified
/// as sibling native GroupBox controls, avoiding accidental UI damage.
/// </summary>
internal static class NativeIsoLinePaneResizer
{
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(
        IntPtr hwnd, out RECT bounds);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(
        IntPtr hwnd, ref POINT p);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static IntPtr _dialog;
    private static Control? _left;
    private static Control? _right;
    private static Control? _parent;
    private static Panel? _handle;
    private static ToolTip? _toolTip;
    private static bool _dragging;
    private static int _dragMouseStart;
    private static int _dragWidthStart;
    private static int _targetWidth;
    private static int _initialWidth;
    private static int _initialGap;
    private static int _initialRightMargin;
    private static int _initialLeftHeight;
    private static int _lastParentWidth;
    private static bool _adjusting;
    private static Action? _onDragRelayout;
    private static int _leftColumn = -1;
    private static int _rightColumn = -1;
    private static int _columnPadding;
    private static readonly Dictionary<Control, int> OriginalListRightMargins = new();
    private static readonly Dictionary<Control, int> OriginalContainerMargins = new();

    public static bool IsAvailable => _handle is { IsDisposed: false };

    public static void Attach(IntPtr dialog, Action? onDragRelayout = null)
    {
        if (dialog == IntPtr.Zero) return;
        if (_dialog == dialog && IsAvailable) return;
        Hide();
        _dialog = dialog;
        _onDragRelayout = onDragRelayout;

        // Resolve original Autodesk group controls, never a SPTC overlay.
        // Both must be siblings in the same layout parent.
        var groups = new List<Control>();
        EnumNativeControls(dialog, control =>
        {
            if (control is GroupBox &&
                (control.Text.Trim().Equals("Display lines", StringComparison.OrdinalIgnoreCase) ||
                 control.Text.Trim().Equals("Output settings", StringComparison.OrdinalIgnoreCase)))
                groups.Add(control);
        });
        _left = groups.FirstOrDefault(g => g.Text.Trim()
            .Equals("Display lines", StringComparison.OrdinalIgnoreCase));
        _right = groups.FirstOrDefault(g => g.Text.Trim()
            .Equals("Output settings", StringComparison.OrdinalIgnoreCase));

        if (_left == null || _right == null ||
            _left.Parent == null || !ReferenceEquals(_left.Parent, _right.Parent))
        {
            // Native layout type unknown; preserve original UI rather than
            // moving the wrong controls. Report is available in F2 console.
            Hide();
            return;
        }

        _parent = _left.Parent;
        if (_right.Left <= _left.Left || _right.Width < 280)
        {
            Hide();
            return;
        }

        _initialWidth = _left.Width;
        _targetWidth = _initialWidth;
        _initialGap = Math.Max(2, _right.Left - _left.Right);
        _initialRightMargin = Math.Max(4, _parent.ClientSize.Width - _right.Right);
        _initialLeftHeight = _left.Height;
        _lastParentWidth = _parent.ClientSize.Width;
        if (_parent is TableLayoutPanel table &&
            table.GetColumn(_left) >= 0 &&
            table.GetColumn(_right) > table.GetColumn(_left))
        {
            _leftColumn = table.GetColumn(_left);
            _rightColumn = table.GetColumn(_right);
            _columnPadding = Math.Max(0,
                table.GetColumnWidths()[_leftColumn] - _left.Width);
        }
        else
        {
            _leftColumn = -1;
            _rightColumn = -1;
        }

        // Some Autodesk lists are not anchored to both sides of their group.
        // Preserve their original right margins, so the actual line list gets
        // wider when the Display lines border grows.
        OriginalListRightMargins.Clear();
        OriginalContainerMargins.Clear();
        RegisterLineLists(_left);

        _handle = new Panel
        {
            Width = 8,
            Height = Math.Max(100, _left.Height - 8),
            Cursor = Cursors.VSplit,
            BackColor = SystemColors.ControlDark,
            TabStop = false
        };
        _handle.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragMouseStart = Control.MousePosition.X;
            _dragWidthStart = _targetWidth;
            _handle.Capture = true;
        };
        _handle.MouseMove += (_, _) =>
        {
            if (!_dragging || _handle == null) return;
            int delta = Control.MousePosition.X - _dragMouseStart;
            SetDesiredWidth(_dragWidthStart + delta);
            _onDragRelayout?.Invoke();
        };
        _handle.MouseUp += (_, _) =>
        {
            _dragging = false;
            if (_handle != null) _handle.Capture = false;
        };
        _handle.MouseCaptureChanged += (_, _) => { _dragging = false; };

        _ = _handle.Handle;
        SetParent(_handle.Handle, dialog);
        if (GetParent(_handle.Handle) != dialog)
        {
            Hide();
            return;
        }

        _toolTip = new ToolTip();
        _toolTip.SetToolTip(_handle,
            "Drag left/right to widen Line Numbers and view full pipeline names");

        // Start at the familiar native width on compact dialogs. Wider
        // screens get useful extra line-name space immediately, with an
        // explicit drag handle for further adjustments.
        if (_parent.ClientSize.Width >= 1150)
            _targetWidth = Math.Max(_initialWidth, 435);
        else if (_parent.ClientSize.Width >= 950)
            _targetWidth = Math.Max(_initialWidth, 325);

        Layout();
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr p);

    private static void EnumNativeControls(IntPtr dialog, Action<Control> visit)
    {
        var seen = new HashSet<Control>();
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            Control? c = Control.FromHandle(hwnd);
            if (c != null && seen.Add(c))
                visit(c);
            return true;
        }, IntPtr.Zero);
    }

    private static void RegisterLineLists(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if ((child is CheckedListBox || child is ListBox ||
                 child is ListView || child is TreeView) && child.Height > 110)
            {
                OriginalListRightMargins[child] =
                    Math.Max(3, root.ClientSize.Width - child.Right);
                // Preserve nested container right margins too. Otherwise a
                // list inside a fixed-width panel would not grow with its group.
                Control? ancestor = child.Parent;
                while (ancestor != null && !ReferenceEquals(ancestor, _left))
                {
                    if (!OriginalContainerMargins.ContainsKey(ancestor) &&
                        ancestor.Parent != null)
                        OriginalContainerMargins[ancestor] =
                            Math.Max(3, ancestor.Parent.ClientSize.Width - ancestor.Right);
                    ancestor = ancestor.Parent;
                }
            }
            else
            {
                RegisterLineLists(child);
            }
        }
    }

    private static int MaxWidth()
    {
        if (_parent == null || _left == null) return 200;
        // Keep at least 420 px of Output settings visible, including
        // Revision and Custom Save Location input controls.
        return Math.Max(_initialWidth,
            _parent.ClientSize.Width - _left.Left -
            _initialGap - _initialRightMargin - 420);
    }

    private static void SetDesiredWidth(int requested)
    {
        if (_left == null) return;
        _targetWidth = Math.Clamp(requested, _initialWidth, MaxWidth());
        Layout();
    }

    public static void Layout()
    {
        if (_adjusting || _parent == null || _left == null || _right == null ||
            _left.IsDisposed || _right.IsDisposed || _parent.IsDisposed)
            return;

        _adjusting = true;
        try
        {
            int available = _parent.ClientSize.Width;
            int target = Math.Clamp(_targetWidth, _initialWidth, MaxWidth());

            // Respect the original group arrangement and output right margin.
            int newRightX = _left.Left + target + _initialGap;
            int newRightWidth = available - newRightX - _initialRightMargin;
            if (newRightWidth < 420)
            {
                target = Math.Max(_initialWidth, target + newRightWidth - 420);
                newRightX = _left.Left + target + _initialGap;
                newRightWidth = available - newRightX - _initialRightMargin;
            }

            if (_parent is TableLayoutPanel table && _leftColumn >= 0 &&
                _leftColumn < table.ColumnStyles.Count)
            {
                // Autodesk may use a fixed-width left TableLayoutPanel column.
                // Change its column width rather than fighting the layout
                // engine by assigning child GroupBox.Bounds directly.
                table.SuspendLayout();
                try
                {
                    table.ColumnStyles[_leftColumn].SizeType = SizeType.Absolute;
                    table.ColumnStyles[_leftColumn].Width = target + _columnPadding;
                }
                finally { table.ResumeLayout(true); }
            }
            else
            {
                _left.SuspendLayout();
                _right.SuspendLayout();
                try
                {
                    if (_left.Width != target) _left.Width = target;
                    if (_right.Left != newRightX || _right.Width != newRightWidth)
                        _right.SetBounds(newRightX, _right.Top,
                            Math.Max(280, newRightWidth), _right.Height);
                }
                finally
                {
                    _left.ResumeLayout(true);
                    _right.ResumeLayout(true);
                }
            }
            AdjustLineListWidths();

            _targetWidth = target;
            _lastParentWidth = available;
            PositionHandle();
        }
        catch
        {
            // This is an experimental Autodesk UI extension. Failure must
            // never block the native Create, Cancel, or ISO generation.
        }
        finally { _adjusting = false; }
    }

    private static void AdjustLineListWidths()
    {
        // Resize ancestor panels first (outermost to innermost).
        foreach (var pair in OriginalContainerMargins
                     .OrderBy(entry => DepthFromLeft(entry.Key)).ToArray())
        {
            if (pair.Key.IsDisposed || pair.Key.Parent == null) continue;
            int width = pair.Key.Parent.ClientSize.Width -
                pair.Key.Left - pair.Value;
            if (width >= 100 && pair.Key.Width != width)
                pair.Key.Width = width;
        }

        foreach (var entry in OriginalListRightMargins.ToArray())
        {
            Control list = entry.Key;
            if (list.IsDisposed || list.Parent == null) continue;
            int newWidth = list.Parent.ClientSize.Width - list.Left - entry.Value;
            if (newWidth >= 100 && list.Width != newWidth)
                list.Width = newWidth;
        }
    }

    private static int DepthFromLeft(Control control)
    {
        int depth = 0;
        for (Control? c = control.Parent; c != null && c != _left; c = c.Parent)
            depth++;
        return depth;
    }

    private static void PositionHandle()
    {
        if (_handle == null || _handle.IsDisposed ||
            _left == null || !GetWindowRect(_left.Handle, out RECT screen))
            return;
        POINT top = new() { X = screen.Right, Y = screen.Top };
        if (!ScreenToClient(_dialog, ref top)) return;
        int x = top.X - 3;
        int y = top.Y + 21;
        int h = Math.Max(80, _initialLeftHeight - 36);
        SetWindowPos(_handle.Handle, IntPtr.Zero,
            x, y, 8, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _handle.BringToFront();
    }

    public static void Refresh()
    {
        if (_parent == null || _parent.IsDisposed) return;
        if (_lastParentWidth != _parent.ClientSize.Width)
            Layout();
        else
            PositionHandle();
    }

    public static void Hide()
    {
        _dragging = false;
        try { _toolTip?.Dispose(); } catch { }
        try { _handle?.Dispose(); } catch { }
        _toolTip = null;
        _handle = null;
        _dialog = IntPtr.Zero;
        _left = null;
        _right = null;
        _parent = null;
        _targetWidth = 0;
        _initialWidth = 0;
        _lastParentWidth = 0;
        _leftColumn = -1;
        _rightColumn = -1;
        _onDragRelayout = null;
        OriginalContainerMargins.Clear();
        OriginalListRightMargins.Clear();
    }
}
