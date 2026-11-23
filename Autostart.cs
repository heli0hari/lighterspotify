using System;
using Microsoft.Win32;

namespace LighterSpotify
{
    // "Start with Windows" via the current user's Run key (no admin needed)
    public static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "LighterSpotify";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Enable()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true))
                {
                    key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\" --minimized");
                }
            }
            catch { }
        }

        public static void Disable()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key != null && key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
                }
            }
            catch { }
        }
    }
}
