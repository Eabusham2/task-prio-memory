using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TaskPrioMemory.UI;

namespace TaskPrioMemory
{
    internal static class Program
    {
        // Per-user single-instance guard.
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            _mutex = new Mutex(true, @"Local\TaskPrioMemory.SingleInstance", out createdNew);
            if (!createdNew)
            {
                // Already running in the tray — nothing to do.
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
