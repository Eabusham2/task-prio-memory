using System;
using System.Diagnostics;
using TaskPrioMemory.Model;

namespace TaskPrioMemory.Core
{
    public enum ApplyResult
    {
        Applied,
        Unchanged,
        AccessDenied,
        Gone,
        Error
    }

    /// <summary>
    /// The ONLY mutations this program performs: setting a process's priority
    /// class and/or its processor affinity. Nothing else (no killing, no memory
    /// edits, etc.) is exposed anywhere in the app.
    /// </summary>
    public static class ProcessManager
    {
        public static int CpuCount => Environment.ProcessorCount;

        /// <summary>Full affinity mask covering every logical processor on this machine.</summary>
        public static long FullMask
        {
            get
            {
                int n = CpuCount;
                return n >= 64 ? -1L : (1L << n) - 1L;
            }
        }

        public static ApplyResult SetPriority(Process p, ProcessPriorityClass priority, out string message)
        {
            message = null;
            try
            {
                if (p.HasExited) { message = "process exited"; return ApplyResult.Gone; }
                if (p.PriorityClass == priority) return ApplyResult.Unchanged;
                p.PriorityClass = priority;
                return ApplyResult.Applied;
            }
            catch (Exception ex) { return Classify(ex, out message); }
        }

        public static ApplyResult SetAffinity(Process p, long mask, out string message)
        {
            message = null;
            try
            {
                if (p.HasExited) { message = "process exited"; return ApplyResult.Gone; }

                // Never allow an empty mask (would be rejected by the OS anyway).
                long safe = mask & FullMask;
                if (safe == 0) safe = FullMask;

                if ((long)p.ProcessorAffinity == safe) return ApplyResult.Unchanged;
                p.ProcessorAffinity = (IntPtr)safe;
                return ApplyResult.Applied;
            }
            catch (Exception ex) { return Classify(ex, out message); }
        }

        /// <summary>Apply an entire saved rule to a running process.</summary>
        public static ApplyResult ApplyRule(Process p, ProcessRule rule, out string message)
        {
            message = null;
            var overall = ApplyResult.Unchanged;

            if (rule.TryGetPriority(out var prio))
            {
                var r = SetPriority(p, prio, out message);
                overall = Merge(overall, r);
                if (IsHardFailure(r)) return r;
            }

            if (rule.HasAffinity)
            {
                var r = SetAffinity(p, rule.Affinity.Value, out message);
                overall = Merge(overall, r);
                if (IsHardFailure(r)) return r;
            }

            return overall;
        }

        private static bool IsHardFailure(ApplyResult r) =>
            r == ApplyResult.AccessDenied || r == ApplyResult.Error;

        private static ApplyResult Merge(ApplyResult a, ApplyResult b)
        {
            if (a == ApplyResult.Applied || b == ApplyResult.Applied) return ApplyResult.Applied;
            if (a == ApplyResult.Gone || b == ApplyResult.Gone) return ApplyResult.Gone;
            return b;
        }

        private static ApplyResult Classify(Exception ex, out string message)
        {
            message = ex.Message;
            if (ex is InvalidOperationException) return ApplyResult.Gone; // process already exited
            if (ex is System.ComponentModel.Win32Exception w)
            {
                const int ERROR_ACCESS_DENIED = 5;
                if (w.NativeErrorCode == ERROR_ACCESS_DENIED)
                {
                    message = "Access denied. Run TaskPrioMemory as administrator to change this process.";
                    return ApplyResult.AccessDenied;
                }
            }
            return ApplyResult.Error;
        }
    }
}
