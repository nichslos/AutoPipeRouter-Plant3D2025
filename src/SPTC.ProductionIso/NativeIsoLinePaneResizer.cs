using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;

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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(
        IntPtr hwnd, StringBuilder text, int capacity);
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
    private static int _lastGripX = int.MinValue;
    private static int _lastGripY = int.MinValue;
    private static int _lastGripHeight = -1;
    private static bool _adjusting;
    private static Action? _onDragRelayout;
    private static int _leftColumn = -1;
    private static int _rightColumn = -1;
    private static int _columnPadding;
    private static readonly Dictionary<Control, int> OriginalListRightMargins = new();
    private static readonly Dictionary<Control, int> OriginalContainerMargins = new();
    private sealed record FilterChild(IntPtr Handle, int OriginalRightGap, bool IsEdit);
    private static FilterChild? _nativeFilterInput;
    private static FilterChild? _nativeFilterFunnel;
    private static readonly List<FilterChild> _nativeFilterParents = new();
    // REV22: original three Drawing/Project/Filter mode icons above the
    // search bar must travel with the widened left group, not stay behind.
    private sealed record TopToolbarTarget(IntPtr Handle, int RightGap);
    private static readonly List<TopToolbarTarget> _topToolbarTargets = new();

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
            table.GetColumn(_right) > table.GetColumn(_left) &&
            table.GetColumn(_left) < table.GetColumnWidths().Length)
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
        // REV21: capture the original native search field and funnel before
        // widening the left group. Filter controls have their own anchoring and
        // were left behind in REV20 even when the list itself grew.
        CaptureFilterRow();
        CaptureTopToolbar();

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
            StretchFilterRow();
            MoveTopToolbar();

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

    private static void CaptureTopToolbar()
    {
        _topToolbarTargets.Clear();
        if (_left == null || !GetWindowRect(_left.Handle, out RECT group))
            return;

        var candidates = new List<(IntPtr Handle, RECT Rect, bool Strip)>();
        EnumChildWindows(_left.Handle, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT rect)) return true;
            int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
            int cy = (rect.Top + rect.Bottom) / 2 - group.Top;
            if (cy < 8 || cy > 48 || h < 15 || h > 43 ||
                rect.Left < group.Left + 55 ||
                rect.Right > group.Right - 5)
                return true;

            Control? c = Control.FromHandle(hwnd);
            var cls = new StringBuilder(96);
            GetClassName(hwnd, cls, cls.Capacity);
            bool isStrip = c is ToolStrip && w >= 60 && w <= 160;
            bool isButton = (c is ButtonBase ||
                cls.ToString().Contains("BUTTON", StringComparison.OrdinalIgnoreCase))
                && w >= 15 && w <= 45;
            if (isStrip || isButton)
                candidates.Add((hwnd, rect, isStrip));
            return true;
        }, IntPtr.Zero);

        // Prefer a complete native toolbar container: moving its HWND keeps
        // the three original controls and their click events intact.
        var strip = candidates.Where(c => c.Strip).OrderByDescending(c => c.Rect.Right).FirstOrDefault();
        if (strip.Handle != IntPtr.Zero)
        {
            _topToolbarTargets.Add(new TopToolbarTarget(
                strip.Handle, group.Right - strip.Rect.Right));
            return;
        }

        var buttons = candidates.Where(c => !c.Strip)
            .OrderBy(c => c.Rect.Left).ToList();
        List<(IntPtr Handle, RECT Rect, bool Strip)>? triple = null;
        for (int i = 0; i + 2 < buttons.Count; i++)
        {
            var groupThree = buttons.Skip(i).Take(3).ToList();
            if (groupThree[2].Rect.Right - groupThree[0].Rect.Left > 130)
                continue;
            int topMin = groupThree.Min(c => c.Rect.Top);
            int topMax = groupThree.Max(c => c.Rect.Top);
            if (topMax - topMin > 12) continue;
            triple = groupThree;
        }
        if (triple == null) return;

        // If the toolbar buttons share a small non-GroupBox parent, move the
        // containing panel as a unit instead of moving children outside it.
        IntPtr shared = GetParent(triple[0].Handle);
        if (shared != IntPtr.Zero && shared != _left.Handle &&
            triple.All(c => GetParent(c.Handle) == shared) &&
            GetWindowRect(shared, out RECT p) &&
            p.Right - p.Left <= 170 && p.Bottom - p.Top <= 55)
        {
            _topToolbarTargets.Add(new TopToolbarTarget(shared, group.Right - p.Right));
        }
        else
        {
            foreach (var button in triple)
                _topToolbarTargets.Add(new TopToolbarTarget(
                    button.Handle, group.Right - button.Rect.Right));
        }
    }

    private static void MoveTopToolbar()
    {
        if (_left == null || !GetWindowRect(_left.Handle, out RECT group))
            return;
        foreach (var button in _topToolbarTargets)
            UpdateRightEdge(button.Handle,
                group.Right - button.RightGap, resize: false);
    }

    // Used by REV22 click detection after the three toolbar icons shift.
    // Native clicks must continue working regardless of left-pane width.
    public static bool IsTopToolbarPoint(int screenX, int screenY)
    {
        foreach (var target in _topToolbarTargets)
        {
            if (GetWindowRect(target.Handle, out RECT r) &&
                screenX >= r.Left - 2 && screenX <= r.Right + 2 &&
                screenY >= r.Top - 2 && screenY <= r.Bottom + 2)
                return true;
        }
        return false;
    }

    // Search only the narrow horizontal filter band (below the 3 toolbar
    // icons, above the "Line Numbers:" label). Do not mutate filter text.
    private static void CaptureFilterRow()
    {
        _nativeFilterInput = null;
        _nativeFilterFunnel = null;
        _nativeFilterParents.Clear();
        if (_left == null || !GetWindowRect(_left.Handle, out RECT group))
            return;

        IntPtr foundInput = IntPtr.Zero;
        IntPtr foundFunnel = IntPtr.Zero;
        int inputScore = int.MinValue;
        int funnelScore = int.MinValue;

        EnumChildWindows(_left.Handle, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT r)) return true;
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            int relativeY = (r.Top + r.Bottom) / 2 - group.Top;
            int relativeX = (r.Left + r.Right) / 2 - group.Left;
            if (relativeY < 37 || relativeY > 90 ||
                relativeX < 5 || relativeX > group.Right - group.Left)
                return true;

            var name = new StringBuilder(100);
            GetClassName(hwnd, name, name.Capacity);
            string cls = name.ToString();
            Control? control = Control.FromHandle(hwnd);
            bool edit = control is TextBoxBase ||
                cls.Contains("EDIT", StringComparison.OrdinalIgnoreCase);
            if (edit && width >= 80 && height >= 15 && height <= 42)
            {
                // Prefer the broad, left-aligned native Filter input, not
                // any unrelated short edit or controls inside Output settings.
                int score = width - Math.Abs(r.Left - group.Left - 8) * 2;
                if (score > inputScore) { inputScore = score; foundInput = hwnd; }
            }

            bool smallButton = control is ButtonBase ||
                control is PictureBox ||
                cls.Contains("BUTTON", StringComparison.OrdinalIgnoreCase);
            if (smallButton && width >= 15 && width <= 43 &&
                height >= 15 && height <= 39)
            {
                int gap = group.Right - r.Right;
                if (gap >= 0 && gap <= 63)
                {
                    int score = 90 - gap;
                    if (score > funnelScore) { funnelScore = score; foundFunnel = hwnd; }
                }
            }
            return true;
        }, IntPtr.Zero);

        if (foundInput != IntPtr.Zero &&
            GetWindowRect(foundInput, out RECT inputBounds))
        {
            _nativeFilterInput = new FilterChild(
                foundInput, Math.Max(6, group.Right - inputBounds.Right), true);

            // The search input might be nested in a fixed-width row Panel.
            // Preserve each ancestor's original margin from Display lines,
            // expanding ancestors BEFORE moving the textbox itself.
            var ancestors = new List<FilterChild>();
            IntPtr current = GetParent(foundInput);
            while (current != IntPtr.Zero && current != _left.Handle)
            {
                if (!GetWindowRect(current, out RECT parentRect)) break;
                if (parentRect.Left >= group.Left &&
                    parentRect.Right <= group.Right + 4 &&
                    parentRect.Bottom >= group.Top &&
                    parentRect.Top <= group.Bottom)
                {
                    ancestors.Add(new FilterChild(current,
                        Math.Max(6, group.Right - parentRect.Right), false));
                }
                current = GetParent(current);
                if (ancestors.Count >= 8) break;
            }
            ancestors.Reverse();
            _nativeFilterParents.AddRange(ancestors);
        }

        if (foundFunnel != IntPtr.Zero &&
            GetWindowRect(foundFunnel, out RECT funnel))
            _nativeFilterFunnel = new FilterChild(
                foundFunnel, Math.Max(6, group.Right - funnel.Right), false);
    }

    private static void StretchFilterRow()
    {
        if (_left == null || !GetWindowRect(_left.Handle, out RECT group))
            return;

        foreach (var parent in _nativeFilterParents)
            UpdateRightEdge(parent.Handle, group.Right - parent.OriginalRightGap,
                resize: true);
        if (_nativeFilterInput is { } edit)
            UpdateRightEdge(edit.Handle, group.Right - edit.OriginalRightGap,
                resize: true);
        // Move the ORIGINAL Autodesk funnel, not our custom folder arrow.
        if (_nativeFilterFunnel is { } funnel)
            UpdateRightEdge(funnel.Handle, group.Right - funnel.OriginalRightGap,
                resize: false);
    }

    private static void UpdateRightEdge(IntPtr hwnd, int desiredScreenRight, bool resize)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT bounds))
            return;

        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        IntPtr parent = GetParent(hwnd);
        if (parent == IntPtr.Zero) return;

        int leftScreen = resize ? bounds.Left
                                : desiredScreenRight - width;
        int newWidth = resize ? desiredScreenRight - leftScreen : width;
        if (newWidth < (resize ? 60 : 12)) return;
        POINT local = new() { X = leftScreen, Y = bounds.Top };
        if (!ScreenToClient(parent, ref local)) return;
        if (bounds.Right == desiredScreenRight) return;

        SetWindowPos(hwnd, IntPtr.Zero, local.X, local.Y,
            newWidth, height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static int DepthFromLeft(Control control)
    {
        int depth = 0;
        for (Control? c = control.Parent; c != null && c != _left; c = c.Parent)
            depth++;
        return depth;
    }

    // REV25: follow the CURRENT native Display lines GroupBox geometry.
    // REV20 incorrectly captured its height once when opened; this left
    // a short/overhanging drag bar after vertically resizing the dialog.
    private static void PositionHandle()
    {
        if (_handle == null || _handle.IsDisposed ||
            _left == null || _left.IsDisposed ||
            _dialog == IntPtr.Zero ||
            !GetWindowRect(_left.Handle, out RECT group) ||
            !GetClientRect(_dialog, out RECT dialogRect))
            return;

        POINT top = new() { X = group.Right, Y = group.Top };
        POINT bottom = new() { X = group.Right, Y = group.Bottom };
        if (!ScreenToClient(_dialog, ref top) ||
            !ScreenToClient(_dialog, ref bottom))
            return;

        int x = top.X - 3;
        int y = top.Y + 21;
        // Follow the bottom of the actual Display lines section, leaving
        // a modest margin inside its border. Never cover dialog buttons.
        int safeBottom = Math.Min(bottom.Y - 12, dialogRect.Bottom - 55);
        int h = Math.Max(24, safeBottom - y);
        if (x == _lastGripX && y == _lastGripY && h == _lastGripHeight)
            return; // No repaint/Win32 layout work when nothing changed.

        SetWindowPos(_handle.Handle, IntPtr.Zero,
            x, y, 8, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _lastGripX = x;
        _lastGripY = y;
        _lastGripHeight = h;
    }

    public static void Refresh()
    {
        if (_parent == null || _parent.IsDisposed) return;
        if (_lastParentWidth != _parent.ClientSize.Width)
            Layout();
        else
        {
            // REV22: WinForms parent resizing can re-layout the icon toolbar
            // after our column-width calculation. Keep the three original
            // icons anchored to the live right edge during dialog stretches.
            MoveTopToolbar();
            PositionHandle();
        }
    }

    public static void Hide()
    {
        _dragging = false;
        _lastGripX = int.MinValue;
        _lastGripY = int.MinValue;
        _lastGripHeight = -1;
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
        _nativeFilterInput = null;
        _nativeFilterFunnel = null;
        _nativeFilterParents.Clear();
        _topToolbarTargets.Clear();
    }
}
