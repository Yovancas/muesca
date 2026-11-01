using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class RiskColorTests
{
    [Fact]
    public void At_zero_is_ok()
        => Assert.Equal(Theme.Dark.Ok, ColorMath.RiskColor(0, Theme.Dark, 70, 90));

    [Fact]
    public void At_hundred_is_critical()
        => Assert.Equal(Theme.Dark.Critical, ColorMath.RiskColor(100, Theme.Dark, 70, 90));

    [Fact]
    public void Red_channel_is_monotonic_nondecreasing()
    {
        int prev = -1;
        for (int p = 0; p <= 100; p += 5)
        {
            int r = ColorMath.RiskColor(p, Theme.Dark, 70, 90).R;
            Assert.True(r >= prev, $"R bajó en {p}%");
            prev = r;
        }
    }

    [Fact]
    public void Clamps_out_of_range()
    {
        Assert.Equal(Theme.Dark.Ok, ColorMath.RiskColor(-50, Theme.Dark, 70, 90));
        Assert.Equal(Theme.Dark.Critical, ColorMath.RiskColor(250, Theme.Dark, 70, 90));
    }
}
