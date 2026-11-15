using System;
using System.Drawing;
using System.IO;

namespace ThreeFingerWindowDrag
{
    internal static class ResizeModeChecks
    {
        private static void Require(bool value, string error) { if (!value) throw new Exception(error); }
        private static ContactFrame Frame(double x = 0, double y = 0, double scale = 1, int count = 3)
        {
            var frame = new ContactFrame { Device = new IntPtr(1) };
            for (int i = 0; i < count; i++) frame.Contacts.Add(new Contact { Id = i, X = 40 + x + (i - 1) * 10 * scale, Y = 30 + y, Tip = true, Confident = true });
            return frame;
        }
        private static void HoldForResize(GestureEngine engine)
        {
            engine.Process(Frame(), 0, false);
            engine.Process(Frame(), 100, false);
            engine.Process(Frame(), 200, false);
        }
        internal static void TenthsAndSavedSettings()
        {
            Require(ResizeTiming.NormalizeSeconds(.5 + (double)(float).1) == .6, "XAML float increment was not normalized");
            Require(ResizeTiming.NormalizeSeconds(.6000000015) == .6 && ResizeTiming.NormalizeSeconds(.2 - .1) == .2, "noise/minimum rounding");
            Require(ResizeTiming.NormalizeSeconds(10.1) == 10 && ResizeTiming.NormalizeSeconds(double.NaN) == 1, "maximum/invalid rounding");
            double value = .5;
            for (int i = 0; i < 20; i++) value = ResizeTiming.NormalizeSeconds(value + .1);
            for (int i = 0; i < 20; i++) value = ResizeTiming.NormalizeSeconds(value - .1);
            Require(value == .5, "repeated stepping drifted");
            string path = Settings.FilePath; byte[] backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                new Settings { ResizeHoldSeconds = .6000000015, ResizeBehavior = ResizeBehavior.LowerCorners }.Save();
                Require(File.ReadAllText(path).Contains("ResizeHoldSeconds=0.6\r\n"), "saved fractional noise");
                var loaded = Settings.Load();
                Require(loaded.ResizeHoldSeconds == .6 && loaded.ResizeOptions.HoldMilliseconds == 600 && loaded.ResizeBehavior == ResizeBehavior.LowerCorners, "saved mode/time did not round trip");
                File.WriteAllText(path, "SetupDone=true\nResizeBehavior=Shrink\n");
                Require(Settings.Load().ResizeBehavior == ResizeBehavior.LowerCorners, "previous selection did not migrate to lower corners");
                File.WriteAllText(path, "SetupDone=true\nResizeHoldSeconds=0.6000000015\n"); loaded = Settings.Load();
                Require(loaded.ResizeHoldSeconds == .6 && loaded.ResizeBehavior == ResizeBehavior.Grow && !loaded.NeedsWelcome, "old settings changed resize mode");
                File.WriteAllText(path, "ResizeBehavior=999\n"); Require(Settings.Load().ResizeBehavior == ResizeBehavior.Grow, "invalid behavior was accepted");
            }
            finally { if (backup == null) File.Delete(path); else File.WriteAllBytes(path, backup); }
        }
        internal static void AllCornerDirectionsAndReversal()
        {
            var original = new Rectangle(300, 200, 500, 400);
            var cases = new[] {
                (X:-2d, Y:-2d, Edges:ResizeEdges.Right|ResizeEdges.Bottom, Bounds:new Rectangle(300,200,476,376)),
                (X:2d, Y:-2d, Edges:ResizeEdges.Left|ResizeEdges.Bottom, Bounds:new Rectangle(324,200,476,376)),
                (X:-2d, Y:2d, Edges:ResizeEdges.Left|ResizeEdges.Bottom, Bounds:new Rectangle(276,200,524,424)),
                (X:2d, Y:2d, Edges:ResizeEdges.Right|ResizeEdges.Bottom, Bounds:new Rectangle(300,200,524,424))
            };
            foreach (var item in cases)
            {
                var mover = new FakeMover(); var engine = new GestureEngine(mover) {
                    DirectionalResize = new DirectionalResizeOptions { HoldMilliseconds = 200, Behavior = ResizeBehavior.LowerCorners }
                };
                HoldForResize(engine); engine.Process(Frame(item.X, item.Y), 220, false);
                Require(mover.EdgeResizes == 1 && mover.Edges == item.Edges && !mover.RestoreSnap, "selected lower corner/baseline changed");
                Rectangle result = DirectionalResizeGeometry.Apply(original, (int)mover.X, (int)mover.Y, mover.Edges, new Size(200,120), new Size(2000,1200));
                Require(result == item.Bounds, "four-direction mapping is wrong: " + result + " vs " + item.Bounds);
                engine.Process(Frame(-item.X/2, -item.Y/2), 240, false);
                result = DirectionalResizeGeometry.Apply(original, (int)mover.X, (int)mover.Y, mover.Edges, new Size(200,120), new Size(2000,1200));
                int reverse = -(int)(item.Y * 6);
                Require(mover.Edges == item.Edges && result.Width == original.Width + reverse && result.Height == original.Height + reverse && result.Top == original.Top, "reversal switched corners or moved top");
                Require((item.Edges & ResizeEdges.Right) != 0 ? result.Left == original.Left : result.Right == original.Right, "upper fixed corner moved");
                engine.Process(Frame(count: 0), 250, false);
                Require(mover.Finishes == 1 && mover.Action == WindowLayoutAction.None, "directional resize released a layout command");
            }
            foreach (int x in new[]{-1,1}) foreach(int y in new[]{-1,1})
            {
                var mover = new FakeMover(); var engine = new GestureEngine(mover) { DirectionalResize = new DirectionalResizeOptions { HoldMilliseconds = 200 } };
                HoldForResize(engine); engine.Process(Frame(x*2, y*2), 220, false);
                ResizeEdges expected = (x<0 ? ResizeEdges.Left : ResizeEdges.Right) | (y<0 ? ResizeEdges.Top : ResizeEdges.Bottom);
                Rectangle result = DirectionalResizeGeometry.Apply(original,(int)mover.X,(int)mover.Y,mover.Edges,new Size(200,120),new Size(2000,1200));
                Require(mover.Edges == expected && result.Width == 524 && result.Height == 424, "grow behavior changed");
            }
        }
        internal static void StaggeredAxesChooseCorrectLowerCorner()
        {
            var mover = new FakeMover(); var engine = new GestureEngine(mover) { DirectionalResize = new DirectionalResizeOptions { HoldMilliseconds = 200, Behavior = ResizeBehavior.LowerCorners } };
            HoldForResize(engine); engine.Process(Frame(.8,0),210,false);
            Require(mover.EdgeResizes == 0, "horizontal lead grew the wrong side before diagonal intent");
            engine.Process(Frame(.8,-.8),220,false);
            Require(mover.Edges == (ResizeEdges.Left|ResizeEdges.Bottom) && mover.X == 10 && mover.Y == -10, "up-right did not move lower-left");
            mover = new FakeMover(); engine = new GestureEngine(mover) { PixelsPerMm = 3, DirectionalResize = new DirectionalResizeOptions { HoldMilliseconds = 200, Behavior = ResizeBehavior.LowerCorners } };
            HoldForResize(engine); engine.Process(Frame(.64,-.8),210,false);
            Require(mover.Edges == ResizeEdges.Bottom, "vertical lead picked a horizontal corner too early");
            int before = mover.EdgeResizes; engine.Process(Frame(.66,-.8),220,false);
            Require(mover.EdgeResizes == before+1 && mover.Edges == (ResizeEdges.Left|ResizeEdges.Bottom), "new corner was lost when rounded pixel coordinate stayed the same");
        }
        internal static void PinchDragAndSnapshotsStayIndependent()
        {
            var options = new DirectionalResizeOptions { Behavior = ResizeBehavior.LowerCorners };
            var mover = new FakeMover(); var engine = new GestureEngine(mover) { DirectionalResize = options };
            engine.Process(Frame(), 0, false); engine.Process(Frame(2), 20, false);
            Require(mover.Moves == 1 && mover.X == 24 && mover.EdgeResizes == 0, "normal drag was reversed");
            mover = new FakeMover(); engine = new GestureEngine(mover) { DirectionalResize = options };
            engine.Process(Frame(), 0, false); engine.Process(Frame(scale: 1.2), 20, false);
            Require(mover.Resizes == 1 && Math.Abs(mover.Scale - 1.2) < .00001 && mover.EdgeResizes == 0, "pinch was reversed");
            DirectionalResizeOptions snapshot = options.Snapshot(); options.Behavior = ResizeBehavior.Grow;
            Require(snapshot.Behavior == ResizeBehavior.LowerCorners, "behavior snapshot was shared");
        }
        private sealed class BoundMover : IWindowMover
        {
            private readonly WindowMover mover;
            private readonly IntPtr window;
            internal BoundMover(WindowMover value, IntPtr handle) { mover = value; window = handle; }
            public bool CanResize { get { return mover.CanResize; } }
            public bool Prepare()
            {
                Native.GetWindowRect(window, out Native.Rect rect);
                return mover.PrepareWindow(window, new Native.Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2));
            }
            public bool Capture(bool restoreSnap = true) { return mover.Capture(restoreSnap); }
            public bool Move(double x, double y) { return mover.Move(x,y); }
            public bool Resize(double scale) { return mover.Resize(scale); }
            public bool ResizeByEdges(double x, double y, ResizeEdges edges) { return mover.ResizeByEdges(x,y,edges); }
            public void Finish(WindowLayoutAction action = WindowLayoutAction.None) { mover.Finish(action); }
            public void Release() { mover.Release(); }
        }
        internal static void NativeCornerModes()
        {
            using var target = new LatencyChecks.SlowWindow(true);
            using var mover = new WindowMover { BringToFront = false, FollowCursor = false, MaximizeAtTop = false, SnapAtSides = false };
            Native.GetWindowRect(target.Handle, out Native.Rect original);
            foreach (ResizeBehavior behavior in new[] { ResizeBehavior.Grow, ResizeBehavior.LowerCorners })
            foreach (int horizontal in new[] { -1, 1 })
            foreach (int vertical in new[] { -1, 1 })
            {
                Require(Native.SetWindowPos(target.Handle, IntPtr.Zero, original.Left, original.Top, original.Right-original.Left, original.Bottom-original.Top, 0x0214), "reset native size");
                var engine = new GestureEngine(new BoundMover(mover, target.Handle)) {
                    DirectionalResize = new DirectionalResizeOptions { HoldMilliseconds = 200, Behavior = behavior }
                };
                HoldForResize(engine); engine.Process(Frame(horizontal * 2, vertical * 2), 220, false);
                Require(mover.WaitForIdle(2000), "native corner resize idle"); Native.GetWindowRect(target.Handle, out Native.Rect result);
                int delta = behavior == ResizeBehavior.Grow ? 24 : vertical * 24;
                Require(result.Right-result.Left == original.Right-original.Left+delta && result.Bottom-result.Top == original.Bottom-original.Top+delta, "native corner mode size");
                if (behavior == ResizeBehavior.Grow)
                {
                    Require((vertical<0 ? result.Bottom == original.Bottom : result.Top == original.Top) &&
                        (horizontal<0 ? result.Right == original.Right : result.Left == original.Left), "grow opposite corner moved");
                }
                else
                {
                    Require(result.Top == original.Top && result.Bottom == original.Bottom+vertical*24, "lower mode moved top instead of bottom");
                    bool right = horizontal == vertical;
                    Require(right ? result.Left == original.Left && result.Right == original.Right+horizontal*24 :
                        result.Right == original.Right && result.Left == original.Left+horizontal*24, "native lower corner/upper anchor incorrect");
                }
                engine.Process(Frame(count:0), 240, false); Require(mover.WaitForIdle(2000) && !Native.IsZoomed(target.Handle), "corner resize maximized on lift");
            }
        }
    }
}
