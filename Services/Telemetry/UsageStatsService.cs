using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services.Telemetry;

/// <summary>
/// Programa el contador anónimo de uso: la primera comprobación entre 2 y 5 min tras arrancar y después cada hora mientras
/// la app esté abierta (así el cambio de día se cubre sin reiniciar). Un <see cref="System.Threading.Timer"/> y nada más:
/// cero CPU entre comprobaciones y nada en el hilo de UI (la red y la lectura de <c>config.json</c> van en el pool). Lo
/// aceptado se guarda con <c>persist</c>, que el host lleva al hilo de UI. Apagarlo (<see cref="SetEnabled"/>) cancela al
/// momento lo que esté en vuelo y para el temporizador.
/// </summary>
public sealed class UsageStatsService : IDisposable
{
    public static readonly TimeSpan Period = TimeSpan.FromHours(1);
    public static readonly TimeSpan FirstMin = TimeSpan.FromMinutes(2), FirstMax = TimeSpan.FromMinutes(5);

    private readonly IUsageStatsSender? _sender;
    private readonly Func<AppConfig?> _load;
    private readonly Func<string> _edition;
    private readonly string _systemLanguage;
    private readonly string _version;
    private readonly bool _environmentAllows;
    private readonly Action<Action<AppConfig>> _persist;
    private readonly Func<DateTime> _localNow, _utcNow;
    private readonly Func<TimeSpan> _firstDelay;
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();

    private CancellationTokenSource _cts = new();
    private bool _enabled, _started, _disposed;
    private int _running;
    // En memoria: lo aceptado que quizá aún no se ha guardado, y el día en que el servidor rechazó el aviso (400).
    private UsageStatsData? _committed;
    private string? _failedDay;

    /// <param name="sender">null = destino no válido: nunca se manda nada.</param>
    /// <param name="load">config.json actual (null si no se pudo leer: esa comprobación se salta).</param>
    /// <param name="edition">free | pro | github, en el momento del aviso.</param>
    /// <param name="systemLanguage">Idioma de Windows para «system» (<see cref="Localization.ResolveSystemCode"/>).</param>
    /// <param name="environmentAllows"><see cref="UsageStatsEnvironment.Allows"/>.</param>
    /// <param name="persist">Aplica un cambio sobre config.json (el host lo lleva al hilo de UI).</param>
    public UsageStatsService(IUsageStatsSender? sender, Func<AppConfig?> load, Func<string> edition, string systemLanguage,
        string version, bool environmentAllows, Action<Action<AppConfig>> persist, Func<DateTime>? localNow = null,
        Func<DateTime>? utcNow = null, Func<TimeSpan>? firstDelay = null)
    {
        _sender = sender;
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _edition = edition ?? throw new ArgumentNullException(nameof(edition));
        _systemLanguage = systemLanguage ?? "en";
        _version = version ?? "0.0.0";
        _environmentAllows = environmentAllows;
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _localNow = localNow ?? (() => DateTime.Now);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _firstDelay = firstDelay ?? RandomFirstDelay;
        _timer = new System.Threading.Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>¿Puede llegar a mandar algo alguna vez? (entorno y destino válidos).</summary>
    public bool Active => _environmentAllows && _sender is not null;

    /// <summary>Entre 2 y 5 min.</summary>
    public static TimeSpan RandomFirstDelay() =>
        FirstMin + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * (FirstMax - FirstMin).TotalMilliseconds);

    /// <summary>Arranca con el ajuste actual: la primera comprobación, en 2-5 min.</summary>
    public void Start(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _started = true;
            _enabled = enabled;
            if (enabled && Active) _timer.Change(_firstDelay(), Period);
        }
    }

    /// <summary>El interruptor cambió: apagado, se cancela lo que esté en vuelo y se para todo; encendido, otra vez la primera
    /// comprobación en 2-5 min.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed || !_started || enabled == _enabled) return;
            _enabled = enabled;
            if (!enabled)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                _cts.Cancel();
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }
            else if (Active) _timer.Change(_firstDelay(), Period);
        }
    }

    /// <summary>La siguiente comprobación dentro de <paramref name="delay"/> (y luego cada hora): tras la bienvenida o el globo
    /// de quien actualiza.</summary>
    public void Kick(TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled || !Active) return;
            _timer.Change(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Period);
        }
    }

    private void OnTimer()
    {
        CancellationToken ct;
        lock (_gate)
        {
            if (_disposed || !_enabled) return;
            ct = _cts.Token;
        }
        _ = RunGuardedAsync(ct);
    }

    private async Task RunGuardedAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;   // la anterior sigue en vuelo: nada de colas
        try { await RunOnceAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { AppLog.Warn($"contador de uso: {ex.Message}"); }
        finally { Volatile.Write(ref _running, 0); }
    }

    /// <summary>Una comprobación: decide, manda si toca y guarda solo si el servidor lo aceptó. null = no tocaba (o no se
    /// puede). La usan el temporizador y los tests.</summary>
    internal async Task<UsagePingResult?> RunOnceAsync(CancellationToken ct)
    {
        if (!Active) return null;
        lock (_gate) { if (_disposed || !_enabled) return null; }
        var cfg = _load();
        if (cfg is null) return null;
        var utc = _utcNow();
        var local = _localNow();
        if (!UsagePing.MaySend(cfg.UsageStatsEnabled, _environmentAllows, cfg.OnboardingPending, cfg.UsageStats, utc)) return null;

        var state = Merge(cfg.UsageStats, _committed);
        if (UsagePing.Decide(state, local, _failedDay) is not { } flags) return null;

        var payload = new UsagePingPayload(_version, _edition(), UsageStatsFacts.Language(cfg.Language, _systemLanguage), flags);
        string today = UsagePing.DayOf(local);
        if (!payload.IsValid)
        {
            _failedDay = today;
            AppLog.Warn($"contador de uso: aviso no válido, no se manda ({payload.ToJson()})");
            return UsagePingResult.FailedToday;
        }

        // Con respuesta, se guarda aunque lo hayan apagado entretanto: así nunca se cuenta dos veces el mismo día.
        int? status = await _sender!.SendAsync(payload, ct).ConfigureAwait(false);
        var result = UsagePing.Classify(status);
        switch (result)
        {
            case UsagePingResult.Sent:
                var committed = UsagePing.Commit(state, local);
                _committed = committed;
                _persist(c => c.UsageStats = Merge(c.UsageStats, committed));
                AppLog.Info($"contador de uso: aviso aceptado (m={(flags.Month ? 1 : 0)} n={(flags.NewInstall ? 1 : 0)})");
                break;
            case UsagePingResult.FailedToday:
                _failedDay = today;
                AppLog.Warn($"contador de uso: el servidor rechazó el aviso ({status}); hasta mañana no se insiste");
                break;
            default:
                AppLog.Info($"contador de uso: sin respuesta válida ({status?.ToString() ?? "sin red"}); se reintenta más tarde");
                break;
        }
        return result;
    }

    /// <summary>Lo guardado, con lo aceptado en memoria si es más reciente (el guardado va por el hilo de UI y puede tardar).</summary>
    private static UsageStatsData Merge(UsageStatsData? saved, UsageStatsData? committed)
    {
        saved ??= new UsageStatsData();
        if (committed?.LastDay is not { } day || string.CompareOrdinal(day, saved.LastDay ?? "") <= 0) return saved;
        return saved with { LastDay = committed.LastDay, LastMonth = committed.LastMonth, FirstSent = true };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            _cts.Cancel();
            _cts.Dispose();
        }
        _sender?.Dispose();
    }
}
