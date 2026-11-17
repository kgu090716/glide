using System;
using System.Diagnostics;

namespace ThreeFingerWindowDrag
{
    internal static class PerformanceChecks
    {
        internal static void Benchmark()
        { Measure(true); MeasureReports(true); }
        internal static void ReportAllocationBudget()
        { if (MeasureReports(false) > 40000) throw new Exception("HID decoding and assembly allocated more than 0.4 bytes per report."); }
        private static long MeasureReports(bool print)
        {
            var decoder = new ReportDecoder(); var assembler = new FrameAssembler();
            HidField Field(int index) => new HidField { Index = (ushort)index, Report = 1,
                ValueCaps = new Native.ValueCaps { LogicalMax = 1000, PhysicalMax = 1000, Units = 0x11, UnitsExp = 0xE, BitSize = 16 } };
            decoder.Counts.Add(Field(0)); decoder.Scans.Add(Field(1));
            var data = new Native.HidData[17];
            for (int i = 0; i < data.Length; i++) data[i].Index = (ushort)i;
            data[0].Value = 3;
            for (int i = 0; i < 3; i++)
            {
                int n = 2 + i * 5;
                decoder.Slots.Add(new ContactSlot { Id = Field(n), X = Field(n + 1), Y = Field(n + 2), Tip = Field(n + 3), Confidence = Field(n + 4) });
                data[n].Value = (uint)i; data[n + 1].Value = (uint)(300 + i * 100); data[n + 2].Value = 300;
                data[n + 3].Value = data[n + 4].Value = 1;
            }
            for (int i = 0; i < 10000; i++) assembler.Push(decoder.Decode(1, data, (uint)data.Length), i);
            var watch = new Stopwatch(); long before = GC.GetAllocatedBytesForCurrentThread(); watch.Start();
            const int count = 100000;
            for (int i = 0; i < count; i++)
            {
                data[1].Value = (uint)i;
                var contacts = assembler.Push(decoder.Decode(1, data, (uint)data.Length), 10000L + i);
                if (contacts.Count != 3 || contacts[2].Id != 2 || contacts[2].X != 50) throw new Exception("Report benchmark lost contact data.");
            }
            watch.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (print) Console.WriteLine("HID decode + frame assembly (native I/O excluded): reports=" + count + "; allocated bytes=" + allocated + "; elapsed ms=" + watch.Elapsed.TotalMilliseconds.ToString("F2"));
            return allocated;
        }
        internal static void AllocationBudget()
        {
            if (Measure(false) > 40000) throw new Exception("Gesture processing allocated more than 0.4 bytes per steady-state frame.");
        }
        private static long Measure(bool print)
        {
            var frames = new ContactFrame[4];
            for (int f = 0; f < frames.Length; f++)
            {
                frames[f] = new ContactFrame { Device = new IntPtr(1) };
                for (int i = 0; i < 3; i++) frames[f].Contacts.Add(new Contact { Id = i, X = 30 + i * 10 + f * .3, Y = 30, Tip = true, Confident = true });
            }
            var mover = new FakeMover();
            var engine = new GestureEngine(mover) { DirectionalResize = new DirectionalResizeOptions { Enabled = false } };
            for (int i = 0; i < 10000; i++) engine.Process(frames[i % frames.Length], i * 8L, false);
            long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            const int count = 100000;
            for (int i = 0; i < count; i++) engine.Process(frames[i % frames.Length], (i + 10000) * 8L, false);
            watch.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (mover.Captures != 1 || mover.Moves < count / 2 || mover.Resizes != 0 || mover.EdgeResizes != 0) throw new Exception("Performance fixture changed gesture behavior.");
            if (print)
            {
                Console.WriteLine("Preallocated three-contact frames; native window I/O excluded.");
                Console.WriteLine("Frames: " + count + "; allocated bytes: " + allocated + "; bytes/frame: " + (allocated / (double)count).ToString("F2"));
                Console.WriteLine("Elapsed ms: " + watch.Elapsed.TotalMilliseconds.ToString("F2") + "; microseconds/frame: " + (watch.Elapsed.TotalMilliseconds * 1000 / count).ToString("F3"));
            }
            return allocated;
        }
        internal static void ContactReuseAndReordering()
        {
            var mover = new FakeMover(); var engine = new GestureEngine(mover);
            var frame = new ContactFrame { Device = new IntPtr(1) };
            for (int i = 0; i < 3; i++) frame.Contacts.Add(new Contact { Id = i, X = 20 + i * 10, Y = 20, Tip = true, Confident = true });
            engine.Process(frame, 0, false);
            foreach (Contact contact in frame.Contacts) contact.X += .8;
            int confirmed = GestureEngine.ThreeFingerConfirmationMs;
            frame.Contacts.Reverse(); engine.Process(frame, confirmed, false);
            if (mover.Moves != 1 || mover.X != 10) throw new Exception("Contact reordering or object reuse lost movement.");
            foreach (Contact contact in frame.Contacts) contact.X += .4;
            engine.Process(frame, confirmed + 10, false);
            if (mover.Captures != 1 || mover.X != 14) throw new Exception("Reused contact objects changed the previous sample.");
            frame.Contacts[1].Id = frame.Contacts[0].Id; engine.Process(frame, confirmed + 20, false);
            if (engine.Tracking || engine.Dragging || mover.Moves != 2) throw new Exception("Duplicate contacts did not cancel safely.");
        }
    }
}
