using System;
using System.ComponentModel;
using System.Drawing;
using System.Text;

namespace ThreeFingerWindowDrag
{
    internal sealed class WindowMover : IWindowMover, IDisposable
    {
        internal const string FailurePrefix = "창 이동 실패: ";
        private IntPtr window;
        private Native.Point cursor;
        private int originX, originY, width, height;
        private bool resize;
        private Rectangle[] workAreas;
        private Rectangle[] monitorBounds;
        private Size maximumResize;
        private WindowLayoutAction edgeAction;
        private Point layoutPoint;
        private readonly LatestMoveDispatcher dispatcher;
        private readonly SnapRestoreMemory snapRestore = new SnapRestoreMemory();
        private Rectangle layoutRestoreBounds;
        private bool clearSnapRestore;
        private long activatedGeneration = -1;
        private IntPtr activatedWindow;
        internal bool RestoreMaximized = true;
        internal bool FollowCursor = true;
        internal bool MaximizeAtTop = true;
        internal bool SnapAtSides = true;
        internal bool BringToFront = true;
        internal bool AllowTopOverflow = true;
        public bool CanResize { get; private set; }
        internal volatile bool ElevationRequired;
        internal bool TopEdgeArmed { get { return edgeAction == WindowLayoutAction.Maximize; } }
        internal WindowLayoutAction EdgeAction { get { return edgeAction; } }
        internal string Status = "창 위에 커서를 놓고 세 손가락을 움직여 보세요.";
        internal int Moves;
        internal long Coalesced { get { return dispatcher.Replaced; } }
        internal long NativeAttempts { get { return dispatcher.Applied; } }
        internal WindowMover() { dispatcher = new LatestMoveDispatcher(ApplyPosition, CompletePosition); }

        public bool Prepare()
        {
            Native.Point point;
            if (!Native.GetCursorPos(out point)) return false;
            IntPtr hit = Native.WindowFromPoint(point);
            return PrepareWindow(Native.GetAncestor(hit, 2), point);
        }
        internal bool PrepareWindow(IntPtr root, Native.Point anchor)
        {
            dispatcher.Cancel();
            window = IntPtr.Zero;
            CanResize = false;
            ElevationRequired = false;
            cursor = anchor;
            layoutPoint = new Point(anchor.X, anchor.Y); edgeAction = WindowLayoutAction.None;
            if (!Eligible(root)) return false;
            if (ElevationSupport.RequiresElevation(root))
            {
                ElevationRequired = true; Status = ElevationSupport.RequiredMessage; return false;
            }
            window = root;
            CanResize = (Native.GetWindowLong(root, -16) & 0x00040000) != 0;
            return true;
        }
        private static bool Eligible(IntPtr candidate)
        {
            if (candidate == IntPtr.Zero || candidate == Native.GetDesktopWindow() || candidate == Native.GetShellWindow() ||
                !Native.IsWindow(candidate) || !Native.IsWindowVisible(candidate) || Native.IsIconic(candidate)) return false;
            if (!WindowPolicy.CanMove(candidate)) return false;
            Native.Rect rect;
            if (!Native.GetWindowRect(candidate, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return false;
            // Ignore borderless fullscreen surfaces (games, video, desktop overlays).
            Rectangle bounds = MonitorLayout.ForWindow(candidate, false);
            bool borderless = (Native.GetWindowLong(candidate, -16) & 0x00C40000) == 0;
            if (borderless && rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom) return false;
            return true;
        }
        public bool Capture(bool restoreSnap = true)
        {
            if (!Eligible(window)) return false;
            Native.Rect rect;
            if (!Native.GetWindowRect(window, out rect)) return false;
            originX = rect.Left; originY = rect.Top;
            width = rect.Right - rect.Left; height = rect.Bottom - rect.Top;
            resize = false;
            bool maximized = Native.IsZoomed(window);
            var current = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            bool remembered = snapRestore.TryGet(window, current, maximized, out Rectangle original);
            layoutRestoreBounds = remembered ? original : current;
            clearSnapRestore = remembered;
            workAreas = MonitorLayout.WorkAreas();
            monitorBounds = MonitorLayout.Bounds();
            int maximumWidth = 200, maximumHeight = 120;
            foreach (Rectangle monitor in monitorBounds)
            { maximumWidth = Math.Max(maximumWidth, monitor.Width * 2); maximumHeight = Math.Max(maximumHeight, monitor.Height * 2); }
            maximumResize = new Size(maximumWidth, maximumHeight);
            edgeAction = WindowLayoutAction.None;
            if (maximized)
            {
                if (!RestoreMaximized) { Status = "최대화된 창 이동이 꺼져 있습니다."; return false; }
                Native.WindowPlacement placement = new Native.WindowPlacement { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WindowPlacement)) };
                if (!Native.GetWindowPlacement(window, ref placement)) return false;
                int restoredWidth = placement.Normal.Right - placement.Normal.Left;
                int restoredHeight = placement.Normal.Bottom - placement.Normal.Top;
                if (restoredWidth < 100 || restoredHeight < 80) return false;
                if (!remembered) layoutRestoreBounds = new Rectangle(placement.Normal.Left, placement.Normal.Top, restoredWidth, restoredHeight);
                Size restored = remembered && restoreSnap ? original.Size : new Size(restoredWidth, restoredHeight);
                Rectangle anchored = SnapRestoreMemory.UnderCursor(current, restored, new Point(cursor.X, cursor.Y));
                width = anchored.Width; height = anchored.Height; originX = anchored.X; originY = anchored.Y;
                resize = true;
            }
            else if (remembered && restoreSnap)
            {
                Rectangle anchored = SnapRestoreMemory.UnderCursor(current, original.Size, new Point(cursor.X, cursor.Y));
                width = anchored.Width; height = anchored.Height; originX = anchored.X; originY = anchored.Y;
                resize = true;
            }
            Status = "이동 중: 선택한 창";
            return true;
        }
        internal static Point KeepReachable(Rectangle desired, Rectangle[] workAreas, bool allowTopOverflow = false)
        {
            const int visible = 64;
            foreach (Rectangle work in workAreas)
            {
                int overlap = Math.Min(desired.Right, work.Right) - Math.Max(desired.Left, work.Left);
                int minimumTop = allowTopOverflow ? work.Top - desired.Height + Math.Min(visible, desired.Height) : work.Top;
                if (overlap >= Math.Min(visible, desired.Width) && desired.Top >= minimumTop && desired.Top + 24 <= work.Bottom)
                    return desired.Location;
            }
            Rectangle nearest = workAreas[0]; double minimumDistance = double.MaxValue;
            foreach (Rectangle w in workAreas)
            {
                double cx = desired.Left + desired.Width / 2.0, cy = desired.Top + 16;
                double dx = Math.Max(w.Left - cx, Math.Max(0, cx - w.Right));
                double dy = Math.Max(w.Top - cy, Math.Max(0, cy - w.Bottom));
                double distance = dx * dx + dy * dy;
                if (distance < minimumDistance) { minimumDistance = distance; nearest = w; }
            }
            return new Point(Math.Max(nearest.Left - desired.Width + visible, Math.Min(nearest.Right - visible, desired.Left)),
                Math.Max(allowTopOverflow ? nearest.Top - desired.Height + Math.Min(visible, desired.Height) : nearest.Top,
                    Math.Min(nearest.Bottom - 32, desired.Top)));
        }
        public bool Move(double dx, double dy)
        {
            if (!Native.IsWindow(window) || Native.IsIconic(window)) return false;
            Rectangle desired = new Rectangle(originX + (int)dx, originY + (int)dy, width, height);
            Point position = KeepReachable(desired, workAreas, AllowTopOverflow);
            Point projected = FollowCursor ? new Point(cursor.X + (int)dx, cursor.Y + (int)dy) : new Point(desired.Left + desired.Width / 2, desired.Top);
            layoutPoint = projected;
            edgeAction = WindowLayoutPolicy.AtEdge(projected, monitorBounds,
                MaximizeAtTop && (Native.GetWindowLong(window, -16) & 0x10000) != 0, SnapAtSides && CanResize);
            return dispatcher.Submit(new MoveRequest {
                Window = window, X = position.X, Y = position.Y, Width = width, Height = height,
                OriginX = originX, OriginY = originY, CursorX = cursor.X, CursorY = cursor.Y,
                Resize = resize, FollowCursor = FollowCursor, BringToFront = BringToFront,
                ClearSnapRestore = clearSnapRestore,
                AllowTopCursorTravel = MaximizeAtTop && desired.Top < position.Y, TopCursorY = projected.Y
            });
        }
        public bool Resize(double scale)
        {
            if (!CanResize || !Native.IsWindow(window) || Native.IsIconic(window)) return false;
            edgeAction = WindowLayoutAction.None;
            var original = new Rectangle(originX, originY, width, height);
            var anchor = new Point(cursor.X, cursor.Y);
            Rectangle desired = ResizeGeometry.ScaleAroundAnchor(original, anchor, scale, new Size(200,120), maximumResize);
            Point position = KeepReachable(desired, workAreas, AllowTopOverflow);
            return dispatcher.Submit(new MoveRequest {
                Window = window, X = position.X, Y = position.Y, Width = desired.Width, Height = desired.Height,
                Resize = true, PinchResize = true, FollowCursor = FollowCursor, BringToFront = BringToFront,
                ClearSnapRestore = clearSnapRestore,
                AnchorX = Math.Max(0, Math.Min(1, (cursor.X - originX) / (double)width)),
                AnchorY = Math.Max(0, Math.Min(1, (cursor.Y - originY) / (double)height))
            });
        }
        public bool ResizeByEdges(double dx, double dy, ResizeEdges edges)
        {
            if (!CanResize || !Native.IsWindow(window) || Native.IsIconic(window)) return false;
            edgeAction = WindowLayoutAction.None;
            var original = new Rectangle(originX, originY, width, height);
            Rectangle desired = DirectionalResizeGeometry.Apply(original, (int)dx, (int)dy, edges, new Size(200, 120), maximumResize);
            Point position = KeepReachable(desired, workAreas, AllowTopOverflow);
            return dispatcher.Submit(new MoveRequest {
                Window = window, X = position.X, Y = position.Y, Width = desired.Width, Height = desired.Height,
                Resize = true, PinchResize = true, FollowCursor = FollowCursor, BringToFront = BringToFront, ClearSnapRestore = clearSnapRestore,
                AnchorX = Math.Clamp((cursor.X - originX) / (double)width, 0, 1), AnchorY = Math.Clamp((cursor.Y - originY) / (double)height, 0, 1)
            });
        }
        internal static uint MovementFlags(bool bringToFront, bool firstOperation, bool sizeMatches)
        {
            uint flags = 0x0010 | 0x0200;
            if (!bringToFront || !firstOperation) flags |= 0x0004;
            if (sizeMatches) flags |= 0x0001;
            return flags;
        }
        private MoveResult ApplyPosition(MoveRequest request)
        {
            if (!Native.IsWindow(request.Window) || Native.IsIconic(request.Window)) return new MoveResult { Error = 1400 };
            // Release commands also revalidate the target on the native worker.
            if (request.LayoutAction != WindowLayoutAction.None && !WindowPolicy.CanMove(request.Window)) return new MoveResult { Error = 5 };
            bool firstOperation = activatedWindow != request.Window || activatedGeneration != request.Generation;
            if (firstOperation)
            {
                if (request.BringToFront) ForegroundActivation.Raise(request.Window);
                activatedWindow = request.Window; activatedGeneration = request.Generation;
            }
            if (request.LayoutAction == WindowLayoutAction.Maximize)
            {
                if ((Native.GetWindowLong(request.Window, -16) & 0x10000) == 0) return new MoveResult { Error = 50 };
                // Move to the gesture's display before maximizing, including vertically stacked displays.
                Rectangle currentWork = MonitorLayout.ForWindow(request.Window, true);
                if (currentWork != request.LayoutWorkArea)
                {
                    if (Native.IsZoomed(request.Window)) Native.ShowWindow(request.Window, request.BringToFront ? 9 : 4);
                    Native.Rect previous;
                    if (!Native.GetWindowRect(request.Window, out previous)) return new MoveResult { Error = 1400 };
                    if (!Native.SetWindowPos(request.Window, IntPtr.Zero, request.LayoutWorkArea.Left + 16, request.LayoutWorkArea.Top + 16,
                        Math.Min(previous.Right - previous.Left, Math.Max(100, request.LayoutWorkArea.Width - 32)),
                        Math.Min(previous.Bottom - previous.Top, Math.Max(80, request.LayoutWorkArea.Height - 32)),
                        MovementFlags(request.BringToFront, firstOperation, false)))
                        return new MoveResult { Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error() };
                }
                Native.ShowWindow(request.Window, 3);
                Native.Rect maximized;
                if (!Native.GetWindowRect(request.Window, out maximized)) return new MoveResult { Error = 1400 };
                return new MoveResult { Success = true, X = maximized.Left, Y = maximized.Top };
            }
            if (request.LayoutAction == WindowLayoutAction.LeftHalf || request.LayoutAction == WindowLayoutAction.RightHalf)
            {
                if ((Native.GetWindowLong(request.Window, -16) & 0x00040000) == 0) return new MoveResult { Error = 50 };
                if (Native.IsZoomed(request.Window)) Native.ShowWindow(request.Window, request.BringToFront ? 9 : 4);
                Rectangle half = WindowLayoutPolicy.Half(request.LayoutWorkArea, request.LayoutAction);
                Native.Rect outer, visible;
                if (Native.GetWindowRect(request.Window, out outer) && Native.DwmGetWindowAttribute(request.Window, 9, out visible, 16) == 0 &&
                    visible.Right > visible.Left && visible.Bottom > visible.Top)
                    half = WindowLayoutPolicy.IncludeInvisibleBorders(half,
                        Rectangle.FromLTRB(outer.Left, outer.Top, outer.Right, outer.Bottom),
                        Rectangle.FromLTRB(visible.Left, visible.Top, visible.Right, visible.Bottom));
                request.X = half.X; request.Y = half.Y; request.Width = half.Width; request.Height = half.Height; request.Resize = true;
            }
            if (request.PinchResize && (Native.GetWindowLong(request.Window, -16) & 0x00040000) == 0) return new MoveResult { Error = 50 };
            if (request.Resize && Native.IsZoomed(request.Window)) Native.ShowWindow(request.Window, request.BringToFront ? 9 : 4);
            Native.Rect before;
            bool sizeMatches = !request.Resize || (Native.GetWindowRect(request.Window, out before) &&
                before.Right - before.Left == request.Width && before.Bottom - before.Top == request.Height);
            uint flags = MovementFlags(request.BringToFront, firstOperation, sizeMatches);
            // Synchronous on a dedicated worker: the target must finish before another call is sent.
            // SWP_ASYNCWINDOWPOS used to enqueue every historical sample in the target application.
            if (!Native.SetWindowPos(request.Window, IntPtr.Zero, request.X, request.Y, request.Width, request.Height, flags))
                return new MoveResult { Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error() };
            Native.Rect actual;
            if (!Native.GetWindowRect(request.Window, out actual)) return new MoveResult { Error = 1400 };
            if (request.LayoutAction == WindowLayoutAction.LeftHalf || request.LayoutAction == WindowLayoutAction.RightHalf)
                snapRestore.Remember(request.Window, request.LayoutRestoreBounds, Rectangle.FromLTRB(actual.Left, actual.Top, actual.Right, actual.Bottom));
            else if (request.ClearSnapRestore) snapRestore.Forget(request.Window);
            return new MoveResult { Success = true, X = actual.Left, Y = actual.Top, Width = actual.Right - actual.Left, Height = actual.Bottom - actual.Top };
        }
        private void CompletePosition(MoveRequest request, MoveResult result)
        {
            if (!result.Success)
            {
                int error = result.Error;
                ElevationRequired = error == 5 && !ElevationSupport.IsElevated;
                Status = ElevationRequired ? ElevationSupport.RequiredMessage : FailurePrefix + new Win32Exception(error).Message;
                return;
            }
            if (request.FollowCursor)
            {
                Point pointer = CursorDestination(new Point(request.CursorX, request.CursorY), new Point(request.OriginX, request.OriginY), new Point(result.X, result.Y));
                if (request.PinchResize)
                    pointer = new Point(result.X + (int)Math.Round(result.Width * request.AnchorX), result.Y + (int)Math.Round(result.Height * request.AnchorY));
                if (request.AllowTopCursorTravel) pointer.Y = Math.Min(pointer.Y, request.TopCursorY);
                Native.SetCursorPos(pointer.X, pointer.Y);
            }
            System.Threading.Interlocked.Increment(ref Moves);
        }
        // Anchor to the original cursor, rather than repeatedly adding to the current cursor.
        // Use the constrained window position so the pointer stops when the window hits a boundary.
        internal static Point CursorDestination(Point initialCursor, Point windowOrigin, Point windowPosition)
        {
            return new Point(initialCursor.X + windowPosition.X - windowOrigin.X,
                initialCursor.Y + windowPosition.Y - windowOrigin.Y);
        }
        public void Release()
        {
            dispatcher.Cancel();
            edgeAction = WindowLayoutAction.None;
            window = IntPtr.Zero;
            if (Status.StartsWith("이동 중:")) Status = "이동 완료 · 손가락을 떼면 멈춥니다.";
        }
        public void Finish(WindowLayoutAction action = WindowLayoutAction.None)
        {
            IntPtr target = window;
            // Flick actions are independent of the screen-edge toggles.
            if (action == WindowLayoutAction.None) action = edgeAction;
            Rectangle destination = MonitorLayout.ForPoint(layoutPoint, true);
            Rectangle restoreBounds = layoutRestoreBounds;
            Release();
            if (action != WindowLayoutAction.None && Eligible(target))
                dispatcher.Submit(new MoveRequest { Window = target, LayoutAction = action, LayoutWorkArea = destination,
                    LayoutRestoreBounds = restoreBounds, BringToFront = BringToFront });
        }
        internal bool WaitForIdle(int timeoutMs) { return dispatcher.WaitForIdle(timeoutMs); }
        public void Dispose() { Release(); dispatcher.Dispose(); }
    }
}
