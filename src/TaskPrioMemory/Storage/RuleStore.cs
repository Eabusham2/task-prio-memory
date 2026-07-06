using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using TaskPrioMemory.Model;

namespace TaskPrioMemory.Storage
{
    /// <summary>Wrapper used only for (de)serializing the list of rules.</summary>
    [DataContract]
    public class RuleFile
    {
        [DataMember(Order = 0)]
        public List<ProcessRule> Rules { get; set; } = new List<ProcessRule>();
    }

    /// <summary>
    /// Loads/saves the remembered preferences and the app settings under
    /// %AppData%\TaskPrioMemory. Thread-safe for the small amount of contention
    /// between the UI thread and the background watcher.
    /// </summary>
    public class RuleStore
    {
        private readonly object _gate = new object();
        private readonly string _rulesPath;
        private readonly string _settingsPath;

        private RuleFile _ruleFile;
        private AppSettings _settings;

        public static string DataDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TaskPrioMemory");

        public RuleStore()
        {
            var dir = DataDirectory;
            _rulesPath = Path.Combine(dir, "rules.json");
            _settingsPath = Path.Combine(dir, "settings.json");
            Reload();
        }

        public void Reload()
        {
            lock (_gate)
            {
                _ruleFile = JsonFile.Load<RuleFile>(_rulesPath);
                if (_ruleFile.Rules == null) _ruleFile.Rules = new List<ProcessRule>();
                _settings = JsonFile.Load<AppSettings>(_settingsPath);
            }
        }

        public AppSettings Settings
        {
            get { lock (_gate) return _settings; }
        }

        public void SaveSettings()
        {
            lock (_gate) JsonFile.Save(_settingsPath, _settings);
        }

        /// <summary>Returns a snapshot copy of all rules (safe to enumerate off-lock).</summary>
        public List<ProcessRule> GetRules()
        {
            lock (_gate) return _ruleFile.Rules.ToList();
        }

        /// <summary>Case-insensitive lookup by executable name (without ".exe").</summary>
        public ProcessRule Find(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return null;
            lock (_gate)
                return _ruleFile.Rules.FirstOrDefault(
                    r => string.Equals(r.Name, processName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Insert or update the rule for a program, then persist.</summary>
        public void Upsert(ProcessRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.Name)) return;
            rule.Name = rule.Name.Trim();
            lock (_gate)
            {
                var existing = _ruleFile.Rules.FirstOrDefault(
                    r => string.Equals(r.Name, rule.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) _ruleFile.Rules.Remove(existing);
                _ruleFile.Rules.Add(rule);
                Persist();
            }
        }

        public void Remove(string name)
        {
            lock (_gate)
            {
                _ruleFile.Rules.RemoveAll(
                    r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                Persist();
            }
        }

        public void SetEnabled(string name, bool enabled)
        {
            lock (_gate)
            {
                var r = _ruleFile.Rules.FirstOrDefault(
                    x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (r != null && r.Enabled != enabled)
                {
                    r.Enabled = enabled;
                    Persist();
                }
            }
        }

        private void Persist() => JsonFile.Save(_rulesPath, _ruleFile);
    }
}
