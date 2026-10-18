using System;
using System.Runtime.InteropServices;

namespace DualSensePad
{
    /// <summary>Synthesizes real mouse / keyboard events (the game reads raw mouse look and mouse buttons).</summary>
    public static class NativeInput
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT_M
        {
            public int type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT_K
        {
            public int type;
            public KEYBDINPUT ki;
            public int pad1;
            public int pad2;
        }

        [DllImport("user32.dll", EntryPoint = "SendInput")]
        static extern uint SendInputM(uint n, ref INPUT_M input, int size);
        [DllImport("user32.dll", EntryPoint = "SendInput")]
        static extern uint SendInputK(uint n, ref INPUT_K input, int size);
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint code, uint mapType);

        public const uint MOVE = 0x0001, LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010,
            MIDDLEDOWN = 0x0020, MIDDLEUP = 0x0040, WHEEL = 0x0800;
        const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;

        public const ushort VK_ESCAPE = 0x1B, VK_TAB = 0x09, VK_RETURN = 0x0D;

        static int sizeM = Marshal.SizeOf(typeof(INPUT_M));
        static int sizeK = Marshal.SizeOf(typeof(INPUT_K));

        static void Mouse(int dx, int dy, uint data, uint flags)
        {
            INPUT_M i = new INPUT_M();
            i.type = 0;
            i.mi.dx = dx;
            i.mi.dy = dy;
            i.mi.mouseData = data;
            i.mi.dwFlags = flags;
            SendInputM(1, ref i, sizeM);
        }

        public static void MoveRelative(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            Mouse(dx, dy, 0, MOVE);
        }

        public static void Wheel(int notches)
        {
            if (notches == 0) return;
            Mouse(0, 0, unchecked((uint)(notches * 120)), WHEEL);
        }

        /// <param name="button">0 = left, 1 = right, 2 = middle</param>
        public static void MouseButton(int button, bool down)
        {
            uint f;
            if (button == 0) f = down ? LEFTDOWN : LEFTUP;
            else if (button == 1) f = down ? RIGHTDOWN : RIGHTUP;
            else f = down ? MIDDLEDOWN : MIDDLEUP;
            Mouse(0, 0, 0, f);
        }

        public static void Key(ushort vk, bool down)
        {
            INPUT_K i = new INPUT_K();
            i.type = 1;
            i.ki.wVk = vk;
            i.ki.wScan = (ushort)MapVirtualKey(vk, 0);
            i.ki.dwFlags = down ? 0u : KEYEVENTF_KEYUP;
            SendInputK(1, ref i, sizeK);
        }
    }
}
