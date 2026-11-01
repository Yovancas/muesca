namespace ClaudeBarWin.Services.Updates;

/// <summary>Qué hace la entrada de actualizaciones del menú de la bandeja.</summary>
public enum UpdateMenuAction
{
    /// <summary>Buscar actualizaciones (y avisar del resultado).</summary>
    Check,
    /// <summary>Aplicar la versión nueva y reiniciar (descargándola antes si hace falta).</summary>
    Apply,
    /// <summary>Hay una comprobación o una descarga en marcha: la entrada está deshabilitada.</summary>
    None,
}

/// <summary>
/// La entrada de actualizaciones del menú de la bandeja según <see cref="IAppUpdates"/> (texto, acción y avisos), sin
/// WinForms, para poder probarla.
/// </summary>
public static class UpdateMenu
{
    public static string Text(UpdateStage stage, string? version, double progress, Strings s) => stage switch
    {
        UpdateStage.Checking => s.Checking,
        UpdateStage.Downloading => string.Format(s.UpdateDownloadingFmt, version, (int)Math.Round(Math.Clamp(progress, 0, 1) * 100)),
        UpdateStage.ReadyToApply => string.Format(s.UpdateRestartFmt, version),
        // Se encontró, pero la descarga falló: al pulsar se reintenta y se aplica.
        UpdateStage.Failed when version is not null => string.Format(s.UpdateAvailableFmt, version),
        _ => s.CheckUpdates,
    };

    public static UpdateMenuAction Action(UpdateStage stage, string? version) => stage switch
    {
        UpdateStage.ReadyToApply => UpdateMenuAction.Apply,
        UpdateStage.Failed when version is not null => UpdateMenuAction.Apply,
        UpdateStage.Checking or UpdateStage.Downloading => UpdateMenuAction.None,
        _ => UpdateMenuAction.Check,
    };

    /// <summary>Aviso justo después de «Buscar actualizaciones» (la comprobación ya terminó; la descarga puede seguir).</summary>
    public static string AfterCheck(UpdateStage stage, string? version, string currentVersion, string logPath, Strings s) => stage switch
    {
        UpdateStage.Failed => string.Format(s.UpdateFailedLogFmt, logPath),
        UpdateStage.Downloading => string.Format(s.UpdateFoundFmt, version),
        UpdateStage.ReadyToApply => string.Format(s.UpdateReadyFmt, version),
        _ => string.Format(s.UpToDateFmt, currentVersion),
    };
}
