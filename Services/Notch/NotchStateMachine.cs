using System.Globalization;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Máquina de modos del notch. PURA: cada entrada recibe el instante por parámetro (regla del repo) y
/// no hay timers dentro; el llamador pregunta <see cref="NextWakeIn"/> y llama a <see cref="Tick"/>
/// entonces. En reposo <see cref="NextWakeIn"/> es <c>null</c>: nada que despertar (0 % CPU).
///
/// <para>Precedencia del modo (de más a menos):</para>
/// <list type="number">
/// <item><b>Hidden</b>: pantalla completa con <see cref="NotchSettings.HideInFullscreen"/>. Mientras
/// tanto la cola no se consulta: los avisos que lleguen esperan y su reloj arranca al volver.</item>
/// <item><b>Permiso con botones</b> (F2, <see cref="NotchAlert.RequestId"/>): manda incluso sobre el expandido, para
/// que ir con el ratón hacia los botones no lo convierta en el panel.</item>
/// <item><b>Expanded</b>: el aviso activo se integra dentro (<see cref="ActiveAlert"/>), no lo tapa.</item>
/// <item><b>Aviso</b> de la cola: Permiso &gt; Aviso 95 &gt; Terminado &gt; Recordatorio (F5 · B5) &gt; fin de fase del pomodoro &gt;
/// HUD 80 &gt; aviso de la app.</item>
/// <item><b>Peek</b>: puntero encima (o en la gracia de salida) sin expandir.</item>
/// <item><b>Working</b> si alguna sesión procesa/compacta; si no <b>Idle</b> (o <b>Minimal</b>).</item>
/// </list>
/// Hover: entrar ⇒ Peek; tras <see cref="NotchTiming.HoverExpand"/> continuo ⇒ Expanded (o solo con
/// clic si <see cref="NotchSettings.OpenOnClick"/>). Salir ⇒ contrae tras <see cref="NotchTiming.CollapseGrace"/>
/// salvo que vuelva a entrar antes.
///
/// <para>Encoger solo: con <see cref="NotchSettings.AutoMinimalAfter"/>, tras ese rato en reposo (sin sesión
/// trabajando, sin avisos, sin puntero) el reposo pasa a <b>Minimal</b>. Cualquier actividad lo despierta y el
/// reloj vuelve a empezar.</para>
///
/// <para>Botones de Permiso (F2): no aceptan clics hasta <see cref="NotchTiming.PermissionArm"/> después de que ESA
/// petición se muestre (si se oculta y vuelve, o cambia su texto, se arma otra vez). Sin acción por defecto. Tras un clic,
/// <see cref="PermissionAnswer"/> se queda <see cref="NotchTiming.PermissionConfirm"/> a la vista (aunque Claude ya
/// haya seguido) y luego el aviso sale. Si la petición se cierra sin respuesta (caducó, el helper se fue), el aviso
/// sigue pero en solo lectura.</para>
///
/// <para>Recordatorios de tareas (F5 · B5): <see cref="OnTaskReminder"/> encola un aviso persistente (sesión
/// <c>task:&lt;id&gt;</c>) que sale con <see cref="ClearTaskReminder"/> («Hecha», «+10 min» o descartarlo). Sus botones se
/// arman como los del Permiso (<see cref="ReminderArmed"/>) y, como él, manda sobre el expandido (salvo editando). Mientras se teclea una tarea, <see cref="SetHold"/> mantiene el
/// expandido abierto aunque el puntero salga o haya un clic suelto.</para>
///
/// <para>Fin de fase del pomodoro: <see cref="OnFocusEnd"/> encola un aviso con un botón («Empezar descanso» / «Empezar foco»)
/// que sale solo a los <see cref="NotchTiming.FocusEnd"/> o con <see cref="ClearFocusAlert"/>. Se enseña como el recordatorio
/// (modo <see cref="NotchMode.Reminder"/>, su botón armado igual, <see cref="FocusAlertActionable"/>) y, como él, manda sobre el
/// expandido que se abre con él a la vista (ir con el ratón al botón no debe abrir el panel), salvo editando una tarea. Si llega
/// con el panel YA abierto, no lo cierra (pasa cada 5–25 minutos): se integra arriba hasta que el panel se cierra.</para>
/// </summary>
public sealed class NotchStateMachine
{
    private readonly NotchSettings _settings;
    private readonly NotchEventQueue _queue = new();

    private bool _anyActive;        // alguna sesión Processing/Compacting
    private bool _fullscreen;
    private bool _pointerInside;
    private bool _peek;             // puntero encima o en la gracia de salida
    private bool _expanded;
    private DateTime? _expandAt;    // hover pendiente de expandir
    private DateTime? _collapseAt;  // gracia de salida en curso
    private bool _needsTick;        // OnFullscreen no trae instante: el siguiente Tick recalcula el aviso
    private DateTime? _quietSince;  // desde cuándo está en reposo sin actividad (encoger solo)
    private bool _autoMinimal;      // encogido a la rayita por inactividad
    private string? _armRequestId;  // petición cuyos botones se están armando / armados (la que se ve)
    private string? _armContent;    // texto (título + detalle) con el que se armó: si cambia, se vuelve a armar
    private DateTime _armSince;     // desde cuándo se ve
    private Answer? _answer;        // clic dado: confirmación en pantalla hasta Until
    private bool _hold;             // edición en línea de una tarea (F5 · B5): el expandido no se cierra
    private DateTime? _pageHoldUntil; // cambio de página del usuario: salir no contrae hasta entonces (o hasta volver)
    private bool _pageShapeSettled;   // la forma de la página nueva ya no se mueve
    private bool _keepAwake;        // canción con letra en el notch pequeño: el reposo no se encoge solo
    private string? _remArmId;      // recordatorio cuyos botones se están armando / armados (el que se ve)
    private DateTime _remArmSince;
    private bool _focusOverPanel;   // el fin de fase llegó con el panel ya abierto: se integra arriba, no lo cierra

    private sealed record Answer(string RequestId, string SessionId, PermissionVerdict Verdict, DateTime Until);

    public NotchStateMachine(NotchSettings s)
    {
        _settings = s ?? throw new ArgumentNullException(nameof(s));
        Mode = BaseMode();
    }

    public NotchMode Mode { get; private set; }

    /// <summary>Vista previa con el reposo debajo (sin sesión trabajando ni la raya de Mínimo): la letra compacta sigue en
    /// ella y solo crece el anillo de la cuota (pedido del 29-oct).</summary>
    public bool PeekOverIdle => Mode == NotchMode.Peek && BaseMode() == NotchMode.Idle;

    /// <summary>El aviso vigente (también con el notch expandido, para integrarlo dentro). <c>null</c> si
    /// no hay ninguno o el notch está oculto.</summary>
    public NotchAlert? ActiveAlert { get; private set; }

    public void OnSignal(ClaudeSignal sig, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(sig);
        switch (sig)
        {
            case PermissionPending p:
            {
                // El hook de sesión no trae request_id. Si ya hay un aviso del helper (con botones), se conserva TAL
                // CUAL: los botones contestan esa petición y lo que se ve tiene que ser lo que se aprueba. No se
                // mezcla el texto del hook con el id del helper: con dos peticiones en la misma sesión (tools o
                // subagentes en paralelo) el hook puede hablar de otra (B) mientras los botones siguen atados a la A.
                // Llegan por pipes distintos y en cualquier orden; si el helper llega después, sustituye al del hook.
                if (p.RequestId is null && _queue.Find(NotchAlertKind.Permission, p.SessionId) is { RequestId: not null })
                    break;
                // Otra petición de la sesión que se está confirmando sustituye al aviso en su sitio: la confirmación
                // («Permitido» / «Denegado») es de la anterior y no puede pintarse sobre el texto de esta, que no se
                // ha contestado. La respuesta ya está en el broker; aquí solo se acaba antes lo que se ve.
                if (p.RequestId is not null && _answer is { } prev
                    && string.Equals(prev.SessionId, p.SessionId, StringComparison.Ordinal) && prev.RequestId != p.RequestId)
                    _answer = null;
                _queue.Enqueue(new NotchAlert(NotchAlertKind.Permission, p.SessionId, p.ProjectName,
                    ToolDetail(p.Tool, p.Preview), now, NotchAlert.DefaultDuration(NotchAlertKind.Permission), p.RequestId));
                break;
            }
            case PermissionCleared c:
                if (!Answering(c.SessionId)) _queue.Remove(NotchAlertKind.Permission, c.SessionId);
                break;
            case PermissionRequestClosed rc:
                // La respondida se cierra sola al acabar la confirmación; otra cualquiera pierde los botones.
                if (_answer?.RequestId != rc.RequestId) DropButtons(rc.SessionId, rc.RequestId);
                break;
            case TaskCompleted t:
                _queue.Enqueue(new NotchAlert(NotchAlertKind.Completed, t.SessionId, t.ProjectName, null, now,
                    NotchAlert.DefaultDuration(NotchAlertKind.Completed)));
                break;
            case SessionGone g:
                // Un permiso de una sesión cerrada ya no se puede contestar. Su «Terminado» sigue valiendo.
                if (!Answering(g.SessionId)) _queue.Remove(NotchAlertKind.Permission, g.SessionId);
                break;
            // ToolActivity: no cambia el modo; el texto de «Trabajando» lo pinta el renderer desde el hub.
        }
        Recompute(now);
    }

    /// <summary>El aviso vigente es un Permiso con botones y aún no se ha contestado.</summary>
    public bool PermissionActionable =>
        Mode == NotchMode.Permission && ActiveAlert?.RequestId is not null && _answer is null;

    /// <summary>Relleno del armado de los botones en [0, 1] (1 = armados); 0 si no hay botones a la vista.</summary>
    public float PermissionArmProgress(DateTime now)
    {
        if (_armRequestId is null) return 0f;
        if (_answer is not null) return 1f;
        return (float)Math.Clamp((now - _armSince).TotalMilliseconds / NotchTiming.PermissionArm.TotalMilliseconds, 0, 1);
    }

    /// <summary>¿Los botones del Permiso a la vista ya aceptan clics?</summary>
    public bool PermissionArmed(DateTime now) => _armRequestId is not null && now - _armSince >= NotchTiming.PermissionArm;

    /// <summary>Respuesta en confirmación («Permitido» / «Denegado»); <see cref="PermissionVerdict.None"/> si no hay.</summary>
    public PermissionVerdict PermissionAnswer => _answer?.Verdict ?? PermissionVerdict.None;

    /// <summary>
    /// Clic en Permitir (<paramref name="allow"/>) o Denegar. Devuelve el <c>RequestId</c> a contestar en el broker, o
    /// <c>null</c> si el clic no vale: sin botones a la vista, aún sin armar o ya contestado.
    /// </summary>
    public string? OnPermissionButton(bool allow, DateTime now)
    {
        if (_answer is not null || Mode != NotchMode.Permission || ActiveAlert is not { RequestId: { } id } a
            || _armRequestId != id || !PermissionArmed(now))
            return null;
        _answer = new Answer(id, a.SessionId, allow ? PermissionVerdict.Allow : PermissionVerdict.Deny,
            now + NotchTiming.PermissionConfirm);
        Recompute(now);
        return id;
    }

    /// <summary>
    /// Otra ventana del notch (un notch por monitor) contestó la petición <paramref name="requestId"/> de esa sesión: aquí,
    /// que la enseñaba en solo lectura, sale la misma confirmación («Permitido» / «Denegado») durante
    /// <see cref="NotchTiming.PermissionConfirm"/> y luego el aviso, como en la que tenía los botones. Si ya no está en la cola
    /// (u otra petición la sustituyó), nada.
    /// </summary>
    public void OnPermissionAnsweredElsewhere(string sessionId, string requestId, PermissionVerdict verdict, DateTime now)
    {
        if (verdict == PermissionVerdict.None || _answer is not null) return;
        if (_queue.Find(NotchAlertKind.Permission, sessionId) is not { RequestId: { } id } || id != requestId) return;
        _answer = new Answer(requestId, sessionId, verdict, now + NotchTiming.PermissionConfirm);
        Recompute(now);
    }

    /// <summary>El broker no aceptó la respuesta (caducada o ya cerrada): fuera la confirmación y los botones.</summary>
    public void OnPermissionRejected(string requestId, DateTime now)
    {
        if (_answer is { } a && a.RequestId == requestId)
        {
            _answer = null;
            DropButtons(a.SessionId, requestId);
        }
        Recompute(now);
    }

    /// <summary>
    /// La petición <paramref name="requestId"/> no se puede contestar en el notch (su texto no cabe ENTERO en las líneas
    /// del Permiso): pierde los botones y queda en solo lectura, sin moverse de la cola. Quien llama la cierra en el broker.
    /// </summary>
    public void OnPermissionUnanswerable(string requestId, DateTime now)
    {
        if (_answer is { } a && a.RequestId == requestId) _answer = null;
        if (ActiveAlert is { Kind: NotchAlertKind.Permission } cur && cur.RequestId == requestId)
            DropButtons(cur.SessionId, requestId);
        Recompute(now);
    }

    /// <summary>Tipo de aviso de un cruce de cuota: ≥ 95 % es el aviso rojo; por debajo, el HUD discreto.
    /// Público para que el sonido decida con la misma regla que la máquina.</summary>
    public static NotchAlertKind QuotaAlertKind(int threshold) =>
        threshold >= 95 ? NotchAlertKind.QuotaAlert : NotchAlertKind.QuotaHud;

    public void OnQuotaCrossing(QuotaCrossing c, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(c);
        string window = c.Window == QuotaWindowKind.FiveHour ? "5h" : "7d";
        string id = "quota:" + window;
        var kind = QuotaAlertKind(c.Threshold);
        // El aviso 95 deja obsoleto un HUD 80 de la misma ventana que siga en cola.
        if (kind == NotchAlertKind.QuotaAlert) _queue.Remove(NotchAlertKind.QuotaHud, id);
        _queue.Enqueue(new NotchAlert(kind, id, window,
            Math.Round(c.Pct).ToString(CultureInfo.InvariantCulture) + "%", now, NotchAlert.DefaultDuration(kind)));
        Recompute(now);
    }

    /// <summary>Recordatorio de una tarea que vence (F5 · B5): aviso persistente en la cola. <paramref name="sessionId"/> =
    /// <c>TaskAlerts.SessionId</c>; el mismo id sustituye al anterior (no se duplica).</summary>
    public void OnTaskReminder(string sessionId, string title, DateTime now)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        _queue.Enqueue(new NotchAlert(NotchAlertKind.Reminder, sessionId, title ?? "", null, now,
            NotchAlert.DefaultDuration(NotchAlertKind.Reminder)));
        Recompute(now);
    }

    /// <summary>Quita el recordatorio de esa tarea (contestado, descartado, o la tarea ya no existe / está hecha).</summary>
    public void ClearTaskReminder(string sessionId, DateTime now)
    {
        _queue.Remove(NotchAlertKind.Reminder, sessionId);
        Recompute(now);
    }

    /// <summary>Aviso de la app (<see cref="NotchAlertKind.Notice"/>, p. ej. «tu prueba de Pro ha terminado»): el último en
    /// prioridad, sale solo a los <see cref="NotchTiming.Notice"/> y no suena. <paramref name="id"/> lo identifica (el mismo
    /// sustituye al anterior).</summary>
    public void OnNotice(string id, DateTime now)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        _queue.Enqueue(new NotchAlert(NotchAlertKind.Notice, id, id, null, now, NotchAlert.DefaultDuration(NotchAlertKind.Notice)));
        Recompute(now);
    }

    /// <summary>Id (sesión) del aviso del fin de fase del pomodoro: uno solo, el nuevo sustituye al anterior.</summary>
    public const string FocusAlertId = "focus";

    /// <summary>Acabó una fase del pomodoro: aviso con su botón en la cola. Título y detalle son DATOS (las fases, por su nombre
    /// en el enum): el painter los compone en el idioma vigente.</summary>
    public void OnFocusEnd(string endedPhase, string nextPhase, DateTime now)
    {
        _focusOverPanel = _expanded;
        _queue.Enqueue(new NotchAlert(NotchAlertKind.Focus, FocusAlertId, endedPhase ?? "", nextPhase, now,
            NotchAlert.DefaultDuration(NotchAlertKind.Focus)));
        Recompute(now);
    }

    /// <summary>Quita el aviso del fin de fase (se empezó la fase, se descartó o se tocó el pomodoro en otra ventana).</summary>
    public void ClearFocusAlert(DateTime now)
    {
        _queue.Remove(NotchAlertKind.Focus, FocusAlertId);
        Recompute(now);
    }

    /// <summary>El aviso vigente es el fin de fase del pomodoro, a la vista con su botón.</summary>
    public bool FocusAlertActionable => Mode == NotchMode.Reminder && ActiveAlert?.Kind == NotchAlertKind.Focus;

    /// <summary>Quita ese aviso de la app (se pulsó).</summary>
    public void ClearNotice(string id, DateTime now)
    {
        _queue.Remove(NotchAlertKind.Notice, id);
        Recompute(now);
    }

    /// <summary>
    /// Abre el expandido sin hover ni clic (la tarjeta del Pro pedida desde la bandeja). Con el puntero fuera se cierra tras
    /// <paramref name="linger"/> (null = la gracia de siempre); si el puntero entra, se queda como con el hover, y al salir
    /// contrae tras la gracia de siempre.
    /// </summary>
    public void OpenExpanded(DateTime now, TimeSpan? linger = null)
    {
        _expanded = true;
        _peek = true;
        _expandAt = null;
        _collapseAt = _pointerInside ? null : now + (linger ?? NotchTiming.CollapseGrace);
        Recompute(now);
    }

    /// <summary>El aviso vigente es un recordatorio a la vista (con sus botones).</summary>
    public bool ReminderActionable => Mode == NotchMode.Reminder && ActiveAlert?.Kind == NotchAlertKind.Reminder;

    /// <summary>¿Los botones del recordatorio (o el del fin de fase del pomodoro) a la vista ya aceptan clics? (mismo plazo que el
    /// Permiso).</summary>
    public bool ReminderArmed(DateTime now) => _remArmId is not null && now - _remArmSince >= NotchTiming.PermissionArm;

    /// <summary>Relleno del armado de los botones del recordatorio en [0, 1]; 0 si no hay ninguno a la vista.</summary>
    public float ReminderArmProgress(DateTime now) => _remArmId is null ? 0f
        : (float)Math.Clamp((now - _remArmSince).TotalMilliseconds / NotchTiming.PermissionArm.TotalMilliseconds, 0, 1);

    /// <summary>
    /// Edición en línea de una tarea (F5 · B5): con <paramref name="on"/> el expandido se abre (si no lo estaba) y no se
    /// cierra ni al sacar el puntero ni con un clic suelto. Al soltarlo, si el puntero está fuera, contrae tras la gracia de
    /// siempre. Ocultarse en pantalla completa lo suelta.
    /// </summary>
    /// <summary>
    /// Mantener despierto (pedido del 29-oct): mientras suena una canción con su letra en el notch pequeño, el reposo no se
    /// encoge a la raya (<see cref="NotchSettings.AutoMinimalAfter"/>). Al soltarlo el reloj de inactividad empieza de
    /// cero; si ya estaba encogido, volver a pedirlo lo despierta. No toca el ajuste «Minimalista» fijo.
    /// </summary>
    public void SetKeepAwake(bool on, DateTime now)
    {
        if (_keepAwake == on) return;
        _keepAwake = on;
        Recompute(now);
    }

    public void SetHold(bool on, DateTime now)
    {
        if (_hold == on) return;
        _hold = on;
        if (on)
        {
            _expanded = true;
            _peek = true;
            _expandAt = null;
            _collapseAt = null;
        }
        else if (!_pointerInside && (_peek || _expanded)) _collapseAt = now + NotchTiming.CollapseGrace;
        Recompute(now);
    }

    public void OnSessions(IReadOnlyList<SessionState> s, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(s);
        _anyActive = s.Any(x => x.Phase.IsActive());
        Recompute(now);
    }

    /// <summary>
    /// El usuario cambió de página del expandido con el ratón (punto, rueda, flecha…). La página nueva puede ser más pequeña
    /// y dejar el cursor fuera: eso NO es «el ratón se ha ido». Hasta que el puntero vuelva a entrar en la forma nueva ya
    /// asentada (<see cref="OnPageShapeSettled"/>) salir no abre la gracia; si se queda fuera, el expandido se cierra a los
    /// <see cref="NotchTiming.PageChangeHold"/> del cambio. Fuera del expandido, nada.
    /// </summary>
    public void OnUserPageChange(DateTime now)
    {
        if (!_expanded) return;
        _pageHoldUntil = now + NotchTiming.PageChangeHold;
        _pageShapeSettled = false;
        if (_collapseAt is not null && !_hold) _collapseAt = _pageHoldUntil;   // ya fuera: espera al plazo, no a la gracia
        Recompute(now);
    }

    /// <summary>La forma de la página nueva dejó de moverse (la zona de hover vuelve a ser solo ella). Si el puntero
    /// sigue dentro, la protección del cambio de página acaba: salir después es el cierre normal.</summary>
    public void OnPageShapeSettled(DateTime now)
    {
        _pageShapeSettled = true;
        if (_pointerInside) _pageHoldUntil = null;
        Recompute(now);
    }

    private bool PageHeld(DateTime now) => _pageHoldUntil is { } u && now < u;

    public void OnPointerEnter(DateTime now)
    {
        _pointerInside = true;
        _peek = true;
        _collapseAt = null;
        if (_pageShapeSettled) _pageHoldUntil = null;   // de vuelta en la forma nueva: la próxima salida es la normal
        if (!_expanded && !_settings.OpenOnClick) _expandAt ??= now + NotchTiming.HoverExpand;
        Recompute(now);
    }

    public void OnPointerLeave(DateTime now)
    {
        _pointerInside = false;
        _expandAt = null;
        if ((_peek || _expanded) && !_hold)
            _collapseAt = _expanded && PageHeld(now) ? _pageHoldUntil : now + NotchTiming.CollapseGrace;
        Recompute(now);
    }

    /// <summary>
    /// Arrastrar algo encima del notch (F5 · B4): se abre YA, sin esperar al hover ni pedir clic (con
    /// <see cref="NotchSettings.OpenOnClick"/> tampoco: mientras se arrastra no se puede hacer clic), y despierta el
    /// encogido. Cuenta como puntero encima: si el arrastre sale sin soltar, llega <see cref="OnPointerLeave"/> y contrae
    /// tras la gracia de siempre; si se suelta, sigue abierto con el puntero encima.
    /// </summary>
    public void OnDragEnter(DateTime now)
    {
        _pointerInside = true;
        _peek = true;
        _expanded = true;
        _expandAt = null;
        _collapseAt = null;
        Recompute(now);
    }

    public void OnClick(DateTime now)
    {
        _expandAt = null;
        if (_expanded && _hold) { Recompute(now); return; }   // editando una tarea: no se cierra
        if (_expanded)
        {
            _expanded = false;              // segundo clic: contrae (a Peek si el puntero sigue encima)
            _peek = _pointerInside;
            _collapseAt = null;
        }
        else
        {
            _expanded = true;
            _peek = true;
        }
        Recompute(now);
    }

    public void OnFullscreen(bool on)
    {
        bool wasHidden = IsHidden;
        _fullscreen = on;
        if (on && _settings.HideInFullscreen)
        {
            // Al ocultarse se olvida el hover: al volver no debe reaparecer expandido.
            _pointerInside = _peek = _expanded = _hold = false;
            _expandAt = _collapseAt = null;
        }
        // Sin instante no se consulta la cola aquí: NextWakeIn devuelve 0 para que el llamador haga
        // Tick enseguida (al salir de pantalla completa pueden esperar avisos llegados mientras tanto).
        if (IsHidden) { ActiveAlert = null; _armRequestId = null; _armContent = null; _remArmId = null; }
        Mode = ComputeMode(ActiveAlert);
        if (wasHidden && !IsHidden) _needsTick = true;
    }

    /// <summary>Cuánto falta para el próximo cambio programado (hover, gracia o caducidad de un aviso);
    /// <c>null</c> si no hay nada pendiente o el notch está oculto.</summary>
    public TimeSpan? NextWakeIn(DateTime now)
    {
        if (IsHidden) return null;
        if (_needsTick) return TimeSpan.Zero;
        DateTime? next = Min(Min(_expandAt, _collapseAt), _queue.CurrentExpiresAtUtc);
        if (!_autoMinimal && _quietSince is { } q && _settings.AutoMinimalAfter is { } after)
            next = Min(next, q + after);
        if (_armRequestId is not null && _answer is null && !PermissionArmed(now))
            next = Min(next, _armSince + NotchTiming.PermissionArm);   // repintar al armarse
        if (_answer is { } ans) next = Min(next, ans.Until);
        if (_remArmId is not null && !ReminderArmed(now)) next = Min(next, _remArmSince + NotchTiming.PermissionArm);
        if (next is null) return null;
        var d = next.Value - now;
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }

    public void Tick(DateTime now) => Recompute(now);

    private bool IsHidden => _fullscreen && _settings.HideInFullscreen;

    private void Recompute(DateTime now)
    {
        if (_expandAt is { } e && now >= e) { _expandAt = null; _expanded = true; }
        if (_collapseAt is { } c && now >= c && !_hold) { _collapseAt = null; _expanded = false; _peek = false; }
        if (!_expanded || (_pageHoldUntil is { } h && now >= h)) _pageHoldUntil = null;   // contraído o caducado

        if (_answer is { } ans && now >= ans.Until)
        {
            // Fin de la confirmación: sale el Permiso respondido (si sigue siendo esa petición).
            _answer = null;
            if (_queue.Find(NotchAlertKind.Permission, ans.SessionId) is { } done && done.RequestId == ans.RequestId)
                _queue.Remove(NotchAlertKind.Permission, ans.SessionId);
        }

        _needsTick = false;
        if (!_expanded) _focusOverPanel = false;   // con el panel cerrado, el aviso del fin de fase vuelve a mandar
        ActiveAlert = IsHidden ? null : _queue.Current(now);
        UpdateQuiet(now);
        Mode = ComputeMode(ActiveAlert);
        UpdateArming(now);
    }

    /// <summary>El armado cuenta desde que ESA petición se ve con ESE texto; si deja de verse o cambia lo que dice,
    /// se olvida (al volver, otra vez 400 ms): no se acepta un clic sobre algo que el usuario aún no ha podido leer.</summary>
    private void UpdateArming(DateTime now)
    {
        if (Mode == NotchMode.Permission && ActiveAlert is { RequestId: { } id } a)
        {
            string content = a.Title + "\n" + a.Detail;
            if (_armRequestId != id || !string.Equals(_armContent, content, StringComparison.Ordinal))
            {
                _armRequestId = id;
                _armContent = content;
                _armSince = now;
            }
        }
        else if (_answer is null) { _armRequestId = null; _armContent = null; }

        // Recordatorio (o fin de fase del pomodoro): se arma desde que ESE se ve (al volver de oculto o tras otro aviso, otra vez
        // 400 ms).
        if (Mode == NotchMode.Reminder && ActiveAlert is { Kind: NotchAlertKind.Reminder or NotchAlertKind.Focus } r)
        {
            if (_remArmId != r.SessionId) { _remArmId = r.SessionId; _remArmSince = now; }
        }
        else _remArmId = null;
    }

    private bool Answering(string sessionId) =>
        _answer is { } a && string.Equals(a.SessionId, sessionId, StringComparison.Ordinal);

    /// <summary>El Permiso de esa sesión con esa petición pasa a solo lectura, sin moverse de la cola.</summary>
    private void DropButtons(string sessionId, string requestId)
    {
        if (_queue.Find(NotchAlertKind.Permission, sessionId) is { } cur && cur.RequestId == requestId)
            _queue.ReplaceInPlace(cur, cur with { RequestId = null });
    }

    /// <summary>Reloj de inactividad: corre solo en reposo de verdad; cualquier actividad lo reinicia.</summary>
    private void UpdateQuiet(DateTime now)
    {
        bool quiet = !IsHidden && !_expanded && !_peek && ActiveAlert is null && !_anyActive && !_settings.Minimal
                     && !_keepAwake;
        if (!quiet || _settings.AutoMinimalAfter is not { } after)
        {
            _quietSince = null;
            _autoMinimal = false;
            return;
        }
        _quietSince ??= now;
        if (now - _quietSince.Value >= after) _autoMinimal = true;
    }

    private NotchMode ComputeMode(NotchAlert? alert)
    {
        if (IsHidden) return NotchMode.Hidden;
        if (alert is { Kind: NotchAlertKind.Permission, RequestId: not null }) return NotchMode.Permission;
        // Recordatorio (F5 · B5): también manda sobre el expandido (ir con el ratón a «Hecha» no debe abrir el panel), salvo
        // mientras se teclea una tarea: ahí se integra arriba y no corta la edición.
        if (alert is { Kind: NotchAlertKind.Reminder } && !_hold) return NotchMode.Reminder;
        if (alert is { Kind: NotchAlertKind.Focus } && !_hold && !(_expanded && _focusOverPanel)) return NotchMode.Reminder;
        if (_expanded) return NotchMode.Expanded;
        if (alert is not null) return alert.Kind switch
        {
            NotchAlertKind.Permission => NotchMode.Permission,
            NotchAlertKind.QuotaAlert => NotchMode.QuotaAlert,
            NotchAlertKind.Completed => NotchMode.Completed,
            NotchAlertKind.Reminder or NotchAlertKind.Focus => NotchMode.Reminder,
            NotchAlertKind.Notice => NotchMode.Completed,   // título + detalle, como «Terminado»
            _ => NotchMode.QuotaHud,
        };
        if (_peek) return NotchMode.Peek;
        return BaseMode();
    }

    private NotchMode BaseMode() =>
        _anyActive ? NotchMode.Working : _settings.Minimal || _autoMinimal ? NotchMode.Minimal : NotchMode.Idle;

    private static string? ToolDetail(string tool, string? preview)
    {
        bool hasTool = !string.IsNullOrEmpty(tool), hasPreview = !string.IsNullOrEmpty(preview);
        if (hasTool && hasPreview) return tool + ": " + preview;
        return hasTool ? tool : hasPreview ? preview : null;
    }

    private static DateTime? Min(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : (a < b ? a : b);
}
