using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using TaskPrioMemory.Core;
using TaskPrioMemory.Model;

namespace TaskPrioMemory.UI
{
    /// <summary>
    /// Small modal dialog for creating or editing a saved preference directly —
    /// including for programs that are not currently running.
    /// </summary>
    public sealed class RuleEditorForm : Form
    {
        private readonly TextBox _nameBox;
        private readonly ComboBox _priorityCombo;
        private readonly CheckedListBox _affinityList;

        /// <summary>The resulting rule; valid only when ShowDialog returns OK.</summary>
        public ProcessRule Result { get; private set; }

        public RuleEditorForm(ProcessRule existing)
        {
            Text = existing == null ? "Add saved preference" : "Edit saved preference";
            Icon = IconFactory.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(340, 380);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(12)
            };

            layout.Controls.Add(new Label { Text = "Program (exe name, without .exe):", AutoSize = true });
            _nameBox = new TextBox { Width = 300, Text = existing?.Name ?? "" };
            if (existing != null) _nameBox.ReadOnly = true; // name is the rule's key
            layout.Controls.Add(_nameBox);

            layout.Controls.Add(new Label { Text = "Priority:", AutoSize = true, Margin = new Padding(3, 10, 3, 0) });
            _priorityCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            _priorityCombo.Items.AddRange(new object[]
            {
                "(leave unchanged)", "Idle", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime"
            });
            _priorityCombo.SelectedIndex = 0;
            if (existing != null && existing.HasPriority)
                _priorityCombo.SelectedItem = existing.Priority;
            layout.Controls.Add(_priorityCombo);

            layout.Controls.Add(new Label { Text = "CPU affinity:", AutoSize = true, Margin = new Padding(3, 10, 3, 0) });
            _affinityList = new CheckedListBox
            {
                CheckOnClick = true,
                Width = 300,
                Height = 150,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle
            };
            long mask = existing?.Affinity ?? ProcessManager.FullMask;
            for (int i = 0; i < ProcessManager.CpuCount; i++)
                _affinityList.Items.Add("CPU " + i, (mask & (1L << i)) != 0);
            layout.Controls.Add(_affinityList);

            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
            var ok = new Button { Text = "Save", Width = 90, Height = 30, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "Cancel", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => TrySave();
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void TrySave()
        {
            string name = _nameBox.Text.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            if (name.Length == 0)
            {
                MessageBox.Show("Enter the program's exe name.", "TaskPrioMemory",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool changePriority = _priorityCombo.SelectedIndex > 0;
            var prioText = changePriority ? (string)_priorityCombo.SelectedItem : null;

            long mask = 0;
            for (int i = 0; i < _affinityList.Items.Count; i++)
                if (_affinityList.GetItemChecked(i)) mask |= (1L << i);
            if (mask == 0)
            {
                MessageBox.Show("At least one CPU must stay checked.", "TaskPrioMemory",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bool fullMask = (mask & ProcessManager.FullMask) == ProcessManager.FullMask;

            if (!changePriority && fullMask)
            {
                MessageBox.Show(
                    "Nothing to remember: choose a priority and/or restrict the CPUs first.",
                    "TaskPrioMemory", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (changePriority && prioText == nameof(ProcessPriorityClass.RealTime))
            {
                var okRt = MessageBox.Show(
                    "RealTime priority can make the whole system unresponsive. Use it anyway?",
                    "TaskPrioMemory", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (okRt != DialogResult.Yes) return;
            }

            Result = new ProcessRule
            {
                Name = name,
                Priority = prioText,
                Affinity = fullMask ? (long?)null : mask,
                Enabled = true
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
