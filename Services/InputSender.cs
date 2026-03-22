using System.Runtime.InteropServices;

namespace D4Pressure.Services;

public enum MouseButton { Left = 1, Right = 2, Middle = 3, X1 = 4, X2 = 5 }

public static class InputSender
{
    // ── Win32 structs ────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_UNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUT_UNION u;
    }

    private const uint INPUT_KEYBOARD  = 1;
    private const uint INPUT_MOUSE     = 0;
    private const uint KEYEVENTF_KEYUP    = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC    = 0;

    private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
    private const uint MOUSEEVENTF_XDOWN      = 0x0080;
    private const uint MOUSEEVENTF_XUP        = 0x0100;
    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;

    // Cached once — Marshal.SizeOf uses reflection internally
    private static readonly int _inputSize = Marshal.SizeOf<INPUT>();

    // Unsafe SendInput — accepts a pointer so callers can use stackalloc (no heap alloc per press)
    [DllImport("user32.dll", SetLastError = true)]
    private static extern unsafe uint SendInput(uint nInputs, INPUT* pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // ── Keyboard API ─────────────────────────────────────────────────────────

    public static unsafe void KeyDown(ushort scanCode)
    {
        var input = MakeKeyInput(scanCode, 0);
        SendInput(1, &input, _inputSize);
    }

    public static unsafe void KeyUp(ushort scanCode)
    {
        var input = MakeKeyInput(scanCode, KEYEVENTF_KEYUP);
        SendInput(1, &input, _inputSize);
    }

    public static unsafe void Tap(ushort scanCode)
    {
        INPUT* inputs = stackalloc INPUT[2];
        inputs[0] = MakeKeyInput(scanCode, 0);
        inputs[1] = MakeKeyInput(scanCode, KEYEVENTF_KEYUP);
        SendInput(2, inputs, _inputSize);
    }

    public static ushort VkToScan(uint vk) => (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);

    // ── Mouse API ─────────────────────────────────────────────────────────────

    public static unsafe void MouseDown(MouseButton button)
    {
        var input = MakeMouseInput(button, down: true);
        SendInput(1, &input, _inputSize);
    }

    public static unsafe void MouseUp(MouseButton button)
    {
        var input = MakeMouseInput(button, down: false);
        SendInput(1, &input, _inputSize);
    }

    public static unsafe void MouseTap(MouseButton button)
    {
        INPUT* inputs = stackalloc INPUT[2];
        inputs[0] = MakeMouseInput(button, down: true);
        inputs[1] = MakeMouseInput(button, down: false);
        SendInput(2, inputs, _inputSize);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static INPUT MakeKeyInput(ushort scanCode, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUT_UNION { ki = new KEYBDINPUT { wScan = scanCode, dwFlags = KEYEVENTF_SCANCODE | flags } }
    };

    private static INPUT MakeMouseInput(MouseButton button, bool down)
    {
        (uint downFlag, uint upFlag, uint data) = button switch
        {
            MouseButton.Left   => (MOUSEEVENTF_LEFTDOWN,   MOUSEEVENTF_LEFTUP,   0u),
            MouseButton.Right  => (MOUSEEVENTF_RIGHTDOWN,  MOUSEEVENTF_RIGHTUP,  0u),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP, 0u),
            MouseButton.X1     => (MOUSEEVENTF_XDOWN,      MOUSEEVENTF_XUP,      XBUTTON1),
            MouseButton.X2     => (MOUSEEVENTF_XDOWN,      MOUSEEVENTF_XUP,      XBUTTON2),
            _                  => (MOUSEEVENTF_LEFTDOWN,   MOUSEEVENTF_LEFTUP,   0u),
        };
        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = down ? downFlag : upFlag, mouseData = data } }
        };
    }
}
