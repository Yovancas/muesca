using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Services.Mascot;

/// <summary>
/// Ojos del gato que miran el cursor (lenguaje de movimiento §5). Solo se alimenta con el
/// <c>WM_MOUSEMOVE</c> de la propia ventana mientras el cursor está dentro (sin hooks ni sondeo). El
/// objetivo es clamp(dx/60, ±2) px en horizontal y clamp(dy/80, ±1) px en vertical, en px enteros y a
/// escala de DPI, y se alcanza con <see cref="Motion.Motion.Ajuste"/> (95 % a 279 ms). Al salir el
/// cursor, los ojos vuelven al centro a los <see cref="Motion.Motion.EyesReturnMs"/>. Con el cursor
/// quieto no hay fotogramas. PURO: recibe el tiempo.
/// </summary>
public sealed class EyeTracker
{
    private readonly SpringValue _x = new(0, Motion.Motion.Ajuste);
    private readonly SpringValue _y = new(0, Motion.Motion.Ajuste);
    private double? _returnAtMs;

    /// <summary>Objetivo (px enteros) para el cursor a (dx, dy) px del centro de la cara.</summary>
    public static (int Dx, int Dy) TargetFor(double dx, double dy, double scale)
    {
        double s = scale > 0 ? scale : 1;
        double h = Math.Clamp(dx / (60 * s), -2, 2) * s;
        double v = Math.Clamp(dy / (80 * s), -1, 1) * s;
        return ((int)Math.Round(h, MidpointRounding.AwayFromZero), (int)Math.Round(v, MidpointRounding.AwayFromZero));
    }

    /// <summary>Desplazamiento actual de los ojos (px enteros: el texto nunca va en fracciones).</summary>
    public (int Dx, int Dy) Offset => ((int)Math.Round(_x.Value, MidpointRounding.AwayFromZero),
                                       (int)Math.Round(_y.Value, MidpointRounding.AwayFromZero));

    /// <summary>¿Se están moviendo los ojos?</summary>
    public bool IsAnimating => !_x.IsSettled || !_y.IsSettled;

    /// <summary>Hora (ms) a la que los ojos volverán al centro, o null.</summary>
    public double? NextDeadlineMs => _returnAtMs;

    /// <summary>El cursor se movió dentro del panel a (dx, dy) px del centro de la cara.</summary>
    public void Track(double dx, double dy, double scale, double nowMs)
    {
        _returnAtMs = null;
        var (tx, ty) = TargetFor(dx, dy, scale);
        if (_x.Target != tx) _x.SetTarget(tx);
        if (_y.Target != ty) _y.SetTarget(ty);
    }

    /// <summary>El cursor salió: los ojos vuelven al centro dentro de 1,5 s.</summary>
    public void Leave(double nowMs) => _returnAtMs = nowMs + Motion.Motion.EyesReturnMs;

    /// <summary>Al centro al instante (panel oculto, reduce-motion).</summary>
    public void Reset()
    {
        _returnAtMs = null;
        _x.Snap(0);
        _y.Snap(0);
    }

    /// <summary>Integra <paramref name="dtSeconds"/> y aplica el plazo de vuelta si ya llegó.</summary>
    public void Advance(double dtSeconds, double nowMs)
    {
        if (_returnAtMs is { } at && nowMs >= at)
        {
            _returnAtMs = null;
            _x.SetTarget(0);
            _y.SetTarget(0);
        }
        _x.Advance(dtSeconds);
        _y.Advance(dtSeconds);
    }
}
