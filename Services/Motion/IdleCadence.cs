namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Cadencia del panel quieto (lenguaje de movimiento §4, «Cadencia del panel»): sin nada animando, el
/// panel visible no late a 1 Hz sino que se despierta justo cuando va a cambiar algún texto que depende
/// del reloj (cuenta atrás y «hace N min» a resolución de minutos, umbral de dato envejecido), con un
/// despertar como mínimo por minuto. PURO: recibe el instante «ahora».
/// </summary>
public static class IdleCadence
{
    /// <summary>Margen (ms) tras la frontera para despertar con el texto ya cambiado.</summary>
    public const double EpsilonMs = 20;

    /// <summary>Despertar mínimo (ms): nunca se programa un timer más corto.</summary>
    public const int MinIdleMs = 50;

    /// <summary>Despertar máximo (ms): al menos uno por minuto aunque nada vaya a cambiar.</summary>
    public const int MaxIdleMs = 60_000;

    /// <summary>
    /// ms hasta que cambie el texto de <see cref="UsageFormat.CountdownMinutesAt"/> (por horas con días,
    /// por minutos por debajo); null si no hay reset futuro.
    /// </summary>
    public static double? CountdownChangeMs(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null) return null;
        double r = (resetsAt.Value - now).TotalMilliseconds;
        if (r <= 0) return null;
        double unit = r >= TimeSpan.FromDays(1).TotalMilliseconds ? 3_600_000 : 60_000;
        return r % unit + EpsilonMs;
    }

    /// <summary>ms hasta que cambie el texto de <see cref="UsageFormat.RelativeMinutesAt"/>.</summary>
    public static double RelativeChangeMs(DateTime utc, DateTime nowUtc)
    {
        double span = Math.Max(0, (nowUtc - utc).TotalMilliseconds);
        double unit = span >= TimeSpan.FromDays(1).TotalMilliseconds ? 86_400_000
            : span >= TimeSpan.FromHours(1).TotalMilliseconds ? 3_600_000 : 60_000;
        return unit - span % unit + EpsilonMs;
    }

    /// <summary>ms hasta un instante concreto (p. ej. el umbral de dato envejecido); null si ya pasó.</summary>
    public static double? InstantChangeMs(DateTime atUtc, DateTime nowUtc)
    {
        double ms = (atUtc - nowUtc).TotalMilliseconds;
        return ms > 0 ? ms + EpsilonMs : null;
    }

    /// <summary>El más próximo de los plazos, acotado a [<see cref="MinIdleMs"/>, <see cref="MaxIdleMs"/>].</summary>
    public static int Earliest(params double?[] deadlinesMs)
    {
        double best = MaxIdleMs;
        foreach (var d in deadlinesMs)
            if (d is { } v && v < best) best = v;
        return (int)Math.Clamp(Math.Ceiling(best), MinIdleMs, MaxIdleMs);
    }
}
