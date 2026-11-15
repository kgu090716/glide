using System;
using System.Drawing;

namespace ThreeFingerWindowDrag
{
    [Flags] internal enum ResizeEdges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }
    internal enum ResizeBehavior { Grow, LowerCorners }
    internal static class ResizeTiming
    {
        internal static double NormalizeSeconds(double seconds)
        {
            return double.IsFinite(seconds) ? Math.Round(Math.Clamp(seconds, 0.2, 10), 1, MidpointRounding.AwayFromZero) : 1;
        }
    }
    internal sealed class DirectionalResizeOptions
    {
        internal bool Enabled = true;
        internal int HoldMilliseconds = 1000;
        internal ResizeBehavior Behavior = ResizeBehavior.Grow;
        internal DirectionalResizeOptions Snapshot()
        {
            return new DirectionalResizeOptions {
                Enabled = Enabled,
                HoldMilliseconds = Math.Clamp(HoldMilliseconds, 200, 10000),
                Behavior = Enum.IsDefined(Behavior) ? Behavior : ResizeBehavior.Grow
            };
        }
    }
    internal static class DirectionalResizeGeometry
    {
        internal static Rectangle Apply(Rectangle original, int dx, int dy, ResizeEdges edges, Size minimum, Size maximum)
        {
            int left = original.Left, top = original.Top, right = original.Right, bottom = original.Bottom;
            if ((edges & ResizeEdges.Left) != 0) left = right - (int)Math.Clamp((long)original.Width - dx, minimum.Width, maximum.Width);
            else if ((edges & ResizeEdges.Right) != 0) right = left + (int)Math.Clamp((long)original.Width + dx, minimum.Width, maximum.Width);
            if ((edges & ResizeEdges.Top) != 0) top = bottom - (int)Math.Clamp((long)original.Height - dy, minimum.Height, maximum.Height);
            else if ((edges & ResizeEdges.Bottom) != 0) bottom = top + (int)Math.Clamp((long)original.Height + dy, minimum.Height, maximum.Height);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }
}
