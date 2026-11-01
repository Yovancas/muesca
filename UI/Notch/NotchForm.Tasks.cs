using System.Globalization;
using System.Runtime.InteropServices;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Notch.Platform;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Página Tareas y recordatorios del notch (F5 · B5). Archivo aparte para no mezclarse con el resto de la ventana.
/// <list type="bullet">
/// <item><b>Lista</b> (<see cref="TasksLayout"/>, la MISMA cuenta que se pinta): clic al SOLTAR en el círculo marca hecha
/// (o la desmarca), en el aspa la quita; rueda sobre la lista que no cabe.</item>
/// <item><b>Edición en línea</b> («+ Añadir»): <see cref="TaskEditFocus"/> quita <c>WS_EX_NOACTIVATE</c> un momento y trae el
/// notch al frente para recibir <c>WM_CHAR</c> (<see cref="TaskDraft"/>: máx. 80, Intro guarda, Esc cancela, Tab cambia a la
/// hora, Ctrl+V pega). Al acabar devuelve el foco y restaura NOACTIVATE. Mientras se edita, la máquina mantiene el
/// expandido abierto (<see cref="NotchStateMachine.SetHold"/>). Si el usuario se va a otra ventana, se cierra la página o
/// el notch se oculta, se guarda lo escrito si se puede (nunca se roba el foco de vuelta).</item>
/// <item><b>Recordatorios</b>: <see cref="TaskReminderClock"/> con UN temporizador de un solo disparo a la próxima hora
/// (nada en reposo sin tareas con hora) más un sondeo al volver de suspender, al cambiar la hora del sistema y al salir de
/// pantalla completa. El almacén, el reloj y los temporizadores son de <see cref="NotchShared"/> (uno para todas las
/// ventanas: con un notch por monitor cada aviso vence y se apunta una vez); cada ventana encola el aviso en su máquina y
/// contestarlo en una lo quita de todas. Cada tarea avisa una vez (apuntado en <c>tasks.json</c>: un reinicio no lo repite); el aviso entra en la cola (entre «Terminado» y el HUD) con «Hecha» /
/// «+10 min» y la campanita; con el notch oculto espera y sale al volver. Clic fuera de los botones lo descarta. Dependen
/// del ajuste «Tareas» (<see cref="NotchOptions.Tasks"/>), NO de que la página esté en la lista o a la vista.</item>
/// <item><b>Medianoche</b>: un disparo único a la siguiente (solo si hay tareas) purga las hechas y repinta
/// «mañana»/vencidas.</item>
/// </list>
/// </summary>
public sealed partial class NotchForm
{
    private const int WM_ACTIVATE = 0x0006, WM_CHAR = 0x0102, WA_INACTIVE = 0;

    private readonly ClaudeBarWin.Services.Motion.HoverHighlight _tasksHover = new();
    private TaskEditFocus? _editFocus;
    private IReadOnlyList<NotchTask> _taskItems = Array.Empty<NotchTask>();   // ya ordenadas (TaskList.Order)
    private TaskDraft? _taskDraft;                       // edición en línea abierta
    private TaskHit _tasksHeld;                          // control pulsado (DOWN) y aún sin soltar
    private TaskHit _tasksHoverHit;                      // control del realce (se conserva mientras se desvanece)
    private int _tasksOffset;                            // rueda: primera tarea mostrada
    private int _tasksWheelAccum;
    private PermissionVerdict _reminderHeld;             // botón del recordatorio pulsado: Allow = Hecha, Deny = +10 min
    private string? _reminderHeldId;

    /// <summary>¿La página Tareas se usa? (en la lista y sin el candado del Pro: con él no hay lista ni edición).</summary>
    private bool TasksEnabled => _pages.Contains(NotchPage.Tasks) && !PageLocked(NotchPage.Tasks);

    /// <summary>¿La página Tareas es la del expandido (objetivo)?</summary>
    private bool TasksPageVisible => TasksEnabled && !_hiddenForFullscreen && _sm.Mode == NotchMode.Expanded
        && _carousel.Current == NotchPage.Tasks;

    /// <summary>¿Se PINTA la página Tareas quieta y entera? Solo entonces acepta ratón (lo que se pinta es lo que se acciona).</summary>
    private bool TasksPageShown => TasksPageVisible && _anim.ShownMode == NotchMode.Expanded && _anim.ContentAlpha >= 1f
        && _pageAnim.Current == NotchPage.Tasks && !_pageAnim.IsAnimating && !_proCardOpen;

    // ---------------------------------------------------------------- almacén y recordatorios

    /// <summary>Las tareas (de <see cref="NotchShared"/>), si están cargadas.</summary>
    private TaskStore? Tasks => _shared.Tasks;

    /// <summary>Carga <c>tasks.json</c> la primera vez (al mostrarse con el ajuste «Tareas» activo, esté o no la página en
    /// la lista): purga, primer aviso y medianoche los hace <see cref="NotchShared.EnsureTasks"/>, una vez para todas.</summary>
    private void EnsureTasks()
    {
        if (!_options.Tasks) return;
        _shared.EnsureTasks();
        RefreshTasks();
    }

    /// <summary>Ajuste «Tareas» apagado: fuera la edición (guardando lo escrito), los avisos pendientes de la cola y los
    /// temporizadores; el archivo se queda como está (al volver a encenderlo se relee).</summary>
    private void UnloadTasks()
    {
        if (Tasks is null && _taskDraft is null) return;
        EndTaskEdit(save: true, restoreFocus: true);
        _shared.UnloadTasks();
        _taskItems = Array.Empty<NotchTask>();
        ResetTasksPointer();
        _tasksOffset = 0;
    }

    /// <summary>Las tareas cambiaron (aquí o en otra ventana), se cargaron, o es medianoche: lista nueva y repintado.</summary>
    private void OnTasksChanged()
    {
        if (!Alive) return;
        RefreshTasks();
        Sync();
    }

    private void RefreshTasks()
    {
        _taskItems = Tasks is not { } tasks ? Array.Empty<NotchTask>() : TaskList.Order(tasks.Items);
        _tasksOffset = Math.Min(_tasksOffset, Math.Max(0, _taskItems.Count - 1));
    }

    /// <summary>Vence un recordatorio (una vez para todas las ventanas): a la cola; con el notch oculto espera y sale al volver.</summary>
    private void OnReminderDue(string sessionId, string title)
    {
        if (!Alive) return;
        _sm.OnTaskReminder(sessionId, title, DateTime.UtcNow);
        Sync();
    }

    /// <summary>Un recordatorio se contestó o se descartó (aquí o en otra ventana), o su tarea ya no está: fuera de la cola.</summary>
    private void OnReminderCleared(string sessionId)
    {
        if (!Alive) return;
        _sm.ClearTaskReminder(sessionId, DateTime.UtcNow);
        Sync();
    }

    /// <summary>Cambios en <c>tasks.json</c>: un error de disco no tumba la ventana (se anota; la lista sigue en memoria).</summary>
    private static void TryTasks(Action change) => NotchShared.TryTasks(change);

    // ---------------------------------------------------------------- aviso de recordatorio

    /// <summary>¿El recordatorio que SE PINTA es el de la máquina? En un fundido puede no serlo.</summary>
    private bool ReminderShownMatches(NotchAlert alert) =>
        _anim.ShownAlert is { Kind: NotchAlertKind.Reminder } shown && shown.SessionId == alert.SessionId
        && shown.Title == alert.Title;

    /// <summary>¿Los botones del recordatorio a la vista aceptan clics? Armados (400 ms), forma quieta y lo pintado = lo real.</summary>
    private bool ReminderButtonsLive(DateTime now) =>
        _interactive && _sm.ReminderActionable && _sm.ActiveAlert is { } a && _sm.ReminderArmed(now) && _anim.ShapeSettled
        && ReminderShownMatches(a);

    /// <summary>WM_LBUTTONDOWN: recuerda el botón del recordatorio bajo el cursor (y pinta la pulsación).</summary>
    private void ReminderPress(Point p)
    {
        var over = HitTest(p) && ReminderButtonsLive(DateTime.UtcNow) ? PermissionButtonAt(p) : PermissionVerdict.None;
        string? id = over == PermissionVerdict.None ? null : _sm.ActiveAlert?.SessionId;
        if (over == _reminderHeld && id == _reminderHeldId) return;
        _reminderHeld = over;
        _reminderHeldId = id;
        RenderFrame();
    }

    /// <summary>
    /// Clic al SOLTAR en el aviso de recordatorio: «Hecha» completa la tarea, «+10 min» la aplaza (y vuelve a sonar); pulsar
    /// y soltar sobre el MISMO botón. Clic en el resto del aviso (ya armado) lo descarta. <c>false</c> si no hay recordatorio
    /// a la vista (clic normal).
    /// </summary>
    private bool ReminderClick(Point p)
    {
        var held = _reminderHeld;
        string? heldId = _reminderHeldId;
        _reminderHeld = PermissionVerdict.None;
        _reminderHeldId = null;
        if (!_interactive || !_sm.ReminderActionable || _sm.ActiveAlert is not { Kind: NotchAlertKind.Reminder } alert) return false;
        var now = DateTime.UtcNow;
        if (!ReminderButtonsLive(now)) return true;   // recién aparecido o en pleno fundido: el clic no cuenta
        var over = PermissionButtonAt(p);
        if (over == PermissionVerdict.None)
        {
            if (held == PermissionVerdict.None) _shared.ClearReminder(alert.SessionId);   // de todas las ventanas
            return true;
        }
        if (over != held || heldId != alert.SessionId) return true;

        _shared.ClearReminder(alert.SessionId);
        if (TaskAlerts.TaskId(alert.SessionId) is not { } id || Tasks is not { } tasks) return true;
        if (over == PermissionVerdict.Allow) TryTasks(() => tasks.Complete(id, DateTime.Now));
        else
        {
            _shared.ReminderClock.Rearm(id);
            TryTasks(() => tasks.Snooze(id, TaskTimes.Snooze, DateTime.Now));
        }
        return true;
    }

    /// <summary>Armado de los botones del recordatorio que SE PINTA (sin armar mientras la forma se mueve).</summary>
    private float ReminderArmShown()
    {
        if (_sm.ActiveAlert is not { Kind: NotchAlertKind.Reminder } active || !ReminderShownMatches(active)) return 0f;
        float arm = _sm.ReminderArmProgress(DateTime.UtcNow);
        return arm >= 1f && !_anim.ShapeSettled ? ArmWaitingShape : arm;
    }

    /// <summary>Botón del recordatorio pulsado a pintar (solo el del recordatorio a la vista).</summary>
    private PermissionVerdict ReminderHeldShown =>
        _sm.ReminderActionable && _reminderHeldId == _sm.ActiveAlert?.SessionId ? _reminderHeld : PermissionVerdict.None;

    /// <summary>Botones del recordatorio armándose (relleno animado con el timer del spinner).</summary>
    private bool ReminderArmingOn => _interactive && !_options.ReduceMotion && _sm.ReminderActionable && !_sm.ReminderArmed(DateTime.UtcNow);

    // ---------------------------------------------------------------- página Tareas: ratón

    private void ResetTasksPointer()
    {
        _tasksHeld = default;
        _tasksWheelAccum = 0;
        if (_tasksHover.Key is not null || _tasksHover.Visible) _tasksHover.Reset();
    }

    /// <summary>Control de la página Tareas bajo <paramref name="p"/>; nada si la página no se pinta quieta.</summary>
    private TaskHit TasksHitAt(Point p)
    {
        if (!TasksPageShown) return default;
        return TasksLayout.HitTest(new PointF(p.X + 0.5f, p.Y + 0.5f), MusicInner(), _scale, _taskItems.Count, _tasksOffset,
            _taskDraft is not null);
    }

    /// <summary>Realce de la fila o del botón bajo el cursor (HoverIn al entrar, Rápido al salir; reduce-motion = salto).</summary>
    private void TasksHover(Point p)
    {
        var hit = TasksHitAt(p);
        if (hit.Control == TaskControl.None)
        {
            if (_tasksHover.Key is null) return;
            _tasksHover.Leave(snap: _options.ReduceMotion);
        }
        else
        {
            string key = $"{hit.Control}:{hit.Index}";
            if (key == _tasksHover.Key) return;
            _tasksHoverHit = hit;
            _tasksHover.Enter(key, RectangleF.Empty, snap: _options.ReduceMotion);
        }
        Frame();
        Pump();
    }

    /// <summary>WM_LBUTTONDOWN: recuerda el control pulsado.</summary>
    private void TasksPress(Point p)
    {
        var hit = TasksHitAt(p);
        if (hit == _tasksHeld) return;
        _tasksHeld = hit;
        RenderFrame();
    }

    /// <summary>
    /// Clic al SOLTAR en la página Tareas, sobre el MISMO control que se pulsó: «+ Añadir» abre la edición; círculo = hecha
    /// / pendiente; aspa = quitar; editando: campo (teclear el título), punto (color), horas rápidas y «Guardar». Soltar fuera
    /// tras pulsar un control lo cancela (sin cerrar el expandido). Editando, ningún clic de la página cierra el expandido.
    /// <c>false</c> si el clic no era de la página Tareas (clic normal).
    /// </summary>
    private bool TasksClick(Point p)
    {
        var held = _tasksHeld;
        _tasksHeld = default;
        var hit = TasksHitAt(p);
        if (hit.Control == TaskControl.None)
            return TasksPageVisible && (held.Control != TaskControl.None || _taskDraft is not null);
        if (hit != held || Tasks is not { } tasks) return true;

        var now = DateTime.Now;
        NotchTask? task = hit.Index >= 0 && hit.Index < _taskItems.Count ? _taskItems[hit.Index] : null;
        switch (hit.Control)
        {
            case TaskControl.Add:
                BeginTaskEdit();
                return true;
            case TaskControl.Check when task is not null:
                if (task.Done)
                {
                    // Desmarcar no vuelve a hacer sonar un aviso que ya sonó (sería ruido por un clic de corrección).
                    TryTasks(() => tasks.Update(task with { Done = false, DoneAt = null }));
                }
                else
                {
                    _shared.ClearReminder(TaskAlerts.SessionId(task.Id));
                    TryTasks(() => tasks.Complete(task.Id, now));
                }
                break;
            case TaskControl.Delete when task is not null:
                _shared.ClearReminder(TaskAlerts.SessionId(task.Id));
                _tasksHover.Reset();   // las filas suben bajo el cursor: el realce viejo no vale
                TryTasks(() => tasks.Remove(task.Id));
                break;
            case TaskControl.Field:
                _taskDraft?.Focus(TaskField.Title);
                break;
            case TaskControl.Color:
                _taskDraft?.CycleColor();
                break;
            case TaskControl.Quick when _taskDraft is not null:
                _taskDraft.Choose((TaskQuick)hit.Index);
                break;
            case TaskControl.Save:
                if (_taskDraft is { CanSave: true }) EndTaskEdit(save: true, restoreFocus: true);
                return true;
        }
        Sync();
        return true;
    }

    /// <summary>Desplaza la lista de Tareas (<paramref name="rows"/> = filas que se pintan), como la de la Bandeja.</summary>
    private void ScrollTasks(int delta, int rows)
    {
        _tasksWheelAccum += delta;
        int steps = _tasksWheelAccum / NotchNative.WHEEL_DELTA;
        if (steps == 0) return;
        _tasksWheelAccum -= steps * NotchNative.WHEEL_DELTA;
        int next = Math.Clamp(_tasksOffset - steps, 0, ShelfLayout.MaxOffset(_taskItems.Count, rows));
        if (next == _tasksOffset) return;
        _tasksOffset = next;
        _tasksHover.Reset();
        RenderFrame();
    }

    /// <summary>Filas de la lista de Tareas que se pintan y si el punto está sobre ella (para la rueda).</summary>
    private (int Rows, bool OverList) TasksWheelInfo(Point p, bool withAlert, bool dots)
    {
        var pill = NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface);
        var r = TasksLayout.Compute(NotchContentPainter.PageInner(pill, _scale, withAlert, dots), _scale, _taskDraft is not null);
        return (r.Rows, r.List.Contains(p));
    }

    // ---------------------------------------------------------------- edición en línea

    /// <summary>Abre la edición: borrador nuevo (color siguiente), expandido retenido y el notch al frente para el teclado.
    /// Si Windows no deja traerlo al frente, no se abre (se anota).</summary>
    private void BeginTaskEdit()
    {
        if (Tasks is null || _taskDraft is not null || !IsHandleCreated) return;
        var utc = DateTime.UtcNow;
        _taskDraft = new TaskDraft(_taskItems.Count(t => !t.Done) % (TaskStore.MaxColor + 1));
        _sm.SetHold(true, utc);
        _editFocus ??= new TaskEditFocus(new EditFocusHost(() => IsHandleCreated ? Handle : IntPtr.Zero));
        if (!_editFocus.Begin())
        {
            AppLog.Warn("notch: tareas: Windows no dejó traer el notch al frente para escribir");
            _taskDraft = null;
            _sm.SetHold(false, utc);
        }
        ResetTasksPointer();
        Sync();
    }

    /// <summary>Cierra la edición: guarda si se pide y se puede, restaura NOACTIVATE y (si se pide) devuelve el foco a la
    /// ventana anterior; suelta el expandido.</summary>
    private void EndTaskEdit(bool save, bool restoreFocus)
    {
        var d = _taskDraft;
        if (d is null) return;
        _taskDraft = null;   // antes de soltar el foco: el WM_ACTIVATE que provoca no debe volver a entrar aquí
        if (save && Tasks is { } tasks && d.TryBuild(DateTime.Now, out var title, out var at))
            TryTasks(() => tasks.Add(title, at, d.Color));
        _editFocus?.End(restoreFocus);
        _sm.SetHold(false, DateTime.UtcNow);
        ResetTasksPointer();
        if (Alive) Sync();
    }

    /// <summary>WM_CHAR durante la edición.</summary>
    private void OnTaskChar(char c)
    {
        if (_taskDraft is not { } d) return;
        switch (d.OnChar(c))
        {
            case TaskKey.Commit:
                EndTaskEdit(save: true, restoreFocus: true);
                return;
            case TaskKey.Cancel:
                EndTaskEdit(save: false, restoreFocus: true);
                return;
            case TaskKey.Paste:
                if (d.Paste(ReadClipboardText())) RenderFrame();
                return;
            case TaskKey.Changed:
                RenderFrame();
                return;
        }
    }

    private static string? ReadClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException)
        {
            AppLog.Warn($"notch: tareas: no se pudo leer el portapapeles: {ex.Message}");
            return null;
        }
    }

    /// <summary>Mensajes de teclado/activación de la edición. <c>true</c> si el mensaje se consumió.</summary>
    private bool TasksWndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_CHAR when _taskDraft is not null:
                OnTaskChar((char)(long)m.WParam);
                m.Result = IntPtr.Zero;
                return true;
            case WM_ACTIVATE when _taskDraft is not null && ((long)m.WParam & 0xFFFF) == WA_INACTIVE:
                // El usuario se fue a otra ventana: se guarda lo escrito (si vale) y no se le roba el foco de vuelta.
                EndTaskEdit(save: true, restoreFocus: false);
                return false;   // WinForms sigue con su WM_ACTIVATE
        }
        return false;
    }

    /// <summary>Intro, Esc y Tab llegan como WM_CHAR a la edición: que WinForms no se los quede como teclas de diálogo.</summary>
    protected override bool ProcessDialogKey(Keys keyData) => _taskDraft is null && base.ProcessDialogKey(keyData);

    // ---------------------------------------------------------------- frame

    /// <summary>La página Tareas del frame (solo en el expandido, con la página en la lista y las tareas cargadas).</summary>
    private TasksView? TasksFrame(NotchMode shown)
    {
        if (shown != NotchMode.Expanded || !TasksEnabled || Tasks is null) return null;
        var hover = _tasksHover.Visible ? _tasksHoverHit : default;
        return new TasksView(_taskItems, DateTime.Now, ClockText.Prefers24h(CultureInfo.CurrentCulture), _taskDraft?.View(),
            _tasksOffset, hover, (float)_tasksHover.Intensity, _tasksHeld);
    }

    /// <summary>Filas de la página Tareas para su alto: una por tarea y, editando, la de las horas rápidas.</summary>
    private int TaskPageRows => _taskItems.Count + (_taskDraft is not null ? 1 : 0);

    /// <summary>Limpieza de lo de las tareas (desde <see cref="Dispose(bool)"/>): el almacén y los temporizadores son del
    /// contenedor compartido; aquí solo se suelta la ventana.</summary>
    private void DisposeTasks()
    {
        _shared.TasksChanged -= OnTasksChanged;
        _shared.ReminderDue -= OnReminderDue;
        _shared.ReminderCleared -= OnReminderCleared;
    }
}
