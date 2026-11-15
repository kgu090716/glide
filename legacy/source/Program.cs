using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ThreeFingerWindowDrag
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && args[0] == "--diagnose") return Diagnostics.Run(args);
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\ThreeFingerWindowDrag.1", out created))
            {
                if (!created) { MessageBox.Show("이미 실행 중입니다. 작업 표시줄의 숨겨진 아이콘에서 ‘세 손가락 창 이동’을 열어 주세요.", "세 손가락 창 이동"); return 0; }
                try
                {
                    using (MainForm form = new MainForm(Settings.Load()))
                    {
                        if (args.Contains("--tray")) form.Shown += delegate { form.Hide(); };
                        Application.Run(form);
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("프로그램을 시작하지 못했습니다.\n" + ex.Message, "세 손가락 창 이동", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }
    }
    internal static class Diagnostics
    {
        internal static string Describe(RawTouchpad input)
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine("ThreeFingerWindowDrag 1.0.0");
            output.AppendLine("OS: " + Environment.OSVersion.Version);
            output.AppendLine("Process bits: " + (IntPtr.Size * 8));
            output.AppendLine("Precision touchpads: " + input.DeviceCount);
            output.AppendLine("Ctrl+Alt+F9 registered: " + input.HotkeyRegistered);
            output.AppendLine("Last input error: " + (input.LastError ?? "none"));
            foreach (TouchpadDevice device in input.Devices)
            {
                output.AppendLine();
                output.AppendLine("Device: " + device.Name);
                output.AppendLine("HID input length: " + device.Caps.InputLength);
                output.AppendLine("Contact slots per report: " + device.Decoder.Slots.Count);
                output.AppendLine("Raw reports: " + device.Reports + "; assembled frames: " + device.Frames);
                output.AppendLine("Parser error: " + (device.LastError ?? "none"));
                foreach (ContactSlot slot in device.Decoder.Slots)
                    output.AppendLine("  Report " + slot.Id.Report + ", collection " + slot.Id.Link + ": X " + slot.X.ValueCaps.LogicalMax + ", Y " + slot.Y.ValueCaps.LogicalMax);
            }
            output.AppendLine();
            output.AppendLine("Requirements: Windows Precision Touchpad exposing digitizer page 0x0D / usage 0x05 via Raw Input.");
            output.AppendLine("Windows three-finger Swipes and Taps must both be set to Nothing.");
            return output.ToString();
        }
        internal static int Run(string[] args)
        {
            try
            {
                using (RawTouchpad input = new RawTouchpad())
                {
                    int seconds = 0;
                    if (args.Length >= 3) int.TryParse(args[2], out seconds);
                    seconds = Math.Max(0, Math.Min(120, seconds));
                    StringBuilder transitions = new StringBuilder();
                    int last = -1, maximum = 0;
                    input.Frame += delegate(ContactFrame frame, long now) {
                        int count = frame.Contacts.Count(c => c.Tip && c.Confident);
                        maximum = Math.Max(maximum, count);
                        if (last != count) { transitions.AppendLine(now + " ms: " + count + " contacts"); last = count; }
                    };
                    Stopwatch elapsed = Stopwatch.StartNew();
                    while (elapsed.Elapsed.TotalSeconds < seconds) { Application.DoEvents(); Thread.Sleep(8); }
                    File.WriteAllText(Path.GetFullPath(args[1]), Describe(input) + "\r\nMax simultaneous contacts: " + maximum + "\r\n" + transitions, new UTF8Encoding(true));
                    return input.DeviceCount > 0 ? 0 : 2;
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.GetFullPath(args[1]), ex.ToString(), new UTF8Encoding(true));
                return 1;
            }
        }
    }
}
