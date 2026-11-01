using System.Globalization;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Una tarjeta de cuota de la página Uso (5 h o 7 d), con los textos YA formateados: nombre, %, cuenta atrás del reinicio y
/// línea del ritmo («↗ ritmo 118 % · se agota 17:40»). <see cref="Value"/>: el % para el anillo (null sin dato);
/// <see cref="Pace"/>: estado del ritmo (su color); <see cref="Focused"/>: la ventana destacada.
/// </summary>
public sealed record UsageTile(string Title, string Pct, double? Value, string? Reset, string? PaceText, PaceStatus? Pace,
    bool Focused);

/// <summary>Una fila bajo las tarjetas: cuota por modelo («Opus 7d · 31 %», con su barrita) o el uso extra (sin barra).</summary>
public sealed record UsageRow(string Label, string? Pct, double? Value, bool Accent = false);

/// <summary>
/// Los números de la página Uso (gratis siempre), ya formateados: las dos tarjetas, las filas por modelo y de uso extra, el
/// plan (null si no se sabe) y la salud del servicio (null sin el ajuste o sin respuesta). Se rehacen al llegar datos, al
/// cambiar de idioma o de minuto con la página a la vista; nunca por frame.
/// </summary>
public sealed record UsageNumbers(IReadOnlyList<UsageTile> Tiles, IReadOnlyList<UsageRow> Rows, string? Plan,
    string? Health, HealthLevel HealthLevel);

/// <summary>Los textos de la página Uso. PURO: datos, idioma e instante por parámetro.</summary>
public static class UsageText
{
    /// <summary>
    /// Números de la página con lo último de la bandeja (<paramref name="snap"/>: cuota, ritmo, salud) y el plan. Sin cuota
    /// todavía, «—» y, en la primera tarjeta, por qué (sin sesión, sin conexión…).
    /// </summary>
    public static UsageNumbers Build(AppSnapshot? snap, PlanInfo? plan, DateTime nowUtc, QuotaFocus focus, Strings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var ci = s.Culture;
        var now = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        var u = snap?.Usage;
        var tiles = new[]
        {
            Tile($"{s.SessionWord} · 5h", u?.FiveHour, snap?.PaceFive, focus == QuotaFocus.Five, now, s),
            Tile($"{s.WeekWord} · 7d", u?.SevenDay, snap?.PaceSeven, focus == QuotaFocus.Seven, now, s),
        };
        if (u is null && snap is not null) tiles[0] = tiles[0] with { Reset = UsageFormat.StateMessage(snap.LatestState, s) };

        var rows = new List<UsageRow>(UsageLayout.Rows);
        if (u?.SevenDayOpus is { } opus) rows.Add(new UsageRow("Opus 7d", Pct(opus.UtilizationPct, ci), opus.UtilizationPct));
        if (u?.SevenDaySonnet is { } sonnet) rows.Add(new UsageRow("Sonnet 7d", Pct(sonnet.UtilizationPct, ci), sonnet.UtilizationPct));
        if (u?.ExtraUsageEnabled == true) rows.Add(new UsageRow(s.NotchUsageExtraOn, null, null, Accent: true));

        string? planText = plan is { SubscriptionType.Length: > 0 } p ? p.Display : null;
        var (health, level) = snap?.Health is { } h ? h.Level switch
        {
            HealthLevel.Operational => (s.HealthOk, HealthLevel.Operational),
            HealthLevel.Degraded => (s.HealthDegraded, HealthLevel.Degraded),
            HealthLevel.Outage => (s.HealthOutage, HealthLevel.Outage),
            _ => ((string?)null, HealthLevel.Unknown),
        } : ((string?)null, HealthLevel.Unknown);
        return new UsageNumbers(tiles, rows, planText, health, level);
    }

    private static UsageTile Tile(string title, UsageWindow? w, PaceResult? pace, bool focused, DateTimeOffset now, Strings s)
    {
        if (w is null) return new UsageTile(title, "—", null, null, null, null, focused);
        string reset = UsageFormat.CountdownMinutesAt(w.ResetsAt, s.Resetting, now);
        return new UsageTile(title, Pct(w.UtilizationPct, s.Culture), w.UtilizationPct,
            reset.Length > 0 ? $"{s.ResetsIn} {reset}" : null, pace is null ? null : PaceLine(pace, now, s), pace?.Status, focused);
    }

    /// <summary>
    /// «→ ritmo 82 %» o, si se agota antes del reinicio, «↗ 118 % · se agota 17:40» (con el día si no es hoy): la hora es lo que
    /// importa y tiene que caber entera en la tarjeta, así que entonces la flecha y el % ya dicen «ritmo».
    /// </summary>
    public static string PaceLine(PaceResult p, DateTimeOffset now, Strings s)
    {
        ArgumentNullException.ThrowIfNull(p);
        var ci = s.Culture;
        string arrow = p.Status == PaceStatus.Ok ? "→" : "↗";
        string pct = (p.PaceRatio * 100).ToString("0", ci) + "%";
        if (!p.ExhaustsBeforeReset || p.EtaUtc is not { } eta) return $"{arrow} {string.Format(ci, s.NotchUsagePaceFmt, pct)}";
        return $"{arrow} {pct} · {s.PaceEtaLabel} {EtaClock(eta, now, ci)}";
    }

    /// <summary>¿Cae <paramref name="eta"/> hoy (en la fecha local de <paramref name="now"/>)?</summary>
    public static bool IsToday(DateTimeOffset eta, DateTimeOffset now) => eta.ToLocalTime().Date == now.ToLocalTime().Date;

    /// <summary>Hora local de <paramref name="eta"/>: «17:40» si es hoy; si no, con el día («sáb 10:00»), que la hora sola se
    /// leería como de hoy (y quizá ya pasada).</summary>
    public static string EtaClock(DateTimeOffset eta, DateTimeOffset now, CultureInfo ci) =>
        eta.ToLocalTime().ToString(IsToday(eta, now) ? "HH:mm" : "ddd HH:mm", ci);

    /// <summary>% redondeado al entero con la cultura del idioma («42%»).</summary>
    public static string Pct(double v, CultureInfo ci) => UsageFormat.Percent(Math.Round(v), ci);
}

/// <summary>Lo que la bandeja da a la página Uso en cada refresco: la última foto (cuota, ritmo, gasto, salud) y el plan.</summary>
public sealed record UsageInput(AppSnapshot? Snapshot, PlanInfo? Plan);

/// <summary>Los datos de la gráfica de un rango: gasto por casilla (del parser de <c>.jsonl</c>) y muestras del % (SQLite).</summary>
public sealed record UsageSeriesData(IReadOnlyList<HistoryBucket> Spend, IReadOnlyList<PctPoint> Percent, DateTime AtUtc);

/// <summary>
/// Lee los datos de la gráfica de un rango (lo llama la bandeja; el trabajo, en segundo plano).
/// <list type="bullet">
/// <item><b>% real:</b> las muestras de SQLite SIN promediar (un promedio por número de muestras convertiría la caída de un
/// reinicio en rampa y pondría puntos dentro de los huecos de cuando la app estaba cerrada) y aligeradas con
/// <see cref="UsageBuckets.EdgesPerBucket"/> en las MISMAS casillas que pintará la gráfica.</item>
/// <item><b>Hora:</b> <see cref="UsageSeriesData.AtUtc"/> es el «ahora» con el que se leyó el %, no uno tomado antes: si no,
/// las muestras recién escritas quedarían después de «ahora» y la raya de ahora iría por detrás.</item>
/// </list>
/// </summary>
public static class UsageSeriesLoader
{
    /// <summary>Junta el gasto (<paramref name="spend"/>, ya en marcha) con el % de <paramref name="history"/> leído en el hilo
    /// de fondo a la hora de <paramref name="utcNow"/>.</summary>
    public static async Task<UsageSeriesData> LoadAsync(ChartRange range, Task<List<HistoryBucket>> spend,
        UsageHistoryStore history, Func<DateTime> utcNow)
    {
        ArgumentNullException.ThrowIfNull(spend);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(utcNow);
        var pct = Task.Run(() => ReadPercent(range, history, utcNow()));
        await Task.WhenAll(spend, pct).ConfigureAwait(false);
        var (points, at) = await pct.ConfigureAwait(false);
        return new UsageSeriesData(await spend.ConfigureAwait(false), points, at);
    }

    /// <summary>Muestras del % de <paramref name="range"/> hasta <paramref name="nowUtc"/>, listas para la gráfica.</summary>
    public static (List<PctPoint> Points, DateTime AtUtc) ReadPercent(ChartRange range, UsageHistoryStore history, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(history);
        var from = nowUtc - UsageRanges.Span(range);
        var raw = history.QueryPercent(from, nowUtc, maxPoints: 0);
        return (UsageBuckets.EdgesPerBucket(raw, from, nowUtc, UsageRanges.PercentBuckets(range)), nowUtc);
    }
}

/// <summary>
/// Cuándo pedir los datos de la gráfica a la bandeja (que lee el parser y SQLite en segundo plano): por rango, solo si lo
/// guardado tiene más de <see cref="Fresh"/> y no hay ya una petición en vuelo (que caduca a los <see cref="InFlight"/>, por
/// si la respuesta se perdió). Lo guardado se sigue enseñando mientras llega lo nuevo. PURO: el instante, por parámetro.
/// </summary>
public sealed class UsageFeed
{
    /// <summary>Lo que vale un dato antes de volver a pedirlo (el refresco por defecto de la bandeja).</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Tras esto, una petición sin respuesta se da por perdida y se puede repetir. Holgado: la primera carga de 30D con el
    /// índice frío puede tardar más de 30 s en parsear, y repetirla antes solo encola trabajo doble tras el lock del parser.
    /// </summary>
    public static readonly TimeSpan InFlight = TimeSpan.FromMinutes(2);

    private readonly Dictionary<ChartRange, UsageSeriesData> _data = new();
    private readonly Dictionary<ChartRange, DateTime> _asked = new();
    private readonly HashSet<ChartRange> _failed = new();

    /// <summary>Lo guardado de <paramref name="range"/> (null si nunca llegó).</summary>
    public UsageSeriesData? Get(ChartRange range) => _data.GetValueOrDefault(range);

    /// <summary>¿Falló la última petición de <paramref name="range"/> (y no se ha vuelto a pedir)?</summary>
    public bool HasFailed(ChartRange range) => _failed.Contains(range);

    /// <summary>¿Hay que pedir <paramref name="range"/>? (viejo o sin datos, y sin petición viva).</summary>
    public bool ShouldRequest(ChartRange range, DateTime nowUtc)
    {
        if (_asked.TryGetValue(range, out var asked) && nowUtc - asked < InFlight && nowUtc >= asked) return false;
        return _data.GetValueOrDefault(range) is not { } d || nowUtc - d.AtUtc >= Fresh || nowUtc < d.AtUtc;
    }

    /// <summary>Apunta que se pidió <paramref name="range"/> ahora.</summary>
    public void MarkRequested(ChartRange range, DateTime nowUtc)
    {
        _asked[range] = nowUtc;
        _failed.Remove(range);
    }

    /// <summary>Llegaron los datos de <paramref name="range"/>.</summary>
    public void Store(ChartRange range, UsageSeriesData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data[range] = data;
        _asked.Remove(range);
        _failed.Remove(range);
    }

    /// <summary>La petición falló: se puede volver a pedir ya (lo guardado se queda).</summary>
    public void Failed(ChartRange range)
    {
        _asked.Remove(range);
        _failed.Add(range);
    }
}

/// <summary>Estado de la gráfica que se enseña respecto al rango elegido.</summary>
public enum UsageChartStatus
{
    /// <summary>Es la del rango elegido.</summary>
    Ready,
    /// <summary>Sus datos aún no han llegado: se ve «Cargando…» (sobre la del rango anterior, atenuada, si la había).</summary>
    Loading,
    /// <summary>No se pudieron leer: se dice y se reintenta al minuto.</summary>
    Failed,
}

/// <summary>De qué rango es la gráfica que se ve. PURO.</summary>
public static class UsageChartState
{
    /// <summary>
    /// <paramref name="shown"/>: la gráfica que se enseña (la del rango anterior se queda mientras llegan los datos, para no
    /// destellar); <paramref name="wanted"/>: el rango elegido; <paramref name="failed"/>: falló su última petición. Si no
    /// es la del rango elegido (o aún se está cargando), <see cref="UsageChartStatus.Loading"/> o
    /// <see cref="UsageChartStatus.Failed"/>: la barra marcaría un rango y el eje y el hover serían de otro.
    /// </summary>
    public static UsageChartStatus Of(UsageChartModel? shown, ChartRange wanted, bool failed) =>
        shown is not null && !shown.Loading && shown.Range == wanted ? UsageChartStatus.Ready
            : failed ? UsageChartStatus.Failed : UsageChartStatus.Loading;
}

/// <summary>
/// Lo que hacen la rueda y la leyenda con las series de la gráfica. PURO.
/// <list type="bullet">
/// <item><b>Leyenda:</b> clic en una serie la oculta o la vuelve a enseñar; nunca se ocultan todas (la última vuelve a
/// mostrarlas todas).</item>
/// <item><b>Rueda</b> sobre la gráfica: recorre «todas → solo la 1.ª → solo la 2.ª → … → todas» (hacia abajo; hacia arriba
/// al revés).</item>
/// </list>
/// </summary>
public static class UsageSeriesToggle
{
    /// <summary>Ocultas tras el clic en <paramref name="key"/> de la leyenda (<paramref name="keys"/>: todas las series).</summary>
    public static HashSet<string> Toggle(IReadOnlyList<string> keys, IReadOnlySet<string> hidden, string key)
    {
        var next = new HashSet<string>(hidden.Where(keys.Contains), StringComparer.Ordinal);
        if (!next.Remove(key)) next.Add(key);
        if (keys.Count > 0 && keys.All(next.Contains)) next.Clear();
        return next;
    }

    /// <summary>Ocultas tras un paso de rueda (<paramref name="step"/> &gt; 0 = hacia abajo, la siguiente).</summary>
    public static HashSet<string> Cycle(IReadOnlyList<string> keys, IReadOnlySet<string> hidden, int step)
    {
        if (keys.Count < 2 || step == 0) return new HashSet<string>(StringComparer.Ordinal);
        // Estado actual: −1 = todas; k = solo la k (exactamente esa a la vista); cualquier otra mezcla cuenta como «todas».
        var shown = keys.Where(k => !hidden.Contains(k)).ToList();
        int state = shown.Count == 1 ? keys.ToList().IndexOf(shown[0]) : -1;
        int n = keys.Count + 1;   // «todas» + una por serie
        int next = ((state + 1 + Math.Sign(step)) % n + n) % n - 1;
        return next < 0 ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(keys.Where((_, i) => i != next), StringComparer.Ordinal);
    }

    /// <summary>La serie que se ve sola, o null si se ven varias.</summary>
    public static string? Solo(IReadOnlyList<string> keys, IReadOnlySet<string> hidden)
    {
        var shown = keys.Where(k => !hidden.Contains(k)).ToList();
        return shown.Count == 1 && keys.Count > 1 ? shown[0] : null;
    }
}
