namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Entrada escalonada de las secciones al abrir el panel (lenguaje de movimiento §4 #3): PURA y sin
/// estado. Cada sección (cabecera = 0, datos = 1..n, pie = n+1) arranca <see cref="Motion.StaggerMs"/>
/// después de la anterior, con <see cref="Motion.StaggerMaxSteps"/> pasos como máximo (las demás entran
/// con la última). Una sola traslación desde la bandeja, y(t) = 6(1 + ωt)e^(−ωt) con ω = √300 (curva
/// crítica cerrada), en px enteros; y un fundido por sección con Rápido que el panel aplica cubriendo
/// la sección con el color de su superficie y alfa (1 − a). El <c>y</c> de layout NUNCA cambia.
/// </summary>
public static class Stagger
{
    /// <summary>Inicio (ms desde la apertura) de la sección <paramref name="index"/>.</summary>
    public static double StartMs(int index)
        => Math.Clamp(index, 0, Motion.StaggerMaxSteps - 1) * Motion.StaggerMs;

    /// <summary>
    /// Traslación vertical (px enteros) de la sección: de <see cref="Motion.StaggerMaxOffsetPx"/> × escala
    /// a 0. <paramref name="direction"/> = +1 con la bandeja abajo (suben), −1 con la bandeja arriba.
    /// Antes de su inicio se queda en el desplazamiento máximo (aún tapada por su fundido).
    /// </summary>
    public static int OffsetPx(double tSinceOpenMs, int index, double scale = 1.0, int direction = 1)
    {
        double local = Math.Max(0, tSinceOpenMs - StartMs(index)) / 1000.0;
        if (double.IsInfinity(local)) return 0;
        double w = Motion.StaggerOmega;
        double y = Motion.StaggerMaxOffsetPx * scale * (1 + w * local) * Math.Exp(-w * local);
        return direction * (int)Math.Round(y, MidpointRounding.AwayFromZero);
    }

    /// <summary>Opacidad (0..1) de la sección: Rápido desde su inicio; 0 antes de él.</summary>
    public static double FadeAlpha(double tSinceOpenMs, int index)
    {
        double local = tSinceOpenMs - StartMs(index);
        if (double.IsPositiveInfinity(local)) return 1.0;
        double a = Math.Clamp(SpringCurve.Progress(local, Motion.Rapido), 0.0, 1.0);
        return 1.0 - a < Motion.Rapido.EpsX ? 1.0 : a;   // mismo encaje fino que el muelle
    }

    private static double _settleScale = double.NaN, _settleMs;

    /// <summary>ms desde su inicio hasta que una sección está quieta y opaca (se memoriza por escala).</summary>
    public static double SettleMs(double scale = 1.0)
    {
        if (scale == _settleScale) return _settleMs;
        double t = 0;
        while (t < 2000 && (OffsetPx(t, 0, scale) != 0 || FadeAlpha(t, 0) < 1.0)) t += 1;
        (_settleScale, _settleMs) = (scale, t);
        return t;
    }

    /// <summary>¿Sigue en vuelo la entrada de alguna sección hasta <paramref name="lastIndex"/>?</summary>
    public static bool IsActive(double tSinceOpenMs, int lastIndex, double scale = 1.0)
        => tSinceOpenMs < StartMs(lastIndex) + SettleMs(scale);
}
