using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using D4Pressure.Services;
using D4Pressure.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace D4Pressure.Views;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext!;
    private nint _hwnd;
    private bool _inputHasFocus;
    private OverlayWindow? _overlay;

    // ── Win32 window style ────────────────────────────────────────────────────
    private const int GWL_EXSTYLE        = -20;
    private const int WS_EX_NOACTIVATE   = 0x08000000;
    private const int DefaultOverlayWidth = 260;

    [DllImport("user32.dll")] private static extern int  GetWindowLong(nint hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsCapturing))
            {
                if (vm.IsCapturing)
                {
                    SetNoActivate(false);
                    CaptureOverlay.Focus();
                }
                else if (!vm.IsAddingCharacter && !_inputHasFocus)
                {
                    SetNoActivate(true);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.IsAddingCharacter))
            {
                if (vm.IsAddingCharacter)
                    SetNoActivate(false);
                else if (!vm.IsCapturing && !_inputHasFocus)
                    SetNoActivate(true);
            }
        };
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _hwnd = (nint)TryGetPlatformHandle()!.Handle;

        SetNoActivate(true);

        AddHandler(InputElement.GotFocusEvent,  OnInputGotFocus,  RoutingStrategies.Bubble);
        AddHandler(InputElement.LostFocusEvent, OnInputLostFocus, RoutingStrategies.Bubble);

        // One-shot: reposition window once AutoLoad has set MainWindowX
        PropertyChangedEventHandler? posHandler = null;
        posHandler = (_, pe) =>
        {
            if (pe.PropertyName != nameof(MainViewModel.MainWindowX)) return;
            Vm.PropertyChanged -= posHandler;
            if (Vm.MainWindowX is int x && Vm.MainWindowY is int y)
            {
                var pos = new Avalonia.PixelPoint(x, y);
                if (Screens.All.Any(s => s.Bounds.Contains(pos)))
                    Position = pos;
            }
        };
        Vm.PropertyChanged += posHandler;

        // Track position changes in-memory (persisted on exit)
        PositionChanged += (_, _) =>
        {
            Vm.MainWindowX = Position.X;
            Vm.MainWindowY = Position.Y;
        };

        GlobalHotkey.Install(vk =>
        {
            if (vk == 0x71)      // F2 — toggle
                Dispatcher.UIThread.Post(() => Vm.Toggle_Hotkey());
            else if (vk == 0x72) // F3 — overlay window
                Dispatcher.UIThread.Post(ToggleOverlay);
            else if (vk == 0x78) // F9 — quit
                Dispatcher.UIThread.Post(Close);
            else if (Vm.ToggleVk != 0 && vk == Vm.ToggleVk)
                Dispatcher.UIThread.Post(() => Vm.Toggle_Hotkey());
        });

        Dispatcher.UIThread.Post(() => Vm.AutoLoad(), DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_overlay is not null)
        {
            // Set VM properties only — SaveOnExit below handles the single disk write
            var pos = _overlay.Position;
            Vm.OverlayX         = pos.X;
            Vm.OverlayY         = pos.Y;
            Vm.OverlayW         = (int)_overlay.Width;
            Vm.OverlayH         = (int)_overlay.Height;
            Vm.OverlayScreenIdx = GetScreenIndex(pos);
            _overlay.Close();
        }
        GlobalHotkey.Uninstall();
        if (Vm.IsRunning) Vm.Toggle_Hotkey();
        Vm.SaveOnExit();
        base.OnClosed(e);
    }

    // ── Input focus — lift NOACTIVATE while typing ────────────────────────────

    private void OnInputGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is TextBox)
        {
            _inputHasFocus = true;
            SetNoActivate(false);
            Activate(); // bring window to foreground so keyboard input reaches the text field
        }
    }

    private void OnInputLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox) return;
        _inputHasFocus = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_inputHasFocus && !Vm.IsCapturing && !Vm.IsAddingCharacter)
                SetNoActivate(true);
        });
    }

    // ── Capture overlay — keyboard ────────────────────────────────────────────

    private void CaptureOverlay_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            Vm.CapturingRow?.ClearKey();
            Vm.CapturingRow = null;
            Vm.ClearToggleKey();
            return;
        }

        if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
                  or Key.LeftAlt  or Key.RightAlt  or Key.LWin    or Key.RWin or Key.None)
            return;

        // Toggle key capture mode
        if (Vm.IsCapturingToggleKey)
        {
            var vk = KeyToVk(e.Key);
            Vm.ApplyToggleKey(e.Key, vk);
            return;
        }

        // Row key capture mode
        if (Vm.CapturingRow is null) return;
        var rowVk   = (uint)KeyToVk(e.Key);
        var scan    = rowVk > 0 ? InputSender.VkToScan(rowVk) : (ushort)0;
        Vm.CapturingRow.ApplyKey(e.Key, scan);
        Vm.CapturingRow = null;
    }

    // ── Capture overlay — mouse ───────────────────────────────────────────────

    private void CaptureOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm.CapturingRow is null) return;
        e.Handled = true;

        var props = e.GetCurrentPoint(null).Properties;
        D4Pressure.Services.MouseButton? btn =
            props.IsLeftButtonPressed   ? D4Pressure.Services.MouseButton.Left   :
            props.IsRightButtonPressed  ? D4Pressure.Services.MouseButton.Right  :
            props.IsMiddleButtonPressed ? D4Pressure.Services.MouseButton.Middle :
            props.IsXButton1Pressed     ? D4Pressure.Services.MouseButton.X1     :
            props.IsXButton2Pressed     ? D4Pressure.Services.MouseButton.X2     :
            null;

        if (btn is null) return;
        Vm.CapturingRow.ApplyMouseButton(btn.Value);
        Vm.CapturingRow = null;
    }

    // ── Overlay window ────────────────────────────────────────────────────────

    private void ToggleOverlay()
    {
        if (_overlay is not null)
        {
            SaveOverlayGeometryFromWindow(_overlay);
            _overlay.Close();
            // _overlay is cleared in the Closed handler
            return;
        }

        _overlay = new OverlayWindow { DataContext = Vm };
        _overlay.Closed += (_, _) => _overlay = null;

        // Restore saved size
        if (Vm.OverlayW is int ow && Vm.OverlayH is int oh)
        {
            _overlay.Width  = ow;
            _overlay.Height = oh;
        }

        // Restore saved position — with screen-aware fallback
        bool positioned = false;
        if (Vm.OverlayX is int ox && Vm.OverlayY is int oy)
        {
            var saved = new Avalonia.PixelPoint(ox, oy);
            if (Screens.All.Any(s => s.Bounds.Contains(saved)))
            {
                _overlay.Position = saved;
                positioned = true;
            }
            else if (Vm.OverlayScreenIdx is int si)
            {
                // The screen the overlay was on is gone — find it by sorted index
                var sorted = SortedScreens();
                var target = si < sorted.Count ? sorted[si] : Screens.Primary;
                if (target is not null)
                {
                    _overlay.Position = CenteredOnScreen(target, Vm.OverlayW ?? DefaultOverlayWidth);
                    positioned = true;
                }
            }
        }

        if (!positioned)
        {
            var screen = Screens.Primary;
            if (screen is not null)
                _overlay.Position = CenteredOnScreen(screen, Vm.OverlayW ?? DefaultOverlayWidth);
        }

        _overlay.Show();
    }

    private void SaveOverlayGeometryFromWindow(OverlayWindow overlay)
    {
        var pos = overlay.Position;
        Vm.SaveOverlayGeometry(pos.X, pos.Y, (int)overlay.Width, (int)overlay.Height, GetScreenIndex(pos));
    }

    private int GetScreenIndex(Avalonia.PixelPoint pos)
    {
        var sorted = SortedScreens();
        for (int i = 0; i < sorted.Count; i++)
            if (sorted[i].Bounds.Contains(pos))
                return i;
        return 0;
    }

    private List<Screen> SortedScreens() =>
        Screens.All.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList();

    private static Avalonia.PixelPoint CenteredOnScreen(Screen screen, int width) =>
        new(screen.Bounds.X + (screen.Bounds.Width - width) / 2, screen.Bounds.Y + 60);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetNoActivate(bool noActivate)
    {
        if (_hwnd == 0) return;
        var style = GetWindowLong(_hwnd, GWL_EXSTYLE);
        if (noActivate) style |=  WS_EX_NOACTIVATE;
        else            style &= ~WS_EX_NOACTIVATE;
        SetWindowLong(_hwnd, GWL_EXSTYLE, style);
    }

    private static int KeyToVk(Key key) => key switch
    {
        Key.D0 => 0x30, Key.D1 => 0x31, Key.D2 => 0x32, Key.D3 => 0x33,
        Key.D4 => 0x34, Key.D5 => 0x35, Key.D6 => 0x36, Key.D7 => 0x37,
        Key.D8 => 0x38, Key.D9 => 0x39,
        Key.A => 0x41, Key.B => 0x42, Key.C => 0x43, Key.D => 0x44,
        Key.E => 0x45, Key.F => 0x46, Key.G => 0x47, Key.H => 0x48,
        Key.I => 0x49, Key.J => 0x4A, Key.K => 0x4B, Key.L => 0x4C,
        Key.M => 0x4D, Key.N => 0x4E, Key.O => 0x4F, Key.P => 0x50,
        Key.Q => 0x51, Key.R => 0x52, Key.S => 0x53, Key.T => 0x54,
        Key.U => 0x55, Key.V => 0x56, Key.W => 0x57, Key.X => 0x58,
        Key.Y => 0x59, Key.Z => 0x5A,
        Key.F1  => 0x70, Key.F2  => 0x71, Key.F3  => 0x72, Key.F4  => 0x73,
        Key.F5  => 0x74, Key.F6  => 0x75, Key.F7  => 0x76, Key.F8  => 0x77,
        Key.F9  => 0x78, Key.F10 => 0x79, Key.F11 => 0x7A, Key.F12 => 0x7B,
        Key.Space    => 0x20, Key.Return   => 0x0D, Key.Tab    => 0x09,
        Key.Back     => 0x08, Key.Escape   => 0x1B, Key.Delete => 0x2E,
        Key.Insert   => 0x2D, Key.Home     => 0x24, Key.End    => 0x23,
        Key.PageUp   => 0x21, Key.PageDown => 0x22,
        Key.Left     => 0x25, Key.Up       => 0x26, Key.Right  => 0x27, Key.Down => 0x28,
        Key.LeftShift or Key.RightShift => 0x10,
        Key.LeftCtrl  or Key.RightCtrl  => 0x11,
        Key.LeftAlt   or Key.RightAlt   => 0x12,
        Key.NumPad0 => 0x60, Key.NumPad1 => 0x61, Key.NumPad2 => 0x62,
        Key.NumPad3 => 0x63, Key.NumPad4 => 0x64, Key.NumPad5 => 0x65,
        Key.NumPad6 => 0x66, Key.NumPad7 => 0x67, Key.NumPad8 => 0x68,
        Key.NumPad9 => 0x69,
        Key.Multiply => 0x6A, Key.Add     => 0x6B,
        Key.Subtract => 0x6D, Key.Divide  => 0x6F,
        Key.Decimal  => 0x6E,
        _ => 0
    };
}
