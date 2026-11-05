using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using WinForms = System.Windows.Forms;

namespace SPTC.SupportDrawings;

/// <summary>REV 02: Batch review dialog for block-based pipe supports.</summary>
internal sealed class SupportBatchDialog : WinForms.Form
{
    private readonly WinForms.CheckedListBox list = new();
    private readonly WinForms.TextBox folder = new();
    private readonly WinForms.TextBox prefix = new();

    public IReadOnlyList<int> CheckedIndices => Enumerable.Range(0, list.Items.Count)
        .Where(i => list.GetItemChecked(i)).ToList();
    public string OutputFolder => folder.Text.Trim();
    public string Prefix => prefix.Text.Trim();

    public SupportBatchDialog(IReadOnlyList<(ObjectId id, string name)> supports)
    {
        Text = "SPTC Support Drawing Batch Export - REV 02";
        Font = new Font("Segoe UI", 9f);
        StartPosition = WinForms.FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 505);
        MinimumSize = new Size(580, 430);
        FormBorderStyle = WinForms.FormBorderStyle.Sizable;
        MaximizeBox = false;

        var header = new WinForms.Label
        {
            Text = "Select supports to export (each BLOCK INSERT = one drawing)",
            Location = new Point(14, 12), Size = new Size(620, 25),
            Anchor = WinForms.AnchorStyles.Top | WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Right
        };
        Controls.Add(header);

        list.CheckOnClick = true;
        list.IntegralHeight = false;
        list.Location = new Point(14, 42);
        list.Size = new Size(632, 300);
        list.Anchor = WinForms.AnchorStyles.Top | WinForms.AnchorStyles.Bottom
                    | WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Right;
        for (int i = 0; i < supports.Count; i++)
            list.Items.Add($"{i + 1:000} | {supports[i].name} | {supports[i].id.Handle}", true);
        Controls.Add(list);

        var all = new WinForms.Button { Text = "Select all", Location = new Point(14, 352), Size = new Size(91, 28) };
        var none = new WinForms.Button { Text = "Clear", Location = new Point(112, 352), Size = new Size(91, 28) };
        all.Click += (_, _) => CheckAll(true);
        none.Click += (_, _) => CheckAll(false);
        all.Anchor = WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Bottom;
        none.Anchor = WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Bottom;
        Controls.Add(all);
        Controls.Add(none);

        var outputLabel = new WinForms.Label { Text = "Output folder:", Location = new Point(14, 391), Size = new Size(94, 22) };
        folder.Location = new Point(113, 387);
        folder.Size = new Size(431, 25);
        folder.Anchor = WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Right;
        var browse = new WinForms.Button { Text = "Browse...", Location = new Point(554, 386), Size = new Size(92, 28) };
        browse.Anchor = WinForms.AnchorStyles.Right | WinForms.AnchorStyles.Bottom;
        browse.Click += (_, _) =>
        {
            using var picker = new WinForms.FolderBrowserDialog { Description = "Choose destination for support sheets" };
            if (Directory.Exists(folder.Text)) picker.SelectedPath = folder.Text;
            if (picker.ShowDialog(this) == WinForms.DialogResult.OK) folder.Text = picker.SelectedPath;
        };
        outputLabel.Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Left;
        Controls.Add(outputLabel); Controls.Add(folder); Controls.Add(browse);

        var prefixLabel = new WinForms.Label { Text = "File prefix:", Location = new Point(14, 428), Size = new Size(94, 22) };
        prefix.Location = new Point(113, 424);
        prefix.Size = new Size(200, 25);
        prefix.Text = "SUPPORT";
        prefix.Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Left;
        prefixLabel.Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Left;
        Controls.Add(prefixLabel); Controls.Add(prefix);

        var run = new WinForms.Button { Text = "Export checked", Location = new Point(411, 464), Size = new Size(134, 30) };
        var cancel = new WinForms.Button { Text = "Cancel", Location = new Point(554, 464), Size = new Size(92, 30) };
        run.Anchor = WinForms.AnchorStyles.Right | WinForms.AnchorStyles.Bottom;
        cancel.Anchor = WinForms.AnchorStyles.Right | WinForms.AnchorStyles.Bottom;
        cancel.DialogResult = WinForms.DialogResult.Cancel;
        run.Click += (_, _) =>
        {
            if (CheckedIndices.Count == 0)
            {
                WinForms.MessageBox.Show(this, "Select at least one support.", "SPTC Export",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(OutputFolder) || !Directory.Exists(OutputFolder))
            {
                WinForms.MessageBox.Show(this, "Select an existing output folder.", "SPTC Export",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(Prefix))
            {
                WinForms.MessageBox.Show(this, "Enter a file prefix.", "SPTC Export",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }
            DialogResult = WinForms.DialogResult.OK;
            Close();
        };
        Controls.Add(run); Controls.Add(cancel);
        AcceptButton = run; CancelButton = cancel;
    }

    private void CheckAll(bool value)
    {
        for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, value);
    }
}
