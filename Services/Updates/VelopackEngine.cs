using Velopack;

namespace ClaudeBarWin.Services.Updates;

/// <summary>
/// Lo que <see cref="VelopackUpdates"/> necesita de Velopack, para poder probar sus etapas con un motor falso.
/// </summary>
internal interface IUpdateEngine
{
    /// <summary>¿Esta copia la instaló Velopack? (una build de desarrollo o la vieja de Inno, no).</summary>
    bool IsInstalled { get; }

    /// <summary>Versión ya descargada en una ejecución anterior y aún sin aplicar, o null.</summary>
    string? PendingVersion { get; }

    /// <summary>Busca la última versión verificada; null = al día. Lanza si no se pudo comprobar (sin red, o solo hay
    /// releases con la firma mala).</summary>
    Task<string?> CheckAsync();

    /// <summary>Descarga la versión de la última comprobación (con parches delta si puede) y comprueba su SHA-256.
    /// <paramref name="progress"/> va de 0 a 100.</summary>
    Task DownloadAsync(Action<int> progress);

    /// <summary>Deja a Update.exe esperando a que salga la app para aplicar lo descargado; con
    /// <paramref name="restart"/>, la vuelve a abrir después (y enseña su progreso); sin él, en silencio.</summary>
    void ApplyAfterExit(bool restart);
}

/// <summary><see cref="IUpdateEngine"/> de verdad: el <see cref="UpdateManager"/> de Velopack sobre <see cref="SignedGithubSource"/>.</summary>
internal sealed class VelopackEngine : IUpdateEngine
{
    private readonly SignedGithubSource _source;
    private readonly UpdateManager? _manager;
    private UpdateInfo? _found;

    public VelopackEngine(SignedGithubSource source)
    {
        _source = source;
        // Sin VelopackApp.Run() no hay localizador y el constructor lanza: entonces no hay actualizaciones, sin más.
        try { _manager = new UpdateManager(source); }
        catch (Exception ex) { AppLog.Warn($"actualizaciones: Velopack no está disponible ({ex.Message})"); }
    }

    public bool IsInstalled => _manager?.IsInstalled == true;

    public string? PendingVersion => IsInstalled ? _manager!.UpdatePendingRestart?.Version?.ToString() : null;

    public async Task<string?> CheckAsync()
    {
        var manager = _manager ?? throw new InvalidOperationException("Velopack no está disponible");
        _found = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (_found is null && NotUpToDateReason(_source.LastRejected, _source.LastVerified, manager.CurrentVersion) is { } why)
            throw new InvalidDataException(why);
        return _found?.TargetFullRelease.Version.ToString();
    }

    /// <summary>
    /// Sin ninguna versión válida más nueva que <paramref name="installed"/>, ¿es eso «al día» (null) o algo no cuadra (el
    /// motivo)? No cuadra si se descartó una release que podría ser más nueva (por su nombre, o sin versión legible: una
    /// manipulada, publicada sin firmar o que no se pudo bajar), o si no hay ninguna release firmada a la vista. Las
    /// descartadas más viejas que la instalada no importan: no taparían nada.
    /// </summary>
    internal static string? NotUpToDateReason(IReadOnlyList<RejectedRelease> rejected, int verified, SemanticVersion? installed)
    {
        var newer = rejected.Where(r => installed is null || r.ClaimedVersion is not { } v || v > installed).ToList();
        if (newer.Count > 0) return "ninguna versión nueva con la firma válida; descartadas: " + string.Join("; ", newer);
        if (verified == 0) return "ninguna release con el manifiesto firmado entre las últimas";
        return null;
    }

    public Task DownloadAsync(Action<int> progress)
    {
        var manager = _manager ?? throw new InvalidOperationException("Velopack no está disponible");
        var found = _found ?? throw new InvalidOperationException("no hay ninguna versión que descargar");
        return manager.DownloadUpdatesAsync(found, progress);
    }

    public void ApplyAfterExit(bool restart)
    {
        var manager = _manager ?? throw new InvalidOperationException("Velopack no está disponible");
        // Lo que de verdad está descargado y es más nuevo que lo instalado (de esta ejecución o de otra). Al descargar,
        // Velopack borra los demás paquetes aunque la descarga falle: lo último que se buscó puede no estar. Sin nada así,
        // Update.exe aplicaría el último paquete local, que es la versión instalada: mejor fallar.
        var asset = manager.UpdatePendingRestart
                    ?? throw new InvalidOperationException("no hay ninguna versión descargada que aplicar");
        manager.WaitExitThenApplyUpdates(asset, silent: !restart, restart: restart);
    }
}
