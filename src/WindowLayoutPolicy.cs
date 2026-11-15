using System;
using System.Drawing;

namespace ThreeFingerWindowDrag
{
    internal enum WindowLayoutAction { None, Maximize, LeftHalf, RightHalf }

    internal static class WindowLayoutPolicy
    {
        internal static Rectangle Nearest(Point point, Rectangle[] areas)
        {
            Rectangle nearest = areas[0];
            double best = double.MaxValue;
            foreach (Rectangle area in areas)
            {
                double dx = Math.Max(area.Left - point.X, Math.Max(0, point.X - area.Right + 1));
                double dy = Math.Max(area.Top - point.Y, Math.Max(0, point.Y - area.Bottom + 1));
                double distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; nearest = area; }
            }
            return nearest;
        }
        internal static WindowLayoutAction AtEdge(Point point, Rectangle[] monitors, bool top, bool sides, int threshold = 10)
        {
            if (monitors == null || monitors.Length == 0) return WindowLayoutAction.None;
            Rectangle monitor = Nearest(point, monitors);
            // Top takes precedence in corners; horizontal edges require the same display's Y range.
            if (top && point.X >= monitor.Left && point.X < monitor.Right && point.Y <= monitor.Top + threshold)
                return WindowLayoutAction.Maximize;
            if (sides && point.Y >= monitor.Top && point.Y < monitor.Bottom)
            {
                if (point.X <= monitor.Left + threshold) return WindowLayoutAction.LeftHalf;
                if (point.X >= monitor.Right - 1 - threshold) return WindowLayoutAction.RightHalf;
            }
            return WindowLayoutAction.None;
        }
        internal static Rectangle Half(Rectangle workArea, WindowLayoutAction action)
        {
            int middle = workArea.Left + workArea.Width / 2;
            return action == WindowLayoutAction.LeftHalf
                ? Rectangle.FromLTRB(workArea.Left, workArea.Top, middle, workArea.Bottom)
                : Rectangle.FromLTRB(middle, workArea.Top, workArea.Right, workArea.Bottom);
        }
        internal static Rectangle IncludeInvisibleBorders(Rectangle visibleTarget, Rectangle outer, Rectangle visible)
        {
            int left = Math.Clamp(visible.Left - outer.Left, 0, 32), top = Math.Clamp(visible.Top - outer.Top, 0, 32);
            int right = Math.Clamp(outer.Right - visible.Right, 0, 32), bottom = Math.Clamp(outer.Bottom - visible.Bottom, 0, 32);
            return Rectangle.FromLTRB(visibleTarget.Left - left, visibleTarget.Top - top, visibleTarget.Right + right, visibleTarget.Bottom + bottom);
        }
    }
}
