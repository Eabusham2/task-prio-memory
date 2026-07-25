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
        private TextBox _filterBox;
        private CheckBox _autoRefresh;
        private ComboBox _priorityCombo;
        private CheckedListBox _affinityList;
        private Label _selectedLabel;
        private Button _applyNow;
        private Button _saveRule;

        // Saved tab
        private ListView _rulesList;

        private SplitContainer _split;
        private Label _status;
        private Timer _refreshTimer;

        // Snapshot of the last process scan (so filtering doesn't re-enumerate).
        private sealed class ProcSnap
        {
            public int Id;
            public string Name;
            public string Prio;
            public string Aff;
            public bool Saved;
        }
        private List<ProcSnap> _snapshot = new List<ProcSnap>();

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
            tabs.TabPages.Add(BuildOptionsTab());

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
            _procList.Columns.Add("Program", 160);
            _procList.Columns.Add("PID", 55, HorizontalAlignment.Right);
            _procList.Columns.Add("Priority", 95);
            _procList.Columns.Add("Affinity", 150);
            _procList.Columns.Add("Saved", 55, HorizontalAlignment.Center);
            _procList.SelectedIndexChanged += (s, e) => OnProcessSelected();

            var leftPanel = new Panel { Dock = DockStyle.Fill };

            _filterBox = new TextBox { Dock = DockStyle.Top };
            _filterBox.TextChanged += (s, e) => ApplyFilter();
            var filterLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Text = "Filter by name:",
                ForeColor = SystemColors.GrayText
            };

            var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, AutoSize = false };
            var refreshBtn = new Button { Text = "Refresh list", Width = 100, Height = 28 };
            refreshBtn.Click += (s, e) => RefreshProcesses();
            _autoRefresh = new CheckBox
            {
                Text = "Auto-refresh",
                AutoSize = true,
                Margin = new Padding(8, 6, 0, 0)
            };
            _autoRefresh.CheckedChanged += (s, e) => ToggleAutoRefresh(_autoRefresh.Checked);
            topBar.Controls.Add(refreshBtn);
            topBar.Controls.Add(_autoRefresh);

            // Docked controls render in reverse add-order, so add the fill first.
            leftPanel.Controls.Add(_procList);
            leftPanel.Controls.Add(_filterBox);
            leftPanel.Controls.Add(filterLabel);
            leftPanel.Controls.Add(topBar);
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
            var addBtn = new Button { Text = "Add program…", Width = 115, Height = 30 };
            var editBtn = new Button { Text = "Edit selected", Width = 110, Height = 30 };
            var removeBtn = new Button { Text = "Remove selected", Width = 130, Height = 30 };
            var applyBtn = new Button { Text = "Apply all now", Width = 110, Height = 30 };
            var folderBtn = new Button { Text = "Open data folder", Width = 125, Height = 30 };
            addBtn.Click += (s, e) => EditRule(null);
            editBtn.Click += (s, e) =>
            {
                var name = _rulesList.SelectedItems.Count > 0
                    ? _rulesList.SelectedItems[0].Tag as string : null;
                if (name == null) { SetStatus("Select a saved preference to edit."); return; }
                EditRule(_store.Find(name));
            };
            _rulesList.DoubleClick += (s, e) => editBtn.PerformClick();
            removeBtn.Click += (s, e) => RemoveSelectedRules();
            applyBtn.Click += (s, e) =>
            {
                _watcher.ApplyToAllRunningNow();
                SetStatus("Applied saved preferences to matching running programs.");
            };
            folderBtn.Click += (s, e) => OpenDataFolder();
            bar.Controls.Add(addBtn);
            bar.Controls.Add(editBtn);
            bar.Controls.Add(removeBtn);
            bar.Controls.Add(applyBtn);
            bar.Controls.Add(folderBtn);

            page.Controls.Add(_rulesList);
            page.Controls.Add(bar);
            return page;
        }

        // ---------- Options tab ----------

        private TabPage BuildOptionsTab()
        {
            var page = new TabPage("Options") { Padding = new Padding(16) };
            var s = _store.Settings;

            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            var startupChk = new CheckBox
            {
                Text = "Run at Windows startup (starts hidden in the tray)",
                AutoSize = true,
                Checked = StartupManager.IsEnabled(),
                Margin = new Padding(0, 6, 0, 6)
            };
            startupChk.CheckedChanged += (o, e) =>
            {
                if (!StartupManager.SetEnabled(startupChk.Checked))
                    startupChk.Checked = StartupManager.IsEnabled();
            };

            var minimizedChk = new CheckBox
            {
                Text = "Start minimized to the tray",
                AutoSize = true,
                Checked = s.StartMinimized,
                Margin = new Padding(0, 6, 0, 6)
            };
            minimizedChk.CheckedChanged += (o, e) =>
            {
                s.StartMinimized = minimizedChk.Checked;
                _store.SaveSettings();
            };

            var reapplyChk = new CheckBox
            {
                Text = "Continuously re-apply (guards apps that reset their own priority/affinity)",
                AutoSize = true,
                Checked = s.ReapplyContinuously,
                Margin = new Padding(0, 6, 0, 6)
            };
            reapplyChk.CheckedChanged += (o, e) =>
            {
                s.ReapplyContinuously = reapplyChk.Checked;
                _store.SaveSettings();
            };

            var pollPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 6) };
            pollPanel.Controls.Add(new Label
            {
                Text = "Scan for newly launched programs every",
                AutoSize = true,
                Margin = new Padding(0, 6, 6, 0)
            });
            var pollUpDown = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 3600,
                Value = Math.Min(3600, Math.Max(1, s.PollSeconds)),
                Width = 70
            };
            pollUpDown.ValueChanged += (o, e) =>
            {
                s.PollSeconds = (int)pollUpDown.Value;
                _store.SaveSettings();
                _watcher.UpdateInterval();
                SetStatus($"Scan interval set to {s.PollSeconds}s.");
            };
            pollPanel.Controls.Add(pollUpDown);
            pollPanel.Controls.Add(new Label { Text = "seconds", AutoSize = true, Margin = new Padding(4, 6, 0, 0) });

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 16, 0, 0),
                Text = "A larger interval means even lower CPU usage; a smaller one applies " +
                       "your saved preferences to freshly-launched programs a little sooner. " +
                       "The watcher idles at ~0% CPU between scans either way."
            };

            layout.Controls.Add(new Label
            {
                Text = "Behaviour",
                AutoSize = true,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6)
            });
            layout.Controls.Add(startupChk);
            layout.Controls.Add(minimizedChk);
            layout.Controls.Add(reapplyChk);
            layout.Controls.Add(pollPanel);
            layout.Controls.Add(note);

            page.Controls.Add(layout);
            return page;
        }

        // ---------- Behaviour ----------

        private void RefreshProcesses()
        {
            var savedNames = new HashSet<string>(
                _store.GetRules().Select(r => r.Name), StringComparer.OrdinalIgnoreCase);

            _snapshot = Process.GetProcesses()
                .Select(p =>
                {
                    try
                    {
                        string name = p.ProcessName;
                        return new ProcSnap
                        {
                            Id = p.Id,
                            Name = name,
                            Prio = SafePriority(p),
                            Aff = SafeAffinity(p),
                            Saved = savedNames.Contains(name)
                        };
                    }
                    catch { return null; }
                    finally { p.Dispose(); }
                })
                .Where(x => x != null && !string.IsNullOrEmpty(x.Name))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id)
                .ToList();

            ApplyFilter();
        }

        /// <summary>Renders the current snapshot into the list, honouring the filter box.</summary>
        private void ApplyFilter()
        {
            string previouslySelected = SelectedProcessName();
            string filter = _filterBox?.Text?.Trim();

            _procList.BeginUpdate();
            _procList.Items.Clear();
            try
            {
                foreach (var x in _snapshot)
                {
                    if (!string.IsNullOrEmpty(filter) &&
                        x.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var item = new ListViewItem(x.Name) { Tag = x.Name };
                    item.SubItems.Add(x.Id.ToString());
                    item.SubItems.Add(x.Prio);
                    item.SubItems.Add(x.Aff);
                    item.SubItems.Add(x.Saved ? "✓" : "");
                    if (x.Saved) item.ForeColor = Color.FromArgb(0x1B, 0x66, 0x2C);
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

        private void ToggleAutoRefresh(bool on)
        {
            if (on)
            {
                if (_refreshTimer == null)
                {
                    _refreshTimer = new Timer { Interval = 3000 };
                    _refreshTimer.Tick += (s, e) => RefreshProcesses();
                }
                _refreshTimer.Start();
            }
            else
            {
                _refreshTimer?.Stop();
            }
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
            // "All CPUs" means "no affinity preference" (stored as null), so leaving
            // every box checked must NOT touch a process's existing restricted affinity.
            bool changeAffinity = !fullMask;
            if (!changePriority && !changeAffinity)
            {
                MessageBox.Show(
                    "Nothing to apply: choose a priority and/or restrict the CPUs first.",
                    "TaskPrioMemory", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
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
                    if (changeAffinity)
                    {
                        var ra = ProcessManager.SetAffinity(p, mask, out _);
                        Tally(ra, ref applied, ref denied, ref gone);
                    }
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

        /// <summary>Opens the editor dialog; pass null to create a new rule.</summary>
        private void EditRule(ProcessRule existing)
        {
            using (var dlg = new RuleEditorForm(existing))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                _store.Upsert(dlg.Result);
                RefreshRules();
                RefreshProcesses(); // update the "Saved" column
                SetStatus($"Saved preference for {dlg.Result.Name}.");
            }
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
                _refreshTimer?.Stop(); // don't scan while hidden
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // Resume auto-refresh only while the window is actually on screen.
            if (Visible && _autoRefresh != null && _autoRefresh.Checked)
            {
                RefreshProcesses();
                _refreshTimer?.Start();
            }
            else
            {
                _refreshTimer?.Stop();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _refreshTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
