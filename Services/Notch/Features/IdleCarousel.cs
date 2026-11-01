using System.Globalization;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Qué enseña la pastilla en reposo.</summary>
public enum IdleFace { Quota, Clock }

/// <summary>
/// Alternancia en reposo: cuota durante <c>quotaFor</c> (7 s) y reloj durante <c>clockFor</c> (5 s), en bucle.
/// PURO: el instante llega por parámetro y no lee el reloj (regla del repo). No tiene timers: quien la usa
/// mete <see cref="NextChangeIn"/> en la cola de despertares para no gastar CPU en reposo.
///
/// <list type="bullet">
/// <item>Sin <see cref="Restart"/> previo, el primer instante consultado hace de arranque.</item>
/// <item>Si el reloj va hacia atrás (cambio de hora, reanudación rara), se reinicia en la cuota.</item>
/// </list>
/// </summary>
public sealed class IdleCarousel
{
    private readonly TimeSpan _quotaFor;
    private readonly TimeSpan _clockFor;
    private DateTime? _start;

    public IdleCarousel(TimeSpan quotaFor, TimeSpan clockFor)
    {
        if (quotaFor <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(quotaFor));
        if (clockFor <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(clockFor));
        _quotaFor = quotaFor;
        _clockFor = clockFor;
    }

    /// <summary>Vuelve a empezar por la cuota en <paramref name="now"/> (al entrar en reposo).</summary>
    public void Restart(DateTime now) => _start = now;

    public IdleFace Face(DateTime now) => Phase(now) < _quotaFor ? IdleFace.Quota : IdleFace.Clock;

    /// <summary>Tiempo hasta el siguiente cambio de cara; siempre &gt; 0.</summary>
    public TimeSpan NextChangeIn(DateTime now)
    {
        var p = Phase(now);
        return p < _quotaFor ? _quotaFor - p : _quotaFor + _clockFor - p;
    }

    // Posición dentro del ciclo [0, quota + clock).
    private TimeSpan Phase(DateTime now)
    {
        if (_start is not { } start || now < start)
        {
            _start = now;
            return TimeSpan.Zero;
        }
        long cycle = (_quotaFor + _clockFor).Ticks;
        return TimeSpan.FromTicks((now - start).Ticks % cycle);
    }
}

/// <summary>Textos del reloj del notch: hora («18:55» / «6:55 p. m.») y fecha corta («mié 28 oct»).</summary>
public static class ClockText
{
    /// <summary>
    /// <paramref name="use24h"/> lo decide el llamador (ajuste de Windows; ver <see cref="Prefers24h"/>).
    /// Se usan patrones propios (no el ShortTimePattern) para que no se cuelen segundos ni espacios raros;
    /// los nombres de día/mes y los designadores AM/PM salen de la cultura, sin puntos de abreviatura.
    /// </summary>
    public static (string Time, string Date) Format(DateTime local, CultureInfo culture, bool use24h)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var dtf = culture.DateTimeFormat;

        string time;
        string designator = (local.Hour < 12 ? dtf.AMDesignator : dtf.PMDesignator).Trim();
        if (use24h || designator.Length == 0)
            time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        else
            time = local.ToString("h:mm", CultureInfo.InvariantCulture) + " " + designator;

        string day = StripDot(dtf.GetAbbreviatedDayName(local.DayOfWeek));
        string month = StripDot(dtf.GetAbbreviatedMonthName(local.Month));
        string dayNum = local.Day.ToString(CultureInfo.InvariantCulture);
        string date = DayBeforeMonth(dtf) ? $"{day} {dayNum} {month}" : $"{day} {month} {dayNum}";
        return (time, date);
    }

    /// <summary>La cultura escribe la hora en 24 h (su patrón corto usa <c>H</c>).</summary>
    public static bool Prefers24h(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.DateTimeFormat.ShortTimePattern.Contains('H');
    }

    // «d 'de' MMMM» (es) → día primero; «MMMM d» (en-US) → mes primero.
    private static bool DayBeforeMonth(DateTimeFormatInfo dtf)
    {
        string p = dtf.MonthDayPattern;
        int d = p.IndexOf('d');
        int m = p.IndexOf('M');
        return d < 0 || m < 0 || d < m;
    }

    private static string StripDot(string s) => s.Trim().TrimEnd('.');
}
