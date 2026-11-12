using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace SPTC.ProductionIso;

/// <summary>
/// REV08: read the live P3dLineGroup Tag linked to Plant 3D piping objects
/// in THIS open DWG. No changes to PnP database or model objects.
/// When exactly one active tag is verified, use Autodesk's own native text
/// filter to hide old line numbers; never remove entries from the source list.
/// </summary>
internal static class ActiveDwgTags
{
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hwnd, string text);
    [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr hwnd, bool enabled);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private const uint EM_SETREADONLY = 0x00CF;
    // Windows edit-control cue banner: used for Autodesk's start-up
    // "Filter (eg. *52PW*)" hint. Do not change the actual search filter.
    private const uint EM_SETCUEBANNER = 0x1501;
    [DllImport("user32.dll", EntryPoint = "SendMessageW",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageText(
        IntPtr hwnd, uint msg, IntPtr wParam, string text);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    private static readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase);
    public static int Count => Tags.Count;
    public static bool HasScanned { get; private set; }
    public static int DirectTaggedParts { get; private set; }
    public static int GroupTaggedParts { get; private set; }
    public static void Invalidate()
    {
        HasScanned = false;
        Tags.Clear();
        CheckedEntities = 0;
        LinkedEntities = 0;
        DirectTaggedParts = 0;
        GroupTaggedParts = 0;
        LastDiagnostic = "Not scanned";
    }
    public static IReadOnlyList<string> CurrentTags => Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();
    public static int CheckedEntities { get; private set; }
    public static int LinkedEntities { get; private set; }
    public static string LastDiagnostic { get; private set; } = "Not scanned";
    private static IntPtr _lastAppliedDialog = IntPtr.Zero;

    // REV43: In single Active-DWG mode the native Autodesk filter must keep
    // the exact tag internally to show only that route. Display it as an
    // EMPTY search field until the user deliberately types a search.
    // Never clear the native filter just to clear the visible caption:
    // that would bring back all Project line numbers and break Create.
    private static TextBoxBase? _autoFilterEdit;
    private static IntPtr _autoFilterDialog = IntPtr.Zero;
    private static string _autoFilterTag = "";
    private static bool _autoFilterMasked;

    public static void Scan(Document doc)
    {
        Invalidate();
        HasScanned = true;
        // Prefer the actual current LineNumberTag on physical piping parts.
        // Group associations can retain old tags after route renames.
        var componentTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relationshipTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _lastAppliedDialog = IntPtr.Zero;
        try
        {
            object? dlm = ResolveDataLinksManager();
            if (dlm == null)
            {
                LastDiagnostic = "Piping DataLinksManager unavailable; no route filter applied.";
                doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
                return;
            }

            Type dlmType = dlm.GetType();
            MethodInfo? find = dlmType.GetMethods().FirstOrDefault(
                m => m.Name == "FindAcPpRowId" && m.GetParameters().Length == 1 &&
                     m.GetParameters()[0].ParameterType == typeof(ObjectId));
            MethodInfo? related = dlmType.GetMethods().FirstOrDefault(
                m => m.Name == "GetRelatedRowIds" && m.GetParameters().Length == 4 &&
                     m.GetParameters()[0].ParameterType == typeof(string));
            MethodInfo? properties = dlmType.GetMethods().FirstOrDefault(
                m => m.Name == "GetAllProperties" && m.GetParameters().Length == 2);

            if (find == null || related == null || properties == null)
            {
                LastDiagnostic = "The Plant 3D DataLinks API is different on this installation; native list left unchanged.";
                doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
                return;
            }

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                int inspected = 0;
                foreach (ObjectId id in model)
                {
                    if (++inspected > 20000)
                    {
                        Tags.Clear();
                        LinkedEntities = 0;
                        LastDiagnostic = "Too many model-space entities to validate safely; native list unchanged.";
                        doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
                        return;
                    }
                    if (!id.IsValid || id.IsErased) continue;
                    Entity? e;
                    try { e = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                    catch { continue; }
                    if (e == null || e.IsErased) continue;

                    // Restrict to actual Plant 3D model objects. Ignore CAD lines,
                    // xrefs, blocks, miscellaneous metadata and orphan PnP group rows.
                    string typeName = e.GetType().FullName ?? "";
                    string dxfName = id.ObjectClass?.DxfName ?? "";
                    bool plantPart = typeName.StartsWith("Autodesk.ProcessPower.PnP3dObjects.", StringComparison.OrdinalIgnoreCase)
                        || dxfName.StartsWith("ACPP", StringComparison.OrdinalIgnoreCase);
                    if (!plantPart) continue;
                    CheckedEntities++;
                    if (CheckedEntities > 1500)
                    {
                        Tags.Clear();
                        LinkedEntities = 0;
                        LastDiagnostic = "More than 1500 Plant objects; automatic tag filtering disabled to keep Plant 3D responsive.";
                        doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
                        return;
                    }

                    try
                    {
                        object? rowObj = find.Invoke(dlm, new object[] { id });
                        int rowId = Convert.ToInt32(rowObj);
                        if (rowId <= 0) continue;

                        // REV22: The value shown in Properties for a *current*
                        // physical object is more authoritative than a stale
                        // relationship to an older P3dLineGroup Tag.
                        string? actualTag = PropertyFromRow(
                            properties.Invoke(dlm, new object[] { rowId, true }),
                            "LineNumberTag");
                        if (!string.IsNullOrWhiteSpace(actualTag) &&
                            !actualTag.Trim().Equals("Unassigned",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            componentTags.Add(actualTag.Trim());
                            DirectTaggedParts++;
                            LinkedEntities++;
                            continue;
                        }

                        // If this part has no direct line-number property, only
                        // then fall back to its explicit line-group relationship.
                        // Never count standalone/orphan group records as live.
                        object? groupIds = related.Invoke(dlm,
                            new object[] { "P3dLineGroupPartRelationship",
                                "Part", rowId, "LineGroup" });
                        bool linked = false;
                        if (groupIds is IEnumerable enumerated)
                        {
                            foreach (object? group in enumerated)
                            {
                                if (group == null) continue;
                                int groupId = Convert.ToInt32(group);
                                if (groupId <= 0) continue;
                                string? tag = PropertyFromRow(
                                    properties.Invoke(dlm, new object[] { groupId, false }),
                                    "Tag");
                                if (string.IsNullOrWhiteSpace(tag) ||
                                    tag.Trim().Equals("Unassigned",
                                        StringComparison.OrdinalIgnoreCase))
                                    continue;
                                relationshipTags.Add(tag.Trim());
                                linked = true;
                            }
                        }
                        if (linked) GroupTaggedParts++;
                        if (linked) LinkedEntities++;
                    }
                    catch
                    {
                        // Nonpiping Plant entities may lack the relationship.
                        // Never delete or modify data when no tag can be found.
                    }
                }
                tr.Commit();
            }

            // Use direct tags whenever actual component properties provided
            // any. The fallback group set can contain old-but-registered tags.
            foreach (string tag in (componentTags.Count > 0
                         ? componentTags : relationshipTags))
                Tags.Add(tag);
            LastDiagnostic = $"Scanned {CheckedEntities} Plant objects; " +
                $"{DirectTaggedParts} direct-tagged, {GroupTaggedParts} fallback " +
                $"group-tagged, {Count} distinct current route tag(s).";
            doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
            if (Count > 0)
                doc.Editor.WriteMessage("\nSPTC REV08 actual tags: " + string.Join(", ", CurrentTags));
        }
        catch (Exception ex)
        {
            Tags.Clear();
            LastDiagnostic = "Read-only scan failed: " + ex.Message;
            doc.Editor.WriteMessage("\nSPTC REV08: " + LastDiagnostic);
        }
    }

    private static object? ResolveDataLinksManager()
    {
        // Plant-specific DLLs exist inside Plant 3D, not in the GitHub build
        // runner. Reflection avoids additional local installation or project edits.
        Type? app = FindPlantType("Autodesk.ProcessPower.PlantInstance.PlantApplication");
        if (app == null) return null;
        object? project = app.GetProperty("CurrentProject", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        object? parts = project?.GetType().GetProperty("ProjectParts")?.GetValue(project);
        object? piping = parts?.GetType().GetProperty("Item")?.GetValue(parts, new object[] { "Piping" });
        return piping?.GetType().GetProperty("DataLinksManager")?.GetValue(piping);
    }

    private static Type? FindPlantType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? match = assembly.GetType(fullName, false);
            if (match != null) return match;
        }
        try
        {
            return Assembly.Load("PnPProjectManagerMgd").GetType(fullName, false);
        }
        catch { return null; }
    }

    private static string? PropertyFromRow(object? values, string key)
    {
        if (values is not IEnumerable list) return null;
        foreach (object? entry in list)
        {
            if (entry == null) continue;
            Type t = entry.GetType();
            string? name = t.GetProperty("Key")?.GetValue(entry)?.ToString();
            // Display labels and PnP property keys may differ only by
            // spaces/underscores (e.g. "Line Number Tag" vs "LineNumberTag").
            // Compare normalized names, not a substring or guessed prefix.
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    new string((name ?? "").Where(char.IsLetterOrDigit).ToArray()),
                    new string(key.Where(char.IsLetterOrDigit).ToArray()),
                    StringComparison.OrdinalIgnoreCase))
                continue;
            return t.GetProperty("Value")?.GetValue(entry)?.ToString();
        }
        return null;
    }

    // REV27: find the REAL native Filter textbox inside Display lines,
    // independently of how wide the left panel has been stretched. REV08's
    // whole-dialog "x < 33%" search failed after the REV20 splitter resized
    // the left column.
    private static IntPtr FindNativeFilterInput(IntPtr dialog)
    {
        IntPtr group = IntPtr.Zero;
        EnumChildWindows(dialog, (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is GroupBox box &&
                box.Text.Trim().Equals("Display lines", StringComparison.OrdinalIgnoreCase))
            {
                group = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (group == IntPtr.Zero || !GetWindowRect(group, out RECT container))
            return IntPtr.Zero;

        IntPtr found = IntPtr.Zero;
        int best = int.MinValue;
        EnumChildWindows(group, (hwnd, _) =>
        {
            if (!GetWindowRect(hwnd, out RECT r) ||
                r.Width < 70 || r.Height < 15 || r.Height > 46)
                return true;

            int relativeY = (r.Top + r.Bottom) / 2 - container.Top;
            if (relativeY < 39 || relativeY > 112)
                return true;

            Control? control = Control.FromHandle(hwnd);
            var cls = new StringBuilder(100);
            GetClassName(hwnd, cls, cls.Capacity);
            if (control is not TextBoxBase &&
                !cls.ToString().Contains("EDIT", StringComparison.OrdinalIgnoreCase))
                return true;

            // Prefer the input closest to the left margin of the Filter row,
            // not a textbox embedded in an unrelated child control.
            int score = r.Width - 2 * Math.Abs(r.Left - container.Left - 9);
            if (score > best)
            {
                found = hwnd;
                best = score;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // REV44: display an actually blank search field when Production Iso
    // FIRST opens, rather than Autodesk's default "Filter (eg. *52PW*)"
    // hint. Preserve all real line filters, including the hidden REV43
    // Active-DWG tag and every subsequent user-entered search.
    public static void ClearInitialSearchHint(IntPtr dialog)
    {
        if (dialog == IntPtr.Zero || ProjectFolderLinePicker.FolderActive)
            return;
        IntPtr hwnd = FindNativeFilterInput(dialog);
        if (hwnd == IntPtr.Zero)
            return;

        try
        {
            Control? control = Control.FromHandle(hwnd);

            // WinForms .NET 8 TextBox placeholder support.
            if (control is TextBox box && box.PlaceholderText.Length > 0)
                box.PlaceholderText = string.Empty;

            // Native Windows EDIT cue banner (works for Autodesk EDIT
            // controls that are not exposed as managed TextBox objects).
            SendMessageText(hwnd, EM_SETCUEBANNER, IntPtr.Zero, "");

            // Some Autodesk dialogs put the hint in Text instead of using
            // a cue banner. Clear ONLY the known default hint, never a
            // user search or the internally applied Active-DWG tag.
            if (control is TextBoxBase edit)
            {
                if (IsStartupSearchHint(edit.Text))
                    edit.Clear();
            }
            else
            {
                var value = new StringBuilder(256);
                GetWindowText(hwnd, value, value.Capacity);
                if (IsStartupSearchHint(value.ToString()))
                    SetWindowText(hwnd, "");
            }
        }
        catch (Exception)
        {
            // Unsupported native control: retain Autodesk's original UI.
        }
    }

    private static bool IsStartupSearchHint(string? text)
    {
        string value = (text ?? "").Trim();
        return value.StartsWith("Filter (eg.", StringComparison.OrdinalIgnoreCase) &&
               value.EndsWith(")", StringComparison.Ordinal);
    }

    // REV43: mask only an internally supplied Active-DWG filter value.
    // When the user focuses the field, remove the internal query so the
    // search box is genuinely empty and ready for manual input.
    private static void NativeSearchEnter(object? sender, EventArgs e)
    {
        if (sender is not TextBoxBase edit ||
            !ReferenceEquals(edit, _autoFilterEdit) ||
            _autoFilterDialog != _lastAppliedDialog || !_autoFilterMasked)
            return;
        _autoFilterMasked = false;
        edit.ForeColor = SystemColors.WindowText;
        if (edit.Text.Trim().Equals(_autoFilterTag,
                StringComparison.OrdinalIgnoreCase))
            edit.Clear();
    }

    private static void NativeSearchLeave(object? sender, EventArgs e)
    {
        if (sender is not TextBoxBase edit ||
            !ReferenceEquals(edit, _autoFilterEdit) ||
            _autoFilterDialog == IntPtr.Zero ||
            _autoFilterDialog != _lastAppliedDialog)
            return;

        // An empty manual query in Active mode returns to the same exact
        // active route, but keeps its automatic tag visually hidden.
        // Explicit non-empty user searches are never overwritten.
        if (string.IsNullOrWhiteSpace(edit.Text) &&
            Tags.Contains(_autoFilterTag) && !ProjectFolderLinePicker.FolderActive)
        {
            edit.Text = _autoFilterTag;
            _autoFilterMasked = true;
            edit.ForeColor = edit.BackColor;
            NativeIsoExactLineFilter.Begin(_autoFilterDialog, _autoFilterTag,
                Autodesk.AutoCAD.ApplicationServices.Application
                    .DocumentManager.MdiActiveDocument);
        }
    }

    private static void NativeSearchTextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextBoxBase edit ||
            !ReferenceEquals(edit, _autoFilterEdit))
            return;

        // Native refresh/manual typing can replace the automatic tag.
        // Restore normal readable text when this happens.
        if (_autoFilterMasked &&
            !edit.Text.Trim().Equals(_autoFilterTag,
                StringComparison.OrdinalIgnoreCase))
            _autoFilterMasked = false;

        if (!_autoFilterMasked && edit.ForeColor != SystemColors.WindowText)
            edit.ForeColor = SystemColors.WindowText;
    }

    private static void SetAutoFilterAppearance(TextBoxBase edit)
    {
        Color intended = _autoFilterMasked &&
            ReferenceEquals(edit, _autoFilterEdit) &&
            edit.Text.Trim().Equals(_autoFilterTag,
                StringComparison.OrdinalIgnoreCase) && !edit.Focused
            ? edit.BackColor : SystemColors.WindowText;
        if (edit.ForeColor != intended)
            edit.ForeColor = intended;
    }

    private static void AttachAutoFilterDisplay(
        IntPtr dialog, TextBoxBase edit, string tag)
    {
        if (!ReferenceEquals(_autoFilterEdit, edit) ||
            _autoFilterDialog != dialog)
        {
            ResetAutoFilterDisplay();
            _autoFilterEdit = edit;
            _autoFilterDialog = dialog;
            edit.Enter += NativeSearchEnter;
            edit.Leave += NativeSearchLeave;
            edit.TextChanged += NativeSearchTextChanged;
        }
        _autoFilterTag = tag;
        _autoFilterMasked = true;
        SetAutoFilterAppearance(edit);
    }

    // Called when switching back to Project, or the native dialog closes.
    // Never leave the native search field with a hidden user query.
    public static void ResetAutoFilterDisplay()
    {
        TextBoxBase? edit = _autoFilterEdit;
        _autoFilterEdit = null;
        _autoFilterDialog = IntPtr.Zero;
        _autoFilterTag = "";
        _autoFilterMasked = false;
        if (edit is not { IsDisposed: false }) return;
        edit.Enter -= NativeSearchEnter;
        edit.Leave -= NativeSearchLeave;
        edit.TextChanged -= NativeSearchTextChanged;
        if (edit.ForeColor != SystemColors.WindowText)
            edit.ForeColor = SystemColors.WindowText;
    }

    // REV42: keep the ORIGINAL native filter input editable for manual line
    // number searches. Autodesk sometimes leaves the edit portion greyed
    // out after switching to the active-DWG scope. We only enable the
    // identified filter-row input and its narrow immediate container;
    // no native line items, filter text or project records are changed.
    public static bool EnsureNativeSearchEditable(IntPtr dialog)
    {
        if (dialog == IntPtr.Zero || ProjectFolderLinePicker.FolderActive)
            return false;
        IntPtr hwnd = FindNativeFilterInput(dialog);
        if (hwnd == IntPtr.Zero)
            return false;

        Control? control = Control.FromHandle(hwnd);
        try
        {
            // Edit inside a small filter-row panel can inherit Disabled from
            // the parent. Do not enable the whole Display lines group.
            Control? parent = control?.Parent;
            for (int depth = 0; depth < 2 && parent != null; depth++)
            {
                if (parent is GroupBox)
                    break;
                if (parent is Panel panel && panel.Height <= 58)
                    panel.Enabled = true;
                else if (parent is ComboBox combo)
                    combo.Enabled = true;
                else
                    break;
                parent = parent.Parent;
            }

            if (control is TextBoxBase edit)
            {
                if (!edit.Enabled) edit.Enabled = true;
                if (edit.ReadOnly) edit.ReadOnly = false;
                if (edit.Enabled && !edit.ReadOnly)
                {
                    SetAutoFilterAppearance(edit);
                    return true;
                }
            }
            else if (control is ComboBox combo)
            {
                if (!combo.Enabled) combo.Enabled = true;
                return combo.Enabled && combo.DropDownStyle != ComboBoxStyle.DropDownList;
            }

            // Some Autodesk versions expose a native Windows EDIT child
            // rather than a managed WinForms TextBox.
            var cls = new StringBuilder(96);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString().Contains("EDIT",
                    StringComparison.OrdinalIgnoreCase))
            {
                EnableWindow(hwnd, true);
                SendMessage(hwnd, EM_SETREADONLY, IntPtr.Zero, IntPtr.Zero);
                return true;
            }
        }
        catch (Exception)
        {
            // Preserve Autodesk's original UI on an unrecognized control.
        }
        return false;
    }

    // REV28: keep the independent DWG checkbox synchronized if Autodesk's
    // Drawing/Project buttons or the user clear/change the native Filter text.
    // UI-only read; it does not scan any PnP project metadata.
    public static bool IsNativeFilterCurrent(IntPtr dialog)
    {
        if (dialog == IntPtr.Zero || Count != 1) return false;
        IntPtr hwnd = FindNativeFilterInput(dialog);
        if (hwnd == IntPtr.Zero) return false;

        string current;
        if (Control.FromHandle(hwnd) is TextBoxBase edit)
            current = edit.Text;
        else
        {
            var buffer = new StringBuilder(2048);
            GetWindowText(hwnd, buffer, buffer.Capacity);
            current = buffer.ToString();
        }

        return string.Equals(current.Trim(), Tags.First(),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool ApplyOneTag(IntPtr dialog, Document? doc)
    {
        if (Count != 1 || dialog == IntPtr.Zero)
        {
            doc?.Editor.WriteMessage(
                "\nSPTC REV27: Active native filter needs exactly one verified " +
                "physical route; no list changed. Detected tags: " + Count);
            return false;
        }

        IntPtr nativeFilter = FindNativeFilterInput(dialog);
        if (nativeFilter == IntPtr.Zero)
        {
            doc?.Editor.WriteMessage(
                "\nSPTC REV27: Original Filter textbox not found in Display lines. " +
                "No route list changed.");
            return false;
        }

        string tag = Tags.First();
        try
        {
            // The native TextChanged handler itself updates its OWN original
            // checked list; never create a second overlaid checklist.
            if (Control.FromHandle(nativeFilter) is TextBoxBase edit)
                edit.Text = tag;
            else if (!SetWindowText(nativeFilter, tag))
                return false;

            _lastAppliedDialog = dialog;
            EnsureNativeSearchEditable(dialog);
            if (Control.FromHandle(nativeFilter) is TextBoxBase visibleEdit)
                AttachAutoFilterDisplay(dialog, visibleEdit, tag);
            doc?.Editor.WriteMessage(
                "\nSPTC REV27: Native active filter applied: " + tag);
            return true;
        }
        catch (Exception ex)
        {
            doc?.Editor.WriteMessage(
                "\nSPTC REV27: Native active filter failed: " + ex.Message);
            return false;
        }
    }

    public static bool ClearOneTag(IntPtr dialog, Document? doc)
    {
        if (dialog == IntPtr.Zero) return false;
        IntPtr nativeFilter = FindNativeFilterInput(dialog);
        if (nativeFilter == IntPtr.Zero)
        {
            doc?.Editor.WriteMessage(
                "\nSPTC REV27: Original Filter textbox not found for All-lines restore.");
            return false;
        }

        try
        {
            // Avoid auto-reinstating the tag from LostFocus when the user
            // explicitly switches back to All Project.
            _lastAppliedDialog = IntPtr.Zero;
            ResetAutoFilterDisplay();
            if (Control.FromHandle(nativeFilter) is TextBoxBase edit)
                edit.Text = "";
            else if (!SetWindowText(nativeFilter, ""))
                return false;
            doc?.Editor.WriteMessage(
                "\nSPTC REV27: Cleared native line filter; All Project lines restored.");
            return true;
        }
        catch (Exception ex)
        {
            doc?.Editor.WriteMessage(
                "\nSPTC REV27: Native filter clear failed: " + ex.Message);
            return false;
        }
    }
}
