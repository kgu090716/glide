using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ThreeFingerWindowDrag
{
    internal sealed class FakeMover : IWindowMover
    {
        public int Prepares, Captures, Moves, Finishes, Resizes, Releases;
        public double X, Y, Scale = 1;
        public bool SupportsResize = true;
        public bool CanResize { get { return SupportsResize; } }
        public bool Allow = true;
        public WindowLayoutAction Action;
        public bool RestoreSnap;
        public int EdgeResizes;
        public ResizeEdges Edges;
        public bool Prepare() { Prepares++; return Allow; }
        public bool Capture(bool restoreSnap = true) { Captures++; RestoreSnap = restoreSnap; return Allow; }
        public bool Move(double x, double y) { Moves++; X = x; Y = y; return Allow; }
        public bool Resize(double scale) { Resizes++; Scale = scale; return Allow; }
        public bool ResizeByEdges(double dx, double dy, ResizeEdges edges) { EdgeResizes++; X = dx; Y = dy; Edges = edges; return Allow; }
        public void Finish(WindowLayoutAction action = WindowLayoutAction.None) { Finishes++; Action = action; }
        public void Release() { Releases++; }
    }
    internal static class TestRunner
    {
        [DllImport("hid.dll")] private static extern int HidP_InitializeReportForID(int type, byte id, IntPtr preparsed, byte[] report, uint length);
        [DllImport("hid.dll")] private static extern int HidP_SetData(int type, [In, Out] Native.HidData[] data, ref uint count, IntPtr preparsed, byte[] report, uint length);
        private static int passed;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        private static void Test(string name, Action test)
        { test(); passed++; Console.WriteLine("PASS " + name); }
        private static Contact C(int id, double x, double y, bool tip = true, bool confidence = true)
        { return new Contact { Id = id, X = x, Y = y, Tip = tip, Confident = confidence }; }
        private static ContactFrame F(double x, int n = 3, int idOffset = 0, int device = 1)
        {
            ContactFrame frame = new ContactFrame { Device = new IntPtr(device) };
            for (int i = 0; i < n; i++) frame.Contacts.Add(C(i + idOffset, x + i * 10, 20));
            return frame;
        }
        private static ReportPart P(int count, int scan, params Contact[] contacts)
        {
            ReportPart part = new ReportPart { Count = count, Scan = scan };
            part.Contacts.AddRange(contacts); return part;
        }
        private static HidField Field(int page, int usage, int index)
        {
            return new HidField { Page = (ushort)page, Usage = (ushort)usage, Index = (ushort)index, Report = 1,
                ValueCaps = new Native.ValueCaps { LogicalMax = 1000, PhysicalMax = 1000, Units = 0x11, UnitsExp = 0xE, BitSize = 16 } };
        }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
                if (args.Contains("--performance-only")) { PerformanceChecks.Benchmark(); return 0; }
                if (args.Contains("--task-manager-only"))
                {
                    int regressionAt = Array.IndexOf(args, "--regression-log");
                    if (regressionAt >= 0)
                    {
                        var previousOut = Console.Out; var previousError = Console.Error;
                        int result;
                        using (var log = new StreamWriter(args[regressionAt + 1]) { AutoFlush = true })
                        {
                            Console.SetOut(log); Console.SetError(log);
                            Console.WriteLine("Elevated native verification: " + ElevationSupport.IsElevated);
                            try { result = Main(new[] { "--motion-check", "--latency-check" }); }
                            finally { Console.SetOut(previousOut); Console.SetError(previousError); }
                        }
                        Check(result == 0, "Elevated native regression failed; see regression log.");
                    }
                    Test("Task Manager elevation and window movement", ElevationChecks.TaskManager);
                    int resultAt = Array.IndexOf(args, "--result-file");
                    if (resultAt >= 0) File.WriteAllText(args[resultAt + 1], "PASS: actual Task Manager window. Elevated=" + ElevationSupport.IsElevated);
                    int restartAt = Array.IndexOf(args, "--restart-glide");
                    if (restartAt >= 0)
                    {
                        Check(ElevationSupport.IsElevated, "Handoff verification requires Windows-approved elevation.");
                        int previousId = int.Parse(args[restartAt + 2]);
                        using var previous = System.Diagnostics.Process.GetProcessById(previousId);
                        using var replacement = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args[restartAt + 1], "--elevated-restart " + previousId) { UseShellExecute = false });
                        Check(previous.WaitForExit(10000) && !replacement.WaitForExit(2000), "Elevated Glide did not replace the previous instance.");
                        if (resultAt >= 0) File.AppendAllText(args[resultAt + 1], "\r\nPASS: elevated Glide handoff. ProcessId=" + replacement.Id);
                    }
                    return 0;
                }
                Test("Windows ABI sizes", delegate {
                    Check(Marshal.SizeOf(typeof(Native.ValueCaps)) == 72, "value caps ABI");
                    Check(Marshal.SizeOf(typeof(Native.ButtonCaps)) == 72, "button caps ABI");
                    Check(Marshal.SizeOf(typeof(Native.HidCaps)) == 64, "HID caps ABI");
                    Check(Marshal.SizeOf(typeof(Native.HidData)) == 8, "data ABI");
                    Check(Marshal.SizeOf(typeof(Native.RawHeader)) == (IntPtr.Size == 8 ? 24 : 16), "raw header ABI");
                });
                Test("parallel report ignores padded contact slots", delegate {
                    var a = new FrameAssembler();
                    var result = a.Push(P(3, 1, C(1,0,0), C(2,0,0), C(3,0,0), C(0,0,0,false)), 0);
                    Check(result != null && result.Count == 3 && result[2].Id == 3, "parallel frame");
                });
                Test("hybrid reports are emitted only when complete", delegate {
                    var a = new FrameAssembler();
                    Check(a.Push(P(3, 42, C(1,0,0)), 0) == null, "part one");
                    Check(a.Push(P(0, 42, C(2,0,0)), 1) == null, "part two");
                    var frame = a.Push(P(0, 42, C(3,0,0,false)), 2);
                    Check(frame.Count == 3 && !frame[2].Tip, "count includes released contacts");
                });
                Test("reused HID contact slots preserve all hybrid fragments and clear lifted frames", delegate {
                    var assembler = new FrameAssembler(); var slot = C(1, 10, 20);
                    var report = P(3, 42, slot);
                    Check(assembler.Push(report, 0) == null, "hybrid first fragment emitted");
                    report.Count = 0; slot.Id = 2; slot.X = 30;
                    Check(assembler.Push(report, 1) == null, "hybrid second fragment emitted");
                    slot.Id = 3; slot.X = 50; slot.Tip = false;
                    var contacts = assembler.Push(report, 2);
                    Check(contacts.Count == 3 && contacts[0].Id == 1 && contacts[0].X == 10 && contacts[1].Id == 2 && contacts[1].X == 30 && !contacts[2].Tip, "reused decoder slot overwrote hybrid contacts");
                    report.Scan++; report.Contacts.Clear();
                    Check(assembler.Push(report, 3).Count == 0, "empty frame retained a pooled contact");
                });
                Test("hybrid frame mismatch and timeout discard partial contacts", delegate {
                    var a = new FrameAssembler();
                    a.Push(P(3, 1, C(1,0,0)), 0);
                    Check(a.Push(P(0, 2, C(2,0,0)), 1).Count == 0, "scan mismatch");
                    a.Push(P(3, 3, C(1,0,0)), 2);
                    Check(a.Push(P(0, 3, C(2,0,0)), 103).Count == 0, "timeout");
                });
                Test("duplicate contact ID discards a corrupt frame", delegate {
                    var a = new FrameAssembler();
                    Check(a.Push(P(3, 1, C(1,0,0), C(1,0,0), C(3,0,0)), 0) == null, "duplicate accepted");
                });
                Test("descriptor data indices decode Tip, Confidence, ID and physical units", delegate {
                    var decoder = new ReportDecoder();
                    decoder.Counts.Add(Field(13, 0x54, 0)); decoder.Scans.Add(Field(13,0x56,1));
                    decoder.Slots.Add(new ContactSlot { Id = Field(13,0x51,2), X = Field(1,0x30,3), Y = Field(1,0x31,4), Tip = Field(13,0x42,5), Confidence = Field(13,0x47,6) });
                    var data = new[] { new Native.HidData { Index=0,Value=1 }, new Native.HidData { Index=1,Value=42 }, new Native.HidData { Index=2,Value=7 }, new Native.HidData { Index=3,Value=500 }, new Native.HidData { Index=4,Value=250 }, new Native.HidData { Index=5,Value=1 }, new Native.HidData { Index=6,Value=1 } };
                    var part = decoder.Decode(1,data,(uint)data.Length);
                    Check(part.Count == 1 && part.Scan == 42 && part.Contacts[0].Id == 7, "metadata");
                    Check(part.Contacts[0].X == 50 && part.Contacts[0].Y == 25 && part.Contacts[0].Tip && part.Contacts[0].Confident, "physical conversion");
                    var up = decoder.Decode(1,data,5);
                    Check(!up.Contacts[0].Tip && !up.Contacts[0].Confident, "absent HID buttons must be off");
                    Check(decoder.Decode(2,data,7) == null, "unrelated report");
                });
                Test("invalid coordinate is rejected", delegate {
                    bool rejected = false;
                    try { Field(1,0x30,0).Millimeters(1001,true); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected, "out of range coordinate accepted");
                });
                Test("one and two fingers cannot move a window", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m);
                    e.Process(F(0,1),0,false); e.Process(F(10,2),10,false);
                    Check(m.Prepares == 0 && m.Moves == 0, "ordinary touch intercepted");
                });
                Test("three fingers lock target on contact and activate after movement threshold", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m);
                    e.Process(F(0),0,false); e.Process(F(0.2),10,false);
                    Check(m.Prepares == 1 && m.Captures == 0, "threshold ignored");
                    e.Process(F(1),20,false);
                    Check(m.Captures == 1 && m.X == 12 && e.Dragging, "movement wrong");
                    e.Process(F(2),30,false);
                    Check(m.Prepares == 1 && m.Captures == 1 && m.X == 24, "target reselected");
                });
                Test("lifting one finger stops immediately", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m);
                    e.Process(F(0),0,false); e.Process(F(1),10,false);
                    int moves = m.Moves; e.Process(F(2,2),20,false); e.Process(F(5,2),30,false);
                    Check(!e.Dragging && m.Moves == moves, "did not release");
                });
                Test("four fingers and palm are excluded", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m);
                    e.Process(F(0,4),0,false); e.Process(F(1,4),10,false);
                    var f = F(2); f.Contacts.Add(C(4,1,1,true,false));
                    e.Process(f,20,false);
                    Check(m.Prepares == 0, "palm or four finger frame accepted");
                });
                Test("contact ID replacement cancels instead of jumping", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m);
                    e.Process(F(0),0,false); e.Process(F(1),10,false); int moves = m.Moves;
                    e.Process(F(2,3,10),20,false); e.Process(F(3,3,10),30,false);
                    Check(!e.Dragging && m.Moves == moves, "ID replacement moved window");
                });
                Test("Escape blocks gesture until fingers are lifted", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m);
                    e.Process(F(0),0,false); e.Process(F(1),10,false); int moves = m.Moves;
                    e.Process(F(2),20,true); e.Process(F(3),30,false);
                    Check(m.Moves == moves && !e.Dragging, "Escape did not latch");
                    e.Process(F(0,0),40,false); e.Process(F(0),50,false); e.Process(F(1),60,false);
                    Check(e.Dragging, "gesture never resumed");
                });
                Test("stale input timeout cancels", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m);
                    e.Process(F(0),0,false); e.Process(F(1),10,false); e.Tick(461);
                    Check(!e.Dragging, "timeout did not release");
                });
                Test("pause and failed window operations stop movement", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m) { Enabled = false };
                    e.Process(F(0),0,false); e.Process(F(5),10,false);
                    Check(m.Moves == 0, "disabled moved window");
                    e.Enabled=true; e.Process(F(0,0),20,false); e.Process(F(0),30,false);
                    m.Allow=false; e.Process(F(1),40,false);
                    Check(!e.Dragging, "capture failure ignored");
                });
                Test("input from another touchpad cannot hijack active gesture", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m);
                    e.Process(F(0),0,false); e.Process(F(1),10,false);
                    e.Process(F(10,3,0,2),20,false);
                    Check(m.X == 12 && m.Prepares == 1, "other device hijacked gesture");
                });
                Test("fractional motion accumulates without rounding drift", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m) { ThresholdMm=0 };
                    e.Process(F(0),0,false);
                    for (int i=1; i<=100; i++) e.Process(F(i*0.01),i,false);
                    Check(m.X == 12, "fractional motion lost");
                });
                Test("large coordinate jump cancels", delegate {
                    var m = new FakeMover(); var e = new GestureEngine(m);
                    e.Process(F(0),0,false); e.Process(F(50),10,false);
                    Check(m.Moves == 0 && !e.Dragging, "jump accepted");
                });
                Test("multi-monitor negative coordinates are retained and lost windows constrained", delegate {
                    Rectangle[] areas = { new Rectangle(-1920,0,1920,1040), new Rectangle(0,0,1920,1040) };
                    Point valid = WindowMover.KeepReachable(new Rectangle(-1500,200,800,600),areas);
                    Check(valid.X == -1500 && valid.Y == 200, "negative monitor clamped");
                    Point lost = WindowMover.KeepReachable(new Rectangle(9000,-1000,800,600),areas);
                    Check(lost.Y >= 0 && lost.X <= 1856, "window unreachable");
                });
                Test("real touchpad descriptor roundtrip through Windows HID parser", delegate {
                    using (var raw = new RawTouchpad())
                    {
                        if (raw.DeviceCount == 0) { Console.WriteLine("  No compatible hardware: native descriptor roundtrip skipped."); return; }
                        TouchpadDevice d = raw.Devices.First();
                        uint bytes = 0;
                        Native.GetRawInputDeviceInfo(d.Handle,Native.RidiPreparsed,IntPtr.Zero,ref bytes);
                        IntPtr preparsed = Marshal.AllocHGlobal((int)bytes);
                        try
                        {
                            Native.GetRawInputDeviceInfo(d.Handle,Native.RidiPreparsed,preparsed,ref bytes);
                            byte reportId = d.Decoder.Slots[0].Id.Report;
                            var slots = d.Decoder.Slots.Where(s => s.Id.Report == reportId).Take(3).ToArray();
                            if (slots.Length < 3) { Console.WriteLine("  Hybrid descriptor: covered by hybrid assembly tests."); return; }
                            var countField = d.Decoder.Counts.First(f => f.Report == reportId);
                            var scanField = d.Decoder.Scans.First(f => f.Report == reportId);
                            var m = new FakeMover(); var e = new GestureEngine(m);
                            for (int frameNumber=0; frameNumber<2; frameNumber++)
                            {
                                byte[] report = new byte[d.Caps.InputLength];
                                Check(HidP_InitializeReportForID(0,reportId,preparsed,report,(uint)report.Length) == Native.HidSuccess,"native report init");
                                var fields = new List<Native.HidData> { new Native.HidData { Index=countField.Index,Value=3 }, new Native.HidData { Index=scanField.Index,Value=(uint)(100+frameNumber*100) } };
                                for (int i=0; i<3; i++)
                                {
                                    ContactSlot s=slots[i];
                                    fields.Add(new Native.HidData { Index=s.Id.Index,Value=(uint)i });
                                    fields.Add(new Native.HidData { Index=s.X.Index,Value=(uint)(s.X.ValueCaps.LogicalMax/2 + frameNumber * s.X.ValueCaps.LogicalMax/50) });
                                    fields.Add(new Native.HidData { Index=s.Y.Index,Value=(uint)(s.Y.ValueCaps.LogicalMax/2) });
                                    fields.Add(new Native.HidData { Index=s.Tip.Index,Value=1 });
                                    fields.Add(new Native.HidData { Index=s.Confidence.Index,Value=1 });
                                }
                                var entries=fields.ToArray(); uint fieldCount=(uint)entries.Length;
                                Check(HidP_SetData(0,entries,ref fieldCount,preparsed,report,(uint)report.Length) == Native.HidSuccess,"native report set");
                                ContactFrame parsed=d.Parse(report,frameNumber*10);
                                Check(parsed != null && parsed.Contacts.Count(c=>c.Tip && c.Confident)==3,"actual descriptor decoding");
                                e.Process(parsed,frameNumber*10,false);
                            }
                            Check(e.Dragging && m.X > 0 && m.Y == 0,"native descriptor to movement pipeline");
                        }
                        finally { Marshal.FreeHGlobal(preparsed); }
                    }
                });
                Test("cursor follows the window using its initial anchor", delegate {
                    Point cursor = new Point(450,350), origin = new Point(100,100);
                    Point first = WindowMover.CursorDestination(cursor,origin,new Point(120,140));
                    Point second = WindowMover.CursorDestination(cursor,origin,new Point(130,150));
                    Check(first == new Point(470,390) && second == new Point(480,400), "cursor drift or wrong displacement");
                });
                Test("cursor uses constrained window position at screen edges", delegate {
                    Rectangle[] areas = { new Rectangle(0,0,1920,1040) };
                    Point clamped = WindowMover.KeepReachable(new Rectangle(300, -100, 800, 600),areas);
                    Point cursor = WindowMover.CursorDestination(new Point(450,350),new Point(100,100),clamped);
                    Check(cursor == new Point(650,250), "cursor followed unclamped requested motion");
                });
                Test("cursor tracks negative monitor coordinates", delegate {
                    Point cursor = WindowMover.CursorDestination(new Point(-1500,300),new Point(-1800,100),new Point(-1900,80));
                    Check(cursor == new Point(-1600,280), "negative cursor coordinate clamped");
                });
                Test("native input window and monitor layout without Windows Forms", delegate {
                    using (var raw = new RawTouchpad()) Check(raw.Handle != IntPtr.Zero,"no message window");
                    Check(MonitorLayout.WorkAreas().Length > 0,"no monitors");
                    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Windows.Forms"),"legacy UI framework loaded");
                });
                Test("latest target replaces historical positions and handles reversal", LatencyChecks.LatestTargetWins);
                Test("Start menu and shell surfaces are excluded while apps remain movable", FeatureChecks.ShellWindowsAreExcluded);
                Test("top-edge maximum respects option and monitor geometry", FeatureChecks.TopEdgeGeometry);
                Test("installed settings and startup command paths", FeatureChecks.InstalledSettingsPath);
                Test("only a normal finger release finishes a gesture", delegate {
                    var normal = new FakeMover(); var e = new GestureEngine(normal);
                    e.Process(F(0),0,false); e.Process(F(1),10,false); e.Process(F(2,2),20,false);
                    Check(normal.Finishes == 1, "normal release did not finish");
                    var cancelled = new FakeMover(); e = new GestureEngine(cancelled);
                    e.Process(F(0),0,false); e.Process(F(1),10,false); e.Process(F(2),20,true);
                    Check(cancelled.Finishes == 0, "Escape finished/maximized instead of cancelling");
                    var fourth = new FakeMover(); e = new GestureEngine(fourth);
                    e.Process(F(0),0,false); e.Process(F(1),10,false); e.Process(F(2,4),20,false);
                    Check(fourth.Finishes == 0, "four-finger change finished/maximized");
                });
                Test("three-finger spread and contraction resize the same window", GestureFeatureChecks.PinchExpandsAndContracts);
                Test("pinch resize can be disabled and respects fixed-size targets", GestureFeatureChecks.PinchCanBeDisabled);
                Test("drag and pinch modes resist jitter and cannot switch midway", GestureFeatureChecks.ModeLockAndJitter);
                Test("resize anchor, size bounds and top/foreground options", GestureFeatureChecks.GeometryAndBounds);
                Test("release drops pending targets and late cursor feedback", LatencyChecks.ReleaseDropsPendingAndCursorFeedback);
                Test("input continues while the UI caller is blocked", LatencyChecks.InputThreadSurvivesBusyCaller);
                Test("side edges and monitor seams honor independent options", LayoutChecks.EdgesAndDisplays);
                Test("half placement covers odd work areas and invisible borders", LayoutChecks.HalfGeometry);
                Test("flick directions are stable across input report rates", LayoutChecks.FlickDirectionsAndReportRates);
                Test("upward diagonal flicks maximize on release and horizontal placement stays independent", LayoutChecks.UpwardDiagonals);
                Test("flick sensitivity rejects slow, stale, diagonal and reversing motion", LayoutChecks.SensitivityAndRejections);
                Test("flick dispatch happens on lift and ignores cursor speed", LayoutChecks.GestureReleaseAndToggle);
                Test("resize, Escape, fourth finger and timeout never flick", LayoutChecks.ResizeAndCancelNeverFlick);
                Test("layout options persist and old settings retain defaults", LayoutChecks.SettingsRoundtrip);
                Test("unsnap keeps the cursor's relative anchor and negative coordinates", SnapRestoreChecks.AnchorGeometry);
                Test("drag restores snap size while pinch uses the displayed size", SnapRestoreChecks.GestureChoosesRestoreOnlyForDrag);
                Test("stationary hold arms directional resize and locks selected edges", InteractionChecks.HoldStartsResizeBeforeMoving);
                Test("early drag, disabled resize and fixed windows retain normal behavior", InteractionChecks.EarlyMovementLocksNormalDrag);
                Test("old modifier-only settings use the saved hold time and keep the resize behavior", InteractionChecks.LegacyModifierSettingsUseHold);
                Test("directional geometry respects opposite edges and size limits", InteractionChecks.GeometryAndLimits);
                Test("flick must end within the initial contact time window", InteractionChecks.FlickMustFinishSoonAfterContact);
                Test("first-run welcome, upgrade migration and resize settings persist", InteractionChecks.WelcomeAndSettingsPersist);
                Test("input settings copy resize options across thread boundaries", InteractionChecks.InputOptionsAreSnapshots);
                Test("reordered and reused contact objects retain movement; duplicates cancel", PerformanceChecks.ContactReuseAndReordering);
                Test("steady-state gesture processing stays within the allocation budget", PerformanceChecks.AllocationBudget);
                Test("steady-state HID decoding and assembly stay within the allocation budget", PerformanceChecks.ReportAllocationBudget);
                Test("elevation restart uses Windows consent and rejects invalid handoff targets", delegate {
                    var start = ElevationSupport.RestartInfo(Environment.ProcessPath, Environment.ProcessId);
                    Check(start.UseShellExecute && start.Verb == "runas" && start.Arguments == "--elevated-restart " + Environment.ProcessId, "restart bypasses the Windows consent flow");
                    Check(!ElevationSupport.CompleteHandoff(Environment.ProcessId, "unused", 0), "restart targeted itself");
                    Check(!ElevationSupport.CompleteHandoff(-1, "unused", 0), "negative process accepted");
                });
                Test("resize wait time stays in tenths and behavior persists", ResizeModeChecks.TenthsAndSavedSettings);
                Test("all four lower-corner directions match the requested mapping and reverse correctly", ResizeModeChecks.AllCornerDirectionsAndReversal);
                Test("staggered diagonal axes select the correct lower corner without jumps", ResizeModeChecks.StaggeredAxesChooseCorrectLowerCorner);
                Test("lower-corner mode leaves normal drag, pinch and snapshots independent", ResizeModeChecks.PinchDragAndSnapshotsStayIndependent);
                if (args.Contains("--motion-check")) Test("real window and cursor move by matching pixel distances", LatencyChecks.NativeMotion);
                if (args.Contains("--latency-check")) Test("native asynchronous backlog versus latest-target dispatch", LatencyChecks.NativeQueueBenchmark);
                if (args.Contains("--registry-check")) Test("startup registry enable/disable and ownership", FeatureChecks.StartupRegistryRoundtrip);
                if (args.Contains("--shell-check")) Test("real Windows shell windows reject movement", FeatureChecks.RealShellWindowCheck);
                if (args.Contains("--motion-check")) Test("native top-edge maximize, toggle-off and cancellation", FeatureChecks.NativeTopMaximize);
                if (args.Contains("--motion-check") || args.Contains("--new-motion-check")) Test("native foreground and top-overflow toggles", GestureFeatureChecks.NativeRaiseAndTopOverflow);
                if (args.Contains("--motion-check") || args.Contains("--new-motion-check")) Test("native pinch expansion, contraction and cursor anchor", GestureFeatureChecks.NativePinchResize);
                if (args.Contains("--motion-check") || args.Contains("--layout-check")) Test("native left/right edge placement, option-off and cancellation", LayoutChecks.NativeSideSnapAndCancel);
                if (args.Contains("--motion-check") || args.Contains("--layout-check")) Test("native flick commands work with edge options off", LayoutChecks.NativeFlickCommandsIndependent);
                if (args.Contains("--motion-check") || args.Contains("--restore-check")) Test("native unsnap restores size and cursor without changing on contact/cancel", SnapRestoreChecks.NativeRestoreAndCursor);
                if (args.Contains("--motion-check") || args.Contains("--restore-check")) Test("repeated left/right snaps and different windows retain their own original sizes", SnapRestoreChecks.NativeRepeatedSidesAndWindows);
                if (args.Contains("--motion-check") || args.Contains("--restore-check")) Test("snap memory handles maximize, pinch and external resize", SnapRestoreChecks.NativePinchExternalResizeAndMaximize);
                if (args.Contains("--motion-check") || args.Contains("--directional-check")) Test("native directional resize, reversal, cursor anchor and release", InteractionChecks.NativeDirectionalResize);
                if (args.Contains("--motion-check") || args.Contains("--directional-check")) Test("native grow and lower-corner modes in all four directions", ResizeModeChecks.NativeCornerModes);
                Console.WriteLine(passed + " tests passed.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
