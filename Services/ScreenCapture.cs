using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace D4Pressure.Services;

public static class ScreenCapture
{
    // ── Win32 ────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdcDst, int x, int y, int cx, int cy,
                                      nint hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    private const uint SRCCOPY    = 0x00CC0020;
    private const int  SM_CXSCREEN = 0;
    private const int  SM_CYSCREEN = 1;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the Diablo IV game window, captures the skill bar region,
    /// and returns up to 6 skill icon bitmaps.
    /// Falls back to the primary monitor if the game window is not found.
    /// </summary>
    public static Avalonia.Media.Imaging.Bitmap?[] CaptureSkillIcons()
    {
        var result = new Avalonia.Media.Imaging.Bitmap?[6];
        try
        {
            // Find D4 window — try common title variants
            nint gameWnd = FindWindow(null, "Diablo IV");
            if (gameWnd == 0) gameWnd = FindWindow(null, "Diablo® IV");
            if (gameWnd == 0) gameWnd = FindWindow(null, "Diablo 4");

            int originX, originY, sw, sh;

            if (gameWnd != 0 && GetWindowRect(gameWnd, out var wr))
            {
                // Use the game window's screen coordinates
                originX = wr.Left;
                originY = wr.Top;
                sw = wr.Right  - wr.Left;
                sh = wr.Bottom - wr.Top;
            }
            else
            {
                // Fallback: primary monitor
                originX = 0;
                originY = 0;
                sw = GetSystemMetrics(SM_CXSCREEN);
                sh = GetSystemMetrics(SM_CYSCREEN);
            }

            // Capture the screen area containing the game
            using var bmp = CaptureScreenRegion(originX, originY, sw, sh);

            // ── D4 skill bar proportions (relative to game window) ───────────
            // Measured pixel-precisely at 2560×1440; proportional so any resolution works.
            // barTop lands at the START of the icon art (below the decorative frame).
            // No key-label strip needed — the capture region contains only icon art.
            int slotW  = (int)(sw * 0.0301);
            int slotH  = (int)(sh * 0.0493);
            int barTop = (int)(sh * 0.9070);
            int gapW   = (int)(sw * 0.00273);
            int totalW = slotW * 6 + gapW * 5;
            int startX = sw / 2 - totalW / 2;

            for (int i = 0; i < 6; i++)
            {
                int x     = startX + i * (slotW + gapW);
                int iconH = slotH; // full height — already just the icon art

                var iconRect = new System.Drawing.Rectangle(x, barTop, slotW, iconH);
                using var icon = bmp.Clone(iconRect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                var ms = new MemoryStream();
                icon.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Seek(0, SeekOrigin.Begin);
                result[i] = new Avalonia.Media.Imaging.Bitmap(ms);
            }
        }
        catch
        {
            // Return nulls — UI shows placeholders
        }
        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static System.Drawing.Bitmap CaptureScreenRegion(int x, int y, int w, int h)
    {
        var screenDC = GetDC(nint.Zero);
        var memDC    = CreateCompatibleDC(screenDC);
        var hBmp     = CreateCompatibleBitmap(screenDC, w, h);
        var old      = SelectObject(memDC, hBmp);

        BitBlt(memDC, 0, 0, w, h, screenDC, x, y, SRCCOPY);

        SelectObject(memDC, old);
        DeleteDC(memDC);
        ReleaseDC(nint.Zero, screenDC);

        var result = System.Drawing.Image.FromHbitmap(hBmp);
        DeleteObject(hBmp);
        return result;
    }
}
