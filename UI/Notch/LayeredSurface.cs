using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClaudeBarWin.UI.Notch;

/// <summary>Una DIB persistente + su <see cref="Graphics"/>, con el rectángulo sucio del frame anterior: lo que
/// <see cref="LayeredGdiRenderer"/> entrega con <c>UpdateLayeredWindow</c> (y el asistente de bienvenida, igual).</summary>
internal sealed class LayeredSurface : IDisposable
{
    private IntPtr _memDc, _dib, _oldBmp;
    private Bitmap? _bitmap;
    private Graphics? _g;
    private float _scale;
    private Rectangle _dirty;      // lo pintado en el frame anterior (lo único que hay que borrar)

    public Size Size { get; private set; }

    /// <summary>Prepara la superficie (la recrea si cambian tamaño o escala) y borra el frame anterior.</summary>
    public Graphics Begin(Size surface, float scale)
    {
        Ensure(surface, scale);
        var g = _g!;
        // Borrar solo el rectángulo del frame anterior (al recrear la superficie, ya nace vacía).
        if (!_dirty.IsEmpty)
        {
            g.SetClip(_dirty);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.ResetClip();
        }
        return g;
    }

    /// <summary>Cierra el frame: vuelca GDI+ a la DIB y apunta lo pintado para borrarlo en el siguiente.</summary>
    public void End(RectangleF drawn, Size surface)
    {
        _g!.Flush(FlushIntention.Sync);
        // Ceiling sube cada componente (también X/Y): el margen de 2 px cubre ese medio píxel y el antialias.
        _dirty = drawn.Width <= 0 || drawn.Height <= 0 ? Rectangle.Empty
            : Rectangle.Intersect(Rectangle.Inflate(Rectangle.Ceiling(drawn), 2, 2), new Rectangle(Point.Empty, surface));
    }

    /// <summary>Entrega la DIB a la ventana con <c>ULW_ALPHA</c> y <paramref name="constAlpha"/> como alfa constante.</summary>
    public bool Present(IntPtr hwnd, Point topLeft, Size surface, byte constAlpha)
    {
        var dst = new POINT { X = topLeft.X, Y = topLeft.Y };
        var size = new SIZE { cx = surface.Width, cy = surface.Height };
        var src = new POINT();
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = constAlpha, AlphaFormat = AC_SRC_ALPHA };
        return UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0, ref blend, ULW_ALPHA);
    }

    /// <summary>(Re)crea la DIB solo si cambian la superficie o la escala (DPI / tamaño del notch).</summary>
    private void Ensure(Size surface, float scale)
    {
        if (_g is not null && surface == Size && Math.Abs(scale - _scale) < 0.001f) return;
        Release();

        var bmi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = surface.Width,
            biHeight = -surface.Height,    // negativo = top-down: misma disposición que el Bitmap
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };
        _memDc = CreateCompatibleDC(IntPtr.Zero);
        if (_memDc == IntPtr.Zero) throw new InvalidOperationException("CreateCompatibleDC falló");
        _dib = CreateDIBSection(_memDc, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            Release();
            throw new InvalidOperationException("CreateDIBSection falló");
        }
        _oldBmp = SelectObject(_memDc, _dib);

        // La DIB nace a ceros (transparente) y GDI+ escribe en ella premultiplicado, que es lo que pide ULW.
        _bitmap = new Bitmap(surface.Width, surface.Height, surface.Width * 4, PixelFormat.Format32bppPArgb, bits);
        _g = Graphics.FromImage(_bitmap);
        Size = surface;
        _scale = scale;
        _dirty = Rectangle.Empty;
    }

    private void Release()
    {
        _g?.Dispose(); _g = null;
        _bitmap?.Dispose(); _bitmap = null;
        if (_memDc != IntPtr.Zero && _oldBmp != IntPtr.Zero) SelectObject(_memDc, _oldBmp);
        if (_dib != IntPtr.Zero) DeleteObject(_dib);
        if (_memDc != IntPtr.Zero) DeleteDC(_memDc);
        _memDc = _dib = _oldBmp = IntPtr.Zero;
        Size = Size.Empty;
        _dirty = Rectangle.Empty;
    }

    public void Dispose() => Release();

    // ---------------------------------------------------------------- Win32

    private const int ULW_ALPHA = 0x2;
    private const byte AC_SRC_OVER = 0x00, AC_SRC_ALPHA = 0x01;
    private const int BI_RGB = 0, DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, int usage,
        out IntPtr ppvBits, IntPtr hSection, int offset);
}
