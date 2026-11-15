using System;
using System.Collections.Generic;

namespace ThreeFingerWindowDrag
{
    internal sealed class Contact
    {
        public int Id;
        public double X, Y;
        public bool Tip, Confident;
    }
    internal sealed class ContactFrame
    {
        public IntPtr Device;
        public readonly List<Contact> Contacts = new List<Contact>();
    }
    internal sealed class ReportPart
    {
        public int Count, Scan;
        public readonly List<Contact> Contacts = new List<Contact>();
    }

    // Reassembles parallel and hybrid HID reports. Count includes lifted contacts.
    internal sealed class FrameAssembler
    {
        private int expected, scan;
        private readonly List<Contact> pending = new List<Contact>(10);
        private readonly List<Contact> complete = new List<Contact>(10);
        private readonly Contact[] storage = new Contact[10];
        private long started;
        internal FrameAssembler() { for (int i = 0; i < storage.Length; i++) storage[i] = new Contact(); }
        public void Reset() { expected = 0; pending.Clear(); }
        // The returned view is consumed synchronously and remains valid until the next Push.
        public List<Contact> Push(ReportPart part, long now)
        {
            complete.Clear();
            if (expected > 0 && (now - started > 100 || part.Scan != scan)) Reset();
            if (part.Count > 0)
            {
                Reset();
                if (part.Count > 10) return null;
                expected = part.Count; scan = part.Scan; started = now;
            }
            else if (expected == 0)
            {
                // A true empty frame, rather than a hybrid continuation.
                return complete;
            }
            foreach (Contact contact in part.Contacts)
            {
                if (pending.Count >= expected) break;
                foreach (Contact existing in pending) if (existing.Id == contact.Id) { Reset(); return null; }
                // Decoder slots are reused; copy values so hybrid continuation cannot overwrite them.
                Contact saved = storage[pending.Count];
                saved.Id = contact.Id; saved.X = contact.X; saved.Y = contact.Y;
                saved.Tip = contact.Tip; saved.Confident = contact.Confident;
                pending.Add(saved);
            }
            if (pending.Count < expected) return null;
            complete.AddRange(pending);
            Reset();
            return complete;
        }
    }

    internal interface IWindowMover
    {
        bool CanResize { get; }
        bool Prepare();
        bool Capture(bool restoreSnap = true);
        bool Move(double dx, double dy);
        bool Resize(double scale);
        bool ResizeByEdges(double dx, double dy, ResizeEdges edges);
        void Finish(WindowLayoutAction action = WindowLayoutAction.None);
        void Release();
    }

    // All gesture state is confined to the input thread and one touchpad at a time.
    internal sealed class GestureEngine
    {
        private readonly IWindowMover mover;
        private struct ContactSample { internal int Id; internal double X, Y; }
        private readonly ContactSample[] previous = new ContactSample[3];
        private readonly List<Contact> active = new List<Contact>(4);
        private bool hasPrevious;
        private IntPtr owner;
        private long lastTime, contactStarted;
        private double accumulatedX, accumulatedY, sentX, sentY;
        private bool captured, blocked;
        private enum GestureMode { Undecided, Move, Resize, DirectionalResize }
        private GestureMode mode;
        private double initialSpan, sentScale;
        private bool pinchAllowed;
        private ResizeEdges resizeEdges;
        public DirectionalResizeOptions DirectionalResize = new DirectionalResizeOptions();
        private readonly FlickRecognizer flick = new FlickRecognizer();
        public bool Enabled = true;
        public bool PinchResizeEnabled = true;
        public bool FlickGesturesEnabled = true;
        public int FlickSensitivity = 5;
        public double PixelsPerMm = 12;
        public double ThresholdMm = 0.65;
        public bool Dragging { get { return captured; } }
        public bool Tracking { get { return hasPrevious; } }
        public bool Resizing { get { return captured && (mode == GestureMode.Resize || mode == GestureMode.DirectionalResize); } }
        public bool DirectionalResizing { get { return captured && mode == GestureMode.DirectionalResize; } }
        public bool ResizeReady { get; private set; }
        public GestureEngine(IWindowMover windowMover) { mover = windowMover; }

        public void Cancel()
        {
            mover.Release(); Reset(); blocked = true;
        }
        private void Reset()
        {
            hasPrevious = false; active.Clear(); owner = IntPtr.Zero;
            accumulatedX = accumulatedY = sentX = sentY = 0;
            captured = false;
            mode = GestureMode.Undecided; initialSpan = 0; sentScale = 1;
            flick.Reset();
            resizeEdges = ResizeEdges.None; ResizeReady = false;
        }
        public void Tick(long now)
        {
            if (hasPrevious && now - lastTime > 450) Cancel();
            ResizeReady = hasPrevious && mode == GestureMode.Undecided && IsDirectionalHoldReady(now);
        }
        private bool IsDirectionalHoldReady(long now)
        {
            return mover.CanResize && DirectionalResize.Enabled && now - contactStarted >= DirectionalResize.HoldMilliseconds;
        }
        public void Process(ContactFrame frame, long now, bool cancel)
        {
            if (owner != IntPtr.Zero && owner != frame.Device && hasPrevious && now - lastTime <= 450) return;
            if (cancel || !Enabled) { Cancel(); return; }
            active.Clear(); bool palm = false;
            foreach (Contact contact in frame.Contacts)
            {
                if (!contact.Tip) continue;
                if (!contact.Confident) palm = true;
                else active.Add(contact);
            }
            if (active.Count != 3 || palm)
            {
                if (captured && active.Count < 3 && !palm)
                {
                    mover.Finish(FlickGesturesEnabled && mode == GestureMode.Move ? flick.OnRelease(now, FlickSensitivity) : WindowLayoutAction.None);
                    Reset();
                }
                // Idle/lift continuation frames must not lock the motion queue or cancel a released flick.
                else if (hasPrevious) Cancel();
                blocked = false; return;
            }
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < i; j++)
                    if (active[i].Id == active[j].Id) { Cancel(); return; }
            if (blocked) return; // Escape/pause cannot restart until fingers are lifted.
            if (!hasPrevious || owner != frame.Device || now - lastTime > 450)
            {
                owner = frame.Device; lastTime = now; contactStarted = now;
                RememberContacts();
                accumulatedX = accumulatedY = sentX = sentY = 0;
                // Lock the window at the beginning of the three-finger contact.
                captured = false;
                if (!mover.Prepare()) { Cancel(); return; }
                initialSpan = ResizeGeometry.FingerSpan(active);
                pinchAllowed = PinchResizeEnabled && mover.CanResize && initialSpan >= 2;
                mode = GestureMode.Undecided; sentScale = 1;
                flick.Reset(); flick.Add(now, 0, 0);
                resizeEdges = ResizeEdges.None; ResizeReady = false;
                return;
            }
            lastTime = now;
            double dx = 0, dy = 0;
            for (int i = 0; i < 3; i++)
            {
                Contact contact = active[i]; int match = -1;
                for (int j = 0; j < 3; j++) if (previous[j].Id == contact.Id) { match = j; break; }
                if (match < 0) { Cancel(); return; }
                double x = contact.X - previous[match].X, y = contact.Y - previous[match].Y;
                if (Math.Abs(x) > 25 || Math.Abs(y) > 25) { Cancel(); return; }
                dx += x; dy += y;
            }
            dx /= 3; dy /= 3;
            RememberContacts();
            // A corrupt/stale frame or contact jump must never throw a window across the screen.
            if (Math.Abs(dx) > 25 || Math.Abs(dy) > 25) { Cancel(); return; }
            accumulatedX += dx; accumulatedY += dy;
            if (FlickGesturesEnabled && (mode == GestureMode.Undecided || mode == GestureMode.Move)) flick.Add(now, accumulatedX, accumulatedY);
            double span = mode == GestureMode.Undecided || mode == GestureMode.Resize ? ResizeGeometry.FingerSpan(active) : initialSpan;
            if (mode == GestureMode.Undecided)
            {
                double translation = Math.Sqrt(accumulatedX * accumulatedX + accumulatedY * accumulatedY);
                double radial = Math.Abs(span - initialSpan);
                ResizeReady = IsDirectionalHoldReady(now);
                // Choose once, so a normal drag cannot turn into an accidental resize midway.
                if (ResizeReady && translation >= ThresholdMm)
                    mode = GestureMode.DirectionalResize;
                else if (pinchAllowed && radial >= Math.Max(0.65, initialSpan * 0.06) && radial >= translation * 0.7)
                    mode = GestureMode.Resize;
                else if (translation >= ThresholdMm && (!pinchAllowed || translation >= radial * 1.6 || translation >= 1.8))
                    mode = GestureMode.Move;
                else return;
            }
            if (!captured)
            {
                if (!mover.Capture(mode == GestureMode.Move)) { Cancel(); return; }
                captured = true;
                ResizeReady = false;
            }
            if (mode == GestureMode.Resize)
            {
                double scale = span / initialSpan;
                if (Math.Abs(scale - sentScale) >= 0.001)
                {
                    if (!mover.Resize(scale)) { Cancel(); return; }
                    sentScale = scale;
                }
                return;
            }
            double targetX = Math.Round(accumulatedX * PixelsPerMm);
            double targetY = Math.Round(accumulatedY * PixelsPerMm);
            if (mode == GestureMode.DirectionalResize)
            {
                ResizeEdges beforeEdges = resizeEdges;
                bool lowerCorners = DirectionalResize.Behavior == ResizeBehavior.LowerCorners;
                if (lowerCorners)
                {
                    if (Math.Abs(accumulatedY) >= ThresholdMm) resizeEdges |= ResizeEdges.Bottom;
                    // Wait for both axes before choosing a lower corner, so a slightly earlier
                    // horizontal sample cannot make an upward diagonal grow the wrong side.
                    if ((resizeEdges & (ResizeEdges.Left | ResizeEdges.Right)) == 0 &&
                        Math.Abs(accumulatedX) >= ThresholdMm && Math.Abs(accumulatedY) >= ThresholdMm)
                        resizeEdges |= (accumulatedX < 0) == (accumulatedY < 0) ? ResizeEdges.Right : ResizeEdges.Left;
                }
                else
                {
                    if ((resizeEdges & (ResizeEdges.Left | ResizeEdges.Right)) == 0 && Math.Abs(accumulatedX) >= ThresholdMm)
                        resizeEdges |= accumulatedX < 0 ? ResizeEdges.Left : ResizeEdges.Right;
                    if ((resizeEdges & (ResizeEdges.Top | ResizeEdges.Bottom)) == 0 && Math.Abs(accumulatedY) >= ThresholdMm)
                        resizeEdges |= accumulatedY < 0 ? ResizeEdges.Top : ResizeEdges.Bottom;
                }
                if ((!lowerCorners || resizeEdges != ResizeEdges.None) &&
                    (targetX != sentX || targetY != sentY || (lowerCorners && resizeEdges != beforeEdges)) &&
                    !mover.ResizeByEdges(targetX, targetY, resizeEdges)) { Cancel(); return; }
                sentX = targetX; sentY = targetY;
                return;
            }
            if ((targetX != sentX || targetY != sentY) && !mover.Move(targetX, targetY)) { Cancel(); return; }
            sentX = targetX; sentY = targetY;
        }
        private void RememberContacts()
        {
            for (int i = 0; i < 3; i++) previous[i] = new ContactSample { Id = active[i].Id, X = active[i].X, Y = active[i].Y };
            hasPrevious = true;
        }
    }
}
