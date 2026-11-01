namespace ClaudeBarWin.Services;

public sealed record MergedQuota(RealUsage Usage, DateTime ObservedUtc, bool FromStatusline);

/// <summary>
/// v0.4 — fusiona statusline (un snapshot por sesión) y API ventana a ventana: gana la ventana más nueva (mayor
/// resets_at, tolerancia 10 min) y, dentro de ella, el mayor % (el uso de una ventana solo sube). Así ni una sesión
/// inactiva que repite datos viejos ni una respuesta de la API anterior pueden bajar un valor más nuevo. Spec §6.
/// </summary>
public static class QuotaMerger
{
    internal static readonly TimeSpan SameWindowTolerance = TimeSpan.FromMinutes(10);

    public static RateWindowObs? Pick(IEnumerable<RateWindowObs> candidates, DateTime nowUtc)
    {
        var all = candidates.ToList();
        if (all.Count == 0) return null;
        var now = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        var live = all.Where(c => c.ResetsAt is null || c.ResetsAt > now).ToList();
        // Todo caducado = la ventana se reinició y ese uso ya no cuenta: 0 % desde ya. (Antes se enseñaba el último
        // % visto hasta la próxima lectura, que con la API cada 15 min podía ser un cuarto de hora de dato falso.)
        if (live.Count == 0) return new RateWindowObs(0, null, nowUtc, false);
        var withReset = live.Where(c => c.ResetsAt is not null).ToList();
        if (withReset.Count == 0) return live.MaxBy(c => c.ObservedUtc);
        var newest = withReset.Max(c => c.ResetsAt!.Value);
        var same = withReset.Where(c => newest - c.ResetsAt!.Value <= SameWindowTolerance).ToList();
        var maxPct = same.Max(c => c.Pct);
        return same.Where(c => c.Pct == maxPct).MaxBy(c => c.ObservedUtc);
    }

    public static MergedQuota? Merge(IReadOnlyList<RateLimitSnapshot> snaps, RealUsage? api, DateTime apiAtUtc, DateTime nowUtc)
    {
        RateWindowObs? Choose(string key, UsageWindow? fromApi)
        {
            var c = new List<RateWindowObs>();
            foreach (var s in snaps)
                if (s.Windows.TryGetValue(key, out var w)) c.Add(w);
            if (fromApi is not null) c.Add(new RateWindowObs(fromApi.UtilizationPct, fromApi.ResetsAt, apiAtUtc, false));
            return Pick(c, nowUtc);
        }

        var five = Choose("five_hour", api?.FiveHour);
        var seven = Choose("seven_day", api?.SevenDay);
        var opus = Choose("seven_day_opus", api?.SevenDayOpus);
        var sonnet = Choose("seven_day_sonnet", api?.SevenDaySonnet);
        if (five is null && seven is null && opus is null && sonnet is null) return null;

        static UsageWindow? W(RateWindowObs? o) => o is null ? null : new UsageWindow(o.Pct, o.ResetsAt);
        var usage = new RealUsage
        {
            FiveHour = W(five),
            SevenDay = W(seven),
            SevenDayOpus = W(opus),
            SevenDaySonnet = W(sonnet),
            ExtraUsageEnabled = api?.ExtraUsageEnabled ?? false,
        };
        var main = new[] { five, seven }.Where(o => o is not null).Select(o => o!).ToList();
        var observed = main.Count > 0 ? main.Max(o => o.ObservedUtc) : apiAtUtc;
        return new MergedQuota(usage, observed, main.Any(o => o.FromStatusline));
    }
}
