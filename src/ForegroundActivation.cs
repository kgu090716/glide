using System;
using System.Runtime.InteropServices;

namespace ThreeFingerWindowDrag
{
    internal static class ForegroundActivation
    {
        internal static void Raise(IntPtr window)
        {
            if (Native.GetForegroundWindow() == window) return;
            Native.BringWindowToTop(window);
            Native.SetForegroundWindow(window);
            if (Native.GetForegroundWindow() == window) return;
            IntPtr foreground = Native.GetForegroundWindow();
            // Attaching queues resets their key state; do not do so during modifier shortcuts.
            if ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x11) & 0x8000) != 0 ||
                (Native.GetAsyncKeyState(0x12) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0) return;
            // Never attach the gesture/input thread or wait on an unresponsive foreground app.
            if ((foreground != IntPtr.Zero && !Responsive(foreground)) || !Responsive(window)) return;
            Native.PeekMessage(out Native.PumpMessage unused, IntPtr.Zero, 0, 0, 0);
            uint current = Native.GetCurrentThreadId();
            uint foregroundThread = foreground == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(foreground, out _);
            uint targetThread = Native.GetWindowThreadProcessId(window, out _);
            bool attachedForeground = false, attachedTarget = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != current)
                    attachedForeground = Native.AttachThreadInput(current, foregroundThread, true);
                if (targetThread != 0 && targetThread != current && targetThread != foregroundThread)
                    attachedTarget = Native.AttachThreadInput(current, targetThread, true);
                Native.BringWindowToTop(window);
                Native.SetForegroundWindow(window);
            }
            finally
            {
                if (attachedTarget) Native.AttachThreadInput(current, targetThread, false);
                if (attachedForeground) Native.AttachThreadInput(current, foregroundThread, false);
            }
        }
        private static bool Responsive(IntPtr window)
        { return Native.SendMessageTimeout(window, 0, IntPtr.Zero, IntPtr.Zero, 0x23, 40, out _) != IntPtr.Zero; }
    }
    internal static partial class Native
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool AttachThreadInput(uint source, uint target, bool attach);
        [DllImport("user32.dll", EntryPoint = "PeekMessageW")] internal static extern bool PeekMessage(out PumpMessage message, IntPtr window, uint min, uint max, uint remove);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] internal static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    }
}
