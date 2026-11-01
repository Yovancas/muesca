using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Renderer GDI+ sobre ventanas layered (spec §4.1, lenguaje de movimiento §6.3).
///
/// <para>Dos superficies, una por ventana: la de la pastilla (forma, contenido y zona de clic invisible) y
/// la de la sombra (sombra y halo, en una ventana <c>WS_EX_TRANSPARENT</c> que no recibe clics). Cada una
/// es una DIB section 32bpp top-down creada UNA vez por tamaño de superficie (y escala): se selecciona en
/// un DC de memoria y se envuelve en un <see cref="Bitmap"/> PArgb que apunta a SUS píxeles, así GDI+
/// pinta directamente en la memoria que <c>UpdateLayeredWindow</c> lee. Nada de <c>GetHbitmap</c> por frame
/// (copia de la superficie entera + objeto GDI nuevo cada vez).</para>
///
/// <para>Por frame: se borra solo lo que se pintó en el frame anterior, se pinta y se entrega con
/// <c>ULW_ALPHA</c>. <see cref="NotchFrame.PillAlpha"/> va como alfa constante de la mezcla en las dos
/// ventanas. La sombra solo se repinta si cambian la forma, sus alfas o la posición (el spinner a 30 fps
/// no la toca). En la ventana de la pastilla los píxeles con alfa 0 dejan pasar el ratón.</para>
/// </summary>
public sealed class LayeredGdiRenderer : INotchRenderer
{
    private readonly NotchContentPainter _painter;
    private readonly bool _ownsPainter;
    private readonly LayeredSurface _pill = new(), _shadow = new();

    private SolidBrush? _padBrush;
    private ShadowKey? _lastShadow;  // lo último entregado a la ventana de sombra (para no repetirlo)
    private bool _failureLogged;
    private bool _disposed;

    /// <param name="painter">Quien pinta el contenido. Si <paramref name="ownsPainter"/>, se libera con el renderer.</param>
    public LayeredGdiRenderer(NotchContentPainter painter, bool ownsPainter = true)
    {
        _painter = painter ?? throw new ArgumentNullException(nameof(painter));
        _ownsPainter = ownsPainter;
    }

    /// <summary>Superficie actual de la pastilla (0×0 hasta el primer frame). Para diagnóstico y para NotchForm.</summary>
    public Size SurfaceSize => _pill.Size;

    public void Render(IntPtr hwnd, IntPtr shadowHwnd, Point topLeft, Size surface, NotchFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_disposed || hwnd == IntPtr.Zero || surface.Width <= 0 || surface.Height <= 0) return;
        byte constAlpha = (byte)Math.Round(Math.Clamp(frame.PillAlpha, 0f, 1f) * 255f);
        try
        {
            var g = _pill.Begin(surface, frame.Scale);
            var drawn = PaintHitPad(g, surface, frame, constAlpha);
            var body = _painter.Paint(g, surface, frame);
            _pill.End(drawn.IsEmpty ? body : body.IsEmpty ? drawn : RectangleF.Union(drawn, body), surface);
            if (!_pill.Present(hwnd, topLeft, surface, constAlpha))
                LogOnce($"UpdateLayeredWindow falló (error {Marshal.GetLastWin32Error()})", null);

            if (shadowHwnd != IntPtr.Zero) RenderShadow(shadowHwnd, topLeft, surface, frame, constAlpha);
        }
        catch (Exception ex)
        {
            // Un frame perdido no debe tumbar la app; se registra la primera vez para no inundar el log.
            LogOnce("Render del notch falló", ex);
        }
    }

    /// <summary>Sombra y halo en su ventana, solo si algo de lo que se ve cambió desde la última entrega.</summary>
    private void RenderShadow(IntPtr hwnd, Point topLeft, Size surface, NotchFrame f, byte constAlpha)
    {
        var key = new ShadowKey(hwnd, f.Mode == NotchMode.Hidden, f.Width, f.Height, f.Squash, f.Scale,
            f.ShadowAlpha, f.HaloAlpha, constAlpha, topLeft, surface);
        if (_lastShadow == key) return;

        var g = _shadow.Begin(surface, f.Scale);
        _shadow.End(_painter.PaintShadow(g, surface, f), surface);
        if (_shadow.Present(hwnd, topLeft, surface, constAlpha)) _lastShadow = key;
        else LogOnce($"UpdateLayeredWindow (sombra) falló (error {Marshal.GetLastWin32Error()})", null);
    }

    /// <summary>
    /// Zona de clic invisible: el contorno de <see cref="NotchGeometry.HitPadPath"/> con alfa 1/255, debajo
    /// de la pastilla. Sin ella el margen de 4·s y la zona mínima de Mínimo serían alfa 0 y el ratón los
    /// atravesaría (Windows ni siquiera manda <c>WM_NCHITTEST</c>). Con la ventana atenuada (Snap, fundido de
    /// aparición) el alfa se compensa con el alfa constante para que el efectivo no redondee a 0: sigue
    /// siendo invisible (≤ 1/255 tras la mezcla) y sigue recibiendo el ratón. Hidden → nada.
    /// </summary>
    private RectangleF PaintHitPad(Graphics g, Size surface, NotchFrame f, byte constAlpha)
    {
        if (f.Mode == NotchMode.Hidden) return RectangleF.Empty;
        using var pad = NotchGeometry.HitPadPath(f.Width, f.Height, f.Scale, surface);
        if (f.Hold.Active)
        {
            // Cambio de página: la zona de hover es la unión con la forma vieja y la nueva (NotchGeometry.HitTest).
            using var from = NotchGeometry.HitPadPath(f.Hold.From.Width, f.Hold.From.Height, f.Scale, surface);
            using var to = NotchGeometry.HitPadPath(f.Hold.To.Width, f.Hold.To.Height, f.Scale, surface);
            pad.AddPath(from, false);
            pad.AddPath(to, false);
        }
        if (pad.PointCount == 0) return RectangleF.Empty;
        int alpha = constAlpha == 0 ? 1 : Math.Min(255, (255 + constAlpha - 1) / constAlpha);   // ceil(255 / c)
        if (_padBrush is null || _padBrush.Color.A != alpha)
        {
            _padBrush?.Dispose();
            _padBrush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
        }
        g.SmoothingMode = SmoothingMode.None;     // alfa mínimo sin AA: con AA los bordes quedarían a 0
        g.FillPath(_padBrush, pad);
        return pad.GetBounds();
    }

    private void LogOnce(string message, Exception? ex)
    {
        if (_failureLogged) return;
        _failureLogged = true;
        AppLog.Error(message, ex);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pill.Dispose();
        _shadow.Dispose();
        _padBrush?.Dispose();
        if (_ownsPainter) _painter.Dispose();
    }

    /// <summary>Lo que decide el aspecto de la ventana de sombra: si no cambia, no se repinta ni se entrega.</summary>
    private readonly record struct ShadowKey(IntPtr Hwnd, bool Hidden, float Width, float Height, float Squash, float Scale,
        float Shadow, float Halo, byte Alpha, Point TopLeft, Size Surface);
}
