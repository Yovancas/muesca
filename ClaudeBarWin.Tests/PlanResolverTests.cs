using System.Net;
using System.Net.Http;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El plan se quedaba en «Pro» tras pasar a Max (7-oct-2026): Claude Code no actualiza <c>subscriptionType</c> de
/// <c>.credentials.json</c> al renovar el token y ClaudeBar solo lo leía al arrancar. Ahora manda la API de perfil.
/// Todo con HTTP falso, reloj falso y archivos temporales: nunca toca la API real ni los archivos del usuario.
/// </summary>
public sealed class PlanResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "claudebar-tests", "plan-" + Guid.NewGuid().ToString("N"));
    private readonly string _cache;
    private DateTime _now = new(2026, 10, 7, 16, 0, 0, DateTimeKind.Utc);

    // Respuesta real de /api/oauth/profile (7-oct-2026), sin datos personales.
    private const string MaxProfileJson = """
        {"account":{"uuid":"a","has_claude_max":true,"has_claude_pro":false},
         "organization":{"uuid":"o","organization_type":"claude_max","billing_type":"stripe_subscription",
                         "rate_limit_tier":"default_claude_max_5x","subscription_status":"active"}}
        """;

    private static readonly PlanInfo StalePro = new("pro", "default_claude_ai");   // lo que deja Claude Code
    private static readonly PlanInfo Max5x = new("max", "default_claude_max_5x");

    public PlanResolverTests()
    {
        Directory.CreateDirectory(_dir);
        _cache = Path.Combine(_dir, "plan.json");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeProfile
    {
        public Func<PlanInfo?> Next = () => Max5x;
        public int Calls;
        public Task<PlanInfo?> Fetch() { Calls++; return Task.FromResult(Next()); }
    }

    // Estado simulado de .credentials.json: plan, sesión iniciada, cuándo se escribió y si se puede leer.
    private DateTime _written = new(2026, 10, 7, 15, 0, 0, DateTimeKind.Utc);
    private bool _hasToken = true;
    private bool _unreadable;

    private int _credsReads;   // veces que se «abrió» .credentials.json

    private PlanResolver Resolver(FakeProfile api, Func<PlanInfo> creds, bool useCredentials = true) =>
        new(api.Fetch, () =>
        {
            _credsReads++;
            return _unreadable ? null : new CredentialsState(creds(), _hasToken, _written);
        }, () => _now, _cache, useCredentials);

    // ---------- «Leer la cuota con tu sesión de Claude Code» apagado ----------

    [Fact]
    public async Task Without_the_session_setting_neither_the_profile_api_nor_the_credentials_file_are_touched()
    {
        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro, useCredentials: false);

        for (int i = 0; i < 3; i++)
        {
            await r.RefreshAsync(useProfileApi: false);
            _written = _written.AddMinutes(30);   // Claude Code lo reescribe: tampoco se mira
            _now = _now.AddHours(2);
        }
        Assert.Equal(0, api.Calls);
        Assert.Equal(0, _credsReads);             // ni al construir ni en cada refresco
        Assert.Equal("Plan desconocido", r.Current.Display);   // sin plan.json no hay nada que enseñar
        Assert.Equal("none", r.Source);

        await r.RefreshAsync();                   // al volver a activarlo se lee y se pregunta en esa misma vuelta
        Assert.Equal(1, _credsReads);
        Assert.Equal(1, api.Calls);
        Assert.Equal(Max5x, r.Current);
    }

    [Fact]
    public async Task Without_the_session_setting_the_last_profile_in_plan_json_is_shown()
    {
        await Resolver(new FakeProfile(), () => StalePro).RefreshAsync();   // un arranque anterior con el ajuste encendido
        _credsReads = 0;

        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro, useCredentials: false);
        Assert.Equal(Max5x, r.Current);
        Assert.Equal("cache", r.Source);
        await r.RefreshAsync(useProfileApi: false);
        Assert.Equal(Max5x, r.Current);
        Assert.Equal(0, _credsReads);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task Turning_the_setting_off_while_running_stops_reading_the_credentials_file()
    {
        var api = new FakeProfile { Next = () => null };   // la API no contesta: lo que se enseñaba salía del archivo
        var r = Resolver(api, () => StalePro);
        await r.RefreshAsync();
        Assert.Equal(StalePro, r.Current);
        int reads = _credsReads;

        await r.RefreshAsync(useProfileApi: false);
        _written = _written.AddMinutes(5);
        _now = _now.AddHours(2);
        await r.RefreshAsync(useProfileApi: false);
        Assert.Equal(reads, _credsReads);
        Assert.Equal("Plan desconocido", r.Current.Display);   // lo del archivo no se enseña con el ajuste apagado
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task Turning_the_setting_off_after_signing_out_does_not_bring_back_the_old_plan()
    {
        await Resolver(new FakeProfile(), () => StalePro).RefreshAsync();   // plan.json: Max de la cuenta de antes
        _hasToken = false;                                                   // /logout
        var r = Resolver(new FakeProfile(), () => StalePro);
        await r.RefreshAsync();
        Assert.Equal("credentials", r.Source);

        await r.RefreshAsync(useProfileApi: false);
        Assert.Equal("Plan desconocido", r.Current.Display);   // no el Max guardado de la cuenta que ya no está
        Assert.Equal("none", r.Source);
    }

    [Fact]
    public async Task Turning_the_setting_off_keeps_what_the_profile_api_said()
    {
        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro);
        await r.RefreshAsync();
        Assert.Equal(Max5x, r.Current);
        int reads = _credsReads;

        Assert.False(await r.RefreshAsync(useProfileApi: false));
        Assert.Equal(Max5x, r.Current);
        Assert.Equal("profile", r.Source);
        Assert.Equal(reads, _credsReads);
    }

    // ---------- parseo del perfil ----------

    [Fact]
    public void Real_max_profile_is_parsed_as_max_5x()
    {
        var p = UsageApiClient.ParseProfile(MaxProfileJson);
        Assert.Equal(Max5x, p);
        Assert.Equal("Plan Max · 5x", p!.Display);
    }

    [Fact]
    public void Pro_profile_shows_plan_pro()
    {
        var p = UsageApiClient.ParseProfile("""{"organization":{"organization_type":"claude_pro","rate_limit_tier":"default_claude_ai"}}""");
        Assert.Equal("Plan Pro", p!.Display);
    }

    [Fact]
    public void Max_20x_profile_shows_the_tier()
    {
        var p = UsageApiClient.ParseProfile("""{"organization":{"organization_type":"claude_max","rate_limit_tier":"default_claude_max_20x"}}""");
        Assert.Equal("Plan Max · 20x", p!.Display);
    }

    [Fact]
    public void Account_flags_are_the_fallback_when_the_org_type_is_missing_or_not_a_subscription()
    {
        Assert.Equal("max", UsageApiClient.ParseProfile("""{"account":{"has_claude_max":true}}""")!.SubscriptionType);
        var p = UsageApiClient.ParseProfile(
            """{"account":{"has_claude_max":false,"has_claude_pro":true},"organization":{"organization_type":"api_individual","rate_limit_tier":"x"}}""");
        Assert.Equal(new PlanInfo("pro", "x"), p);
    }

    [Theory]
    [InlineData("""{"organization":{"organization_type":"api_individual"}}""")]
    [InlineData("""{"organization":{"organization_type":null},"account":{"has_claude_max":false}}""")]
    [InlineData("""{"organization":"claude_max"}""")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("<html>")]
    [InlineData("")]
    public void Unusable_profiles_return_null_so_the_fallback_wins(string json)
    {
        Assert.Null(UsageApiClient.ParseProfile(json));
    }

    [Theory]
    [InlineData("claude_max", "max")]
    [InlineData("CLAUDE_MAX", "max")]
    [InlineData("claude_team", "team")]
    [InlineData("claude_enterprise", "enterprise")]
    public void Organization_type_maps_to_the_subscription(string orgType, string expected)
    {
        Assert.Equal(expected, PlanInfo.FromOrganizationType(orgType, "")!.SubscriptionType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("claude_")]
    [InlineData("api")]
    public void Non_subscription_org_types_map_to_null(string? orgType)
    {
        Assert.Null(PlanInfo.FromOrganizationType(orgType, "t"));
    }

    // ---------- resolver ----------

    [Fact]
    public async Task Stale_pro_credentials_are_overridden_by_the_profile()
    {
        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro);
        Assert.Equal(StalePro, r.Current); // antes de la red: lo único que hay

        Assert.True(await r.RefreshAsync());
        Assert.Equal(Max5x, r.Current);
        Assert.Equal("profile", r.Source);
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task Next_start_shows_the_cached_profile_immediately_without_network()
    {
        await Resolver(new FakeProfile(), () => StalePro).RefreshAsync();

        var offline = new FakeProfile { Next = () => null };
        var r = Resolver(offline, () => StalePro);
        Assert.Equal(Max5x, r.Current);
        Assert.Equal("cache", r.Source);
        Assert.Equal(0, offline.Calls);

        Assert.False(await r.RefreshAsync()); // sin red sigue con la caché, no vuelve a «Pro»
        Assert.Equal(Max5x, r.Current);
    }

    [Fact]
    public async Task Cache_from_another_login_is_ignored()
    {
        await Resolver(new FakeProfile(), () => StalePro).RefreshAsync();

        var relogged = new PlanInfo("team", "default_claude_team");
        var r = Resolver(new FakeProfile { Next = () => null }, () => relogged);
        Assert.Equal(relogged, r.Current);
        Assert.Equal("credentials", r.Source);
    }

    [Fact]
    public async Task Profile_is_asked_at_most_once_an_hour()
    {
        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro);
        await r.RefreshAsync();

        _now = _now.AddMinutes(59);
        await r.RefreshAsync();
        Assert.Equal(1, api.Calls);

        _now = _now.AddMinutes(1);
        await r.RefreshAsync();
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task Failures_retry_after_15_minutes_and_keep_the_fallback()
    {
        var api = new FakeProfile { Next = () => null };
        var r = Resolver(api, () => StalePro);
        Assert.False(await r.RefreshAsync());
        Assert.Equal(StalePro, r.Current);
        Assert.Equal("credentials", r.Source);

        _now = _now.AddMinutes(14);
        await r.RefreshAsync();
        Assert.Equal(1, api.Calls);

        api.Next = () => Max5x;
        _now = _now.AddMinutes(1);
        Assert.True(await r.RefreshAsync());
        Assert.Equal(Max5x, r.Current);
    }

    [Fact]
    public async Task Downgrade_on_the_web_is_picked_up_on_the_next_hourly_check()
    {
        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro);
        await r.RefreshAsync();

        api.Next = () => new PlanInfo("pro", "default_claude_ai");
        _now = _now.AddHours(1);
        Assert.True(await r.RefreshAsync());
        Assert.Equal("Plan Pro", r.Current.Display);
    }

    [Fact]
    public async Task A_new_login_invalidates_the_profile_and_asks_again_right_away()
    {
        var creds = StalePro;
        var api = new FakeProfile();
        var r = Resolver(api, () => creds);
        await r.RefreshAsync();

        creds = new PlanInfo("team", "default_claude_team"); // /login con otra cuenta
        _written = _written.AddHours(1);
        api.Next = () => null;                                 // y el perfil aún no responde
        _now = _now.AddMinutes(5);                             // dentro de la hora: aun así se pregunta
        Assert.True(await r.RefreshAsync());
        Assert.Equal(2, api.Calls);
        Assert.Equal(creds, r.Current); // no se queda con el Max de la cuenta anterior
    }

    [Fact]
    public async Task An_unreadable_credentials_file_is_not_treated_as_a_new_login()
    {
        var creds = StalePro;
        var api = new FakeProfile();
        var r = Resolver(api, () => creds);
        await r.RefreshAsync();

        _unreadable = true; // Claude Code reescribiendo el archivo justo en ese momento
        _now = _now.AddMinutes(5);
        Assert.False(await r.RefreshAsync());
        Assert.Equal(Max5x, r.Current);
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task A_login_to_another_account_with_the_same_stale_plan_is_detected_by_the_rewrite()
    {
        // Revisión adversarial: la cuenta A (Max, archivo «pro») → /login a la cuenta B (Pro), que escribe «pro»
        // idéntico. Antes se seguía viendo «Max» hasta 1 h; ahora la reescritura del archivo dispara la consulta.
        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro);
        await r.RefreshAsync();

        api.Next = () => new PlanInfo("pro", "default_claude_ai");
        _written = _written.AddHours(2);
        _now = _now.AddMinutes(3);
        Assert.True(await r.RefreshAsync());
        Assert.Equal("Plan Pro", r.Current.Display);
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task A_token_renewal_with_the_network_down_keeps_the_known_plan()
    {
        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro);
        await r.RefreshAsync();

        api.Next = () => null;
        _written = _written.AddHours(2); // Claude Code renovó el token
        _now = _now.AddMinutes(10);
        Assert.False(await r.RefreshAsync());
        Assert.Equal(Max5x, r.Current); // sin volver a «Pro» por un fallo de red
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task A_file_rewritten_constantly_does_not_hammer_the_profile_api()
    {
        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro);
        await r.RefreshAsync();

        for (int i = 0; i < 10; i++) // cada minuto, una sesión distinta renueva el token
        {
            _now = _now.AddMinutes(1);
            _written = _written.AddMinutes(1);
            await r.RefreshAsync();
        }
        Assert.InRange(api.Calls, 2, 6); // 1 al arrancar + como mucho 1 cada 2 min
    }

    [Fact]
    public async Task Signing_out_drops_the_previous_plan_and_signing_in_asks_again()
    {
        var api = new FakeProfile();
        var r = Resolver(api, () => StalePro);
        await r.RefreshAsync();

        _hasToken = false; // /logout
        _written = _written.AddHours(1);
        _now = _now.AddMinutes(1);
        Assert.True(await r.RefreshAsync());
        Assert.NotEqual(Max5x, r.Current);
        Assert.Equal("credentials", r.Source);

        _now = _now.AddMinutes(30);
        await r.RefreshAsync();
        Assert.Equal(1, api.Calls); // sin token no se pregunta

        _hasToken = true; // /login de nuevo
        _written = _written.AddHours(1);
        _now = _now.AddMinutes(1);
        Assert.True(await r.RefreshAsync());
        Assert.Equal(Max5x, r.Current);
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task The_cache_is_not_used_while_signed_out()
    {
        await Resolver(new FakeProfile(), () => StalePro).RefreshAsync();
        _hasToken = false;
        var r = Resolver(new FakeProfile { Next = () => null }, () => StalePro);
        Assert.Equal(StalePro, r.Current);
        Assert.Equal("credentials", r.Source);
    }

    [Fact]
    public async Task Exceptions_from_the_fetch_or_the_credentials_never_escape()
    {
        var r = new PlanResolver(() => throw new HttpRequestException("boom"), () => throw new IOException("locked"),
            () => _now, _cache);
        Assert.Equal("Plan desconocido", r.Current.Display);
        Assert.False(await r.RefreshAsync());
    }

    [Fact]
    public async Task An_exception_from_the_profile_fetch_keeps_the_fallback_and_retries_later()
    {
        int calls = 0;
        var r = new PlanResolver(() => { calls++; throw new HttpRequestException("boom"); },
            () => new CredentialsState(StalePro, HasToken: true, _written), () => _now, _cache);
        Assert.False(await r.RefreshAsync());
        Assert.Equal(StalePro, r.Current);
        Assert.Equal(1, calls);

        _now = _now.AddMinutes(14);
        await r.RefreshAsync();
        Assert.Equal(1, calls); // la excepción cuenta como fallo: reintento a los 15 min
    }

    [Fact]
    public void A_corrupt_cache_is_ignored()
    {
        File.WriteAllText(_cache, "{not json");
        var r = Resolver(new FakeProfile(), () => StalePro);
        Assert.Equal(StalePro, r.Current);
    }

    // ---------- cliente HTTP del perfil ----------

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Next = () => new HttpResponseMessage(HttpStatusCode.OK);
        public HttpRequestMessage? Last;
        public string? LastAuth; // la cabecera EN el envío: después se quita de la petición
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Last = request;
            LastAuth = request.Headers.Authorization?.ToString();
            return Task.FromResult(Next());
        }
    }

    private string WriteCreds(DateTime expiresAt)
    {
        var path = Path.Combine(_dir, ".credentials.json");
        File.WriteAllText(path,
            $"{{\"claudeAiOauth\":{{\"accessToken\":\"sk-ant-oat01-test\",\"refreshToken\":\"r\",\"expiresAt\":{new DateTimeOffset(expiresAt).ToUnixTimeMilliseconds()},\"subscriptionType\":\"pro\",\"rateLimitTier\":\"default_claude_ai\"}}}}");
        return path;
    }

    [Fact]
    public async Task FetchProfile_calls_the_profile_endpoint_with_the_oauth_headers()
    {
        var cred = WriteCreds(_now.AddHours(8));
        var h = new FakeHandler { Next = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MaxProfileJson) } };
        var api = new UsageApiClient(h, () => cred, () => _now, () => false, () => 1.0);

        Assert.Equal(Max5x, await api.FetchProfileAsync());
        Assert.Equal("https://api.anthropic.com/api/oauth/profile", h.Last!.RequestUri!.ToString());
        Assert.Equal("Bearer sk-ant-oat01-test", h.LastAuth);
        Assert.Null(h.Last.Headers.Authorization); // el token no sigue colgado de la petición
        Assert.Equal("oauth-2025-04-20", h.Last.Headers.GetValues("anthropic-beta").Single());
    }

    [Theory]
    [InlineData(429)]
    [InlineData(401)]
    [InlineData(500)]
    public async Task FetchProfile_errors_return_null(int status)
    {
        var cred = WriteCreds(_now.AddHours(8));
        var h = new FakeHandler { Next = () => new HttpResponseMessage((HttpStatusCode)status) };
        var api = new UsageApiClient(h, () => cred, () => _now, () => false, () => 1.0);
        Assert.Null(await api.FetchProfileAsync());
        Assert.Equal(DateTime.MinValue, api.CooldownUntilUtc); // un 429 del perfil no frena la cuota
    }

    [Fact]
    public async Task FetchProfile_with_an_expired_token_does_not_call_the_network_nor_the_cli()
    {
        var cred = WriteCreds(_now.AddMinutes(-1));
        var h = new FakeHandler();
        bool cli = false;
        var api = new UsageApiClient(h, () => cred, () => _now, () => cli = true, () => 1.0);
        Assert.Null(await api.FetchProfileAsync());
        Assert.Equal(0, h.Calls);
        Assert.False(cli);
    }

    [Fact]
    public void Credentials_plan_is_read_while_another_process_holds_the_file_open_for_writing()
    {
        var cred = WriteCreds(_now.AddHours(8));
        using var writer = new FileStream(cred, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal(StalePro, CredentialsReader.Read(cred));
        var st = CredentialsReader.ReadState(cred)!;
        Assert.True(st.HasToken);
        Assert.Equal(File.GetLastWriteTimeUtc(cred), st.WrittenUtc);
    }

    [Fact]
    public void Credentials_state_tells_signed_out_and_unreadable_apart()
    {
        var path = Path.Combine(_dir, "creds-state.json");
        Assert.False(CredentialsReader.ReadState(path)!.HasToken); // sin archivo = sesión cerrada

        File.WriteAllText(path, "{}");                               // /logout deja el archivo sin claudeAiOauth
        Assert.False(CredentialsReader.ReadState(path)!.HasToken);

        File.WriteAllText(path, "{\"claudeAiOauth\":{\"subscriptionType\":\"pro\",\"accessToken\":\"\"}}");
        Assert.False(CredentialsReader.ReadState(path)!.HasToken);   // token vacío = sin sesión

        File.WriteAllText(path, "{\"claudeAiOauth\":{\"subscri");   // a medio escribir
        Assert.Null(CredentialsReader.ReadState(path));
    }
}
