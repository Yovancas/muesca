using ClaudeBarWin.Services;

public class QuotaMergerTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static DateTimeOffset R(double hours) => new DateTimeOffset(Now).AddHours(hours);
    private static RateWindowObs O(double pct, double resetH, int agoMin, bool sl = true) => new(pct, R(resetH), Now.AddMinutes(-agoMin), sl);

    [Fact]
    public void Same_window_takes_the_highest_pct_even_if_older()
    {
        var p = QuotaMerger.Pick(new[] { O(30, 2, 0), O(35, 2, 20) }, Now)!;
        Assert.Equal(35, p.Pct);
    }

    [Fact]
    public void Newer_window_beats_higher_pct_of_old_window()
    {
        var p = QuotaMerger.Pick(new[] { O(90, 0.5, 1), O(5, 5.5, 0) }, Now)!;
        Assert.Equal(5, p.Pct); // resets a > 10 min: son ventanas distintas y manda la de reset más tardío
    }

    [Fact]
    public void Resets_within_tolerance_are_the_same_window()
    {
        var p = QuotaMerger.Pick(new[] { O(40, 2, 0), O(42, 2.1, 5) }, Now)!;
        Assert.Equal(42, p.Pct);
    }

    [Fact]
    public void Expired_candidates_are_ignored_when_a_live_one_exists() =>
        Assert.Equal(10, QuotaMerger.Pick(new[] { O(95, -0.1, 1), O(10, 4, 3) }, Now)!.Pct);

    [Fact]
    public void All_candidates_expired_means_the_window_reset_to_zero()
    {
        // Pasado su resets_at, ese uso ya no cuenta; con la API cada 15 min no se puede esperar a la próxima
        // lectura para enseñarlo (la barra se quedaría en el % viejo hasta un cuarto de hora).
        var p = QuotaMerger.Pick(new[] { O(95, -0.1, 1), O(80, -0.2, 3) }, Now)!;
        Assert.Equal(0, p.Pct);
        Assert.Null(p.ResetsAt);
        Assert.Equal(Now, p.ObservedUtc);
        Assert.False(p.FromStatusline);
    }

    [Fact]
    public void Without_resets_the_latest_observation_wins()
    {
        var a = new RateWindowObs(3, null, Now.AddMinutes(-5), false);
        var b = new RateWindowObs(1, null, Now, true);
        Assert.Equal(1, QuotaMerger.Pick(new[] { a, b }, Now)!.Pct);
    }

    [Fact]
    public void Empty_candidates_pick_null() => Assert.Null(QuotaMerger.Pick(Array.Empty<RateWindowObs>(), Now));

    [Fact]
    public void Merge_combines_statusline_5h7d_with_api_models()
    {
        var snap = new RateLimitSnapshot("s", Now, Now, new Dictionary<string, RateWindowObs>
        {
            ["five_hour"] = O(24, 2, 0), ["seven_day"] = O(41, 100, 0),
        });
        var api = new RealUsage
        {
            FiveHour = new UsageWindow(20, R(2)), SevenDay = new UsageWindow(40, R(100)),
            SevenDayOpus = new UsageWindow(33, R(100)), ExtraUsageEnabled = true,
        };
        var m = QuotaMerger.Merge(new[] { snap }, api, Now.AddMinutes(-14), Now)!;
        Assert.Equal(24, m.Usage.FiveHour!.UtilizationPct);
        Assert.Equal(41, m.Usage.SevenDay!.UtilizationPct);
        Assert.Equal(33, m.Usage.SevenDayOpus!.UtilizationPct);
        Assert.True(m.Usage.ExtraUsageEnabled);
        Assert.True(m.FromStatusline);
        Assert.Equal(Now, m.ObservedUtc);
    }

    [Fact]
    public void Merge_with_api_only_behaves_like_before()
    {
        var api = new RealUsage { FiveHour = new UsageWindow(20, R(2)), SevenDay = new UsageWindow(40, R(100)) };
        var m = QuotaMerger.Merge(Array.Empty<RateLimitSnapshot>(), api, Now.AddMinutes(-1), Now)!;
        Assert.Equal(20, m.Usage.FiveHour!.UtilizationPct);
        Assert.False(m.FromStatusline);
        Assert.Equal(Now.AddMinutes(-1), m.ObservedUtc);
    }

    [Fact]
    public void Merge_with_nothing_is_null() =>
        Assert.Null(QuotaMerger.Merge(Array.Empty<RateLimitSnapshot>(), null, DateTime.MinValue, Now));

    [Fact]
    public void Idle_session_heartbeat_never_lowers_a_newer_value()
    {
        var idle = new RateLimitSnapshot("idle", Now, Now.AddHours(-2), new Dictionary<string, RateWindowObs> { ["five_hour"] = O(10, 2, 0) });
        var busy = new RateLimitSnapshot("busy", Now.AddMinutes(-3), Now.AddMinutes(-3), new Dictionary<string, RateWindowObs> { ["five_hour"] = O(55, 2, 3) });
        Assert.Equal(55, QuotaMerger.Merge(new[] { idle, busy }, null, DateTime.MinValue, Now)!.Usage.FiveHour!.UtilizationPct);
    }
}
