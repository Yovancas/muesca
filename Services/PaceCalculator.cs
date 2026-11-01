namespace ClaudeBarWin.Services;

public enum PaceStatus { Ok, Over, Critical }

public sealed record PaceResult(
    string Window,                 // "5h" | "7d"
    double UtilPct,
    double PaceRatio,              // used% / ideal% (>1 = ahead of pace)
    double IdealPct,               // % donde deberías ir según el tiempo transcurrido
    DateTimeOffset? EtaUtc,        // projected time to hit 100% (null if not burning)
    DateTimeOffset? ResetsAt,
    bool ExhaustsBeforeReset,
    PaceStatus Status);

/// <summary>
/// Hybrid pace: "ritmo vs ideal" works from the current snapshot (no history needed),
/// while the ETA uses the slope of recent real-% samples (falling back to the average rate).
/// </summary>
public static class PaceCalculator
{
    private const double OverThreshold = 1.0;
    private const double CriticalThreshold = 1.3;

    public static (PaceResult? Five, PaceResult? Seven) Compute(
        RealUsage usage, IReadOnlyList<PctPoint> recent, DateTime nowUtc)
    {
        var five = For("5h", usage.FiveHour, TimeSpan.FromHours(5), recent, p => p.FivePct, TimeSpan.FromHours(1), nowUtc);
        var seven = For("7d", usage.SevenDay, TimeSpan.FromDays(7), recent, p => p.SevenPct, TimeSpan.FromHours(6), nowUtc);
        return (five, seven);
    }

    private static PaceResult? For(
        string window, UsageWindow? w, TimeSpan windowLen,
        IReadOnlyList<PctPoint> recent, Func<PctPoint, double?> sel, TimeSpan rateSpan, DateTime nowUtc)
    {
        if (w?.ResetsAt is not { } reset) return null;

        double u = w.UtilizationPct;
        var now = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        double msUntilReset = (reset - now).TotalMilliseconds;
        double elapsedMs = windowLen.TotalMilliseconds - msUntilReset;
        if (elapsedMs < 1) elapsedMs = 1; // just after a reset
        double e = Math.Clamp(elapsedMs / windowLen.TotalMilliseconds, 0.0001, 1.0);

        double idealUsed = e * 100.0;
        double paceRatio = idealUsed > 0 ? u / idealUsed : (u > 1 ? 99 : 1);

        // rate (%/ms): sum of positive deltas over recent span (ignores resets), else average.
        double? rate = PositiveRate(recent, sel, nowUtc - rateSpan, nowUtc);
        if (rate is null or <= 0)
            rate = u / elapsedMs; // average since window start

        DateTimeOffset? eta = null;
        if (rate > 0 && u < 100)
            eta = now.AddMilliseconds((100.0 - u) / rate.Value);

        bool exhausts = eta is { } et && et < reset;
        var status = paceRatio >= CriticalThreshold ? PaceStatus.Critical
                   : paceRatio >= OverThreshold ? PaceStatus.Over
                   : PaceStatus.Ok;

        return new PaceResult(window, u, paceRatio, idealUsed, eta, reset, exhausts, status);
    }

    private static double? PositiveRate(IReadOnlyList<PctPoint> recent, Func<PctPoint, double?> sel, DateTime fromUtc, DateTime nowUtc)
    {
        var pts = recent
            .Where(p => p.TsUtc >= fromUtc && sel(p) is not null)
            .OrderBy(p => p.TsUtc)
            .ToList();
        if (pts.Count < 2) return null;

        double sumPos = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double d = sel(pts[i])!.Value - sel(pts[i - 1])!.Value;
            if (d > 0) sumPos += d; // ignore drops (window resets)
        }
        double dtMs = (pts[^1].TsUtc - pts[0].TsUtc).TotalMilliseconds;
        if (dtMs <= 0) return null;
        return sumPos / dtMs;
    }
}
