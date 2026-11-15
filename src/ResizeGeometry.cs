using System;
using System.Collections.Generic;
using System.Drawing;

namespace ThreeFingerWindowDrag
{
    internal static class ResizeGeometry
    {
        internal static double FingerSpan(IList<Contact> contacts)
        {
            double x = 0, y = 0, squared = 0;
            foreach (Contact c in contacts) { x += c.X; y += c.Y; }
            x /= contacts.Count; y /= contacts.Count;
            foreach (Contact c in contacts) squared += (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y);
            return Math.Sqrt(squared / contacts.Count);
        }
        internal static Rectangle ScaleAroundAnchor(Rectangle original, Point anchor, double scale, Size minimum, Size maximum)
        {
            if (original.Width <= 0 || original.Height <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) return original;
            double lower = Math.Max(minimum.Width / (double)original.Width, minimum.Height / (double)original.Height);
            double upper = Math.Max(lower, Math.Min(maximum.Width / (double)original.Width, maximum.Height / (double)original.Height));
            scale = Math.Max(lower, Math.Min(upper, scale));
            int width = (int)Math.Round(original.Width * scale), height = (int)Math.Round(original.Height * scale);
            double ax = Math.Max(0, Math.Min(1, (anchor.X - original.Left) / (double)original.Width));
            double ay = Math.Max(0, Math.Min(1, (anchor.Y - original.Top) / (double)original.Height));
            return new Rectangle(anchor.X - (int)Math.Round(width * ax), anchor.Y - (int)Math.Round(height * ay), width, height);
        }
    }
}
