using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Captura del fondo del escritorio (la de verdad es Win32, <c>GdiBackdropSampler</c>; los tests usan un doble).</summary>
public interface IBackdropSampler
{
    /// <summary>Píxeles BGRA de 32 bpp de <paramref name="strip"/> (px físicos de pantalla), fila a fila y sin relleno;
    /// <c>null</c> si no se pudo capturar.</summary>
    byte[]? Capture(Rectangle strip);
}

/// <summary>
/// Contraste adaptativo del notch (F5 · B6). Quien lo usa llama a <see cref="Request"/> SOLO en arranque, cambio de
/// primer plano, fin de arrastre/redimensionado y fin de pantalla completa; cada petición reinicia un debounce de
/// <see cref="Debounce"/> (una ráfaga = una captura). <see cref="Tick"/> captura una vez vencido el plazo y deja de
/// estar pendiente pase lo que pase (también si la captura falla): nunca reintenta ni muestrea en bucle.
/// <see cref="ContrastPolicy"/> decide con histéresis. Tiempo como <see cref="TimeSpan"/> monótono (el reloj de la ventana).
/// </summary>
public sealed class AdaptiveContrast
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly IBackdropSampler _sampler;
    private TimeSpan? _due;

    public AdaptiveContrast(IBackdropSampler sampler) =>
        _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));

    /// <summary>Contraste vigente; empieza en <see cref="NotchContrast.OnLight"/> (el notch de siempre).</summary>
    public NotchContrast Current { get; private set; } = NotchContrast.OnLight;

    /// <summary>Pide un muestreo dentro de <see cref="Debounce"/> (reinicia la cuenta si ya había uno pendiente).</summary>
    public void Request(TimeSpan now) => _due = now + Debounce;

    /// <summary>Olvida el muestreo pendiente (p. ej. al esconderse en pantalla completa).</summary>
    public void Cancel() => _due = null;

    /// <summary>Vuelve al estado de arranque (sin muestreo pendiente y en <see cref="NotchContrast.OnLight"/>). Al apagar
    /// el ajuste: así, al encenderlo de nuevo, no se pinta un contraste viejo hasta el primer muestreo.</summary>
    public void Reset()
    {
        _due = null;
        Current = NotchContrast.OnLight;
    }

    /// <summary>Cuánto falta para el muestreo pendiente; <c>null</c> si no hay ninguno (no hace falta despertar).</summary>
    public TimeSpan? NextWakeIn(TimeSpan now) => _due is { } d ? (d > now ? d - now : TimeSpan.Zero) : null;

    /// <summary>Si el plazo venció, captura <paramref name="strip"/> y decide. Devuelve true solo si
    /// <see cref="Current"/> cambió (hay que repintar).</summary>
    public bool Tick(TimeSpan now, Rectangle strip)
    {
        if (_due is not { } d || now < d) return false;
        _due = null;
        if (strip.Width <= 0 || strip.Height <= 0) return false;

        var px = _sampler.Capture(strip);
        if (px is null || px.Length < strip.Width * strip.Height * 4) return false;

        var next = ContrastPolicy.Decide(ContrastPolicy.Analyze(px, strip.Width, strip.Height), Current);
        if (next == Current) return false;
        Current = next;
        return true;
    }

    /// <summary>
    /// Franja de 1 px de alto y el ancho de <paramref name="pill"/> (px físicos), centrada y justo debajo de la pastilla,
    /// dejando 1 px de aire para no leer su borde con AA. <paramref name="surfaceTopLeft"/> y
    /// <paramref name="surfaceWidth"/> son los de la superficie del notch, centrada arriba en su monitor.
    /// </summary>
    public static Rectangle StripBelow(Point surfaceTopLeft, int surfaceWidth, SizeF pill)
    {
        int w = (int)MathF.Round(pill.Width);
        if (w <= 0 || pill.Height <= 0) return Rectangle.Empty;
        int h = (int)MathF.Ceiling(pill.Height);
        return new Rectangle(surfaceTopLeft.X + (surfaceWidth - w) / 2, surfaceTopLeft.Y + h + 1, w, 1);
    }
}
