using System;
using System.Runtime.InteropServices;

namespace ThreeFingerWindowDrag;

internal sealed class TrayIcon : MessageWindow, IDisposable
{
    private const int Callback = 0x8000 + 17;
    private Native.NotifyData data;
    private readonly IntPtr owner;
    private IntPtr icon;
    internal event Action Open, Toggle, Exit;
    internal bool Enabled;
    internal TrayIcon(IntPtr window)
    {
        owner = window;
        CreateHandle("Glide.Tray");
        icon = Native.LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "Glide.ico"), 1, 32, 32, 0x10);
        if (icon == IntPtr.Zero) icon = Native.LoadIcon(IntPtr.Zero, new IntPtr(32512));
        data = new Native.NotifyData {
            Size = (uint)Marshal.SizeOf<Native.NotifyData>(), Window = Handle, Id = 1,
            Flags = 1 | 2 | 4, Callback = Callback, Icon = icon, Tip = "Glide · 세 손가락 창 이동"
        };
        Native.ShellNotifyIcon(0, ref data);
    }
    protected override void WndProc(ref NativeMessage message)
    {
        if (message.Msg == Callback)
        {
            int mouse = message.LParam.ToInt32();
            if (mouse == 0x202 || mouse == 0x203) Open?.Invoke();
            else if (mouse == 0x205 || mouse == 0x7B) ShowMenu();
        }
        base.WndProc(ref message);
    }
    private void ShowMenu()
    {
        IntPtr menu = Native.CreatePopupMenu();
        Native.AppendMenu(menu, 0, new UIntPtr(1), "Glide 열기");
        Native.AppendMenu(menu, Enabled ? 8u : 0u, new UIntPtr(2), "창 이동 사용");
        Native.AppendMenu(menu, 0x800, UIntPtr.Zero, null);
        Native.AppendMenu(menu, 0, new UIntPtr(3), "종료");
        Native.GetCursorPos(out Native.Point position);
        Native.SetForegroundWindow(owner);
        uint result = Native.TrackPopupMenuEx(menu, 0x100 | 2, position.X, position.Y, owner, IntPtr.Zero);
        Native.PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
        Native.DestroyMenu(menu);
        if (result == 1) Open?.Invoke();
        else if (result == 2) Toggle?.Invoke();
        else if (result == 3) Exit?.Invoke();
    }
    public void Dispose()
    {
        Native.ShellNotifyIcon(2, ref data);
        if (icon != IntPtr.Zero) Native.DestroyIcon(icon);
        icon = IntPtr.Zero;
        DestroyHandle();
    }
}

internal static partial class Native
{
    internal delegate IntPtr SubclassProcedure(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct NotifyData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] internal static extern bool ShellNotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)] internal static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] internal static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] internal static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] internal static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] internal static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")] internal static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("comctl32.dll")] internal static extern bool SetWindowSubclass(IntPtr window, SubclassProcedure procedure, UIntPtr id, IntPtr data);
    [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(IntPtr window, SubclassProcedure procedure, UIntPtr id);
    [DllImport("comctl32.dll")] internal static extern IntPtr DefSubclassProc(IntPtr window, uint msg, IntPtr wParam, IntPtr lParam);
}
