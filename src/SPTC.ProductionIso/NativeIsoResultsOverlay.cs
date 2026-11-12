using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SPTC.ProductionIso;

/// <summary>
/// REV55: Add Custom Save Location links as real child nodes at the END of
/// Autodesk's original Results TreeView. One shared scroll bar, no overlay
/// panel, no shrinking/moving/reparenting of Autodesk's results control.
/// Only Errors shows links for error rows, never successful files.
/// </summary>
internal static class NativeIsoResultsOverlay
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendNative(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

    private const uint WM_CLOSE = 0x0010;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint TVM_GETNEXTITEM = 0x110A;
    private const uint TVM_GETITEMW = 0x113E;
    private const uint TVM_INSERTITEMW = 0x1132;
    private const uint TVM_DELETEITEM = 0x1101;
    private const uint TVM_HITTEST = 0x1111;
    private const uint TVGN_ROOT = 0;
    private const uint TVGN_NEXT = 1;
    private const uint TVGN_CHILD = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TVITEMW
    {
        public uint mask;
        public IntPtr hItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage, iSelectedImage, cChildren;
        public IntPtr lParam;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TVINSERTSTRUCTW
    {
        public IntPtr hParent, hInsertAfter;
        public TVITEMW item;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TVHITTESTINFO
    {
        public POINT pt;
        public uint flags;
        public IntPtr hItem;
    }
    private sealed record NativeCustomLink(IntPtr Item, IntPtr Parent, string Path);

    private sealed class NativeTreeHook : NativeWindow
    {
        private readonly IntPtr _tree;
        public NativeTreeHook(IntPtr tree)
        {
            _tree = tree;
            AssignHandle(tree);
        }
        protected override void WndProc(ref Message message)
        {
            if ((uint)message.Msg == WM_LBUTTONDOWN ||
                (uint)message.Msg == WM_LBUTTONUP ||
                (uint)message.Msg == WM_LBUTTONDBLCLK)
            {
                IntPtr item = HitNativeItem(_tree, message.LParam);
                if (_winLinks.TryGetValue(item, out var link))
                {
                    // Never send our custom row clicks to Autodesk's
                    // original result tree item handler.
                    if ((uint)message.Msg == WM_LBUTTONUP)
                    {
                        try { OpenDrawing(link.Path); }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                "Could not open custom ISO result:\n" +
                                ex.GetBaseException().Message,
                                "SPTC Custom ISO Link", MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    }
                    message.Result = IntPtr.Zero;
                    return;
                }
            }
            base.WndProc(ref message);
        }
    }

    private sealed record CustomResultLink(string Path, bool OpenFolder);

    private static IntPtr _dialog;
    private static TreeView? _nativeTree;
    private static readonly HashSet<TreeNode> _managedCustomLinks = new();
    private static IntPtr _winTree;
    private static NativeTreeHook? _winHook;
    private static readonly Dictionary<IntPtr, NativeCustomLink> _winLinks = new();
    private static string _displaySignature = "";
    private static bool _missingTreeWarned;
    private static System.Windows.Forms.Timer? _timer;
    private static string _customFolder = "";
    private static readonly List<string> CopiedPaths = new();
    private static readonly HashSet<string> ExpectedIsoTags =
        new(StringComparer.OrdinalIgnoreCase);
    private static string? _pendingOpen;
    private static DateTime _pendingCloseTime;

    public static void Reset()
    {
        try { _timer?.Stop(); _timer?.Dispose(); } catch { }
        _timer = null;
        DetachTree();
        _dialog = IntPtr.Zero;
        _customFolder = "";
        CopiedPaths.Clear();
        ExpectedIsoTags.Clear();
        _pendingOpen = null;
        _pendingCloseTime = default;
        _missingTreeWarned = false;
    }

    public static void BeginSession(string? customFolder)
    {
        if (string.IsNullOrWhiteSpace(customFolder))
            return;
        try { _customFolder = Path.GetFullPath(customFolder); }
        catch (Exception) { return; }
        if (_timer != null)
            return;

        // Keep listening even if Autodesk closes and reopens Results from
        // its completion notification. Launch() resets for the next ISO run.
        _timer = new System.Windows.Forms.Timer { Interval = 550 };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        OnTick();
    }

    public static void SetExpectedIsoTags(IEnumerable<string> tags)
    {
        ExpectedIsoTags.Clear();
        foreach (string tag in tags)
            if (!string.IsNullOrWhiteSpace(tag))
                ExpectedIsoTags.Add(tag.Trim());
        _displaySignature = ""; // selected route set may have changed
    }

    private static bool MatchesIsoName(string fileName, string tag)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Equals(tag, StringComparison.OrdinalIgnoreCase))
            return true;

        // Native ISO collision numbering: "A-08 (2).dwg".
        if (stem.StartsWith(tag + " (", StringComparison.OrdinalIgnoreCase) &&
            stem.EndsWith(")", StringComparison.Ordinal) &&
            int.TryParse(stem.Substring(tag.Length + 2,
                stem.Length - tag.Length - 3), out int number) && number > 0)
            return true;

        // Existing project ISO sheet numbering, e.g. LINE-1.dwg.
        if (stem.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(stem.Substring(tag.Length + 1), out int sheet) && sheet > 0)
            return true;
        return false;
    }

    public static bool IsExpectedIsoOutput(string fileName) =>
        ExpectedIsoTags.Count == 0 ||
        ExpectedIsoTags.Any(tag => MatchesIsoName(fileName, tag));

    public static void ReportCopied(string path)
    {
        if (string.IsNullOrWhiteSpace(_customFolder))
            return;
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { return; }

        string folderPrefix =
            Path.TrimEndingDirectorySeparator(_customFolder) +
            Path.DirectorySeparatorChar;
        if (!full.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full) ||
            !IsExpectedIsoOutput(Path.GetFileName(full)) ||
            CopiedPaths.Any(p => p.Equals(full, StringComparison.OrdinalIgnoreCase)))
            return;

        CopiedPaths.Add(full);
        _displaySignature = "";
        RefreshNativeResults();
    }

    private static string WindowText(IntPtr hwnd)
    {
        var value = new StringBuilder(256);
        GetWindowText(hwnd, value, value.Capacity);
        return value.ToString().Trim();
    }

    private static IntPtr FindResults()
    {
        IntPtr result = IntPtr.Zero;
        uint process = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == process &&
                WindowText(hwnd).Equals("Isometric Creation Results",
                    StringComparison.OrdinalIgnoreCase))
            {
                result = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static TreeView? FindNativeTree(IntPtr dialog)
    {
        if (!GetWindowRect(dialog, out RECT window))
            return null;
        double best = -1;
        TreeView? found = null;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is not TreeView tree ||
                tree.IsDisposed || !tree.Visible || tree.Nodes.Count == 0 ||
                !GetWindowRect(hwnd, out RECT rect))
                return true;

            POINT start = new() { X = rect.Left, Y = rect.Top };
            if (!ScreenToClient(dialog, ref start))
                return true;
            if (rect.Width < window.Width * 0.55 ||
                rect.Height < window.Height * 0.42 ||
                start.Y < 40 || start.Y > window.Height * 0.4)
                return true;

            double score = rect.Width * (double)rect.Height;
            if (score > best)
            {
                best = score;
                found = tree;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool OnlyErrorsSelected(IntPtr dialog)
    {
        bool onlyErrors = false;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            Control? control = Control.FromHandle(hwnd);
            string value = control is ComboBox combo
                ? combo.Text.Trim() : WindowText(hwnd);
            if (value.Equals("Only Errors", StringComparison.OrdinalIgnoreCase))
            {
                onlyErrors = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return onlyErrors;
    }

    private static IEnumerable<TreeNode> OriginalNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is CustomResultLink)
                continue;
            yield return node;
            foreach (TreeNode nested in OriginalNodes(node.Nodes))
                yield return nested;
        }
    }

    private static HashSet<string> VisibleErrorLines(TreeView tree)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // In View -> Only Errors, Autodesk suppresses successful "from *.pcf"
        // nodes. Find only remaining original route rows, never our own.
        foreach (TreeNode node in OriginalNodes(tree.Nodes))
        {
            string text = node.Text.Trim();
            int from = text.IndexOf("from ", StringComparison.OrdinalIgnoreCase);
            int pcf = text.LastIndexOf(".pcf", StringComparison.OrdinalIgnoreCase);
            if (from < 0 || pcf <= from + 5)
                continue;
            string name = text.Substring(from + 5, pcf - (from + 5)).Trim();
            if (name.Length > 0)
                tags.Add(name);
        }
        return tags;
    }

    private static void OnTick()
    {
        IntPtr current = FindResults();
        if (_dialog != current ||
            _nativeTree is { IsDisposed: true })
        {
            DetachTree();
            _dialog = current;
        }

        // Clicking a custom file closes Autodesk's MODAL Results window
        // before opening the DWG using the existing REV50 runtime API.
        if (_pendingOpen is string pending)
        {
            if (current == IntPtr.Zero &&
                DateTime.UtcNow - _pendingCloseTime >
                    TimeSpan.FromMilliseconds(200))
            {
                _pendingOpen = null;
                OpenDrawingInPlant(pending);
            }
            else if (DateTime.UtcNow - _pendingCloseTime >
                     TimeSpan.FromSeconds(8))
                _pendingOpen = null;
            return;
        }

        if (current != IntPtr.Zero)
            RefreshNativeResults();
    }

    private static void RefreshNativeResults()
    {
        if (_dialog == IntPtr.Zero || !IsWindow(_dialog))
            return;
        TreeView? tree = FindNativeTree(_dialog);
        if (tree != null)
        {
            if (!ReferenceEquals(tree, _nativeTree))
            {
                DetachTree();
                _nativeTree = tree;
                tree.NodeMouseClick += OnResultNodeClicked;
                tree.BeforeSelect += OnBeforeSelect;
            }
            RefreshManagedPerLine(tree);
            return;
        }

        // REV56: REV55's WinForms-only discovery missed this Autodesk
        // dialog on some versions. Try the native SysTreeView32 control.
        IntPtr windowTree = FindWin32Tree(_dialog);
        if (windowTree != IntPtr.Zero)
        {
            if (_winTree != windowTree)
            {
                DetachTree();
                _winTree = windowTree;
                try { _winHook = new NativeTreeHook(windowTree); }
                catch (Exception ex) { Diagnostic("native hook: " +
                    ex.GetBaseException().Message); }
            }
            RefreshWin32PerLine(windowTree);
            return;
        }
        if (CopiedPaths.Count > 0 && !_missingTreeWarned)
        {
            _missingTreeWarned = true;
            Diagnostic("Results control not a managed TreeView or " +
                "SysTreeView32; custom links not inserted.");
        }
    }

    private static void RefreshManagedPerLine(TreeView tree)
    {
        bool errorsOnly = OnlyErrorsSelected(_dialog);
        var desired = new Dictionary<TreeNode, List<string>>();
        foreach (TreeNode parent in AllOriginalNodes(tree.Nodes))
        {
            string? tag = ExtractRoute(parent.Text);
            if (tag is null) continue;
            List<string> matched = CopiedPaths.Where(p =>
                File.Exists(p) &&
                MatchesIsoName(Path.GetFileName(p), tag)).ToList();
            // Autodesk's Only Errors view removes successful route nodes.
            // Any remaining "from ...pcf" row is error-related.
            if (matched.Count > 0) desired[parent] = matched;
        }

        var remove = _managedCustomLinks.Where(n =>
            n.TreeView != tree || n.Parent is null ||
            !desired.TryGetValue(n.Parent, out var paths) ||
            n.Tag is not CustomResultLink link ||
            !paths.Contains(link.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();
        bool changed = remove.Count > 0 || desired.Any(kv =>
            kv.Value.Any(path => !_managedCustomLinks.Any(n =>
                ReferenceEquals(n.Parent, kv.Key) &&
                n.Tag is CustomResultLink link &&
                link.Path.Equals(path, StringComparison.OrdinalIgnoreCase))));
        if (!changed) return;

        TreeNode? previousTop = tree.TopNode;
        tree.BeginUpdate();
        try
        {
            foreach (TreeNode node in remove)
            {
                if (node.TreeView != null) node.Remove();
                _managedCustomLinks.Remove(node);
            }
            foreach (var (parent, paths) in desired)
            {
                foreach (string path in paths)
                {
                    if (_managedCustomLinks.Any(n =>
                        ReferenceEquals(n.Parent, parent) &&
                        n.Tag is CustomResultLink link &&
                        link.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // One tightly spaced line, directly after Autodesk's
                    // original File + Explorer links. No second scroll.
                    var item = new TreeNode("Custom: " + path)
                    {
                        ForeColor = Color.Blue,
                        ToolTipText = path,
                        Tag = new CustomResultLink(path, false)
                    };
                    parent.Nodes.Add(item);
                    _managedCustomLinks.Add(item);
                    if (!parent.IsExpanded) parent.Expand();
                }
            }
        }
        finally
        {
            tree.EndUpdate();
            try
            {
                if (previousTop != null &&
                    ReferenceEquals(previousTop.TreeView, tree) &&
                    previousTop.IsVisible)
                    tree.TopNode = previousTop;
            }
            catch (Exception) { }
        }
    }

    private static IEnumerable<TreeNode> AllOriginalNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is CustomResultLink) continue;
            yield return node;
            foreach (TreeNode child in AllOriginalNodes(node.Nodes))
                yield return child;
        }
    }

    private static string? ExtractRoute(string label)
    {
        string value = label.Trim();
        int from = value.IndexOf("from ", StringComparison.OrdinalIgnoreCase);
        int pcf = value.LastIndexOf(".pcf", StringComparison.OrdinalIgnoreCase);
        return from >= 0 && pcf > from + 5
            ? value.Substring(from + 5, pcf - from - 5).Trim()
            : null;
    }

    private static void Diagnostic(string message)
    {
        try
        {
            AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\nSPTC REV56: " + message);
        }
        catch (Exception) { }
    }

    private static IntPtr FindWin32Tree(IntPtr dialog)
    {
        if (!GetWindowRect(dialog, out RECT bounds)) return IntPtr.Zero;
        IntPtr found = IntPtr.Zero;
        long area = 0;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            var kind = new StringBuilder(120);
            GetClassName(hwnd, kind, kind.Capacity);
            if (!kind.ToString().Contains("SysTreeView32",
                    StringComparison.OrdinalIgnoreCase) ||
                !GetWindowRect(hwnd, out RECT rect))
                return true;
            long size = (long)rect.Width * rect.Height;
            if (rect.Width >= bounds.Width * 0.50 &&
                rect.Height >= bounds.Height * 0.35 &&
                size > area)
            {
                found = hwnd;
                area = size;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static IntPtr WinNext(IntPtr tree, uint type, IntPtr node) =>
        SendNative(tree, TVM_GETNEXTITEM, new IntPtr(type), node);

    private static string NativeItemText(IntPtr tree, IntPtr node)
    {
        if (node == IntPtr.Zero) return "";
        IntPtr buffer = IntPtr.Zero, itemBuffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(2048);
            Marshal.WriteInt16(buffer, 0);
            var entry = new TVITEMW
            {
                mask = 1, // TVIF_TEXT
                hItem = node,
                pszText = buffer,
                cchTextMax = 1024
            };
            itemBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<TVITEMW>());
            Marshal.StructureToPtr(entry, itemBuffer, false);
            return SendNative(tree, TVM_GETITEMW, IntPtr.Zero,
                itemBuffer) == IntPtr.Zero
                ? "" : Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (itemBuffer != IntPtr.Zero) Marshal.FreeHGlobal(itemBuffer);
        }
    }

    private static IEnumerable<IntPtr> WalkNativeTree(IntPtr tree)
    {
        var output = new List<IntPtr>();
        var seen = new HashSet<IntPtr>();
        void Walk(IntPtr first, int depth)
        {
            if (depth > 14) return;
            IntPtr item = first;
            for (int counter = 0;
                 item != IntPtr.Zero && counter < 10000; counter++)
            {
                if (!seen.Add(item)) break;
                if (!_winLinks.ContainsKey(item))
                    output.Add(item);
                Walk(WinNext(tree, TVGN_CHILD, item), depth + 1);
                item = WinNext(tree, TVGN_NEXT, item);
            }
        }
        Walk(WinNext(tree, TVGN_ROOT, IntPtr.Zero), 0);
        return output;
    }

    private static IntPtr InsertNativeLink(
        IntPtr tree, IntPtr parent, string path)
    {
        IntPtr text = IntPtr.Zero, memory = IntPtr.Zero;
        try
        {
            text = Marshal.StringToHGlobalUni("Custom: " + path);
            var entry = new TVINSERTSTRUCTW
            {
                hParent = parent,
                hInsertAfter = new IntPtr(-65534), // TVI_LAST
                item = new TVITEMW
                {
                    mask = 1, // TVIF_TEXT
                    pszText = text
                }
            };
            memory = Marshal.AllocHGlobal(
                Marshal.SizeOf<TVINSERTSTRUCTW>());
            Marshal.StructureToPtr(entry, memory, false);
            return SendNative(tree, TVM_INSERTITEMW, IntPtr.Zero, memory);
        }
        finally
        {
            if (memory != IntPtr.Zero) Marshal.FreeHGlobal(memory);
            if (text != IntPtr.Zero) Marshal.FreeHGlobal(text);
        }
    }

    private static void RefreshWin32PerLine(IntPtr tree)
    {
        var required = new Dictionary<IntPtr, List<string>>();
        foreach (IntPtr parent in WalkNativeTree(tree))
        {
            string? tag = ExtractRoute(NativeItemText(tree, parent));
            if (tag is null) continue;
            var paths = CopiedPaths.Where(p =>
                File.Exists(p) && MatchesIsoName(
                    Path.GetFileName(p), tag)).ToList();
            if (paths.Count > 0) required[parent] = paths;
        }

        foreach (NativeCustomLink link in _winLinks.Values.ToList())
        {
            if (required.TryGetValue(link.Parent, out var paths) &&
                paths.Contains(link.Path,
                    StringComparer.OrdinalIgnoreCase) &&
                NativeItemText(tree, link.Item).StartsWith(
                    "Custom: ", StringComparison.Ordinal))
                continue;

            if (NativeItemText(tree, link.Item).StartsWith(
                    "Custom: ", StringComparison.Ordinal))
                SendNative(tree, TVM_DELETEITEM, IntPtr.Zero, link.Item);
            _winLinks.Remove(link.Item);
        }

        foreach (var (parent, paths) in required)
            foreach (string path in paths)
            {
                if (_winLinks.Values.Any(link =>
                    link.Parent == parent &&
                    link.Path.Equals(path,
                        StringComparison.OrdinalIgnoreCase)))
                    continue;
                IntPtr created = InsertNativeLink(tree, parent, path);
                if (created != IntPtr.Zero)
                    _winLinks[created] = new NativeCustomLink(
                        created, parent, path);
            }
    }

    private static IntPtr HitNativeItem(IntPtr tree, IntPtr location)
    {
        long bits = location.ToInt64();
        int x = unchecked((short)(bits & 0xFFFF));
        int y = unchecked((short)((bits >> 16) & 0xFFFF));
        IntPtr memory = IntPtr.Zero;
        try
        {
            memory = Marshal.AllocHGlobal(
                Marshal.SizeOf<TVHITTESTINFO>());
            Marshal.StructureToPtr(new TVHITTESTINFO
            {
                pt = new POINT { X = x, Y = y }
            }, memory, false);
            return SendNative(tree, TVM_HITTEST, IntPtr.Zero, memory);
        }
        finally
        {
            if (memory != IntPtr.Zero) Marshal.FreeHGlobal(memory);
        }
    }

    private static void OnBeforeSelect(
        object? sender, TreeViewCancelEventArgs e)
    {
        // Do not send native Autodesk selection handlers to an SPTC
        // hyperlink node; our NodeMouseClick opens it separately.
        if (e.Node.Tag is CustomResultLink)
            e.Cancel = true;
    }

    private static void OnResultNodeClicked(
        object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Left ||
            e.Node.Tag is not CustomResultLink link)
            return;
        try
        {
            if (link.OpenFolder)
                OpenFolder(link.Path);
            else
                OpenDrawing(link.Path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not open custom ISO result:\n" +
                ex.GetBaseException().Message,
                "SPTC Custom ISO Link", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static void RemoveCustomGroup()
    {
        foreach (TreeNode node in _managedCustomLinks.ToList())
        {
            try { if (node.TreeView != null) node.Remove(); }
            catch (Exception) { }
        }
        _managedCustomLinks.Clear();
    }

    private static void DetachTree()
    {
        RemoveCustomGroup();
        if (_nativeTree is { IsDisposed: false } tree)
        {
            tree.NodeMouseClick -= OnResultNodeClicked;
            tree.BeforeSelect -= OnBeforeSelect;
        }
        _nativeTree = null;
        if (_winTree != IntPtr.Zero && IsWindow(_winTree))
        {
            foreach (var link in _winLinks.Values.ToList())
                if (NativeItemText(_winTree, link.Item).StartsWith(
                    "Custom: ", StringComparison.Ordinal))
                    SendNative(_winTree, TVM_DELETEITEM, IntPtr.Zero, link.Item);
        }
        _winLinks.Clear();
        try { _winHook?.ReleaseHandle(); } catch (Exception) { }
        _winHook = null;
        _winTree = IntPtr.Zero;
        _displaySignature = "";
    }

    // REV49: just like Autodesk's original DWG result link, close the
    // current modal results dialog BEFORE opening the custom DWG.
    // Process.Start on the file from inside the modal form (REV48) deferred
    // opening until the user manually closed the Results dialog.
    private static void OpenDrawing(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The custom DWG is missing.", path);
        if (_pendingOpen != null)
            return;

        IntPtr hwnd = _dialog;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            throw new InvalidOperationException(
                "The Autodesk results dialog is no longer available.");

        _pendingOpen = path;
        _pendingCloseTime = DateTime.UtcNow;

        // Post asynchronously to avoid modal WinForms re-entrancy during
        // the LinkClicked handler. Do not close any other dialog/window.
        if (!PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
        {
            _pendingOpen = null;
            throw new InvalidOperationException(
                "The Autodesk results dialog could not be closed.");
        }
    }

    // REV50: Use Autodesk's managed document-opening API instead of
    // sending the unsupported -OPEN command. AutoCAD's official method is
    // DocumentCollectionExtension.Open(DocumentCollection,string,bool).
    // The lightweight NuGet compile stub lacks that extension, so resolve
    // it from the REAL AutoCAD runtime assembly using reflection.
    // This code runs only after the native modal Results form has closed.
    private static void OpenDrawingInPlant(string path)
    {
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "The custom DWG is missing.", path);

            var documents = AcadApp.DocumentManager;
            var existing = documents
                .Cast<Autodesk.AutoCAD.ApplicationServices.Document>()
                .FirstOrDefault(doc => string.Equals(
                    Path.GetFullPath(doc.Name),
                    Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                documents.MdiActiveDocument = existing;
                return;
            }

            // Autodesk runtime API is installed with Plant 3D even though
            // AutoCAD.NET 25.0.1 reference package does not expose all
            // DocumentCollection extension methods to GitHub CI.
            Type? extensionType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(
                    "Autodesk.AutoCAD.ApplicationServices.DocumentCollectionExtension",
                    throwOnError: false))
                .FirstOrDefault(type => type is not null);

            if (extensionType is null)
                throw new MissingMethodException(
                    "Autodesk runtime DocumentCollectionExtension unavailable.");

            var method = extensionType
                .GetMethods(System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "Open")
                        return false;
                    var parameters = m.GetParameters();
                    return parameters.Length == 3 &&
                           parameters[0].ParameterType.IsInstanceOfType(documents) &&
                           parameters[1].ParameterType == typeof(string) &&
                           parameters[2].ParameterType == typeof(bool);
                });

            if (method is null)
                throw new MissingMethodException(
                    "Autodesk runtime DocumentCollectionExtension.Open was not found.");

            object? result = method.Invoke(
                null, new object[] { documents, path, false });
            if (result is Autodesk.AutoCAD.ApplicationServices.Document opened)
                documents.MdiActiveDocument = opened;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not open the custom DWG in Plant 3D:\n" +
                ex.GetBaseException().Message,
                "SPTC Custom ISO Link", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        // Standard Explorer folder link, like Autodesk's original results UI.
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true
        });
    }

}
