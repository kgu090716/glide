using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ThreeFingerWindowDrag
{
    internal static class ElevationSupport
    {
        internal static readonly bool IsElevated = ReadElevation(new IntPtr(-1));
        internal const string RequiredMessage = "작업 관리자처럼 관리자 권한으로 실행된 창입니다. Glide를 관리자 권한으로 다시 시작해 주세요.";
        private static bool ReadElevation(IntPtr process)
        {
            if (!Native.OpenProcessToken(process, 0x0008, out IntPtr token)) return false;
            try { return Native.GetTokenInformation(token, 20, out int elevated, 4, out _) && elevated != 0; }
            finally { Native.CloseHandle(token); }
        }
        internal static bool RequiresElevation(IntPtr window)
        {
            if (IsElevated) return false;
            Native.GetWindowThreadProcessId(window, out uint pid);
            IntPtr process = Native.OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return false;
            try { return ReadElevation(process); }
            finally { Native.CloseHandle(process); }
        }
        internal static ProcessStartInfo RestartInfo(string executable, int previousProcess)
        {
            if (previousProcess <= 0) throw new ArgumentOutOfRangeException(nameof(previousProcess));
            string path = Path.GetFullPath(executable);
            return new ProcessStartInfo(path, "--elevated-restart " + previousProcess) {
                UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(path)
            };
        }
        // Called only after Windows has approved elevation. Cancellation leaves the current app alive.
        internal static bool CompleteHandoff(int previousProcess, string windowTitle, uint quitMessage)
        {
            if (!IsElevated || previousProcess <= 0 || previousProcess == Environment.ProcessId) return false;
            try
            {
                using Process previous = Process.GetProcessById(previousProcess);
                if (!string.Equals(previous.MainModule.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return false;
                IntPtr window = Native.FindWindow(null, windowTitle);
                Native.GetWindowThreadProcessId(window, out uint owner);
                if (window == IntPtr.Zero || owner != previousProcess) return false;
                return Native.PostMessage(window, quitMessage, IntPtr.Zero, IntPtr.Zero) && previous.WaitForExit(8000);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            { return false; }
        }
    }
    internal static partial class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool PostMessage(IntPtr window, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information, uint length, out uint required);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr filter);
    }
}
