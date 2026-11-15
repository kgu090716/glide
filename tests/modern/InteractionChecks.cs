using System;
using System.Drawing;
using System.IO;
using System.Threading;

namespace ThreeFingerWindowDrag
{
    internal static class InteractionChecks
    {
        private static void Require(bool value, string error) { if (!value) throw new Exception(error); }
        private static ContactFrame Frame(double x = 0, double y = 0, int count = 3)
        {
            var frame = new ContactFrame { Device = new IntPtr(1) };
            for (int i = 0; i < count; i++) frame.Contacts.Add(new Contact { Id = i, X = 40 + x + i * 10, Y = 30 + y, Tip = true, Confident = true });
            return frame;
        }
        private static void Hold(GestureEngine engine, int until)
        { for (int time = 0; time <= until; time += 100) engine.Process(Frame(), time, false); }
        internal static void HoldStartsResizeBeforeMoving()
        {
            var mover = new FakeMover(); var engine = new GestureEngine(mover) { DirectionalResize = new DirectionalResizeOptions { HoldMilliseconds = 500 } };
            Hold(engine, 500); engine.Tick(500);
            Require(engine.ResizeReady && mover.Captures == 0 && mover.Moves == 0, "hold changed window before motion");
            engine.Process(Frame(1), 520, false);
            Require(engine.DirectionalResizing && mover.EdgeResizes == 1 && mover.Edges == ResizeEdges.Right && !mover.RestoreSnap, "hold resize/size baseline");
            engine.Process(Frame(-2), 540, false);
            Require(mover.Edges == ResizeEdges.Right && mover.X == -24, "reverse switched edge instead of shrinking");
            engine.Process(Frame(-2, -2), 560, false);
            Require(mover.Edges == (ResizeEdges.Right | ResizeEdges.Top), "second axis did not select an edge");
            engine.Process(Frame(-2, -2, 0), 570, false);
            Require(mover.Finishes == 1 && mover.Action == WindowLayoutAction.None, "resize released a flick");
        }
        internal static void EarlyMovementLocksNormalDrag()
        {
            var mover = new FakeMover(); var engine = new GestureEngine(mover) { DirectionalResize = new DirectionalResizeOptions { HoldMilliseconds = 2000 } };
            Hold(engine, 500); engine.Process(Frame(2), 520, false);
            for (int time = 600; time <= 2200; time += 100) engine.Process(Frame(2), time, false);
            engine.Process(Frame(3), 2220, false);
            Require(mover.Moves > 0 && mover.EdgeResizes == 0 && !engine.Resizing, "normal drag became resize midway");
            mover = new FakeMover(); engine = new GestureEngine(mover) { DirectionalResize = new DirectionalResizeOptions { Enabled = false, HoldMilliseconds = 200 } };
            Hold(engine, 500); engine.Process(Frame(2), 520, false);
            Require(mover.Moves > 0 && mover.EdgeResizes == 0, "disabled direction resize activated");
            mover = new FakeMover { SupportsResize = false }; engine = new GestureEngine(mover);
            Hold(engine, 1100); engine.Process(Frame(2), 1120, false);
            Require(mover.EdgeResizes == 0, "fixed-size target resized");
        }
        internal static void GeometryAndLimits()
        {
            var rect = new Rectangle(-600, 200, 500, 400); var min = new Size(200, 120); var max = new Size(2000, 1200);
            Rectangle left = DirectionalResizeGeometry.Apply(rect, -100, 0, ResizeEdges.Left, min, max);
            Require(left.Left == -700 && left.Right == rect.Right && left.Width == 600 && left.Height == rect.Height, "left edge geometry");
            Rectangle right = DirectionalResizeGeometry.Apply(rect, 100, 0, ResizeEdges.Right, min, max);
            Require(right.Left == rect.Left && right.Width == 600 && right.Height == rect.Height, "right edge geometry");
            Rectangle top = DirectionalResizeGeometry.Apply(rect, 0, -100, ResizeEdges.Top, min, max);
            Require(top.Top == 100 && top.Bottom == rect.Bottom && top.Height == 500 && top.Width == rect.Width, "top edge geometry");
            Rectangle bottom = DirectionalResizeGeometry.Apply(rect, 0, 100, ResizeEdges.Bottom, min, max);
            Require(bottom.Top == rect.Top && bottom.Height == 500, "bottom edge geometry");
            Rectangle tiny = DirectionalResizeGeometry.Apply(rect, 5000, 5000, ResizeEdges.Left | ResizeEdges.Top, min, max);
            Require(tiny.Size == min && tiny.Right == rect.Right && tiny.Bottom == rect.Bottom, "minimum/opposite edges lost");
            Rectangle large = DirectionalResizeGeometry.Apply(rect, 5000, 5000, ResizeEdges.Right | ResizeEdges.Bottom, min, max);
            Require(large.Size == max && large.Location == rect.Location, "maximum/opposite edges lost");
        }
        internal static void FlickMustFinishSoonAfterContact()
        {
            var delayed = new FlickRecognizer();
            for (int time = 0; time <= 500; time += 10) delayed.Add(time, 0, 0);
            for (int time = 510; time <= 600; time += 10) delayed.Add(time, (time - 500) * .24, 0);
            Require(delayed.OnRelease(605, 10) == WindowLayoutAction.None, "held contact became flick");
            var shortWait = new FlickRecognizer();
            for (int time = 0; time <= 100; time += 10) shortWait.Add(time, 0, 0);
            for (int time = 110; time <= 200; time += 10) shortWait.Add(time, 0, -(time - 100) * .24);
            Require(shortWait.OnRelease(205, 5) == WindowLayoutAction.Maximize, "brief initial dwell blocked a valid flick");
            var mover = new FakeMover(); var engine = new GestureEngine(mover) { DirectionalResize = new DirectionalResizeOptions { Enabled = false } };
            Hold(engine, 500);
            for (int time = 510; time <= 600; time += 10) engine.Process(Frame((time - 500) * .24), time, false);
            engine.Process(Frame(24, 0, 0), 605, false);
            Require(mover.Finishes == 1 && mover.Action == WindowLayoutAction.None, "engine dispatched late flick");
        }
        internal static void WelcomeAndSettingsPersist()
        {
            string path = Settings.FilePath; byte[] backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                Require(Settings.Load().NeedsWelcome, "fresh install skipped welcome");
                var settings = new Settings { DirectionalResize = false, ResizeHoldSeconds = 2.4 };
                settings.Save(); Require(Settings.Load().NeedsWelcome, "unshown welcome was lost");
                settings.MarkWelcomeShown(); settings.Save(); var loaded = Settings.Load();
                Require(!loaded.NeedsWelcome && !loaded.DirectionalResize && loaded.ResizeHoldSeconds == 2.4, "welcome/options did not persist");
                File.WriteAllText(path, "SetupDone=false\nSpeed=26\n");
                Require(!Settings.Load().NeedsWelcome && Settings.Load().Speed == 26, "old installation got a welcome on upgrade");
                File.WriteAllText(path, "OnboardingShown=false\nResizeHoldSeconds=NaN\n");
                loaded = Settings.Load(); Require(loaded.NeedsWelcome && loaded.ResizeHoldSeconds == 1, "invalid settings/defaults");
            }
            finally { if (backup == null) File.Delete(path); else File.WriteAllBytes(path, backup); }
        }
        internal static void LegacyModifierSettingsUseHold()
        {
            string path = Settings.FilePath;
            byte[] backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                File.WriteAllText(path, "SetupDone=true\nDirectionalResize=true\nResizeTrigger=Modifier\nResizeModifier=Control\nResizeHoldSeconds=0.5\nResizeBehavior=LowerCorners\n");
                var settings = Settings.Load();
                Require(settings.ResizeHoldSeconds == 0.5 && settings.ResizeBehavior == ResizeBehavior.LowerCorners, "old timing or mode was lost");
                var mover = new FakeMover();
                var engine = new GestureEngine(mover) { DirectionalResize = settings.ResizeOptions };
                Hold(engine, 500);
                engine.Process(Frame(2, 2), 500, false);
                Require(mover.EdgeResizes == 1 && mover.Edges == (ResizeEdges.Right | ResizeEdges.Bottom), "previous key-only setting did not switch to a hold");
                settings.Save();
                string saved = File.ReadAllText(path);
                Require(!saved.Contains("ResizeTrigger=") && !saved.Contains("ResizeModifier="), "obsolete settings were written again");
            }
            finally { if (backup == null) File.Delete(path); else File.WriteAllBytes(path, backup); }
        }
        internal static void InputOptionsAreSnapshots()
        {
            var options = new DirectionalResizeOptions { Enabled = false, HoldMilliseconds = 2400 };
            using var controller = new InputController(false, 12, true, true, resizeOptions: options);
            options.Enabled = true; options.HoldMilliseconds = 1800;
            Require(!controller.State.DirectionalResize && controller.State.ResizeHoldMilliseconds == 2400, "constructor shares mutable options");
            controller.Configure(false, 12, true, true, resizeOptions: options); options.HoldMilliseconds = 3100;
            Thread.Sleep(120);
            Require(controller.State.DirectionalResize && controller.State.ResizeHoldMilliseconds == 1800, "configuration is not a snapshot");
        }
        internal static void NativeDirectionalResize()
        {
            Require(Native.GetCursorPos(out Native.Point saved), "get cursor");
            try
            {
                using var target = new LatencyChecks.SlowWindow(true);
                using var mover = new WindowMover { BringToFront = false, FollowCursor = true, MaximizeAtTop = true, SnapAtSides = true };
                Native.GetWindowRect(target.Handle, out Native.Rect rect);
                var anchor = new Native.Point(rect.Left + 180, rect.Top + 140);
                Require(Native.SetCursorPos(anchor.X, anchor.Y), "set anchor");
                Require(mover.PrepareWindow(target.Handle, anchor) && mover.Capture(false), "capture for directional resize");
                Require(mover.ResizeByEdges(120, 0, ResizeEdges.Right) && mover.WaitForIdle(2000), "right resize");
                Native.GetWindowRect(target.Handle, out Native.Rect grown); Native.GetCursorPos(out Native.Point cursor);
                Require(grown.Left == rect.Left && grown.Right == rect.Right + 120 && grown.Top == rect.Top && grown.Bottom == rect.Bottom, "native right edge/height");
                Require(cursor.X == anchor.X + 45 && cursor.Y == anchor.Y, "native resize cursor anchor");
                Require(mover.ResizeByEdges(-60, 0, ResizeEdges.Right) && mover.WaitForIdle(2000), "reverse resize");
                Native.GetWindowRect(target.Handle, out grown);
                Require(grown.Right == rect.Right - 60 && grown.Left == rect.Left, "reverse accumulated width");
                Require(mover.ResizeByEdges(-80, -60, ResizeEdges.Left | ResizeEdges.Top) && mover.WaitForIdle(2000), "left/top resize");
                Native.GetWindowRect(target.Handle, out grown);
                Require(grown.Right == rect.Right && grown.Bottom == rect.Bottom && grown.Left == rect.Left - 80 && grown.Top == rect.Top - 60, "native opposite edges drift");
                Require(mover.EdgeAction == WindowLayoutAction.None, "resize armed snap");
                mover.Finish(); Require(mover.WaitForIdle(2000) && !Native.IsZoomed(target.Handle), "directional release maximized");
            }
            finally { Native.SetCursorPos(saved.X, saved.Y); }
        }
    }
}
