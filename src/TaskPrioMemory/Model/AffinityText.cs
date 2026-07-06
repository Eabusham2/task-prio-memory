using System.Collections.Generic;
using System.Text;

namespace TaskPrioMemory.Model
{
    /// <summary>Helpers to render an affinity mask as a friendly CPU list, e.g. "CPU 0-3, 6".</summary>
    public static class AffinityText
    {
        public static string Describe(long mask, int cpuCount)
        {
            if (cpuCount <= 0) cpuCount = 64;
            long full = cpuCount >= 64 ? -1L : (1L << cpuCount) - 1L;
            if ((mask & full) == full) return "All CPUs";

            var cpus = new List<int>();
            for (int i = 0; i < cpuCount; i++)
                if ((mask & (1L << i)) != 0) cpus.Add(i);

            if (cpus.Count == 0) return "(none)";

            // Collapse consecutive runs into ranges.
            var sb = new StringBuilder("CPU ");
            int start = cpus[0], prev = cpus[0];
            for (int idx = 1; idx <= cpus.Count; idx++)
            {
                bool end = idx == cpus.Count;
                if (!end && cpus[idx] == prev + 1) { prev = cpus[idx]; continue; }
                if (sb.Length > 4) sb.Append(", ");
                sb.Append(start == prev ? start.ToString() : start + "-" + prev);
                if (!end) { start = prev = cpus[idx]; }
            }
            return sb.ToString();
        }
    }
}
