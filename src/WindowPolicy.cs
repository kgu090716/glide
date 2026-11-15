using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ThreeFingerWindowDrag
{
    internal static class WindowPolicy
    {
        internal static bool IsShellSurface(string className, string executable)
        {
            switch ((executable ?? "").ToLowerInvariant())
            {
                case "startmenuexperiencehost.exe": case "searchhost.exe": case "searchapp.exe":
                case "shellexperiencehost.exe": case "textinputhost.exe": case "lockapp.exe":
                case "widgets.exe": case "widgetservice.exe": case "shellexperience.exe":
                    return true;
            }
            switch (className ?? "")
            {
                case "Progman": case "WorkerW": case "Shell_TrayWnd": case "Shell_SecondaryTrayWnd":
                case "#32768": case "tooltips_class32": case "DV2ControlHost":
                case "ImmersiveLauncher": case "NotifyIconOverflowWindow": case "MultitaskingViewFrame":
                case "XamlExplorerHostIslandWindow": case "TopLevelWindowForOverflowXamlIsland":
                    return true;
            }
            // Explorer windows are ordinary movable apps; generic CoreWindow is also used by UWP apps.
            return false;
        }
        internal static bool CanMove(IntPtr window)
        {
            var name = new StringBuilder(256);
            Native.GetClassName(window, name, name.Capacity);
            if (IsShellSurface(name.ToString(), null)) return false;
            Native.GetWindowThreadProcessId(window, out uint pid);
            IntPtr process = Native.OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return false;
            try
            {
                var path = new StringBuilder(512);
                uint size = (uint)path.Capacity;
                if (!Native.QueryFullProcessImageName(process, 0, path, ref size))
                {
                    if (Marshal.GetLastWin32Error() != 122) return false;
                    path = new StringBuilder(32768); size = (uint)path.Capacity;
                    if (!Native.QueryFullProcessImageName(process, 0, path, ref size)) return false;
                }
                return !IsShellSurface(name.ToString(), Path.GetFileName(path.ToString()));
            }
            finally { Native.CloseHandle(process); }
        }
    }
    internal static partial class Native
    {
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    }
}
