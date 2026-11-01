using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Cadencia del panel en reposo (lenguaje de movimiento §4, «Cadencia del panel» y micro-interacción 4):
/// los textos que dependen del reloj van a resolución de minutos y el panel quieto se despierta solo
/// cuando alguno va a cambiar (≈ 1 vez por minuto), nunca con un tick de 1 Hz. Todo puro.
/// </summary>
public class IdleCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2 * 86400 + 3 * 3600 + 120, "2d 3h")]
    [InlineData(2 * 3600 + 13 * 60 + 40, "2h 13m")]
    [InlineData(13 * 60 + 40, "13m")]
    [InlineData(59, "<1m")]
    public void Countdown_in_minutes_has_no_seconds(int seconds, string expected)
        => Assert.Equal(expected, UsageFormat.CountdownMinutesAt(Now.AddSeconds(seconds), "resetting…", Now));

    [Fact]
    public void Countdown_in_minutes_shows_the_label_when_due()
    {
        Assert.Equal("resetting…", UsageFormat.CountdownMinutesAt(Now, "resetting…", Now));
        Assert.Equal("", UsageFormat.CountdownMinutesAt(null, "resetting…", Now));
    }

    [Theory]
    [InlineData(5, "hace <1 min")]
    [InlineData(59, "hace <1 min")]
    [InlineData(5 * 60 + 3, "hace 5 min")]
    [InlineData(2 * 3600, "hace 2 h")]
    public void Relative_in_minutes_has_no_seconds(int seconds, string expected)
    {
        var s = Localization.Get("es");
        var now = Now.UtcDateTime;
        Assert.Equal(expected, UsageFormat.RelativeMinutesAt(now.AddSeconds(-seconds), now, s));
    }

    // Siguiente cambio del texto de la cuenta atrás: justo después de cruzar el minuto (la hora con días).
    [Theory]
    [InlineData(2 * 3600 + 13 * 60 + 40, 40)]
    [InlineData(13 * 60 + 5, 5)]
    [InlineData(13 * 60, 0)]       // justo en la frontera: cambia ya
    [InlineData(2 * 86400 + 3 * 3600 + 120, 120)]
    [InlineData(30, 30)]
    public void Countdown_change_is_at_the_next_boundary(int seconds, int expectedSeconds)
    {
        double? ms = IdleCadence.CountdownChangeMs(Now.AddSeconds(seconds), Now);
        Assert.NotNull(ms);
        Assert.InRange(ms!.Value, expectedSeconds * 1000.0, expectedSeconds * 1000.0 + IdleCadence.EpsilonMs);
    }

    [Fact]
    public void Countdown_without_future_reset_has_no_change()
    {
        Assert.Null(IdleCadence.CountdownChangeMs(null, Now));
        Assert.Null(IdleCadence.CountdownChangeMs(Now.AddSeconds(-5), Now));
    }

    [Theory]
    [InlineData(5, 55)]
    [InlineData(5 * 60 + 3, 57)]
    [InlineData(2 * 3600 + 10, 3590)]
    public void Relative_change_is_at_the_next_boundary(int ageSeconds, int expectedSeconds)
    {
        var now = Now.UtcDateTime;
        double ms = IdleCadence.RelativeChangeMs(now.AddSeconds(-ageSeconds), now);
        Assert.InRange(ms, expectedSeconds * 1000.0, expectedSeconds * 1000.0 + IdleCadence.EpsilonMs);
    }

    [Fact]
    public void Instant_change_is_null_once_past()
    {
        var now = Now.UtcDateTime;
        Assert.Null(IdleCadence.InstantChangeMs(now.AddSeconds(-1), now));
        Assert.InRange(IdleCadence.InstantChangeMs(now.AddSeconds(10), now)!.Value, 10_000, 10_000 + IdleCadence.EpsilonMs);
    }

    [Fact]
    public void Earliest_ignores_missing_and_caps_at_a_minute()
    {
        Assert.Equal(1234, IdleCadence.Earliest(null, 5000, 1234, null));
        Assert.Equal(IdleCadence.MaxIdleMs, IdleCadence.Earliest(null, null));
        Assert.Equal(IdleCadence.MaxIdleMs, IdleCadence.Earliest(10 * 60_000.0));
        Assert.Equal(IdleCadence.MinIdleMs, IdleCadence.Earliest(1));
        Assert.Equal(60_000, IdleCadence.MaxIdleMs);
    }

    [Fact]
    public void Scheduler_sleeps_until_the_next_change_when_still()
    {
        var s = new MotionScheduler();
        Assert.Equal(Motion.FastTickMs, s.DesiredIntervalMs(visible: true, animating: true, idleWakeMs: 42_000));
        Assert.Equal(42_000, s.DesiredIntervalMs(visible: true, animating: false, idleWakeMs: 42_000));
        Assert.True(s.DesiredIntervalMs(visible: false, animating: false, idleWakeMs: 42_000) <= 0);
    }

    // Micro-interacción 4: solo cruzan los dígitos que cambian.
    [Theory]
    [InlineData("en 2h 13m · mar", "en 2h 12m · mar", "en 2h 1", "3", "2", "m · mar")]
    [InlineData("en 2h 10m", "en 2h 9m", "en 2h ", "10", "9", "m")]
    [InlineData("hace <1 min", "hace 1 min", "hace ", "<", "", "1 min")]
    [InlineData("abc", "abc", "abc", "", "", "")]
    [InlineData("", "12m", "", "", "12m", "")]
    public void Text_diff_isolates_the_changed_middle(string a, string b, string pre, string aMid, string bMid, string suf)
        => Assert.Equal((pre, aMid, bMid, suf), TextDiff.Split(a, b));

    [Fact]
    public void Text_blend_crossfades_120ms_linearly_on_change()
    {
        var m = new MotionState();
        Assert.Equal((null, 1.0), m.TextBlend("reset:5h", "en 2h 13m"));     // primera vez: sin cruce
        Assert.False(m.IsAnimating);
        var (prev, blend) = m.TextBlend("reset:5h", "en 2h 12m");
        Assert.Equal("en 2h 13m", prev);
        Assert.Equal(0.0, blend);
        Assert.True(m.IsAnimating);
        m.Advance(60);
        Assert.Equal(0.5, m.TextBlend("reset:5h", "en 2h 12m").Blend, 9);
        m.Advance(60);
        Assert.Equal((null, 1.0), m.TextBlend("reset:5h", "en 2h 12m"));
        Assert.False(m.IsAnimating);
        Assert.Equal(120, Motion.TextCrossfadeMs);
    }
}
