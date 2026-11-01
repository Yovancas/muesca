using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Página Foco del notch: el pomodoro (la única función de la referencia que faltaba; diseño propio). Archivo aparte para no
/// mezclarse con el resto de la ventana.
/// <list type="bullet">
/// <item><b>Un pomodoro para todas las ventanas</b> (<see cref="NotchShared.Focus"/>, puro: <see cref="PomodoroTimer"/>): un botón en
/// cualquier ventana lo cambia en todas; cada fase acaba UNA vez (<see cref="NotchShared.FocusEnded"/>). Con un notch por
/// monitor, solo la ventana interactiva acciona sus botones y suena; en las demás, un clic pide serlo.</item>
/// <item><b>Página:</b> anillo grande con la cuenta atrás, Empezar / Pausa / Reanudar, saltar, reiniciar, los focos de hoy y, con
/// el Pro (<see cref="ProFeature.NotchFocus"/>, por <see cref="NotchProGating"/>), la tarea enlazada (un clic pasa a la siguiente
/// pendiente; cada foco completo se apunta en ella) y la semana. Sin el Pro, esas dos filas con el chip «Pro».</item>
/// <item><b>Notch cerrado</b> con el pomodoro en marcha o en pausa: el mini anillo en el reposo (manda sobre la canción y el
/// reloj), en la vista previa (con el anillo de 5 h al lado) y, sin texto, junto al % de Trabajando (Claude manda); en Mínimo,
/// la raya se llena con lo que queda. Los avisos (Permiso, cuota, «Terminado», recordatorios) lo tapan como siempre: el mini
/// anillo nunca es un aviso. Corriendo, el reposo no se encoge solo.</item>
/// <item><b>Fin de fase:</b> si la página está a la vista, la oferta ya está en ella (el anillo bota y suena); si no, el aviso
/// del fin de fase en la cola (<see cref="NotchStateMachine.OnFocusEnd"/>, después de los recordatorios) con «Empezar descanso» /
/// «Empezar foco», armado como el del recordatorio; un clic fuera del botón lo descarta en todas las ventanas. El anillo del
/// aviso entra con un bote (<see cref="NotchMotion.FocusPopFrom"/> con el muelle de atención; con «reducir movimiento», quieto).
/// El aviso discreto suena una vez, en la interactiva, con su ajuste y nunca en pantalla completa ni horas después.</item>
/// <item><b>0 % de CPU:</b> nada de ticks. Con la página a la vista, un disparo cuando cambia el segundo que se enseña; cerrado,
/// cuando cambia el minuto (en el último, el segundo); parado o en pausa, nada. Los textos (<see cref="FocusText"/>) se rehacen
/// solo cuando cambia lo que se ve.</item>
/// </list>
/// </summary>
public sealed partial class NotchForm
{
    /// <summary>Maquetación del notch cerrado con el mini anillo del pomodoro (reposo, vista previa y Trabajando).</summary>
    private const int LayoutFocus = 64;

    /// <summary>Un fin de fase visto más tarde que esto (suspensión, app cerrada) se enseña, pero no suena.</summary>
    private static readonly TimeSpan FocusChimeLate = TimeSpan.FromMinutes(1);

    private readonly HoverHighlight _focusHover = new();
    // Bote del fin de fase: el muelle de atención (rebota a propósito) con umbrales de escala 0..1 (los globales son de px).
    private static readonly SpringPreset FocusPopPreset = Motion.SpringAttention with { EpsX = 0.002, EpsV = 0.02 };
    private readonly SpringValue _focusPop = new(1, FocusPopPreset);
    private FocusHit _focusHeld;                 // control de la página pulsado (DOWN) y aún sin soltar
    private FocusHit _focusHoverHit;             // control del realce (se conserva mientras se desvanece)
    private bool _focusAlertHeld;                // botón del aviso del fin de fase pulsado y aún sin soltar
    private NotchAlert? _focusPopped;            // el aviso del fin de fase que ya botó
    private DateTime? _focusChimeFor;            // cuándo acabó la fase que aún puede sonar (UTC); null = ya sonó o se calló
    // Textos del pomodoro, rehechos solo cuando cambia lo que se ve (segundo, minuto, estado, idioma, tarea).
    private FocusTexts? _focusTexts;
    private FocusTextKey _focusTextsKey;
    private IReadOnlyList<int> _focusWeek = Array.Empty<int>();
    private int _focusTaskColor = -1;

    private PomodoroTimer Pomodoro => _shared.Focus;

    /// <summary>¿La página Foco está en el carrusel?</summary>
    private bool FocusEnabled => _pages.Contains(NotchPage.Focus);

    /// <summary>¿La página Foco es la del expandido (objetivo)?</summary>
    private bool FocusPageVisible => FocusEnabled && !_hiddenForFullscreen && _sm.Mode == NotchMode.Expanded
        && _carousel.Current == NotchPage.Focus && !_proCardOpen;

    /// <summary>¿Se PINTA la página Foco quieta y entera? Solo entonces acepta ratón (lo que se pinta es lo que se acciona).</summary>
    private bool FocusPageShown => FocusPageVisible && _anim.ShownMode == NotchMode.Expanded && _anim.ContentAlpha >= 1f
        && _pageAnim.Current == NotchPage.Focus && !_pageAnim.IsAnimating && !_proCardOpen;

    /// <summary>Sin el Pro: la tarea enlazada y la semana con el chip (lo decide <see cref="NotchProGating"/> con <see cref="ProGate"/>).</summary>
    private bool FocusLocked => (_options.Locked & NotchProLocks.FocusPro) != 0;

    /// <summary>¿Lleva el notch cerrado el mini anillo? Con el pomodoro en marcha o en pausa.</summary>
    private bool FocusMini => Pomodoro.Active;

    /// <summary>¿Lleva el REPOSO el mini anillo? En marcha, sí; en pausa, solo si no suena música (si suena, vuelve la canción).</summary>
    private bool FocusIdle(DateTime utcNow) => FocusFace.OnIdle(Pomodoro.Run, _options.Music && _idleMusic.Show(utcNow));

    // ---------------------------------------------------------------- estado

    /// <summary>El pomodoro cambió (aquí o en otra ventana): sin oferta, fuera el aviso del fin de fase; y a repintar.</summary>
    private void OnFocusChanged()
    {
        if (!Alive) return;
        if (Pomodoro.Finished is null) _sm.ClearFocusAlert(DateTime.UtcNow);
        Sync();
    }

    /// <summary>
    /// Acabó una fase (una vez para todas las ventanas). Con la página Foco a la vista la oferta ya está en ella: el anillo bota
    /// y suena (en la interactiva). Si no, el aviso a la cola: suena cuando se ve (<see cref="ChimeIfNewAlert"/>).
    /// </summary>
    private void OnFocusEnded(FocusEnd e)
    {
        if (!Alive) return;
        _focusChimeFor = DateTime.UtcNow - e.Late;
        if (FocusPageVisible)
        {
            FocusChime();
            StartFocusPop();
        }
        else _sm.OnFocusEnd(e.Ended.ToString(), e.Next.ToString(), DateTime.UtcNow);
        Sync();
    }

    /// <summary>El aviso del fin de fase se descartó en otra ventana (o en esta): fuera de la cola.</summary>
    private void OnFocusAlertCleared()
    {
        if (!Alive) return;
        _sm.ClearFocusAlert(DateTime.UtcNow);
        Sync();
    }

    /// <summary>El aviso discreto del fin de fase: una vez, en la interactiva, con su ajuste y el del sonido del notch, nunca en
    /// pantalla completa (en ningún monitor) ni si la fase acabó hace rato: el aviso pudo esperar detrás de un Permiso o de un
    /// juego a pantalla completa; entonces se ve, pero ya no suena.</summary>
    private void FocusChime()
    {
        if (_focusChimeFor is not { } endedAt || !NotchAlertRouting.MayChime(_interactive)) return;
        _focusChimeFor = null;
        if (DateTime.UtcNow - endedAt > FocusChimeLate) return;
        bool fullscreen = NotchAlertRouting.QuietForFullscreen(_fullscreen.IsFullscreen, _shared.AnyFullscreen);
        if (NotchSound.ShouldPlayFocus(_options.Sound, _options.FocusOrDefault.Sound, fullscreen)) NotchSound.PlayFocus();
    }

    /// <summary>El bote del anillo (muelle de atención); con «reducir movimiento», nada.</summary>
    private void StartFocusPop()
    {
        if (_options.ReduceMotion)
        {
            _focusPop.Snap(1);
            return;
        }
        _focusPop.Snap(NotchMotion.FocusPopFrom);
        _focusPop.SetTarget(1);
    }

    /// <summary>Plazo del siguiente repintado del pomodoro (para <see cref="ScheduleWake"/>): corriendo, con la página a la vista,
    /// cuando cambie el segundo que enseña; con el notch cerrado (el mini anillo), cuando cambie su minuto. Parado o en pausa con
    /// la página a la vista, a la medianoche local (los focos de «hoy» vuelven a 0). Si no, null.</summary>
    private TimeSpan? FocusNextTickIn()
    {
        if (Pomodoro.Run != FocusRun.Running)
            return FocusPageVisible ? FocusClock.NextMidnightIn(DateTime.UtcNow, TimeZoneInfo.Local) : null;
        var left = Pomodoro.Remaining(_shared.FocusNow());
        if (FocusPageVisible) return FocusClock.NextSecondIn(left);
        return _sm.Mode is NotchMode.Idle or NotchMode.Peek or NotchMode.Working or NotchMode.Minimal
            ? FocusClock.NextCompactIn(left) : null;
    }

    // ---------------------------------------------------------------- textos y frame

    /// <summary>La tarea enlazada que se ve (de la lista de esta ventana, sin copiar el almacén por frame); null sin enlace, sin
    /// el Pro, sin tareas o si ya no está.</summary>
    private NotchTask? FocusTaskShown()
    {
        if (!_options.Tasks || FocusLocked || Pomodoro.TaskId is not { } id) return null;
        for (int i = 0; i < _taskItems.Count; i++)
            if (_taskItems[i].Id == id) return _taskItems[i];
        return null;
    }

    /// <summary>Los textos de ahora: los de la caché mientras no cambie lo que se ve. El segundo solo cuenta con la página
    /// (<paramref name="page"/>) a la vista: cerrado, el texto cambia con el minuto y no se rehace cada segundo.</summary>
    private FocusTexts FocusTextsNow(FocusNow now, bool page)
    {
        var t = Pomodoro;
        var left = t.Remaining(now);
        var task = FocusTaskShown();
        var key = new FocusTextKey(t.Version, page ? FocusClock.ShownSeconds(left) : -1, FocusClock.Compact(left), _painter.Strings,
            DateOnly.FromDateTime(now.Local), task, _taskItems, FocusLocked);
        if (_focusTexts is not null && key == _focusTextsKey) return _focusTexts;
        _focusTextsKey = key;
        var week = t.Week(now.Local);
        _focusWeek = week.Select(d => d.Focuses).ToArray();
        _focusTaskColor = task?.Color ?? -1;
        bool anyPending = _taskItems.Any(x => !x.Done);
        _focusTexts = FocusText.Build(t.Phase, t.Run, t.Finished, left, t.Cycle, t.Durations.LongEvery, t.Today(now.Local), week,
            task is { } x ? new FocusTaskInfo(x.Title, x.Color, x.Focuses) : null, anyPending, _painter.Strings);
        return _focusTexts;
    }

    /// <summary>Lo que se pinta del pomodoro en este frame: la página (en el expandido), el mini anillo (si la maquetación que SE
    /// PINTA lo lleva, o en Mínimo) o el bote del aviso del fin de fase; null si nada de eso se ve.</summary>
    private FocusView? FocusFrame(NotchMode shown)
    {
        bool page = shown == NotchMode.Expanded && FocusEnabled
            && (_pageAnim.Current == NotchPage.Focus || _pageAnim.Previous == NotchPage.Focus);
        bool face = FocusMini && (shown == NotchMode.Minimal
            || (shown is NotchMode.Idle or NotchMode.Peek or NotchMode.Working && (_anim.ShownLayout & LayoutFocus) != 0));
        bool alert = shown == NotchMode.Reminder && _anim.ShownAlert?.Kind == NotchAlertKind.Focus;
        if (!page && !face && !alert) return null;
        var now = _shared.FocusNow();
        var texts = FocusTextsNow(now, page);
        var t = Pomodoro;
        var left = t.Remaining(now);
        float progress = FocusClock.Progress(TimeSpan.FromSeconds(FocusClock.ShownSeconds(left)), t.Length);
        float compact = FocusClock.Progress(FocusClock.CompactShown(left), t.Length);
        return new FocusView(texts, t.Phase, t.Run, t.Finished, progress, compact, t.Cycle, t.Durations.LongEvery, _focusTaskColor,
            _focusWeek, FocusLocked, _interactive, face, _focusHover.Visible ? _focusHoverHit : default, (float)_focusHover.Intensity,
            _focusHeld, (float)_focusPop.Value);
    }

    /// <summary>¿Se mueve algo del pomodoro (realce o bote)?</summary>
    private bool FocusAnimating => _focusHover.IsAnimating || !_focusPop.IsSettled;

    /// <summary>Avanza el realce y el bote (desde <see cref="Frame"/>); el aviso del fin de fase bota cuando su contenido ya se
    /// ve (no al entrar en la cola: tras otro aviso, o mientras la forma se abre, nadie lo vería).</summary>
    private void StepFocus(double dt)
    {
        if (_anim.ShownAlert is { Kind: NotchAlertKind.Focus } shown && _anim.ContentAlpha >= 0.5f && !ReferenceEquals(shown, _focusPopped))
        {
            _focusPopped = shown;
            StartFocusPop();
        }
        if (dt <= 0) return;
        _focusHover.Advance(dt);
        _focusPop.Advance(dt);
    }

    // ---------------------------------------------------------------- página: ratón

    private void ResetFocusPointer()
    {
        _focusHeld = default;
        if (_focusHover.Key is not null || _focusHover.Visible) _focusHover.Reset();
    }

    /// <summary>La página Foco pasa a verse: una oferta muy vieja caduca y el puntero, de cero.</summary>
    private void EnterFocusPage()
    {
        _shared.FocusRefresh();
        ResetFocusPointer();
    }

    /// <summary>Control de la página Foco bajo <paramref name="p"/>; nada si la página no se pinta quieta.</summary>
    private FocusHit FocusHitAt(Point p) => !FocusPageShown ? default
        : FocusLayout.HitTest(new PointF(p.X + 0.5f, p.Y + 0.5f), FocusLayout.Compute(MusicInner(), _scale), FocusLocked);

    /// <summary>Realce del control bajo el cursor (HoverIn al entrar, Rápido al salir; reduce-motion = salto).</summary>
    private void FocusHover(Point p)
    {
        var hit = FocusHitAt(p);
        if (hit.Control == FocusControl.None)
        {
            if (_focusHover.Key is null) return;
            _focusHover.Leave(snap: _options.ReduceMotion);
        }
        else
        {
            string key = hit.Control.ToString();
            if (key == _focusHover.Key) return;
            _focusHoverHit = hit;
            _focusHover.Enter(key, RectangleF.Empty, snap: _options.ReduceMotion);
        }
        Frame();
        Pump();
    }

    /// <summary>WM_LBUTTONDOWN: recuerda el control pulsado (y pinta la pulsación, al instante).</summary>
    private void FocusPress(Point p)
    {
        var hit = FocusHitAt(p);
        if (hit == _focusHeld) return;
        _focusHeld = hit;
        RenderFrame();
    }

    /// <summary>
    /// Clic al SOLTAR en la página Foco, sobre el MISMO control que se pulsó: el botón principal, saltar, reiniciar, la tarea
    /// (la siguiente pendiente) o, sin el Pro, la tarjeta. Soltar fuera tras pulsar un control lo cancela sin cerrar el
    /// expandido. En una ventana que no es la interactiva, pide serlo (la tarjeta del Pro sí se abre). <c>false</c> si el clic
    /// no era de la página (clic normal).
    /// </summary>
    private bool FocusClick(Point p)
    {
        var held = _focusHeld;
        _focusHeld = default;
        if (!FocusPageShown) return false;
        var hit = FocusHitAt(p);
        if (hit.Control == FocusControl.None) return held.Control != FocusControl.None;
        if (!_interactive && hit.Control != FocusControl.ProChip)
        {
            PromoteRequested?.Invoke(this);
            return true;
        }
        if (hit != held) return true;
        switch (hit.Control)
        {
            case FocusControl.Primary: _shared.FocusToggle(); break;
            case FocusControl.Skip: _shared.FocusSkip(); break;
            case FocusControl.Reset: _shared.FocusReset(); break;
            case FocusControl.Task: CycleFocusTask(); break;
            case FocusControl.ProChip:
                OpenProCard();
                return true;
        }
        Frame();
        Pump();
        return true;
    }

    /// <summary>La tarea enlazada pasa a la siguiente pendiente (ninguna → la primera → … → la última → ninguna). Sin tareas
    /// pendientes, a la página Tareas para añadir una.</summary>
    private void CycleFocusTask()
    {
        var pending = _taskItems.Where(t => !t.Done).Select(t => t.Id).ToList();
        if (pending.Count == 0)
        {
            if (TasksEnabled) GoToPage(NotchPage.Tasks);
            return;
        }
        _shared.FocusLink(FocusTasks.Next(pending, Pomodoro.TaskId));
    }

    /// <summary>A la página <paramref name="page"/> del carrusel, con su cruce.</summary>
    private void GoToPage(NotchPage page)
    {
        if (!_pages.Contains(page) || _carousel.Current == page) return;
        int from = _carousel.Index;
        _carousel.GoTo(page);
        PageChanged(_carousel.Index > from ? 1 : -1);
    }

    /// <summary>
    /// Clic en la vista previa con el mini anillo (quieta y entera, sin avisos): en la mitad del pomodoro abre el expandido en
    /// la página Foco; en la del anillo de 5 h, en la página Uso (como un clic en los anillos). <c>false</c> si no era el caso.
    /// </summary>
    private bool FocusRingClick(Point p)
    {
        if (_proCardOpen || _anim.ContentAlpha < 1f || _sm.Mode != NotchMode.Peek || _anim.ShownMode != NotchMode.Peek
            || (_anim.ShownLayout & LayoutFocus) == 0 || _sm.ActiveAlert is not null || _hub.Sessions.Any(s => s.Phase.IsActive()))
            return false;
        var pill = NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface);
        bool focusHalf = p.X + 0.5f < pill.Left + pill.Width / 2f;
        if (focusHalf ? !FocusEnabled : !UsageEnabled) return false;
        _sm.OnClick(DateTime.UtcNow);   // abre el expandido
        if (focusHalf) ShowFocusPage();
        else ShowUsagePage(QuotaFocus.Five);
        return true;
    }

    /// <summary>Abre la página Foco: con el expandido ya abierto, con su cruce; si se está abriendo, de golpe y sin que Sync lo
    /// devuelva a Claude.</summary>
    private void ShowFocusPage()
    {
        if (!FocusEnabled) return;
        if (GoToRequestedPage(NotchPage.Focus)) EnterFocusPage();
    }

    // ---------------------------------------------------------------- aviso del fin de fase

    /// <summary>¿Acepta clics el botón del aviso a la vista? Armado (400 ms), forma quieta y lo pintado = lo de la máquina.</summary>
    private bool FocusAlertLive(DateTime now) => _interactive && _sm.FocusAlertActionable && _sm.ReminderArmed(now)
        && _anim.ShapeSettled && _anim.ShownAlert is { Kind: NotchAlertKind.Focus };

    private Rectangle FocusAlertButtonRect() =>
        NotchGeometry.FocusAlertButton(NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface), _scale);

    /// <summary>WM_LBUTTONDOWN: ¿se pulsó el botón del aviso? (armado y de verdad bajo el cursor).</summary>
    private void FocusAlertPress(Point p)
    {
        bool over = HitTest(p) && FocusAlertLive(DateTime.UtcNow) && FocusAlertButtonRect().Contains(p);
        if (over == _focusAlertHeld) return;
        _focusAlertHeld = over;
        RenderFrame();
    }

    /// <summary>
    /// Clic al SOLTAR en el aviso del fin de fase: sobre el botón pulsado, empieza la fase que toca (y el aviso sale de todas las
    /// ventanas con el cambio); en el resto del aviso, ya armado, lo descarta en todas. <c>false</c> si no hay aviso del fin de
    /// fase a la vista con su botón (clic normal).
    /// </summary>
    private bool FocusAlertClick(Point p)
    {
        bool held = _focusAlertHeld;
        _focusAlertHeld = false;
        if (!_interactive || !_sm.FocusAlertActionable) return false;
        if (!FocusAlertLive(DateTime.UtcNow)) return true;   // recién aparecido o en pleno fundido: el clic no cuenta
        if (!FocusAlertButtonRect().Contains(p))
        {
            if (!held) _shared.ClearFocusAlert();   // soltar fuera tras pulsar el botón solo lo cancela
            return true;
        }
        if (held) _shared.FocusStart();
        return true;
    }

    /// <summary>Armado del botón del aviso que SE PINTA (sin armar mientras la forma se mueve).</summary>
    private float FocusAlertArmShown()
    {
        if (_sm.ActiveAlert is not { Kind: NotchAlertKind.Focus } || _anim.ShownAlert is not { Kind: NotchAlertKind.Focus }) return 0f;
        float arm = _sm.ReminderArmProgress(DateTime.UtcNow);
        return arm >= 1f && !_anim.ShapeSettled ? ArmWaitingShape : arm;
    }

    /// <summary>El botón del aviso pulsado a pintar.</summary>
    private PermissionVerdict FocusAlertHeldShown =>
        _sm.FocusAlertActionable && _focusAlertHeld ? PermissionVerdict.Allow : PermissionVerdict.None;

    /// <summary>El botón del aviso armándose (relleno animado con el timer del spinner).</summary>
    private bool FocusAlertArmingOn => _interactive && !_options.ReduceMotion && _sm.FocusAlertActionable
        && !_sm.ReminderArmed(DateTime.UtcNow);

    /// <summary>De qué están hechos los textos del pomodoro (se comparan por valor, sin reservas por frame).</summary>
    private readonly record struct FocusTextKey(int Version, int Seconds, (int Value, bool Minutes) Compact, Strings? Strings,
        DateOnly Day, NotchTask? Task, IReadOnlyList<NotchTask>? Items, bool Locked);

    // ---------------------------------------------------------------- tests y capturas

    /// <summary>Lo que pinta del pomodoro ahora mismo (tests).</summary>
    internal FocusView? FocusViewNow => FocusFrame(_anim.ShownMode);

    private void DisposeFocus()
    {
        _shared.FocusChanged -= OnFocusChanged;
        _shared.FocusEnded -= OnFocusEnded;
        _shared.FocusAlertCleared -= OnFocusAlertCleared;
    }
}
