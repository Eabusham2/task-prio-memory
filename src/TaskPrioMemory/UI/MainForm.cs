using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TaskPrioMemory.Core;
using TaskPrioMemory.Model;
using TaskPrioMemory.Storage;

namespace TaskPrioMemory.UI
{
    /// <summary>
    /// The GUI. Two tabs:
    ///   - Processes: pick a running program and set/remember its priority + affinity.
    ///   - Saved Preferences: view, enable/disable and remove remembered programs.
    /// Closing the window only hides it; the app keeps living in the tray.
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly RuleStore _store;
        private readonly ProcessWatcher _watcher;

        // Processes tab
        private ListView _procList;
        private ComboBox _priorityCombo;
        private CheckedListBox _affinityList;
        private Label _selectedLabel;
        private Button _applyNow;
        private Button _saveRule;

        // Saved tab
        private ListView _rulesList;

        private SplitContainer _split;
        private Label _status;

        public MainForm(RuleStore store, ProcessWatcher watcher)
        {
            _store = store;
            _watcher = watcher;
            BuildUi();
            RefreshProcesses();
            RefreshRules();
        }

        private void BuildUi()
        {
            Text = "Task Priority Memory";
            Icon = IconFactory.AppIcon;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 520);
            Size = new Size(880, 600);
            Font = new Font("Segoe UI", 9f);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildProcessesTab());
            tabs.TabPages.Add(BuildSavedTab());

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                BackColor = SystemColors.ControlLight,
                Text = $"{ProcessManager.CpuCount} logical processors detected. " +
                       "Only priority and affinity are changed — nothing else."
            };

            Controls.Add(tabs);
            Controls.Add(_status);
        }

        // ---------- Processes tab ----------

        private TabPage BuildProcessesTab()
        {
            var page = new TabPage("Processes") { Padding = new Padding(8) };

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };
            var split = _split;

            // Left: list of running processes
            _procList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                GridLines = false
            };
            _procList.Columns.Add("Program", 170);
            _procList.Columns.Add("PID", 60, HorizontalAlignment.Right);
            _procList.Columns.Add("Priority", 100);
            _procList.Columns.Add("Affinity", 160);
            _procList.SelectedIndexChanged += (s, e) => OnProcessSelected();

            var leftPanel = new Panel { Dock = DockStyle.Fill };
            var refreshBtn = new Button { Text = "Refresh list", Dock = DockStyle.Top, Height = 30 };
            refreshBtn.Click += (s, e) => RefreshProcesses();
            leftPanel.Controls.Add(_procList);
            leftPanel.Controls.Add(refreshBtn);
            split.Panel1.Controls.Add(leftPanel);

            // Right: editor
            split.Panel2.Controls.Add(BuildEditor());

            page.Controls.Add(split);
            return page;
        }

        private Control BuildEditor()
        {
            var host = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(10, 4, 4, 4),
                AutoScroll = true
            };
            host.RowStyles.Clear();

            _selectedLabel = new Label
            {
                Text = "Select a program on the left.",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 8)
            };

            var prioLbl = new Label { Text = "Priority", AutoSize = true, Margin = new Padding(0, 6, 0, 2) };
            _priorityCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 220
            };
            _priorityCombo.Items.AddRange(new object[]
            {
                "(leave unchanged)",
                "Idle",          // lowest
                "BelowNormal",
                "Normal",
                "AboveNormal",
                "High",
                "RealTime"       // use with care
            });
            _priorityCombo.SelectedIndex = 0;

            var affLbl = new Label { Text = "CPU affinity", AutoSize = true, Margin = new Padding(0, 10, 0, 2) };
            _affinityList = new CheckedListBox
            {
                CheckOnClick = true,
                Width = 240,
                Height = 170,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle
            };
            for (int i = 0; i < ProcessManager.CpuCount; i++)
                _affinityList.Items.Add("CPU " + i, true);

            var affBtns = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            var allBtn = new Button { Text = "All", Width = 70 };
            var noneBtn = new Button { Text = "None", Width = 70 };
            allBtn.Click += (s, e) => SetAllAffinity(true);
            noneBtn.Click += (s, e) => SetAllAffinity(false);
            affBtns.Controls.Add(allBtn);
            affBtns.Controls.Add(noneBtn);

            var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
            _applyNow = new Button { Text = "Apply now", Width = 110, Height = 32, Enabled = false };
            _saveRule = new Button
            {
                Text = "Save + remember",
                Width = 150,
                Height = 32,
                Enabled = false
            };
            _applyNow.Click += (s, e) => ApplyToSelected(save: false);
            _saveRule.Click += (s, e) => ApplyToSelected(save: true);
            actions.Controls.Add(_applyNow);
            actions.Controls.Add(_saveRule);

            var hint = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(300, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 14, 0, 0),
                Text = "\"Save + remember\" stores this preference. Whenever the program " +
                       "launches again, TaskPrioMemory re-applies it automatically."
            };

            host.Controls.Add(_selectedLabel);
            host.Controls.Add(prioLbl);
            host.Controls.Add(_priorityCombo);
            host.Controls.Add(affLbl);
            host.Controls.Add(_affinityList);
            host.Controls.Add(affBtns);
            host.Controls.Add(actions);
            host.Controls.Add(hint);
            return host;
        }

        // ---------- Saved tab ----------

        private TabPage BuildSavedTab()
        {
            var page = new TabPage("Saved Preferences") { Padding = new Padding(8) };

            _rulesList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false
            };
            _rulesList.Columns.Add("Program", 170);
            _rulesList.Columns.Add("Priority", 110);
            _rulesList.Columns.Add("Affinity", 220);
            _rulesList.Columns.Add("Enabled", 70);
            _rulesList.ItemChecked += OnRuleChecked;

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, AutoSize = false };
            var reloadBtn = new Button { Text = "Refresh", Width = 90, Height = 30 };
            var removeBtn = new Button { Text = "Remove selected", Width = 140, Height = 30 };
            var applyBtn = new Button { Text = "Apply all now", Width = 120, Height = 30 };
            var folderBtn = new Button { Text = "Open data folder", Width = 130, Height = 30 };
            reloadBtn.Click += (s, e) => { _store.Reload(); RefreshRules(); };
            removeBtn.Click += (s, e) => RemoveSelectedRules();
            applyBtn.Click += (s, e) =>
            {
                _watcher.ApplyToAllRunningNow();
                SetStatus("Applied saved preferences to matching running programs.");
            };
            folderBtn.Click += (s, e) => OpenDataFolder();
            bar.Controls.Add(reloadBtn);
            bar.Controls.Add(removeBtn);
            bar.Controls.Add(applyBtn);
            bar.Controls.Add(folderBtn);

            page.Controls.Add(_rulesList);
            page.Controls.Add(bar);
            return page;
        }

        // ---------- Behaviour ----------

        private void RefreshProcesses()
        {
            string previouslySelected = SelectedProcessName();
            _procList.BeginUpdate();
            _procList.Items.Clear();
            try
            {
                var procs = Process.GetProcesses()
                    .Select(p =>
                    {
                        try
                        {
                            return new
                            {
                                p.Id,
                                Name = p.ProcessName,
                                Prio = SafePriority(p),
                                Aff = SafeAffinity(p)
                            };
                        }
                        catch { return null; }
                        finally { p.Dispose(); }
                    })
                    .Where(x => x != null && !string.IsNullOrEmpty(x.Name))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Id)
                    .ToList();

                foreach (var x in procs)
                {
                    var item = new ListViewItem(x.Name) { Tag = x.Name };
                    item.SubItems.Add(x.Id.ToString());
                    item.SubItems.Add(x.Prio);
                    item.SubItems.Add(x.Aff);
                    _procList.Items.Add(item);
                }
            }
            finally
            {
                _procList.EndUpdate();
            }

            if (previouslySelected != null)
                ReselectProcess(previouslySelected);
        }

        private static string SafePriority(Process p)
        {
            try { return p.PriorityClass.ToString(); }
            catch { return "-"; }
        }

        private static string SafeAffinity(Process p)
        {
            try { return AffinityText.Describe((long)p.ProcessorAffinity, ProcessManager.CpuCount); }
            catch { return "-"; }
        }

        private string SelectedProcessName() =>
            _procList.SelectedItems.Count > 0 ? _procList.SelectedItems[0].Tag as string : null;

        private void ReselectProcess(string name)
        {
            foreach (ListViewItem it in _procList.Items)
            {
                if (string.Equals(it.Tag as string, name, StringComparison.OrdinalIgnoreCase))
                {
                    it.Selected = true;
                    it.EnsureVisible();
                    return;
                }
            }
        }

        private void OnProcessSelected()
        {
            string name = SelectedProcessName();
            bool has = name != null;
            _applyNow.Enabled = has;
            _saveRule.Enabled = has;
            if (!has)
            {
                _selectedLabel.Text = "Select a program on the left.";
                return;
            }

            _selectedLabel.Text = name;

            // Pre-fill the editor from an existing saved rule if there is one.
            var rule = _store.Find(name);
            if (rule != null && rule.TryGetPriority(out var prio))
                _priorityCombo.SelectedItem = prio.ToString();
            else
                _priorityCombo.SelectedIndex = 0;

            long mask = rule?.Affinity ?? ProcessManager.FullMask;
            for (int i = 0; i < _affinityList.Items.Count; i++)
                _affinityList.SetItemChecked(i, (mask & (1L << i)) != 0);
        }

        private void SetAllAffinity(bool value)
        {
            for (int i = 0; i < _affinityList.Items.Count; i++)
                _affinityList.SetItemChecked(i, value);
        }

        private long BuildMaskFromChecks()
        {
            long mask = 0;
            for (int i = 0; i < _affinityList.Items.Count; i++)
                if (_affinityList.GetItemChecked(i)) mask |= (1L << i);
            return mask;
        }

        private void ApplyToSelected(bool save)
        {
            string name = SelectedProcessName();
            if (name == null) return;

            bool changePriority = _priorityCombo.SelectedIndex > 0;
            ProcessPriorityClass prio = ProcessPriorityClass.Normal;
            if (changePriority && !Enum.TryParse((string)_priorityCombo.SelectedItem, out prio))
                changePriority = false;

            long mask = BuildMaskFromChecks();
            bool fullMask = (mask & ProcessManager.FullMask) == ProcessManager.FullMask;
            if (mask == 0)
            {
                MessageBox.Show("At least one CPU must stay checked.", "TaskPrioMemory",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (changePriority && prio == ProcessPriorityClass.RealTime)
            {
                var ok = MessageBox.Show(
                    "RealTime priority can make the whole system unresponsive. Use it anyway?",
                    "TaskPrioMemory", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (ok != DialogResult.Yes) return;
            }

            // Apply to every currently-running instance of that program.
            int applied = 0, denied = 0, gone = 0;
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    if (changePriority)
                    {
                        var r = ProcessManager.SetPriority(p, prio, out _);
                        Tally(r, ref applied, ref denied, ref gone);
                    }
                    var ra = ProcessManager.SetAffinity(p, mask, out _);
                    Tally(ra, ref applied, ref denied, ref gone);
                }
            }

            if (save)
            {
                var rule = new ProcessRule
                {
                    Name = name,
                    Priority = changePriority ? prio.ToString() : null,
                    Affinity = fullMask ? (long?)null : mask,
                    Enabled = true
                };
                // If neither field is set, saving a rule is meaningless.
                if (!rule.HasPriority && !rule.HasAffinity)
                {
                    MessageBox.Show(
                        "Nothing to remember: choose a priority and/or restrict the CPUs first.",
                        "TaskPrioMemory", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _store.Upsert(rule);
                RefreshRules();
            }

            RefreshProcesses();
            ReselectProcess(name);

            string verb = save ? "Saved & applied" : "Applied";
            string note = $"{verb} for {name}. Changed {applied}"
                          + (denied > 0 ? $", {denied} denied (need admin)" : "")
                          + (gone > 0 ? $", {gone} exited" : "") + ".";
            SetStatus(note);
        }

        private static void Tally(ApplyResult r, ref int applied, ref int denied, ref int gone)
        {
            switch (r)
            {
                case ApplyResult.Applied: applied++; break;
                case ApplyResult.AccessDenied: denied++; break;
                case ApplyResult.Gone: gone++; break;
            }
        }

        private void RefreshRules()
        {
            _rulesList.BeginUpdate();
            _rulesList.ItemChecked -= OnRuleChecked;
            _rulesList.Items.Clear();
            foreach (var r in _store.GetRules().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(r.Name) { Tag = r.Name, Checked = r.Enabled };
                item.SubItems.Add(r.HasPriority ? r.Priority : "-");
                item.SubItems.Add(r.HasAffinity
                    ? AffinityText.Describe(r.Affinity.Value, ProcessManager.CpuCount)
                    : "-");
                item.SubItems.Add(r.Enabled ? "Yes" : "No");
                _rulesList.Items.Add(item);
            }
            _rulesList.ItemChecked += OnRuleChecked;
            _rulesList.EndUpdate();
        }

        private void OnRuleChecked(object sender, ItemCheckedEventArgs e)
        {
            string name = e.Item.Tag as string;
            if (name == null) return;
            _store.SetEnabled(name, e.Item.Checked);
            e.Item.SubItems[3].Text = e.Item.Checked ? "Yes" : "No";
            SetStatus($"{name} preference {(e.Item.Checked ? "enabled" : "disabled")}.");
        }

        private void RemoveSelectedRules()
        {
            var names = _rulesList.SelectedItems.Cast<ListViewItem>()
                .Select(i => i.Tag as string).Where(n => n != null).ToList();
            if (names.Count == 0)
            {
                SetStatus("Select a saved preference to remove.");
                return;
            }
            foreach (var n in names) _store.Remove(n);
            RefreshRules();
            SetStatus($"Removed {names.Count} saved preference(s).");
        }

        private void OpenDataFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(RuleStore.DataDirectory);
                Process.Start("explorer.exe", RuleStore.DataDirectory);
            }
            catch (Exception ex) { SetStatus("Could not open folder: " + ex.Message); }
        }

        private void SetStatus(string text) => _status.Text = text;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Safe to size the splitter now that the container has real bounds.
            try
            {
                if (_split != null && _split.Width > 200)
                    _split.SplitterDistance = (int)(_split.Width * 0.52);
            }
            catch { /* ignore transient layout constraints */ }
        }

        // Closing the window hides it to the tray instead of exiting.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
