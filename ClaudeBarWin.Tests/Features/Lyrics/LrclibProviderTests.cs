using System.Net;
using System.Text.Json;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary><see cref="LrclibProvider"/> (letras v2 · B2) con un handler falso: TODOS los resultados de
/// <c>/api/search</c>, sincronizados y planos; host, redirecciones y tamaño vigilados.</summary>
public class LrclibProviderTests
{
    private static readonly TimeSpan Dur = TimeSpan.FromSeconds(200);
    private static readonly SearchCandidate Q = new("Vai Rebola Pro Pai", "Kevin O Chris", 0);

    private static string Item(int id, double duration, string? synced, string? plain, bool instrumental = false) =>
        JsonSerializer.Serialize(new
        {
            id, trackName = $"T{id}", artistName = $"A{id}", albumName = "x", duration, instrumental,
            plainLyrics = plain, syncedLyrics = synced,
        });

    private static (LrclibProvider P, FakeHttp Http) New(Func<Uri, HttpResponseMessage> respond, TimeSpan? timeout = null)
    {
        var http = new FakeHttp(respond);
        return (new LrclibProvider(http, timeout ?? TimeSpan.FromSeconds(8)), http);
    }

    [Fact]
    public async Task Returns_every_synced_and_plain_result()
    {
        var (p, http) = New(_ => FakeHttp.Json(
            $"[{Item(1, 201, "[00:01.00]a", "a")},{Item(2, 180, null, "solo plana")},{Item(3, 200, null, null, instrumental: true)},{Item(4, 200, "  ", "")}]"));

        var r = await p.SearchAsync(Q, Dur, CancellationToken.None);

        Assert.Equal(2, r.Count);
        Assert.Equal(new LyricsCandidate("LRCLIB", "1", "T1", "A1", TimeSpan.FromSeconds(201), true, "[00:01.00]a"), r[0]);
        Assert.Equal(new LyricsCandidate("LRCLIB", "2", "T2", "A2", TimeSpan.FromSeconds(180), false, "solo plana"), r[1]);
        Assert.Equal("LRCLIB", p.Name);

        var req = Assert.Single(http.Requests);
        Assert.Equal("https://lrclib.net/api/search", req.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal(new Dictionary<string, string> { ["track_name"] = Q.Title, ["artist_name"] = Q.Artist }, FakeHttp.Query(req.RequestUri));
        Assert.StartsWith("ClaudeBarWin/", req.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Empty_exact_search_falls_back_to_free_query()
    {
        var (p, http) = New(u => FakeHttp.Query(u).ContainsKey("q")
            ? FakeHttp.Json($"[{Item(7, 200, "[00:01.00]x", null)}]")
            : FakeHttp.Json("[]"));

        var r = await p.SearchAsync(Q, Dur, CancellationToken.None);

        Assert.Equal("7", Assert.Single(r).ExternalId);
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal("Vai Rebola Pro Pai Kevin O Chris", FakeHttp.Query(http.Requests[1].RequestUri!)["q"]);
    }

    [Fact]
    public async Task Without_artist_only_track_name_and_404_is_nothing()
    {
        var (p, http) = New(_ => FakeHttp.Status(HttpStatusCode.NotFound));
        var r = await p.SearchAsync(new SearchCandidate("Solo", " ", 0), Dur, CancellationToken.None);

        Assert.Empty(r);
        Assert.Equal(new Dictionary<string, string> { ["track_name"] = "Solo" }, FakeHttp.Query(http.Requests[0].RequestUri!));
    }

    [Fact]
    public async Task Empty_title_makes_no_request()
    {
        var (p, http) = New(_ => throw new InvalidOperationException("no debería pedir"));
        Assert.Empty(await p.SearchAsync(new SearchCandidate(" ", "x", 0), Dur, CancellationToken.None));
        Assert.Empty(http.Requests);
    }

    public static IEnumerable<object[]> Failures() => new[]
    {
        new object[] { "500" }, new object[] { "302" }, new object[] { "big" }, new object[] { "host" },
        new object[] { "json" }, new object[] { "object" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Bad_responses_throw_a_provider_exception(string kind)
    {
        var (p, _) = New(_ => kind switch
        {
            "500" => FakeHttp.Status(HttpStatusCode.InternalServerError),
            "302" => FakeHttp.Redirect("https://evil.example/"),
            "big" => FakeHttp.TooBig(),
            "host" => FakeHttp.FromOtherHost("[]"),
            "json" => FakeHttp.Json("[{roto"),
            _ => FakeHttp.Json("{\"no\":\"array\"}"),
        });
        var ex = await Assert.ThrowsAsync<LyricsProviderException>(() => p.SearchAsync(Q, Dur, CancellationToken.None));
        Assert.DoesNotContain("Rebola", ex.Message);
    }

    [Fact]
    public async Task Timeout_throws_but_caller_cancellation_propagates()
    {
        var http = new FakeHttp(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return FakeHttp.Json("[]"); });
        var p = new LrclibProvider(http, TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<LyricsProviderException>(() => p.SearchAsync(Q, Dur, CancellationToken.None));

        var slow = new LrclibProvider(http, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow.SearchAsync(Q, Dur, cts.Token));
    }

    /// <summary>Medición del 29-oct: LRCLIB contesta 503/429 a ráfagas de búsquedas; un reintento tras una pausa
    /// las salva, y si vuelve a fallar es un fallo normal (no se insiste más).</summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Busy_answer_is_retried_once(HttpStatusCode busy)
    {
        int n = 0;
        var http = new FakeHttp(_ => n++ == 0 ? FakeHttp.Status(busy) : FakeHttp.Json($"[{Item(5, 200, "[00:01.00]y", null)}]"));
        var p = new LrclibProvider(http, TimeSpan.FromSeconds(8), TimeSpan.FromMilliseconds(1));

        Assert.Equal("5", Assert.Single(await p.SearchAsync(Q, Dur, CancellationToken.None)).ExternalId);
        Assert.Equal(2, http.Requests.Count);

        var always = new FakeHttp(_ => FakeHttp.Status(busy));
        var q = new LrclibProvider(always, TimeSpan.FromSeconds(8), TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<LyricsProviderException>(() => q.SearchAsync(Q, Dur, CancellationToken.None));
        Assert.Equal(2, always.Requests.Count);
    }

    [Fact]
    public void Default_handler_does_not_follow_redirects()
    {
        using var p = new LrclibProvider();
        using var h = LyricsHttp.CreateDefaultHandler();
        Assert.False(h.AllowAutoRedirect);
    }

    [Fact]
    public void Default_handler_does_not_keep_cookies()
    {
        // Ninguna fuente de letras necesita cookies: una búsqueda no arrastra estado de la anterior.
        using var h = LyricsHttp.CreateDefaultHandler();
        Assert.False(h.UseCookies);
    }
}
