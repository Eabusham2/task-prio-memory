using System;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace TaskPrioMemory.Model
{
    /// <summary>
    /// A saved preference for a program, matched by executable name (without ".exe").
    /// Either a priority, an affinity mask, or both may be remembered.
    /// </summary>
    [DataContract]
    public class ProcessRule
    {
        /// <summary>Executable name without extension, e.g. "chrome". Matched case-insensitively.</summary>
        [DataMember(Order = 0)]
        public string Name { get; set; }

        /// <summary>Remembered priority class name (see <see cref="ProcessPriorityClass"/>), or null.</summary>
        [DataMember(Order = 1, EmitDefaultValue = false)]
        public string Priority { get; set; }

        /// <summary>Remembered processor affinity mask, or null. Bit N set => allowed to run on CPU N.</summary>
        [DataMember(Order = 2, EmitDefaultValue = false)]
        public long? Affinity { get; set; }

        /// <summary>When false the rule is kept but not applied to new processes.</summary>
        [DataMember(Order = 3)]
        public bool Enabled { get; set; } = true;

        // Field initializers don't run during DataContract deserialization; keep a
        // rule from a file that predates Enabled from silently deserializing disabled.
        [OnDeserializing]
        private void SetDefaults(StreamingContext _) => Enabled = true;

        public bool HasPriority => !string.IsNullOrEmpty(Priority);
        public bool HasAffinity => Affinity.HasValue;

        public bool TryGetPriority(out ProcessPriorityClass priority)
        {
            priority = ProcessPriorityClass.Normal;
            if (!HasPriority) return false;
            return Enum.TryParse(Priority, out priority);
        }

        public string Describe(int cpuCount)
        {
            string p = HasPriority ? Priority : "-";
            string a = HasAffinity ? AffinityText.Describe(Affinity.Value, cpuCount) : "-";
            return "Priority: " + p + "   |   Affinity: " + a;
        }
    }
}
