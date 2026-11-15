using System;
using System.Drawing;

namespace ThreeFingerWindowDrag
{
    internal static class TopEdgePolicy
    {
        internal static bool ShouldMaximize(bool enabled, Point projectedCursor, Rectangle[] monitors, int threshold = 10)
        {
            return WindowLayoutPolicy.AtEdge(projectedCursor, monitors, enabled, false, threshold) == WindowLayoutAction.Maximize;
        }
    }
}
