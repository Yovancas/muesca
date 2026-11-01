using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Telemetry;

namespace ClaudeBarWin.Tests;

/// <summary>Contador anónimo de uso: la decisión pura (qué día, qué casillas), la respuesta del servidor y las puertas.</summary>
public class UsagePingTests
{
    private static readonly DateTime Oct30 = new(2026, 10, 30, 9, 15, 0, DateTimeKind.Local);

    private static UsageStatsData S(string? day = null, string? month = null, bool first = false, bool fresh = false) =>
        new() { LastDay = day, LastMonth = month, FirstSent = first, NewInstall = fresh, NoticeSeen = true };

    // ------------------------------------------------------------ decidir

    [Fact]
    public void A_new_install_never_sent_gets_d_m_and_n()
    {
        var f = UsagePing.Decide(S(fresh: true), Oct30);
        Assert.Equal(new UsagePingFlags(Month: true, NewInstall: true), f);
        Assert.True(f!.Value.Day);
    }

    [Fact]
    public void An_upgrade_never_sent_is_not_counted_as_new()
    {
        Assert.Equal(new UsagePingFlags(Month: true, NewInstall: false), UsagePing.Decide(S(fresh: false), Oct30));
    }

    [Fact]
    public void Null_state_behaves_as_an_upgrade_never_sent()
    {
        Assert.Equal(new UsagePingFlags(true, false), UsagePing.Decide(null, Oct30));
    }

    [Fact]
    public void At_most_one_ping_per_local_day()
    {
        Assert.Null(UsagePing.Decide(S("2026-10-30", "2026-10", first: true), Oct30));
        Assert.Null(UsagePing.Decide(S("2026-10-30", "2026-10", first: true), Oct30.Date.AddHours(23).AddMinutes(59)));
        Assert.NotNull(UsagePing.Decide(S("2026-10-30", "2026-10", first: true), Oct30.Date.AddDays(1)));
    }

    [Fact]
    public void A_later_day_in_the_same_month_is_d_only()
    {
        Assert.Equal(new UsagePingFlags(false, false), UsagePing.Decide(S("2026-10-29", "2026-10", first: true), Oct30));
    }

    [Fact]
    public void The_first_ping_of_a_month_has_m()
    {
        var nov1 = new DateTime(2026, 11, 1, 0, 0, 1);
        Assert.Equal(new UsagePingFlags(true, false), UsagePing.Decide(S("2026-10-31", "2026-10", first: true), nov1));
        var jan = new DateTime(2027, 1, 2);
        Assert.Equal(new UsagePingFlags(true, false), UsagePing.Decide(S("2026-12-31", "2026-12", first: true), jan));
    }

    [Fact]
    public void N_only_on_the_very_first_accepted_ping_of_a_new_install()
    {
        var first = S(fresh: true);
        var after = UsagePing.Commit(first, Oct30);
        Assert.True(after.FirstSent);
        Assert.True(after.NewInstall);   // se conserva, pero ya no cuenta
        Assert.Equal(new UsagePingFlags(false, false), UsagePing.Decide(after, Oct30.AddDays(1)));
    }

    [Fact]
    public void A_clock_set_back_sends_nothing_and_keeps_the_state()
    {
        var state = S("2026-10-30", "2026-10", first: true);
        Assert.Null(UsagePing.Decide(state, new DateTime(2026, 10, 29)));
        Assert.Null(UsagePing.Decide(state, new DateTime(2025, 12, 31)));
        Assert.Equal("2026-10-30", state.LastDay);
        // Solo el mes guardado (sin día) y el reloj en un mes anterior: tampoco.
        Assert.Null(UsagePing.Decide(S(null, "2026-11", first: true), Oct30));
    }

    [Fact]
    public void A_failed_day_is_not_retried_until_the_next_day()
    {
        var state = S("2026-10-29", "2026-10", first: true);
        Assert.Null(UsagePing.Decide(state, Oct30, failedDay: "2026-10-30"));
        Assert.NotNull(UsagePing.Decide(state, Oct30.AddDays(1), failedDay: "2026-10-30"));
    }

    [Theory]
    [InlineData("garbage", "2026-10")]
    [InlineData("2026-13-40", null)]
    [InlineData("", "")]
    [InlineData("30/10/2026", "10-2026")]
    public void Unreadable_saved_values_count_as_absent(string? day, string? month)
    {
        var f = UsagePing.Decide(S(day, month, first: true), Oct30);
        Assert.NotNull(f);
        Assert.Equal(month != "2026-10", f!.Value.Month);
    }

    [Fact]
    public void A_last_day_newer_than_the_last_month_wins_for_m()
    {
        // config.json editado a mano: el día dice octubre y el mes septiembre → octubre ya está avisado.
        Assert.Equal(new UsagePingFlags(false, false), UsagePing.Decide(S("2026-10-02", "2026-09", first: true), Oct30));
    }

    [Fact]
    public void Commit_writes_local_day_and_month_and_keeps_the_notice()
    {
        var state = S(fresh: true) with { NoticeUtc = new DateTime(2026, 10, 30, 7, 0, 0, DateTimeKind.Utc) };
        var c = UsagePing.Commit(state, new DateTime(2026, 1, 5, 23, 59, 0));
        Assert.Equal("2026-01-05", c.LastDay);
        Assert.Equal("2026-01", c.LastMonth);
        Assert.True(c.FirstSent);
        Assert.True(c.NoticeSeen);
        Assert.Equal(state.NoticeUtc, c.NoticeUtc);
        Assert.Equal("2026-01-05", UsagePing.Commit(null, new DateTime(2026, 1, 5)).LastDay);
    }

    [Fact]
    public void Day_strings_ignore_the_current_culture()
    {
        var saved = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("th-TH");   // calendario budista
            Assert.Equal("2026-10-30", UsagePing.DayOf(Oct30));
            Assert.Equal("2026-10", UsagePing.MonthOf(Oct30));
        }
        finally { Thread.CurrentThread.CurrentCulture = saved; }
    }

    // ------------------------------------------------------------ respuesta

    [Theory]
    [InlineData(200, UsagePingResult.Sent)]
    [InlineData(204, UsagePingResult.Sent)]
    [InlineData(299, UsagePingResult.Sent)]
    [InlineData(429, UsagePingResult.Retry)]
    [InlineData(408, UsagePingResult.Retry)]
    [InlineData(500, UsagePingResult.Retry)]
    [InlineData(503, UsagePingResult.Retry)]
    [InlineData(400, UsagePingResult.FailedToday)]
    [InlineData(404, UsagePingResult.FailedToday)]
    [InlineData(413, UsagePingResult.FailedToday)]
    [InlineData(301, UsagePingResult.FailedToday)]
    public void Server_answers_map_to_what_to_do(int status, UsagePingResult expected) =>
        Assert.Equal(expected, UsagePing.Classify(status));

    [Fact]
    public void No_answer_is_retried_later() => Assert.Equal(UsagePingResult.Retry, UsagePing.Classify(null));

    // ------------------------------------------------------------ puertas

    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Nothing_is_sent_before_the_welcome_or_the_notice()
    {
        var seen = new UsageStatsData { NoticeSeen = true };
        Assert.True(UsagePing.MaySend(true, true, false, seen, Now));
        Assert.False(UsagePing.MaySend(true, true, onboardingPending: true, seen, Now));
        Assert.False(UsagePing.MaySend(true, true, false, new UsageStatsData(), Now));
        Assert.False(UsagePing.MaySend(true, true, false, null, Now));
    }

    [Fact]
    public void Off_in_settings_or_environment_sends_nothing()
    {
        var seen = new UsageStatsData { NoticeSeen = true };
        Assert.False(UsagePing.MaySend(false, true, false, seen, Now));
        Assert.False(UsagePing.MaySend(true, false, false, seen, Now));
    }

    [Fact]
    public void After_the_upgrade_balloon_it_waits_ten_minutes()
    {
        var shown = UsagePing.NoticeShown(new UsageStatsData(), Now);
        Assert.True(shown.NoticeSeen);
        Assert.False(UsagePing.MaySend(true, true, false, shown, Now));
        Assert.False(UsagePing.MaySend(true, true, false, shown, Now.AddMinutes(9).AddSeconds(59)));
        Assert.True(UsagePing.MaySend(true, true, false, shown, Now.AddMinutes(10)));
        // Reloj atrasado: se espera.
        Assert.False(UsagePing.MaySend(true, true, false, shown, Now.AddHours(-3)));
    }

    [Fact]
    public void A_notice_time_read_back_without_kind_counts_as_utc()
    {
        var shown = new UsageStatsData { NoticeSeen = true, NoticeUtc = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) };
        Assert.True(UsagePing.MaySend(true, true, false, shown, Now.AddMinutes(10)));
    }

    [Fact]
    public void The_upgrade_balloon_shows_once_and_only_when_it_matters()
    {
        Assert.True(UsagePing.NeedsUpgradeNotice(true, true, false, new UsageStatsData()));
        Assert.True(UsagePing.NeedsUpgradeNotice(true, true, false, null));
        Assert.False(UsagePing.NeedsUpgradeNotice(true, true, false, new UsageStatsData { NoticeSeen = true }));
        Assert.False(UsagePing.NeedsUpgradeNotice(true, true, onboardingPending: true, new UsageStatsData()));
        Assert.False(UsagePing.NeedsUpgradeNotice(false, true, false, new UsageStatsData()));
        Assert.False(UsagePing.NeedsUpgradeNotice(true, false, false, new UsageStatsData()));
    }

    [Fact]
    public void Seen_in_the_app_has_no_wait_and_does_not_reset_an_earlier_balloon()
    {
        var seen = UsagePing.NoticeSeenInApp(null);
        Assert.True(seen.NoticeSeen);
        Assert.Null(seen.NoticeUtc);
        var balloon = UsagePing.NoticeShown(null, Now);
        Assert.Same(balloon, UsagePing.NoticeSeenInApp(balloon));
    }

    // ------------------------------------------------------------ el aviso

    [Fact]
    public void The_json_is_exactly_the_six_fields_in_order()
    {
        var p = new UsagePingPayload("0.5.0", "free", "es", new UsagePingFlags(true, false));
        Assert.Equal("{\"v\":\"0.5.0\",\"e\":\"free\",\"l\":\"es\",\"d\":1,\"m\":1,\"n\":0}", p.ToJson());
        Assert.True(p.IsValid);
        Assert.Equal("{\"v\":\"1.2.3\",\"e\":\"pro\",\"l\":\"zh\",\"d\":1,\"m\":0,\"n\":1}",
            new UsagePingPayload("1.2.3", "pro", "zh", new UsagePingFlags(false, true)).ToJson());
    }

    [Theory]
    [InlineData("0.5.0", "free", "es", true)]
    [InlineData("0.0.0-test", "github", "en", true)]
    [InlineData("10.20.3000", "pro", "pt-BR", true)]
    [InlineData("0.5", "free", "es", false)]
    [InlineData("0.5.0+sha", "free", "es", false)]
    [InlineData("0.5.0", "store", "es", false)]
    [InlineData("0.5.0", "free", "zh-Hant", false)]
    [InlineData("0.5.0", "free", "ES", false)]
    public void Validation_mirrors_the_server(string v, string e, string l, bool ok) =>
        Assert.Equal(ok, new UsagePingPayload(v, e, l, default).IsValid);

    [Theory]
    [InlineData(ProState.Pro, true, "pro")]
    [InlineData(ProState.Pro, false, "pro")]
    [InlineData(ProState.Trial, true, "free")]
    [InlineData(ProState.Free, true, "free")]
    [InlineData(ProState.Free, false, "github")]
    [InlineData(ProState.Trial, false, "github")]
    public void Edition_comes_from_the_licence_and_the_package(ProState state, bool packaged, string expected) =>
        Assert.Equal(expected, UsageStatsFacts.Edition(state, packaged));

    [Theory]
    [InlineData("system", "es", "es")]
    [InlineData(null, "de", "de")]
    [InlineData("", "ja", "ja")]
    [InlineData("fr", "es", "fr")]
    [InlineData("zh-Hant", "en", "zh")]
    [InlineData("system", "zh-Hant", "zh")]
    [InlineData("klingon", "es", "en")]
    public void Language_is_the_interface_language(string? configured, string system, string expected) =>
        Assert.Equal(expected, UsageStatsFacts.Language(configured, system));

    [Fact]
    public void Every_interface_language_is_accepted_by_the_server()
    {
        foreach (var (code, _) in ClaudeBarWin.Services.Localization.Languages)
            Assert.True(new UsagePingPayload("0.5.0", "free", UsageStatsFacts.Language(code, "en"), default).IsValid, code);
    }

    [Fact]
    public void Version_is_major_minor_build()
    {
        Assert.Equal("0.5.0", UsageStatsFacts.Version(new Version(0, 5, 0, 7)));
        Assert.Equal("1.2.0", UsageStatsFacts.Version(new Version(1, 2)));
        Assert.Equal("0.0.0", UsageStatsFacts.Version(null));
        Assert.True(new UsagePingPayload(UsageStatsFacts.Version(typeof(UsagePing).Assembly.GetName().Version), "free", "es",
            default).IsValid);
    }

    // ------------------------------------------------------------ entorno y destino

    [Fact]
    public void Environment_switches()
    {
        Func<string, string?> Env(params (string k, string v)[] vars) => k => vars.FirstOrDefault(x => x.k == k).v;
        Assert.True(UsageStatsEnvironment.Allows(Env(), debugBuild: false));
        Assert.False(UsageStatsEnvironment.Allows(Env(("CLAUDEBAR_NO_STATS", "1")), false));
        Assert.True(UsageStatsEnvironment.Allows(Env(("CLAUDEBAR_NO_STATS", "0")), false));
        Assert.False(UsageStatsEnvironment.Allows(Env(), debugBuild: true));
        Assert.True(UsageStatsEnvironment.Allows(Env(("CLAUDEBAR_STATS_DEBUG", "1")), true));
        Assert.False(UsageStatsEnvironment.Allows(Env(("CLAUDEBAR_STATS_DEBUG", "1"), ("CLAUDEBAR_NO_STATS", "1")), true));
    }

    [Theory]
    [InlineData(null, UsageStatsClient.DefaultUrl)]
    [InlineData("  ", UsageStatsClient.DefaultUrl)]
    [InlineData("https://example.test/v1/ping", "https://example.test/v1/ping")]
    [InlineData("http://127.0.0.1:5123/v1/ping", "http://127.0.0.1:5123/v1/ping")]
    [InlineData("http://localhost:5123/v1/ping", "http://localhost:5123/v1/ping")]
    [InlineData("http://example.test/v1/ping", null)]
    [InlineData("ftp://example.test/", null)]
    [InlineData("https://user:pw@example.test/", null)]
    [InlineData("not a url", null)]
    public void Endpoint_is_https_or_loopback_only(string? value, string? expected) =>
        Assert.Equal(expected, UsageStatsClient.ResolveEndpoint(value)?.ToString());

    [Fact]
    public void The_default_endpoint_is_the_stats_worker_over_https()
    {
        var uri = UsageStatsClient.ResolveEndpoint(null)!;
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("claudebar-stats.claudebar-stats-worker.workers.dev", uri.Host);
        Assert.Equal("/v1/ping", uri.AbsolutePath);
    }
}
