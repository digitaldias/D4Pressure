using System;
using System.Runtime.InteropServices;

namespace D4Pressure.Services;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that fires globally regardless of
/// which window has focus — unlike RegisterHotKey which requires Avalonia to pump
/// WM_HOTKEY, this hook runs in the OS input stack.
/// </summary>
public static class GlobalHotkey
{
    private const int WH_KEYBOARD_LL  = 13;
    private const int WM_KEYDOWN      = 0x0100;
    private const int WM_SYSKEYDOWN   = 0x0104;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    // Keep a strong reference — GC must not collect the delegate while the hook is active
    private static LowLevelKeyboardProc? _proc;
    private static nint _hookId;
    private static Action<int>? _callback;

    [DllImport("user32.dll")]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    /// <summary>
    /// Installs the global hook. <paramref name="callback"/> receives the Win32 VK code
    /// for every key-down event system-wide. Call from the UI thread.
    /// </summary>
    public static void Install(Action<int> callback)
    {
        _callback = callback;
        _proc = HookProc;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public static void Uninstall()
    {
        if (_hookId != 0)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = 0;
        }
    }

    private const int LLKHF_INJECTED = 0x10;

    private static nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int flags = Marshal.ReadInt32(lParam, 8);
            if ((flags & LLKHF_INJECTED) == 0)   // ignore synthetic input from SendInput
            {
                int vk = Marshal.ReadInt32(lParam);
                _callback?.Invoke(vk);
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
