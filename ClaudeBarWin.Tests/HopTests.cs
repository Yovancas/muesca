using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="Hop"/> (lenguaje de movimiento §5): salto con gravedad y suelo, función pura del tiempo.
/// g = 272 px/s², restitución 0,75 y corte cuando el siguiente pico bajaría de 1,2 px. Siempre ≥ 0 y en
/// px enteros. Panel v₀ 57 ⇒ 6 / 3,4 / 1,9 px y 419 / 314 / 236 ms (≈ 970 ms).
/// </summary>
public class HopTests
{
    [Fact]
    public void Panel_hop_has_three_bounces_with_the_documented_peaks_and_durations()
    {
        var b = Hop.Bounces(Motion.HopPanelV0);
        Assert.Equal(3, b.Count);
        Assert.Equal(6.0, b[0].PeakPx, 1);
        Assert.Equal(3.4, b[1].PeakPx, 1);
        Assert.Equal(1.9, b[2].PeakPx, 1);
        Assert.Equal(419, b[0].DurationMs, 0);
        Assert.Equal(314, b[1].DurationMs, 0);
        Assert.Equal(236, b[2].DurationMs, 0);
        Assert.InRange(Hop.DurationMs(Motion.HopPanelV0), 965, 975);
    }

    [Theory]
    [InlineData(40, 2, 515)]   // notch: 3 / 1,7 px
    [InlineData(30, 1, 221)]   // notch durante el rebote de la forma: un solo bote de 1,65 px
    public void Notch_hops_follow_the_same_model(double v0, int count, double totalMs)
    {
        Assert.Equal(count, Hop.Bounces(v0).Count);
        Assert.InRange(Hop.DurationMs(v0), totalMs - 3, totalMs + 3);
    }

    [Fact]
    public void Offset_is_whole_pixels_never_negative_and_zero_outside()
    {
        double total = Hop.DurationMs(Motion.HopPanelV0);
        Assert.Equal(0, Hop.OffsetPx(-5, Motion.HopPanelV0));
        Assert.Equal(0, Hop.OffsetPx(0, Motion.HopPanelV0));
        Assert.Equal(0, Hop.OffsetPx(total + 1, Motion.HopPanelV0));
        int max = 0;
        for (double t = 0; t <= total; t += 1)
        {
            int o = Hop.OffsetPx(t, Motion.HopPanelV0);
            Assert.True(o >= 0);
            max = Math.Max(max, o);
        }
        Assert.Equal(6, max);
        Assert.Equal(6, Hop.OffsetPx(419 / 2.0, Motion.HopPanelV0));
    }

    // Unidades (§ cabecera): las alturas escalan con el DPI, los tiempos no.
    [Fact]
    public void Scale_multiplies_heights_and_keeps_times()
    {
        Assert.Equal(9, Hop.OffsetPx(419 / 2.0, Motion.HopPanelV0, scale: 1.5));
        Assert.Equal(0, Hop.OffsetPx(Hop.DurationMs(Motion.HopPanelV0) + 1, Motion.HopPanelV0, scale: 1.5));
    }

    [Fact]
    public void Touchdowns_are_at_the_end_of_each_bounce()
    {
        var td = Hop.TouchdownsMs(Motion.HopPanelV0);
        Assert.Equal(3, td.Count);
        Assert.Equal(419, td[0], 0);
        Assert.Equal(733, td[1], 0);
        Assert.Equal(969, td[2], 0);
    }

    // Micro-interacción 1: en cada toque de suelo la cara alternativa dura 90 ms.
    [Fact]
    public void Landing_window_lasts_90ms_after_each_touchdown()
    {
        double v0 = Motion.HopPanelV0;
        Assert.False(Hop.IsLanding(200, v0));
        Assert.True(Hop.IsLanding(420, v0));
        Assert.True(Hop.IsLanding(419 + 89, v0));
        Assert.False(Hop.IsLanding(419 + 91, v0));
        Assert.True(Hop.IsLanding(975, v0));
        Assert.False(Hop.IsLanding(969 + 91, v0));
        Assert.Equal(969 + Motion.HopLandingFaceMs, Hop.ActiveMs(v0), 0);
    }

    // Calendario de atención (§3.4 / §4 #7): entrada y repeticiones a 3,2 / 6,4 / 9,6 / 12,8 / 22,8 s.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3199, 0)]
    [InlineData(3200, 3200)]
    [InlineData(13000, 12800)]
    [InlineData(22799, 12800)]
    [InlineData(22800, 22800)]
    [InlineData(60000, 22800)]
    public void Attention_cadence_picks_the_latest_scheduled_hop(double since, double expected)
        => Assert.Equal(expected, Hop.LatestScheduledMs(since));

    [Fact]
    public void Attention_cadence_has_six_hops_and_then_stays_still()
    {
        Assert.Equal(new double[] { 0, 3200, 6400, 9600, 12800, 22800 }, Motion.AttentionHopScheduleMs);
        Assert.Equal(3200, Hop.NextScheduledMs(100));
        Assert.Null(Hop.NextScheduledMs(22800));
        Assert.Null(Hop.LatestScheduledMs(-1));
    }
}
