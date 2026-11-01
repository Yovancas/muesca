using System.Net.Http;
using System.Text.Json;

namespace ClaudeBarWin.Services;

public enum HealthLevel
{
    Unknown,
    Operational,
    Degraded,   // minor
    Outage      // major / critical
}

public sealed record HealthStatus(HealthLevel Level, string Description);

/// <summary>
/// Reads Anthropic's public Statuspage (status.claude.com) for a service-health indicator.
/// No auth; this is a separate, CDN-cached endpoint — not the rate-limited usage API.
/// </summary>
public static class StatusClient
{
    private const string StatusUrl = "https://status.claude.com/api/v2/status.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static HealthStatus? _cache;
    private static DateTime _cacheUntilUtc = DateTime.MinValue;

    public static async Task<HealthStatus?> FetchAsync()
    {
        if (DateTime.UtcNow < _cacheUntilUtc && _cache is not null)
            return _cache;

        try
        {
            var json = await Http.GetStringAsync(StatusUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("status", out var st))
                return _cache;

            string indicator = st.TryGetProperty("indicator", out var ind) ? ind.GetString() ?? "" : "";
            string desc = st.TryGetProperty("description", out var de) ? de.GetString() ?? "" : "";

            var level = indicator switch
            {
                "none" => HealthLevel.Operational,
                "minor" => HealthLevel.Degraded,
                "major" or "critical" => HealthLevel.Outage,
                _ => HealthLevel.Unknown
            };

            _cache = new HealthStatus(level, desc);
            _cacheUntilUtc = DateTime.UtcNow.AddMinutes(2); // polite caching
            return _cache;
        }
        catch
        {
            return _cache; // keep last known on failure
        }
    }
}
