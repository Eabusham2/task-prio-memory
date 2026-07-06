using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TaskPrioMemory.Model;
using TaskPrioMemory.Storage;

namespace TaskPrioMemory.Core
{
    /// <summary>
    /// Watches for newly launched processes and applies any saved preference.
    ///
    /// Deliberately uses a single lightweight timer that diffs the PID set every
    /// few seconds rather than a WMI event sink, so it needs no admin rights and
    /// idles at effectively zero CPU. A scan of the process table costs only a
    /// couple of milliseconds and runs on a background thread-pool thread.
    /// </summary>
    public sealed class ProcessWatcher : IDisposable
    {
        private readonly RuleStore _store;
        private readonly Timer _timer;
        // Keyed by PID + start time so a reused PID counts as a new process.
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly object _scanGate = new object();
        private volatile bool _disposed;

        /// <summary>Raised (on a background thread) whenever a rule is applied to a process.</summary>
        public event Action<AppliedEventArgs> Applied;

        public ProcessWatcher(RuleStore store)
        {
            _store = store;
            _timer = new Timer(_ => SafeScan(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>Start scanning. Applies rules to already-running processes on the first pass.</summary>
        public void Start()
        {
            int period = _store.Settings.NormalizedPollMs();
            _timer.Change(0, period);
        }

        public void UpdateInterval()
        {
            if (_disposed) return;
            int period = _store.Settings.NormalizedPollMs();
            _timer.Change(period, period);
        }

        /// <summary>Force an immediate pass over all running processes (used by "Apply now to all").</summary>
        public void ApplyToAllRunningNow()
        {
            lock (_scanGate) Scan(reapplyEverything: true);
        }

        private void SafeScan()
        {
            if (_disposed) return;
            // Skip if a previous scan is still running (never pile up).
            if (!Monitor.TryEnter(_scanGate)) return;
            try { Scan(reapplyEverything: _store.Settings.ReapplyContinuously); }
            catch { /* keep the watcher alive no matter what */ }
            finally { Monitor.Exit(_scanGate); }
        }

        private void Scan(bool reapplyEverything)
        {
            Process[] procs = Process.GetProcesses();
            var current = new HashSet<string>();

            foreach (var p in procs)
            {
                int pid = p.Id;
                string key = IdentityKey(p, pid);
                current.Add(key);
                bool isNew = !_seen.Contains(key);

                if (isNew || reapplyEverything)
                {
                    string name = SafeName(p);
                    if (name != null)
                    {
                        var rule = _store.Find(name);
                        if (rule != null && rule.Enabled && (rule.HasPriority || rule.HasAffinity))
                        {
                            var result = ProcessManager.ApplyRule(p, rule, out string msg);
                            if (result == ApplyResult.Applied || result == ApplyResult.AccessDenied)
                                Applied?.Invoke(new AppliedEventArgs(name, pid, rule, result, msg));
                        }
                    }
                }
                p.Dispose();
            }

            // Keep the "seen" set bounded to live processes so it never grows unbounded.
            _seen.Clear();
            _seen.UnionWith(current);
        }

        /// <summary>
        /// Stable-enough identity for a running process. Start time distinguishes a
        /// reused PID from the original process; if it can't be read (access denied)
        /// we fall back to the PID alone.
        /// </summary>
        private static string IdentityKey(Process p, int pid)
        {
            try { return pid + ":" + p.StartTime.Ticks; }
            catch { return pid.ToString(); }
        }

        private static string SafeName(Process p)
        {
            try { return p.ProcessName; } // already excludes ".exe"
            catch { return null; }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
        }
    }

    public sealed class AppliedEventArgs : EventArgs
    {
        public string ProcessName { get; }
        public int Pid { get; }
        public ProcessRule Rule { get; }
        public ApplyResult Result { get; }
        public string Message { get; }

        public AppliedEventArgs(string name, int pid, ProcessRule rule, ApplyResult result, string message)
        {
            ProcessName = name;
            Pid = pid;
            Rule = rule;
            Result = result;
            Message = message;
        }
    }
}
