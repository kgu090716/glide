using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ThreeFingerWindowDrag
{
    internal sealed class HidField
    {
        public ushort Page, Usage, Link, Index;
        public byte Report;
        public bool Button;
        public Native.ValueCaps ValueCaps;
        public double Millimeters(uint raw, bool horizontal)
        {
            Native.ValueCaps v = ValueCaps;
            long signed = raw;
            if (v.LogicalMin < 0 && v.BitSize > 0 && v.BitSize < 32)
            {
                uint sign = 1u << (v.BitSize - 1);
                if ((raw & sign) != 0) signed = (long)raw - (1L << v.BitSize);
            }
            else if (v.LogicalMin < 0 && v.BitSize == 32) signed = unchecked((int)raw);
            double range = (double)v.LogicalMax - v.LogicalMin;
            if (range <= 0 || signed < v.LogicalMin || signed > v.LogicalMax)
                throw new InvalidOperationException("터치패드가 범위를 벗어난 좌표를 보고했습니다.");
            double physical = (double)v.PhysicalMax - v.PhysicalMin;
            int exponent = (int)(v.UnitsExp & 15);
            if (exponent >= 8) exponent -= 16;
            int system = (int)(v.Units & 15);
            double size = physical * Math.Pow(10, exponent) * (system == 3 ? 25.4 : 10);
            if ((system != 1 && system != 3) || ((v.Units >> 4) & 15) != 1 || size < 10 || size > 1000)
                size = horizontal ? 120 : 75;
            return (signed - v.LogicalMin) / range * size;
        }
    }

    internal sealed class ContactSlot
    {
        public HidField Id, X, Y, Tip, Confidence;
    }

    // Descriptor-driven: no manufacturer-specific offsets or fixed report byte layout.
    internal sealed class ReportDecoder
    {
        internal readonly List<ContactSlot> Slots = new List<ContactSlot>();
        internal readonly List<HidField> Counts = new List<HidField>();
        internal readonly List<HidField> Scans = new List<HidField>();
        private readonly Dictionary<ushort, uint> values = new Dictionary<ushort, uint>();
        private readonly ReportPart part = new ReportPart();
        private readonly List<Contact> contacts = new List<Contact>(10);
        // Temporary decoder view; FrameAssembler copies each slot before the next report.
        internal ReportPart Decode(byte report, Native.HidData[] data, uint count)
        {
            HidField countField = null, scanField = null;
            foreach (HidField field in Counts) if (field.Report == report) { countField = field; break; }
            foreach (HidField field in Scans) if (field.Report == report) { scanField = field; break; }
            if (countField == null || scanField == null) return null;
            values.Clear();
            for (int i = 0; i < count; i++) values[data[i].Index] = data[i].Value;
            uint contactCount, scan;
            if (!values.TryGetValue(countField.Index, out contactCount) || !values.TryGetValue(scanField.Index, out scan)) return null;
            part.Count = (int)contactCount; part.Scan = (int)scan; part.Contacts.Clear();
            foreach (ContactSlot slot in Slots)
            {
                if (slot.Id.Report != report) continue;
                uint id, x, y, tip, confidence;
                if (!values.TryGetValue(slot.Id.Index, out id) || !values.TryGetValue(slot.X.Index, out x) || !values.TryGetValue(slot.Y.Index, out y)) continue;
                values.TryGetValue(slot.Tip.Index, out tip);
                values.TryGetValue(slot.Confidence.Index, out confidence);
                int index = part.Contacts.Count;
                if (index == contacts.Count) contacts.Add(new Contact());
                Contact contact = contacts[index];
                contact.Id = (int)id; contact.X = slot.X.Millimeters(x, true); contact.Y = slot.Y.Millimeters(y, false);
                contact.Tip = tip != 0; contact.Confident = confidence != 0;
                part.Contacts.Add(contact);
            }
            return part;
        }
    }

    internal sealed class TouchpadDevice : IDisposable
    {
        internal readonly IntPtr Handle;
        internal readonly string Name;
        internal Native.HidCaps Caps;
        internal readonly ReportDecoder Decoder = new ReportDecoder();
        private readonly FrameAssembler assembler = new FrameAssembler();
        private readonly ContactFrame frame = new ContactFrame();
        private IntPtr preparsed;
        private Native.HidData[] data;
        private byte[] padded;
        internal long Reports, Frames;
        internal string LastError;

        internal TouchpadDevice(IntPtr handle)
        {
            Handle = handle;
            uint size = 0;
            if (Native.GetRawInputDeviceInfo(handle, Native.RidiPreparsed, IntPtr.Zero, ref size) == uint.MaxValue || size == 0 || size > 1024 * 1024)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            preparsed = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.GetRawInputDeviceInfo(handle, Native.RidiPreparsed, preparsed, ref size) == uint.MaxValue) throw new Win32Exception();
                Check(Native.HidP_GetCaps(preparsed, out Caps));
                if (Caps.Page != 0x0D || Caps.Usage != 0x05) throw new InvalidOperationException("Not a precision touchpad.");
                uint nameSize = 0;
                Native.GetRawInputDeviceName(handle, Native.RidiName, null, ref nameSize);
                StringBuilder name = new StringBuilder((int)nameSize + 1);
                Native.GetRawInputDeviceName(handle, Native.RidiName, name, ref nameSize);
                Name = name.ToString();
                ReadDescriptor();
                uint max = Native.HidP_MaxDataListLength(0, preparsed);
                if (max == 0 || max > 65536 || Caps.InputLength == 0) throw new InvalidOperationException("Invalid HID report sizes.");
                data = new Native.HidData[max];
                padded = new byte[Caps.InputLength];
            }
            catch { Dispose(); throw; }
        }
        private static void Check(int result)
        {
            if (result != Native.HidSuccess) throw new InvalidOperationException("HID parser status 0x" + result.ToString("X8"));
        }
        private void ReadDescriptor()
        {
            List<HidField> fields = new List<HidField>();
            Native.ValueCaps[] values = new Native.ValueCaps[Caps.ValueCount];
            Native.ButtonCaps[] buttons = new Native.ButtonCaps[Caps.ButtonCount];
            ushort valueCount = Caps.ValueCount, buttonCount = Caps.ButtonCount;
            if (valueCount > 0) Check(Native.HidP_GetValueCaps(0, values, ref valueCount, preparsed));
            if (buttonCount > 0) Check(Native.HidP_GetButtonCaps(0, buttons, ref buttonCount, preparsed));
            foreach (Native.ValueCaps v in values.Take(valueCount))
            {
                // Usage value arrays cannot identify individual contact collections reliably.
                if (v.IsRange == 0 && v.ReportCount > 1) continue;
                int end = v.IsRange != 0 ? v.UsageMax : v.UsageMin;
                for (int u = v.UsageMin; u <= end; u++)
                    fields.Add(new HidField { Page = v.Page, Usage = (ushort)u, Link = v.Link, Report = v.Report,
                        Index = (ushort)(v.DataMin + u - v.UsageMin), ValueCaps = v });
            }
            foreach (Native.ButtonCaps b in buttons.Take(buttonCount))
            {
                int end = b.IsRange != 0 ? b.UsageMax : b.UsageMin;
                for (int u = b.UsageMin; u <= end; u++)
                    fields.Add(new HidField { Page = b.Page, Usage = (ushort)u, Link = b.Link, Report = b.Report,
                        Index = (ushort)(b.DataMin + u - b.UsageMin), Button = true });
            }
            Native.LinkNode[] nodes = new Native.LinkNode[Caps.LinkCount];
            uint nodeCount = Caps.LinkCount;
            if (nodeCount > 0) Check(Native.HidP_GetLinkCollectionNodes(nodes, ref nodeCount, preparsed));
            foreach (HidField f in fields)
            {
                if (f.Page == 0x0D && f.Usage == 0x54) Decoder.Counts.Add(f);
                if (f.Page == 0x0D && f.Usage == 0x56) Decoder.Scans.Add(f);
                ushort link = f.Link;
                for (int depth = 0; link > 0 && link < nodes.Length && depth < nodes.Length; depth++)
                {
                    if (nodes[link].Page == 0x0D && nodes[link].Usage == 0x22) { f.Link = link; break; }
                    link = nodes[link].Parent;
                }
            }
            foreach (var group in fields.GroupBy(f => new { f.Report, f.Link }))
            {
                ContactSlot slot = new ContactSlot {
                    Id = group.FirstOrDefault(f => f.Page == 13 && f.Usage == 0x51),
                    X = group.FirstOrDefault(f => f.Page == 1 && f.Usage == 0x30),
                    Y = group.FirstOrDefault(f => f.Page == 1 && f.Usage == 0x31),
                    Tip = group.FirstOrDefault(f => f.Page == 13 && f.Usage == 0x42),
                    Confidence = group.FirstOrDefault(f => f.Page == 13 && f.Usage == 0x47)
                };
                if (slot.Id != null && slot.X != null && slot.Y != null && slot.Tip != null && slot.Confidence != null)
                    Decoder.Slots.Add(slot);
            }
            if (Decoder.Slots.Count == 0 || Decoder.Counts.Count == 0 || Decoder.Scans.Count == 0)
                throw new InvalidOperationException("이 터치패드의 접촉 보고서 형식은 지원되지 않습니다.");
        }
        internal ContactFrame Parse(byte[] report, long now)
        {
            Reports++;
            if (report.Length > padded.Length) { LastError = "HID report too long."; assembler.Reset(); return null; }
            Array.Clear(padded, 0, padded.Length);
            Array.Copy(report, padded, report.Length);
            uint count = (uint)data.Length;
            int status = Native.HidP_GetData(0, data, ref count, preparsed, padded, (uint)padded.Length);
            if (status != Native.HidSuccess) { LastError = "HID status 0x" + status.ToString("X8"); assembler.Reset(); return null; }
            ReportPart part = Decoder.Decode(padded[0], data, count);
            if (part == null) return null;
            List<Contact> contacts = assembler.Push(part, now);
            if (contacts == null) return null;
            Frames++;
            frame.Device = Handle; frame.Contacts.Clear();
            frame.Contacts.AddRange(contacts);
            LastError = null;
            return frame;
        }
        public void Dispose()
        {
            if (preparsed != IntPtr.Zero) { Marshal.FreeHGlobal(preparsed); preparsed = IntPtr.Zero; }
        }
    }

    internal sealed class RawTouchpad : MessageWindow, IDisposable
    {
        private readonly Dictionary<IntPtr, TouchpadDevice> devices = new Dictionary<IntPtr, TouchpadDevice>();
        private readonly HashSet<IntPtr> unsupported = new HashSet<IntPtr>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        internal event Action<ContactFrame, long> Frame;
        internal event Action DevicesChanged;
        internal event Action Toggle;
        internal event Action InputFault;
        internal string LastError;
        internal bool HotkeyRegistered;
        internal int ContactCount;
        internal long LastFrameTime = -1;
        private IntPtr inputBuffer;
        private uint inputCapacity;
        private byte[] reportBuffer;
        internal int DeviceCount { get { return devices.Count; } }
        internal long Now { get { return clock.ElapsedMilliseconds; } }
        internal IEnumerable<TouchpadDevice> Devices { get { return devices.Values; } }
        internal RawTouchpad()
        {
            CreateHandle("ThreeFingerWindowDrag.Input");
            Native.RawDevice[] registration = { new Native.RawDevice { Page = 13, Usage = 5, Flags = Native.InputSink | Native.DeviceNotify, Target = Handle } };
            if (!Native.RegisterRawInputDevices(registration, 1, (uint)Marshal.SizeOf(typeof(Native.RawDevice))))
            {
                DestroyHandle();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "터치패드 입력 등록에 실패했습니다.");
            }
            HotkeyRegistered = Native.RegisterHotKey(Handle, 1, 0x4000 | 0x0002 | 0x0001, 0x78); // Ctrl+Alt+F9
            RefreshDevices();
        }
        internal void RefreshDevices()
        {
            foreach (TouchpadDevice device in devices.Values) device.Dispose();
            devices.Clear(); unsupported.Clear(); LastError = null;
            uint count = 0, size = (uint)Marshal.SizeOf(typeof(Native.DeviceList));
            if (Native.GetRawInputDeviceList(null, ref count, size) == uint.MaxValue) { LastError = "장치 목록을 읽지 못했습니다."; return; }
            Native.DeviceList[] list = new Native.DeviceList[count];
            uint result = Native.GetRawInputDeviceList(list, ref count, size);
            if (result == uint.MaxValue) { LastError = "장치 목록이 바뀌었습니다. 다시 검색해 주세요."; return; }
            foreach (Native.DeviceList entry in list.Take((int)result))
                if (entry.Type == 2) TryAdd(entry.Handle, false);
            ContactCount = 0; LastFrameTime = -1;
        }
        private TouchpadDevice TryAdd(IntPtr handle, bool reportErrors)
        {
            TouchpadDevice existing;
            if (devices.TryGetValue(handle, out existing)) return existing;
            if (unsupported.Contains(handle)) return null;
            try
            {
                TouchpadDevice device = new TouchpadDevice(handle);
                devices.Add(handle, device); return device;
            }
            catch (Exception ex)
            {
                unsupported.Add(handle);
                if (reportErrors || ex.Message != "Not a precision touchpad.") LastError = ex.Message;
                return null;
            }
        }
        protected override void WndProc(ref NativeMessage message)
        {
            try
            {
                if (message.Msg == Native.WmInput) ReadInput(message.LParam);
                else if (message.Msg == Native.WmHotkey && message.WParam.ToInt32() == 1)
                { if (Toggle != null) Toggle(); }
                else if (message.Msg == Native.WmDeviceChange)
                {
                    TouchpadDevice device;
                    if (message.WParam.ToInt32() == 2)
                    {
                        if (devices.TryGetValue(message.LParam, out device)) { device.Dispose(); devices.Remove(message.LParam); }
                        unsupported.Remove(message.LParam);
                    }
                    else TryAdd(message.LParam, false);
                    ContactCount = 0; LastFrameTime = -1;
                    if (DevicesChanged != null) DevicesChanged();
                }
            }
            catch (Exception ex) { LastError = ex.Message; if (InputFault != null) InputFault(); }
            // DefWindowProc is required to clean up foreground WM_INPUT messages.
            base.WndProc(ref message);
        }
        private void ReadInput(IntPtr input)
        {
            uint size = 0, headerSize = (uint)Marshal.SizeOf(typeof(Native.RawHeader));
            if (Native.GetRawInputData(input, Native.RidInput, IntPtr.Zero, ref size, headerSize) == uint.MaxValue || size < headerSize + 8 || size > 1024 * 1024) return;
            if (size > inputCapacity)
            {
                uint newCapacity = Math.Max(size, 4096);
                IntPtr newBuffer = Marshal.AllocHGlobal((int)newCapacity);
                if (inputBuffer != IntPtr.Zero) Marshal.FreeHGlobal(inputBuffer);
                inputBuffer = newBuffer; inputCapacity = newCapacity;
            }
            IntPtr buffer = inputBuffer;
            uint actual = Native.GetRawInputData(input, Native.RidInput, buffer, ref size, headerSize);
            if (actual == uint.MaxValue || actual < headerSize + 8) return;
            Native.RawHeader header = Marshal.PtrToStructure<Native.RawHeader>(buffer);
            if (header.Type != 2) return;
            TouchpadDevice device = TryAdd(header.Device, true);
            if (device == null) return;
            int reportSize = Marshal.ReadInt32(buffer, (int)headerSize);
            int reportCount = Marshal.ReadInt32(buffer, (int)headerSize + 4);
            if (reportSize <= 0 || reportCount < 0 || (long)headerSize + 8 + (long)reportSize * reportCount > actual) return;
            if (reportBuffer == null || reportBuffer.Length != reportSize) reportBuffer = new byte[reportSize];
            byte[] report = reportBuffer;
            for (int i = 0; i < reportCount; i++)
            {
                Marshal.Copy(IntPtr.Add(buffer, (int)headerSize + 8 + i * reportSize), report, 0, reportSize);
                ContactFrame frame = device.Parse(report, Now);
                if (frame == null) continue;
                LastFrameTime = Now;
                ContactCount = 0;
                foreach (Contact contact in frame.Contacts) if (contact.Tip && contact.Confident) ContactCount++;
                if (Frame != null) Frame(frame, Now);
            }
        }
        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            Native.UnregisterHotKey(Handle, 1);
            Native.RegisterRawInputDevices(new[] { new Native.RawDevice { Page = 13, Usage = 5, Flags = 1, Target = IntPtr.Zero } }, 1, (uint)Marshal.SizeOf(typeof(Native.RawDevice)));
            foreach (TouchpadDevice device in devices.Values) device.Dispose();
            devices.Clear(); DestroyHandle();
            if (inputBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(inputBuffer); inputBuffer = IntPtr.Zero; }
        }
    }
}
