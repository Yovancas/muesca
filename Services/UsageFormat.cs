using System.Globalization;

namespace ClaudeBarWin.Services;

public static class UsageFormat
{
    /// <summary>
    /// "$420.50" (en-US) / "$420,50" (es-ES): símbolo $ fijo (el coste API-equiv es siempre USD) +
    /// decimales según la cultura del idioma elegido (T2). Antes el "0.00" inline caía en la
    /// CurrentCulture del SO y la UI inglesa mostraba "$420,50".
    /// </summary>
    public static string Money(double usd, CultureInfo culture) => "$" + usd.ToString("0.00", culture);

    /// <summary>
    /// "12.5%" (en-US) / "12,5%" (es-ES): el patrón "0.#" de los % de la UI (barras, glance, tween,
    /// mini-filas de modelo, peak de la gráfica) con el separador decimal de la cultura del idioma (T2).
    /// </summary>
    public static string Percent(double pct, CultureInfo culture) => pct.ToString("0.#", culture) + "%";

    /// <summary>"2h 13m", "1d 4h", "45m 12s", or the localized "resetting…" label.</summary>
    public static string Countdown(DateTimeOffset? resetsAt, string resettingLabel)
        => CountdownAt(resetsAt, resettingLabel, DateTimeOffset.Now);

    /// <summary>
    /// Como <see cref="Countdown"/> pero contra el «ahora» <paramref name="now"/> (inyectable): el notch de demostración del
    /// tutorial cuenta los reinicios de sus datos de ejemplo con el reloj de la demo, no con el de la máquina (con un reloj
    /// fijo en el pasado salía «resetea en reseteando…»).
    /// </summary>
    public static string CountdownAt(DateTimeOffset? resetsAt, string resettingLabel, DateTimeOffset now)
    {
        if (resetsAt is null) return "";
        var span = resetsAt.Value - now;
        if (span <= TimeSpan.Zero) return resettingLabel;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{span.Minutes}m {span.Seconds}s";
    }

    /// <summary>Hora local absoluta del reset en formato "ddd HH:mm" (p.ej. "mar 18:42"), con el día
    /// abreviado en la cultura del idioma elegido (T2: antes "jue 02:12" se colaba en la UI inglesa);
    /// "" si es null.</summary>
    /// <summary>
    /// Cuenta atrás a resolución de MINUTOS para el panel (lenguaje de movimiento §4, micro-interacción 4):
    /// "2d 3h", "2h 13m", "13m" y "&lt;1m"; sin segundos, así el panel quieto no necesita un tick de 1 Hz.
    /// El «ahora» es inyectable (tests y cadencia).
    /// </summary>
    public static string CountdownMinutesAt(DateTimeOffset? resetsAt, string resettingLabel, DateTimeOffset now)
    {
        if (resetsAt is null) return "";
        var span = resetsAt.Value - now;
        if (span <= TimeSpan.Zero) return resettingLabel;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
        return "<1m";
    }

    public static string ResetAbsolute(DateTimeOffset? resetsAt, CultureInfo culture)
        => resetsAt is { } r ? r.ToLocalTime().ToString("ddd HH:mm", culture) : "";

    /// <summary>Antigüedad de un dato UTC en texto relativo localizado ("hace 5 min" / "hace 30 s").
    /// Espejo de <see cref="Countdown"/>. Normaliza Kind=Unspecified como UTC.</summary>
    public static string Relative(DateTime utc, Strings s) => RelativeAt(utc, DateTime.UtcNow, s);

    /// <summary>
    /// Como <see cref="Relative(DateTime, Strings)"/> pero con el instante "ahora" <b>inyectable</b>
    /// (<paramref name="nowUtc"/>) en vez de leer el reloj. Lo usa <see cref="FooterLayout"/> para que
    /// la guardia de re-layout y el pintado del footer compartan el MISMO texto de forma determinista
    /// (testeable sin reloj real).
    /// </summary>
    public static string RelativeAt(DateTime utc, DateTime nowUtc, Strings s)
    {
        var span = AsUtc(nowUtc) - AsUtc(utc);
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        string ago =
            span.TotalDays >= 1 ? $"{(int)span.TotalDays} d"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h"
            : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min"
            : $"{(int)span.TotalSeconds} s";
        return string.Format(s.AgoFormat, ago);
    }

    /// <summary>
    /// Como <see cref="RelativeAt"/> pero a resolución de MINUTOS ("&lt;1 min", "5 min", "2 h", "1 d"):
    /// el pie del panel deja de contar segundos y el panel quieto se despierta ~1 vez por minuto.
    /// </summary>
    public static string RelativeMinutesAt(DateTime utc, DateTime nowUtc, Strings s)
        => string.Format(s.AgoFormat, AgeMinutesAt(utc, nowUtc));

    /// <summary>Antigüedad a resolución de minutos, sin «hace»: "&lt;1 min", "5 min", "2 h", "1 d".</summary>
    public static string AgeMinutesAt(DateTime utc, DateTime nowUtc)
    {
        var span = AsUtc(nowUtc) - AsUtc(utc);
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalDays >= 1 ? $"{(int)span.TotalDays} d"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h"
            : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min"
            : "<1 min";
    }

    /// <summary>«Última lectura hace 12 min»: la cuota viene solo de la statusline y no hay sesión que la renueve.</summary>
    public static string LastReadingAt(DateTime utc, DateTime nowUtc, Strings s)
        => string.Format(s.LastReadingFmt, AgeMinutesAt(utc, nowUtc));

    /// <summary>El dato se considera "envejecido" cuando supera 3× la frecuencia de refresco.
    /// Normaliza Kind=Unspecified como UTC.</summary>
    public static bool IsStale(DateTime utc, int refreshSeconds) => IsStaleAt(utc, DateTime.UtcNow, refreshSeconds);

    /// <summary>
    /// Como <see cref="IsStale"/> pero con el instante "ahora" <b>inyectable</b> (<paramref name="nowUtc"/>):
    /// la guardia de re-layout del footer congela un "ahora" común por tick para que medir y pintar
    /// decidan lo mismo de forma determinista.
    /// </summary>
    public static bool IsStaleAt(DateTime utc, DateTime nowUtc, int refreshSeconds)
        => AsUtc(nowUtc) - AsUtc(utc) > TimeSpan.FromSeconds(3 * refreshSeconds);

    /// <summary>
    /// v0.4: como <see cref="IsStaleAt"/> para una instantánea completa. Con la cuota alimentada por una statusline
    /// fresca la API no se sondea a propósito: unos minutos sin mensajes de Claude no hacen «viejo» el dato.
    /// </summary>
    public static bool IsSnapshotStaleAt(AppSnapshot snap, DateTime nowUtc, int refreshSeconds)
        => !snap.LiveFromStatusline && IsStaleAt(snap.UsageAtUtc, nowUtc, refreshSeconds);

    /// <summary>Normaliza un DateTime a UTC: Unspecified se asume ya en UTC; Local se convierte.</summary>
    private static DateTime AsUtc(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
    };

    public static string StateMessage(UsageFetchState state, Strings s) => state switch
    {
        UsageFetchState.NoCredentials => s.StateNoCredentials,
        UsageFetchState.AuthExpired => s.StateAuthExpired,
        UsageFetchState.RateLimited => s.StateRateLimited,
        UsageFetchState.NetworkError => s.StateNetworkError,
        UsageFetchState.StatuslineOnly => s.StateStatuslineOnly,
        _ => ""
    };
}
