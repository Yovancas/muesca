using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

public class EasingTests
{
    // --- OutCubic ---------------------------------------------------------

    [Fact]
    public void OutCubic_endpoints_are_0_and_1()
    {
        Assert.Equal(0.0, Easing.OutCubic(0.0), 9);
        Assert.Equal(1.0, Easing.OutCubic(1.0), 9);
    }

    [Fact]
    public void OutCubic_is_monotonically_increasing()
    {
        double prev = double.NegativeInfinity;
        for (int i = 0; i <= 100; i++)
        {
            double v = Easing.OutCubic(i / 100.0);
            Assert.True(v >= prev, $"not monotone at t={i / 100.0}: {v} < {prev}");
            prev = v;
        }
    }

    [Fact]
    public void OutCubic_is_ahead_of_linear_at_half()
    {
        // ease-out front-loads: at the midpoint it should be past 0.5.
        Assert.True(Easing.OutCubic(0.5) > 0.5);
    }

    [Fact]
    public void OutCubic_clamps_input_outside_unit_interval()
    {
        Assert.Equal(0.0, Easing.OutCubic(-1.0), 9);
        Assert.Equal(1.0, Easing.OutCubic(2.0), 9);
    }

    // --- OutQuad ----------------------------------------------------------

    [Fact]
    public void OutQuad_endpoints_are_0_and_1()
    {
        Assert.Equal(0.0, Easing.OutQuad(0.0), 9);
        Assert.Equal(1.0, Easing.OutQuad(1.0), 9);
    }

    [Fact]
    public void OutQuad_is_monotonically_increasing_and_ahead_at_half()
    {
        double prev = double.NegativeInfinity;
        for (int i = 0; i <= 100; i++)
        {
            double v = Easing.OutQuad(i / 100.0);
            Assert.True(v >= prev);
            prev = v;
        }
        Assert.True(Easing.OutQuad(0.5) > 0.5);
    }

    // --- InOutCubic -------------------------------------------------------

    [Fact]
    public void InOutCubic_endpoints_are_0_and_1()
    {
        Assert.Equal(0.0, Easing.InOutCubic(0.0), 9);
        Assert.Equal(1.0, Easing.InOutCubic(1.0), 9);
    }

    [Fact]
    public void InOutCubic_is_symmetric_around_half()
    {
        Assert.Equal(0.5, Easing.InOutCubic(0.5), 9);
        // f(t) + f(1-t) == 1 for a symmetric ease-in-out.
        for (int i = 0; i <= 50; i++)
        {
            double t = i / 100.0;
            Assert.Equal(1.0, Easing.InOutCubic(t) + Easing.InOutCubic(1.0 - t), 9);
        }
    }

    [Fact]
    public void InOutCubic_is_monotonically_increasing()
    {
        double prev = double.NegativeInfinity;
        for (int i = 0; i <= 100; i++)
        {
            double v = Easing.InOutCubic(i / 100.0);
            Assert.True(v >= prev);
            prev = v;
        }
    }

    // --- OutBack (overshoot for bounce) -----------------------------------

    [Fact]
    public void OutBack_endpoints_are_0_and_1()
    {
        Assert.Equal(0.0, Easing.OutBack(0.0), 9);
        Assert.Equal(1.0, Easing.OutBack(1.0), 9);
    }

    [Fact]
    public void OutBack_overshoots_above_1_somewhere()
    {
        bool overshot = false;
        for (int i = 0; i <= 100; i++)
        {
            if (Easing.OutBack(i / 100.0) > 1.0) { overshot = true; break; }
        }
        Assert.True(overshot, "OutBack should rise above 1 to produce a bounce");
    }
}
