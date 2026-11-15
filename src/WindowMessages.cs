using System;
using System.ComponentModel;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ThreeFingerWindowDrag
{
    internal struct NativeMessage
    {
        internal IntPtr Window, WParam, LParam, Result;
        internal int Msg;
    }
    // A message-only HWND on WinUI's dispatcher thread. No Windows Forms dependency.
    internal abstract class MessageWindow
    {
        private Native.WindowProcedure procedure;
        private string className;
        internal IntPtr Handle { get; private set; }
        protected void CreateHandle(string title)
        {
            className = title + "." + Guid.NewGuid().ToString("N");
            procedure = Dispatch;
            Native.WindowClass wc = new Native.WindowClass {
                Size = (uint)Marshal.SizeOf(typeof(Native.WindowClass)), Procedure = procedure,
                Instance = Native.GetModuleHandle(null), Name = className
            };
            if (Native.RegisterClassEx(ref wc) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Handle = Native.CreateWindowEx(0, className, title, 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wc.Instance, IntPtr.Zero);
            if (Handle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Native.UnregisterClass(className, wc.Instance);
                throw new Win32Exception(error);
            }
        }
        private IntPtr Dispatch(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            NativeMessage message = new NativeMessage { Window = hwnd, Msg = (int)msg, WParam = wParam, LParam = lParam };
            WndProc(ref message);
            return message.Result;
        }
        protected virtual void WndProc(ref NativeMessage message)
        { message.Result = Native.DefWindowProc(message.Window, (uint)message.Msg, message.WParam, message.LParam); }
        protected void DestroyHandle()
        {
            if (Handle == IntPtr.Zero) return;
            Native.DestroyWindow(Handle); Handle = IntPtr.Zero;
            Native.UnregisterClass(className, Native.GetModuleHandle(null));
            procedure = null;
        }
    }
    internal static class MonitorLayout
    {
        internal static Rectangle ForPoint(Point point, bool work)
        {
            Native.MonitorInfo info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(Native.MonitorInfo)) };
            if (!Native.GetMonitorInfo(Native.MonitorFromPoint(new Native.Point(point.X, point.Y), 2), ref info))
                return WindowLayoutPolicy.Nearest(point, work ? WorkAreas() : Bounds());
            Native.Rect r = work ? info.Work : info.Monitor;
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
        internal static Rectangle ForWindow(IntPtr window, bool work)
        {
            Native.MonitorInfo info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(Native.MonitorInfo)) };
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(window, 2), ref info)) return new Rectangle(0, 0, 1920, 1080);
            Native.Rect r = work ? info.Work : info.Monitor;
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
        internal static Rectangle[] WorkAreas()
        { return Areas(true); }
        internal static Rectangle[] Bounds()
        { return Areas(false); }
        private static Rectangle[] Areas(bool work)
        {
            List<Rectangle> areas = new List<Rectangle>();
            Native.MonitorProcedure callback = delegate(IntPtr monitor, IntPtr dc, ref Native.Rect rect, IntPtr data) {
                Native.MonitorInfo info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(Native.MonitorInfo)) };
                if (Native.GetMonitorInfo(monitor, ref info)) {
                    Native.Rect r = work ? info.Work : info.Monitor;
                    areas.Add(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
                }
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return areas.Count > 0 ? areas.ToArray() : new[] { new Rectangle(0, 0, 1920, 1080) };
        }
    }
    internal static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct PumpMessage
        {
            public IntPtr Window;
            public uint Message;
            public IntPtr WParam, LParam;
            public uint Time;
            public Point Location;
            public uint Private;
        }
        [DllImport("user32.dll", EntryPoint = "GetMessageW")] internal static extern int GetMessage(out PumpMessage message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref PumpMessage message);
        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] internal static extern IntPtr DispatchMessage(ref PumpMessage message);
        [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] internal static extern bool PostThreadMessage(uint thread, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern UIntPtr SetTimer(IntPtr window, UIntPtr id, uint interval, IntPtr callback);
        [DllImport("user32.dll")] internal static extern bool KillTimer(IntPtr window, UIntPtr id);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        internal delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        internal delegate bool MonitorProcedure(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass
        {
            public uint Size, Style;
            public WindowProcedure Procedure;
            public int ClassExtra, WindowExtra;
            public IntPtr Instance, Icon, Cursor, Background;
            public string Menu, Name;
            public IntPtr SmallIcon;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string module);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WindowClass wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool UnregisterClass(string name, IntPtr instance);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProcedure callback, IntPtr data);
    }
}
