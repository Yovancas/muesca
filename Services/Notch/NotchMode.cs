namespace ClaudeBarWin.Services.Notch;

/// <summary>Lo que el notch está enseñando ahora mismo (cada modo tiene su tamaño en <see cref="NotchLayout"/>).</summary>
public enum NotchMode { Hidden, Minimal, Idle, Working, Peek, Expanded, Permission, Completed, QuotaHud, QuotaAlert,
    /// <summary>Recordatorio de una tarea (F5 · B5) con «Hecha» / «+10 min».</summary>
    Reminder }

/// <summary>Tipo de aviso de la cola. El valor es la prioridad: menor = más prioritario (spec §4.5). El recordatorio de
/// una tarea (F5 · B5) va entre «Terminado» y el HUD; el fin de una fase del pomodoro (<see cref="Focus"/>: «¡Foco terminado!»
/// con «Empezar descanso»), justo detrás: nunca tapa un Permiso, el aviso rojo de cuota, «Terminado» ni un recordatorio, y
/// el HUD del 80 % espera a que salga; el aviso de la app (<see cref="Notice"/>: «tu prueba de Pro ha terminado»), el último:
/// nunca tapa nada de Claude. Solo vive en memoria: renumerar no rompe nada guardado.</summary>
public enum NotchAlertKind { Permission = 0, QuotaAlert = 1, Completed = 2, Reminder = 3, Focus = 4, QuotaHud = 5, Notice = 6 }

/// <summary>
/// Un aviso en la cola del notch. Se fusiona por (<see cref="Kind"/>, <see cref="SessionId"/>).
/// <see cref="Duration"/> cuenta desde que el aviso pasa a mostrarse; <c>null</c> = persistente
/// (Permiso: hasta que se resuelve).
///
/// <para><see cref="Title"/>/<see cref="Detail"/> son datos, no texto final: el nombre del proyecto y
/// «herramienta: vista previa» en los avisos de sesión; la ventana («5h»/«7d») y el % en los de cuota.
/// El renderer los compone con <c>Localization</c>.</para>
///
/// <para><see cref="RequestId"/> (solo Permiso, F2): la petición del helper que se puede contestar con los botones;
/// <c>null</c> = solo lectura («Responde en la terminal»).</para>
/// </summary>
public sealed record NotchAlert(NotchAlertKind Kind, string SessionId, string Title, string? Detail,
    DateTime EnqueuedUtc, TimeSpan? Duration, string? RequestId = null)
{
    /// <summary>Duración por defecto de cada tipo (spec §4.5).</summary>
    public static TimeSpan? DefaultDuration(NotchAlertKind kind) => kind switch
    {
        NotchAlertKind.Completed => NotchTiming.Completed,
        NotchAlertKind.QuotaHud => NotchTiming.QuotaHud,
        NotchAlertKind.QuotaAlert => NotchTiming.QuotaAlert,
        NotchAlertKind.Notice => NotchTiming.Notice,
        NotchAlertKind.Focus => NotchTiming.FocusEnd,
        _ => null,   // Permission y Reminder: persistentes (hasta que se contestan)
    };
}

/// <summary>Ajustes del notch que afectan a la máquina de modos (el resto vive en la UI).</summary>
public sealed record NotchSettings(bool OpenOnClick, bool Minimal, bool HideInFullscreen,
    TimeSpan? AutoMinimalAfter = null);

/// <summary>Tiempos del notch en un único sitio (spec §4.5): nada de literales sueltos en la máquina.</summary>
public static class NotchTiming
{
    /// <summary>Hover continuo necesario para expandir (si no se abre con clic).</summary>
    public static readonly TimeSpan HoverExpand = TimeSpan.FromMilliseconds(250);

    /// <summary>Gracia tras sacar el puntero antes de contraer.</summary>
    public static readonly TimeSpan CollapseGrace = TimeSpan.FromMilliseconds(250);

    /// <summary>Tras un cambio de página pedido por el usuario, cuánto sigue abierto el expandido si el puntero se quedó
    /// fuera de la forma nueva y no vuelve a entrar.</summary>
    public static readonly TimeSpan PageChangeHold = TimeSpan.FromSeconds(4);

    /// <summary>Aviso «Terminado» (como los avisos de conexión/carga de la referencia: 4,5 s).</summary>
    public static readonly TimeSpan Completed = TimeSpan.FromSeconds(4.5);

    /// <summary>HUD de cuota al 80 % (como los HUD discretos de la referencia: 1,8 s).</summary>
    public static readonly TimeSpan QuotaHud = TimeSpan.FromSeconds(1.8);

    /// <summary>Aviso rojo de cuota al 95 %.</summary>
    public static readonly TimeSpan QuotaAlert = TimeSpan.FromSeconds(4.5);

    /// <summary>Permiso: los botones no aceptan clics hasta este rato después de mostrarse (sin clics por accidente).</summary>
    public static readonly TimeSpan PermissionArm = TimeSpan.FromMilliseconds(400);

    /// <summary>Permiso: «Permitido» / «Denegado» se queda a la vista este rato antes de cerrar.</summary>
    public static readonly TimeSpan PermissionConfirm = TimeSpan.FromMilliseconds(600);

    /// <summary>Aviso de la app («tu prueba de Pro ha terminado»): un poco más que «Terminado», sin sonido.</summary>
    public static readonly TimeSpan Notice = TimeSpan.FromSeconds(8);

    /// <summary>La tarjeta del Pro abierta desde la bandeja, sin el puntero encima: se queda este rato antes de cerrar.</summary>
    public static readonly TimeSpan ProCardLinger = TimeSpan.FromSeconds(10);

    /// <summary>Fin de una fase del pomodoro: el notch se abre este rato con «Empezar descanso» / «Empezar foco» y se cierra solo
    /// (la oferta sigue en la página Foco).</summary>
    public static readonly TimeSpan FocusEnd = TimeSpan.FromSeconds(10);
}
