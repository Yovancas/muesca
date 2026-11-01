using System.Text.Json;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// El pomodoro del notch (<see cref="PomodoroTimer"/>), todo PURO: fases y ciclo del descanso largo, pausa, saltar, reiniciar,
/// suspensión (el reloj monotónico que no avanza dormido), cambios de hora, reinicio de la app (guardar y restaurar), cambio de
/// día y lo que se enseña de la cuenta atrás (<see cref="FocusClock"/>).
/// </summary>
public class PomodoroTimerTests
{
    private static readonly DateTime Utc0 = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Local0 = new(2026, 10, 30, 10, 0, 0);

    /// <summary>Un reloj de prueba: pared (UTC y local, que avanzan juntas) y monotónico, que se pueden mover por separado.</summary>
    private sealed class Clock
    {
        public DateTime Utc = Utc0;
        public DateTime Local = Local0;
        public TimeSpan Mono = TimeSpan.FromHours(3);   // el proceso lleva un rato vivo

        public FocusNow Now => new(Utc, Mono, Local);

        /// <summary>Pasa el tiempo con la app despierta: avanzan los dos relojes.</summary>
        public FocusNow Pass(TimeSpan d)
        {
            Utc += d;
            Local += d;
            Mono += d;
            return Now;
        }

        public FocusNow Minutes(double m) => Pass(TimeSpan.FromMinutes(m));

        /// <summary>Suspensión con un monotónico que NO cuenta lo dormido: solo avanza la pared.</summary>
        public FocusNow Sleep(TimeSpan d)
        {
            Utc += d;
            Local += d;
            return Now;
        }

        /// <summary>Alguien cambia la hora del sistema: solo salta la pared.</summary>
        public FocusNow SetClock(TimeSpan jump)
        {
            Utc += jump;
            Local += jump;
            return Now;
        }
    }

    private static TimeSpan Min(double m) => TimeSpan.FromMinutes(m);

    /// <summary>Corre la fase actual hasta el final y la cierra.</summary>
    private static FocusEnd RunPhase(PomodoroTimer t, Clock c)
    {
        t.Start(c.Now);
        c.Pass(t.Remaining(c.Now));
        return Assert.IsType<FocusEnd>(t.Advance(c.Now));
    }

    // ---------------------------------------------------------------- duraciones

    [Fact]
    public void Defaults_are_25_5_15_every_4()
    {
        var d = FocusDurations.Default;
        Assert.Equal(Min(25), d.For(FocusPhase.Focus));
        Assert.Equal(Min(5), d.For(FocusPhase.ShortBreak));
        Assert.Equal(Min(15), d.For(FocusPhase.LongBreak));
        Assert.Equal(4, d.LongEvery);
        Assert.Contains(25, FocusDurations.FocusOptions);
        Assert.Contains(5, FocusDurations.ShortOptions);
        Assert.Contains(15, FocusDurations.LongOptions);
        Assert.Contains(4, FocusDurations.EveryOptions);
    }

    [Fact]
    public void Durations_out_of_range_are_clamped()
    {
        var d = new FocusDurations(0, -5, 999, 1).Normalize();
        Assert.Equal(new FocusDurations(1, 1, 180, 2), d);
        Assert.Equal(8, new FocusDurations(LongEvery: 50).Normalize().LongEvery);
        Assert.Equal(Min(180), new FocusDurations(FocusMinutes: 10_000).For(FocusPhase.Focus));
    }

    [Fact]
    public void A_new_timer_is_a_ready_focus()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(FocusRun.Ready, t.Run);
        Assert.False(t.Active);
        Assert.Equal(Min(25), t.Remaining(c.Now));
        Assert.Null(t.NextEndIn(c.Now));
        Assert.Null(t.Advance(c.Now));
        Assert.Equal(0, t.Today(c.Local).Focuses);
    }

    // ---------------------------------------------------------------- fases

    [Fact]
    public void A_focus_runs_down_and_ends_into_a_ready_short_break_with_the_offer()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        Assert.Equal(FocusRun.Running, t.Run);
        Assert.True(t.Active);
        Assert.Equal(Min(25), t.NextEndIn(c.Now));
        c.Minutes(10);
        Assert.Equal(Min(15), t.Remaining(c.Now));
        Assert.Equal(0.4, t.Progress(c.Now), 6);
        Assert.Null(t.Advance(c.Now));   // aún no ha acabado

        c.Minutes(15);
        var end = t.Advance(c.Now);
        Assert.NotNull(end);
        Assert.Equal(FocusPhase.Focus, end.Ended);
        Assert.Equal(FocusPhase.ShortBreak, end.Next);
        Assert.True(end.Counted);
        Assert.Equal(25, end.Minutes);
        Assert.Equal(TimeSpan.Zero, end.Late);

        Assert.Equal(FocusPhase.ShortBreak, t.Phase);
        Assert.Equal(FocusRun.Ready, t.Run);
        Assert.Equal(FocusPhase.Focus, t.Finished);
        Assert.Equal(Min(5), t.Remaining(c.Now));
        Assert.Equal(1, t.Cycle);
        Assert.Equal(new FocusDay(DateOnly.FromDateTime(c.Local), 1, 25), t.Today(c.Local));
        Assert.Null(t.Advance(c.Now));   // la siguiente no arranca sola
    }

    [Fact]
    public void Every_fourth_focus_leads_to_the_long_break_and_the_cycle_starts_again()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        var seen = new List<FocusPhase>();
        for (int i = 0; i < 9; i++)
        {
            seen.Add(t.Phase);
            RunPhase(t, c);
        }
        Assert.Equal(new[]
        {
            FocusPhase.Focus, FocusPhase.ShortBreak, FocusPhase.Focus, FocusPhase.ShortBreak, FocusPhase.Focus,
            FocusPhase.ShortBreak, FocusPhase.Focus, FocusPhase.LongBreak, FocusPhase.Focus,
        }, seen);
        Assert.Equal(FocusPhase.ShortBreak, t.Phase);   // el 5.º foco abre un ciclo nuevo
        Assert.Equal(1, t.Cycle);
        Assert.Equal(5, t.Today(c.Local).Focuses);
        Assert.Equal(125, t.Today(c.Local).Minutes);
    }

    [Fact]
    public void Breaks_do_not_count_as_focuses()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        var end = RunPhase(t, c);
        Assert.Equal(FocusPhase.ShortBreak, end.Ended);
        Assert.False(end.Counted);
        Assert.Equal(FocusPhase.Focus, end.Next);
        Assert.Equal(1, t.Today(c.Local).Focuses);
        Assert.Equal(FocusPhase.ShortBreak, t.Finished);
    }

    [Fact]
    public void The_long_break_period_follows_the_setting()
    {
        var c = new Clock();
        var t = new PomodoroTimer(new FocusDurations(LongEvery: 2));
        RunPhase(t, c);
        Assert.Equal(FocusPhase.ShortBreak, t.Phase);
        RunPhase(t, c);
        var end = RunPhase(t, c);
        Assert.Equal(FocusPhase.LongBreak, end.Next);
        Assert.Equal(Min(15), t.Remaining(c.Now));
        RunPhase(t, c);
        Assert.Equal(0, t.Cycle);
        Assert.Equal(FocusPhase.Focus, t.Phase);
    }

    // ---------------------------------------------------------------- pausa

    [Fact]
    public void Pause_freezes_what_is_left_whatever_time_passes()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(10);
        t.Pause(c.Now);
        Assert.Equal(FocusRun.Paused, t.Run);
        Assert.True(t.Active);
        Assert.Null(t.NextEndIn(c.Now));
        c.Minutes(60);
        c.Sleep(TimeSpan.FromHours(2));
        Assert.Equal(Min(15), t.Remaining(c.Now));
        Assert.Null(t.Advance(c.Now));

        t.Start(c.Now);   // reanudar
        Assert.Equal(FocusRun.Running, t.Run);
        Assert.Equal(Min(15), t.NextEndIn(c.Now));
        c.Minutes(15);
        Assert.NotNull(t.Advance(c.Now));
    }

    [Fact]
    public void Toggle_starts_pauses_and_resumes()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Toggle(c.Now);
        Assert.Equal(FocusRun.Running, t.Run);
        c.Minutes(1);
        t.Toggle(c.Now);
        Assert.Equal(FocusRun.Paused, t.Run);
        Assert.Equal(Min(24), t.Remaining(c.Now));
        t.Toggle(c.Now);
        Assert.Equal(FocusRun.Running, t.Run);
        Assert.Equal(Min(24), t.Remaining(c.Now));
    }

    [Fact]
    public void Pause_or_start_twice_does_nothing_more()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Pause(c.Now);
        Assert.Equal(FocusRun.Ready, t.Run);
        t.Start(c.Now);
        c.Minutes(5);
        t.Start(c.Now);   // ya corre: no vuelve a anclar
        Assert.Equal(Min(20), t.Remaining(c.Now));
    }

    [Fact]
    public void Starting_from_the_offer_clears_it()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        Assert.Equal(FocusPhase.Focus, t.Finished);
        t.Start(c.Now);
        Assert.Null(t.Finished);
        Assert.Equal(FocusPhase.ShortBreak, t.Phase);
        Assert.Equal(FocusRun.Running, t.Run);
    }

    // ---------------------------------------------------------------- saltar y reiniciar

    [Fact]
    public void Skipping_a_running_focus_does_not_count_it_and_the_break_starts()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(20);
        t.Skip(c.Now);
        Assert.Equal(FocusPhase.ShortBreak, t.Phase);
        Assert.Equal(FocusRun.Running, t.Run);
        Assert.Equal(Min(5), t.Remaining(c.Now));
        Assert.Equal(0, t.Cycle);
        Assert.Equal(0, t.Today(c.Local).Focuses);
        Assert.Null(t.Finished);
    }

    [Fact]
    public void Skipping_while_stopped_or_paused_leaves_the_next_phase_stopped()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Skip(c.Now);
        Assert.Equal(FocusPhase.ShortBreak, t.Phase);
        Assert.Equal(FocusRun.Ready, t.Run);
        t.Start(c.Now);
        c.Minutes(1);
        t.Pause(c.Now);
        t.Skip(c.Now);
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(FocusRun.Ready, t.Run);
        Assert.Equal(Min(25), t.Remaining(c.Now));
    }

    [Fact]
    public void Skipping_the_long_break_starts_a_new_cycle()
    {
        var c = new Clock();
        var t = new PomodoroTimer(new FocusDurations(LongEvery: 2));
        RunPhase(t, c);
        RunPhase(t, c);
        RunPhase(t, c);
        Assert.Equal(FocusPhase.LongBreak, t.Phase);
        Assert.Equal(2, t.Cycle);
        t.Skip(c.Now);
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(0, t.Cycle);
    }

    [Fact]
    public void Reset_rewinds_the_phase_and_from_stopped_the_whole_cycle()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        RunPhase(t, c);
        t.Start(c.Now);   // segundo foco
        c.Minutes(10);
        t.Reset(c.Now);
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(FocusRun.Ready, t.Run);
        Assert.Equal(Min(25), t.Remaining(c.Now));
        Assert.Equal(1, t.Cycle);   // el ciclo sigue

        t.Skip(c.Now);   // descanso parado
        t.Reset(c.Now);  // parado: todo el ciclo
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(0, t.Cycle);
        Assert.Null(t.Finished);
        Assert.Equal(1, t.Today(c.Local).Focuses);   // lo contado se queda
    }

    [Fact]
    public void Reset_clears_a_pending_offer()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        t.Reset(c.Now);
        Assert.Null(t.Finished);
        Assert.Equal(FocusPhase.Focus, t.Phase);
    }

    // ---------------------------------------------------------------- suspensión y hora del sistema

    [Fact]
    public void Sleeping_counts_even_if_the_monotonic_clock_stood_still()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(5);
        c.Sleep(Min(10));
        Assert.Equal(Min(10), t.Remaining(c.Now));   // manda la pared
        t.OnResume(c.Now);
        c.Minutes(4);
        Assert.Equal(Min(6), t.Remaining(c.Now));
    }

    [Fact]
    public void Sleeping_through_the_end_counts_the_focus_on_the_day_it_ended()
    {
        var c = new Clock { Local = new DateTime(2026, 10, 30, 23, 50, 0) };
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(5);
        c.Sleep(Min(40));   // le quedaban 20: acabó a las 00:15, dormido
        t.OnResume(c.Now);
        var end = t.Advance(c.Now);
        Assert.NotNull(end);
        Assert.True(end.Counted);
        Assert.Equal(Min(20), end.Late);
        // Empezó a las 23:50 y acabó a las 00:15: cuenta el día nuevo.
        Assert.Equal(1, t.Today(c.Local).Focuses);
        Assert.Equal(new DateOnly(2026, 10, 31), DateOnly.FromDateTime(c.Local));
        Assert.Equal(0, t.Today(new DateTime(2026, 10, 30, 23, 59, 0)).Focuses);
    }

    [Fact]
    public void A_focus_ended_just_before_midnight_counts_for_that_day_even_if_seen_after()
    {
        var c = new Clock { Local = new DateTime(2026, 10, 30, 23, 30, 0) };
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(25);     // acaba a las 23:55
        c.Sleep(Min(30));  // y se ve a las 00:25
        var end = t.Advance(c.Now);
        Assert.NotNull(end);
        Assert.Equal(1, t.Today(new DateTime(2026, 10, 30, 12, 0, 0)).Focuses);
        Assert.Equal(0, t.Today(c.Local).Focuses);
    }

    [Fact]
    public void Turning_the_clock_back_does_not_stretch_the_phase()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(5);
        c.SetClock(TimeSpan.FromHours(-1));
        Assert.Equal(Min(20), t.Remaining(c.Now));   // manda el monotónico
        t.OnClockChanged(c.Now);
        Assert.Equal(c.Utc + Min(20), t.Save().EndsAtUtc);   // la hora de fin guardada sigue a la nueva hora
        c.Minutes(20);
        Assert.NotNull(t.Advance(c.Now));
    }

    [Fact]
    public void Turning_the_clock_forward_is_undone_by_the_time_change_notice()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(5);
        c.SetClock(TimeSpan.FromHours(2));
        t.OnClockChanged(c.Now);   // llega WM_TIMECHANGE: se realinea con el monotónico
        Assert.Equal(Min(20), t.Remaining(c.Now));
        Assert.Null(t.Advance(c.Now));
        c.Minutes(19);
        Assert.Equal(Min(1), t.Remaining(c.Now));
    }

    [Fact]
    public void Resume_and_clock_notices_do_nothing_when_not_running()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(3);
        t.Pause(c.Now);
        c.Sleep(Min(30));
        t.OnResume(c.Now);
        t.OnClockChanged(c.SetClock(TimeSpan.FromHours(1)));
        Assert.Equal(Min(22), t.Remaining(c.Now));
        Assert.Equal(FocusRun.Paused, t.Run);
    }

    // ---------------------------------------------------------------- reinicio de la app

    [Fact]
    public void A_running_focus_survives_a_restart_of_the_app()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(10);
        var saved = t.Save();
        Assert.Equal(FocusRun.Running, saved.Run);
        Assert.Equal(Utc0 + Min(25), saved.EndsAtUtc);

        // La app se cierra 2 minutos y vuelve: proceso nuevo, monotónico desde 0.
        var c2 = new Clock { Utc = c.Utc + Min(2), Local = c.Local + Min(2), Mono = TimeSpan.FromSeconds(1) };
        var back = PomodoroTimer.Restore(saved, FocusDurations.Default, c2.Now);
        Assert.Equal(FocusRun.Running, back.Run);
        Assert.Equal(Min(13), back.Remaining(c2.Now));
        c2.Minutes(13);
        var end = back.Advance(c2.Now);
        Assert.NotNull(end);
        Assert.True(end.Counted);
    }

    [Fact]
    public void A_focus_that_ended_with_the_app_closed_is_counted_on_restore()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Link(Guid.NewGuid());
        t.Start(c.Now);
        var saved = t.Save();
        var c2 = new Clock { Utc = c.Utc + Min(40), Local = c.Local + Min(40), Mono = TimeSpan.Zero };
        var back = PomodoroTimer.Restore(saved, FocusDurations.Default, c2.Now);
        Assert.Equal(TimeSpan.Zero, back.Remaining(c2.Now));
        var end = back.Advance(c2.Now);
        Assert.NotNull(end);
        Assert.Equal(Min(15), end.Late);
        Assert.Equal(t.TaskId, end.TaskId);
        Assert.Equal(1, back.Today(c2.Local).Focuses);
        Assert.Equal(FocusPhase.Focus, back.Finished);
    }

    [Fact]
    public void A_phase_that_ended_long_ago_is_not_counted_and_the_cycle_restarts()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        RunPhase(t, c);
        t.Start(c.Now);
        var saved = t.Save();
        var c2 = new Clock { Utc = c.Utc + TimeSpan.FromHours(13), Local = c.Local + TimeSpan.FromHours(13), Mono = TimeSpan.Zero };
        var back = PomodoroTimer.Restore(saved, FocusDurations.Default, c2.Now);
        Assert.Null(back.Advance(c2.Now));
        Assert.Equal(FocusPhase.Focus, back.Phase);
        Assert.Equal(FocusRun.Ready, back.Run);
        Assert.Equal(0, back.Cycle);
        Assert.Null(back.Finished);
        Assert.Equal(1, back.Today(c.Local).Focuses);   // lo de antes se conserva
    }

    [Fact]
    public void A_paused_phase_and_the_offer_survive_a_restart()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        var offer = PomodoroTimer.Restore(t.Save(), FocusDurations.Default, c.Now);
        Assert.Equal(FocusPhase.ShortBreak, offer.Phase);
        Assert.Equal(FocusPhase.Focus, offer.Finished);
        Assert.Equal(1, offer.Cycle);

        t.Start(c.Now);
        c.Minutes(2);
        t.Pause(c.Now);
        var c2 = new Clock { Utc = c.Utc + TimeSpan.FromHours(5), Mono = TimeSpan.Zero };
        var back = PomodoroTimer.Restore(t.Save(), FocusDurations.Default, c2.Now);
        Assert.Equal(FocusRun.Paused, back.Run);
        Assert.Equal(Min(3), back.Remaining(c2.Now));
    }

    [Fact]
    public void Restoring_with_the_clock_set_back_never_makes_the_phase_longer()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        var c2 = new Clock { Utc = c.Utc - TimeSpan.FromDays(1), Mono = TimeSpan.Zero };
        var back = PomodoroTimer.Restore(t.Save(), FocusDurations.Default, c2.Now);
        Assert.Equal(Min(25), back.Remaining(c2.Now));
    }

    [Fact]
    public void Restoring_garbage_falls_back_to_something_valid()
    {
        var c = new Clock();
        var day = DateOnly.FromDateTime(c.Local);
        var junk = new FocusState((FocusPhase)42, (FocusRun)9, null, TimeSpan.FromMinutes(-3), TimeSpan.Zero, -3, (FocusPhase)7,
            Guid.Empty, null, new[] { new FocusDay(day, 2, -5), new FocusDay(day, 3, 75), new FocusDay(day.AddDays(-1), 0, 0) });
        var t = PomodoroTimer.Restore(junk, FocusDurations.Default, c.Now);
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(FocusRun.Ready, t.Run);
        Assert.Equal(Min(25), t.Remaining(c.Now));
        Assert.Equal(0, t.Cycle);
        Assert.Null(t.Finished);
        Assert.Null(t.TaskId);
        Assert.Single(t.Days);
        Assert.Equal(new FocusDay(day, 3, 75), t.Today(c.Local));

        // Corriendo sin hora de fin, o en pausa sin lo que quedaba: parada.
        Assert.Equal(FocusRun.Ready, PomodoroTimer.Restore(new FocusState(Run: FocusRun.Running), null, c.Now).Run);
        Assert.Equal(FocusRun.Ready, PomodoroTimer.Restore(new FocusState(Run: FocusRun.Paused), null, c.Now).Run);
        Assert.Equal(FocusRun.Ready, PomodoroTimer.Restore(null, null, c.Now).Run);
    }

    [Fact]
    public void The_saved_state_round_trips_through_json_like_config()
    {
        var c = new Clock();
        var t = new PomodoroTimer(new FocusDurations(50, 10, 20, 3));
        t.Link(Guid.NewGuid());
        RunPhase(t, c);
        t.Start(c.Now);
        c.Minutes(1);
        var json = JsonSerializer.Serialize(t.Save(), new JsonSerializerOptions { WriteIndented = true });
        var read = JsonSerializer.Deserialize<FocusState>(json)!;
        var back = PomodoroTimer.Restore(read, new FocusDurations(50, 10, 20, 3), c.Now);
        Assert.Equal(t.Phase, back.Phase);
        Assert.Equal(t.Run, back.Run);
        Assert.Equal(t.TaskId, back.TaskId);
        Assert.Equal(t.Cycle, back.Cycle);
        Assert.Equal(Min(9), back.Remaining(c.Now));
        Assert.Equal(t.Today(c.Local), back.Today(c.Local));
        Assert.Equal(FocusRun.Ready, JsonSerializer.Deserialize<FocusState>("{}")!.Run);
    }

    // ---------------------------------------------------------------- días, caducidad y duraciones

    [Fact]
    public void Today_resets_at_midnight_and_the_week_keeps_the_last_seven_days()
    {
        var c = new Clock { Local = new DateTime(2026, 10, 30, 22, 0, 0) };
        var t = new PomodoroTimer();
        RunPhase(t, c);
        RunPhase(t, c);
        RunPhase(t, c);
        Assert.Equal(2, t.Today(c.Local).Focuses);
        var tomorrow = new DateTime(2026, 10, 31, 0, 5, 0);
        Assert.Equal(0, t.Today(tomorrow).Focuses);
        var week = t.Week(tomorrow);
        Assert.Equal(PomodoroTimer.WeekDays, week.Count);
        Assert.Equal(new DateOnly(2026, 10, 31), week[^1].Date);
        Assert.Equal(0, week[^1].Focuses);
        Assert.Equal(2, week[^2].Focuses);
        Assert.Equal(new DateOnly(2026, 10, 25), week[0].Date);
    }

    [Fact]
    public void History_keeps_only_the_most_recent_days()
    {
        var c = new Clock();
        var t = new PomodoroTimer(new FocusDurations(FocusMinutes: 1, ShortMinutes: 1, LongMinutes: 1));
        for (int d = 0; d < 20; d++)
        {
            RunPhase(t, c);
            t.Reset(c.Now);
            c.Pass(TimeSpan.FromDays(1));
        }
        Assert.Equal(PomodoroTimer.HistoryDays, t.Days.Count);
        Assert.Equal(DateOnly.FromDateTime(c.Local).AddDays(-1), t.Days[^1].Date);
    }

    [Fact]
    public void A_stale_offer_expires_and_starting_opens_a_new_cycle()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        Assert.False(t.Expire(c.Minutes(60)));
        c.Pass(TimeSpan.FromHours(12));
        t.Start(c.Now);   // al día siguiente: no arranca el descanso de ayer
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(0, t.Cycle);
        Assert.Equal(FocusRun.Running, t.Run);
    }

    [Fact]
    public void A_forgotten_pause_expires_and_says_when()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        Assert.Null(t.ExpiresIn(c.Now));   // parado al principio: nada que caducar
        t.Start(c.Now);
        Assert.Null(t.ExpiresIn(c.Now));   // corriendo: manda el fin de la fase
        c.Minutes(10);
        t.Pause(c.Now);
        Assert.Equal(PomodoroTimer.StaleAfter, t.ExpiresIn(c.Now));
        c.Pass(TimeSpan.FromHours(11));
        Assert.Equal(TimeSpan.FromHours(1), t.ExpiresIn(c.Now));
        Assert.False(t.Expire(c.Now));
        Assert.Equal(FocusRun.Paused, t.Run);
        c.Pass(TimeSpan.FromHours(2));
        Assert.Equal(TimeSpan.Zero, t.ExpiresIn(c.Now));
        Assert.True(t.Expire(c.Now));
        Assert.Equal(FocusRun.Ready, t.Run);
        Assert.False(t.Active);            // el reposo vuelve a ser el de siempre
        Assert.Equal(Min(25), t.Remaining(c.Now));
        Assert.Null(t.ExpiresIn(c.Now));
    }

    [Fact]
    public void Resuming_a_pause_older_than_the_limit_starts_a_fresh_cycle()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        RunPhase(t, c);
        t.Start(c.Now);
        c.Minutes(3);
        t.Pause(c.Now);
        c.Pass(TimeSpan.FromHours(13));
        t.Start(c.Now);
        Assert.Equal(FocusRun.Running, t.Run);
        Assert.Equal(0, t.Cycle);
        Assert.Equal(Min(25), t.Remaining(c.Now));
    }

    [Fact]
    public void A_stale_offer_says_when_it_expires()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        Assert.Equal(PomodoroTimer.StaleAfter, t.ExpiresIn(c.Now));
    }

    [Fact]
    public void A_recent_offer_starts_the_phase_it_offers()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        c.Minutes(30);
        t.Start(c.Now);
        Assert.Equal(FocusPhase.ShortBreak, t.Phase);
    }

    [Fact]
    public void Skipping_after_a_long_gap_still_starts_the_skipped_to_phase()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        RunPhase(t, c);
        t.Reset(c.Now);
        c.Pass(TimeSpan.FromDays(1));
        t.Skip(c.Now);   // foco parado → descanso parado: es una acción, no caduca
        t.Start(c.Now);
        Assert.Equal(FocusPhase.ShortBreak, t.Phase);
        Assert.Equal(FocusRun.Running, t.Run);
    }

    [Fact]
    public void New_durations_apply_to_the_stopped_phase_but_not_to_the_running_one()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        Assert.True(t.SetDurations(new FocusDurations(50, 10, 20, 4)));
        Assert.False(t.SetDurations(new FocusDurations(50, 10, 20, 4)));
        Assert.Equal(Min(50), t.Remaining(c.Now));
        t.Start(c.Now);
        c.Minutes(10);
        t.SetDurations(FocusDurations.Default);
        Assert.Equal(Min(40), t.Remaining(c.Now));
        Assert.Equal(Min(50), t.Length);
        c.Minutes(40);
        var end = t.Advance(c.Now);
        Assert.Equal(50, end!.Minutes);
        Assert.Equal(Min(5), t.Remaining(c.Now));   // el descanso ya con las de ahora
    }

    [Fact]
    public void Only_completed_focuses_carry_the_linked_task()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        var id = Guid.NewGuid();
        Assert.True(t.Link(id));
        Assert.False(t.Link(id));
        Assert.Equal(id, RunPhase(t, c).TaskId);
        Assert.Null(RunPhase(t, c).TaskId);   // un descanso no
        t.Start(c.Now);
        t.Skip(c.Now);
        Assert.Equal(1, t.Today(c.Local).Focuses);
        Assert.True(t.Link(null));
        Assert.Null(t.TaskId);
    }

    [Fact]
    public void Every_change_bumps_the_version()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        int v = t.Version;
        foreach (var act in new Action[]
                 {
                     () => t.Start(c.Now), () => t.Pause(c.Now), () => t.Skip(c.Now), () => t.Reset(c.Now),
                     () => t.Link(Guid.NewGuid()), () => t.SetDurations(new FocusDurations(30)),
                 })
        {
            act();
            Assert.True(t.Version > v);
            v = t.Version;
        }
    }

    // ---------------------------------------------------------------- lo que se enseña

    [Theory]
    [InlineData(1500, "25:00")]
    [InlineData(1499.2, "25:00")]
    [InlineData(1499, "24:59")]
    [InlineData(61, "01:01")]
    [InlineData(0.4, "00:01")]
    [InlineData(0, "00:00")]
    [InlineData(-3, "00:00")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3601, "1:00:01")]
    public void The_page_clock_rounds_seconds_up(double seconds, string expected) =>
        Assert.Equal(expected, FocusClock.Clock(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void The_page_wakes_when_the_shown_second_changes()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(300) + FocusClock.WakeSlack, FocusClock.NextSecondIn(TimeSpan.FromSeconds(24.3)));
        Assert.Equal(TimeSpan.FromSeconds(1) + FocusClock.WakeSlack, FocusClock.NextSecondIn(TimeSpan.FromSeconds(24)));
        Assert.Null(FocusClock.NextSecondIn(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(1500, 25, true)]
    [InlineData(1441, 25, true)]
    [InlineData(1440, 24, true)]
    [InlineData(61, 2, true)]
    [InlineData(60, 60, false)]
    [InlineData(59.5, 60, false)]
    [InlineData(1, 1, false)]
    [InlineData(0, 0, false)]
    public void Closed_the_notch_shows_minutes_and_seconds_only_in_the_last_minute(double seconds, int value, bool minutes) =>
        Assert.Equal((value, minutes), FocusClock.Compact(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Closed_the_notch_wakes_once_a_minute_until_the_last_one()
    {
        var slack = FocusClock.WakeSlack;
        Assert.Equal(TimeSpan.FromSeconds(60) + slack, FocusClock.NextCompactIn(TimeSpan.FromMinutes(25)));
        Assert.Equal(TimeSpan.FromSeconds(20) + slack, FocusClock.NextCompactIn(TimeSpan.FromSeconds(24 * 60 + 20)));
        Assert.Equal(TimeSpan.FromSeconds(1) + slack, FocusClock.NextCompactIn(TimeSpan.FromSeconds(61)));
        Assert.Equal(TimeSpan.FromSeconds(1) + slack, FocusClock.NextCompactIn(TimeSpan.FromSeconds(60)));
        Assert.Null(FocusClock.NextCompactIn(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMinutes(24), FocusClock.CompactShown(TimeSpan.FromSeconds(23 * 60 + 1)));
        Assert.Equal(TimeSpan.FromSeconds(42), FocusClock.CompactShown(TimeSpan.FromSeconds(41.2)));
    }

    [Fact]
    public void The_mini_ring_moves_with_its_text()
    {
        var len = Min(25);
        Assert.Equal(0f, FocusClock.Progress(FocusClock.CompactShown(Min(25)), len));
        Assert.Equal(0f, FocusClock.Progress(FocusClock.CompactShown(Min(24.5)), len));   // sigue en «25 min»
        Assert.Equal(1f / 25f, FocusClock.Progress(FocusClock.CompactShown(Min(24)), len), 5);
        Assert.Equal(1f, FocusClock.Progress(TimeSpan.Zero, len));
        Assert.Equal(0f, FocusClock.Progress(TimeSpan.Zero, TimeSpan.Zero));
    }

    // ---------------------------------------------------------------- revisión

    [Fact]
    public void A_clock_set_back_while_closed_realigns_the_saved_end_so_the_phase_does_not_restart_every_launch()
    {
        var c = new Clock();
        var t = new PomodoroTimer();
        t.Start(c.Now);
        c.Minutes(5);
        var saved = t.Save();   // acaba a Utc0 + 25 min

        // La app se cierra y alguien atrasa la hora 2 h: al volver, la fase no puede durar más de lo que dura.
        var c2 = new Clock { Utc = c.Utc - TimeSpan.FromHours(2), Local = c.Local - TimeSpan.FromHours(2), Mono = TimeSpan.FromSeconds(1) };
        var back = PomodoroTimer.Restore(saved, FocusDurations.Default, c2.Now);
        Assert.Equal(Min(25), back.Remaining(c2.Now));
        Assert.Equal(c2.Utc + Min(25), back.Save().EndsAtUtc);   // la hora de fin guardada sigue a la acotada

        // Otro arranque 10 minutos después: quedan 15, no otra vez 25.
        var again = back.Save();
        var c3 = new Clock { Utc = c2.Utc + Min(10), Local = c2.Local + Min(10), Mono = TimeSpan.FromSeconds(1) };
        Assert.Equal(Min(15), PomodoroTimer.Restore(again, FocusDurations.Default, c3.Now).Remaining(c3.Now));
    }

    [Fact]
    public void A_restored_pause_without_last_activity_still_expires()
    {
        var c = new Clock();
        var state = new FocusState(Run: FocusRun.Paused, Remaining: Min(10), Length: Min(25), Cycle: 1);
        var t = PomodoroTimer.Restore(state, FocusDurations.Default, c.Now);
        Assert.Equal(FocusRun.Paused, t.Run);
        Assert.Equal(c.Utc, t.LastActivityUtc);
        Assert.Equal(PomodoroTimer.StaleAfter, t.ExpiresIn(c.Now));
        c.Pass(PomodoroTimer.StaleAfter + Min(1));
        Assert.True(t.Expire(c.Now));
        Assert.Equal(FocusRun.Ready, t.Run);
    }

    [Theory]
    [InlineData(FocusRun.Running)]
    [InlineData(FocusRun.Paused)]
    public void Without_pro_a_custom_length_ends_its_phase_and_the_next_ones_are_standard(FocusRun run)
    {
        var c = new Clock();
        // Guardado con el Pro: foco de 50 min. Ahora sin el Pro: las duraciones de serie.
        var state = new FocusState(Run: run, EndsAtUtc: run == FocusRun.Running ? c.Utc + Min(20) : null,
            Remaining: run == FocusRun.Paused ? Min(20) : null, Length: Min(50), Cycle: 0, LastActivityUtc: c.Utc);
        var t = PomodoroTimer.Restore(state, FocusDurations.Default, c.Now);
        Assert.Equal(Min(50), t.Length);   // la fase en curso acaba con la suya
        Assert.Equal(Min(20), t.Remaining(c.Now));
        if (run == FocusRun.Paused) t.Start(c.Now);
        c.Minutes(20);
        Assert.NotNull(t.Advance(c.Now));
        Assert.Equal(Min(5), t.Length);
        RunPhase(t, c);
        Assert.Equal(Min(25), t.Length);   // y el siguiente foco, el de serie

        // Reiniciar la fase personalizada (en pausa) también la deja en la de serie.
        var paused = PomodoroTimer.Restore(state with { Run = FocusRun.Paused, Remaining = Min(20), EndsAtUtc = null },
            FocusDurations.Default, c.Now);
        paused.Reset(c.Now);
        Assert.Equal(Min(25), paused.Length);
        Assert.Equal(Min(25), paused.Save().Length);
    }

    [Fact]
    public void Today_wakes_at_the_next_local_midnight()
    {
        var tz = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(1), "t", "t");
        var utc = new DateTime(2026, 10, 30, 21, 30, 0, DateTimeKind.Utc);   // 22:30 local
        Assert.Equal(TimeSpan.FromMinutes(90) + FocusClock.WakeSlack, FocusClock.NextMidnightIn(utc, tz));
        var justAfter = new DateTime(2026, 10, 30, 23, 0, 0, DateTimeKind.Utc);   // 00:00 del 31
        Assert.Equal(TimeSpan.FromHours(24) + FocusClock.WakeSlack, FocusClock.NextMidnightIn(justAfter, tz));
    }
}