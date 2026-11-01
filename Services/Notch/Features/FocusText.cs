namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>La tarea enlazada tal como se enseña: título, color (índice de la paleta) y focos ya apuntados en ella.</summary>
public readonly record struct FocusTaskInfo(string Title, int Color, int Focuses);

/// <summary>
/// Los textos del pomodoro YA formateados (<see cref="FocusText.Build"/>): el reloj de la página («24:59») y el del notch
/// cerrado («25 min»), la cabecera y los focos de hoy, la línea de la fase (o «¡Foco terminado!» con <see cref="PhaseAccent"/>),
/// las pistas de saltar y reiniciar (su tooltip, con el ratón encima del icono), el botón principal, la fila de la tarea
/// (<see cref="TaskMuted"/>: sin tarea enlazada) con sus focos, la semana y las filas con el chip «Pro». Se rehacen cuando cambia
/// lo que se ve (el segundo, el minuto, el estado o el idioma), nunca por frame.
/// </summary>
public sealed record FocusTexts(string Clock, string Compact, string Header, string Today, string PhaseLine, bool PhaseAccent,
    string SkipHint, string ResetHint, string Primary, string TaskLine, bool TaskMuted, string? TaskCount, string WeekLine,
    string LockedTask, string LockedStats)
{
    /// <summary>El nombre que enseña el tooltip del notch sobre el icono de saltar o reiniciar (null: ese control no lleva).</summary>
    public string? TipFor(FocusControl control) => control switch
    {
        FocusControl.Skip => SkipHint,
        FocusControl.Reset => ResetHint,
        _ => null,
    };
}

/// <summary>Los textos del pomodoro. PURO: estado, idioma y lo que queda, por parámetro.</summary>
public static class FocusText
{
    /// <summary>Todos los textos de la página y del notch cerrado para este estado.</summary>
    public static FocusTexts Build(FocusPhase phase, FocusRun run, FocusPhase? finished, TimeSpan remaining, int cycle, int every,
        FocusDay today, IReadOnlyList<FocusDay> week, FocusTaskInfo? task, bool anyPendingTask, Strings s)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(s);
        var ci = s.Culture;

        string phaseLine;
        bool accent = finished is not null;
        if (finished is { } done) phaseLine = done == FocusPhase.Focus ? s.NotchFocusDoneFocus : s.NotchFocusDoneBreak;
        else
        {
            phaseLine = PhaseName(phase, s);
            if (phase == FocusPhase.Focus)
                phaseLine += " · " + string.Format(ci, s.NotchFocusCycleFmt, Math.Clamp(cycle + 1, 1, Math.Max(1, every)), every);
            if (run == FocusRun.Paused) phaseLine += " · " + s.NotchFocusPaused;
        }

        string primary = finished is not null
            ? phase == FocusPhase.Focus ? s.NotchFocusStartFocus : s.NotchFocusStartBreak
            : run switch
            {
                FocusRun.Running => s.NotchFocusPause,
                FocusRun.Paused => s.NotchFocusResume,
                _ => s.NotchFocusStart,
            };

        string todayText = today.Focuses == 1 ? s.NotchFocusTodayOne : string.Format(ci, s.NotchFocusTodayFmt, today.Focuses);
        if (today.Minutes > 0) todayText += " · " + Duration(today.Minutes, s);

        int weekFocuses = week.Sum(d => d.Focuses), weekMinutes = week.Sum(d => d.Minutes);
        string weekLine = weekFocuses == 1
            ? string.Format(ci, s.NotchFocusWeekOneFmt, Duration(weekMinutes, s))
            : string.Format(ci, s.NotchFocusWeekFmt, weekFocuses, Duration(weekMinutes, s));

        return new FocusTexts(FocusClock.Clock(remaining), Compact(remaining, s), s.NotchPageFocus, todayText, phaseLine, accent,
            s.NotchFocusSkipHint, run == FocusRun.Ready ? s.NotchFocusResetCycle : s.NotchFocusResetPhase, primary,
            task is { } t ? t.Title : anyPendingTask ? s.NotchFocusLinkTask : s.NotchFocusNoTasks, task is null,
            task is { Focuses: > 0 } tf ? tf.Focuses.ToString(ci) : null, weekLine, s.NotchFocusLinkTask, s.NotchFocusStats);
    }

    /// <summary>Nombre de la fase («Foco», «Descanso corto», «Descanso largo»).</summary>
    public static string PhaseName(FocusPhase phase, Strings s) => phase switch
    {
        FocusPhase.ShortBreak => s.NotchFocusPhaseShort,
        FocusPhase.LongBreak => s.NotchFocusPhaseLong,
        _ => s.NotchFocusPhaseFocus,
    };

    /// <summary>Lo que queda con el notch cerrado: «25 min» o, en el último minuto, «42 s» (<see cref="FocusClock.Compact"/>).</summary>
    public static string Compact(TimeSpan remaining, Strings s)
    {
        var (v, minutes) = FocusClock.Compact(remaining);
        return string.Format(s.Culture, minutes ? s.NotchFocusMinutesFmt : s.NotchFocusSecondsFmt, v);
    }

    /// <summary>Un tiempo en minutos: «45 min», «2 h», «1 h 15 min».</summary>
    public static string Duration(int minutes, Strings s)
    {
        minutes = Math.Max(0, minutes);
        var ci = s.Culture;
        if (minutes < 60) return string.Format(ci, s.NotchFocusMinutesFmt, minutes);
        string h = string.Format(ci, s.NotchFocusHoursFmt, minutes / 60);
        return minutes % 60 == 0 ? h : h + " " + string.Format(ci, s.NotchFocusMinutesFmt, minutes % 60);
    }

    /// <summary>Título del aviso del fin de fase.</summary>
    public static string AlertTitle(FocusPhase ended, Strings s) =>
        ended == FocusPhase.Focus ? s.NotchFocusDoneFocus : s.NotchFocusDoneBreak;

    /// <summary>Detalle del aviso: lo que toca ahora («Siguiente: descanso corto»).</summary>
    public static string AlertDetail(FocusPhase next, Strings s)
    {
        string name = PhaseName(next, s);
        // En mitad de la frase, en minúscula donde el idioma lo hace (la cultura decide; en japonés, coreano y chino no cambia).
        if (name.Length > 0 && char.IsUpper(name[0]) && s.Culture.TwoLetterISOLanguageName is not "de")
            name = char.ToLower(name[0], s.Culture) + name[1..];
        return string.Format(s.Culture, s.NotchFocusNextFmt, name);
    }

    /// <summary>El botón del aviso: «Empezar descanso» o «Empezar foco».</summary>
    public static string AlertButton(FocusPhase next, Strings s) =>
        next == FocusPhase.Focus ? s.NotchFocusStartFocus : s.NotchFocusStartBreak;

    /// <summary>El aviso del fin de fase lleva como datos la fase que acabó y la que toca (sus nombres en el enum, no texto
    /// final): así se pinta en el idioma vigente aunque cambie mientras se ve.</summary>
    public static (FocusPhase Ended, FocusPhase Next) AlertPhases(string? title, string? detail) =>
        (Enum.TryParse<FocusPhase>(title, out var e) && Enum.IsDefined(e) ? e : FocusPhase.Focus,
         Enum.TryParse<FocusPhase>(detail, out var n) && Enum.IsDefined(n) ? n : FocusPhase.ShortBreak);
}
