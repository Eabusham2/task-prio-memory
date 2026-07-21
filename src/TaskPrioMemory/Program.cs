using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TaskPrioMemory.UI;

namespace TaskPrioMemory
{
    internal static class Program
    {
        // Per-user single-instance guard + wake-up signal for second launches.
        internal const string ShowWindowEventName = @"Local\TaskPrioMemory.ShowWindow";
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            _mutex = new Mutex(true, @"Local\TaskPrioMemory.SingleInstance", out createdNew);
            if (!createdNew)
            {
                // Already running in the tray — ask that instance to show its window.
                try
                {
                    using (var wake = EventWaitHandle.OpenExisting(ShowWindowEventName))
                        wake.Set();
                }
                catch { /* first instance is mid-startup or exiting; nothing to do */ }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool startHidden = args != null &&
                               args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));

            try
            {
                Application.Run(new TrayApplicationContext(startHidden));
            }
            finally
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
    }
}
