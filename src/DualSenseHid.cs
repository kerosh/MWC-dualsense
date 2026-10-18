using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DualSensePad
{
    /// <summary>Button bit flags of the DualSense.</summary>
    public static class Btn
    {
        public const uint Square = 1u << 0;
        public const uint Cross = 1u << 1;
        public const uint Circle = 1u << 2;
        public const uint Triangle = 1u << 3;
        public const uint L1 = 1u << 4;
        public const uint R1 = 1u << 5;
        public const uint L2 = 1u << 6;
        public const uint R2 = 1u << 7;
        public const uint Create = 1u << 8;
        public const uint Options = 1u << 9;
        public const uint L3 = 1u << 10;
        public const uint R3 = 1u << 11;
        public const uint PS = 1u << 12;
        public const uint Touchpad = 1u << 13;
        public const uint Mute = 1u << 14;
        public const uint DUp = 1u << 15;
        public const uint DRight = 1u << 16;
        public const uint DDown = 1u << 17;
        public const uint DLeft = 1u << 18;
    }

    /// <summary>Snapshot of the controller state (copied out of the reader thread).</summary>
    public struct PadState
    {
        public bool Connected;
        public bool Bluetooth;
        public float LX, LY, RX, RY;   // -1..1, Y positive = stick pushed up
        public float L2, R2;           // 0..1
        public uint Buttons;
        public bool Has(uint b) { return (Buttons & b) != 0; }
    }

    /// <summary>
    /// Raw HID access to a Sony DualSense (USB or Bluetooth): input reports on a reader thread,
    /// output reports (rumble, adaptive triggers, lightbar) on a writer thread.
    /// </summary>
    public class DualSenseHid
    {
        // ---------------- native ----------------
        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        const int DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
        static readonly IntPtr INVALID = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, [Out] byte[] caps);
        [DllImport("hid.dll")] static extern bool HidD_GetFeature(IntPtr h, [In, Out] byte[] buf, int len);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr h, [Out] byte[] buf, int len, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(IntPtr h, byte[] buf, int len, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool CancelIoEx(IntPtr h, IntPtr overlapped);

        // ---------------- state ----------------
        IntPtr readH = INVALID, writeH = INVALID;
        int inLen, outLen;
        volatile bool bt;
        volatile bool running;
        volatile bool connected;
        Thread reader, writer;
        readonly object stateLock = new object();
        readonly object ioLock = new object();
        PadState state;
        readonly AutoResetEvent outEvent = new AutoResetEvent(false);
        byte[] pending = new byte[47];   // "common" part of the output report
        bool pendingDirty = true;
        bool releaseLightbar = true;
        byte btSeq;
        public string DeviceInfo = "";
        public volatile string LastError = "";

        public bool Connected { get { return connected; } }
        public bool IsBluetooth { get { return bt; } }

        public void Start()
        {
            if (running) return;
            running = true;
            reader = new Thread(ReadLoop);
            reader.IsBackground = true;
            reader.Name = "DualSense reader";
            reader.Start();
            writer = new Thread(WriteLoop);
            writer.IsBackground = true;
            writer.Name = "DualSense writer";
            writer.Start();
        }

        public PadState GetState()
        {
            lock (stateLock) { return state; }
        }

        /// <summary>Queue new output values (47-byte common block, see BuildCommon).</summary>
        public void SetOutput(byte[] common)
        {
            lock (ioLock)
            {
                Buffer.BlockCopy(common, 0, pending, 0, 47);
                pendingDirty = true;
            }
            outEvent.Set();
        }

        /// <summary>Stop threads; sends a neutral report first (no rumble, triggers released).</summary>
        public void Shutdown()
        {
            if (!running) return;
            try
            {
                byte[] neutral = OutputBuilder.Neutral();
                lock (ioLock)
                {
                    Buffer.BlockCopy(neutral, 0, pending, 0, 47);
                    if (writeH != INVALID) WriteReport(pending);
                }
            }
            catch { }
            running = false;
            outEvent.Set();
            try { if (readH != INVALID) CancelIoEx(readH, IntPtr.Zero); } catch { }
            try { if (reader != null) reader.Join(300); } catch { }
            try { if (writer != null) writer.Join(300); } catch { }
            CloseHandles();
        }

        // ---------------- reader ----------------
        void ReadLoop()
        {
            byte[] buf = new byte[128];
            while (running)
            {
                try
                {
                    if (readH == INVALID)
                    {
                        if (!TryOpen()) { Thread.Sleep(1500); continue; }
                        buf = new byte[Math.Max(inLen, 64)];
                    }
                    int n;
                    bool ok = ReadFile(readH, buf, inLen, out n, IntPtr.Zero);
                    if (!running) break;
                    if (!ok || n <= 0)
                    {
                        LastError = "read failed (" + Marshal.GetLastWin32Error() + ")";
                        CloseHandles();
                        Thread.Sleep(500);
                        continue;
                    }
                    Parse(buf, n);
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    CloseHandles();
                    Thread.Sleep(1000);
                }
            }
        }

        void Parse(byte[] b, int n)
        {
            int o;
            bool simple = false;
            if (b[0] == 0x01 && !bt) o = 1;              // USB full report
            else if (b[0] == 0x31 && n >= 12) o = 2;     // BT full report
            else if (b[0] == 0x01 && bt) { simple = true; o = 1; } // BT "simple" report (before feature 0x05)
            else return;

            PadState s = new PadState();
            s.Connected = true;
            s.Bluetooth = bt;
            s.LX = Axis(b[o + 0]);
            s.LY = -Axis(b[o + 1]);
            s.RX = Axis(b[o + 2]);
            s.RY = -Axis(b[o + 3]);
            byte b0, b1, b2;
            if (simple)
            {
                b0 = b[o + 4]; b1 = b[o + 5]; b2 = b[o + 6];
                s.L2 = b[o + 7] / 255f; s.R2 = b[o + 8] / 255f;
            }
            else
            {
                s.L2 = b[o + 4] / 255f; s.R2 = b[o + 5] / 255f;
                b0 = b[o + 7]; b1 = b[o + 8]; b2 = b[o + 9];
            }
            uint m = 0;
            int hat = b0 & 0x0F;
            if (hat == 0 || hat == 1 || hat == 7) m |= Btn.DUp;
            if (hat == 1 || hat == 2 || hat == 3) m |= Btn.DRight;
            if (hat == 3 || hat == 4 || hat == 5) m |= Btn.DDown;
            if (hat == 5 || hat == 6 || hat == 7) m |= Btn.DLeft;
            if ((b0 & 0x10) != 0) m |= Btn.Square;
            if ((b0 & 0x20) != 0) m |= Btn.Cross;
            if ((b0 & 0x40) != 0) m |= Btn.Circle;
            if ((b0 & 0x80) != 0) m |= Btn.Triangle;
            if ((b1 & 0x01) != 0) m |= Btn.L1;
            if ((b1 & 0x02) != 0) m |= Btn.R1;
            if ((b1 & 0x04) != 0) m |= Btn.L2;
            if ((b1 & 0x08) != 0) m |= Btn.R2;
            if ((b1 & 0x10) != 0) m |= Btn.Create;
            if ((b1 & 0x20) != 0) m |= Btn.Options;
            if ((b1 & 0x40) != 0) m |= Btn.L3;
            if ((b1 & 0x80) != 0) m |= Btn.R3;
            if ((b2 & 0x01) != 0) m |= Btn.PS;
            if ((b2 & 0x02) != 0) m |= Btn.Touchpad;
            if ((b2 & 0x04) != 0) m |= Btn.Mute;
            s.Buttons = m;
            lock (stateLock) { state = s; }
        }

        static float Axis(byte v)
        {
            float f = (v - 127.5f) / 127.5f;
            if (f > 1f) f = 1f;
            if (f < -1f) f = -1f;
            return f;
        }

        // ---------------- writer ----------------
        void WriteLoop()
        {
            DateTime lastSend = DateTime.MinValue;
            while (running)
            {
                outEvent.WaitOne(250, false);
                if (!running) break;
                try
                {
                    lock (ioLock)
                    {
                        if (writeH == INVALID) continue;
                        // re-send at least once a second so a reconnect/other app can't leave stale state
                        if (pendingDirty || (DateTime.Now - lastSend).TotalMilliseconds > 1000)
                        {
                            pendingDirty = false;
                            WriteReport(pending);
                            lastSend = DateTime.Now;
                        }
                    }
                }
                catch (Exception e) { LastError = e.Message; }
            }
        }

        // caller holds ioLock
        void WriteReport(byte[] common)
        {
            byte[] c = (byte[])common.Clone();
            if (releaseLightbar && (c[1] & 0x04) != 0)
            {
                // first time we take over the lightbar: release the default blue "fade in" animation
                c[38] |= 0x02;
                c[41] = 0x02;
                releaseLightbar = false;
            }
            byte[] r;
            if (!bt)
            {
                r = new byte[Math.Max(outLen, 48)];
                r[0] = 0x02;
                Buffer.BlockCopy(c, 0, r, 1, 47);
            }
            else
            {
                r = new byte[Math.Max(outLen, 78)];
                r[0] = 0x31;
                r[1] = (byte)(btSeq << 4);
                btSeq = (byte)((btSeq + 1) & 0x0F);
                r[2] = 0x10;
                Buffer.BlockCopy(c, 0, r, 3, 47);
                uint crc = Crc32.Compute(0xA2, r, 74);
                r[74] = (byte)crc;
                r[75] = (byte)(crc >> 8);
                r[76] = (byte)(crc >> 16);
                r[77] = (byte)(crc >> 24);
            }
            int written;
            if (!WriteFile(writeH, r, outLen > 0 ? outLen : r.Length, out written, IntPtr.Zero))
                LastError = "write failed (" + Marshal.GetLastWin32Error() + ")";
        }

        // ---------------- device discovery ----------------
        bool TryOpen()
        {
            Guid g;
            HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == INVALID || set == IntPtr.Zero) return false;
            try
            {
                for (int i = 0; i < 256; i++)
                {
                    SP_DEVICE_INTERFACE_DATA d = new SP_DEVICE_INTERFACE_DATA();
                    d.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(req);
                    string path = null;
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, detail, req, out req, IntPtr.Zero))
                            path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                    if (path == null) continue;
                    string lp = path.ToLowerInvariant();
                    if (lp.IndexOf("054c") < 0) continue;
                    if (lp.IndexOf("0ce6") < 0 && lp.IndexOf("0df2") < 0) continue;
                    if (OpenPath(path)) return true;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return false;
        }

        bool OpenPath(string path)
        {
            IntPtr r = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (r == INVALID) { LastError = "open failed (" + Marshal.GetLastWin32Error() + ")"; return false; }
            HIDD_ATTRIBUTES a = new HIDD_ATTRIBUTES();
            a.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
            if (!HidD_GetAttributes(r, ref a) || a.VendorID != 0x054C || (a.ProductID != 0x0CE6 && a.ProductID != 0x0DF2))
            {
                CloseHandle(r);
                return false;
            }
            IntPtr pp;
            byte[] caps = new byte[64];
            if (!HidD_GetPreparsedData(r, out pp)) { CloseHandle(r); return false; }
            HidP_GetCaps(pp, caps);
            HidD_FreePreparsedData(pp);
            ushort usage = BitConverter.ToUInt16(caps, 0);
            ushort usagePage = BitConverter.ToUInt16(caps, 2);
            int il = BitConverter.ToUInt16(caps, 4);
            int ol = BitConverter.ToUInt16(caps, 6);
            int fl = BitConverter.ToUInt16(caps, 8);
            if (usagePage != 0x01 || usage != 0x05 || il == 0) { CloseHandle(r); return false; }

            IntPtr w = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (w == INVALID) { CloseHandle(r); return false; }

            inLen = il;
            outLen = ol;
            bt = il > 64 || ol > 48;
            if (bt)
            {
                // reading the calibration feature report switches a BT DualSense into full (0x31) report mode
                try
                {
                    byte[] f = new byte[Math.Max(fl, 64)];
                    f[0] = 0x05;
                    HidD_GetFeature(r, f, fl > 0 ? fl : 41);
                }
                catch { }
            }
            lock (ioLock)
            {
                readH = r;
                writeH = w;
                pendingDirty = true;
                releaseLightbar = true;
            }
            DeviceInfo = (a.ProductID == 0x0DF2 ? "DualSense Edge" : "DualSense") + (bt ? " (Bluetooth)" : " (USB)");
            connected = true;
            outEvent.Set();
            return true;
        }

        void CloseHandles()
        {
            lock (ioLock)
            {
                if (readH != INVALID) { try { CloseHandle(readH); } catch { } readH = INVALID; }
                if (writeH != INVALID) { try { CloseHandle(writeH); } catch { } writeH = INVALID; }
            }
            connected = false;
            lock (stateLock) { state = new PadState(); }
        }
    }

    public static class Crc32
    {
        static uint[] table;

        public static uint Compute(byte seed, byte[] data, int len)
        {
            if (table == null)
            {
                uint[] t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[i] = c;
                }
                table = t;
            }
            uint crc = 0xFFFFFFFFu;
            crc = table[(crc ^ seed) & 0xFF] ^ (crc >> 8);
            for (int i = 0; i < len; i++) crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }

    /// <summary>Builds the 47-byte "common" output block and adaptive trigger effects.</summary>
    public static class OutputBuilder
    {
        public static byte[] Neutral()
        {
            byte[] c = new byte[47];
            c[0] = 0x01 | 0x02 | 0x04 | 0x08;
            c[10] = 0x05; // R2 off
            c[21] = 0x05; // L2 off
            return c;
        }

        /// <param name="bigMotor">0..1 low-frequency (left) motor</param>
        /// <param name="smallMotor">0..1 high-frequency (right) motor</param>
        public static byte[] Build(float bigMotor, float smallMotor, byte[] r2, byte[] l2, bool lightbar, byte red, byte green, byte blue)
        {
            byte[] c = new byte[47];
            c[0] = 0x01 | 0x02 | 0x04 | 0x08;       // compatible vibration + haptics select + R2 + L2 effects
            c[1] = (byte)(lightbar ? 0x04 : 0x00);  // lightbar control
            c[2] = ToByte(smallMotor);
            c[3] = ToByte(bigMotor);
            Buffer.BlockCopy(r2, 0, c, 10, 11);
            Buffer.BlockCopy(l2, 0, c, 21, 11);
            c[44] = red; c[45] = green; c[46] = blue;
            return c;
        }

        static byte ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f);
        }

        // ---- adaptive trigger effects (11 bytes each) ----
        public static byte[] TriggerOff()
        {
            byte[] e = new byte[11];
            e[0] = 0x05;
            return e;
        }

        /// <summary>Resistance per zone. strengths[i] 0 = none, 1..8 = force for zone i (0 = released .. 9 = fully pressed).</summary>
        public static byte[] TriggerFeedback(int[] strengths)
        {
            uint forces = 0;
            ushort active = 0;
            for (int i = 0; i < 10; i++)
            {
                int s = strengths[i];
                if (s <= 0) continue;
                if (s > 8) s = 8;
                forces |= (uint)((s - 1) & 0x07) << (3 * i);
                active |= (ushort)(1 << i);
            }
            if (active == 0) return TriggerOff();
            byte[] e = new byte[11];
            e[0] = 0x21;
            e[1] = (byte)(active & 0xFF);
            e[2] = (byte)(active >> 8);
            e[3] = (byte)(forces & 0xFF);
            e[4] = (byte)((forces >> 8) & 0xFF);
            e[5] = (byte)((forces >> 16) & 0xFF);
            e[6] = (byte)((forces >> 24) & 0xFF);
            return e;
        }

        /// <summary>Uniform resistance from 'start' zone (0..9) with strength 1..8.</summary>
        public static byte[] TriggerFeedback(int start, int strength)
        {
            int[] s = new int[10];
            for (int i = 0; i < 10; i++) s[i] = i >= start ? strength : 0;
            return TriggerFeedback(s);
        }

        /// <summary>Vibration from 'start' zone, amplitude 1..8, frequency in Hz (1..255).</summary>
        public static byte[] TriggerVibration(int start, int amplitude, int frequency)
        {
            if (amplitude <= 0 || frequency <= 0) return TriggerOff();
            if (amplitude > 8) amplitude = 8;
            if (frequency > 255) frequency = 255;
            if (start < 0) start = 0;
            if (start > 9) start = 9;
            uint forces = 0;
            ushort active = 0;
            for (int i = start; i < 10; i++)
            {
                forces |= (uint)((amplitude - 1) & 0x07) << (3 * i);
                active |= (ushort)(1 << i);
            }
            byte[] e = new byte[11];
            e[0] = 0x26;
            e[1] = (byte)(active & 0xFF);
            e[2] = (byte)(active >> 8);
            e[3] = (byte)(forces & 0xFF);
            e[4] = (byte)((forces >> 8) & 0xFF);
            e[5] = (byte)((forces >> 16) & 0xFF);
            e[6] = (byte)((forces >> 24) & 0xFF);
            e[9] = (byte)frequency;
            return e;
        }
    }
}
