using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ThreeFingerWindowDrag
{
    internal static partial class Native
    {
        internal const int HidSuccess = 0x00110000;
        internal const uint RidInput = 0x10000003, RidiPreparsed = 0x20000005, RidiName = 0x20000007;
        internal const int WmInput = 0x00FF, WmDeviceChange = 0x00FE, WmHotkey = 0x0312;
        internal const uint InputSink = 0x100, DeviceNotify = 0x2000;

        [StructLayout(LayoutKind.Sequential)] internal struct RawDevice
        { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceList
        { public IntPtr Handle; public uint Type; }
        [StructLayout(LayoutKind.Sequential)] internal struct RawHeader
        { public uint Type, Size; public IntPtr Device, WParam; }
        [StructLayout(LayoutKind.Sequential)] internal struct HidData
        { public ushort Index, Reserved; public uint Value; }

        // Fixed Windows HID ABI layouts. BOOLEAN is one byte, not a Win32 BOOL.
        [StructLayout(LayoutKind.Explicit, Size = 64)] internal struct HidCaps
        {
            [FieldOffset(0)] public ushort Usage;
            [FieldOffset(2)] public ushort Page;
            [FieldOffset(4)] public ushort InputLength;
            [FieldOffset(44)] public ushort LinkCount;
            [FieldOffset(46)] public ushort ButtonCount;
            [FieldOffset(48)] public ushort ValueCount;
            [FieldOffset(50)] public ushort DataCount;
        }
        [StructLayout(LayoutKind.Explicit, Size = 72)] internal struct ValueCaps
        {
            [FieldOffset(0)] public ushort Page;
            [FieldOffset(2)] public byte Report;
            [FieldOffset(6)] public ushort Link;
            [FieldOffset(12)] public byte IsRange;
            [FieldOffset(18)] public ushort BitSize;
            [FieldOffset(20)] public ushort ReportCount;
            [FieldOffset(32)] public uint UnitsExp;
            [FieldOffset(36)] public uint Units;
            [FieldOffset(40)] public int LogicalMin;
            [FieldOffset(44)] public int LogicalMax;
            [FieldOffset(48)] public int PhysicalMin;
            [FieldOffset(52)] public int PhysicalMax;
            [FieldOffset(56)] public ushort UsageMin;
            [FieldOffset(58)] public ushort UsageMax;
            [FieldOffset(68)] public ushort DataMin;
            [FieldOffset(70)] public ushort DataMax;
        }
        [StructLayout(LayoutKind.Explicit, Size = 72)] internal struct ButtonCaps
        {
            [FieldOffset(0)] public ushort Page;
            [FieldOffset(2)] public byte Report;
            [FieldOffset(6)] public ushort Link;
            [FieldOffset(12)] public byte IsRange;
            [FieldOffset(56)] public ushort UsageMin;
            [FieldOffset(58)] public ushort UsageMax;
            [FieldOffset(68)] public ushort DataMin;
            [FieldOffset(70)] public ushort DataMax;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct LinkNode
        {
            public ushort Usage, Page, Parent, ChildCount, NextSibling, FirstChild;
            public uint Flags;
            public IntPtr Context;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Point
        { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct WindowPlacement
        {
            public uint Length, Flags, Show;
            public Point Min, Max;
            public Rect Normal;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo
        { public uint Size; public Rect Monitor, Work; public uint Flags; }

        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetRawInputDeviceList([In, Out] DeviceList[] devices, ref uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)] internal static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint GetRawInputDeviceName(IntPtr device, uint command, StringBuilder data, ref uint size);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
        [DllImport("hid.dll")] internal static extern int HidP_GetValueCaps(int type, [Out] ValueCaps[] caps, ref ushort count, IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetButtonCaps(int type, [Out] ButtonCaps[] caps, ref ushort count, IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetLinkCollectionNodes([Out] LinkNode[] nodes, ref uint count, IntPtr data);
        [DllImport("hid.dll")] internal static extern uint HidP_MaxDataListLength(int type, IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetData(int type, [Out] HidData[] list, ref uint count, IntPtr data, byte[] report, uint length);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);
        [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out Rect rect, uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    }
}
