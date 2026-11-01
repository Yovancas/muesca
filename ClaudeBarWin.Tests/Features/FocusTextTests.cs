using System.Reflection;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Los textos del pomodoro (<see cref="FocusText"/>), ya formateados para la página, el notch cerrado y el aviso del fin de fase,
/// y que estén en TODOS los idiomas.
/// </summary>
public class FocusTextTests
{
    private static readonly Strings Es = Localization.Get("es");
    private static readonly DateOnly Day = new(2026, 10, 30);
    private static readonly IReadOnlyList<FocusDay> Week = new[]
    {
        new FocusDay(Day.AddDays(-2), 4, 100), new FocusDay(Day.AddDays(-1), 3, 75), new FocusDay(Day, 2, 50),
    };

    private static FocusTexts Build(FocusPhase phase = FocusPhase.Focus, FocusRun run = FocusRun.Running, FocusPhase? finished = null,
        double seconds = 1470, int cycle = 1, FocusDay? today = null, FocusTaskInfo? task = null, bool anyPending = true, Strings? s = null) =>
        FocusText.Build(phase, run, finished, TimeSpan.FromSeconds(seconds), cycle, 4, today ?? new FocusDay(Day, 2, 50), Week, task,
            anyPending, s ?? Es);

    [Fact]
    public void A_running_focus_reads_naturally()
    {
        var t = Build(task: new FocusTaskInfo("Escribir el informe", 2, 3));
        Assert.Equal("24:30", t.Clock);
        Assert.Equal("25 min", t.Compact);
        Assert.Equal("Foco", t.Header);
        Assert.Equal("2 focos hoy · 50 min", t.Today);
        Assert.Equal("Foco · 2 de 4", t.PhaseLine);
        Assert.False(t.PhaseAccent);
        Assert.Equal("Pausa", t.Primary);
        Assert.Equal("Saltar a la siguiente fase", t.SkipHint);
        Assert.Equal("Reiniciar esta fase", t.ResetHint);
        Assert.Equal("Escribir el informe", t.TaskLine);
        Assert.False(t.TaskMuted);
        Assert.Equal("3", t.TaskCount);
        Assert.Equal("Esta semana: 9 focos · 3 h 45 min", t.WeekLine);
        Assert.Equal("Enlazar con una tarea", t.LockedTask);
        Assert.Equal("Estadísticas de la semana", t.LockedStats);
    }

    [Fact]
    public void Paused_stopped_and_the_offer_change_the_line_and_the_button()
    {
        var paused = Build(run: FocusRun.Paused);
        Assert.Equal("Foco · 2 de 4 · en pausa", paused.PhaseLine);
        Assert.Equal("Reanudar", paused.Primary);

        var ready = Build(run: FocusRun.Ready, seconds: 1500, cycle: 0);
        Assert.Equal("Empezar", ready.Primary);
        Assert.Equal("Foco · 1 de 4", ready.PhaseLine);
        Assert.Equal("25:00", ready.Clock);
        Assert.Equal("Empezar el ciclo de nuevo", ready.ResetHint);

        var breakOffer = Build(FocusPhase.ShortBreak, FocusRun.Ready, FocusPhase.Focus, seconds: 300);
        Assert.Equal("¡Foco terminado!", breakOffer.PhaseLine);
        Assert.True(breakOffer.PhaseAccent);
        Assert.Equal("Empezar descanso", breakOffer.Primary);

        var focusOffer = Build(FocusPhase.Focus, FocusRun.Ready, FocusPhase.LongBreak, seconds: 1500, cycle: 0);
        Assert.Equal("Fin del descanso", focusOffer.PhaseLine);
        Assert.Equal("Empezar foco", focusOffer.Primary);

        Assert.Equal("Descanso largo", Build(FocusPhase.LongBreak).PhaseLine);
        Assert.Equal("Descanso corto · en pausa", Build(FocusPhase.ShortBreak, FocusRun.Paused).PhaseLine);
        Assert.Equal("Foco · 4 de 4", Build(cycle: 7).PhaseLine);   // nunca «8 de 4»
    }

    [Fact]
    public void Without_a_linked_task_it_invites_to_link_one_or_says_there_are_none()
    {
        var invite = Build(anyPending: true);
        Assert.Equal("Enlazar con una tarea", invite.TaskLine);
        Assert.True(invite.TaskMuted);
        Assert.Null(invite.TaskCount);
        Assert.Equal("Sin tareas pendientes", Build(anyPending: false).TaskLine);
        Assert.Null(Build(task: new FocusTaskInfo("x", 0, 0)).TaskCount);
    }

    [Fact]
    public void Today_and_the_closed_notch_use_singular_minutes_and_seconds()
    {
        Assert.Equal("1 foco hoy · 25 min", Build(today: new FocusDay(Day, 1, 25)).Today);
        Assert.Equal("0 focos hoy", Build(today: new FocusDay(Day, 0, 0)).Today);
        Assert.Equal("42 s", Build(seconds: 41.2).Compact);
        Assert.Equal("60 s", Build(seconds: 60).Compact);
        Assert.Equal("2 min", Build(seconds: 61).Compact);
        Assert.Equal("1 focus session today · 25 min", Build(today: new FocusDay(Day, 1, 25), s: Localization.Get("en")).Today);
    }

    [Theory]
    [InlineData("es", 45, "45 min")]
    [InlineData("es", 120, "2 h")]
    [InlineData("es", 75, "1 h 15 min")]
    [InlineData("de", 75, "1 Std. 15 Min.")]
    [InlineData("ja", 75, "1 時間 15 分")]
    [InlineData("en", 0, "0 min")]
    public void Durations_read_in_hours_and_minutes(string lang, int minutes, string expected) =>
        Assert.Equal(expected, FocusText.Duration(minutes, Localization.Get(lang)));

    [Theory]
    [InlineData("es", "Siguiente: descanso corto")]
    [InlineData("en", "Up next: short break")]
    [InlineData("de", "Als Nächstes: Kurze Pause")]   // los sustantivos alemanes, en mayúscula
    [InlineData("ja", "次: 短い休憩")]
    public void The_alert_says_what_comes_next(string lang, string expected)
    {
        var s = Localization.Get(lang);
        Assert.Equal(expected, FocusText.AlertDetail(FocusPhase.ShortBreak, s));
        Assert.Equal(s.NotchFocusDoneFocus, FocusText.AlertTitle(FocusPhase.Focus, s));
        Assert.Equal(s.NotchFocusDoneBreak, FocusText.AlertTitle(FocusPhase.LongBreak, s));
        Assert.Equal(s.NotchFocusStartBreak, FocusText.AlertButton(FocusPhase.ShortBreak, s));
        Assert.Equal(s.NotchFocusStartFocus, FocusText.AlertButton(FocusPhase.Focus, s));
    }

    [Fact]
    public void The_alert_carries_its_phases_as_data_and_tolerates_garbage()
    {
        Assert.Equal((FocusPhase.Focus, FocusPhase.LongBreak), FocusText.AlertPhases("Focus", "LongBreak"));
        Assert.Equal((FocusPhase.ShortBreak, FocusPhase.Focus), FocusText.AlertPhases("ShortBreak", "Focus"));
        Assert.Equal((FocusPhase.Focus, FocusPhase.ShortBreak), FocusText.AlertPhases("???", null));
        Assert.Equal((FocusPhase.Focus, FocusPhase.ShortBreak), FocusText.AlertPhases("42", "-1"));
    }

    // ---------------------------------------------------------------- idiomas

    private static IEnumerable<PropertyInfo> FocusProperties() =>
        typeof(Strings).GetProperties().Where(p => p.PropertyType == typeof(string)
            && (p.Name.StartsWith("NotchFocus", StringComparison.Ordinal) || p.Name == nameof(Strings.NotchPageFocus)));

    /// <summary>Frases (no nombres ni unidades) que ningún idioma puede dejar en inglés.</summary>
    private static readonly string[] Sentences =
    {
        nameof(Strings.NotchFocusPhaseShort), nameof(Strings.NotchFocusPhaseLong), nameof(Strings.NotchFocusResume),
        nameof(Strings.NotchFocusStartBreak), nameof(Strings.NotchFocusStartFocus), nameof(Strings.NotchFocusSkipHint),
        nameof(Strings.NotchFocusResetPhase), nameof(Strings.NotchFocusResetCycle), nameof(Strings.NotchFocusDoneFocus),
        nameof(Strings.NotchFocusDoneBreak), nameof(Strings.NotchFocusTodayOne), nameof(Strings.NotchFocusTodayFmt),
        nameof(Strings.NotchFocusLinkTask), nameof(Strings.NotchFocusNoTasks), nameof(Strings.NotchFocusStats),
        nameof(Strings.NotchFocusWeekFmt), nameof(Strings.NotchFocusWeekOneFmt), nameof(Strings.NotchFocusNextFmt),
        nameof(Strings.NotchFocusSoundSetting),
        nameof(Strings.NotchFocusLength), nameof(Strings.NotchFocusLongEvery),
    };

    [Fact]
    public void Every_pomodoro_text_exists_in_every_language_and_the_sentences_are_translated()
    {
        var en = Localization.Get("en");
        var props = FocusProperties().ToList();
        Assert.True(props.Count >= 31, $"solo {props.Count} textos");
        foreach (var (code, _) in Localization.Languages)
        {
            var s = Localization.Get(code);
            foreach (var p in props) Assert.False(string.IsNullOrWhiteSpace((string)p.GetValue(s)!), $"{code}.{p.Name} vacío");
            if (code == "en") continue;
            foreach (var name in Sentences)
            {
                var p = typeof(Strings).GetProperty(name)!;
                Assert.True((string)p.GetValue(s)! != (string)p.GetValue(en)!, $"{code}.{name} cae al inglés");
            }
        }
    }

    [Fact]
    public void Every_format_takes_its_arguments_in_every_language()
    {
        foreach (var (code, _) in Localization.Languages)
        {
            var s = Localization.Get(code);
            foreach (var fmt in new[] { s.NotchFocusTodayFmt, s.NotchFocusMinutesFmt, s.NotchFocusSecondsFmt, s.NotchFocusHoursFmt,
                         s.NotchFocusNextFmt })
            {
                Assert.Contains("{0}", fmt);
                _ = string.Format(s.Culture, fmt, 3);   // no lanza
            }
            foreach (var fmt in new[] { s.NotchFocusCycleFmt, s.NotchFocusWeekFmt })
            {
                Assert.Contains("{0}", fmt);
                Assert.Contains("{1}", fmt);
                _ = string.Format(s.Culture, fmt, 2, 4);
            }
            // Los textos de un estado cualquiera se forman sin lanzar en todos los idiomas.
            var t = Build(FocusPhase.ShortBreak, FocusRun.Paused, s: s);
            Assert.False(string.IsNullOrWhiteSpace(t.PhaseLine));
        }
    }

    [Fact]
    public void One_focus_this_week_is_singular_in_every_language()
    {
        var one = new[] { new FocusDay(Day, 1, 25) };
        var t = FocusText.Build(FocusPhase.Focus, FocusRun.Ready, null, TimeSpan.FromMinutes(25), 0, 4, new FocusDay(Day, 1, 25), one,
            null, true, Es);
        Assert.Equal("Esta semana: 1 foco · 25 min", t.WeekLine);
        var en = FocusText.Build(FocusPhase.Focus, FocusRun.Ready, null, TimeSpan.FromMinutes(25), 0, 4, new FocusDay(Day, 1, 25), one,
            null, true, Localization.Get("en"));
        Assert.Equal("This week: 1 session · 25 min", en.WeekLine);
        foreach (var (code, _) in Localization.Languages)
        {
            var s = Localization.Get(code);
            var line = FocusText.Build(FocusPhase.Focus, FocusRun.Ready, null, TimeSpan.FromMinutes(25), 0, 4, new FocusDay(Day, 1, 25),
                one, null, true, s).WeekLine;
            Assert.Equal(string.Format(s.Culture, s.NotchFocusWeekOneFmt, FocusText.Duration(25, s)), line);
        }
    }
}