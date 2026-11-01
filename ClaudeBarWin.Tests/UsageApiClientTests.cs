using System.Net;
using System.Net.Http;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// v0.3.10: el cliente de cuota no tenía tests. Todo con HTTP falso, reloj falso y credenciales en un
/// archivo temporal: NUNCA toca la API real ni el ~/.claude/.credentials.json del usuario.
/// </summary>
public sealed class UsageApiClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "claudebar-tests", "api-" + Guid.NewGuid().ToString("N"));
    private readonly string _cred;
    private DateTime _now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    public UsageApiClientTests()
    {
        Directory.CreateDirectory(_dir);
        _cred = Path.Combine(_dir, ".credentials.json");
        WriteCreds(expiresAt: _now.AddHours(8));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // Cada escritura deja una fecha distinta, como cuando Claude Code reescribe el archivo de verdad (dos escrituras
    // seguidas en el test podrían caer en el mismo tic del reloj de NTFS y parecer el mismo archivo).
    private int _writes;
    private void WriteCreds(DateTime expiresAt)
    {
        File.WriteAllText(_cred,
            $"{{\"claudeAiOauth\":{{\"accessToken\":\"sk-ant-oat01-test\",\"refreshToken\":\"r\",\"expiresAt\":{new DateTimeOffset(expiresAt).ToUnixTimeMilliseconds()}}}}}");
        File.SetLastWriteTimeUtc(_cred, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(++_writes));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Next = () => new HttpResponseMessage(HttpStatusCode.OK);
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Next());
        }
    }

    private const string OkJson = """
        {"five_hour":{"utilization":37.5,"resets_at":"2026-09-25T13:00:00+00:00"},
         "seven_day":{"utilization":24,"resets_at":"2026-09-28T06:00:00Z"},
         "extra_usage":{"is_enabled":true}}
        """;

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent(OkJson) };
    private static HttpResponseMessage TooMany(Action<HttpResponseMessage>? cfg = null)
    {
        var r = new HttpResponseMessage((HttpStatusCode)429);
        cfg?.Invoke(r);
        return r;
    }

    private UsageApiClient Client(FakeHandler h, Func<bool>? cli = null) =>
        new(h, () => _cred, () => _now, cli ?? (() => false), () => 1.0 /* sin jitter */);

    [Fact]
    public async Task Ok_response_is_parsed_with_invariant_dates()
    {
        var h = new FakeHandler { Next = Ok };
        var r = await Client(h).FetchAsync();
        Assert.Equal(UsageFetchState.Ok, r.State);
        Assert.Equal(37.5, r.Usage!.FiveHour!.UtilizationPct);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 6, 0, 0, TimeSpan.Zero), r.Usage.SevenDay!.ResetsAt);
        Assert.True(r.Usage.ExtraUsageEnabled);
    }

    [Fact]
    public async Task Consecutive_429s_back_off_exponentially_and_skip_the_network_meanwhile()
    {
        var h = new FakeHandler { Next = () => TooMany() };
        var c = Client(h);

        Assert.Equal(UsageFetchState.RateLimited, (await c.FetchAsync()).State);
        Assert.Equal(_now.AddSeconds(300), c.CooldownUntilUtc);

        _now = _now.AddSeconds(100);
        Assert.Equal(UsageFetchState.RateLimited, (await c.FetchAsync()).State);
        Assert.Equal(1, h.Calls); // en enfriamiento no se llama a la API

        _now = c.CooldownUntilUtc;
        await c.FetchAsync();
        Assert.Equal(_now.AddSeconds(600), c.CooldownUntilUtc); // 2º 429 seguido: el doble

        for (int i = 0; i < 6; i++) { _now = c.CooldownUntilUtc; await c.FetchAsync(); }
        Assert.Equal(_now.AddSeconds(UsageApiClient.MaxBackoffSeconds), c.CooldownUntilUtc); // tope
    }

    [Fact]
    public async Task Retry_after_as_http_date_is_respected()
    {
        var h = new FakeHandler { Next = () => TooMany(r => r.Headers.RetryAfter =
            new System.Net.Http.Headers.RetryConditionHeaderValue(new DateTimeOffset(_now.AddMinutes(57)))) };
        var c = Client(h);
        await c.FetchAsync();
        Assert.Equal(_now.AddMinutes(57), c.CooldownUntilUtc); // antes: 300 s fijos
    }

    [Fact]
    public async Task Retry_after_in_seconds_is_respected()
    {
        var h = new FakeHandler { Next = () => TooMany(r => r.Headers.RetryAfter =
            new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(900))) };
        var c = Client(h);
        await c.FetchAsync();
        Assert.Equal(_now.AddSeconds(900), c.CooldownUntilUtc);
    }

    [Fact]
    public async Task Expired_token_does_not_spend_a_request_and_cli_refresh_has_a_cooldown()
    {
        WriteCreds(expiresAt: _now.AddMinutes(-5));
        var h = new FakeHandler { Next = Ok };
        int cliCalls = 0;
        var c = Client(h, () => { cliCalls++; return false; });

        Assert.Equal(UsageFetchState.AuthExpired, (await c.FetchAsync(allowCliRefresh: true)).State);
        Assert.Equal(0, h.Calls);
        Assert.Equal(1, cliCalls);

        _now = _now.AddMinutes(5);
        await c.FetchAsync(allowCliRefresh: true);
        Assert.Equal(1, cliCalls); // enfriamiento: no relanza `claude -p .` en cada tick

        _now = _now.AddMinutes(30);
        await c.FetchAsync(allowCliRefresh: true);
        Assert.Equal(2, cliCalls);
    }

    [Fact]
    public async Task Without_the_setting_an_expired_token_never_launches_the_cli()
    {
        // `claude -p .` es un prompt de verdad que gasta cuota: por defecto ni se lanza.
        WriteCreds(expiresAt: _now.AddMinutes(-5));
        var h = new FakeHandler { Next = Ok };
        int cliCalls = 0;
        var c = Client(h, () => { cliCalls++; return true; });

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(UsageFetchState.AuthExpired, (await c.FetchAsync()).State);
            _now = _now.AddMinutes(31);
        }
        Assert.Equal(0, cliCalls);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task Cli_refresh_that_renews_the_token_lets_the_same_poll_succeed()
    {
        WriteCreds(expiresAt: _now.AddMinutes(-5));
        var h = new FakeHandler { Next = Ok };
        var c = Client(h, () => { WriteCreds(expiresAt: _now.AddHours(8)); return true; });

        Assert.Equal(UsageFetchState.Ok, (await c.FetchAsync(allowCliRefresh: true)).State);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Token_renewed_by_claude_code_is_picked_up_on_the_next_poll()
    {
        WriteCreds(expiresAt: _now.AddMinutes(-5));
        var h = new FakeHandler { Next = Ok };
        var c = Client(h, () => false);
        Assert.Equal(UsageFetchState.AuthExpired, (await c.FetchAsync()).State);

        WriteCreds(expiresAt: _now.AddHours(8)); // Claude Code refrescó por su cuenta
        Assert.Equal(UsageFetchState.Ok, (await c.FetchAsync()).State);
    }

    // ---------- .credentials.json solo se abre cuando toca y un token inservible no se insiste ----------

    [Fact]
    public async Task A_rejected_token_is_not_retried_until_claude_code_rewrites_the_file()
    {
        var h = new FakeHandler { Next = () => new HttpResponseMessage(HttpStatusCode.Unauthorized) };
        var c = Client(h);
        Assert.Equal(UsageFetchState.AuthExpired, (await c.FetchAsync()).State);
        Assert.Equal(1, h.Calls);

        for (int i = 0; i < 5; i++)
        {
            _now = _now.AddMinutes(1);
            Assert.Equal(UsageFetchState.AuthExpired, (await c.FetchAsync()).State);
        }
        Assert.Equal(1, h.Calls);                         // mismo archivo, mismo token: no se gastan más peticiones

        WriteCreds(expiresAt: _now.AddHours(8));          // Claude Code renovó el token
        h.Next = Ok;
        Assert.Equal(UsageFetchState.Ok, (await c.FetchAsync()).State);
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task A_rejected_token_is_retried_after_a_while_anyway()
    {
        var h = new FakeHandler { Next = () => new HttpResponseMessage(HttpStatusCode.Forbidden) };
        var c = Client(h);
        await c.FetchAsync();
        _now = _now + UsageApiClient.DeadTokenRecheck;
        await c.FetchAsync();
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task An_expired_token_is_not_reread_while_the_file_does_not_change()
    {
        WriteCreds(expiresAt: _now.AddMinutes(-5));
        var c = Client(new FakeHandler { Next = Ok });
        Assert.Equal(UsageFetchState.AuthExpired, (await c.FetchAsync()).State);

        // Con el archivo bloqueado, abrirlo fallaría («sin credenciales»): seguir en «caducado» prueba que ni se abrió.
        using (new FileStream(_cred, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _now = _now.AddMinutes(1);
            Assert.Equal(UsageFetchState.AuthExpired, (await c.FetchAsync()).State);
        }
    }

    [Fact]
    public async Task While_rate_limited_the_credentials_file_is_not_opened()
    {
        var h = new FakeHandler { Next = () => TooMany() };
        var c = Client(h);
        await c.FetchAsync();

        using (new FileStream(_cred, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _now = _now.AddSeconds(60);
            Assert.Equal(UsageFetchState.RateLimited, (await c.FetchAsync()).State);
        }
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Missing_credentials_and_401_are_reported()
    {
        File.Delete(_cred);
        var h = new FakeHandler { Next = Ok };
        Assert.Equal(UsageFetchState.NoCredentials, (await Client(h).FetchAsync()).State);

        WriteCreds(_now.AddHours(1));
        h.Next = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        Assert.Equal(UsageFetchState.AuthExpired, (await Client(h).FetchAsync()).State);
    }

    [Fact]
    public async Task Server_error_is_a_network_error_without_cooldown()
    {
        var h = new FakeHandler { Next = () => new HttpResponseMessage(HttpStatusCode.BadGateway) };
        var c = Client(h);
        Assert.Equal(UsageFetchState.NetworkError, (await c.FetchAsync()).State);
        Assert.Equal(DateTime.MinValue, c.CooldownUntilUtc);
    }
}
