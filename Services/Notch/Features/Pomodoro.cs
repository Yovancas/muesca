namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Fase del pomodoro: foco, descanso corto o descanso largo (cada <see cref="FocusDurations.LongEvery"/> focos).</summary>
public enum FocusPhase { Focus, ShortBreak, LongBreak }

/// <summary>El reloj de la fase: parado al principio (<see cref="Ready"/>), corriendo o en pausa.</summary>
public enum FocusRun { Ready, Running, Paused }

/// <summary>
/// El instante, por parámetro (regla del repo): <see cref="Utc"/> es la hora de pared (la hora de fin que se guarda y sobrevive a
/// un reinicio de la app), <see cref="Mono"/> el reloj monotónico del proceso (lo que de verdad pasa mientras la app vive: no
/// salta si alguien cambia la hora) y <see cref="Local"/> la hora local (el día de los contadores).
/// </summary>
public readonly record struct FocusNow(DateTime Utc, TimeSpan Mono, DateTime Local);

/// <summary>
/// Duraciones del pomodoro en minutos: foco, descanso corto y largo, y cada cuántos focos toca el largo. Las de serie
/// (25 / 5 / 15 cada 4) son gratis; cambiarlas es Pro (<c>ProFeature.NotchFocus</c>, lo aplica <c>NotchProGating</c>).
/// </summary>
public sealed record FocusDurations(int FocusMinutes = 25, int ShortMinutes = 5, int LongMinutes = 15, int LongEvery = 4)
{
    public const int MinMinutes = 1, MaxMinutes = 180, MinEvery = 2, MaxEvery = 8;

    /// <summary>25 / 5 / 15, descanso largo cada 4 focos.</summary>
    public static FocusDurations Default { get; } = new();

    /// <summary>Lo que ofrece Ajustes en cada fila (los valores a mano de <c>config.json</c> se acotan con <see cref="Normalize"/>).</summary>
    public static IReadOnlyList<int> FocusOptions { get; } = new[] { 15, 20, 25, 30, 45, 50, 60 };
    public static IReadOnlyList<int> ShortOptions { get; } = new[] { 3, 5, 10 };
    public static IReadOnlyList<int> LongOptions { get; } = new[] { 10, 15, 20, 30 };
    public static IReadOnlyList<int> EveryOptions { get; } = new[] { 2, 3, 4, 5, 6 };

    /// <summary>Todo dentro de rango (minutos 1..180; el largo, cada 2..8 focos).</summary>
    public FocusDurations Normalize() => new(Minutes(FocusMinutes), Minutes(ShortMinutes), Minutes(LongMinutes),
        Math.Clamp(LongEvery, MinEvery, MaxEvery));

    /// <summary>Lo que dura la fase <paramref name="phase"/>.</summary>
    public TimeSpan For(FocusPhase phase) => TimeSpan.FromMinutes(Minutes(phase switch
    {
        FocusPhase.ShortBreak => ShortMinutes,
        FocusPhase.LongBreak => LongMinutes,
        _ => FocusMinutes,
    }));

    private static int Minutes(int m) => Math.Clamp(m, MinMinutes, MaxMinutes);
}

/// <summary>Ajustes del pomodoro (salen de Ajustes): las duraciones y si suena el aviso discreto del fin de fase.</summary>
public sealed record FocusSettings(FocusDurations Durations, bool Sound = true)
{
    /// <summary>Las duraciones de serie, con sonido.</summary>
    public static FocusSettings Default { get; } = new(FocusDurations.Default);
}

/// <summary>Focos contados en un día local y sus minutos.</summary>
public sealed record FocusDay(DateOnly Date, int Focuses, int Minutes);

/// <summary>
/// Lo que se guarda del pomodoro en <c>config.json</c> (<c>NotchFocusState</c>) para sobrevivir a un reinicio de la app: la fase y
/// cómo estaba; corriendo, la hora de FIN en UTC (nunca lo que quedaba: así cuenta también el tiempo con la app cerrada); en
/// pausa, lo que quedaba; lo que dura la fase en curso (cambiar las duraciones no la acorta); el ciclo hacia el descanso largo;
/// la fase recién terminada (la oferta «Empezar descanso» / «Empezar foco»); la tarea enlazada; cuándo se tocó por última vez
/// (una acción o el fin de una fase) y los focos de los últimos días. Cualquier valor raro (a mano, de otra versión) se corrige al restaurar.
/// </summary>
public sealed record FocusState(FocusPhase Phase = FocusPhase.Focus, FocusRun Run = FocusRun.Ready, DateTime? EndsAtUtc = null,
    TimeSpan? Remaining = null, TimeSpan? Length = null, int Cycle = 0, FocusPhase? Finished = null, Guid? TaskId = null,
    DateTime? LastActivityUtc = null, IReadOnlyList<FocusDay>? Days = null);

/// <summary>
/// Una fase que acaba de terminar: cuál (<see cref="Ended"/>) y cuál toca ahora (<see cref="Next"/>, parada a la espera de que el
/// usuario la empiece). <see cref="Counted"/>: un foco completo (suma a hoy y al ciclo); <see cref="TaskId"/>: la tarea enlazada en
/// la que se apunta (solo si se contó). <see cref="Late"/>: cuánto hace que acabó (suspensión o app cerrada).
/// </summary>
public sealed record FocusEnd(FocusPhase Ended, FocusPhase Next, bool Counted, Guid? TaskId, int Minutes, TimeSpan Late);

/// <summary>
/// El pomodoro. PURO: sin timers ni reloj propio; cada entrada recibe el instante (<see cref="FocusNow"/>) y quien lo usa pregunta
/// <see cref="NextEndIn"/> para programar UN disparo. Nunca descuenta por ticks: lo que queda sale siempre de la hora de fin.
/// <list type="bullet">
/// <item><b>Tiempo:</b> al arrancar se fijan dos anclas, la monotónica (<see cref="FocusNow.Mono"/>) y la de pared en UTC. Lo que
/// queda es el MÍNIMO de las dos (lo que más tiempo haya visto pasar): si el reloj monotónico no avanzó durante una suspensión,
/// manda la pared; si alguien atrasa la hora, manda el monotónico. Al volver de suspender (<see cref="OnResume"/>) las dos se
/// realinean; al cambiar la hora del sistema (<see cref="OnClockChanged"/>) la de pared sigue a la monotónica.</item>
/// <item><b>Reinicio de la app:</b> <see cref="Save"/> guarda la hora de fin; <see cref="Restore"/> la convierte en ancla
/// monotónica nueva. Si acabó con la app cerrada, <see cref="Advance"/> lo cuenta al momento; si hace más de
/// <see cref="StaleAfter"/>, ya no: el ciclo vuelve a empezar sin contar ni avisar.</item>
/// <item><b>Fases:</b> foco → descanso corto, salvo cada <see cref="FocusDurations.LongEvery"/> focos, que toca el largo (y el ciclo
/// vuelve a 0); tras un descanso, foco. Al acabar una fase la siguiente queda PARADA con la oferta a la vista
/// (<see cref="Finished"/>).</item>
/// <item><b>Saltar:</b> la fase acaba ya sin contar (un foco saltado no suma) y pasa a la siguiente, corriendo si se corría.
/// <b>Reiniciar:</b> con el reloj en marcha o en pausa, la fase vuelve a su principio; parado, todo el ciclo vuelve a empezar.</item>
/// <item><b>Días:</b> cada foco cuenta en el día local en que ACABÓ (<see cref="Today"/>; a medianoche, hoy vuelve a 0 sin tocar
/// nada). Tras <see cref="StaleAfter"/> sin tocarlo (ni una acción ni el fin de una fase), una oferta pendiente, un ciclo a
/// medias o una pausa olvidada caducan y el ciclo vuelve a empezar (<see cref="Expire"/>; <see cref="ExpiresIn"/> dice cuándo). Se guardan los focos de los últimos <see cref="HistoryDays"/> días (estadísticas, Pro).</item>
/// </list>
/// </summary>
public sealed class PomodoroTimer
{
    /// <summary>Una fase que acabó hace más que esto (PC apagado, app cerrada) ya no se cuenta ni se avisa; una oferta pendiente
    /// más vieja caduca y el ciclo vuelve a empezar.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

    /// <summary>Días de historial que se guardan.</summary>
    public const int HistoryDays = 14;

    /// <summary>Días de la semana de las estadísticas (hoy incluido).</summary>
    public const int WeekDays = 7;

    private readonly List<FocusDay> _days = new();
    private FocusDurations _durations;
    private TimeSpan _length;       // lo que dura la fase en curso
    private TimeSpan _endsMono;     // corriendo: ancla monotónica del fin
    private DateTime _endsUtc;      // corriendo: ancla de pared del fin (la que se guarda)
    private TimeSpan _remaining;    // en pausa: lo que quedaba

    public PomodoroTimer(FocusDurations? durations = null)
    {
        _durations = (durations ?? FocusDurations.Default).Normalize();
        _length = _durations.For(FocusPhase.Focus);
    }

    public FocusDurations Durations => _durations;
    public FocusPhase Phase { get; private set; }
    public FocusRun Run { get; private set; }

    /// <summary>Lo que dura la fase en curso (la de las duraciones de cuando empezó).</summary>
    public TimeSpan Length => _length;

    /// <summary>Focos completos desde el último descanso largo.</summary>
    public int Cycle { get; private set; }

    /// <summary>La fase que acaba de terminar (la oferta de empezar la siguiente); null si no hay oferta.</summary>
    public FocusPhase? Finished { get; private set; }

    /// <summary>La tarea enlazada (Pro): cada foco completo se apunta en ella.</summary>
    public Guid? TaskId { get; private set; }

    /// <summary>Cuándo se tocó por última vez (UTC): una acción o el fin de una fase; null si nunca.</summary>
    public DateTime? LastActivityUtc { get; private set; }

    /// <summary>En marcha o en pausa: el reposo enseña el mini anillo.</summary>
    public bool Active => Run != FocusRun.Ready;

    /// <summary>Cambia con cada cambio de estado (para cachés de texto de quien pinta).</summary>
    public int Version { get; private set; }

    /// <summary>Los días con focos, del más antiguo al más reciente (como mucho <see cref="HistoryDays"/>).</summary>
    public IReadOnlyList<FocusDay> Days => _days;

    // ---------------------------------------------------------------- lectura

    /// <summary>Lo que queda de la fase en [0, <see cref="Length"/>]: parado, la fase entera; en pausa, lo que quedaba; corriendo,
    /// el mínimo de las dos anclas.</summary>
    public TimeSpan Remaining(FocusNow now) => Run switch
    {
        FocusRun.Running => Clamp(Min(_endsMono - now.Mono, _endsUtc - now.Utc), _length),
        FocusRun.Paused => _remaining,
        _ => _length,
    };

    /// <summary>Lo transcurrido de la fase en [0, 1].</summary>
    public double Progress(FocusNow now) =>
        _length <= TimeSpan.Zero ? 0 : Math.Clamp(1 - Remaining(now) / _length, 0, 1);

    /// <summary>Cuánto falta para que acabe la fase (para UN disparo); null si no corre.</summary>
    public TimeSpan? NextEndIn(FocusNow now) => Run == FocusRun.Running ? Remaining(now) : null;

    /// <summary>Cuánto falta para que caduque lo que hay parado o en pausa (<see cref="Expire"/>); null si no hay nada que caducar
    /// (corriendo, o parado al principio del ciclo).</summary>
    public TimeSpan? ExpiresIn(FocusNow now)
    {
        if (!CanExpire || LastActivityUtc is not { } last) return null;
        var d = last + StaleAfter - now.Utc;
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }

    /// <summary>¿Hay algo que pueda caducar? Parado a medio ciclo o con la oferta, o en pausa.</summary>
    private bool CanExpire => Run == FocusRun.Paused
        || (Run == FocusRun.Ready && (Phase != FocusPhase.Focus || Cycle != 0 || Finished is not null));

    /// <summary>Los focos de hoy (día local de <paramref name="local"/>); a medianoche vuelve a 0.</summary>
    public FocusDay Today(DateTime local)
    {
        var day = DateOnly.FromDateTime(local);
        return _days.FirstOrDefault(d => d.Date == day) ?? new FocusDay(day, 0, 0);
    }

    /// <summary>Los últimos <see cref="WeekDays"/> días hasta hoy, del más antiguo a hoy (los que no tienen focos, a 0).</summary>
    public IReadOnlyList<FocusDay> Week(DateTime local)
    {
        var today = DateOnly.FromDateTime(local);
        var week = new FocusDay[WeekDays];
        for (int i = 0; i < WeekDays; i++)
        {
            var date = today.AddDays(i - WeekDays + 1);
            week[i] = _days.FirstOrDefault(d => d.Date == date) ?? new FocusDay(date, 0, 0);
        }
        return week;
    }

    /// <summary>La fase que viene tras <paramref name="ended"/> con el ciclo <paramref name="cycle"/> (ya contado).</summary>
    public FocusPhase NextAfter(FocusPhase ended, int cycle) => ended == FocusPhase.Focus
        ? cycle >= _durations.LongEvery ? FocusPhase.LongBreak : FocusPhase.ShortBreak
        : FocusPhase.Focus;

    // ---------------------------------------------------------------- acciones

    /// <summary>El botón principal: parado, empieza; corriendo, pausa; en pausa, reanuda.</summary>
    public void Toggle(FocusNow now)
    {
        if (Run == FocusRun.Running) Pause(now);
        else Start(now);
    }

    /// <summary>Empieza la fase parada (con la oferta, la que toca) o reanuda la pausada. Corriendo, nada. Lo que ya caducó
    /// (<see cref="Expire"/>) empieza un ciclo nuevo.</summary>
    public void Start(FocusNow now)
    {
        if (Run == FocusRun.Running) return;
        Expire(now);
        StartCore(now);
    }

    /// <summary>Pausa: guarda lo que queda. Sin correr, nada.</summary>
    public void Pause(FocusNow now)
    {
        if (Run != FocusRun.Running) return;
        _remaining = Remaining(now);
        Run = FocusRun.Paused;
        LastActivityUtc = now.Utc;
        Version++;
    }

    /// <summary>Salta a la siguiente fase sin contar la de ahora (un foco saltado no suma a hoy ni al ciclo). Si corría, la
    /// siguiente empieza ya; si no, queda parada (sin oferta).</summary>
    public void Skip(FocusNow now)
    {
        bool running = Run == FocusRun.Running;
        var next = NextAfter(Phase, Cycle);
        if (Phase == FocusPhase.LongBreak) Cycle = 0;
        SetPhase(next);
        LastActivityUtc = now.Utc;
        if (running) StartCore(now);
        Version++;
    }

    /// <summary>Corriendo o en pausa: la fase vuelve a su principio, parada. Parado: el ciclo entero vuelve a empezar (foco,
    /// ciclo 0, sin oferta).</summary>
    public void Reset(FocusNow now)
    {
        if (Run == FocusRun.Ready) ResetCycle();
        else SetPhase(Phase);
        LastActivityUtc = now.Utc;
        Version++;
    }

    /// <summary>
    /// Si la fase que corre ya acabó, la cierra: cuenta el foco (en el día local en que acabó), deja la siguiente parada con la
    /// oferta y devuelve qué pasó. Una sola fase por llamada (la siguiente no arranca sola). Si acabó hace más de
    /// <see cref="StaleAfter"/>, el ciclo vuelve a empezar sin contar y devuelve null.
    /// </summary>
    public FocusEnd? Advance(FocusNow now)
    {
        if (Run != FocusRun.Running || Remaining(now) > TimeSpan.Zero) return null;
        var late = Max(TimeSpan.Zero, Max(now.Mono - _endsMono, now.Utc - _endsUtc));
        if (late > StaleAfter)
        {
            ResetCycle();
            Version++;
            return null;
        }
        var ended = Phase;
        int minutes = (int)Math.Round(_length.TotalMinutes);
        bool counted = ended == FocusPhase.Focus;
        if (counted)
        {
            Cycle++;
            AddDay(DateOnly.FromDateTime(now.Local - late), minutes);
        }
        var next = NextAfter(ended, Cycle);
        if (ended == FocusPhase.LongBreak) Cycle = 0;
        SetPhase(next);
        Finished = ended;
        LastActivityUtc = now.Utc - late;
        Version++;
        return new FocusEnd(ended, next, counted, counted ? TaskId : null, minutes, late);
    }

    /// <summary>
    /// Parado con una oferta pendiente (o a medio ciclo), o en pausa, y sin tocarlo desde hace más de <see cref="StaleAfter"/>:
    /// caduca y el ciclo vuelve a empezar (una pausa olvidada no tapa para siempre el reposo con su mini anillo). Lo llaman
    /// <see cref="Start"/> y quien enseña el pomodoro (al restaurar, al volver de suspender, al abrir la página y con un disparo a
    /// <see cref="ExpiresIn"/>). true si cambió algo.
    /// </summary>
    public bool Expire(FocusNow now)
    {
        if (!CanExpire || LastActivityUtc is not { } last || now.Utc - last <= StaleAfter) return false;
        ResetCycle();
        Version++;
        return true;
    }

    /// <summary>Vuelta de una suspensión: lo que queda es el mínimo de las dos anclas (el monotónico pudo no avanzar dormido) y
    /// las dos se realinean ahí.</summary>
    public void OnResume(FocusNow now)
    {
        if (Run != FocusRun.Running) return;
        // Sin acotar por abajo: si acabó dormido, las anclas siguen diciendo cuándo (Advance cuenta el retraso).
        Anchor(now, Min(Min(_endsMono - now.Mono, _endsUtc - now.Utc), _length));
        Version++;
    }

    /// <summary>Cambió la hora del sistema: manda el monotónico (lo que de verdad pasó) y la hora de fin guardada lo sigue.</summary>
    public void OnClockChanged(FocusNow now)
    {
        if (Run != FocusRun.Running) return;
        Anchor(now, Min(_endsMono - now.Mono, _length));
        Version++;
    }

    /// <summary>Duraciones nuevas: valen para las fases que empiecen después; la parada se ajusta ya, la que corre o está en
    /// pausa sigue con la suya. true si cambió algo.</summary>
    public bool SetDurations(FocusDurations durations)
    {
        ArgumentNullException.ThrowIfNull(durations);
        var d = durations.Normalize();
        if (d == _durations) return false;
        _durations = d;
        if (Run == FocusRun.Ready) _length = d.For(Phase);
        Version++;
        return true;
    }

    /// <summary>Enlaza (o con null, desenlaza) la tarea en la que se apuntan los focos. true si cambió.</summary>
    public bool Link(Guid? taskId)
    {
        if (taskId == TaskId) return false;
        TaskId = taskId;
        Version++;
        return true;
    }

    // ---------------------------------------------------------------- guardar y restaurar

    /// <summary>Lo que se guarda: corriendo, la hora de FIN (no lo que queda).</summary>
    public FocusState Save() => new(Phase, Run, Run == FocusRun.Running ? _endsUtc : null,
        Run == FocusRun.Paused ? _remaining : null, _length, Cycle, Finished, TaskId, LastActivityUtc, _days.ToArray());

    /// <summary>
    /// El pomodoro guardado en otra ejecución, con las anclas de ahora: corriendo, el ancla monotónica es lo que la hora de fin
    /// deja (acotado a la fase); lo raro se corrige (fase o estado desconocidos, duraciones fuera de rango, días repetidos).
    /// Si ya acabó, el siguiente <see cref="Advance"/> lo cierra.
    /// </summary>
    public static PomodoroTimer Restore(FocusState? state, FocusDurations? durations, FocusNow now)
    {
        var t = new PomodoroTimer(durations);
        if (state is null) return t;
        t.Phase = Enum.IsDefined(state.Phase) ? state.Phase : FocusPhase.Focus;
        t._length = state.Length is { } len && len >= TimeSpan.FromMinutes(FocusDurations.MinMinutes)
            && len <= TimeSpan.FromMinutes(FocusDurations.MaxMinutes) ? len : t._durations.For(t.Phase);
        t.Cycle = Math.Clamp(state.Cycle, 0, FocusDurations.MaxEvery);
        t.Finished = state.Finished is { } f && Enum.IsDefined(f) ? f : null;
        t.TaskId = state.TaskId is { } id && id != Guid.Empty ? id : null;
        // Sin fecha (a mano, de otra versión): cuenta desde ahora, así una pausa o una oferta restauradas también caducan.
        t.LastActivityUtc = state.LastActivityUtc is { } last ? DateTime.SpecifyKind(last, DateTimeKind.Utc) : now.Utc;
        foreach (var d in (state.Days ?? Array.Empty<FocusDay>())
                     .Where(d => d is not null && d.Focuses > 0).GroupBy(d => d.Date).Select(g => g.Last()).OrderBy(d => d.Date))
            t._days.Add(d with { Minutes = Math.Max(0, d.Minutes) });
        t.Prune();

        switch (state.Run)
        {
            case FocusRun.Running when state.EndsAtUtc is { } ends:
                t.Run = FocusRun.Running;
                t._endsUtc = DateTime.SpecifyKind(ends, DateTimeKind.Utc);
                var untilEnd = t._endsUtc - now.Utc;
                // Si la hora se atrasó con la app cerrada, la fase no puede durar más de lo que dura; y la hora de fin (la que se
                // guarda) se realinea ahí: si no, cada arranque volvería a acotar desde la fase entera.
                if (untilEnd > t._length) t.Anchor(now, t._length);
                else t._endsMono = now.Mono + Clamp(untilEnd, t._length);
                break;
            case FocusRun.Paused when state.Remaining is { } left:
                t.Run = FocusRun.Paused;
                t._remaining = Clamp(left, t._length);
                break;
            default:
                // Parada (o corriendo sin hora de fin, en pausa sin lo que quedaba): en su principio, con las duraciones de ahora.
                t.Run = FocusRun.Ready;
                t._length = t._durations.For(t.Phase);
                break;
        }
        return t;
    }

    // ---------------------------------------------------------------- interno

    private void StartCore(FocusNow now)
    {
        var left = Run == FocusRun.Paused ? _remaining : _length;
        Run = FocusRun.Running;
        Finished = null;
        LastActivityUtc = now.Utc;
        Anchor(now, left);
        Version++;
    }

    private void Anchor(FocusNow now, TimeSpan left)
    {
        _endsMono = now.Mono + left;
        _endsUtc = now.Utc + left;
    }

    /// <summary>Esa fase, parada en su principio y sin oferta.</summary>
    private void SetPhase(FocusPhase phase)
    {
        Phase = phase;
        Run = FocusRun.Ready;
        Finished = null;
        _length = _durations.For(phase);
        _remaining = TimeSpan.Zero;
    }

    private void ResetCycle()
    {
        Cycle = 0;
        SetPhase(FocusPhase.Focus);
    }

    private void AddDay(DateOnly date, int minutes)
    {
        int i = _days.FindIndex(d => d.Date == date);
        if (i >= 0) _days[i] = _days[i] with { Focuses = _days[i].Focuses + 1, Minutes = _days[i].Minutes + minutes };
        else
        {
            _days.Add(new FocusDay(date, 1, minutes));
            _days.Sort((a, b) => a.Date.CompareTo(b.Date));
        }
        Prune();
    }

    /// <summary>Solo los <see cref="HistoryDays"/> días más recientes.</summary>
    private void Prune()
    {
        if (_days.Count > HistoryDays) _days.RemoveRange(0, _days.Count - HistoryDays);
    }

    private static TimeSpan Clamp(TimeSpan v, TimeSpan max) => v < TimeSpan.Zero ? TimeSpan.Zero : v > max ? max : v;
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>
/// Lo que se enseña de la cuenta atrás y cuándo cambia. PURO. La página Foco va por segundos («24:59»); el notch cerrado, por
/// minutos («25 min»), y por segundos solo en el último minuto: así, cerrado, se repinta una vez por minuto.
/// </summary>
public static class FocusClock
{
    /// <summary>Margen para despertar justo DESPUÉS del cambio (los timers pueden adelantarse unos ms).</summary>
    public static readonly TimeSpan WakeSlack = TimeSpan.FromMilliseconds(15);

    /// <summary>Segundos que se enseñan: hacia arriba (al empezar, «25:00»; en el último segundo, «00:01»; al acabar, «00:00»).</summary>
    public static int ShownSeconds(TimeSpan remaining) =>
        remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalSeconds - 1e-6);

    /// <summary>«mm:ss» (o «h:mm:ss» desde una hora), con los segundos de <see cref="ShownSeconds"/>.</summary>
    public static string Clock(TimeSpan remaining)
    {
        int s = ShownSeconds(remaining);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
    }

    /// <summary>Cuánto falta para que cambie el segundo que se enseña (+ <see cref="WakeSlack"/>); null sin nada que contar.</summary>
    public static TimeSpan? NextSecondIn(TimeSpan remaining)
    {
        int s = ShownSeconds(remaining);
        if (s <= 0) return null;
        return remaining - TimeSpan.FromSeconds(s - 1) + WakeSlack;
    }

    /// <summary>Lo que se enseña con el notch cerrado: minutos hacia arriba mientras quede más de un minuto; luego, segundos.</summary>
    public static (int Value, bool Minutes) Compact(TimeSpan remaining)
    {
        int s = ShownSeconds(remaining);
        return s > 60 ? ((s + 59) / 60, true) : (s, false);
    }

    /// <summary>Lo que queda tal como se enseña cerrado (el arco del mini anillo va con el texto: cambian a la vez).</summary>
    public static TimeSpan CompactShown(TimeSpan remaining)
    {
        var (v, minutes) = Compact(remaining);
        return minutes ? TimeSpan.FromMinutes(v) : TimeSpan.FromSeconds(v);
    }

    /// <summary>Cuánto falta para que cambie lo que se enseña cerrado (+ <see cref="WakeSlack"/>); null sin nada que contar.</summary>
    public static TimeSpan? NextCompactIn(TimeSpan remaining)
    {
        var (v, minutes) = Compact(remaining);
        if (v <= 0) return null;
        if (!minutes) return NextSecondIn(remaining);
        // Baja al minuto anterior (o, en el último, pasa a segundos) cuando quede (v − 1) minutos.
        return remaining - TimeSpan.FromMinutes(v - 1) + WakeSlack;
    }

    /// <summary>Cuánto falta para la próxima medianoche local de <paramref name="zone"/> (+ <see cref="WakeSlack"/>): ahí «hoy»
    /// vuelve a 0. Por UTC, así un cambio de horario esa noche no adelanta ni retrasa el despertar; si la medianoche no existe
    /// (el horario de verano empieza a las 00:00), la primera hora que sí.</summary>
    public static TimeSpan NextMidnightIn(DateTime utcNow, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var midnight = TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Date.AddDays(1);
        while (zone.IsInvalidTime(midnight)) midnight = midnight.AddMinutes(30);
        var d = TimeZoneInfo.ConvertTimeToUtc(midnight, zone) - utc;
        return (d < TimeSpan.Zero ? TimeSpan.Zero : d) + WakeSlack;
    }

    /// <summary>Lo transcurrido en [0, 1] con lo que se enseña (<paramref name="shown"/>) sobre lo que dura la fase.</summary>
    public static float Progress(TimeSpan shown, TimeSpan length) =>
        length <= TimeSpan.Zero ? 0f : (float)Math.Clamp(1 - shown / length, 0, 1);
}
