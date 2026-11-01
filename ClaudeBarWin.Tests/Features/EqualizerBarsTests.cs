using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary><see cref="EqualizerBars"/>: el ecualizador de «está sonando» del notch (pedido del 29-oct).</summary>
public class EqualizerBarsTests
{
    [Fact]
    public void Heights_stay_between_low_and_full()
    {
        for (double t = 0; t < 10; t += 0.013)
            Assert.All(EqualizerBars.Heights(t, playing: true), h => Assert.InRange(h, EqualizerBars.Low, 1f));
    }

    [Fact]
    public void Paused_bars_rest_low_and_still()
    {
        Assert.All(EqualizerBars.Heights(3.7, playing: false), h => Assert.Equal(EqualizerBars.Low, h));
        Assert.Equal(EqualizerBars.Heights(0, false), EqualizerBars.Heights(9, false));
    }

    [Fact]
    public void Playing_bars_move_and_are_not_in_sync()
    {
        var a = EqualizerBars.Heights(1.0, true);
        var b = EqualizerBars.Heights(1.2, true);
        Assert.NotEqual(a, b);                                    // se mueven
        Assert.True(a.Distinct().Count() >= 3);                   // y no van todas a la vez
    }

    [Fact]
    public void Is_deterministic_for_the_same_instant()
        => Assert.Equal(EqualizerBars.Heights(4.25, true), EqualizerBars.Heights(4.25, true));

    [Fact]
    public void Reduce_motion_draws_a_fixed_shape()
    {
        Assert.Equal(EqualizerBars.Heights(0, true, still: true), EqualizerBars.Heights(7, true, still: true));
        Assert.True(EqualizerBars.Heights(0, true, still: true).Distinct().Count() > 1);
    }
}
