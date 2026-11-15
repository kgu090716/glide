using System;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ThreeFingerWindowDrag
{
    internal static class FeatureChecks
    {
        private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        private static void Require(bool value, string error) { if (!value) throw new Exception(error); }
        internal static void ShellWindowsAreExcluded()
        {
            Require(WindowPolicy.IsShellSurface("Windows.UI.Core.CoreWindow", "StartMenuExperienceHost.exe"), "Start menu allowed");
            Require(WindowPolicy.IsShellSurface("Windows.UI.Core.CoreWindow", "SearchHost.exe"), "Search allowed");
            Require(WindowPolicy.IsShellSurface("Windows.UI.Core.CoreWindow", "ShellExperienceHost.exe"), "Shell panel allowed");
            Require(WindowPolicy.IsShellSurface("XamlExplorerHostIslandWindow", "explorer.exe"), "Explorer shell island allowed");
            Require(!WindowPolicy.IsShellSurface("CabinetWClass", "explorer.exe"), "File Explorer excluded");
            Require(!WindowPolicy.IsShellSurface("Windows.UI.Core.CoreWindow", "ordinary-uwp.exe"), "UWP application excluded");
            Require(!WindowPolicy.IsShellSurface("WinUIDesktopWin32WindowClass", "ThreeFingerWindowDrag.exe"), "WinUI app excluded");
        }
        internal static void TopEdgeGeometry()
        {
            Rectangle[] monitors = { new Rectangle(0, 0, 2560, 1440), new Rectangle(-1920, -1080, 1920, 1080) };
            Require(TopEdgePolicy.ShouldMaximize(true, new Point(800, 2), monitors), "top edge not armed");
            Require(!TopEdgePolicy.ShouldMaximize(false, new Point(800, 2), monitors), "disabled option still armed");
            Require(!TopEdgePolicy.ShouldMaximize(true, new Point(800, 150), monitors), "ordinary drag armed");
            Require(!TopEdgePolicy.ShouldMaximize(true, new Point(-800, -400), monitors), "wrong monitor top selected");
            Require(TopEdgePolicy.ShouldMaximize(true, new Point(-800, -1075), monitors), "negative monitor top not armed");
            Require(!TopEdgePolicy.ShouldMaximize(true, new Point(3000, 0), monitors), "outside horizontal range armed");
        }
        internal static void InstalledSettingsPath()
        {
            string root = Path.GetFullPath(AppContext.BaseDirectory);
            string local = Path.Combine(root, "fake-local-data");
            Require(Settings.ResolvePath(root, local, false) == Path.Combine(root, "settings.ini"), "portable path changed");
            Require(Settings.ResolvePath(root, local, true) == Path.Combine(local, "Glide", "settings.ini"), "installed path not isolated");
            Require(StartupRegistration.CommandFor(Path.Combine(root, "folder with spaces", "Glide.exe")).EndsWith("Glide.exe\" --tray"), "startup command quoting");
        }
        internal static void StartupRegistryRoundtrip()
        {
            string key = @"Software\Glide\Tests\" + Guid.NewGuid().ToString("N");
            string app = Path.Combine(AppContext.BaseDirectory, "space path", "Glide.exe");
            var startup = new StartupRegistration(key);
            try
            {
                Require(!startup.IsEnabled(app), "startup initially enabled");
                startup.SetEnabled(app, true);
                Require(startup.IsEnabled(app), "enable failed");
                startup.SetEnabled(Path.Combine(AppContext.BaseDirectory, "other.exe"), false);
                Require(startup.IsEnabled(app), "another copy removed startup");
                startup.SetEnabled(app, false);
                Require(!startup.IsEnabled(app), "disable failed");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
        }
        internal static void NativeTopMaximize()
        {
            Require(Native.GetCursorPos(out Native.Point saved), "read cursor");
            try
            {
                using var target = new LatencyChecks.SlowWindow(true);
                using var mover = new WindowMover { FollowCursor = true, MaximizeAtTop = true };
                Native.GetWindowRect(target.Handle, out Native.Rect rect);
                Rectangle screen = MonitorLayout.ForWindow(target.Handle, false);
                var cursor = new Native.Point(rect.Left + 180, rect.Top + 140);
                Require(Native.SetCursorPos(cursor.X, cursor.Y), "position cursor");
                Require(mover.PrepareWindow(target.Handle, cursor) && mover.Capture(), "prepare top drag");
                Require(mover.Move(0, screen.Top + 1 - cursor.Y) && mover.WaitForIdle(2000), "move to top");
                Require(mover.TopEdgeArmed && !Native.IsZoomed(target.Handle), "must arm before releasing");
                mover.Finish(); Require(mover.WaitForIdle(2000) && Native.IsZoomed(target.Handle), "release did not maximize");
                Native.ShowWindow(target.Handle, 9);
                mover.MaximizeAtTop = false;
                Native.GetWindowRect(target.Handle, out rect);
                cursor = new Native.Point(rect.Left + 180, rect.Top + 140);
                Native.SetCursorPos(cursor.X, cursor.Y);
                Require(mover.PrepareWindow(target.Handle, cursor) && mover.Capture(), "prepare disabled option");
                mover.Move(0, screen.Top + 1 - cursor.Y); Require(mover.WaitForIdle(2000), "disabled move");
                Require(!mover.TopEdgeArmed, "disabled top edge armed");
                mover.Finish(); Require(mover.WaitForIdle(2000) && !Native.IsZoomed(target.Handle), "disabled option maximized");
                mover.MaximizeAtTop = true;
                Native.GetWindowRect(target.Handle, out rect);
                cursor = new Native.Point(rect.Left + 180, rect.Top + 140);
                Native.SetCursorPos(cursor.X, cursor.Y);
                Require(mover.PrepareWindow(target.Handle, cursor) && mover.Capture(), "prepare cancellation");
                mover.Move(0, screen.Top + 1 - cursor.Y); Require(mover.WaitForIdle(2000) && mover.TopEdgeArmed, "cancel arm");
                mover.Release(); Require(mover.WaitForIdle(2000) && !Native.IsZoomed(target.Handle), "cancel maximized");
            }
            finally { Native.SetCursorPos(saved.X, saved.Y); }
        }
        internal static void RealShellWindowCheck()
        {
            int found = 0;
            EnumWindowCallback callback = (window, _) => {
                Native.GetWindowThreadProcessId(window, out uint pid);
                string name;
                try { using var process = Process.GetProcessById((int)pid); name = process.ProcessName + ".exe"; }
                catch { return true; }
                if (WindowPolicy.IsShellSurface("", name))
                {
                    Require(!WindowPolicy.CanMove(window), "real shell process accepted: " + name);
                    using var mover = new WindowMover();
                    Require(!mover.PrepareWindow(window, new Native.Point(0,0)), "real shell window prepared: " + name);
                    Console.WriteLine("  Protected shell: " + name);
                    found++;
                }
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            Require(found > 0, "No real shell windows found to verify.");
        }
    }
}
