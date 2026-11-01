using System.Net;
using System.Net.Http;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El token OAuth solo viaja por HTTPS a api.anthropic.com: ni redirecciones, ni otros hosts, ni respuestas que lleguen
/// de otro sitio. HTTP falso y credenciales en una carpeta temporal: NUNCA la red real ni el archivo del usuario.
/// </summary>
public sealed class OAuthTransportTests : IDisposable
{
    private const string Token = "sk-ant-oat01-transporte-de-prueba";
    private const string OkJson = """{"five_hour":{"utilization":12,"resets_at":"2026-09-25T13:00:00Z"}}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "claudebar-tests", "oauth-" + Guid.NewGuid().ToString("N"));
    private readonly string _cred;
    private readonly DateTime _now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    public OAuthTransportTests()
    {
        Directory.CreateDirectory(_dir);
        _cred = Path.Combine(_dir, ".credentials.json");
        var exp = new DateTimeOffset(_now.AddHours(8)).ToUnixTimeMilliseconds();
        File.WriteAllText(_cred, $"{{\"claudeAiOauth\":{{\"accessToken\":\"{Token}\",\"refreshToken\":\"r\",\"expiresAt\":{exp}}}}}");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>Apunta cada petición que le llega (URI y cabecera Authorization en ese momento).</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<(Uri? Uri, string? Auth, string UserAgent)> Seen = new();
        public readonly List<HttpRequestMessage> Requests = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Next =
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OkJson) };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request.RequestUri, request.Headers.Authorization?.ToString(), request.Headers.UserAgent.ToString()));
            Requests.Add(request);
            return Task.FromResult(Next(request));
        }
    }

    private UsageApiClient Client(RecordingHandler h) =>
        new(h, () => _cred, () => _now, () => false, () => 1.0);

    private static HttpResponseMessage Redirect(int code, string location)
    {
        var r = new HttpResponseMessage((HttpStatusCode)code);
        r.Headers.Location = new Uri(location);
        return r;
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task A_redirect_to_another_host_is_not_followed_and_the_header_never_leaves(int code)
    {
        var h = new RecordingHandler { Next = _ => Redirect(code, "https://evil.example/collect") };
        var r = await Client(h).FetchAsync();

        Assert.Equal(UsageFetchState.NetworkError, r.State);
        Assert.Null(r.Usage);
        var only = Assert.Single(h.Seen);                       // una sola petición: la redirección no se sigue
        Assert.Equal(OAuthTransport.AllowedHost, only.Uri!.Host);
        Assert.DoesNotContain(h.Seen, s => s.Uri!.Host != OAuthTransport.AllowedHost);
        Assert.DoesNotContain("evil.example", r.Error);          // ni el Location acaba en el log
    }

    [Fact]
    public async Task The_profile_does_not_follow_redirects_either()
    {
        var h = new RecordingHandler { Next = _ => Redirect(302, "https://evil.example/profile") };
        Assert.Null(await Client(h).FetchProfileAsync());
        Assert.Single(h.Seen);
    }

    [Fact]
    public async Task A_response_that_comes_from_another_host_is_rejected()
    {
        // Como si un handler hubiera seguido una redirección por su cuenta: la URI final no es api.anthropic.com.
        var h = new RecordingHandler
        {
            Next = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OkJson),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/api/oauth/usage"),
            }
        };
        var r = await Client(h).FetchAsync();
        Assert.Equal(UsageFetchState.NetworkError, r.State);
        Assert.Null(r.Usage);
    }

    [Theory]
    [InlineData("https://evil.example/api/oauth/usage")]
    [InlineData("http://api.anthropic.com/api/oauth/usage")]               // sin TLS
    [InlineData("https://api.anthropic.com.evil.example/api/oauth/usage")]
    [InlineData("https://api.anthropic.com:8443/api/oauth/usage")]
    [InlineData("https://user@api.anthropic.com/api/oauth/usage")]
    public async Task Nothing_is_sent_to_a_host_that_is_not_api_anthropic_com(string url)
    {
        var h = new RecordingHandler();
        using var http = OAuthTransport.CreateClient(h);
        var creds = CredentialsFile.ReadToken(_cred)!;

        await Assert.ThrowsAsync<HttpRequestException>(() => OAuthTransport.GetAsync(http, new Uri(url), creds, "b"));
        Assert.Empty(h.Seen);
    }

    [Theory]
    [InlineData("https://api.anthropic.com/api/oauth/usage", true)]
    [InlineData("https://API.Anthropic.com/api/oauth/profile", true)]
    [InlineData("https://api.anthropic.com:443/x", true)]
    [InlineData("http://api.anthropic.com/x", false)]
    [InlineData("https://api.anthropic.com:444/x", false)]
    [InlineData("https://anthropic.com/x", false)]
    [InlineData("https://console.anthropic.com/x", false)]
    [InlineData("https://evil.example/api.anthropic.com", false)]
    [InlineData("https://api.anthropic.com.evil.example/x", false)]
    [InlineData("https://a:b@api.anthropic.com/x", false)]
    [InlineData("wss://api.anthropic.com/x", false)]
    public void Only_https_to_api_anthropic_com_is_allowed(string url, bool allowed)
        => Assert.Equal(allowed, OAuthTransport.IsAllowed(new Uri(url)));

    [Fact]
    public void Relative_or_missing_uris_are_not_allowed()
    {
        Assert.False(OAuthTransport.IsAllowed(null));
        Assert.False(OAuthTransport.IsAllowed(new Uri("/api/oauth/usage", UriKind.Relative)));
    }

    [Fact]
    public void The_real_handler_does_not_follow_redirects_nor_keep_cookies_and_uses_the_system_proxy()
    {
        using var handler = OAuthTransport.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.Credentials);
        Assert.False(handler.PreAuthenticate);
        Assert.True(handler.UseProxy);
        Assert.Null(handler.Proxy);                               // null = HttpClient.DefaultProxy (el del sistema)
        Assert.True(handler.ConnectTimeout <= TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void The_client_has_a_short_timeout_and_a_bounded_response()
    {
        using var http = OAuthTransport.CreateClient(new RecordingHandler());
        Assert.True(http.Timeout <= TimeSpan.FromSeconds(15));
        Assert.True(http.MaxResponseContentBufferSize <= 1024 * 1024);
        Assert.Empty(http.DefaultRequestHeaders);                 // nada fijo: la cabecera va por petición
    }

    [Fact]
    public async Task An_oversized_response_is_a_network_error()
    {
        var h = new RecordingHandler
        {
            Next = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string(' ', OAuthTransport.MaxResponseBytes + 1) + OkJson)
            }
        };
        var r = await Client(h).FetchAsync();
        Assert.Equal(UsageFetchState.NetworkError, r.State);
    }

    [Fact]
    public async Task The_header_goes_only_with_the_request_and_is_dropped_once_answered()
    {
        var h = new RecordingHandler();
        var r = await Client(h).FetchAsync();

        Assert.Equal(UsageFetchState.Ok, r.State);
        Assert.Equal("Bearer " + Token, Assert.Single(h.Seen).Auth);
        Assert.Null(Assert.Single(h.Requests).Headers.Authorization); // la petición ya no lo guarda
        Assert.DoesNotContain(Token, r.Error ?? "");
    }

    [Fact]
    public async Task The_user_agent_is_claudebar_and_never_claude_cli()
    {
        var h = new RecordingHandler();
        await Client(h).FetchAsync();
        var ua = Assert.Single(h.Seen).UserAgent;
        Assert.StartsWith("ClaudeBarWin/", ua);
        Assert.DoesNotContain("claude-cli", ua, StringComparison.OrdinalIgnoreCase);
    }
}
