using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class UsageFormatTests
{
    [Fact]
    public void CountdownAt_counts_against_the_given_now()
    {
        var now = new DateTimeOffset(2026, 10, 30, 17, 20, 0, TimeSpan.Zero);
        Assert.Equal("1h 40m", UsageFormat.CountdownAt(now.AddMinutes(100), "resetting…", now));
        Assert.Equal("2d 3h", UsageFormat.CountdownAt(now.AddDays(2).AddHours(3).AddMinutes(5), "resetting…", now));
        Assert.Equal("4m 30s", UsageFormat.CountdownAt(now.AddSeconds(270), "resetting…", now));
        Assert.Equal("resetting…", UsageFormat.CountdownAt(now, "resetting…", now));
        Assert.Equal("resetting…", UsageFormat.CountdownAt(now.AddMinutes(-1), "resetting…", now));
        Assert.Equal("", UsageFormat.CountdownAt(null, "resetting…", now));
    }

    [Fact]
    public void ResetAbsolute_formats_local_time_as_ddd_HHmm()
    {
        // Un instante UTC concreto; ResetAbsolute lo pasa a hora local en formato "ddd HH:mm"
        // con la cultura del idioma elegido (T2), no la CurrentCulture del SO.
        var ci = Localization.Get("en").Culture;
        var when = new DateTimeOffset(2026, 6, 2, 16, 42, 0, TimeSpan.Zero);
        var expected = when.ToLocalTime().ToString("ddd HH:mm", ci);

        Assert.Equal(expected, UsageFormat.ResetAbsolute(when, ci));
    }

    [Fact]
    public void ResetAbsolute_returns_empty_for_null()
    {
        Assert.Equal("", UsageFormat.ResetAbsolute(null, Localization.Get("en").Culture));
    }

    [Fact]
    public void Relative_formats_minutes_localized()
    {
        var s = Localization.Get("es");
        var fiveMinAgo = DateTime.UtcNow - TimeSpan.FromMinutes(5);

        Assert.Equal("hace 5 min", UsageFormat.Relative(fiveMinAgo, s));
    }

    [Fact]
    public void Relative_formats_seconds_localized()
    {
        var s = Localization.Get("es");
        var thirtySecAgo = DateTime.UtcNow - TimeSpan.FromSeconds(30);

        Assert.Equal("hace 30 s", UsageFormat.Relative(thirtySecAgo, s));
    }

    [Fact]
    public void IsStale_is_true_past_three_times_refresh()
    {
        // 10 min de antigüedad con refresh de 60s → umbral 3×60=180s → stale.
        Assert.True(UsageFormat.IsStale(DateTime.UtcNow - TimeSpan.FromMinutes(10), 60));
    }

    [Fact]
    public void IsStale_is_false_within_three_times_refresh()
    {
        // 30s de antigüedad con refresh de 60s → umbral 180s → fresco.
        Assert.False(UsageFormat.IsStale(DateTime.UtcNow - TimeSpan.FromSeconds(30), 60));
    }

    [Fact]
    public void Relative_normalizes_unspecified_kind_as_utc()
    {
        // Un DateTime con Kind=Unspecified debe tratarse como UTC (no como local),
        // si no el cálculo del relativo se desvía por el offset de zona.
        var unspecifiedFiveMinAgo = DateTime.SpecifyKind(
            DateTime.UtcNow - TimeSpan.FromMinutes(5), DateTimeKind.Unspecified);
        var s = Localization.Get("es");

        Assert.Equal("hace 5 min", UsageFormat.Relative(unspecifiedFiveMinAgo, s));
    }

    [Fact]
    public void IsStale_normalizes_unspecified_kind_as_utc()
    {
        var unspecifiedTenMinAgo = DateTime.SpecifyKind(
            DateTime.UtcNow - TimeSpan.FromMinutes(10), DateTimeKind.Unspecified);

        Assert.True(UsageFormat.IsStale(unspecifiedTenMinAgo, 60));
    }

    // v0.4: con la statusline fresca no se sondea la API; 5 min sin mensajes NO es «dato viejo».
    [Fact]
    public void Snapshot_fed_by_fresh_statusline_is_not_stale()
    {
        var now = DateTime.UtcNow;
        var snap = new AppSnapshot { UsageAtUtc = now.AddMinutes(-5), LiveFromStatusline = true };
        Assert.False(UsageFormat.IsSnapshotStaleAt(snap, now, 60));
    }

    [Fact]
    public void Snapshot_from_api_keeps_the_3x_refresh_rule()
    {
        var now = DateTime.UtcNow;
        Assert.True(UsageFormat.IsSnapshotStaleAt(new AppSnapshot { UsageAtUtc = now.AddMinutes(-5) }, now, 60));
        Assert.False(UsageFormat.IsSnapshotStaleAt(new AppSnapshot { UsageAtUtc = now.AddSeconds(-30) }, now, 60));
    }
}
