using System.Drawing;
using System.Globalization;
using System.Text;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Horas rápidas del recordatorio al crear una tarea (los chips de la edición en línea).</summary>
public enum TaskQuick { None, In15Min, In1Hour, Tomorrow9, Custom }

/// <summary>Campo del borrador que recibe lo que se teclea.</summary>
public enum TaskField { Title, Time }

/// <summary>Qué pide una tecla del borrador: nada, repintar, guardar, cancelar o pegar (el llamador lee el portapapeles).</summary>
public enum TaskKey { None, Changed, Commit, Cancel, Paste }

/// <summary>Horas de los recordatorios (F5 · B5). PURO: el instante llega por parámetro, siempre en hora LOCAL.</summary>
public static class TaskTimes
{
    /// <summary>Lo que aplaza el botón «+10 min» del aviso.</summary>
    public static readonly TimeSpan Snooze = TimeSpan.FromMinutes(10);

    /// <summary>Hora que se pide para «mañana».</summary>
    public static readonly TimeSpan TomorrowAt = TimeSpan.FromHours(9);

    /// <summary>
    /// Cuánto falta para la próxima medianoche local (purga de hechas y «mañana»/vencidas al día). Nunca cero: a las
    /// 00:00:00 en punto, la de mañana. Tiempo REAL (lo que espera un timer), no resta de horas de reloj: el día del
    /// cambio de hora se cuenta en UTC (en primavera faltan 1 h menos; en otoño, 1 h más). <paramref name="zone"/> es la
    /// zona de <paramref name="nowLocal"/> (por defecto la del sistema; los tests pasan la suya).
    /// </summary>
    public static TimeSpan UntilMidnight(DateTime nowLocal, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var now = DateTime.SpecifyKind(nowLocal, DateTimeKind.Unspecified);
        var midnight = now.Date.AddDays(1);
        try
        {
            var d = TimeZoneInfo.ConvertTimeToUtc(midnight, zone) - TimeZoneInfo.ConvertTimeToUtc(now, zone);
            if (d > TimeSpan.Zero) return d;
        }
        catch (ArgumentException) { /* hora que no existe en esa zona (salto justo a las 00:00): resta de reloj */ }
        return midnight - now;
    }

    /// <summary>
    /// Hora personalizada tecleada como dígitos (HH:MM sin los dos puntos): 1–2 dígitos = horas en punto («9» → 9:00,
    /// «18» → 18:00); 3–4 = horas y minutos («930» → 9:30, «1830» → 18:30). Fuera de 00:00–23:59 o vacío → <c>null</c>.
    /// </summary>
    public static TimeSpan? ParseCustom(string digits)
    {
        if (string.IsNullOrEmpty(digits) || digits.Length > 4) return null;
        foreach (var c in digits)
            if (c is < '0' or > '9') return null;
        int h, m;
        if (digits.Length <= 2) { h = int.Parse(digits, CultureInfo.InvariantCulture); m = 0; }
        else
        {
            h = int.Parse(digits[..^2], CultureInfo.InvariantCulture);
            m = int.Parse(digits[^2..], CultureInfo.InvariantCulture);
        }
        return h is >= 0 and <= 23 && m is >= 0 and <= 59 ? new TimeSpan(h, m, 0) : null;
    }

    /// <summary>
    /// Hora de aviso para la opción elegida: +15 min y +1 h desde ahora; «mañana 9:00»; la personalizada hoy si aún no ha
    /// pasado (estrictamente después de ahora), si no mañana. Sin opción, o personalizada vacía/imposible → <c>null</c>.
    /// </summary>
    public static DateTime? Resolve(TaskQuick quick, string digits, DateTime nowLocal) => quick switch
    {
        TaskQuick.In15Min => nowLocal.AddMinutes(15),
        TaskQuick.In1Hour => nowLocal.AddHours(1),
        TaskQuick.Tomorrow9 => nowLocal.Date.AddDays(1) + TomorrowAt,
        TaskQuick.Custom when ParseCustom(digits) is { } t => nowLocal.Date + t > nowLocal ? nowLocal.Date + t : nowLocal.Date.AddDays(1) + t,
        _ => null,
    };

    /// <summary>Dígitos de la hora personalizada tal como se enseñan: con los dos puntos antes de los minutos («930» → «9:30»).</summary>
    public static string FormatDigits(string digits) =>
        string.IsNullOrEmpty(digits) || digits.Length < 3 ? digits ?? "" : digits[..^2] + ":" + digits[^2..];

    /// <summary>Etiqueta de la hora de una tarea: solo la hora si es hoy, «mañana 9:00» si es mañana, y si no la fecha corta
    /// localizada con la hora («lun 2 nov 08:05»). Formato de <see cref="ClockText"/>.</summary>
    public static string Label(DateTime remindLocal, DateTime nowLocal, CultureInfo culture, bool use24h, string tomorrow)
    {
        var (time, date) = ClockText.Format(remindLocal, culture, use24h);
        if (remindLocal.Date == nowLocal.Date) return time;
        if (remindLocal.Date == nowLocal.Date.AddDays(1)) return $"{tomorrow} {time}";
        return $"{date} {time}";
    }
}

/// <summary>Foto inmutable del borrador para pintar.</summary>
public sealed record TaskDraftView(string Title, string TimeDigits, TaskQuick Quick, TaskField Field, int Color,
    bool TimeValid, bool CanSave);

/// <summary>
/// Borrador de la edición en línea de una tarea (F5 · B5), alimentado con los <c>WM_CHAR</c> de la ventana. PURO.
/// <list type="bullet">
/// <item>Título: una línea, máx. <see cref="TaskStore.MaxTitle"/> caracteres UTF-16 sin partir pares sustitutos (que llegan
/// en dos <c>WM_CHAR</c>: la mitad alta espera a la baja; una baja suelta se ignora).</item>
/// <item>Retroceso borra el último carácter (el par entero); Ctrl+Retroceso (0x7F) la última palabra (en la hora, toda).</item>
/// <item>Intro guarda si hay título y la hora es válida; Esc cancela; Tab cambia de campo; Ctrl+V pide pegar.</item>
/// <item>Hora: solo dígitos, como mucho 4 (HH:MM). Teclearla elige la opción personalizada. Vacía = sin recordatorio.</item>
/// </list>
/// </summary>
public sealed class TaskDraft
{
    private const int MaxDigits = 4;
    private readonly StringBuilder _title = new();
    private readonly StringBuilder _digits = new();
    private char? _pendingHigh;

    public TaskDraft(int color = 0) => Color = Math.Clamp(color, 0, TaskStore.MaxColor);

    public string Title => _title.ToString();
    public string TimeDigits => _digits.ToString();
    public TaskQuick Quick { get; private set; }
    public TaskField Field { get; private set; }
    public int Color { get; private set; }

    /// <summary>La hora vale: no es la personalizada, o está vacía (sin aviso), o es una hora real.</summary>
    public bool TimeValid => Quick != TaskQuick.Custom || _digits.Length == 0 || TaskTimes.ParseCustom(TimeDigits) is not null;

    public bool CanSave => Title.Trim().Length > 0 && TimeValid;

    public TaskKey OnChar(char c)
    {
        switch (c)
        {
            case '\r': return CanSave ? TaskKey.Commit : TaskKey.None;
            case '\x1b': return TaskKey.Cancel;
            case '\t':
                Focus(Field == TaskField.Title ? TaskField.Time : TaskField.Title);
                return TaskKey.Changed;
            case '\x16': return TaskKey.Paste;
            case '\b': return Backspace() ? TaskKey.Changed : TaskKey.None;
            case '\x7f': return DeleteWord() ? TaskKey.Changed : TaskKey.None;
        }
        if (Field == TaskField.Time)
        {
            if (c is < '0' or > '9' || _digits.Length >= MaxDigits) return TaskKey.None;
            _digits.Append(c);
            Quick = TaskQuick.Custom;
            return TaskKey.Changed;
        }
        if (char.IsHighSurrogate(c))
        {
            _pendingHigh = c;
            return TaskKey.None;
        }
        if (char.IsLowSurrogate(c))
        {
            var high = _pendingHigh;
            _pendingHigh = null;
            if (high is null || _title.Length + 2 > TaskStore.MaxTitle) return TaskKey.None;
            _title.Append(high.Value).Append(c);
            return TaskKey.Changed;
        }
        _pendingHigh = null;
        if (char.IsControl(c) || _title.Length >= TaskStore.MaxTitle) return TaskKey.None;
        _title.Append(c);
        return TaskKey.Changed;
    }

    /// <summary>Pega texto en el campo activo: en el título, las líneas se juntan con espacios y fuera los caracteres de
    /// control, hasta donde quepa; en la hora, solo los dígitos. <c>true</c> si entró algo.</summary>
    public bool Paste(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        _pendingHigh = null;
        bool any = false;
        if (Field == TaskField.Time)
        {
            foreach (var c in text)
            {
                if (c is < '0' or > '9' || _digits.Length >= MaxDigits) continue;
                _digits.Append(c);
                any = true;
            }
            if (any) Quick = TaskQuick.Custom;
            return any;
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                if (_title.Length + 2 > TaskStore.MaxTitle) break;
                _title.Append(c).Append(text[++i]);
                any = true;
                continue;
            }
            if (char.IsSurrogate(c)) continue;
            if (char.IsControl(c))
            {
                // Saltos de línea y tabuladores: un espacio (sin repetirlos).
                if (c is '\r' or '\n' or '\t' && _title.Length > 0 && _title[^1] != ' ' && _title.Length < TaskStore.MaxTitle)
                {
                    _title.Append(' ');
                    any = true;
                }
                continue;
            }
            if (_title.Length >= TaskStore.MaxTitle) break;
            _title.Append(c);
            any = true;
        }
        return any;
    }

    /// <summary>Elige una hora rápida; la misma otra vez la quita. La personalizada pasa el tecleo a la hora.</summary>
    public void Choose(TaskQuick q)
    {
        Quick = Quick == q ? TaskQuick.None : q;
        if (Quick == TaskQuick.Custom) Field = TaskField.Time;
        else if (Field == TaskField.Time) Field = TaskField.Title;
    }

    public void Focus(TaskField f)
    {
        Field = f;
        _pendingHigh = null;
    }

    public void CycleColor() => Color = Color >= TaskStore.MaxColor ? 0 : Color + 1;

    /// <summary>Título limpio y hora de aviso, o <c>false</c> si aún no se puede guardar.</summary>
    public bool TryBuild(DateTime nowLocal, out string title, out DateTime? remindAt)
    {
        title = Title.Trim();
        remindAt = null;
        if (!CanSave) return false;
        remindAt = TaskTimes.Resolve(Quick, TimeDigits, nowLocal);
        return true;
    }

    public TaskDraftView View() => new(Title, TimeDigits, Quick, Field, Color, TimeValid, CanSave);

    private bool Backspace()
    {
        _pendingHigh = null;
        var sb = Field == TaskField.Time ? _digits : _title;
        if (sb.Length == 0) return false;
        int n = sb.Length >= 2 && char.IsLowSurrogate(sb[^1]) && char.IsHighSurrogate(sb[^2]) ? 2 : 1;
        sb.Length -= n;
        return true;
    }

    private bool DeleteWord()
    {
        _pendingHigh = null;
        if (Field == TaskField.Time)
        {
            bool had = _digits.Length > 0;
            _digits.Clear();
            return had;
        }
        if (_title.Length == 0) return false;
        int end = _title.Length;
        while (end > 0 && char.IsWhiteSpace(_title[end - 1])) end--;
        while (end > 0 && !char.IsWhiteSpace(_title[end - 1])) end--;
        _title.Length = end;
        return true;
    }
}

/// <summary>Orden de la lista de tareas. PURO.</summary>
public static class TaskList
{
    /// <summary>Hora efectiva del aviso (el aplazamiento manda).</summary>
    public static DateTime? When(NotchTask t) => t.SnoozedUntilLocal ?? t.RemindAtLocal;

    /// <summary>Pendientes con hora (la más cercana arriba), pendientes sin hora (en su orden) y, al final, las hechas
    /// (en el orden en que se completaron). Estable.</summary>
    public static IReadOnlyList<NotchTask> Order(IReadOnlyList<NotchTask> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select((t, i) => (t, i))
            .OrderBy(x => x.t.Done ? 2 : When(x.t) is null ? 1 : 0)
            .ThenBy(x => x.t.Done ? x.t.DoneAt ?? DateTime.MinValue : When(x.t) ?? DateTime.MinValue)
            .ThenBy(x => x.i)
            .Select(x => x.t)
            .ToArray();
    }
}

/// <summary>
/// Reloj de los recordatorios (F5 · B5) sobre <see cref="TaskReminders"/>. PURO. Recuerda qué tareas ya avisaron (cada
/// una suena UNA vez aunque se sondee muchas veces: al volver de suspender, al salir de pantalla completa, al cambiar la
/// hora del sistema). Sin temporizadores dentro: el llamador programa un disparo con <see cref="NextWakeIn"/>.
/// </summary>
public sealed class TaskReminderClock
{
    private readonly HashSet<Guid> _fired = new();

    /// <summary>Las tareas que vencen ahora y no habían avisado (de la más antigua a la más nueva). Olvida las tareas que
    /// ya no existen.</summary>
    public IReadOnlyList<NotchTask> Poll(IReadOnlyList<NotchTask> items, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(items);
        var alive = items.Select(t => t.Id).ToHashSet();
        _fired.RemoveWhere(id => !alive.Contains(id));
        var due = new List<NotchTask>();
        while (TaskReminders.Due(items, nowLocal, _fired) is { } t) due.Add(t);
        return due;
    }

    /// <summary>Cuánto falta para el próximo aviso (para un temporizador de un solo disparo); <c>null</c> si no hay.</summary>
    public TimeSpan? NextWakeIn(IReadOnlyList<NotchTask> items, DateTime nowLocal)
    {
        if (TaskReminders.NextDue(items, nowLocal) is not { } at) return null;
        var d = at - nowLocal;
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }

    /// <summary>La tarea puede volver a avisar (tras «+10 min»).</summary>
    public void Rearm(Guid id) => _fired.Remove(id);

    public bool HasFired(Guid id) => _fired.Contains(id);
}

/// <summary>Los avisos de recordatorio van a la cola del notch con la «sesión» <c>task:&lt;id&gt;</c>. PURO.</summary>
public static class TaskAlerts
{
    public const string Prefix = "task:";

    public static string SessionId(Guid id) => Prefix + id.ToString("N");

    public static Guid? TaskId(string? sessionId) =>
        sessionId is not null && sessionId.StartsWith(Prefix, StringComparison.Ordinal)
        && Guid.TryParseExact(sessionId[Prefix.Length..], "N", out var id) ? id : null;
}

/// <summary>Lo que la edición en línea pide a Win32 (doble en los tests). Nunca lanza.</summary>
public interface IEditFocusHost
{
    /// <summary>La ventana del notch.</summary>
    IntPtr Self { get; }
    IntPtr Foreground();
    /// <summary>Pone o quita <c>WS_EX_NOACTIVATE</c>.</summary>
    void SetNoActivate(bool on);
    /// <summary><c>SetForegroundWindow</c> sobre el notch; <c>true</c> si quedó delante.</summary>
    bool Activate();
    bool IsWindow(IntPtr hwnd);
    /// <summary>Devuelve el primer plano a <paramref name="hwnd"/>.</summary>
    void Restore(IntPtr hwnd);
}

/// <summary>
/// Foco de la edición en línea (F5 · B5). El notch nunca tiene foco (<c>WS_EX_NOACTIVATE</c>); para teclear una tarea se
/// quita ese estilo un momento, se trae al frente y, al acabar, se vuelve a poner y se devuelve el foco a la ventana que lo
/// tenía (si sigue viva y el usuario no se ha ido ya a otra). PURO sobre <see cref="IEditFocusHost"/>.
/// </summary>
public sealed class TaskEditFocus
{
    private readonly IEditFocusHost _host;
    private IntPtr _previous;

    public TaskEditFocus(IEditFocusHost host) => _host = host ?? throw new ArgumentNullException(nameof(host));

    public bool Active { get; private set; }

    /// <summary>Empieza a recibir teclado. Si Windows no deja traerlo al frente, todo vuelve como estaba y <c>false</c>.</summary>
    public bool Begin()
    {
        if (Active) return true;
        var fg = _host.Foreground();
        _previous = fg == _host.Self ? IntPtr.Zero : fg;
        _host.SetNoActivate(false);
        if (!_host.Activate())
        {
            _host.SetNoActivate(true);
            _previous = IntPtr.Zero;
            return false;
        }
        Active = true;
        return true;
    }

    /// <summary>Deja de recibir teclado. <paramref name="restoreFocus"/>: devolver el foco a la ventana anterior (no cuando el
    /// usuario ya ha hecho clic en otra: no se le roba).</summary>
    public void End(bool restoreFocus)
    {
        if (!Active) return;
        Active = false;
        _host.SetNoActivate(true);
        var prev = _previous;
        _previous = IntPtr.Zero;
        if (restoreFocus && prev != IntPtr.Zero && _host.IsWindow(prev) && _host.Foreground() == _host.Self)
            _host.Restore(prev);
    }
}

/// <summary>Partes de la página Tareas que responden al ratón.</summary>
public enum TaskControl { None, Add, Check, Delete, Row, Field, Color, Quick, Save }

/// <summary>Qué hay bajo el cursor: el control y su índice (fila de la lista ya con el desplazamiento sumado, o el
/// <see cref="TaskQuick"/> de un chip).</summary>
public readonly record struct TaskHit(TaskControl Control, int Index = -1);

/// <summary>Rectángulos de la página Tareas (px físicos). La MISMA cuenta para pintar y para acertar el clic.</summary>
public readonly record struct TasksRects(RectangleF Header, RectangleF Title, RectangleF AddButton, RectangleF ColorDot,
    RectangleF Field, RectangleF SaveButton, RectangleF ChipsRow, RectangleF List, int Rows, float Scale)
{
    /// <summary>Fila visible <paramref name="visible"/> (0 = la de arriba); vacía fuera de rango.</summary>
    public RectangleF Row(int visible)
    {
        if (visible < 0 || visible >= Rows) return RectangleF.Empty;
        float h = NotchLayout.ExpandedLine * Scale;
        return new RectangleF(List.Left, List.Top + visible * h, List.Width, h);
    }

    /// <summary>Círculo para marcar hecha (a la izquierda de la fila).</summary>
    public RectangleF Check(int visible)
    {
        var row = Row(visible);
        if (row.IsEmpty) return row;
        float d = TasksLayout.CheckSize * Scale;
        return new RectangleF(row.Left + TasksLayout.RowInset * Scale, row.Top + (row.Height - d) / 2f, d, d);
    }

    /// <summary>Aspa para quitar la tarea (a la derecha de la fila; se pinta con el ratón encima).</summary>
    public RectangleF Delete(int visible)
    {
        var row = Row(visible);
        if (row.IsEmpty) return row;
        float d = TasksLayout.DeleteSize * Scale;
        return new RectangleF(row.Right - TasksLayout.RowInset * Scale - d, row.Top + (row.Height - d) / 2f, d, d);
    }

    /// <summary>Hueco de la hora, a la izquierda del aspa.</summary>
    public RectangleF TimeLabel(int visible)
    {
        var del = Delete(visible);
        if (del.IsEmpty) return del;
        float w = TasksLayout.TimeWidth * Scale;
        var row = Row(visible);
        return RectangleF.FromLTRB(del.Left - 6f * Scale - w, row.Top, del.Left - 6f * Scale, row.Bottom);
    }

    /// <summary>Chip <paramref name="i"/> de las horas rápidas (0..<see cref="TasksLayout.Chips"/>−1); vacío sin edición.</summary>
    public RectangleF Chip(int i)
    {
        if (ChipsRow.IsEmpty || i < 0 || i >= TasksLayout.Chips) return RectangleF.Empty;
        float x = ChipsRow.Left, h = TasksLayout.ButtonHeight * Scale;
        for (int k = 0; k < i; k++) x += (TasksLayout.ChipWidths[k] + TasksLayout.ChipGap) * Scale;
        return new RectangleF(x, ChipsRow.Top + (ChipsRow.Height - h) / 2f, TasksLayout.ChipWidths[i] * Scale, h);
    }
}

/// <summary>
/// Maquetación de la página Tareas del expandido (F5 · B5). PURA, en px lógicos × escala dentro de la zona de contenido
/// de la página (<c>NotchContentPainter.PageInner</c>):
/// <code>
/// Tareas                                    [+ Añadir]   cabecera
/// ○ Comprar pan                     18:30     ×         una fila por tarea: círculo (hecha), color, título, hora, aspa
/// ◉ Llamar a Fran                mañana 09:00
/// +3 más                                                 lo que no cabe (la rueda desplaza la lista)
/// </code>
/// Editando, la cabecera es el campo de texto y debajo van las horas rápidas:
/// <code>
/// ● Nueva tarea…|                            [Guardar]
/// [+15 min] [+1 h] [Mañana 9:00] [HH:MM]
/// </code>
/// </summary>
public static class TasksLayout
{
    public const float AddWidth = 84f, SaveWidth = 70f, ButtonHeight = 18f, Dot = 10f, CheckSize = 12f, DeleteSize = 14f,
        TimeWidth = 104f, RowInset = 6f, ChipGap = 6f, FieldGap = 8f;

    /// <summary>Anchos (px lógicos) de los chips: +15 min, +1 h, mañana 9:00, HH:MM.</summary>
    public static readonly float[] ChipWidths = { 64f, 48f, 100f, 64f };

    public static int Chips => ChipWidths.Length;

    /// <summary>Hora rápida del chip <paramref name="i"/>.</summary>
    public static TaskQuick ChipQuick(int i) => (TaskQuick)(i + 1);

    public static TasksRects Compute(RectangleF inner, float s, bool editing)
    {
        float line = NotchLayout.ExpandedLine * s, bh = ButtonHeight * s;
        var header = new RectangleF(inner.Left, inner.Top, inner.Width, line);
        float midY = header.Top + (line - bh) / 2f;

        RectangleF title = RectangleF.Empty, add = RectangleF.Empty, dot = RectangleF.Empty, field = RectangleF.Empty,
            save = RectangleF.Empty, chips = RectangleF.Empty;
        float listTop = header.Bottom;
        if (editing)
        {
            float d = Dot * s;
            dot = new RectangleF(inner.Left + RowInset * s, header.Top + (line - d) / 2f, d, d);
            save = new RectangleF(inner.Right - SaveWidth * s, midY, SaveWidth * s, bh);
            float fl = dot.Right + FieldGap * s;
            field = RectangleF.FromLTRB(fl, midY, Math.Max(fl, save.Left - FieldGap * s), midY + bh);
            chips = RectangleF.FromLTRB(inner.Left + RowInset * s, header.Bottom, inner.Right, Math.Min(inner.Bottom, header.Bottom + line));
            listTop = chips.Bottom;
        }
        else
        {
            add = new RectangleF(inner.Right - AddWidth * s, midY, AddWidth * s, bh);
            title = RectangleF.FromLTRB(inner.Left, header.Top, Math.Max(inner.Left, add.Left - 8f * s), header.Bottom);
        }
        var list = RectangleF.FromLTRB(inner.Left, listTop, inner.Right, Math.Max(listTop, inner.Bottom));
        // Media fila de tolerancia: los redondeos de la escala no deben comerse la última fila.
        int rows = line > 0 ? Math.Max(0, (int)Math.Floor((list.Height + 0.5f) / line)) : 0;
        return new TasksRects(header, title, add, dot, field, save, chips, list, rows, s);
    }

    /// <summary>Control bajo <paramref name="p"/>. Filas como la Bandeja (<see cref="ShelfLayout.Window"/>: la fila «+N más»
    /// no es nada). En una fila: el círculo (y su margen a la izquierda) marca hecha, el aspa la quita, el resto es la fila.</summary>
    public static TaskHit HitTest(PointF p, RectangleF inner, float s, int count, int offset, bool editing)
    {
        if (!inner.Contains(p)) return default;
        var r = Compute(inner, s, editing);
        if (editing)
        {
            if (r.SaveButton.Contains(p)) return new(TaskControl.Save);
            if (RectangleF.FromLTRB(inner.Left, r.Header.Top, r.Field.Left, r.Header.Bottom).Contains(p)) return new(TaskControl.Color);
            if (RectangleF.FromLTRB(r.Field.Left, r.Header.Top, r.Field.Right, r.Header.Bottom).Contains(p)) return new(TaskControl.Field);
            for (int i = 0; i < Chips; i++)
                if (r.Chip(i).Contains(p)) return new(TaskControl.Quick, (int)ChipQuick(i));
        }
        else if (r.AddButton.Contains(p)) return new(TaskControl.Add);

        var w = ShelfLayout.Window(count, r.Rows, offset);
        for (int v = 0; v < w.Shown; v++)
        {
            var row = r.Row(v);
            if (!row.Contains(p)) continue;
            int index = w.First + v;
            if (p.X < r.Check(v).Right + 4f * s) return new(TaskControl.Check, index);
            if (p.X >= r.Delete(v).Left - 4f * s) return new(TaskControl.Delete, index);
            return new(TaskControl.Row, index);
        }
        return default;
    }
}

/// <summary>Valores por defecto de las tareas en la app (F5 · B5).</summary>
public static class TaskDefaults
{
    /// <summary><c>%APPDATA%\ClaudeBarWin\notch\tasks.json</c>.</summary>
    public static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeBarWin", "notch", "tasks.json");
}
