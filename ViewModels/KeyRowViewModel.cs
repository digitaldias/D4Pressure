using Avalonia.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4Pressure.Services;
using System;
using MouseButton = D4Pressure.Services.MouseButton;

namespace D4Pressure.ViewModels;

public enum KeyMode { Hold, Tap }

public partial class KeyRowViewModel : ObservableObject
{
    // ── Key / mouse binding ──────────────────────────────────────────────────
    [ObservableProperty] private Key _key = Key.None;
    [ObservableProperty] private ushort _scanCode;
    [ObservableProperty] private bool _isMouseInput;
    [ObservableProperty] private MouseButton _mouseButtonIndex = MouseButton.Left;

    // ── Config ───────────────────────────────────────────────────────────────
    [ObservableProperty] private KeyMode _mode = KeyMode.Tap;
    [ObservableProperty] private string _actionLabel = string.Empty;
    [ObservableProperty] private int _delayMs = 500;
    [ObservableProperty] private bool _isEnabled = true;

    // ── UI state ─────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private bool _isSystemRunning;   // set by MainViewModel
    [ObservableProperty] private Bitmap? _icon;

    // True when this row is actively firing inputs
    public bool IsActive => IsSystemRunning && IsEnabled && (ScanCode > 0 || IsMouseInput);

    partial void OnIsSystemRunningChanged(bool value) => OnPropertyChanged(nameof(IsActive));
    partial void OnIsEnabledChanged(bool value)       => OnPropertyChanged(nameof(IsActive));
    partial void OnScanCodeChanged(ushort value)      => OnPropertyChanged(nameof(IsActive));
    partial void OnIsMouseInputChanged(bool value)    => OnPropertyChanged(nameof(IsActive));

    public string KeyDisplayName =>
        IsMouseInput ? MouseButtonLabel(MouseButtonIndex) :
        Key == Key.None ? "— bind —" : FormatKey(Key);

    private static string FormatKey(Key key)
    {
        var s = key.ToString();
        // Avalonia names digit keys "D0"–"D9" — show just the digit
        if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1]))
            return s[1..];
        return s;
    }

    partial void OnKeyChanged(Key value)          => OnPropertyChanged(nameof(KeyDisplayName));
    partial void OnMouseButtonIndexChanged(MouseButton value) => OnPropertyChanged(nameof(KeyDisplayName));

    // ── Events ───────────────────────────────────────────────────────────────
    public event Action<KeyRowViewModel>? RemoveRequested;
    public event Action<KeyRowViewModel>? CaptureRequested;

    // ── Commands ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private void StartCapture()
    {
        IsCapturing = true;
        CaptureRequested?.Invoke(this);
    }

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    // ── Apply methods ─────────────────────────────────────────────────────────
    public void ApplyKey(Key key, ushort scanCode)
    {
        IsMouseInput = false;
        Key      = key;
        ScanCode = scanCode;
        IsCapturing = false;
        NotifyKeyUIChanged();
    }

    public void ApplyMouseButton(MouseButton btn)
    {
        IsMouseInput     = true;
        MouseButtonIndex = btn;
        ScanCode         = 0;
        IsCapturing      = false;
        NotifyKeyUIChanged();
    }

    public void ClearKey()
    {
        IsMouseInput = false;
        Key      = Key.None;
        ScanCode = 0;
        IsCapturing = false;
        NotifyKeyUIChanged();
    }

    private void NotifyKeyUIChanged()
    {
        OnPropertyChanged(nameof(KeyDisplayName));
        OnPropertyChanged(nameof(IsActive));
    }

    private static string MouseButtonLabel(MouseButton btn) => btn switch
    {
        MouseButton.Left   => "LMB",
        MouseButton.Right  => "RMB",
        MouseButton.Middle => "MMB",
        MouseButton.X1     => "MB4",
        MouseButton.X2     => "MB5",
        _                  => "LMB"
    };
}
