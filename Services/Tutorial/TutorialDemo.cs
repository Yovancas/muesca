using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Tutorial;

/// <summary>Lo que hace el usuario sobre el notch de demostración (o la bandeja de mentira), ya traducido por la ventana
/// desde el ratón o el teclado. Nunca toca nada de verdad: solo cambia <see cref="TutorialDemo"/>.</summary>
public enum TutorialAct
{
    None,
    /// <summary>El ratón entra en el notch (vista previa).</summary>
    HoverNotch,
    /// <summary>Se abre: el ratón se queda encima <see cref="NotchTiming.HoverExpand"/> o hace clic.</summary>
    ExpandNotch,
    /// <summary>El ratón sale y, pasada la gracia, se cierra.</summary>
    LeaveNotch,
    /// <summary>Rueda sobre la lista de sesiones que no cabe (<see cref="TutorialInput.Value"/>: +1 baja, −1 sube).</summary>
    WheelSessions,
    /// <summary>Clic en el texto del Permiso (fuera de los botones): traería su terminal al frente.</summary>
    ClickPermissionText,
    PermissionDeny,
    PermissionAllow,
    /// <summary>Rueda que cambia de página (+1 siguiente, −1 anterior; con la vuelta, como el carrusel).</summary>
    WheelPage,
    /// <summary>Clic en un punto de página (<see cref="TutorialInput.Value"/> = su índice).</summary>
    DotPage,
    /// <summary>Clic en una flecha ‹ › (+1 / −1).</summary>
    ArrowPage,
    PlayPause,
    NextTrack,
    PreviousTrack,
    ToggleLyrics,
    /// <summary>Clic en el círculo de una tarea (<see cref="TutorialInput.Value"/> = fila).</summary>
    CheckTask,
    AddTask,
    ReminderDone,
    ReminderSnooze,
    /// <summary>El archivo de ejemplo, soltado encima del notch.</summary>
    DropFile,
    /// <summary>Clic en una tarjeta de la Bandeja.</summary>
    OpenShelfItem,
    /// <summary>Selector Gasto / Cuota de la página Uso (0 = gasto, 1 = cuota).</summary>
    UsageMode,
    /// <summary>Tarjeta de 5 h (0) o de 7 d (1) de la página Uso.</summary>
    UsageTile,
    /// <summary>El ratón sobre la gráfica (<see cref="TutorialInput.Value"/> = dónde, en milésimas del ancho: 0..1000; negativo =
    /// ya no está encima).</summary>
    ChartHover,
    /// <summary>Empezar / Pausa / Reanudar del pomodoro.</summary>
    FocusPrimary,
    FocusSkip,
    FocusReset,
    /// <summary>La fila de la tarea enlazada (pasa a la siguiente).</summary>
    FocusTask,
    /// <summary>Clic izquierdo en el icono de la bandeja (el panel).</summary>
    TrayLeft,
    /// <summary>Clic derecho en el icono (el menú).</summary>
    TrayRight,
    MenuMonitor,
    MenuSettings,
    MenuTutorial,
}

/// <summary>Un gesto con su dato (dirección, índice) cuando lo lleva.</summary>
public readonly record struct TutorialInput(TutorialAct Act, int Value = 0)
{
    public static readonly TutorialInput None = new(TutorialAct.None);
}

/// <summary>Lo que enseña el notch de demostración con el ratón lejos (luego el ratón lo abre o le pone la vista previa).</summary>
public enum TutorialFace
{
    /// <summary>Los dos anillos de la cuota.</summary>
    Idle,
    /// <summary>Hora y fecha, con el gato.</summary>
    Clock,
    Working,
    /// <summary>«Listo»: Claude terminó.</summary>
    Done,
    /// <summary>El aviso del 95 %, con su halo.</summary>
    QuotaHigh,
    /// <summary>La raya fina.</summary>
    Minimal,
    /// <summary>El Permiso con Denegar / Permitir.</summary>
    Permission,
    /// <summary>El recordatorio de una tarea con «Hecha» / «+10 min».</summary>
    Reminder,
    /// <summary>La línea de la letra que suena.</summary>
    CompactLyrics,
}

/// <summary>Dónde está el ratón respecto al notch de demostración.</summary>
public enum TutorialPointer { Away, Peek, Expanded }

/// <summary>Qué se ve de la bandeja de Windows de mentira (capítulo 10).</summary>
public enum TutorialTray { None, Icon, Panel, Menu, Monitor, Settings }

/// <summary>Lo que «pasaría» de verdad y aquí solo se cuenta (una línea bajo el escenario).</summary>
public enum TutorialEffect { None, Terminal, OpenFile, TutorialHere }

/// <summary>Respuesta dada a un aviso con botones del notch de demostración.</summary>
public enum TutorialAnswer { None, Deny, Allow }

/// <summary>
/// El notch de demostración y su bandeja de mentira en un instante: inmutable, con datos de ejemplo (nunca los del usuario).
/// <see cref="Apply"/> es lo que haría el notch de verdad con cada gesto; <see cref="Mode"/>, el modo que enseñaría (con la
/// misma precedencia que <see cref="NotchStateMachine"/>: el Permiso y el recordatorio mandan sobre el expandido; el expandido,
/// sobre los avisos; los avisos, sobre la vista previa). PURO.
/// </summary>
public sealed record TutorialDemo(
    TutorialFace Face = TutorialFace.Idle,
    TutorialPointer Pointer = TutorialPointer.Away,
    NotchPage Page = NotchPage.Claude,
    int SessionOffset = 0,
    TutorialAnswer Answer = TutorialAnswer.None,
    bool Playing = true,
    int Track = 0,
    bool Lyrics = false,
    int TasksDone = 0,
    bool Drafting = false,
    bool ShelfFile = false,
    UsageChartMode UsageMode = UsageChartMode.Spend,
    QuotaFocus UsageFocus = QuotaFocus.None,
    int ChartAt = -1,
    FocusRun FocusRun = FocusRun.Ready,
    FocusPhase FocusPhase = FocusPhase.Focus,
    int FocusTask = 0,
    TutorialTray Tray = TutorialTray.None,
    TutorialEffect Effect = TutorialEffect.None)
{
    /// <summary>Las páginas del notch de demostración: las de serie, todas (también las del Pro, para probarlas).</summary>
    public static IReadOnlyList<NotchPage> Pages => NotchPages.Default;

    /// <summary>Sesiones de ejemplo (la lista no cabe: la rueda la desplaza) y filas de sesión que caben a la vez.</summary>
    public const int SessionCount = 8, SessionRows = NotchLayout.ExpandedMaxRows;

    /// <summary>Recorrido de <see cref="ChartAt"/> (milésimas del ancho de la gráfica); −1 = sin el ratón encima.</summary>
    public const int ChartSpan = 1000;

    /// <summary>Canciones y tareas de ejemplo.</summary>
    public const int TrackCount = 3, TaskCount = 3;

    /// <summary>Lo más que baja la lista de sesiones (la última fila es «+N más» mientras quedan por debajo).</summary>
    public static int MaxSessionOffset => Math.Max(0, SessionCount - SessionRows);

    /// <summary>Índice de <see cref="Page"/> entre <see cref="Pages"/>.</summary>
    public int PageIndex => IndexOf(Page);

    /// <summary>¿Está hecha la tarea <paramref name="i"/>?</summary>
    public bool TaskDone(int i) => i is >= 0 and < 31 && (TasksDone & (1 << i)) != 0;

    /// <summary>¿Se ve el notch? (en la bandeja de mentira, el notch de reposo sigue arriba, pero quieto).</summary>
    public bool OnTray => Tray != TutorialTray.None;

    /// <summary>
    /// El modo que enseñaría el notch de verdad: Permiso y Recordatorio (sin contestar) mandan sobre todo; luego el expandido;
    /// luego «Listo» y el 95 %; la vista previa sobre el reposo, Trabajando o la raya; y si no, la cara de reposo.
    /// </summary>
    public NotchMode Mode
    {
        get
        {
            if (Face == TutorialFace.Permission) return NotchMode.Permission;
            if (Face == TutorialFace.Reminder && Answer == TutorialAnswer.None) return NotchMode.Reminder;
            if (Pointer == TutorialPointer.Expanded && !OnTray) return NotchMode.Expanded;
            switch (Face)
            {
                case TutorialFace.Done: return NotchMode.Completed;
                case TutorialFace.QuotaHigh: return NotchMode.QuotaAlert;
            }
            if (Pointer == TutorialPointer.Peek && !OnTray) return NotchMode.Peek;
            return Face switch
            {
                TutorialFace.Working => NotchMode.Working,
                TutorialFace.Minimal => NotchMode.Minimal,
                _ => NotchMode.Idle,
            };
        }
    }

    /// <summary>Lo que haría el notch de verdad con este gesto (lo que no le toca, igual). PURO.</summary>
    public TutorialDemo Apply(TutorialInput input)
    {
        switch (input.Act)
        {
            case TutorialAct.HoverNotch:
                return Pointer == TutorialPointer.Away ? this with { Pointer = TutorialPointer.Peek } : this;
            case TutorialAct.ExpandNotch:
                // Cada apertura empieza en Claude (como NotchForm), salvo si ya estaba abierto.
                return Pointer == TutorialPointer.Expanded ? this
                    : this with { Pointer = TutorialPointer.Expanded, Page = NotchPage.Claude, SessionOffset = 0, Drafting = false };
            case TutorialAct.LeaveNotch:
                return this with { Pointer = TutorialPointer.Away, Drafting = false, ChartAt = -1, SessionOffset = 0 };
            case TutorialAct.WheelSessions:
                return this with { SessionOffset = Math.Clamp(SessionOffset + Math.Sign(input.Value), 0, MaxSessionOffset) };
            case TutorialAct.ClickPermissionText:
                return Face == TutorialFace.Permission ? this with { Effect = TutorialEffect.Terminal } : this;
            case TutorialAct.PermissionDeny:
            case TutorialAct.PermissionAllow:
                if (Face != TutorialFace.Permission || Answer != TutorialAnswer.None) return this;
                return this with { Answer = input.Act == TutorialAct.PermissionAllow ? TutorialAnswer.Allow : TutorialAnswer.Deny };
            case TutorialAct.WheelPage:
                return Turn(Math.Sign(input.Value), wrap: true);
            case TutorialAct.ArrowPage:
                // Las flechas no dan la vuelta (PageArrows.Target): en la última, › no lleva a ningún sitio.
                return Turn(Math.Sign(input.Value), wrap: false);
            case TutorialAct.DotPage:
                return input.Value >= 0 && input.Value < Pages.Count ? this with { Page = Pages[input.Value], ChartAt = -1 } : this;
            case TutorialAct.PlayPause:
                return this with { Playing = !Playing };
            case TutorialAct.NextTrack:
                return this with { Track = (Track + 1) % TrackCount, Playing = true };
            case TutorialAct.PreviousTrack:
                return this with { Track = (Track + TrackCount - 1) % TrackCount, Playing = true };
            case TutorialAct.ToggleLyrics:
                return this with { Lyrics = !Lyrics };
            case TutorialAct.CheckTask:
                return input.Value is >= 0 and < TaskCount ? this with { TasksDone = TasksDone ^ (1 << input.Value) } : this;
            case TutorialAct.AddTask:
                return this with { Drafting = true };
            case TutorialAct.ReminderDone:
            case TutorialAct.ReminderSnooze:
                if (Face != TutorialFace.Reminder || Answer != TutorialAnswer.None) return this;
                return this with { Answer = input.Act == TutorialAct.ReminderDone ? TutorialAnswer.Allow : TutorialAnswer.Deny,
                    TasksDone = input.Act == TutorialAct.ReminderDone ? TasksDone | 1 : TasksDone };
            case TutorialAct.DropFile:
                // Algo aceptable encima abre el expandido YA en la Bandeja; al soltar, se queda ahí.
                return this with { ShelfFile = true, Pointer = TutorialPointer.Expanded, Page = NotchPage.Shelf };
            case TutorialAct.OpenShelfItem:
                return ShelfFile ? this with { Effect = TutorialEffect.OpenFile } : this;
            case TutorialAct.UsageMode:
                return this with { UsageMode = input.Value == 0 ? UsageChartMode.Spend : UsageChartMode.Quota,
                    UsageFocus = input.Value == 0 ? QuotaFocus.None : UsageFocus, ChartAt = -1 };
            case TutorialAct.UsageTile:
                return this with { UsageMode = UsageChartMode.Quota, UsageFocus = input.Value == 0 ? QuotaFocus.Five : QuotaFocus.Seven,
                    ChartAt = -1 };
            case TutorialAct.ChartHover:
                // Fuera de la gráfica (valor negativo), sin el ratón encima.
                return this with { ChartAt = input.Value < 0 ? -1 : Math.Clamp(input.Value, 0, ChartSpan) };
            case TutorialAct.FocusPrimary:
                return this with { FocusRun = FocusRun == FocusRun.Running ? FocusRun.Paused : FocusRun.Running };
            case TutorialAct.FocusSkip:
                return this with { FocusPhase = FocusPhase == FocusPhase.Focus ? FocusPhase.ShortBreak : FocusPhase.Focus,
                    FocusRun = FocusRun.Ready };
            case TutorialAct.FocusReset:
                return this with { FocusPhase = FocusPhase.Focus, FocusRun = FocusRun.Ready };
            case TutorialAct.FocusTask:
                return this with { FocusTask = (FocusTask + 1) % TaskCount };
            case TutorialAct.TrayLeft:
                return this with { Tray = Tray == TutorialTray.Panel ? TutorialTray.Icon : TutorialTray.Panel };
            case TutorialAct.TrayRight:
                return this with { Tray = TutorialTray.Menu };
            case TutorialAct.MenuMonitor:
                return Tray is TutorialTray.Menu or TutorialTray.Monitor ? this with { Tray = TutorialTray.Monitor } : this;
            case TutorialAct.MenuSettings:
                return Tray is TutorialTray.Menu or TutorialTray.Monitor ? this with { Tray = TutorialTray.Settings } : this;
            case TutorialAct.MenuTutorial:
                return Tray is TutorialTray.Menu or TutorialTray.Monitor or TutorialTray.Settings
                    ? this with { Effect = TutorialEffect.TutorialHere } : this;
            default:
                return this;
        }
    }

    /// <summary>Página siguiente o anterior: con la vuelta (la rueda, como <see cref="PageCarousel"/>) o sin ella (las flechas).</summary>
    private TutorialDemo Turn(int direction, bool wrap)
    {
        if (direction == 0) return this;
        int n = Pages.Count, to = PageIndex + direction;
        if (!wrap && (to < 0 || to >= n)) return this;
        return this with { Page = Pages[(to % n + n) % n], ChartAt = -1 };
    }

    private static int IndexOf(NotchPage page)
    {
        for (int i = 0; i < Pages.Count; i++) if (Pages[i] == page) return i;
        return 0;
    }
}
