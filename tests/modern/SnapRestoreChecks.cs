using System;
using System.Drawing;

namespace ThreeFingerWindowDrag
{
    internal static class SnapRestoreChecks
    {
        private static void Require(bool value, string error) { if (!value) throw new Exception(error); }
        private static Rectangle Bounds(IntPtr window)
        {
            Require(Native.GetWindowRect(window, out Native.Rect rect), "read window bounds");
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
        private static Native.Point Center(Rectangle rect) { return new Native.Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2); }
        private static void Capture(WindowMover mover, IntPtr window)
        {
            Require(mover.PrepareWindow(window, Center(Bounds(window))) && mover.Capture(), "capture window");
        }
        private static void Snap(WindowMover mover, IntPtr window, WindowLayoutAction action)
        {
            Capture(mover, window); mover.Finish(action); Require(mover.WaitForIdle(2000), "layout idle");
        }
        private static void DragAndCheck(WindowMover mover, IntPtr window, Size expected)
        {
            Capture(mover, window); Require(mover.Move(24, 18) && mover.WaitForIdle(2000), "restore move");
            Require(Bounds(window).Size == expected, "original size not restored: " + Bounds(window).Size + " vs " + expected);
            mover.Finish(); Require(mover.WaitForIdle(2000), "finish restore");
        }
        internal static void AnchorGeometry()
        {
            var snap = new Rectangle(-1928, -200, 976, 1088);
            var cursor = new Point(snap.Left + snap.Width / 4, snap.Top + snap.Height / 2);
            Rectangle restored = SnapRestoreMemory.UnderCursor(snap, new Size(640, 480), cursor);
            Require(restored.Size == new Size(640, 480) && restored.Left + 160 == cursor.X && restored.Top + 240 == cursor.Y, "anchor/negative coordinate drift");
            Rectangle corner = SnapRestoreMemory.UnderCursor(snap, new Size(640, 480), new Point(snap.Left - 50, snap.Bottom + 50));
            Require(corner.Left == snap.Left - 50 && corner.Bottom == snap.Bottom + 50, "outside anchor not clamped");
        }
        internal static void GestureChoosesRestoreOnlyForDrag()
        {
            ContactFrame Frame(double dx, double scale)
            {
                var result = new ContactFrame { Device = new IntPtr(1) };
                for (int i = 0; i < 3; i++) result.Contacts.Add(new Contact { Id = i, X = 40 + dx + (i - 1) * 10 * scale, Y = 30, Tip = true, Confident = true });
                return result;
            }
            var move = new FakeMover(); var engine = new GestureEngine(move);
            engine.Process(Frame(0, 1), 0, false); engine.Process(Frame(2, 1), GestureEngine.ThreeFingerConfirmationMs, false);
            Require(move.Captures == 1 && move.RestoreSnap, "drag did not request unsnap");
            var pinch = new FakeMover(); engine = new GestureEngine(pinch);
            engine.Process(Frame(0, 1), 0, false); engine.Process(Frame(0, 1.2), GestureEngine.ThreeFingerConfirmationMs, false);
            Require(pinch.Resizes == 1 && !pinch.RestoreSnap, "pinch unexpectedly restored old size");
        }
        internal static void NativeRestoreAndCursor()
        {
            Require(Native.GetCursorPos(out Native.Point saved), "read cursor");
            try
            {
                using var target = new LatencyChecks.SlowWindow(true);
                using var mover = new WindowMover { BringToFront = false, FollowCursor = false, MaximizeAtTop = false, SnapAtSides = false };
                Size original = Bounds(target.Handle).Size;
                Capture(mover, target.Handle);
                Require(mover.Move(40, 30) && mover.WaitForIdle(2000), "move before snap");
                mover.Finish(WindowLayoutAction.LeftHalf); Require(mover.WaitForIdle(2000), "first snap");
                Rectangle snapped = Bounds(target.Handle);
                Require(snapped.Size != original, "fixture did not snap");
                // A contact or Escape before the first movement must leave the snap and its memory intact.
                Capture(mover, target.Handle); mover.Release(); Require(mover.WaitForIdle(2000) && Bounds(target.Handle) == snapped, "capture/cancel changed window");
                var anchor = new Native.Point(snapped.Left + snapped.Width / 4, snapped.Top + snapped.Height / 2);
                Require(Native.SetCursorPos(anchor.X, anchor.Y), "set anchor");
                mover.FollowCursor = true;
                Require(mover.PrepareWindow(target.Handle, anchor) && mover.Capture(), "restore capture");
                Require(Bounds(target.Handle) == snapped, "capture restored before drag");
                Require(mover.Move(30, 20) && mover.WaitForIdle(2000), "unsnap drag");
                Rectangle restored = Bounds(target.Handle);
                Rectangle expected = SnapRestoreMemory.UnderCursor(snapped, original, new Point(anchor.X, anchor.Y)); expected.Offset(30, 20);
                Require(restored == expected, "restored geometry/anchor differs: " + restored + " vs " + expected);
                Native.GetCursorPos(out Native.Point cursor);
                Require(cursor.X == anchor.X + 30 && cursor.Y == anchor.Y + 20, "cursor jumped on unsnap");
                mover.Finish();
            }
            finally { Native.SetCursorPos(saved.X, saved.Y); }
        }
        internal static void NativeRepeatedSidesAndWindows()
        {
            using var first = new LatencyChecks.SlowWindow(true);
            using var second = new LatencyChecks.SlowWindow(true);
            using var mover = new WindowMover { BringToFront = false, FollowCursor = false, MaximizeAtTop = false, SnapAtSides = false };
            Rectangle secondBounds = Bounds(second.Handle);
            Require(Native.SetWindowPos(second.Handle, IntPtr.Zero, secondBounds.Left + 80, secondBounds.Top + 80, 700, 420, 0x0214), "second fixture size");
            Size firstSize = Bounds(first.Handle).Size, secondSize = Bounds(second.Handle).Size;
            Snap(mover, first.Handle, WindowLayoutAction.LeftHalf);
            Snap(mover, second.Handle, WindowLayoutAction.RightHalf);
            Snap(mover, first.Handle, WindowLayoutAction.RightHalf);
            Snap(mover, first.Handle, WindowLayoutAction.LeftHalf);
            DragAndCheck(mover, second.Handle, secondSize);
            DragAndCheck(mover, first.Handle, firstSize);
            // An ordinary resize after unsnapping becomes the baseline for the next snap.
            Rectangle rect = Bounds(first.Handle);
            Require(Native.SetWindowPos(first.Handle, IntPtr.Zero, rect.Left, rect.Top, 620, 380, 0x0214), "resize normal window");
            Snap(mover, first.Handle, WindowLayoutAction.RightHalf);
            DragAndCheck(mover, first.Handle, new Size(620, 380));
        }
        internal static void NativePinchExternalResizeAndMaximize()
        {
            using var target = new LatencyChecks.SlowWindow(true);
            using var mover = new WindowMover { BringToFront = false, FollowCursor = false, MaximizeAtTop = false, SnapAtSides = false };
            Size original = Bounds(target.Handle).Size;
            Native.ShowWindow(target.Handle, 3);
            Snap(mover, target.Handle, WindowLayoutAction.LeftHalf);
            Snap(mover, target.Handle, WindowLayoutAction.Maximize);
            DragAndCheck(mover, target.Handle, original);
            Snap(mover, target.Handle, WindowLayoutAction.RightHalf);
            Rectangle snapped = Bounds(target.Handle);
            Require(mover.PrepareWindow(target.Handle, Center(snapped)) && mover.Capture(false), "pinch capture");
            Require(mover.Resize(0.8) && mover.WaitForIdle(2000), "pinch snapped window");
            Size pinched = Bounds(target.Handle).Size;
            Require(pinched.Width == (int)Math.Round(snapped.Width * .8) && pinched.Height == (int)Math.Round(snapped.Height * .8), "pinch started from original instead of snap size");
            mover.Finish(); DragAndCheck(mover, target.Handle, pinched);
            Snap(mover, target.Handle, WindowLayoutAction.LeftHalf);
            Rectangle rect = Bounds(target.Handle);
            Require(Native.SetWindowPos(target.Handle, IntPtr.Zero, rect.Left + 100, rect.Top + 120, 680, 440, 0x0214), "external resize");
            DragAndCheck(mover, target.Handle, new Size(680, 440));
        }
    }
}
