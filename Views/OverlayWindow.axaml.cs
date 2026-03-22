using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System;
using System.Runtime.InteropServices;

namespace D4Pressure.Views;

public partial class OverlayWindow : Window
{
    // ── Win32 ─────────────────────────────────────────────────────────────────

    private const int GWL_EXSTYLE      = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int  GetWindowLong(nint hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out WinPoint pt);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint { public int X, Y; }

    // ── Drag / resize state ───────────────────────────────────────────────────

    private enum DragMode { None, Move, Resize }

    private DragMode  _drag;
    private WindowEdge _resizeEdge;
    private PixelPoint _cursorAtStart;
    private PixelPoint _winPosAtStart;
    private double     _winWAtStart;
    private double     _winHAtStart;

    // ── Constructor ───────────────────────────────────────────────────────────

    public OverlayWindow()
    {
        InitializeComponent();

        // Window-level handlers receive events after pointer capture
        AddHandler(PointerMovedEvent,   OnPointerMoved,   Avalonia.Interactivity.RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var hwnd = (nint)TryGetPlatformHandle()!.Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE);
    }

    // ── Move drag ─────────────────────────────────────────────────────────────

    private void Content_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Don't drag when the press originated on the toggle button
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null) return;

        GetCursorPos(out var pt);
        _drag          = DragMode.Move;
        _cursorAtStart = new PixelPoint(pt.X, pt.Y);
        _winPosAtStart = Position;
        e.Pointer.Capture(ContentBorder);
    }

    // ── Resize handles ────────────────────────────────────────────────────────

    private void Resize_N(object? sender,  PointerPressedEventArgs e) => StartResize(WindowEdge.North,     e);
    private void Resize_S(object? sender,  PointerPressedEventArgs e) => StartResize(WindowEdge.South,     e);
    private void Resize_W(object? sender,  PointerPressedEventArgs e) => StartResize(WindowEdge.West,      e);
    private void Resize_E(object? sender,  PointerPressedEventArgs e) => StartResize(WindowEdge.East,      e);
    private void Resize_NW(object? sender, PointerPressedEventArgs e) => StartResize(WindowEdge.NorthWest, e);
    private void Resize_NE(object? sender, PointerPressedEventArgs e) => StartResize(WindowEdge.NorthEast, e);
    private void Resize_SW(object? sender, PointerPressedEventArgs e) => StartResize(WindowEdge.SouthWest, e);
    private void Resize_SE(object? sender, PointerPressedEventArgs e) => StartResize(WindowEdge.SouthEast, e);

    private void StartResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        GetCursorPos(out var pt);
        _drag          = DragMode.Resize;
        _resizeEdge    = edge;
        _cursorAtStart = new PixelPoint(pt.X, pt.Y);
        _winPosAtStart = Position;
        _winWAtStart   = Width;
        _winHAtStart   = Height;
        e.Pointer.Capture((IInputElement)e.Source!);
    }

    // ── Shared move/resize tracking ───────────────────────────────────────────

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag == DragMode.None) return;

        GetCursorPos(out var pt);
        var dx = pt.X - _cursorAtStart.X;
        var dy = pt.Y - _cursorAtStart.Y;

        if (_drag == DragMode.Move)
        {
            Position = new PixelPoint(_winPosAtStart.X + dx, _winPosAtStart.Y + dy);
            return;
        }

        // Resize
        double x = _winPosAtStart.X, y = _winPosAtStart.Y;
        double w = _winWAtStart,     h = _winHAtStart;

        if (_resizeEdge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest)
        {
            w = Math.Max(_winWAtStart - dx, MinWidth);
            x = _winPosAtStart.X + (_winWAtStart - w);
        }
        if (_resizeEdge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast)
            w = Math.Max(_winWAtStart + dx, MinWidth);

        if (_resizeEdge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast)
        {
            h = Math.Max(_winHAtStart - dy, MinHeight);
            y = _winPosAtStart.Y + (_winHAtStart - h);
        }
        if (_resizeEdge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast)
            h = Math.Max(_winHAtStart + dy, MinHeight);

        Position = new PixelPoint((int)x, (int)y);
        Width  = w;
        Height = h;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _drag = DragMode.None;
        e.Pointer.Capture(null);
    }
}
