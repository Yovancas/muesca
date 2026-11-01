using System.Text.Json;

namespace ClaudeBarWin.Services;

/// <summary>
/// Persists the last successful usage reading to disk so the app can show it
/// immediately on startup and while rate-limited / offline (no live data yet).
/// </summary>
public static class UsageCache
{
    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeBarWin", "last-state.json");

    public sealed class CachedUsage
    {
        public double? FiveHourPct { get; set; }
        public DateTimeOffset? FiveHourReset { get; set; }
        public double? SevenDayPct { get; set; }
        public DateTimeOffset? SevenDayReset { get; set; }
        public double? OpusPct { get; set; }
        public DateTimeOffset? OpusReset { get; set; }
        public double? SonnetPct { get; set; }
        public DateTimeOffset? SonnetReset { get; set; }
        public bool ExtraUsageEnabled { get; set; }
        public DateTime SavedAtUtc { get; set; }

        public RealUsage ToRealUsage() => new()
        {
            FiveHour = FiveHourPct is { } a ? new UsageWindow(a, FiveHourReset) : null,
            SevenDay = SevenDayPct is { } b ? new UsageWindow(b, SevenDayReset) : null,
            SevenDayOpus = OpusPct is { } c ? new UsageWindow(c, OpusReset) : null,
            SevenDaySonnet = SonnetPct is { } d ? new UsageWindow(d, SonnetReset) : null,
            ExtraUsageEnabled = ExtraUsageEnabled
        };
    }

    public static void Save(RealUsage u, DateTime atUtc)
    {
        try
        {
            var dto = new CachedUsage
            {
                FiveHourPct = u.FiveHour?.UtilizationPct,
                FiveHourReset = u.FiveHour?.ResetsAt,
                SevenDayPct = u.SevenDay?.UtilizationPct,
                SevenDayReset = u.SevenDay?.ResetsAt,
                OpusPct = u.SevenDayOpus?.UtilizationPct,
                OpusReset = u.SevenDayOpus?.ResetsAt,
                SonnetPct = u.SevenDaySonnet?.UtilizationPct,
                SonnetReset = u.SevenDaySonnet?.ResetsAt,
                ExtraUsageEnabled = u.ExtraUsageEnabled,
                SavedAtUtc = atUtc
            };
            AtomicFile.WriteAllText(CachePath, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // non-fatal
        }
    }

    public static CachedUsage? Load()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            return JsonSerializer.Deserialize<CachedUsage>(File.ReadAllText(CachePath));
        }
        catch
        {
            return null;
        }
    }
}
