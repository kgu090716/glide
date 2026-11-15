using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ThreeFingerWindowDrag
{
    internal static class LatencyChecks
    {
        private static void Require(bool condition, string error) { if (!condition) throw new Exception(error); }
        internal static void LatestTargetWins()
        {
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            var targets = new List<int>();
            using var moves = new LatestMoveDispatcher(request => {
                lock (targets) targets.Add(request.X);
                if (request.X == 0) { entered.Set(); resume.Wait(2000); }
                return new MoveResult { Success = true, X = request.X };
            }, (_, _) => { });
            moves.Submit(new MoveRequest { Window = new IntPtr(1), X = 0 });
            Require(entered.Wait(1000), "worker did not begin");
            for (int i = 1; i <= 1000; i++) moves.Submit(new MoveRequest { Window = new IntPtr(1), X = i });
            moves.Submit(new MoveRequest { Window = new IntPtr(1), X = -57 });
            resume.Set();
            Require(moves.WaitForIdle(2000), "worker did not become idle");
            Require(targets.Count == 2 && targets[1] == -57, "historical/reversed coordinates were replayed");
            Require(moves.Replaced == 1000, "unbounded target backlog");
        }
        internal static void ReleaseDropsPendingAndCursorFeedback()
        {
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            int applied = 0, completed = 0;
            using var moves = new LatestMoveDispatcher(request => {
                Interlocked.Increment(ref applied); entered.Set(); resume.Wait(2000);
                return new MoveResult { Success = true };
            }, (_, _) => Interlocked.Increment(ref completed));
            moves.Submit(new MoveRequest { Window = new IntPtr(1), X = 1 });
            Require(entered.Wait(1000), "worker did not begin");
            moves.Submit(new MoveRequest { Window = new IntPtr(1), X = 2 });
            moves.Cancel(); resume.Set();
            Require(moves.WaitForIdle(2000), "worker did not stop");
            Require(applied == 1 && completed == 0, "release allowed stale movement/cursor feedback");
            moves.Submit(new MoveRequest { Window = new IntPtr(1), X = 3 });
            Require(moves.WaitForIdle(1000) && completed == 1, "next gesture failed after cancellation");
        }
        internal static void InputThreadSurvivesBusyCaller()
        {
            using var controller = new InputController(false, 12, true, true);
            long before = controller.State.TimerTicks;
            controller.Configure(false, 24, false, true);
            Thread.Sleep(320); // Simulate a blocked/rendering WinUI thread.
            Require(controller.State.TimerTicks > before && controller.State.TimerTicks <= before + 4, "idle input watchdog stalled or still wakes too often");
            Require(controller.State.TimerIntervalMilliseconds == 125, "idle watchdog did not slow down");
            Require(controller.State.Speed == 24 && !controller.State.FollowCursor, "configuration did not reach input thread");
        }
        internal static void NativeMotion()
        {
            Require(Native.GetCursorPos(out Native.Point saved), "get cursor");
            try
            {
                using var target = new SlowWindow(true);
                using var mover = new WindowMover { FollowCursor = true, BringToFront = false };
                Native.GetWindowRect(target.Handle, out Native.Rect before);
                var cursor = new Native.Point(before.Left + 180, before.Top + 140);
                Require(Native.SetCursorPos(cursor.X, cursor.Y), "set cursor");
                Require(WindowPolicy.CanMove(target.Handle), "own test window was denied by process policy");
                Require(mover.PrepareWindow(target.Handle, cursor) && mover.Capture(), "capture target; visible=" + Native.IsWindowVisible(target.Handle) + "; iconic=" + Native.IsIconic(target.Handle) + "; elevated=" + ElevationSupport.IsElevated + "; needs elevation=" + mover.ElevationRequired);
                Require(mover.Move(24, 18) && mover.WaitForIdle(2000), "move target");
                Native.GetWindowRect(target.Handle, out Native.Rect after);
                Native.GetCursorPos(out Native.Point pointer);
                Require(after.Left - before.Left == 24 && after.Top - before.Top == 18, "window displacement");
                Require(pointer.X - cursor.X == 24 && pointer.Y - cursor.Y == 18, "cursor displacement: expected " + (cursor.X+24) + "," + (cursor.Y+18) + "; got " + pointer.X + "," + pointer.Y);
                Require(mover.Move(36, 25) && mover.WaitForIdle(2000), "second move");
                Native.GetCursorPos(out pointer);
                Require(pointer.X - cursor.X == 36 && pointer.Y - cursor.Y == 25, "cursor drift");
                mover.FollowCursor = false;
                Native.GetCursorPos(out Native.Point beforeOff);
                Require(mover.Move(48, 30) && mover.WaitForIdle(2000), "follow-off move");
                Native.GetCursorPos(out pointer);
                Require(pointer.X == beforeOff.X && pointer.Y == beforeOff.Y, "follow-off changed cursor");
            }
            finally { Native.SetCursorPos(saved.X, saved.Y); }
        }
        internal static void NativeQueueBenchmark()
        {
            const int samples = 80;
            using var target = new SlowWindow(false);
            target.DelayMs = 8;
            for (int i = 0; i < samples; i++)
            {
                Native.SetWindowPos(target.Handle, IntPtr.Zero, 100 + i, 100, 480, 280, 0x4000 | 0x0015);
                PaceOneMillisecond();
            }
            var oldTail = Stopwatch.StartNew();
            Native.Rect lastPosition = default(Native.Rect);
            while (oldTail.ElapsedMilliseconds < 8000)
            {
                Native.GetWindowRect(target.Handle, out lastPosition);
                if (lastPosition.Left == 100 + samples - 1) break;
                Thread.Sleep(1);
            }
            oldTail.Stop();
            Require(oldTail.ElapsedMilliseconds < 8000, "legacy native queue did not drain; last x=" + lastPosition.Left + "; expected " + (100 + samples - 1));
            int completed = 0;
            using var moves = new LatestMoveDispatcher(request => {
                bool ok = Native.SetWindowPos(request.Window, IntPtr.Zero, request.X, 100, 480, 280, 0x0015);
                return new MoveResult { Success = ok };
            }, (_, _) => Interlocked.Increment(ref completed));
            for (int i = 0; i < samples; i++)
            {
                moves.Submit(new MoveRequest { Window = target.Handle, X = 100 + i });
                PaceOneMillisecond();
            }
            var newTail = Stopwatch.StartNew();
            moves.Cancel();
            Require(moves.WaitForIdle(1000), "latest-target movement did not stop");
            newTail.Stop();
            Require(newTail.ElapsedMilliseconds < 200, "movement tail is still a backlog");
            string report = "Controlled native target: 80 updates, 8 ms WM_WINDOWPOSCHANGING delay.\r\n" +
                "Legacy asynchronous tail after submission: " + oldTail.Elapsed.TotalMilliseconds.ToString("F1") + " ms\r\n" +
                "Latest-target tail after release: " + newTail.Elapsed.TotalMilliseconds.ToString("F1") + " ms\r\n" +
                "Native operations: " + moves.Applied + "/" + samples + "; superseded: " + moves.Replaced + "\r\n";
            Console.WriteLine(report);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "latency-benchmark.txt"), report);
            Require(moves.Applied < samples / 2, "superseded coordinates were not coalesced");
        }
        private static void PaceOneMillisecond()
        {
            long until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
            while (Stopwatch.GetTimestamp() < until) Thread.SpinWait(16);
        }
        internal sealed class SlowWindow : IDisposable
        {
            internal IntPtr Handle;
            internal volatile int DelayMs;
            private readonly Thread thread;
            private readonly ManualResetEventSlim ready = new ManualResetEventSlim();
            private uint threadId;
            private Exception error;
            internal SlowWindow(bool visible)
            {
                thread = new Thread(() => Run(visible)) { IsBackground = true };
                thread.Start(); Require(ready.Wait(2000), "test window startup timed out");
                if (error != null) throw error;
            }
            private void Run(bool visible)
            {
                string name = "Glide.LatencyTest." + Guid.NewGuid().ToString("N");
                Native.WindowProcedure procedure = (h, msg, wp, lp) => {
                    if (msg == 0x46 && DelayMs > 0) Thread.Sleep(DelayMs);
                    return Native.DefWindowProc(h, msg, wp, lp);
                };
                var wc = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Name = name, Procedure = procedure, Instance = Native.GetModuleHandle(null) };
                try
                {
                    threadId = Native.GetCurrentThreadId();
                    Require(Native.RegisterClassEx(ref wc) != 0, "register test window");
                    Rectangle area = MonitorLayout.WorkAreas()[0];
                    Handle = Native.CreateWindowEx(0, name, "Glide movement verification", visible ? 0x10CF0000u : 0x00CF0000u,
                        area.Left + 160, area.Top + 160, 480, 280, IntPtr.Zero, IntPtr.Zero, wc.Instance, IntPtr.Zero);
                    Require(Handle != IntPtr.Zero, "create test window");
                    // An elevated harness can start hidden; explicitly show only its own visible fixture.
                    if (visible) Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0053);
                    ready.Set();
                    Native.PumpMessage message;
                    while (Native.GetMessage(out message, IntPtr.Zero, 0, 0) > 0) { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
                }
                catch (Exception ex) { error = ex; ready.Set(); }
                finally
                {
                    if (Handle != IntPtr.Zero) Native.DestroyWindow(Handle);
                    Native.UnregisterClass(name, wc.Instance); GC.KeepAlive(procedure);
                }
            }
            public void Dispose() { Native.PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero); thread.Join(2000); ready.Dispose(); }
        }
    }
}
