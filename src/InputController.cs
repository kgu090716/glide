using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace ThreeFingerWindowDrag
{
    internal sealed class InputState
    {
        internal bool Enabled, Dragging, Hotkey, TopEdgeArmed, FollowCursor = true, MaximizeAtTop = true;
        internal bool Resizing, BringToFront = true, AllowTopOverflow = true, PinchResize = true;
        internal bool SnapAtSides = true, FlickGestures = true;
        internal int FlickSensitivity = 5;
        internal bool DirectionalResize, DirectionalResizing, ResizeReady;
        internal bool ElevationRequired;
        internal int ResizeHoldMilliseconds;
        internal ResizeBehavior ResizeBehavior;
        internal WindowLayoutAction EdgeAction;
        internal int DeviceCount, Contacts, Speed = 12;
        internal int TimerIntervalMilliseconds;
        internal long Reports, TimerTicks, NativeMoves, Coalesced;
        internal double MaxFrameMs;
        internal string Error, DeviceDetails;
    }

    // Owns Raw Input and all gesture state on a native message loop, independent of WinUI.
    internal sealed class InputController : IDisposable
    {
        private const uint CommandMessage = 0x8000 + 41;
        private readonly ConcurrentQueue<Action> commands = new ConcurrentQueue<Action>();
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim();
        private readonly Thread thread;
        private InputState state = new InputState();
        private RawTouchpad input;
        private WindowMover mover;
        private GestureEngine engine;
        private uint threadId;
        private UIntPtr timer;
        private int timerInterval;
        private Exception startupError;
        private volatile bool stopping;
        private long ticks, publishedAt, maxFrameTicks;
        private string deviceDetails;
        internal event Action ToggleRequested;
        internal InputState State { get { return Volatile.Read(ref state); } }

        internal InputController(bool enabled, int speed, bool follow, bool restore, bool maximizeAtTop = true,
            bool bringToFront = true, bool allowTopOverflow = true, bool pinchResize = true,
            bool snapAtSides = true, bool flickGestures = true, int flickSensitivity = 5, DirectionalResizeOptions resizeOptions = null)
        {
            DirectionalResizeOptions resize = (resizeOptions ?? new DirectionalResizeOptions()).Snapshot();
            thread = new Thread(() => Run(enabled, speed, follow, restore, maximizeAtTop, bringToFront, allowTopOverflow, pinchResize, snapAtSides, flickGestures, flickSensitivity, resize)) {
                IsBackground = true, Name = "Glide.TouchpadInput"
            };
            thread.Start();
            if (!ready.Wait(10000)) throw new TimeoutException("터치패드 입력 처리를 시작하지 못했습니다.");
            if (startupError != null) throw new InvalidOperationException("터치패드 초기화 실패", startupError);
        }
        private void Run(bool enabled, int speed, bool follow, bool restore, bool maximizeAtTop, bool bringToFront, bool allowTopOverflow, bool pinchResize,
            bool snapAtSides, bool flickGestures, int flickSensitivity, DirectionalResizeOptions resizeOptions)
        {
            try
            {
                threadId = Native.GetCurrentThreadId();
                mover = new WindowMover { FollowCursor = follow, RestoreMaximized = restore, MaximizeAtTop = maximizeAtTop,
                    BringToFront = bringToFront, AllowTopOverflow = allowTopOverflow, SnapAtSides = snapAtSides };
                engine = new GestureEngine(mover) { Enabled = enabled, PixelsPerMm = speed, PinchResizeEnabled = pinchResize,
                    FlickGesturesEnabled = flickGestures, FlickSensitivity = flickSensitivity, DirectionalResize = resizeOptions };
                input = new RawTouchpad();
                input.Frame += OnFrame;
                input.Toggle += () => { Action handler = ToggleRequested; if (handler != null) handler(); };
                input.InputFault += () => { engine.Cancel(); UpdateTimerInterval(); };
                input.DevicesChanged += () => { engine.Cancel(); UpdateTimerInterval(); ReadDeviceDetails(); Publish(); };
                ReadDeviceDetails();
                UpdateTimerInterval();
                Publish(); ready.Set();
                Native.PumpMessage message;
                while (Native.GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                {
                    if (message.Message == CommandMessage)
                    {
                        Action command;
                        while (commands.TryDequeue(out command)) command();
                        Publish();
                    }
                    else if (message.Message == 0x113)
                    {
                        ticks++;
                        if (engine.Tracking)
                        {
                            if ((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) engine.Cancel();
                            engine.Tick(input.Now);
                        }
                        UpdateTimerInterval();
                        if (input.Now - publishedAt >= 100) Publish();
                    }
                    else
                    {
                        Native.TranslateMessage(ref message);
                        Native.DispatchMessage(ref message);
                    }
                }
            }
            catch (Exception ex)
            {
                startupError = ex;
                Volatile.Write(ref state, new InputState { Error = ex.Message });
                ready.Set();
            }
            finally
            {
                if (timer != UIntPtr.Zero) Native.KillTimer(IntPtr.Zero, timer);
                if (engine != null) engine.Cancel();
                if (input != null) input.Dispose();
                if (mover != null) mover.Dispose();
            }
        }
        private void OnFrame(ContactFrame frame, long now)
        {
            long start = Stopwatch.GetTimestamp();
            bool cancel = (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0 ||
                (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0;
            engine.Process(frame, now, cancel);
            UpdateTimerInterval();
            maxFrameTicks = Math.Max(maxFrameTicks, Stopwatch.GetTimestamp() - start);
        }
        private void ReadDeviceDetails()
        {
            StringBuilder text = new StringBuilder();
            foreach (TouchpadDevice d in input.Devices)
                text.AppendLine(d.Name + "\r\nReport bytes: " + d.Caps.InputLength + "; slots: " + d.Decoder.Slots.Count);
            deviceDetails = text.ToString();
        }
        private void UpdateTimerInterval()
        {
            int interval = engine.Tracking ? 16 : 125;
            if (interval == timerInterval) return;
            UIntPtr updated = Native.SetTimer(IntPtr.Zero, timer, (uint)interval, IntPtr.Zero);
            if (updated == UIntPtr.Zero) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            timer = updated; timerInterval = interval;
        }
        private void Publish()
        {
            publishedAt = input.Now;
            string error = input.LastError; long reports = 0;
            foreach (TouchpadDevice device in input.Devices)
            {
                reports += device.Reports;
                if (error == null && !string.IsNullOrEmpty(device.LastError)) error = device.LastError;
            }
            if (error == null && mover.Status.StartsWith(WindowMover.FailurePrefix, StringComparison.Ordinal)) error = mover.Status;
            if (error == null && mover.ElevationRequired) error = ElevationSupport.RequiredMessage;
            Volatile.Write(ref state, new InputState {
                Enabled = engine.Enabled, Dragging = engine.Dragging, Hotkey = input.HotkeyRegistered, TopEdgeArmed = mover.TopEdgeArmed,
                ElevationRequired = mover.ElevationRequired,
                MaximizeAtTop = mover.MaximizeAtTop,
                DirectionalResize = engine.DirectionalResize.Enabled, DirectionalResizing = engine.DirectionalResizing, ResizeReady = engine.ResizeReady,
                ResizeHoldMilliseconds = engine.DirectionalResize.HoldMilliseconds,
                ResizeBehavior = engine.DirectionalResize.Behavior,
                EdgeAction = mover.EdgeAction, SnapAtSides = mover.SnapAtSides, FlickGestures = engine.FlickGesturesEnabled, FlickSensitivity = engine.FlickSensitivity,
                Resizing = engine.Resizing, BringToFront = mover.BringToFront, AllowTopOverflow = mover.AllowTopOverflow, PinchResize = engine.PinchResizeEnabled,
                FollowCursor = mover.FollowCursor, Speed = (int)engine.PixelsPerMm,
                DeviceCount = input.DeviceCount,
                Contacts = input.LastFrameTime >= 0 && input.Now - input.LastFrameTime < 450 ? input.ContactCount : 0,
                Reports = reports, TimerTicks = ticks, TimerIntervalMilliseconds = timerInterval,
                NativeMoves = mover.NativeAttempts, Coalesced = mover.Coalesced,
                MaxFrameMs = maxFrameTicks * 1000.0 / Stopwatch.Frequency,
                Error = error, DeviceDetails = deviceDetails
            });
        }
        private void Post(Action command)
        {
            if (stopping) return;
            commands.Enqueue(command);
            Native.PostThreadMessage(threadId, CommandMessage, IntPtr.Zero, IntPtr.Zero);
        }
        internal void Configure(bool enabled, int speed, bool follow, bool restore, bool maximizeAtTop = true,
            bool bringToFront = true, bool allowTopOverflow = true, bool pinchResize = true,
            bool snapAtSides = true, bool flickGestures = true, int flickSensitivity = 5, DirectionalResizeOptions resizeOptions = null)
        {
            DirectionalResizeOptions resize = (resizeOptions ?? new DirectionalResizeOptions()).Snapshot();
            Post(() => {
                engine.Cancel();
                engine.Enabled = enabled; engine.PixelsPerMm = speed;
                mover.FollowCursor = follow; mover.RestoreMaximized = restore; mover.MaximizeAtTop = maximizeAtTop;
                mover.BringToFront = bringToFront; mover.AllowTopOverflow = allowTopOverflow; engine.PinchResizeEnabled = pinchResize;
                mover.SnapAtSides = snapAtSides; engine.FlickGesturesEnabled = flickGestures; engine.FlickSensitivity = Math.Clamp(flickSensitivity, 1, 10);
                engine.DirectionalResize = resize;
                UpdateTimerInterval();
            });
        }
        internal void RefreshDevices()
        { Post(() => { engine.Cancel(); UpdateTimerInterval(); input.RefreshDevices(); ReadDeviceDetails(); }); }
        public void Dispose()
        {
            if (stopping) return;
            stopping = true;
            Native.PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero);
            thread.Join(1000);
        }
    }
}
