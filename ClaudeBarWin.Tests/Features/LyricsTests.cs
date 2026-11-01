using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Letras sincronizadas: <see cref="LrcParser"/> (LRC → líneas ordenadas), <see cref="LyricsQuery"/> (URLs de
/// búsqueda en LRCLIB) y <see cref="LrclibClient"/> con un handler falso: nada sale a la red en los tests.
/// </summary>
public class LyricsTests
{
    // ---------------- LrcParser ----------------

    [Fact]
    public void Parses_multiple_stamps_and_offset()
    {
        const string lrc = "[ti:Canción]\n[ar:Alguien]\n[offset:+500]\r\n" +
                           "[00:10.00][00:20.50]Hola\n" +
                           "[00:15.00]\n" +
                           "[00:05.123]Primero\n" +
                           "basura sin marca\n" +
                           "[01:02:30]Formato con dos puntos";
        var lines = LrcParser.Parse(lrc);

        Assert.Equal(new[]
        {
            new LyricLine(TimeSpan.FromMilliseconds(4623), "Primero"),
            new LyricLine(TimeSpan.FromMilliseconds(9500), "Hola"),
            new LyricLine(TimeSpan.FromMilliseconds(14500), "♪"),
            new LyricLine(TimeSpan.FromMilliseconds(20000), "Hola"),
            new LyricLine(TimeSpan.FromMilliseconds(61800), "Formato con dos puntos"),
        }, lines);
    }

    [Fact]
    public void Negative_offset_delays_and_never_goes_below_zero()
    {
        var late = LrcParser.Parse("[offset:-250]\n[00:01.00]a");
        Assert.Equal(TimeSpan.FromMilliseconds(1250), late[0].At);
        var early = LrcParser.Parse("[offset:2000]\n[00:01.00]a");
        Assert.Equal(TimeSpan.Zero, early[0].At);
    }

    [Fact]
    public void Empty_or_null_input_gives_no_lines()
    {
        Assert.Empty(LrcParser.Parse(""));
        Assert.Empty(LrcParser.Parse(null!));
        Assert.Empty(LrcParser.Parse("[ar:x]\nsin marcas"));
    }

    [Fact]
    public void IndexAt_with_lead()
    {
        var lines = new[]
        {
            new LyricLine(TimeSpan.FromSeconds(1), "a"),
            new LyricLine(TimeSpan.FromSeconds(2), "b"),
            new LyricLine(TimeSpan.FromSeconds(3), "c"),
        };
        var lead = TimeSpan.FromMilliseconds(80);
        Assert.Equal(-1, LrcParser.IndexAt(lines, TimeSpan.FromMilliseconds(500), lead));
        Assert.Equal(-1, LrcParser.IndexAt(lines, TimeSpan.FromMilliseconds(919), lead));
        Assert.Equal(0, LrcParser.IndexAt(lines, TimeSpan.FromMilliseconds(920), lead));
        Assert.Equal(0, LrcParser.IndexAt(lines, TimeSpan.FromMilliseconds(1919), lead));
        Assert.Equal(1, LrcParser.IndexAt(lines, TimeSpan.FromMilliseconds(1920), lead));
        Assert.Equal(2, LrcParser.IndexAt(lines, TimeSpan.FromMinutes(10), lead));
        Assert.Equal(-1, LrcParser.IndexAt(Array.Empty<LyricLine>(), TimeSpan.FromSeconds(5), lead));
    }

    // ---------------- LyricsQuery ----------------

    [Fact]
    public void Candidates_clean_title_and_main_artist()
    {
        var urls = LyricsQuery.Candidates("Song Name (Remastered 2011) - Live", "Artist A, Artist B feat. C");
        Assert.Equal(3, urls.Count);
        Assert.All(urls, u => Assert.StartsWith("https://lrclib.net/api/search?", u));

        Assert.Equal(Q(("track_name", "Song Name (Remastered 2011) - Live"), ("artist_name", "Artist A, Artist B feat. C")), Query(urls[0]));
        Assert.Equal(Q(("track_name", "Song Name"), ("artist_name", "Artist A")), Query(urls[1]));
        Assert.Equal(Q(("q", "Song Name Artist A")), Query(urls[2]));
    }

    [Theory]
    [InlineData("Tema [Live]", "Uno & Dos", "Tema", "Uno")]
    [InlineData("Otro - 2020 Remaster", "Solista Feat. Invitado", "Otro", "Solista")]
    public void Candidates_cut_rules(string title, string artist, string cleanTitle, string mainArtist)
    {
        var urls = LyricsQuery.Candidates(title, artist);
        Assert.Equal(Q(("track_name", cleanTitle), ("artist_name", mainArtist)), Query(urls[1]));
    }

    [Fact]
    public void Candidates_have_no_duplicates()
    {
        var urls = LyricsQuery.Candidates("Simple", "Artista");
        Assert.Equal(2, urls.Count); // completo == limpio
        Assert.Equal(Q(("q", "Simple Artista")), Query(urls[1]));

        Assert.Empty(LyricsQuery.Candidates("  ", "Artista"));
        var noArtist = LyricsQuery.Candidates("Solo título", "");
        Assert.Equal(Q(("track_name", "Solo título")), Query(noArtist[0]));
        Assert.Equal(urls.Count, urls.Distinct().Count());
    }

    // ---------------- LrclibClient ----------------

    private const string SyncedA = "[00:01.00]primera\n[00:02.00]segunda";

    private static string Item(double duration, string? synced, bool instrumental = false) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            id = 1, trackName = "t", artistName = "a", duration, instrumental,
            plainLyrics = "plano", syncedLyrics = synced,
        });

    private static (LrclibClient Client, FakeHandler Http, List<TimeSpan> Delays) New(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
    {
        var http = new FakeHandler(respond);
        var delays = new List<TimeSpan>();
        var client = new LrclibClient(http, timeout ?? TimeSpan.FromSeconds(8), (d, _) => { delays.Add(d); return Task.CompletedTask; });
        return (client, http, delays);
    }

    private static Task<HttpResponseMessage> Json(string body, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.OK) =>
        Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });

    [Fact]
    public async Task Picks_synced_with_closest_duration()
    {
        var (client, http, _) = New((_, _) => Json(
            $"[{Item(200, null)},{Item(250, "[00:09.00]lejos")},{Item(205, SyncedA)},{Item(195, "[00:07.00]empate")}]"));
        var r = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);

        Assert.Equal(LyricsStatus.Found, r.Status);
        Assert.Equal("primera", r.Lines[0].Text); // 205 y 195 empatan → gana el primero
        Assert.Equal(1, http.Calls);
        var ua = http.Requests[0].Headers.UserAgent.ToString();
        Assert.StartsWith("ClaudeBarWin/", ua);
        Assert.Contains("(+https://github.com/Yovancas/muesca)", ua);
    }

    [Fact]
    public async Task Falls_through_candidates_until_synced()
    {
        var (client, http, _) = New((req, _) =>
            Json(req.RequestUri!.Query.Contains("q=") ? $"[{Item(180, SyncedA)}]" : "[]"));
        var r = await client.FetchAsync("Song (Live)", "Artist", TimeSpan.FromSeconds(180), CancellationToken.None);
        Assert.Equal(LyricsStatus.Found, r.Status);
        Assert.Equal(3, http.Calls);
    }

    [Fact]
    public async Task NotFound_does_not_retry()
    {
        var (client, http, delays) = New((_, _) => Json($"[{Item(200, null)},{Item(200, SyncedA, instrumental: true)}]"));
        var r = await client.FetchAsync("Song (Live)", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);

        Assert.Equal(LyricsStatus.NotFound, r.Status);
        Assert.Empty(r.Lines);
        Assert.Equal(3, http.Calls);   // una pasada por los 3 candidatos, sin reintentos
        Assert.Empty(delays);

        // «Sin letra» queda en caché: volver a la canción no vuelve a preguntar.
        var again = await client.FetchAsync("Song (Live)", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.NotFound, again.Status);
        Assert.Equal(3, http.Calls);
    }

    [Fact]
    public async Task Http_404_is_NotFound()
    {
        var (client, _, delays) = New((_, _) => Json("{\"message\":\"no\"}", System.Net.HttpStatusCode.NotFound));
        var r = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.NotFound, r.Status);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Error_retries_twice()
    {
        var (client, http, delays) = New((_, _) => Json("oops", System.Net.HttpStatusCode.InternalServerError));
        var r = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);

        Assert.Equal(LyricsStatus.Error, r.Status);
        Assert.Equal(3, http.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3) }, delays);

        // Un error no se guarda en caché: el siguiente intento pregunta otra vez.
        await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(6, http.Calls);
    }

    [Fact]
    public async Task Error_then_success_recovers()
    {
        int n = 0;
        var (client, http, delays) = New((_, _) =>
            ++n == 1 ? throw new HttpRequestException("caída") : Json($"[{Item(200, SyncedA)}]"));
        var r = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.Found, r.Status);
        Assert.Equal(2, http.Calls);
        Assert.Single(delays);
    }

    [Fact]
    public async Task Malformed_json_is_Error()
    {
        var (client, _, _) = New((_, _) => Json("{no es json"));
        var r = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.Error, r.Status);
    }

    [Fact]
    public async Task Cache_hits_without_http()
    {
        var (client, http, _) = New((_, _) => Json($"[{Item(200, SyncedA)}]"));
        var a = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        var b = await client.FetchAsync(" song ", "ARTIST", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.Found, b.Status);
        Assert.Same(a, b);
        Assert.Equal(1, http.Calls);
    }

    [Fact]
    public async Task Cache_is_LRU_of_20()
    {
        var (client, http, _) = New((_, _) => Json($"[{Item(200, SyncedA)}]"));
        for (int i = 0; i < 20; i++)
            await client.FetchAsync($"Song{i}", "A", TimeSpan.FromSeconds(200), CancellationToken.None);
        await client.FetchAsync("Song0", "A", TimeSpan.FromSeconds(200), CancellationToken.None); // la más usada
        Assert.Equal(20, http.Calls);
        await client.FetchAsync("Song20", "A", TimeSpan.FromSeconds(200), CancellationToken.None); // expulsa Song1
        await client.FetchAsync("Song0", "A", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(21, http.Calls);
        await client.FetchAsync("Song1", "A", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(22, http.Calls);
    }

    [Fact]
    public async Task Timeout_is_Error()
    {
        var (client, http, delays) = New(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        }, timeout: TimeSpan.FromMilliseconds(50));
        var r = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.Error, r.Status);
        Assert.Equal(3, http.Calls);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Caller_cancellation_throws_and_is_not_an_error()
    {
        using var cts = new CancellationTokenSource();
        var (client, _, _) = New(async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), cts.Token));
    }

    [Fact]
    public async Task Empty_title_is_NotFound_without_http()
    {
        var (client, http, _) = New((_, _) => Json("[]"));
        var r = await client.FetchAsync(" ", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.NotFound, r.Status);
        Assert.Equal(0, http.Calls);
    }

    // ---------------- Revisión API (punto 5): único host y tope de tamaño ----------------

    [Fact]
    public void Default_handler_does_not_follow_redirects_and_host_is_lrclib()
    {
        using var h = LrclibClient.CreateDefaultHandler();
        Assert.False(h.AllowAutoRedirect);
        Assert.Equal("lrclib.net", LrclibClient.Host);
        Assert.Equal(1 << 20, LrclibClient.MaxResponseBytes);
    }

    [Fact]
    public async Task Requests_only_go_to_https_lrclib()
    {
        var (client, http, _) = New((_, _) => Json("[]"));
        await client.FetchAsync("Song (Live)", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.NotEmpty(http.Requests);
        Assert.All(http.Requests, r =>
        {
            Assert.Equal("https", r.RequestUri!.Scheme);
            Assert.Equal("lrclib.net", r.RequestUri.Host);
        });
    }

    [Fact]
    public async Task Redirect_status_is_Error_and_not_followed()
    {
        var (client, http, _) = New((_, _) =>
        {
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.Found) { Content = new StringContent("") };
            resp.Headers.Location = new Uri("https://evil.example/api/search?q=x");
            return Task.FromResult(resp);
        });
        var r = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.Error, r.Status);
        Assert.All(http.Requests, q => Assert.Equal("lrclib.net", q.RequestUri!.Host));
    }

    [Fact]
    public async Task Response_from_another_host_is_Error()
    {
        // Un handler que siguiera la redirección por su cuenta: la respuesta dice venir de otro host.
        var (client, _, _) = New((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent($"[{Item(200, SyncedA)}]"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/api/search?q=x"),
        }));
        var r = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.Error, r.Status);
    }

    [Fact]
    public async Task Response_over_1MB_is_Error()
    {
        var big = "[" + Item(200, SyncedA) + "," + new string(' ', LrclibClient.MaxResponseBytes) + "]";
        var (client, _, _) = New((_, _) => Json(big));
        var r = await client.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None);
        Assert.Equal(LyricsStatus.Error, r.Status);

        // Una respuesta normal (muy por debajo del tope), sí.
        var (ok, _, _) = New((_, _) => Json($"[{Item(200, SyncedA)}]"));
        Assert.Equal(LyricsStatus.Found, (await ok.FetchAsync("Song", "Artist", TimeSpan.FromSeconds(200), CancellationToken.None)).Status);
    }

    // ---------------- utilidades ----------------

    private static Dictionary<string, string> Q(params (string K, string V)[] kv) => kv.ToDictionary(p => p.K, p => p.V);

    private static Dictionary<string, string> Query(string url)
    {
        var q = new Uri(url).Query.TrimStart('?');
        return q.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;
        public int Calls => Requests.Count;
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request);
            return _respond(request, ct);
        }
    }
}
