using System;
using System.Threading;
using System.Windows.Forms;
using TaskPrioMemory.Core;
using TaskPrioMemory.Storage;

namespace TaskPrioMemory.UI
{
    /// <summary>
    /// The application root. Owns the tray icon and the background watcher and
    /// keeps the process alive with no visible window (it lives in the tray /
    /// the taskbar "show hidden icons" flyout).
    /// </summary>
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly RuleStore _store;
        private readonly ProcessWatcher _watcher;
        private readonly NotifyIcon _tray;
        private readonly SynchronizationContext _ui;
        private MainForm _form;

        public TrayApplicationContext(bool startHidden)
        {
            // Captured on the UI thread so background events can marshal back safely.
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            _store = new RuleStore();
            _watcher = new ProcessWatcher(_store);
            _watcher.Applied += OnRuleApplied;

            _tray = new NotifyIcon
            {
                Icon = IconFactory.AppIcon,
                Text = "Task Priority Memory",
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };
            _tray.DoubleClick += (s, e) => ShowWindow();

            _watcher.Start();

            if (!startHidden && !_store.Settings.StartMinimized)
                ShowWindow();
            else
                _tray.ShowBalloonTip(2000, "Task Priority Memory",
                    "Running in the tray. Saved preferences are being enforced.",
                    ToolTipIcon.Info);
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            var open = new ToolStripMenuItem("Open", null, (s, e) => ShowWindow())
            {
                Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold)
            };
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());

            var applyAll = new ToolStripMenuItem("Apply preferences to running apps now",
                null, (s, e) => _watcher.ApplyToAllRunningNow());
            menu.Items.Add(applyAll);

            var startup = new ToolStripMenuItem("Run at Windows startup")
            {
                Checked = StartupManager.IsEnabled(),
                CheckOnClick = true
            };
            startup.Click += (s, e) =>
            {
                if (!StartupManager.SetEnabled(startup.Checked))
                    startup.Checked = StartupManager.IsEnabled();
            };
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApp()));

            // Refresh the startup checkmark each time the menu opens.
            menu.Opening += (s, e) => startup.Checked = StartupManager.IsEnabled();
            return menu;
        }

        private void ShowWindow()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new MainForm(_store, _watcher);
                _form.FormClosed += (s, e) => _form = null;
            }

            if (!_form.Visible) _form.Show();
            if (_form.WindowState == FormWindowState.Minimized)
                _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            _form.BringToFront();
        }

        private void OnRuleApplied(AppliedEventArgs e)
        {
            // Watcher fires on a background thread; marshal to the UI thread.
            if (e.Result != ApplyResult.AccessDenied) return;
            _ui.Post(_ =>
            {
                try
                {
                    _tray.ShowBalloonTip(4000, "Task Priority Memory",
                        $"Could not adjust {e.ProcessName} (access denied). Run as administrator to manage it.",
                        ToolTipIcon.Warning);
                }
                catch { /* balloon can fail if shell tray is busy; ignore */ }
            }, null);
        }

        private void ExitApp()
        {
            try
            {
                _tray.Visible = false;
                _watcher.Dispose();
                _tray.Dispose();
            }
            finally
            {
                ExitThread();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _watcher?.Dispose();
                _tray?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
