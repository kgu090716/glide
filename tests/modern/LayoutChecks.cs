using System;
using System.Drawing;
using System.IO;

namespace ThreeFingerWindowDrag
{
    internal static class LayoutChecks
    {
        private static void Require(bool value, string error) { if (!value) throw new Exception(error); }
        internal static void EdgesAndDisplays()
        {
            Rectangle[] screens = { new Rectangle(-1920, -200, 1920, 1080), new Rectangle(0, 0, 2560, 1440) };
            Require(WindowLayoutPolicy.AtEdge(new Point(-1919, 200), screens, true, true) == WindowLayoutAction.LeftHalf, "negative display left");
            Require(WindowLayoutPolicy.AtEdge(new Point(-1, 200), screens, true, true) == WindowLayoutAction.RightHalf, "negative display right");
            Require(WindowLayoutPolicy.AtEdge(new Point(1, 200), screens, true, true) == WindowLayoutAction.LeftHalf, "display seam left");
            Require(WindowLayoutPolicy.AtEdge(new Point(2558, 200), screens, true, true) == WindowLayoutAction.RightHalf, "right edge");
            Require(WindowLayoutPolicy.AtEdge(new Point(2558, 200), screens, true, false) == WindowLayoutAction.None, "disabled side");
            Require(WindowLayoutPolicy.AtEdge(new Point(500, 400), screens, true, true) == WindowLayoutAction.None, "middle snapped");
            Require(WindowLayoutPolicy.AtEdge(new Point(1, 1), screens, true, true) == WindowLayoutAction.Maximize, "corner priority");
            Require(WindowLayoutPolicy.AtEdge(new Point(1, 1), screens, false, true) == WindowLayoutAction.LeftHalf, "side independent of top");
            Require(WindowLayoutPolicy.AtEdge(new Point(2559, 1600), screens, false, true) == WindowLayoutAction.None, "wrong display vertical range");
        }
        internal static void HalfGeometry()
        {
            var work = new Rectangle(-1919, -160, 1919, 1040);
            Rectangle left = WindowLayoutPolicy.Half(work, WindowLayoutAction.LeftHalf), right = WindowLayoutPolicy.Half(work, WindowLayoutAction.RightHalf);
            Require(left.Left == work.Left && right.Right == work.Right && left.Right == right.Left && left.Height == work.Height, "odd/negative work area gap");
            Rectangle padded = WindowLayoutPolicy.IncludeInvisibleBorders(left, new Rectangle(0, 0, 500, 300), new Rectangle(8, 0, 484, 292));
            Require(padded.Left == left.Left - 8 && padded.Right == left.Right + 8 && padded.Top == left.Top && padded.Bottom == left.Bottom + 8, "invisible border compensation");
        }
        private static FlickRecognizer Stroke(double x, double y, int step = 10, int duration = 100)
        {
            var flick = new FlickRecognizer();
            for (int t = 0; t <= duration; t += step) flick.Add(t, x * t / duration, y * t / duration);
            return flick;
        }
        internal static void FlickDirectionsAndReportRates()
        {
            foreach (int step in new[] { 5, 10, 20 })
            {
                Require(Stroke(0, -22, step).OnRelease(105, 5) == WindowLayoutAction.Maximize, "up/report rate");
                Require(Stroke(-22, 1, step).OnRelease(105, 5) == WindowLayoutAction.LeftHalf, "left/report rate");
                Require(Stroke(22, -1, step).OnRelease(105, 5) == WindowLayoutAction.RightHalf, "right/report rate");
            }
        }
        internal static void SensitivityAndRejections()
        {
            Require(Stroke(12, 0).OnRelease(105, 2) == WindowLayoutAction.None, "low sensitivity too easy");
            Require(Stroke(12, 0).OnRelease(105, 9) == WindowLayoutAction.RightHalf, "high sensitivity not easier");
            Require(Stroke(4, 0).OnRelease(105, 10) == WindowLayoutAction.None, "jitter triggered");
            Require(Stroke(0, 22).OnRelease(105, 10) == WindowLayoutAction.None, "down triggered");
            Require(Stroke(22, 22).OnRelease(105, 10) == WindowLayoutAction.None, "diagonal ambiguous");
            Require(Stroke(22, 0).OnRelease(230, 10) == WindowLayoutAction.None, "stale flick");
            var stopped = Stroke(22, 0); for (int t = 110; t <= 250; t += 10) stopped.Add(t, 22, 0);
            Require(stopped.OnRelease(255, 10) == WindowLayoutAction.None, "pause before release triggered");
            var reverse = new FlickRecognizer(); reverse.Add(0, 0, 0); reverse.Add(30, 20, 0); reverse.Add(60, 0, 0); reverse.Add(100, 20, 0);
            Require(reverse.OnRelease(105, 10) == WindowLayoutAction.None, "back-and-forth triggered");
            Require(Stroke(22, 0, 10, 20).OnRelease(25, 10) == WindowLayoutAction.None, "single spike triggered");
        }
        internal static void UpwardDiagonals()
        {
            foreach (int step in new[] { 4, 8, 10, 20 })
            foreach (int sign in new[] { -1, 1 })
            {
                Require(Stroke(sign * 18, -22, step).OnRelease(105, 5) == WindowLayoutAction.Maximize, "slanted upward flick rejected");
                Require(Stroke(sign * 22, -22, step).OnRelease(105, 5) == WindowLayoutAction.Maximize, "45-degree upward flick rejected");
                Require(Stroke(sign * 22, -10, step).OnRelease(105, 5) == (sign < 0 ? WindowLayoutAction.LeftHalf : WindowLayoutAction.RightHalf), "horizontal flick became maximize");
                Require(Stroke(sign * 22, 22, step).OnRelease(105, 10) == WindowLayoutAction.None, "down diagonal triggered");
            }
            var mover = new FakeMover(); var engine = new GestureEngine(mover);
            for (int t = 0; t <= 100; t += 10) engine.Process(Frame(t * .18, -t * .22), t, false);
            Require(mover.Finishes == 0, "diagonal maximized before release");
            engine.Process(Frame(18, -22, 1, 2), 105, false);
            Require(mover.Action == WindowLayoutAction.Maximize, "diagonal release did not maximize");
            int releases = mover.Releases;
            engine.Process(Frame(18, -22, 1, 1), 110, false); engine.Process(Frame(18, -22, 1, 0), 115, false);
            Require(mover.Releases == releases, "lift continuation cancelled the queued layout");
        }
        private static ContactFrame Frame(double x, double y, double scale = 1, int count = 3)
        {
            var frame = new ContactFrame { Device = new IntPtr(1) };
            for (int i = 0; i < count; i++) frame.Contacts.Add(new Contact { Id = i, X = 40 + x + (i - 1) * 10 * scale, Y = 30 + y, Tip = true, Confident = true });
            return frame;
        }
        internal static void GestureReleaseAndToggle()
        {
            foreach (bool enabled in new[] { true, false })
            foreach (int speed in new[] { 3, 36 })
            {
                var mover = new FakeMover(); var engine = new GestureEngine(mover) { FlickGesturesEnabled = enabled, PixelsPerMm = speed };
                for (int t = 0; t <= 100; t += 10) engine.Process(Frame(t * .22, 0), t, false);
                Require(mover.Finishes == 0, "flick happened before lift");
                engine.Process(Frame(22, 0, 1, 2), 105, false);
                Require(mover.Finishes == 1 && mover.Action == (enabled ? WindowLayoutAction.RightHalf : WindowLayoutAction.None), "toggle/release/speed independence");
            }
        }
        internal static void ResizeAndCancelNeverFlick()
        {
            var mover = new FakeMover(); var engine = new GestureEngine(mover);
            engine.Process(Frame(0, 0), 0, false); engine.Process(Frame(0, 0, 1.2), 10, false);
            for (int t = 20; t <= 100; t += 10) engine.Process(Frame(t * .22, 0, 1.2), t, false);
            engine.Process(Frame(22, 0, 1.2, 2), 105, false);
            Require(mover.Resizes > 0 && mover.Action == WindowLayoutAction.None, "resize flicked");
            foreach (int cancel in new[] { 0, 1, 2 })
            {
                mover = new FakeMover(); engine = new GestureEngine(mover);
                for (int t = 0; t <= 100; t += 10) engine.Process(Frame(t * .22, 0), t, false);
                if (cancel == 0) engine.Process(Frame(22, 0), 105, true);
                else if (cancel == 1) engine.Process(Frame(22, 0, 1, 4), 105, false);
                else engine.Tick(600);
                engine.Process(Frame(22, 0, 1, 0), 610, false);
                Require(mover.Finishes == 0, "cancelled gesture dispatched layout");
            }
        }
        internal static void SettingsRoundtrip()
        {
            string path = Settings.FilePath;
            byte[] backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                new Settings { SnapAtSides = false, FlickGestures = false, FlickSensitivity = 9, Speed = 26 }.Save();
                Settings loaded = Settings.Load();
                Require(!loaded.SnapAtSides && !loaded.FlickGestures && loaded.FlickSensitivity == 9 && loaded.Speed == 26, "settings not persisted");
                File.WriteAllText(path, "Speed=26\nFlickSensitivity=999\n"); loaded = Settings.Load();
                Require(loaded.SnapAtSides && loaded.FlickGestures && loaded.FlickSensitivity == 10 && loaded.Speed == 26, "old settings/default/clamp");
            }
            finally { if (backup == null) File.Delete(path); else File.WriteAllBytes(path, backup); }
        }
        private static void AssertHalf(IntPtr window, Rectangle work, WindowLayoutAction action)
        {
            Require(Native.GetWindowRect(window, out Native.Rect outer), "read half rect");
            Rectangle actual = Rectangle.FromLTRB(outer.Left, outer.Top, outer.Right, outer.Bottom);
            if (Native.DwmGetWindowAttribute(window, 9, out Native.Rect frame, 16) == 0 && frame.Right > frame.Left)
                actual = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
            Rectangle expected = WindowLayoutPolicy.Half(work, action);
            Require(Math.Abs(actual.Left - expected.Left) <= 2 && Math.Abs(actual.Right - expected.Right) <= 2 &&
                Math.Abs(actual.Top - expected.Top) <= 2 && Math.Abs(actual.Bottom - expected.Bottom) <= 2, "half placement differs: " + actual + " vs " + expected);
        }
        internal static void NativeSideSnapAndCancel()
        {
            using var target = new LatencyChecks.SlowWindow(true);
            using var mover = new WindowMover { FollowCursor = false, MaximizeAtTop = false, BringToFront = false };
            Rectangle screen = MonitorLayout.ForWindow(target.Handle, false), work = MonitorLayout.ForWindow(target.Handle, true);
            foreach (WindowLayoutAction action in new[] { WindowLayoutAction.LeftHalf, WindowLayoutAction.RightHalf })
            {
                var anchor = new Native.Point(work.Left + work.Width / 2, work.Top + work.Height / 2);
                Require(mover.PrepareWindow(target.Handle, anchor) && mover.Capture(), "capture for edge");
                Native.GetWindowRect(target.Handle, out Native.Rect before);
                int dx = (action == WindowLayoutAction.LeftHalf ? screen.Left : screen.Right - 1) - (before.Left + (before.Right - before.Left) / 2);
                Require(mover.Move(dx, 0) && mover.WaitForIdle(2000) && mover.EdgeAction == action, "arm side edge");
                mover.Finish(); Require(mover.WaitForIdle(2000), "snap idle"); AssertHalf(target.Handle, work, action);
            }
            mover.SnapAtSides = false;
            var point = new Native.Point(work.Left + 150, work.Top + 160);
            Require(mover.PrepareWindow(target.Handle, point) && mover.Capture(), "disabled capture");
            Native.GetWindowRect(target.Handle, out Native.Rect rect);
            mover.Move(screen.Left - (rect.Left + (rect.Right - rect.Left) / 2), 0); Require(mover.WaitForIdle(2000), "disabled move");
            Require(mover.EdgeAction == WindowLayoutAction.None, "disabled side armed");
            mover.SnapAtSides = true;
            mover.Move(screen.Left - (rect.Left + (rect.Right - rect.Left) / 2), 0); Require(mover.WaitForIdle(2000), "arm cancel");
            Native.GetWindowRect(target.Handle, out Native.Rect cancelled);
            mover.Release(); Require(mover.WaitForIdle(2000), "cancel idle");
            Native.GetWindowRect(target.Handle, out rect);
            Require(rect.Left == cancelled.Left && rect.Right == cancelled.Right, "cancel snapped");
        }
        internal static void NativeFlickCommandsIndependent()
        {
            using var target = new LatencyChecks.SlowWindow(true);
            using var mover = new WindowMover { FollowCursor = false, MaximizeAtTop = false, SnapAtSides = false, BringToFront = false };
            Rectangle work = MonitorLayout.ForWindow(target.Handle, true);
            foreach (WindowLayoutAction action in new[] { WindowLayoutAction.Maximize, WindowLayoutAction.LeftHalf, WindowLayoutAction.RightHalf })
            {
                var anchor = new Native.Point(work.Left + work.Width / 2, work.Top + work.Height / 2);
                Require(mover.PrepareWindow(target.Handle, anchor) && mover.Capture(), "flick command capture");
                mover.Finish(action); Require(mover.WaitForIdle(2000), "flick command idle");
                if (action == WindowLayoutAction.Maximize) Require(Native.IsZoomed(target.Handle), "up flick didn't maximize with top toggle off");
                else AssertHalf(target.Handle, work, action);
            }
        }
    }
}
