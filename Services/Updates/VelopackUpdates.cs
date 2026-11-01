namespace ClaudeBarWin.Services.Updates;

/// <summary>
/// Actualizaciones con Velopack (desde la 0.5; antes, NetSparkle + el instalador Inno entero):
/// <list type="bullet">
/// <item><see cref="StartLoop"/> comprueba al arrancar y cada 6 h, en silencio; si hay versión nueva, la descarga en segundo
/// plano (parches delta si puede).</item>
/// <item>La firma Ed25519 del manifiesto la verifica <see cref="SignedGithubSource"/>; el SHA-256 de cada paquete, Velopack.
/// Sin las dos cosas no se aplica nada.</item>
/// <item>Descargada, se aplica al pulsar en el menú (<see cref="ApplyAndRestartAsync"/>) o, si no, al cerrar la app
/// (<see cref="Dispose"/>: WaitExitThenApplyUpdates en silencio), y el siguiente arranque ya es la versión nueva. Si la app
/// no llegó a cerrarse bien (un apagado), la aplica VelopackApp al arrancar (<see cref="VelopackStartup"/>).</item>
/// </list>
/// </summary>
public sealed class VelopackUpdates : IAppUpdates
{
    public static readonly TimeSpan LoopInterval = TimeSpan.FromHours(6);

    private readonly IUpdateEngine _engine;
    private readonly Action<Action> _post;
    private readonly Action _exitApp;
    private readonly object _gate = new();
    private Task? _checking;
    private Task? _downloading;
    private System.Threading.Timer? _timer;
    private volatile bool _disposed;
    private bool _applyScheduled, _notInstalledLogged;
    private volatile string? _readyVersion;

    private volatile string? _available;
    private volatile UpdateStage _stage;
    private double _progress;

    /// <summary>La de la app: releases de <see cref="SignedGithubSource.RepoUrl"/> (o del repositorio de pruebas de
    /// <see cref="SignedGithubSource.RepoOverrideVariable"/>) con la clave de <see cref="ManifestSignature"/>.</summary>
    public static VelopackUpdates CreateDefault(Action<Action> postToUi, Action exitApp)
    {
        var repo = SignedGithubSource.ResolveRepoUrl(Environment.GetEnvironmentVariable);
        if (repo != SignedGithubSource.RepoUrl) AppLog.Warn($"actualizaciones: se buscan en {repo} ({SignedGithubSource.RepoOverrideVariable})");
        return new(new VelopackEngine(new SignedGithubSource(repo, ManifestSignature.PublicKey)), postToUi, exitApp);
    }

    /// <param name="postToUi">Lleva una acción al hilo de UI: ahí se dispara <see cref="Changed"/>.</param>
    /// <param name="exitApp">Cierra la app con normalidad (se llama ya en el hilo de UI) para que Update.exe pueda aplicar.</param>
    internal VelopackUpdates(IUpdateEngine engine, Action<Action> postToUi, Action exitApp)
    {
        _engine = engine;
        _post = postToUi;
        _exitApp = exitApp;
        // Descargada en otra ejecución y sin aplicar (VelopackApp no la aplicó al arrancar porque había otra instancia
        // abierta, o porque venimos justo de un reinicio de Velopack): lista para aplicar desde el primer momento.
        try
        {
            if (engine.IsInstalled && engine.PendingVersion is { } pending)
            {
                _readyVersion = pending;
                _available = pending;
                _progress = 1;
                _stage = UpdateStage.ReadyToApply;
            }
        }
        catch (Exception ex) { AppLog.Warn($"actualizaciones: no se pudo mirar si había una versión ya descargada: {ex.Message}"); }
    }

    public string? AvailableVersion => _available;
    public UpdateStage Stage => _stage;
    public double DownloadProgress => Volatile.Read(ref _progress);
    public event Action? Changed;

    /// <summary>Para los tests: termina cuando no queda ni comprobación ni descarga en marcha.</summary>
    internal Task Settled
    {
        get { lock (_gate) return Task.WhenAll(_checking ?? Task.CompletedTask, _downloading ?? Task.CompletedTask); }
    }

    public void StartLoop()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null) return;
            _timer = new System.Threading.Timer(_ => OnTick(), null, TimeSpan.Zero, LoopInterval);
        }
    }

    private void OnTick() => _ = SilentCheckAsync();

    /// <summary>Una vuelta del bucle. Con una versión ya lista no hace falta preguntar más: se aplica al reiniciar o al cerrar.</summary>
    internal Task SilentCheckAsync() =>
        _stage == UpdateStage.ReadyToApply ? Task.CompletedTask : StartCheck(interactive: false);

    public Task CheckNowAsync()
    {
        // Una copia sin Velopack se dice ya, sin esperar a una vuelta silenciosa que pueda estar en marcha (que no cambia
        // de etapa y acabaría en «al día»).
        if (!_engine.IsInstalled)
        {
            LogNotInstalledOnce();
            SetStage(UpdateStage.Failed);
            return Task.CompletedTask;
        }
        return StartCheck(interactive: true);
    }

    public async Task ApplyAndRestartAsync()
    {
        if (_stage != UpdateStage.ReadyToApply)
        {
            await StartCheck(interactive: true).ConfigureAwait(false);
            Task? download;
            lock (_gate) download = _downloading;
            if (download is not null) await download.ConfigureAwait(false);
        }
        lock (_gate)
        {
            if (_disposed || _applyScheduled || _stage != UpdateStage.ReadyToApply) return;
            _applyScheduled = true;
        }
        try
        {
            _engine.ApplyAfterExit(restart: true);
        }
        catch (Exception ex)
        {
            lock (_gate) _applyScheduled = false;
            AppLog.Error("actualizaciones: no se pudo lanzar Update.exe para aplicar la versión nueva", ex);
            SetStage(UpdateStage.Failed);
            return;
        }
        AppLog.Info($"actualizaciones: se cierra Muesca para aplicar la v{_available} y volver a abrirla");
        _post(_exitApp);
    }

    /// <summary>Una comprobación a la vez: si ya hay una en marcha se espera a esa, y con una descarga en camino no se
    /// pregunta otra vez. Con la aplicación ya pedida, tampoco: una descarga nueva borraría el paquete que espera
    /// Update.exe (Velopack limpia los demás al descargar).</summary>
    private Task StartCheck(bool interactive)
    {
        lock (_gate)
        {
            if (_disposed || _applyScheduled) return Task.CompletedTask;
            if (_checking is { IsCompleted: false }) return _checking;
            if (_downloading is { IsCompleted: false }) return Task.CompletedTask;
            return _checking = Task.Run(() => CheckAsync(interactive));
        }
    }

    private async Task CheckAsync(bool interactive)
    {
        try
        {
            if (!_engine.IsInstalled)
            {
                LogNotInstalledOnce();
                // Desde el menú se dice que no se pudo; en silencio no hay nada que enseñar.
                if (interactive) SetStage(UpdateStage.Failed);
                return;
            }

            SetStage(UpdateStage.Checking);
            var found = await _engine.CheckAsync().ConfigureAwait(false);
            if (found is null || (_readyVersion is not null && !IsNewer(found, _readyVersion)))
            {
                // Al día, o lo encontrado no es más nuevo que lo ya descargado (p. ej. se borró la release de lo descargado):
                // lo descargado sigue en disco y se aplica igual.
                _available = _readyVersion;
                SetStage(_readyVersion is null ? UpdateStage.Idle : UpdateStage.ReadyToApply);
                return;
            }

            // Una versión más nueva. Al descargarla, Velopack borra cualquier otro paquete (también si falla): lo que
            // hubiera listo deja de estarlo. Por eso no se empieza si mientras se comprobaba se pidió aplicar (o se cerró
            // la app): Update.exe ya espera el paquete de lo descargado, y la nueva queda para la próxima vez.
            bool download, closing;
            string? ready;
            lock (_gate)
            {
                closing = _disposed;
                ready = _readyVersion;
                download = !closing && !_applyScheduled;
                if (download)
                {
                    _readyVersion = null;
                    _available = found;
                    Volatile.Write(ref _progress, 0);
                    _stage = UpdateStage.Downloading;
                }
            }
            if (!download)
            {
                AppLog.Info(SkippedDownloadMessage(found, closing, ready));
                SetStage(ready is null ? UpdateStage.Idle : UpdateStage.ReadyToApply);
                return;
            }
            AppLog.Info($"actualizaciones: hay una versión nueva (v{found}); se descarga en segundo plano");
            Raise();
            // Con la etapa en Downloading ya no se puede pedir aplicar (hace falta ReadyToApply): solo queda mirar el cierre.
            lock (_gate)
            {
                if (!_disposed) _downloading = Task.Run(() => DownloadAsync(found));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"actualizaciones: no se pudo comprobar si hay versión nueva: {ex.Message}");
            // Sin poder comprobar, lo ya descargado (y verificado) sigue valiendo.
            SetStage(_readyVersion is null ? UpdateStage.Failed : UpdateStage.ReadyToApply);
        }
    }

    private void LogNotInstalledOnce()
    {
        if (_notInstalledLogged) return;
        _notInstalledLogged = true;
        AppLog.Info("actualizaciones: esta copia no la instaló Velopack (build de desarrollo o instalación antigua): no se " +
                    "actualiza sola");
    }

    /// <summary>
    /// Por qué no se descarga la versión nueva <paramref name="found"/>: o se está cerrando la app (<paramref name="closing"/>;
    /// si había algo descargado, <paramref name="ready"/>, se aplica al salir), o ya se pidió aplicar <paramref name="ready"/>
    /// desde el menú. <paramref name="ready"/> se lee dentro del lock, con la misma foto que decidió no descargar.
    /// </summary>
    internal static string SkippedDownloadMessage(string found, bool closing, string? ready)
    {
        var why = closing
            ? ready is null ? "Muesca se está cerrando" : $"Muesca se está cerrando y aplicará al salir la v{ready}"
            : ready is null ? "ya se pidió aplicar la versión descargada" : $"ya se pidió aplicar la v{ready}";
        return $"actualizaciones: hay una versión nueva (v{found}), pero {why}: no se descarga";
    }

    /// <summary>¿Es <paramref name="candidate"/> una versión posterior a <paramref name="current"/>? Si alguna no se puede
    /// leer, no (lo ya descargado no se sustituye por algo dudoso).</summary>
    internal static bool IsNewer(string candidate, string current) =>
        Velopack.SemanticVersion.TryParse(candidate, out var c) && Velopack.SemanticVersion.TryParse(current, out var r) && c > r;

    private async Task DownloadAsync(string version)
    {
        try
        {
            await _engine.DownloadAsync(p =>
            {
                Volatile.Write(ref _progress, Math.Clamp(p / 100.0, 0, 1));
                Raise();
            }).ConfigureAwait(false);
            _readyVersion = version;
            Volatile.Write(ref _progress, 1);
            AppLog.Info($"actualizaciones: v{version} descargada y verificada; se aplica al reiniciar o al cerrar Muesca");
            SetStage(UpdateStage.ReadyToApply);
        }
        catch (Exception ex)
        {
            // Incluye ChecksumFailedException: un paquete que no cuadra con el manifiesto firmado no se aplica nunca.
            AppLog.Warn($"actualizaciones: no se pudo descargar la v{version}: {ex.Message}");
            SetStage(UpdateStage.Failed);
        }
    }

    private void SetStage(UpdateStage stage)
    {
        _stage = stage;
        Raise();
    }

    private void Raise()
    {
        if (_disposed) return;
        _post(() => Changed?.Invoke());
    }

    /// <summary>Al cerrar la app. Con una versión descargada y sin aplicar, Update.exe la aplica en silencio en cuanto la app
    /// sale (WaitExitThenApplyUpdates), y el próximo arranque ya es la nueva. Se mira lo descargado, no la etapa: durante una
    /// comprobación la etapa es Checking aunque haya una versión lista.</summary>
    public void Dispose()
    {
        bool applyOnExit;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            applyOnExit = _readyVersion is not null && !_applyScheduled;
            _applyScheduled |= applyOnExit;
        }
        _timer?.Dispose();
        if (!applyOnExit) return;
        try
        {
            _engine.ApplyAfterExit(restart: false);
            AppLog.Info($"actualizaciones: la v{_available} se aplica al salir");
        }
        catch (Exception ex) { AppLog.Warn($"actualizaciones: no se pudo dejar la v{_available} para aplicar al salir: {ex.Message}"); }
    }
}
