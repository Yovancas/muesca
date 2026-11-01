namespace ClaudeBarWin.Services.Updates;

/// <summary>
/// Actualizaciones en la edición de la Store: las da la Store, no Velopack. Siempre <see cref="UpdateStage.Idle"/>, sin
/// versión que ofrecer y sin hacer nada: el notch no enseña la oferta y la bandeja esconde «Buscar actualizaciones». Cuando la
/// Store actualiza la app con ella abierta, Windows la cierra y la vuelve a abrir (<c>StoreRestart</c>, solo en ese cierre).
/// </summary>
public sealed class StoreUpdates : IAppUpdates
{
    public string? AvailableVersion => null;
    public UpdateStage Stage => UpdateStage.Idle;
    public double DownloadProgress => 0;

    /// <summary>Nunca cambia.</summary>
    public event Action? Changed { add { } remove { } }

    public void StartLoop() { }
    public Task CheckNowAsync() => Task.CompletedTask;
    public Task ApplyAndRestartAsync() => Task.CompletedTask;
    public void Dispose() { }
}
