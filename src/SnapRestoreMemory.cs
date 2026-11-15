using System;
using System.Collections.Generic;
using System.Drawing;

namespace ThreeFingerWindowDrag
{
    // Input reads and the native worker writes; no window operations while holding the lock.
    internal sealed class SnapRestoreMemory
    {
        private sealed class Entry
        {
            internal uint Process, Thread;
            internal Rectangle Original, Snapped;
        }
        private readonly object gate = new object();
        private readonly Dictionary<IntPtr, Entry> windows = new Dictionary<IntPtr, Entry>();

        internal bool TryGet(IntPtr window, Rectangle current, bool maximized, out Rectangle original)
        {
            original = Rectangle.Empty;
            Entry entry;
            lock (gate) { if (!windows.TryGetValue(window, out entry)) return false; }
            uint thread = Native.GetWindowThreadProcessId(window, out uint process);
            if (thread == 0 || process != entry.Process || thread != entry.Thread || (!maximized && !Matches(current, entry.Snapped)))
            {
                lock (gate) { if (windows.TryGetValue(window, out Entry found) && ReferenceEquals(found, entry)) windows.Remove(window); }
                return false;
            }
            original = entry.Original;
            return true;
        }
        internal void Remember(IntPtr window, Rectangle original, Rectangle snapped)
        {
            if (original.Width <= 0 || original.Height <= 0 || snapped.Width <= 0 || snapped.Height <= 0) return;
            uint thread = Native.GetWindowThreadProcessId(window, out uint process);
            if (thread == 0) return;
            IntPtr[] stale;
            lock (gate) { stale = new List<IntPtr>(windows.Keys).ToArray(); }
            foreach (IntPtr handle in stale) if (!Native.IsWindow(handle)) Forget(handle);
            lock (gate) windows[window] = new Entry { Process = process, Thread = thread, Original = original, Snapped = snapped };
        }
        internal void Forget(IntPtr window) { lock (gate) windows.Remove(window); }
        private static bool Matches(Rectangle current, Rectangle saved)
        {
            return Math.Abs((long)current.Left - saved.Left) <= 2 && Math.Abs((long)current.Top - saved.Top) <= 2 &&
                Math.Abs((long)current.Right - saved.Right) <= 2 && Math.Abs((long)current.Bottom - saved.Bottom) <= 2;
        }
        internal static Rectangle UnderCursor(Rectangle current, Size restored, Point cursor)
        {
            double x = Math.Clamp((cursor.X - current.Left) / (double)current.Width, 0, 1);
            double y = Math.Clamp((cursor.Y - current.Top) / (double)current.Height, 0, 1);
            return new Rectangle(cursor.X - (int)Math.Round(restored.Width * x), cursor.Y - (int)Math.Round(restored.Height * y), restored.Width, restored.Height);
        }
    }
}
