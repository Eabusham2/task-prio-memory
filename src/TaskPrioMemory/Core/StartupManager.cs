using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskPrioMemory.Core
{
    /// <summary>
    /// Manages the "run at Windows startup" option via the current-user Run key.
    /// HKCU does not require administrator rights and launches the app silently
    /// into the tray at logon.
    /// </summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TaskPrioMemory";

        private static string ExePath => Application.ExecutablePath;

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    var val = key?.GetValue(ValueName) as string;
                    return !string.IsNullOrEmpty(val);
                }
            }
            catch { return false; }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                                 ?? Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return false;
                    if (enabled)
                        key.SetValue(ValueName, "\"" + ExePath + "\" --tray");
                    else if (key.GetValue(ValueName) != null)
                        key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not update startup setting:\n" + ex.Message,
                    "TaskPrioMemory", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
    }
}
