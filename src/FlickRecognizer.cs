using System;
using System.Collections.Generic;

namespace ThreeFingerWindowDrag
{
    // Physical millimetres and elapsed time: independent of cursor speed, DPI and report rate.
    internal sealed class FlickRecognizer
    {
        private struct Sample { internal long Time; internal double X, Y; }
        private readonly List<Sample> samples = new List<Sample>();
        internal const int MaximumGestureDurationMs = 350;
        private long contactStarted;
        internal void Reset() { samples.Clear(); contactStarted = 0; }
        internal void Add(long time, double x, double y)
        {
            if (samples.Count > 0 && time < samples[samples.Count - 1].Time) Reset();
            if (samples.Count == 0) contactStarted = time;
            if (time - contactStarted > MaximumGestureDurationMs) return;
            if (samples.Count > 0 && time == samples[samples.Count - 1].Time) samples.RemoveAt(samples.Count - 1);
            samples.Add(new Sample { Time = time, X = x, Y = y });
            while (samples.Count > 1 && (samples[1].Time < time - 120 || samples.Count > 128)) samples.RemoveAt(0);
        }
        internal WindowLayoutAction OnRelease(long now, int sensitivity)
        {
            if (samples.Count < 3) return WindowLayoutAction.None;
            if (now < contactStarted || now - contactStarted > MaximumGestureDurationMs) return WindowLayoutAction.None;
            Sample first = samples[0], last = samples[samples.Count - 1];
            long duration = last.Time - first.Time;
            if (now < last.Time || now - last.Time > 90 || duration < 35 || duration > 180) return WindowLayoutAction.None;
            double dx = last.X - first.X, dy = last.Y - first.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy), path = 0;
            for (int i = 1; i < samples.Count; i++)
            {
                double x = samples[i].X - samples[i - 1].X, y = samples[i].Y - samples[i - 1].Y;
                path += Math.Sqrt(x * x + y * y);
            }
            int level = Math.Clamp(sensitivity, 1, 10);
            if (distance < 13 - 0.8 * level || distance * 1000 / duration < 260 - 20 * level || distance < path * 0.82)
                return WindowLayoutAction.None;
            // Include upward diagonals up to 45 degrees; horizontal flicks keep their narrower cone.
            if (dy < 0 && -dy >= Math.Abs(dx)) return WindowLayoutAction.Maximize;
            if (Math.Abs(dx) >= Math.Abs(dy) * 1.5) return dx < 0 ? WindowLayoutAction.LeftHalf : WindowLayoutAction.RightHalf;
            return WindowLayoutAction.None;
        }
    }
}
