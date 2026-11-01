namespace ClaudeBarWin.Services.Licensing;

/// <summary>
/// Edición en la que está la app ahora (docs/superpowers/specs/2026-10-30-gratis-y-pro-design.md). <see cref="Free"/>: la
/// edición gratis (GitHub, o la Store sin compra y con la prueba acabada); <see cref="Trial"/>: los 7 días de prueba de la
/// Store, con todo el Pro; <see cref="Pro"/>: el complemento «Pro» comprado. Solo vive en memoria: lo guardado en
/// <c>config.json</c> es la fecha de inicio de la prueba y el último estado conocido de la compra.
/// </summary>
public enum ProState { Free, Trial, Pro }

/// <summary>Funciones de pago (el reparto del diseño). Cada comprobación pasa por <see cref="ProGate.IsUnlocked"/>.</summary>
public enum ProFeature
{
    /// <summary>Permitir / Denegar desde el notch (sin Pro, el helper no recibe respuesta y Claude Code enseña su diálogo).</summary>
    Permissions,
    /// <summary>Música y letras sincronizadas (página Música, canción y letra en el reposo).</summary>
    Music,
    /// <summary>Tareas con avisos.</summary>
    Tasks,
    /// <summary>Bandeja de archivos.</summary>
    Shelf,
    /// <summary>Notch en varios monitores: los tres modos (gratis = solo el principal).</summary>
    MultiMonitor,
    /// <summary>Gráficas interactivas de la página Uso del notch: hover con tooltip, rangos, proyección del ritmo, alternar
    /// series y transiciones. Sin ella la página Uso sigue con todos sus números y la gráfica, quieta, con el chip «Pro».</summary>
    NotchUsage,
    /// <summary>Lo avanzado del pomodoro de la página Foco: enlazar el foco con una tarea (cada foco completo se apunta en ella),
    /// las estadísticas de la semana y las duraciones personalizadas. Sin ella el pomodoro básico sigue entero (25 / 5 / 15
    /// cada 4, el mini anillo, el aviso del fin de fase y los focos de hoy) y lo demás lleva el chip «Pro».</summary>
    NotchFocus,
}

/// <summary>Cómo acabó una compra o una restauración.</summary>
public enum PurchaseResult
{
    /// <summary>Comprado ahora.</summary>
    Purchased,
    /// <summary>Ya era suyo (comprar otra vez, o «Restaurar compra» con la compra hecha).</summary>
    AlreadyOwned,
    /// <summary>«Restaurar compra»: la Store respondió y esta cuenta no tiene el Pro.</summary>
    NotOwned,
    /// <summary>El usuario cerró la ventana de compra.</summary>
    Cancelled,
    /// <summary>Aquí no se puede comprar (edición GitHub) o el complemento no está en la Store.</summary>
    NotAvailable,
    /// <summary>Sin conexión con la Store (o no respondió).</summary>
    NetworkError,
    /// <summary>Cualquier otro fallo (anotado en el log).</summary>
    Failed,
}

/// <summary>Lo que se enseña del Pro fuera del notch (menú de la bandeja, asistente): el estado, los días de prueba que
/// quedan y si se puede comprar desde aquí (edición de la Store).</summary>
public sealed record ProSummary(ProState State, int? TrialDaysLeft, bool Store)
{
    public static ProSummary From(IProLicense license)
    {
        ArgumentNullException.ThrowIfNull(license);
        return new(license.State, license.TrialDaysLeft, license.CanPurchaseHere);
    }
}
