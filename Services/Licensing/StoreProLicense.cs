namespace ClaudeBarWin.Services.Licensing;

/// <summary>Lo que dijo la Store de la licencia: <see cref="Valid"/> = respondió con una licencia de la app activa (una
/// respuesta de verdad); <see cref="OwnsPro"/> = el complemento «Pro» está activo en ella.</summary>
public sealed record StoreLicenseInfo(bool Valid, bool OwnsPro);

/// <summary>
/// La Store vista desde <see cref="StoreProLicense"/>: separada para probar su lógica sin paquete MSIX. La de verdad es
/// <see cref="WindowsStoreBackend"/> (<c>Windows.Services.Store</c>).
/// </summary>
public interface IStoreBackend : IDisposable
{
    /// <summary>La licencia (la Store la cachea: sin red responde con la última). Lanza si no responde.</summary>
    Task<StoreLicenseInfo> GetLicenseAsync();

    /// <summary>La ventana de compra del complemento «Pro». Lanza si algo falla antes de abrirla.</summary>
    Task<PurchaseResult> PurchaseAsync();

    /// <summary>El precio del complemento con su moneda; null si no se sabe. Lanza si no responde.</summary>
    Task<string?> GetPriceAsync();

    /// <summary>Las licencias cambiaron (compra en otro equipo, reembolso, caché renovada). En cualquier hilo.</summary>
    event Action? LicensesChanged;
}

/// <summary>Lo que se guarda del Pro en <c>config.json</c>: inicio de la prueba, el último estado conocido de la compra y si
/// alguna vez la hubo (tras un reembolso no se dice «tu prueba ha terminado»).</summary>
public readonly record struct ProSnapshot(DateTime? TrialStartUtc, bool LastKnownPro, bool EverOwnedPro = false);

/// <summary>Dónde se guarda <see cref="ProSnapshot"/> (en la app, <c>config.json</c> por el camino de siempre de la bandeja).</summary>
public interface IProStateStore
{
    /// <summary>Lo guardado. LANZA si no se puede leer ahora (config bloqueada por el antivirus o la sincronización): entonces
    /// ni se empieza otra prueba ni se guarda nada encima.</summary>
    ProSnapshot Load();

    /// <summary>Solo el inicio de la prueba. En el hilo de UI.</summary>
    void SaveTrialStart(DateTime startUtc);

    /// <summary>Solo el último estado conocido de la compra (con <c>true</c>, también que la hubo). En el hilo de UI.</summary>
    void SaveLastKnownPro(bool owns);
}

/// <summary>
/// Edición de la Store (solo con identidad de paquete): complemento duradero «Pro» y prueba de 7 días.
/// <list type="bullet">
/// <item><b>Prueba:</b> empieza sola el primer arranque (la fecha va a <c>config.json</c>); un temporizador de un disparo
/// (<see cref="ProTrial.NextChangeIn"/>) baja los días y la acaba, sin sondeos. Como el temporizador no cuenta el tiempo
/// suspendido, espera como mucho <see cref="MaxTimerWait"/> y recalcula con el reloj de pared.</item>
/// <item><b>Config ilegible:</b> si <see cref="IProStateStore.Load"/> falla no se empieza prueba ni se guarda nada; se vuelve a
/// intentar cada <see cref="LoadRetry"/> (y al refrescar).</item>
/// <item><b>Último estado conocido:</b> arranca con la compra que había la última vez (también sin red). Un fallo, una
/// excepción, una consulta que tarda más de <see cref="QueryTimeout"/> o una licencia sin validez no cambian nada. Como la
/// app es gratuita, «licencia válida» casi siempre se cumple (caché sin sincronizar, otra cuenta): una sola respuesta sin el
/// complemento tampoco quita el Pro; hacen falta dos separadas al menos <see cref="DenialConfirmation"/> (la segunda la pide
/// un temporizador), o pulsar «Restaurar compra».</item>
/// <item><b>Compra:</b> al completarse, Pro al momento (en caliente). Las consultas lanzadas antes de la compra que acaben
/// después se descartan (contador <c>_epoch</c>): no pisan la compra.</item>
/// </list>
/// <see cref="Changed"/> y las escrituras de <see cref="IProStateStore"/> van al hilo de UI con <c>post</c>. El estado se
/// guarda bajo un lock: las respuestas de la Store pueden volver en otro hilo.
/// </summary>
public sealed class StoreProLicense : IProLicense
{
    /// <summary>Lo más que se espera a una consulta a la Store (licencia, precio, restaurar).</summary>
    internal static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);
    /// <summary>Separación mínima entre las dos respuestas sin el complemento que quitan el Pro.</summary>
    internal static readonly TimeSpan DenialConfirmation = TimeSpan.FromMinutes(30);
    /// <summary>Lo más que duerme el temporizador de la prueba (la suspensión no cuenta en él).</summary>
    internal static readonly TimeSpan MaxTimerWait = TimeSpan.FromHours(1);
    /// <summary>Cada cuánto se reintenta leer la config si no se pudo.</summary>
    internal static readonly TimeSpan LoadRetry = TimeSpan.FromMinutes(1);

    private readonly IStoreBackend _backend;
    private readonly IProStateStore _store;
    private readonly Action<Action> _post;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly ITimer _trialTimer;
    private readonly ITimer _recheckTimer;
    private bool _loaded;
    private DateTime? _trialStart;
    private bool _ownsPro;
    private bool _everOwned;
    private int _epoch;
    private DateTime? _firstDenialUtc;
    private ProState _state;
    private int? _days;
    private string? _price;
    private Task? _refreshing;
    private volatile bool _disposed;

    /// <param name="post">Lleva una acción al hilo de UI: ahí se disparan <see cref="Changed"/> y se guarda.</param>
    /// <param name="time">Reloj y temporizadores (los tests los fijan); null = los del sistema.</param>
    /// <param name="storeProductId">Ficha de la Store (para <see cref="StoreUrl"/>); null = <see cref="ProCatalog.StoreProductId"/>.</param>
    public StoreProLicense(IStoreBackend backend, IProStateStore store, Action<Action> post, TimeProvider? time = null,
        string? storeProductId = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _time = time ?? TimeProvider.System;
        StoreUrl = ProCatalog.StoreUrl(storeProductId);

        _trialTimer = _time.CreateTimer(_ => Post(Recompute), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _recheckTimer = _time.CreateTimer(_ => Post(() => _ = RefreshAsync()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var now = Now;
        EnsureLoaded(now);
        (_state, _days) = Compute(now);
        ArmTrialTimer(now);
        _backend.LicensesChanged += OnLicensesChanged;
    }

    public ProState State { get { lock (_gate) return _state; } }
    public int? TrialDaysLeft { get { lock (_gate) return _days; } }
    public bool TrialEnded { get { lock (_gate) return !_ownsPro && !_everOwned && ProTrial.Ended(_trialStart, Now); } }
    public bool CanPurchaseHere => true;
    public string? StoreUrl { get; }
    public string? Price { get { lock (_gate) return _price; } }
    public event Action? Changed;

    /// <summary>Inicio de la prueba (para los tests y el log).</summary>
    internal DateTime? TrialStartUtc { get { lock (_gate) return _trialStart; } }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public Task RefreshAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_refreshing is { IsCompleted: false }) return _refreshing;
            return _refreshing = RefreshCoreAsync();
        }
    }

    private async Task RefreshCoreAsync()
    {
        EnsureLoaded(Now);
        int epoch = Epoch;
        var info = await QueryAsync().ConfigureAwait(false);
        if (info is not null) Apply(info, epoch, confirmed: false);
        else Recompute();
        await RefreshPriceAsync().ConfigureAwait(false);
    }

    /// <summary>«Restaurar compra»: lo pide el usuario, así que una respuesta válida sin el complemento quita el Pro en el acto.</summary>
    public async Task<PurchaseResult> RestoreAsync()
    {
        if (_disposed) return PurchaseResult.Failed;
        EnsureLoaded(Now);
        int epoch = Epoch;
        var info = await QueryAsync().ConfigureAwait(false);
        if (info is null) return PurchaseResult.NetworkError;
        Apply(info, epoch, confirmed: true);
        return info.OwnsPro ? PurchaseResult.AlreadyOwned : info.Valid ? PurchaseResult.NotOwned : PurchaseResult.NetworkError;
    }

    public async Task<PurchaseResult> PurchaseAsync()
    {
        if (_disposed) return PurchaseResult.Failed;
        PurchaseResult result;
        try { result = await _backend.PurchaseAsync().ConfigureAwait(false); }
        catch (Exception ex)
        {
            AppLog.Warn($"pro: la compra no se pudo abrir: {ex.Message}");
            return PurchaseResult.Failed;
        }
        AppLog.Info($"pro: compra → {result}");
        if (result is PurchaseResult.Purchased or PurchaseResult.AlreadyOwned)
        {
            // Lo que se preguntó antes de la compra ya no vale: si vuelve ahora, se descarta.
            lock (_gate)
            {
                _epoch++;
                _firstDenialUtc = null;
            }
            ArmRecheck(null);
            SetOwned(true);
        }
        return result;
    }

    private int Epoch { get { lock (_gate) return _epoch; } }

    /// <summary>La licencia de la Store; null si no respondió a tiempo (se anota y se sigue con lo que había).</summary>
    private async Task<StoreLicenseInfo?> QueryAsync()
    {
        try { return await _backend.GetLicenseAsync().WaitAsync(QueryTimeout, _time).ConfigureAwait(false); }
        catch (Exception ex)
        {
            AppLog.Warn($"pro: la Store no respondió; se sigue con el último estado conocido: {ex.Message}");
            return null;
        }
    }

    private async Task RefreshPriceAsync()
    {
        string? price;
        try { price = await _backend.GetPriceAsync().WaitAsync(QueryTimeout, _time).ConfigureAwait(false); }
        catch (Exception ex)
        {
            AppLog.Warn($"pro: no se pudo leer el precio: {ex.Message}");
            return;
        }
        if (string.IsNullOrWhiteSpace(price)) return;
        bool changed;
        lock (_gate)
        {
            changed = !string.Equals(price, _price, StringComparison.Ordinal);
            _price = price;
        }
        if (changed) Raise();
    }

    /// <summary>
    /// Lo que dijo la Store a una consulta lanzada en <paramref name="epoch"/> (si hubo una compra después, se descarta).
    /// Comprado: Pro. Sin el complemento y respuesta válida: con <paramref name="confirmed"/> («Restaurar compra») se quita;
    /// si no, solo cuando una segunda respuesta igual llega al menos <see cref="DenialConfirmation"/> después de la primera
    /// (un temporizador la pide). Respuesta sin validez: nada cambia.
    /// </summary>
    private void Apply(StoreLicenseInfo info, int epoch, bool confirmed)
    {
        var now = Now;
        bool? owns = null;
        TimeSpan? recheck = null;
        bool stale;
        lock (_gate)
        {
            stale = epoch != _epoch;
            if (!stale && info.OwnsPro)
            {
                _firstDenialUtc = null;
                owns = true;
            }
            else if (!stale && info.Valid && _ownsPro)
            {
                if (confirmed || (_firstDenialUtc is { } first && now - first >= DenialConfirmation))
                {
                    _firstDenialUtc = null;
                    owns = false;
                }
                else
                {
                    _firstDenialUtc ??= now;
                    var left = _firstDenialUtc.Value + DenialConfirmation - now;
                    recheck = left < TimeSpan.Zero || left > DenialConfirmation ? DenialConfirmation : left;
                }
            }
        }
        if (stale) AppLog.Info("pro: respuesta de la Store anterior a la compra; se descarta");
        if (recheck is { } r) AppLog.Info($"pro: la Store dice que no hay Pro; se confirma dentro de {r.TotalMinutes:0} min");
        if (owns == true || recheck is not null) ArmRecheck(recheck);
        if (owns is { } o) SetOwned(o);
        else Recompute();
    }

    private void SetOwned(bool owns)
    {
        bool persist;
        lock (_gate)
        {
            persist = _loaded && _ownsPro != owns;   // sin config leída no se escribe (se hace al leerla)
            _ownsPro = owns;
            if (owns) _everOwned = true;
        }
        if (persist) Post(() => _store.SaveLastKnownPro(owns));
        Recompute();
    }

    /// <summary>
    /// Lee lo guardado si aún no se pudo. Si falla (config bloqueada), nada: ni prueba nueva ni escrituras; se reintenta. Al
    /// leerlo: sin fecha de prueba, empieza ahora; si la Store ya dijo Pro mientras tanto, se guarda.
    /// </summary>
    private void EnsureLoaded(DateTime now)
    {
        lock (_gate) if (_loaded) return;
        ProSnapshot saved;
        try { saved = _store.Load(); }
        catch (Exception ex)
        {
            AppLog.Warn($"pro: no se pudo leer config.json; ni se empieza la prueba ni se guarda nada: {ex.Message}");
            return;
        }
        bool startTrial, savePro;
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
            _trialStart = saved.TrialStartUtc ?? now;
            startTrial = saved.TrialStartUtc is null;   // primer arranque de la edición de la Store: la prueba empieza ya
            savePro = _ownsPro && !saved.LastKnownPro;
            _ownsPro |= saved.LastKnownPro;
            _everOwned |= saved.EverOwnedPro || saved.LastKnownPro;
        }
        if (startTrial) Post(() => _store.SaveTrialStart(now));
        if (savePro) Post(() => _store.SaveLastKnownPro(true));
    }

    /// <summary>Recalcula estado y días (compra, prueba que baja o acaba) y avisa si cambió algo.</summary>
    internal void Recompute()
    {
        if (_disposed) return;
        var now = Now;
        EnsureLoaded(now);
        bool changed;
        lock (_gate)
        {
            var (state, days) = Compute(now);
            changed = state != _state || days != _days;
            _state = state;
            _days = days;
        }
        ArmTrialTimer(now);
        if (changed) Raise();
    }

    /// <summary>Lo que toca con la compra y la prueba de ahora (llamar con el lock o en el constructor).</summary>
    private (ProState State, int? Days) Compute(DateTime now)
    {
        var state = ProTrial.Resolve(_ownsPro, _trialStart, now);
        return (state, state == ProState.Trial ? ProTrial.DaysLeft(_trialStart, now) : null);
    }

    /// <summary>Un disparo al próximo cambio de la prueba (un segundo después, para que el reloj ya lo vea), como mucho dentro
    /// de <see cref="MaxTimerWait"/>; sin config leída, el reintento; con el Pro comprado o la prueba acabada, ninguno.</summary>
    private void ArmTrialTimer(DateTime now)
    {
        if (_disposed) return;
        TimeSpan? next;
        lock (_gate)
            next = !_loaded ? LoadRetry
                : _ownsPro ? null
                : ProTrial.NextChangeIn(_trialStart, now) is { } d ? d + TimeSpan.FromSeconds(1) : null;
        if (next > MaxTimerWait) next = MaxTimerWait;
        try { _trialTimer.Change(next ?? Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { /* cerrándose */ }
    }

    /// <summary>La segunda consulta que confirma una respuesta sin el complemento; null la cancela.</summary>
    private void ArmRecheck(TimeSpan? due)
    {
        if (_disposed) return;
        try { _recheckTimer.Change(due is { } d ? d + TimeSpan.FromSeconds(1) : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { /* cerrándose */ }
    }

    private void OnLicensesChanged() => Post(() => _ = RefreshAsync());

    private void Raise() => Post(() => Changed?.Invoke());

    private void Post(Action action)
    {
        if (_disposed) return;
        try { _post(() => { if (!_disposed) action(); }); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { /* cerrándose */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.LicensesChanged -= OnLicensesChanged;
        _trialTimer.Dispose();
        _recheckTimer.Dispose();
        _backend.Dispose();
    }
}