using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class PaceCalculatorTests
{
    [Fact]
    public void IdealPct_is_about_50_at_window_midpoint()
    {
        var nowUtc = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        // Reset en now + 2.5h => mitad de una ventana de 5h => ideal ~50%.
        var resetsAt = new DateTimeOffset(nowUtc.AddHours(2.5));
        var usage = new RealUsage { FiveHour = new UsageWindow(40, resetsAt) };

        var (five, _) = PaceCalculator.Compute(usage, Array.Empty<PctPoint>(), nowUtc);

        Assert.NotNull(five);
        Assert.Equal(50.0, five!.IdealPct, 1);
    }

    [Fact]
    public void IdealPct_is_about_0_just_after_reset()
    {
        var nowUtc = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        // Reset en now + 5h => ventana recién reseteada => ideal ~0%.
        var resetsAt = new DateTimeOffset(nowUtc.AddHours(5));
        var usage = new RealUsage { FiveHour = new UsageWindow(0, resetsAt) };

        var (five, _) = PaceCalculator.Compute(usage, Array.Empty<PctPoint>(), nowUtc);

        Assert.NotNull(five);
        Assert.Equal(0.0, five!.IdealPct, 1);
    }
}
