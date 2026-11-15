using System;
using System.IO;
using Microsoft.Win32;

namespace ThreeFingerWindowDrag
{
    internal sealed class StartupRegistration
    {
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly string keyPath, valueName;
        internal StartupRegistration(string key = RunKey, string name = "Glide") { keyPath = key; valueName = name; }
        internal static string CommandFor(string executable) { return "\"" + Path.GetFullPath(executable) + "\" --tray"; }
        internal bool IsEnabled(string executable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
                return string.Equals(key == null ? null : key.GetValue(valueName) as string, CommandFor(executable), StringComparison.OrdinalIgnoreCase);
        }
        internal void SetEnabled(string executable, bool enabled)
        {
            string command = CommandFor(executable);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                if (enabled) key.SetValue(valueName, command, RegistryValueKind.String);
                else if (string.Equals(key.GetValue(valueName) as string, command, StringComparison.OrdinalIgnoreCase)) key.DeleteValue(valueName, false);
            }
        }
    }
}
