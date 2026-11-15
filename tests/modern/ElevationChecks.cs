using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

namespace ThreeFingerWindowDrag
{
    internal static class ElevationChecks
    {
        [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr window, ref Native.WindowPlacement placement);
        internal static void TaskManager()
        {
            var existing = Process.GetProcessesByName("Taskmgr");
            bool owned = existing.Length == 0;
            using Process manager = owned ? Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Taskmgr.exe")) { UseShellExecute = true }) : existing[0];
            foreach (Process other in existing) if (other != manager) other.Dispose();
            IntPtr window = IntPtr.Zero;
            try
            {
                if (!SpinWait.SpinUntil(() => { manager.Refresh(); window = manager.MainWindowHandle; return window != IntPtr.Zero && Native.IsWindowVisible(window); }, 30000))
                    throw new Exception("Task Manager window did not appear.");
                Native.GetWindowRect(window, out Native.Rect original);
                var placement = new Native.WindowPlacement { Length = (uint)Marshal.SizeOf<Native.WindowPlacement>() };
                if (!Native.GetWindowPlacement(window, ref placement)) throw new Exception("Task Manager placement unavailable.");
                using var mover = new WindowMover { FollowCursor = false, BringToFront = false, MaximizeAtTop = false, SnapAtSides = false };
                var anchor = new Native.Point(original.Left + 180, original.Top + 140);
                bool prepared = mover.PrepareWindow(window, anchor);
                if (!ElevationSupport.IsElevated)
                {
                    if (prepared || !mover.ElevationRequired || mover.Status != ElevationSupport.RequiredMessage)
                        throw new Exception("Elevated Task Manager did not produce actionable permission guidance.");
                    Console.WriteLine("Task Manager: blocked elevated target correctly; normal Glide remains running.");
                }
                else
                {
                    if (!prepared || !mover.Capture() || !mover.Move(24, 12) || !mover.WaitForIdle(3000)) throw new Exception("Elevated Task Manager movement failed.");
                    Native.GetWindowRect(window, out Native.Rect moved);
                    if (mover.Moves != 1 || moved.Left == original.Left && moved.Top == original.Top) throw new Exception("Elevated movement did not change Task Manager bounds: " + mover.Status);
                    mover.Release();
                    if (!owned) SetWindowPlacement(window, ref placement);
                    Console.WriteLine("Task Manager: elevated Glide moved the actual window successfully.");
                }
            }
            finally
            {
                if (owned && !manager.HasExited) manager.CloseMainWindow();
            }
        }
    }
}
