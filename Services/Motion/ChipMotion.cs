namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Chip de celebración «✓ cuota renovada» (lenguaje de movimiento §4 #6). PURO, función del tiempo
/// desde que se dispara la celebración:
/// <list type="bullet">
/// <item>A los <see cref="Motion.ChipBgDelayMs"/> el fondo pasa del 70 al 100 % de ancho con Abrir (florece
/// un 2,9 %) y su opacidad de 0 a 1 con Rápido.</item>
/// <item>El texto entra <see cref="Motion.ChipTextDelayMs"/> después, con alfa 0 → 1 en
/// <see cref="Motion.ChipTextFadeMs"/> (ease-out), sin escalar (el texto nunca se escala).</item>
/// <item>Se mantiene hasta <see cref="Motion.CelebrationMs"/> y sale con opacidad (Rápido) y una
/// subida de <see cref="Motion.ChipRisePx"/> px enteros.</item>
/// </list>
/// </summary>
public static class ChipMotion
{
    /// <summary>Un fotograma del chip: ancho relativo del fondo, opacidades y subida (px enteros).</summary>
    public readonly record struct Frame(double BgWidthFrac, double BgAlpha, double TextAlpha, int RisePx)
    {
        /// <summary>Chip pleno y quieto (render offline).</summary>
        public static Frame Full => new(1.0, 1.0, 1.0, 0);
    }

    private const double StartWidth = 0.7;

    private static double ExitProgress(double tMs)
    {
        double p = SpringCurve.Progress(tMs - Motion.CelebrationMs, Motion.Rapido);
        return 1 - p < Motion.Rapido.EpsX ? 1 : p;   // mismo encaje fino que el muelle
    }

    /// <summary>Fotograma a <paramref name="tMs"/> de la celebración, con la escala de DPI para la subida.</summary>
    public static Frame Sample(double tMs, double scale = 1.0)
    {
        double bgT = tMs - Motion.ChipBgDelayMs;
        double width = StartWidth + (1 - StartWidth) * SpringCurve.Progress(bgT, Motion.Abrir);
        double bg = Math.Clamp(SpringCurve.Progress(bgT, Motion.Rapido), 0, 1);
        double textT = (tMs - Motion.ChipBgDelayMs - Motion.ChipTextDelayMs) / Motion.ChipTextFadeMs;
        double text = Easing.OutCubic(Math.Clamp(textT, 0, 1));
        double exit = ExitProgress(tMs);
        int rise = (int)Math.Round(Motion.ChipRisePx * scale * exit, MidpointRounding.AwayFromZero);
        return new Frame(width, bg * (1 - exit), text * (1 - exit), rise);
    }

    /// <summary>Duración total (ms): permanencia más la salida hasta que el chip deja de verse.</summary>
    public static readonly double TotalMs = ComputeTotal();

    private static double ComputeTotal()
    {
        double t = 0;
        while (t < 2000 && 1 - SpringCurve.Progress(t, Motion.Rapido) >= Motion.Rapido.EpsX) t += 1;
        return Motion.CelebrationMs + t;
    }

    private static readonly double EntrySettleMs = ComputeEntrySettle();

    private static double ComputeEntrySettle()
    {
        // Hasta que el ancho (Abrir) y la opacidad quedan quietos a efectos de dibujo (< 0,5 px en ~200 px).
        double t = 0;
        while (t < 3000 && (Math.Abs(1 - SpringCurve.Progress(t, Motion.Abrir)) * 0.3 * 200 >= 0.5
                            || 1 - SpringCurve.Progress(t, Motion.Rapido) >= Motion.Rapido.EpsX)) t += 1;
        return Motion.ChipBgDelayMs + Math.Max(t, Motion.ChipTextDelayMs + Motion.ChipTextFadeMs);
    }

    /// <summary>¿Está el chip entrando o saliendo (pide fotogramas)? Pleno y quieto ⇒ no.</summary>
    public static bool IsAnimating(double tMs)
        => tMs >= 0 && tMs < TotalMs && (tMs < EntrySettleMs || tMs >= Motion.CelebrationMs);
}
