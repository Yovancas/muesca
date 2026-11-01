using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El pomodoro de ejemplo (un foco a medias, uno en pausa, el fin de una fase, la semana con focos) y el frame de cada estado de
/// la página Foco, del mini anillo y del aviso del fin de fase: lo comparten los tests de pintado y las capturas. Monta la
/// <see cref="FocusView"/> como <c>NotchForm.FocusFrame</c>.
/// </summary>
internal static class FocusFixtures
{
    public static readonly DateTime Utc0 = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Local0 = new(2026, 10, 30, 10, 0, 0);
    public static readonly RealUsage Quota = new()
    {
        FiveHour = new UsageWindow(42, DateTimeOffset.Now.AddHours(2)),
        SevenDay = new UsageWindow(18, DateTimeOffset.Now.AddDays(3)),
    };
    private static readonly NotchPage[] Pages = NotchPages.Default.ToArray();

    public static FocusNow At(TimeSpan elapsed) => new(Utc0 + elapsed, TimeSpan.FromHours(2) + elapsed, Local0 + elapsed);

    /// <summary>Un pomodoro con historia: la semana con focos y, hoy, dos focos completos; luego <paramref name="setup"/> encima.
    /// Devuelve el instante final.</summary>
    public static (PomodoroTimer Timer, FocusNow Now) Timer(Action<PomodoroTimer, Func<TimeSpan, FocusNow>> setup)
    {
        var history = new List<FocusDay>();
        var today = DateOnly.FromDateTime(Local0);
        int[] counts = { 3, 5, 0, 4, 6, 2 };
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > 0) history.Add(new FocusDay(today.AddDays(i - counts.Length), counts[i], counts[i] * 25));
        history.Add(new FocusDay(today, 2, 50));
        var t = PomodoroTimer.Restore(new FocusState(Cycle: 2, LastActivityUtc: Utc0, Days: history), FocusDurations.Default, At(TimeSpan.Zero));
        var clock = TimeSpan.Zero;
        FocusNow Pass(TimeSpan d)
        {
            clock += d;
            return At(clock);
        }
        setup(t, Pass);
        return (t, At(clock));
    }

    /// <summary>Corriendo: 7 min 30 s del tercer foco de hoy.</summary>
    public static (PomodoroTimer, FocusNow) Running() => Timer((t, pass) =>
    {
        t.Start(pass(TimeSpan.Zero));
        pass(TimeSpan.FromSeconds(450));
    });

    /// <summary>En pausa a los 12 min.</summary>
    public static (PomodoroTimer, FocusNow) Paused() => Timer((t, pass) =>
    {
        t.Start(pass(TimeSpan.Zero));
        t.Pause(pass(TimeSpan.FromMinutes(12)));
    });

    /// <summary>Acaba el tercer foco de hoy: el descanso corto, parado, con la oferta.</summary>
    public static (PomodoroTimer, FocusNow) Finished() => Timer((t, pass) =>
    {
        t.Start(pass(TimeSpan.Zero));
        t.Advance(pass(TimeSpan.FromMinutes(25)));
    });

    /// <summary>En el último minuto (el notch cerrado ya cuenta segundos).</summary>
    public static (PomodoroTimer, FocusNow) LastMinute() => Timer((t, pass) =>
    {
        t.Start(pass(TimeSpan.Zero));
        pass(TimeSpan.FromMinutes(24) + TimeSpan.FromSeconds(18));
    });

    /// <summary>La vista del pomodoro como la monta la ventana.</summary>
    public static FocusView View((PomodoroTimer Timer, FocusNow Now) state, Strings s, bool locked = false, bool face = false,
        FocusTaskInfo? task = null, FocusHit hover = default, float hoverIntensity = 0f, FocusHit pressed = default, float pop = 1f,
        bool interactive = true)
    {
        var (t, now) = state;
        var left = t.Remaining(now);
        var week = t.Week(now.Local);
        var texts = FocusText.Build(t.Phase, t.Run, t.Finished, left, t.Cycle, t.Durations.LongEvery, t.Today(now.Local), week,
            locked ? null : task, anyPendingTask: true, s);
        return new FocusView(texts, t.Phase, t.Run, t.Finished,
            FocusClock.Progress(TimeSpan.FromSeconds(FocusClock.ShownSeconds(left)), t.Length),
            FocusClock.Progress(FocusClock.CompactShown(left), t.Length), t.Cycle, t.Durations.LongEvery,
            locked || task is null ? -1 : task.Value.Color, week.Select(d => d.Focuses).ToArray(), locked, interactive, face,
            hover, hoverIntensity, pressed, pop);
    }

    /// <summary>La página Foco en el expandido (con los puntos de las páginas y, si se pide, el aviso integrado arriba).</summary>
    public static NotchFrame Page(float s, FocusView? v, bool alert = false)
    {
        var size = NotchLayout.PageSize(NotchPage.Focus, new PageRows(1, 0, 0), alert, withDots: true, s);
        var integrated = alert ? new NotchAlert(NotchAlertKind.Completed, "a", "muesca", null, Utc0, null) : null;
        return new NotchFrame(size.Width, size.Height, s, NotchMode.Expanded, integrated, Array.Empty<SessionState>(), Quota, 0f, 1f,
            ShadowAlpha: 1f, Pages: new NotchPagesView(Pages, NotchPage.Focus, null, 1f, 0f, 0f, Array.IndexOf(Pages, NotchPage.Focus)),
            Focus: v);
    }

    /// <summary>El notch cerrado con el mini anillo: reposo, vista previa, Trabajando o Mínimo.</summary>
    public static NotchFrame Closed(float s, NotchMode mode, FocusView v, IReadOnlyList<SessionState>? sessions = null)
    {
        var size = mode switch
        {
            NotchMode.Idle => FocusFace.IdleSize(s),
            NotchMode.Working => NotchLayout.TargetSize(NotchMode.Working, s, 120f + FocusFace.WorkingSlot),
            _ => NotchLayout.TargetSize(mode, s, 0f),
        };
        return new NotchFrame(size.Width, size.Height, s, mode, null, sessions ?? Array.Empty<SessionState>(), Quota, 0.3f, 1f,
            ShadowAlpha: NotchMotion.ShadowOpacity(mode), Focus: v);
    }

    /// <summary>El aviso del fin de fase (con su botón armándose: <paramref name="arm"/>), en la interactiva o en solo lectura.</summary>
    public static NotchFrame Alert(float s, FocusView? v, float arm = 1f, bool readOnly = false,
        PermissionVerdict held = PermissionVerdict.None, FocusPhase ended = FocusPhase.Focus, FocusPhase next = FocusPhase.ShortBreak)
    {
        var size = readOnly ? NotchLayout.ReadOnlyAlertSize(s) : NotchLayout.TargetSize(NotchMode.Reminder, s, 0f);
        var a = new NotchAlert(NotchAlertKind.Focus, NotchStateMachine.FocusAlertId, ended.ToString(), next.ToString(), Utc0,
            NotchTiming.FocusEnd);
        return new NotchFrame(size.Width, size.Height, s, NotchMode.Reminder, a, Array.Empty<SessionState>(), Quota, 0f, 1f,
            ShadowAlpha: 1f, PermissionArm: arm, PermissionHeld: held, ReadOnly: readOnly, Focus: v);
    }

    /// <summary>Una sesión trabajando (el mini anillo va en la ranura de Trabajando).</summary>
    public static SessionState Working() => new()
    {
        SessionId = "a", Cwd = @"C:\code\muesca", ProjectName = "muesca", Phase = SessionPhase.Processing,
        LastTool = "Edit", LastActivityUtc = DateTime.UtcNow,
    };
}
