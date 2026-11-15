using System;
using System.Threading;

namespace ThreeFingerWindowDrag
{
    internal struct MoveRequest
    {
        internal IntPtr Window;
        internal int X, Y, Width, Height, OriginX, OriginY, CursorX, CursorY;
        internal bool Resize, FollowCursor, AllowTopCursorTravel, BringToFront, PinchResize;
        internal WindowLayoutAction LayoutAction;
        internal System.Drawing.Rectangle LayoutWorkArea;
        internal System.Drawing.Rectangle LayoutRestoreBounds;
        internal bool ClearSnapRestore;
        internal double AnchorX, AnchorY;
        internal int TopCursorY;
        internal long Generation;
    }
    internal struct MoveResult
    {
        internal bool Success;
        internal int X, Y, Width, Height, Error;
    }

    // Exactly one native operation in flight and one replaceable target. Never a FIFO of positions.
    internal sealed class LatestMoveDispatcher : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly ManualResetEventSlim idle = new ManualResetEventSlim(true);
        private readonly Thread thread;
        private readonly Func<MoveRequest, MoveResult> apply;
        private readonly Action<MoveRequest, MoveResult> complete;
        private MoveRequest pending;
        private bool hasPending, stopped;
        private long generation, submitted, applied, replaced;
        internal long Submitted { get { return Interlocked.Read(ref submitted); } }
        internal long Applied { get { return Interlocked.Read(ref applied); } }
        internal long Replaced { get { return Interlocked.Read(ref replaced); } }

        internal LatestMoveDispatcher(Func<MoveRequest, MoveResult> nativeApply, Action<MoveRequest, MoveResult> onComplete)
        {
            apply = nativeApply; complete = onComplete;
            thread = new Thread(Run) { IsBackground = true, Name = "Glide.WindowMotion" };
            thread.Start();
        }
        internal bool Submit(MoveRequest request)
        {
            lock (gate)
            {
                if (stopped) return false;
                request.Generation = generation;
                if (hasPending) Interlocked.Increment(ref replaced);
                pending = request; hasPending = true;
                Interlocked.Increment(ref submitted);
                idle.Reset(); wake.Set();
                return true;
            }
        }
        internal void Cancel()
        {
            lock (gate) { generation++; hasPending = false; }
        }
        internal bool WaitForIdle(int timeoutMs) { return idle.Wait(timeoutMs); }
        private void Run()
        {
            try
            {
                while (true)
                {
                    MoveRequest request;
                    lock (gate)
                    {
                        if (stopped) return;
                        if (!hasPending) { idle.Set(); request = default(MoveRequest); }
                        else { request = pending; hasPending = false; }
                    }
                    if (request.Window == IntPtr.Zero) { wake.WaitOne(); continue; }
                    MoveResult result;
                    try { result = apply(request); }
                    catch { result = new MoveResult { Error = -1 }; }
                    Interlocked.Increment(ref applied);
                    lock (gate)
                    {
                        // Release/disable invalidates both the pending target and late cursor feedback.
                        if (!stopped && request.Generation == generation) complete(request, result);
                    }
                }
            }
            finally { idle.Set(); wake.Dispose(); }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true; generation++; hasPending = false; wake.Set();
            }
            // A hung foreign app may hold the one native call; input/UI threads never wait on it.
            thread.Join(100);
        }
    }
}
