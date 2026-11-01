using System.Net;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Telemetry;

namespace ClaudeBarWin.Tests;

/// <summary>El servicio del contador (sin temporizador: <c>RunOnceAsync</c> a mano) y el cliente HTTP con un handler falso.</summary>
public class UsageStatsServiceTests
{
    private sealed class FakeSender : IUsageStatsSender
    {
        public readonly List<UsagePingPayload> Sent = new();
        public Queue<int?> Answers = new();
        public int? Default = 204;
        public bool Disposed;
        public Func<CancellationToken, Task>? Delay;

        public async Task<int?> SendAsync(UsagePingPayload payload, CancellationToken ct)
        {
            if (Delay is not null) await Delay(ct);
            Sent.Add(payload);
            return Answers.Count > 0 ? Answers.Dequeue() : Default;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class Harness : IDisposable
    {
        public AppConfig Config = new AppConfig { OnboardingPending = false }.Normalize();
        public readonly FakeSender Sender = new();
        public DateTime Local = new(2026, 10, 30, 10, 0, 0);
        public DateTime Utc = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
        public string Edition = "github";
        public int Persists;
        public readonly UsageStatsService Service;

        public Harness(bool environment = true, bool withSender = true)
        {
            Config.UsageStats = Config.UsageStats! with { NoticeSeen = true };
            Service = new UsageStatsService(withSender ? Sender : null, () => Config, () => Edition, "es", "0.5.0", environment,
                change => { change(Config); Persists++; }, () => Local, () => Utc, () => TimeSpan.FromHours(10));
            Service.Start(Config.UsageStatsEnabled);
        }

        public Task<UsagePingResult?> Run() => Service.RunOnceAsync(CancellationToken.None);
        public void Dispose() => Service.Dispose();
    }

    [Fact]
    public async Task Sends_once_a_day_and_saves_only_after_2xx()
    {
        using var h = new Harness();
        Assert.Equal(UsagePingResult.Sent, await h.Run());
        Assert.Single(h.Sender.Sent);
        Assert.Equal("{\"v\":\"0.5.0\",\"e\":\"github\",\"l\":\"es\",\"d\":1,\"m\":1,\"n\":0}", h.Sender.Sent[0].ToJson());
        Assert.Equal("2026-10-30", h.Config.UsageStats!.LastDay);
        Assert.Equal("2026-10", h.Config.UsageStats.LastMonth);
        Assert.True(h.Config.UsageStats.FirstSent);

        Assert.Null(await h.Run());           // mismo día: nada
        h.Local = h.Local.AddHours(13);       // 23:00
        Assert.Null(await h.Run());
        h.Local = h.Local.AddHours(2);        // día siguiente
        Assert.Equal(UsagePingResult.Sent, await h.Run());
        Assert.Equal(2, h.Sender.Sent.Count);
        Assert.Equal("{\"v\":\"0.5.0\",\"e\":\"github\",\"l\":\"es\",\"d\":1,\"m\":0,\"n\":0}", h.Sender.Sent[1].ToJson());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    public async Task Network_errors_5xx_and_429_save_nothing_and_retry_next_time(int? status)
    {
        using var h = new Harness();
        h.Sender.Answers.Enqueue(status);
        Assert.Equal(UsagePingResult.Retry, await h.Run());
        Assert.Null(h.Config.UsageStats!.LastDay);
        Assert.Equal(0, h.Persists);
        Assert.Equal(UsagePingResult.Sent, await h.Run());   // la siguiente comprobación, el mismo aviso
        Assert.Equal(h.Sender.Sent[0], h.Sender.Sent[1]);
    }

    [Fact]
    public async Task A_400_is_not_retried_that_day()
    {
        using var h = new Harness();
        h.Sender.Answers.Enqueue(400);
        Assert.Equal(UsagePingResult.FailedToday, await h.Run());
        Assert.Null(await h.Run());
        Assert.Single(h.Sender.Sent);
        Assert.Null(h.Config.UsageStats!.LastDay);
        h.Local = h.Local.AddDays(1);
        Assert.Equal(UsagePingResult.Sent, await h.Run());
    }

    [Fact]
    public async Task A_new_install_sends_n_once()
    {
        using var h = new Harness();
        h.Config.UsageStats = h.Config.UsageStats! with { NewInstall = true };
        h.Edition = "pro";
        await h.Run();
        h.Local = h.Local.AddDays(1);
        await h.Run();
        Assert.Contains("\"n\":1", h.Sender.Sent[0].ToJson());
        Assert.Contains("\"n\":0", h.Sender.Sent[1].ToJson());
        Assert.Contains("\"e\":\"pro\"", h.Sender.Sent[0].ToJson());
    }

    [Fact]
    public async Task Fresh_config_counts_as_new_install_and_old_config_as_upgrade()
    {
        var fresh = new AppConfig { OnboardingPending = true }.Normalize();
        Assert.True(fresh.UsageStats!.NewInstall);
        var path = Path.Combine(Path.GetTempPath(), $"cb-stats-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"Language\":\"es\",\"OnboardingPending\":false}");
            var upgraded = AppConfig.Load(path);
            Assert.False(upgraded.UsageStats!.NewInstall);
            Assert.True(upgraded.UsageStatsEnabled);
            Assert.False(upgraded.UsageStats.NoticeSeen);
            File.Delete(path);
            Assert.True(AppConfig.Load(path).UsageStats!.NewInstall);   // sin config: se crea con la bienvenida pendiente
            Assert.True(AppConfig.Load(path).UsageStats!.NewInstall);   // y se queda guardado
        }
        finally { File.Delete(path); }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Nothing_before_the_welcome_or_with_the_switch_off()
    {
        using var h = new Harness();
        h.Config.OnboardingPending = true;
        Assert.Null(await h.Run());
        h.Config.OnboardingPending = false;
        h.Config.UsageStats = h.Config.UsageStats! with { NoticeSeen = false };
        Assert.Null(await h.Run());
        h.Config.UsageStats = h.Config.UsageStats with { NoticeSeen = true };
        h.Config.UsageStatsEnabled = false;
        Assert.Null(await h.Run());
        Assert.Empty(h.Sender.Sent);
    }

    [Fact]
    public async Task After_the_upgrade_balloon_the_first_ping_waits_ten_minutes()
    {
        using var h = new Harness();
        h.Config.UsageStats = UsagePing.NoticeShown(h.Config.UsageStats, h.Utc);
        Assert.Null(await h.Run());
        h.Utc = h.Utc.AddMinutes(10);
        Assert.Equal(UsagePingResult.Sent, await h.Run());
        Assert.NotNull(h.Config.UsageStats!.NoticeUtc);   // el commit no borra el aviso
    }

    [Fact]
    public async Task Environment_off_or_no_endpoint_never_sends()
    {
        using (var h = new Harness(environment: false))
        {
            Assert.False(h.Service.Active);
            Assert.Null(await h.Run());
            Assert.Empty(h.Sender.Sent);
        }
        using (var h = new Harness(withSender: false))
        {
            Assert.False(h.Service.Active);
            Assert.Null(await h.Run());
        }
    }

    [Fact]
    public async Task Turning_it_off_cancels_what_is_in_flight_and_stops()
    {
        using var h = new Harness();
        var started = new TaskCompletionSource();
        h.Sender.Delay = async ct => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); };
        var run = h.Service.RunOnceAsync(GetToken(h.Service));
        await started.Task;
        h.Service.SetEnabled(false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Empty(h.Sender.Sent);
        h.Sender.Delay = null;
        Assert.Null(await h.Run());   // apagado: ni lo intenta
        h.Service.SetEnabled(true);
        Assert.Equal(UsagePingResult.Sent, await h.Run());
    }

    private static CancellationToken GetToken(UsageStatsService s)
    {
        var f = typeof(UsageStatsService).GetField("_cts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return ((CancellationTokenSource)f.GetValue(s)!).Token;
    }

    [Fact]
    public async Task A_save_that_has_not_landed_yet_does_not_cause_a_second_ping()
    {
        var cfg = new AppConfig { OnboardingPending = false }.Normalize();
        cfg.UsageStats = cfg.UsageStats! with { NoticeSeen = true };
        var sender = new FakeSender();
        var pending = new List<Action<AppConfig>>();
        using var s = new UsageStatsService(sender, () => cfg, () => "free", "en", "0.5.0", true, pending.Add,
            () => new DateTime(2026, 10, 30, 10, 0, 0), () => DateTime.UtcNow);
        s.Start(true);
        await s.RunOnceAsync(CancellationToken.None);
        Assert.Null(await s.RunOnceAsync(CancellationToken.None));   // el hilo de UI aún no ha guardado
        Assert.Single(sender.Sent);
        pending[0](cfg);
        Assert.Equal("2026-10-30", cfg.UsageStats!.LastDay);
    }

    [Fact]
    public void Dispose_releases_the_sender_and_is_idempotent()
    {
        var h = new Harness();
        h.Dispose();
        h.Dispose();
        Assert.True(h.Sender.Disposed);
    }

    [Fact]
    public void First_check_is_between_two_and_five_minutes()
    {
        for (int i = 0; i < 200; i++)
        {
            var d = UsageStatsService.RandomFirstDelay();
            Assert.InRange(d, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5));
        }
        Assert.Equal(TimeSpan.FromHours(1), UsageStatsService.Period);
    }

    [Fact]
    public async Task The_timer_fires_the_check_on_its_own()
    {
        var cfg = new AppConfig { OnboardingPending = false }.Normalize();
        cfg.UsageStats = cfg.UsageStats! with { NoticeSeen = true };
        var sender = new FakeSender();
        var done = new TaskCompletionSource();
        using var s = new UsageStatsService(sender, () => cfg, () => "free", "en", "0.5.0", true,
            c => { c(cfg); done.TrySetResult(); }, firstDelay: () => TimeSpan.FromMilliseconds(20));
        s.Start(true);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(sender.Sent);
    }

    // ------------------------------------------------------------ cliente HTTP

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.NoContent;
        public Exception? Throw;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (Throw is not null) throw Throw;
            return new HttpResponseMessage(Status);
        }
    }

    [Fact]
    public async Task The_request_carries_only_the_payload_and_a_bare_user_agent()
    {
        var handler = new CaptureHandler();
        using var client = new UsageStatsClient(UsageStatsClient.ResolveEndpoint(null)!, handler);
        var p = new UsagePingPayload("0.5.0", "free", "es", new UsagePingFlags(true, true));
        Assert.Equal(204, await client.SendAsync(p, CancellationToken.None));

        var req = handler.Request!;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(UsageStatsClient.DefaultUrl, req.RequestUri!.ToString());
        Assert.Equal(p.ToJson(), handler.Body);
        Assert.Equal("ClaudeBar", req.Headers.UserAgent.ToString());
        Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);
        // Ni cookies, ni autorización, ni cabeceras propias: solo lo que pone el propio HttpClient.
        Assert.DoesNotContain(req.Headers, h => h.Key is not ("User-Agent" or "Expect"));
        Assert.Empty(req.Options);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 503)]
    public async Task Status_codes_come_back_as_numbers(HttpStatusCode status, int expected)
    {
        using var client = new UsageStatsClient(new Uri(UsageStatsClient.DefaultUrl), new CaptureHandler { Status = status });
        Assert.Equal(expected, await client.SendAsync(new UsagePingPayload("0.5.0", "free", "es", default), CancellationToken.None));
    }

    [Fact]
    public async Task Network_failures_and_timeouts_are_no_answer()
    {
        using (var client = new UsageStatsClient(new Uri(UsageStatsClient.DefaultUrl),
                   new CaptureHandler { Throw = new HttpRequestException("dns") }))
            Assert.Null(await client.SendAsync(new UsagePingPayload("0.5.0", "free", "es", default), CancellationToken.None));
        using (var client = new UsageStatsClient(new Uri(UsageStatsClient.DefaultUrl),
                   new CaptureHandler { Throw = new TaskCanceledException("timeout") }))
            Assert.Null(await client.SendAsync(new UsagePingPayload("0.5.0", "free", "es", default), CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_is_not_swallowed_as_a_network_error()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var client = new UsageStatsClient(new Uri(UsageStatsClient.DefaultUrl),
            new CaptureHandler { Throw = new TaskCanceledException() });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SendAsync(new UsagePingPayload("0.5.0", "free", "es", default), cts.Token));
    }

    [Fact]
    public void Timeout_is_ten_seconds() => Assert.Equal(TimeSpan.FromSeconds(10), UsageStatsClient.Timeout);

    /// <summary>
    /// Prueba real contra el servidor desplegado. Saltada salvo con <c>CLAUDEBAR_STATS_LIVE=1</c> (sin ella no hace nada):
    /// manda un aviso con la versión <c>0.0.0-test</c> y espera un 204.
    /// </summary>
    [Fact]
    public async Task Live_ping_to_the_real_server_returns_204_when_enabled()
    {
        if (Environment.GetEnvironmentVariable("CLAUDEBAR_STATS_LIVE") != "1") return;
        var endpoint = UsageStatsClient.ResolveEndpoint(Environment.GetEnvironmentVariable(UsageStatsEnvironment.UrlVariable));
        Assert.NotNull(endpoint);
        using var client = new UsageStatsClient(endpoint!);
        var payload = new UsagePingPayload("0.0.0-test", "github", "es", new UsagePingFlags(false, false));
        Assert.True(payload.IsValid);
        Assert.Equal(204, await client.SendAsync(payload, CancellationToken.None));
    }
}
