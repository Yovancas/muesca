namespace ClaudeBarWin.Config;

/// <summary>
/// Lo que recuerda el contador anónimo de uso (<see cref="Services.Telemetry.UsagePing"/>) entre ejecuciones, dentro de
/// <c>config.json</c>. No hay ningún identificador: solo fechas locales y casillas para no avisar dos veces el mismo día y
/// para saber si el siguiente aviso es el primero del mes o el de una instalación nueva.
/// </summary>
public sealed record UsageStatsData
{
    /// <summary>Último día local (<c>yyyy-MM-dd</c>) con un aviso aceptado por el servidor (2xx); null = nunca.</summary>
    public string? LastDay { get; init; }

    /// <summary>Último mes local (<c>yyyy-MM</c>) con un aviso aceptado; null = nunca.</summary>
    public string? LastMonth { get; init; }

    /// <summary>Ya hubo un primer aviso aceptado en esta instalación (a partir de ahí, <c>n=0</c>).</summary>
    public bool FirstSent { get; init; }

    /// <summary>Instalación nueva: el primer aviso lleva <c>n=1</c>. Lo fija <see cref="AppConfig.Normalize"/> la primera vez
    /// que no hay datos: solo es nueva si la bienvenida aún está pendiente (quien ya usaba la app y actualiza, <c>n=0</c>).</summary>
    public bool NewInstall { get; init; }

    /// <summary>El usuario ya ha visto el aviso del contador: en la bienvenida, en Ajustes o, al actualizar, en el globo.</summary>
    public bool NoticeSeen { get; init; }

    /// <summary>Cuándo salió el globo para quien actualiza (UTC); el primer aviso espera al menos 10 min desde aquí. null = se
    /// vio en la bienvenida o en Ajustes (sin espera).</summary>
    public DateTime? NoticeUtc { get; init; }
}
