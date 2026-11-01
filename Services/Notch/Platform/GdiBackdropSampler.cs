using System.Drawing;
using System.Runtime.InteropServices;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>
/// Captura del fondo bajo el notch con GDI (F5 · B6): <c>BitBlt</c> del DC de la pantalla a un bitmap compatible de
/// la franja y <c>GetDIBits</c> a BGRA de 32 bpp de arriba abajo. Sin <c>CAPTUREBLT</c>: no hace falta leer ventanas
/// layered (la del notch y su sombra lo son). Una llamada por muestreo, sin estado ni caché; cualquier fallo de Win32
/// ⇒ <c>null</c> (el notch se queda con el contraste que tenía), nunca una excepción.
/// </summary>
public sealed class GdiBackdropSampler : IBackdropSampler
{
    private const int SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0, DIB_RGB_COLORS = 0;

    public byte[]? Capture(Rectangle strip)
    {
        if (strip.Width <= 0 || strip.Height <= 0) return null;
        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return null;
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, strip.Width, strip.Height);
            if (mem == IntPtr.Zero || bmp == IntPtr.Zero) return null;
            old = SelectObject(mem, bmp);
            if (!BitBlt(mem, 0, 0, strip.Width, strip.Height, screen, strip.X, strip.Y, SRCCOPY)) return null;
            SelectObject(mem, old);   // GetDIBits pide que el bitmap no esté seleccionado en un DC
            old = IntPtr.Zero;

            var bi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = strip.Width,
                biHeight = -strip.Height,   // negativo = de arriba abajo
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            };
            var px = new byte[strip.Width * strip.Height * 4];
            return GetDIBits(mem, bmp, 0, (uint)strip.Height, px, ref bi, DIB_RGB_COLORS) == strip.Height ? px : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] lpvBits,
        ref BITMAPINFOHEADER lpbmi, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);
}
