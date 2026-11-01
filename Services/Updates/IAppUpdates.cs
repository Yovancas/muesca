namespace ClaudeBarWin.Services.Updates;

/// <summary>En qué anda el actualizador.</summary>
public enum UpdateStage
{
    /// <summary>Sin nada en marcha: al día, sin comprobar todavía o esta copia no se actualiza sola.</summary>
    Idle,
    /// <summary>Preguntando a GitHub por la última versión (y verificando la firma de su manifiesto).</summary>
    Checking,
    /// <summary>Bajando la versión nueva (parches delta o el paquete completo); ver <see cref="IAppUpdates.DownloadProgress"/>.</summary>
    Downloading,
    /// <summary>Descargada y verificada: se aplica al pulsar o, si no, al cerrar la app.</summary>
    ReadyToApply,
    /// <summary>La última comprobación o descarga falló (sin red, firma que no cuadra, paquete corrupto…). Detalles en
    /// <c>claudebar.log</c>; se reintenta en la siguiente vuelta o desde el menú.</summary>
    Failed,
}

/// <summary>
/// Actualizaciones de la app, sin interfaz propia: el menú de la bandeja (y el notch) leen el estado y piden las acciones.
/// La implementación es <see cref="VelopackUpdates"/>.
/// </summary>
public interface IAppUpdates : IDisposable
{
    /// <summary>Versión nueva encontrada («0.5.1», sin «v»); null = al día o sin comprobar.</summary>
    string? AvailableVersion { get; }

    UpdateStage Stage { get; }

    /// <summary>Progreso de la descarga, de 0 a 1 (solo cuenta en <see cref="UpdateStage.Downloading"/>).</summary>
    double DownloadProgress { get; }

    /// <summary>Cambió algo de lo anterior. Siempre en el hilo de UI.</summary>
    event Action? Changed;

    /// <summary>Comprobación silenciosa al arrancar y cada 6 h; si hay versión nueva, la descarga en segundo plano. Una vez.</summary>
    void StartLoop();

    /// <summary>Comprobación a petición (menú). Termina al saber si hay versión nueva; la descarga sigue en segundo plano.</summary>
    Task CheckNowAsync();

    /// <summary>Descarga si hace falta (verificada), aplica y vuelve a abrir la app. Si no hay nada que aplicar, no hace nada.</summary>
    Task ApplyAndRestartAsync();
}
