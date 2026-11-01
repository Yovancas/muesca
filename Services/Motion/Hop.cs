namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// <b>Salto</b> del gato (lenguaje de movimiento §5): un bote con gravedad y suelo, función PURA del
/// tiempo transcurrido y sin estado. Sustituye al viejo bote OutBack: el muelle Atención pasaría por
/// debajo de la línea base y, recortado, solo daría un salto visible.
///
/// <para>Cada bote sube con velocidad v y cae al suelo: altura v²/2g, duración 2v/g. En cada toque la
/// velocidad se multiplica por la restitución <see cref="Motion.HopRestitution"/> y el salto se para
/// cuando el siguiente pico bajaría de <see cref="Motion.HopMinPeakPx"/>. El desplazamiento es siempre
/// ≥ 0 y en px enteros. Las alturas se dan a escala 1,0 y se multiplican por <c>scale</c> (DPI); los
/// tiempos no escalan.</para>
/// </summary>
public static class Hop
{
    /// <summary>Un bote: inicio y duración (ms), velocidad de despegue (px/s) y altura del pico (px).</summary>
    public readonly record struct Bounce(double StartMs, double DurationMs, double V0, double PeakPx);

    /// <summary>Botes de un salto con velocidad inicial <paramref name="v0"/> (px/s a escala 1,0).</summary>
    public static IReadOnlyList<Bounce> Bounces(double v0)
    {
        var list = new List<Bounce>();
        if (!(v0 > 0)) return list;
        double g = Motion.HopGravity, v = v0, t = 0;
        while (true)
        {
            double dur = 2 * v / g * 1000.0;
            list.Add(new Bounce(t, dur, v, v * v / (2 * g)));
            t += dur;
            double next = v * Motion.HopRestitution;
            if (next * next / (2 * g) < Motion.HopMinPeakPx) break;
            v = next;
        }
        return list;
    }

    /// <summary>Duración total (ms) del salto: hasta el último toque de suelo.</summary>
    public static double DurationMs(double v0)
    {
        var b = Bounces(v0);
        return b.Count == 0 ? 0 : b[^1].StartMs + b[^1].DurationMs;
    }

    /// <summary>Instantes (ms) de cada toque de suelo.</summary>
    public static IReadOnlyList<double> TouchdownsMs(double v0)
        => Bounces(v0).Select(b => b.StartMs + b.DurationMs).ToList();

    /// <summary>Altura (px fraccionarios, ≥ 0, a escala 1,0) a <paramref name="tMs"/> del despegue.</summary>
    public static double HeightAt(double tMs, double v0)
    {
        if (!(tMs > 0)) return 0;
        foreach (var b in Bounces(v0))
        {
            double local = tMs - b.StartMs;
            if (local < b.DurationMs)
            {
                double s = local / 1000.0;
                return Math.Max(0, b.V0 * s - Motion.HopGravity * s * s / 2);
            }
        }
        return 0;
    }

    /// <summary>Desplazamiento hacia arriba en px enteros (≥ 0) con la escala de DPI <paramref name="scale"/>.</summary>
    public static int OffsetPx(double tMs, double v0, double scale = 1.0)
        => (int)Math.Round(HeightAt(tMs, v0) * scale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// ¿Está el gato recién aterrizado? Cara alternativa durante <see cref="Motion.HopLandingFaceMs"/>
    /// tras cada toque de suelo (micro-interacción 1; coste nulo porque ya está animando).
    /// </summary>
    public static bool IsLanding(double tMs, double v0)
    {
        foreach (double td in TouchdownsMs(v0))
            if (tMs >= td && tMs < td + Motion.HopLandingFaceMs) return true;
        return false;
    }

    /// <summary>Tiempo (ms) durante el que el salto pide fotogramas: botes más la última cara de aterrizaje.</summary>
    public static double ActiveMs(double v0) => DurationMs(v0) + Motion.HopLandingFaceMs;

    /// <summary>
    /// Inicio (ms desde que empezó la atención) del último salto programado que ya llegó, según
    /// <see cref="Motion.AttentionHopScheduleMs"/>; null antes del primero.
    /// </summary>
    public static double? LatestScheduledMs(double sinceAttentionMs)
    {
        double? last = null;
        foreach (double at in Motion.AttentionHopScheduleMs)
            if (at <= sinceAttentionMs) last = at;
        return last;
    }

    /// <summary>Siguiente salto programado (ms desde que empezó la atención); null si ya no quedan.</summary>
    public static double? NextScheduledMs(double sinceAttentionMs)
    {
        foreach (double at in Motion.AttentionHopScheduleMs)
            if (at > sinceAttentionMs) return at;
        return null;
    }
}
