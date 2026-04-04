using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace D4Pressure.Services;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that fires globally regardless of
/// which window has focus — unlike RegisterHotKey which requires Avalonia to pump
/// WM_HOTKEY, this hook runs in the OS input stack.
///
/// Also installs a WH_MOUSE_LL hook: when the user presses real LMB,
/// InputPaused is set to true so the tap loop skips until LMB is released.
/// This prevents skill keypresses from cancelling in-game click interactions.
/// </summary>
public static class GlobalHotkey
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL    = 14;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP   = 0x0202;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    // Keep strong references — GC must not collect delegates while hooks are active
    private static LowLevelKeyboardProc? _kbProc;
    private static LowLevelMouseProc?    _mouseProc;
    private static nint _kbHookId;
    private static nint _mouseHookId;
    private static Action<int>? _callback;

    /// <summary>True during a real LMB click. Resets after 200 ms if LMB is still held (hold/drag — don't pause).</summary>
    public static volatile bool InputPaused;

    private static CancellationTokenSource? _pauseCts;

    [DllImport("user32.dll")]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    /// <summary>
    /// Installs the global keyboard and mouse hooks. Call from the UI thread.
    /// </summary>
    public static void Install(Action<int> callback)
    {
        _callback  = callback;
        _kbProc    = KbHookProc;
        _mouseProc = MouseHookProc;
        _kbHookId    = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc,    GetModuleHandle(null), 0);
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL,    _mouseProc, GetModuleHandle(null), 0);
    }

    public static void Uninstall()
    {
        if (_kbHookId != 0)    { UnhookWindowsHookEx(_kbHookId);    _kbHookId    = 0; }
        if (_mouseHookId != 0) { UnhookWindowsHookEx(_mouseHookId); _mouseHookId = 0; }
    }

    private const int LLKHF_INJECTED = 0x10;
    private const int LLMHF_INJECTED = 0x01;

    private static nint KbHookProc(int nCode, nint wParam, nint lParam)
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
        return CallNextHookEx(_kbHookId, nCode, wParam, lParam);
    }

    private static nint MouseHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WM_LBUTTONDOWN || wParam == WM_LBUTTONUP))
        {
            // MSLLHOOKSTRUCT layout: pt(8), mouseData(4), flags(4 @ offset 12), time(4), dwExtraInfo(ptr)
            int flags = Marshal.ReadInt32(lParam, 12);
            if ((flags & LLMHF_INJECTED) == 0)   // ignore synthetic mouse input
            {
                if (wParam == WM_LBUTTONDOWN)
                {
                    var old = _pauseCts;
                    _pauseCts = new CancellationTokenSource();
                    old?.Cancel();
                    old?.Dispose();
                    var token = _pauseCts.Token;
                    InputPaused = true;
                    // If still held after 200 ms it's a drag/hold — unpause.
                    // Pass token to ContinueWith so a rapid second click cancels
                    // any scheduled-but-not-yet-executed continuation from this click.
                    Task.Delay(200, token)
                        .ContinueWith(_ => InputPaused = false, token,
                            TaskContinuationOptions.NotOnCanceled,
                            TaskScheduler.Default);
                }
                else // WM_LBUTTONUP
                {
                    _pauseCts?.Cancel();
                    InputPaused = false;
                }
            }
        }
        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }
}
