using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;

namespace SPTC.ProductionIso;

internal sealed class ProductionIsoForm : Form
{
    private readonly Document _doc;
    private readonly TextBox _source = new();
    private readonly TextBox _destination = new();
    private readonly ComboBox _style = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _revision = new() { ReadOnly = true, Text = "Set in native Production Iso dialog" };
    private readonly Label _status = new() { AutoSize = false };
    private readonly Dictionary<string, (long Size, long Ticks)> _before = new(StringComparer.OrdinalIgnoreCase);
    private string? _isometricFolder;
    private string? _snapshotSource;

    public ProductionIsoForm(Document doc)
    {
        _doc = doc;
        Text = "SPTC - Production Iso Export (2025)";
        Width = 780;
        Height = 380;
        MinimumSize = new Size(660, 350);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
            ColumnCount = 3, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        AddRow(layout, 0, "Iso Style", _style);
        AddRow(layout, 1, "Native output folder", _source);
        AddRow(layout, 2, "Revision Number", _revision);
        AddRow(layout, 3, "Custom Save Location", _destination);
        var browse = new Button { Text = "Browse...", Dock = DockStyle.Fill };
        browse.Click += (_, _) => Browse();
        layout.Controls.Add(browse, 2, 3);

        var note = new Label { Text = "Native Production Iso generates first; this tool copies completed DWGs to your location.",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(note, 0, 4);
        layout.SetColumnSpan(note, 3);
        var instructions = new Label { Text = "After the native dialog finishes generating, return here to copy.",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
        layout.Controls.Add(instructions, 0, 5);
        layout.SetColumnSpan(instructions, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        var copy = new Button { Text = "2. Copy finished DWGs", AutoSize = true };
        copy.Click += (_, _) => Copy();
        var open = new Button { Text = "1. Open Production Iso", AutoSize = true };
        open.Click += (_, _) => Launch();
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(open);
        layout.Controls.Add(buttons, 0, 6);
        layout.SetColumnSpan(buttons, 3);

        _status.Dock = DockStyle.Fill;
        _status.Text = "Ready. No changes to Project Manager, IsoConfig.xml, or original DWGs.";
        layout.Controls.Add(_status, 0, 7);
        layout.SetColumnSpan(_status, 3);

        FindStyles();
        _style.SelectedIndexChanged += (_, _) => UpdateNativePath();
        if (_style.Items.Count == 0) _style.Items.Add("Check_A2");
        int index = _style.FindStringExact("Check_A2");
        _style.SelectedIndex = index >= 0 ? index : 0;
    }

    private static void AddRow(TableLayoutPanel table, int row, string caption, Control value)
    {
        table.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        value.Dock = DockStyle.Fill;
        value.Margin = new Padding(2, 5, 2, 5);
        table.Controls.Add(value, 1, row);
    }

    private void FindStyles()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_doc.Name);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "Project.xml")))
                {
                    string iso = Path.Combine(dir, "Isometric");
                    if (Directory.Exists(iso))
                    {
                        _isometricFolder = iso;
                        foreach (string style in Directory.EnumerateDirectories(iso))
                        {
                            if (File.Exists(Path.Combine(style, "IsoConfig.xml")) ||
                                Directory.Exists(Path.Combine(style, "ProdIsos")))
                                _style.Items.Add(Path.GetFileName(style));
                        }
                    }
                    break;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
        }
        catch (Exception) { /* If detection fails, source path remains editable. */ }
    }

    private void UpdateNativePath()
    {
        if (_isometricFolder != null && _style.SelectedItem != null)
            _source.Text = Path.Combine(_isometricFolder, _style.SelectedItem.ToString()!,
                "ProdIsos", "Drawings");
    }

    private void Browse()
    {
        using var picker = new FolderBrowserDialog {
            Description = "Choose custom folder for COPIES of completed Production Iso drawings",
            ShowNewFolderButton = true
        };
        if (Directory.Exists(_destination.Text)) picker.SelectedPath = _destination.Text;
        if (picker.ShowDialog(this) == DialogResult.OK) _destination.Text = picker.SelectedPath;
    }

    private static bool Nested(string a, string b)
    {
        a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
        b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
        return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private bool Validate(bool mustExist)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_source.Text) || string.IsNullOrWhiteSpace(_destination.Text))
                throw new InvalidOperationException("Enter native source and custom destination paths.");
            string src = Path.GetFullPath(_source.Text.Trim());
            string dst = Path.GetFullPath(_destination.Text.Trim());
            if (Nested(src, dst) || Nested(dst, src))
                throw new InvalidOperationException("Destination cannot equal, contain, or be inside source.");
            if (mustExist && !Directory.Exists(src))
                throw new DirectoryNotFoundException("Native DWG source folder not found: " + src);
            return true;
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, "SPTC Production Iso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private static Dictionary<string, (long Size, long Ticks)> Snapshot(string folder)
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return result;
        foreach (string path in Directory.EnumerateFiles(folder, "*.dwg", SearchOption.AllDirectories))
        {
            try
            {
                var fi = new FileInfo(path);
                result[path] = (fi.Length, fi.LastWriteTimeUtc.Ticks);
            }
            catch (IOException) { }
        }
        return result;
    }

    private void Launch()
    {
        if (!Validate(false)) return;
        try
        {
            _snapshotSource = Path.GetFullPath(_source.Text.Trim());
            _before.Clear();
            foreach (var entry in Snapshot(_snapshotSource))
                _before.Add(entry.Key, entry.Value);
            _status.Text = "Snapshot created. Generate with native Production Iso, then return to copy.";
            _doc.SendStringToExecute("_.PLANTPRODUCTIONISO ", true, false, false);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, "SPTC Production Iso");
        }
    }

    private static string NonOverwritingName(string path)
    {
        if (!File.Exists(path)) return path;
        string folder = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 1; i <= 100000; i++)
        {
            string attempt = Path.Combine(folder, stem + "_copy" + i + ext);
            if (!File.Exists(attempt)) return attempt;
        }
        throw new IOException("Too many duplicate names for " + path);
    }

    private void Copy()
    {
        if (_snapshotSource == null)
        {
            MessageBox.Show(this, "Click 'Open Production Iso' first to establish a BEFORE snapshot.");
            return;
        }
        if (!Validate(true)) return;
        if (!Path.GetFullPath(_source.Text.Trim()).Equals(_snapshotSource,
            StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Source path changed since snapshot. Open Production Iso again.");
            return;
        }
        try
        {
            var after = Snapshot(_snapshotSource);
            int copied = 0, skipped = 0;
            foreach (var item in after)
            {
                if (_before.TryGetValue(item.Key, out var old) && old == item.Value)
                    continue;
                try
                {
                    // Skip DWGs still locked by the generator.
                    using (var check = new FileStream(item.Key, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    var relative = Path.GetRelativePath(_snapshotSource, item.Key);
                    var target = Path.Combine(Path.GetFullPath(_destination.Text.Trim()), relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(item.Key, NonOverwritingName(target), false);
                    copied++;
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
            _status.Text = $"Copied {copied} drawing(s), skipped {skipped}.";
            MessageBox.Show(this, $"Copied {copied} completed DWG(s). Skipped {skipped}.\n\n" +
                "Original project drawings were not changed.", "SPTC Production Iso");
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, "SPTC Production Iso Export",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
