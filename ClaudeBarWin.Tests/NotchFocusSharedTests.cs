using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El pomodoro compartido por todas las ventanas (<see cref="NotchShared"/>): cada botón avisa a las ventanas y pide guardar,
/// lo guardado en otra ejecución se restaura (lo que acabó con la app cerrada se cierra UNA vez y lo muy viejo no se cuenta),
/// las duraciones nuevas se aplican a la fase parada, enlazar tareas es Pro y descartar el aviso llega a todas.
/// </summary>
public class NotchFocusSharedTests
{
    private static readonly NotchOptions Opts = new(false, false, true, true, 0, "", false, Tasks: true);

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private sealed class Probe
    {
        public int Changed, Cleared;
        public readonly List<FocusEnd> Ended = new();
        public readonly List<FocusState> Saved = new();

        public Probe(NotchShared shared)
        {
            shared.FocusChanged += () => Changed++;
            shared.FocusEnded += Ended.Add;
            shared.FocusSaved += Saved.Add;
            shared.FocusAlertCleared += () => Cleared++;
        }
    }

    [Fact]
    public void Every_button_tells_the_windows_and_asks_to_save()
    {
        RunSta(() =>
        {
            using var shared = new NotchShared(new ClaudeStateHub(), Opts, a => a());
            var probe = new Probe(shared);
            shared.FocusToggle();
            Assert.Equal(FocusRun.Running, shared.Focus.Run);
            Assert.Equal(FocusRun.Running, probe.Saved[^1].Run);
            Assert.NotNull(probe.Saved[^1].EndsAtUtc);
            Assert.True(probe.Changed >= 1);

            shared.FocusToggle();
            Assert.Equal(FocusRun.Paused, probe.Saved[^1].Run);
            shared.FocusSkip();
            Assert.Equal(FocusPhase.ShortBreak, shared.Focus.Phase);
            shared.FocusReset();
            Assert.Equal(FocusPhase.Focus, shared.Focus.Phase);
            Assert.Equal(4, probe.Saved.Count);
            Assert.Empty(probe.Ended);
        });
    }

    [Fact]
    public void A_phase_that_ended_with_the_app_closed_ends_once_on_restore()
    {
        RunSta(() =>
        {
            using var shared = new NotchShared(new ClaudeStateHub(), Opts, a => a());
            var probe = new Probe(shared);
            var ended = DateTime.UtcNow.AddMinutes(-2);
            shared.RestoreFocus(new FocusState(Run: FocusRun.Running, EndsAtUtc: ended, Length: TimeSpan.FromMinutes(25)));
            var end = Assert.Single(probe.Ended);
            Assert.True(end.Counted);
            Assert.InRange(end.Late, TimeSpan.FromMinutes(1.9), TimeSpan.FromMinutes(2.5));
            Assert.Equal(FocusPhase.Focus, shared.Focus.Finished);
            Assert.Equal(FocusPhase.ShortBreak, shared.Focus.Phase);
            Assert.Equal(FocusRun.Ready, probe.Saved[^1].Run);
            Assert.Equal(1, shared.Focus.Today(DateTime.Now - end.Late).Focuses);   // el día en que acabó

            shared.FocusRefresh();   // abrir la página no lo vuelve a cerrar
            Assert.Single(probe.Ended);
        });
    }

    [Fact]
    public void A_phase_that_ended_long_ago_is_not_counted_nor_announced()
    {
        RunSta(() =>
        {
            using var shared = new NotchShared(new ClaudeStateHub(), Opts, a => a());
            var probe = new Probe(shared);
            shared.RestoreFocus(new FocusState(Phase: FocusPhase.Focus, Run: FocusRun.Running, EndsAtUtc: DateTime.UtcNow.AddHours(-13),
                Length: TimeSpan.FromMinutes(25), Cycle: 2));
            Assert.Empty(probe.Ended);
            Assert.Equal(FocusRun.Ready, shared.Focus.Run);
            Assert.Equal(FocusRun.Ready, probe.Saved[^1].Run);   // y se guarda: no se vuelve a mirar en el próximo arranque
            Assert.Equal(0, shared.Focus.Cycle);
            Assert.Equal(0, shared.Focus.Today(DateTime.Now).Focuses);
        });
    }

    [Fact]
    public void A_pause_forgotten_overnight_expires_on_restore_and_is_saved()
    {
        RunSta(() =>
        {
            using var shared = new NotchShared(new ClaudeStateHub(), Opts, a => a());
            var probe = new Probe(shared);
            shared.RestoreFocus(new FocusState(Run: FocusRun.Paused, Remaining: TimeSpan.FromMinutes(9), Length: TimeSpan.FromMinutes(25),
                Cycle: 1, LastActivityUtc: DateTime.UtcNow.AddHours(-13)));
            Assert.False(shared.Focus.Active);   // el reposo vuelve a ser el de siempre
            Assert.Equal(FocusRun.Ready, probe.Saved[^1].Run);
            Assert.Empty(probe.Ended);
        });
    }

    [Fact]
    public void A_running_phase_is_restored_where_it_was()
    {
        RunSta(() =>
        {
            using var shared = new NotchShared(new ClaudeStateHub(), Opts, a => a());
            shared.RestoreFocus(new FocusState(Run: FocusRun.Running, EndsAtUtc: DateTime.UtcNow.AddMinutes(10),
                Length: TimeSpan.FromMinutes(25)));
            Assert.Equal(FocusRun.Running, shared.Focus.Run);
            Assert.InRange(shared.Focus.Remaining(shared.FocusNow()), TimeSpan.FromMinutes(9.9), TimeSpan.FromMinutes(10));
        });
    }

    [Fact]
    public void New_durations_reach_the_stopped_phase_and_are_saved()
    {
        RunSta(() =>
        {
            using var shared = new NotchShared(new ClaudeStateHub(), Opts, a => a());
            var probe = new Probe(shared);
            shared.ApplyOptions(Opts with { Focus = new FocusSettings(new FocusDurations(50, 10, 20, 3)) });
            Assert.Equal(TimeSpan.FromMinutes(50), shared.Focus.Length);
            Assert.Equal(TimeSpan.FromMinutes(50), probe.Saved[^1].Length);
            int saves = probe.Saved.Count;
            shared.ApplyOptions(Opts with { Focus = new FocusSettings(new FocusDurations(50, 10, 20, 3), Sound: false) });
            Assert.Equal(saves, probe.Saved.Count);   // solo el sonido: nada que guardar del pomodoro
        });
    }

    [Fact]
    public void Linking_a_task_needs_pro_and_tasks()
    {
        RunSta(() =>
        {
            var free = NotchProGating.Apply(Opts, ClaudeBarWin.Config.NotchMonitorMode.Fixed, ProState.Free).Options;
            using var shared = new NotchShared(new ClaudeStateHub(), free, a => a());
            var id = Guid.NewGuid();
            Assert.False(shared.FocusLinkAllowed);
            shared.FocusLink(id);
            Assert.Null(shared.Focus.TaskId);

            shared.ApplyOptions(Opts);
            Assert.True(shared.FocusLinkAllowed);
            var probe = new Probe(shared);
            shared.FocusLink(id);
            Assert.Equal(id, shared.Focus.TaskId);
            Assert.Equal(id, probe.Saved[^1].TaskId);
            shared.ApplyOptions(Opts with { Tasks = false });
            Assert.False(shared.FocusLinkAllowed);
            Assert.Null(shared.FocusTask);   // ni se ve…
            Assert.Equal(id, shared.Focus.TaskId);   // …ni se borra: al volver el Pro, sigue enlazada
        });
    }

    [Fact]
    public void Dismissing_the_alert_reaches_every_window_and_nothing_fires_after_dispose()
    {
        RunSta(() =>
        {
            var shared = new NotchShared(new ClaudeStateHub(), Opts, a => a());
            var probe = new Probe(shared);
            shared.ClearFocusAlert();
            Assert.Equal(1, probe.Cleared);
            shared.Dispose();
            shared.ClearFocusAlert();
            shared.FocusToggle();
            shared.RestoreFocus(null);
            Assert.Equal(1, probe.Cleared);
            Assert.Empty(probe.Saved);
        });
    }

    [Fact]
    public void A_phase_left_running_past_the_stale_limit_tells_the_windows_when_it_resets()
    {
        RunSta(() =>
        {
            using var shared = new NotchShared(new ClaudeStateHub(), Opts, a => a());
            var at = shared.FocusNow();
            shared.FocusClock = () => at;
            shared.FocusToggle();
            Assert.Equal(FocusRun.Running, shared.Focus.Run);
            var probe = new Probe(shared);

            // 13 h después (el disparo no llegó: equipo colgado, depurador…): acabó hace tanto que no se cuenta.
            var later = TimeSpan.FromHours(13);
            shared.FocusClock = () => new FocusNow(at.Utc + later, at.Mono + later, at.Local + later);
            shared.FocusRefresh();
            Assert.Equal(FocusRun.Ready, shared.Focus.Run);
            Assert.Empty(probe.Ended);
            Assert.Single(probe.Saved);
            Assert.Equal(1, probe.Changed);   // las ventanas quitan el mini anillo
        });
    }
}