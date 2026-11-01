using System.Drawing;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="NotchMascot"/> es PURO: decide cuándo aparece el gato del notch (15 s de reposo), adónde mira
/// (solo con el cursor encima), cuándo se duerme (60 s más) y cuándo despierta. Parpadeo determinista.
/// </summary>
public class NotchMascotTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0);

    private static NotchMascot IdleFrom(DateTime since, DateTime now)
    {
        var m = new NotchMascot();
        m.OnIdleSince(since, now);
        return m;
    }

    [Fact]
    public void Appears_after_15s_idle()
    {
        var m = IdleFrom(T0, T0.AddSeconds(14.9));
        Assert.Equal(MascotState.Hidden, m.State);
        m.OnIdleSince(T0, T0.AddSeconds(15));
        Assert.Equal(MascotState.Awake, m.State);
    }

    [Fact]
    public void Hidden_while_active()
    {
        var m = new NotchMascot();
        m.OnIdleSince(null, T0);
        Assert.Equal(MascotState.Hidden, m.State);
        Assert.Null(m.NextWakeIn(T0));
    }

    [Fact]
    public void Gaze_is_clamped()
    {
        var m = IdleFrom(T0, T0.AddSeconds(20));
        var t = T0.AddSeconds(20);

        m.OnPointer(new PointF(30, 40), t);
        Assert.Equal(MascotState.LookAt, m.State);
        Assert.Equal(0.5f, m.Gaze.X, 3);
        Assert.Equal(0.5f, m.Gaze.Y, 3);

        m.OnPointer(new PointF(1000, 1000), t);
        Assert.Equal(new PointF(3.5f, 2f), m.Gaze);

        m.OnPointer(new PointF(-1000, -1000), t);
        Assert.Equal(new PointF(-3.5f, -1f), m.Gaze);

        // Sin cursor encima: mira al frente.
        m.OnPointer(null, t);
        Assert.Equal(MascotState.Awake, m.State);
        Assert.Equal(PointF.Empty, m.Gaze);
    }

    [Fact]
    public void Pointer_does_nothing_while_hidden()
    {
        var m = IdleFrom(T0, T0.AddSeconds(5));
        m.OnPointer(new PointF(30, 0), T0.AddSeconds(5));
        Assert.Equal(MascotState.Hidden, m.State);
        Assert.Equal(PointF.Empty, m.Gaze);
    }

    [Fact]
    public void Sleeps_after_60s_more()
    {
        var m = IdleFrom(T0, T0.AddSeconds(74.9));
        Assert.Equal(MascotState.Awake, m.State);
        m.Update(T0.AddSeconds(75));
        Assert.Equal(MascotState.Sleepy, m.State);
        m.Update(T0.AddSeconds(75) + NotchMascot.SleepyFor);
        Assert.Equal(MascotState.Asleep, m.State);
        // Dormido no sigue al cursor.
        m.OnPointer(new PointF(30, 0), T0.AddSeconds(80));
        Assert.Equal(MascotState.Asleep, m.State);
        Assert.Equal(PointF.Empty, m.Gaze);
    }

    [Fact]
    public void Any_activity_wakes_and_hides()
    {
        var m = IdleFrom(T0, T0.AddSeconds(100));
        Assert.Equal(MascotState.Asleep, m.State);

        var act = T0.AddSeconds(101);
        m.OnIdleSince(null, act);
        Assert.Equal(MascotState.Waking, m.State);
        Assert.Equal(NotchMascot.WakingFor, m.NextWakeIn(act));

        m.Update(act + NotchMascot.WakingFor);
        Assert.Equal(MascotState.Hidden, m.State);
    }

    [Fact]
    public void New_idle_period_counts_as_activity()
    {
        // Una actividad breve que el llamador no llegó a notificar con null: el «desde» cambia.
        var m = IdleFrom(T0, T0.AddSeconds(100));
        var since2 = T0.AddSeconds(102);
        m.OnIdleSince(since2, since2.AddSeconds(1));
        Assert.Equal(MascotState.Waking, m.State);
        m.Update(since2.AddSeconds(1) + NotchMascot.WakingFor);
        Assert.Equal(MascotState.Hidden, m.State);
        m.Update(since2.AddSeconds(15));
        Assert.Equal(MascotState.Awake, m.State);
    }

    [Fact]
    public void Activity_while_awake_hides_without_waking()
    {
        var m = IdleFrom(T0, T0.AddSeconds(20));
        m.OnIdleSince(null, T0.AddSeconds(21));
        Assert.Equal(MascotState.Hidden, m.State);
    }

    [Fact]
    public void NextWakeIn_null_when_asleep()
    {
        var m = IdleFrom(T0, T0.AddSeconds(200));
        Assert.Equal(MascotState.Asleep, m.State);
        Assert.Null(m.NextWakeIn(T0.AddSeconds(200)));
    }

    [Fact]
    public void NextWakeIn_points_to_appearance_and_to_sleep()
    {
        var m = IdleFrom(T0, T0.AddSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(5), m.NextWakeIn(T0.AddSeconds(10)));

        m.Update(T0.AddSeconds(74));
        var next = m.NextWakeIn(T0.AddSeconds(74));
        Assert.NotNull(next);
        Assert.True(next <= TimeSpan.FromSeconds(1)); // el parpadeo puede llegar antes que el sueño
        Assert.True(next > TimeSpan.Zero);
    }

    [Fact]
    public void Blinks_every_2_6_to_5s_deterministically()
    {
        var a = IdleFrom(T0, T0.AddSeconds(15));
        var b = IdleFrom(T0, T0.AddSeconds(15));
        var blinksA = new List<DateTime>();
        var blinksB = new List<DateTime>();
        bool wasA = false, wasB = false;
        for (var t = T0.AddSeconds(15); t < T0.AddSeconds(74); t = t.AddMilliseconds(10))
        {
            a.Update(t); b.Update(t);
            if (a.Blinking && !wasA) blinksA.Add(t);
            if (b.Blinking && !wasB) blinksB.Add(t);
            wasA = a.Blinking; wasB = b.Blinking;
        }
        Assert.Equal(blinksA, blinksB);
        Assert.True(blinksA.Count >= 10, $"parpadeos: {blinksA.Count}");
        var gaps = blinksA.Zip(blinksA.Skip(1), (x, y) => (y - x).TotalSeconds).ToList();
        Assert.All(gaps, g => Assert.InRange(g, 2.6, 5.0 + NotchMascot.BlinkFor.TotalSeconds + 0.02));
        Assert.True(gaps.Distinct().Count() > 1, "el intervalo debería variar (jitter)");
    }

    [Fact]
    public void NextWakeIn_includes_blink_edges()
    {
        var m = IdleFrom(T0, T0.AddSeconds(15));
        var t = T0.AddSeconds(15);
        var next = m.NextWakeIn(t)!.Value;
        Assert.InRange(next.TotalSeconds, 2.6, 5.0);
        m.Update(t + next);
        Assert.True(m.Blinking);
        Assert.Equal(NotchMascot.BlinkFor, m.NextWakeIn(t + next));
        m.Update(t + next + NotchMascot.BlinkFor);
        Assert.False(m.Blinking);
    }
}
