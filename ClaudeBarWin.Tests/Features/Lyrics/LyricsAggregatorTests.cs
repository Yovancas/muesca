using System.Text.Json;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="LyricsAggregator"/> (letras v2 · B1) con fuentes falsas: consultas en paralelo con timeout por fuente,
/// candidatos por rank hasta dar con algo sincronizado, ranking de <see cref="LyricsAggregator.Best"/>, letra plana como
/// respaldo, NotFound y Error; y con solo LRCLIB (como en la app) sobre un HTTP falso. Nada sale a la red.
/// </summary>
public class LyricsAggregatorTests
{
    private const string Lrc = "[00:01.00]uno\n[00:02.00]dos";
    private static readonly TimeSpan Dur = TimeSpan.FromSeconds(200);
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(300);

    private static LyricsCandidate Synced(string source, double seconds, string id = "1", string body = Lrc) =>
        new(source, id, "t", "a", TimeSpan.FromSeconds(seconds), true, body);

    private static LyricsCandidate Plain(string source, double seconds, string id = "p", string body = "línea uno\nlínea dos") =>
        new(source, id, "t", "a", TimeSpan.FromSeconds(seconds), false, body);

    /// <summary>Fuente falsa: responde con lo que diga <c>respond</c> para cada candidato de búsqueda.</summary>
    private sealed class FakeProvider : ILyricsProvider
    {
        private readonly Func<SearchCandidate, CancellationToken, Task<IReadOnlyList<LyricsCandidate>>> _respond;
        public FakeProvider(string name, Func<SearchCandidate, CancellationToken, Task<IReadOnlyList<LyricsCandidate>>> respond)
        {
            Name = name;
            _respond = respond;
        }

        public string Name { get; }
        public List<SearchCandidate> Queries { get; } = new();

        public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(SearchCandidate q, TimeSpan duration, CancellationToken ct)
        {
            lock (Queries) Queries.Add(q);
            return _respond(q, ct);
        }

        public static FakeProvider Returning(string name, params LyricsCandidate[] results) =>
            new(name, (_, _) => Task.FromResult<IReadOnlyList<LyricsCandidate>>(results));

        public static FakeProvider Failing(string name) =>
            new(name, (_, _) => Task.FromException<IReadOnlyList<LyricsCandidate>>(new LyricsProviderException("caída")));
    }

    private static Func<TrackQuery, IReadOnlyList<SearchCandidate>> OneCandidate =>
        q => new[] { new SearchCandidate(q.Title, q.Artist, 0) };

    private static LyricsAggregator New(params ILyricsProvider[] providers) => new(providers, OneCandidate, Fast);

    // ---------------- Best ----------------

    [Fact]
    public void Best_prefers_synced_within_3s_over_closer_plain_and_far_synced()
    {
        var all = new[] { Plain("A", 200), Synced("B", 230, "lejos"), Synced("C", 202.5, "cerca") };
        Assert.Equal("cerca", LyricsAggregator.Best(all, Dur)!.ExternalId);
    }

    [Fact]
    public void Best_without_synced_in_window_takes_closest_synced_then_plain()
    {
        var all = new[] { Plain("A", 200), Synced("B", 240, "40s"), Synced("C", 215, "15s") };
        Assert.Equal("15s", LyricsAggregator.Best(all, Dur)!.ExternalId);

        var plains = new[] { Plain("A", 260, "60s"), Plain("B", 199, "1s") };
        Assert.Equal("1s", LyricsAggregator.Best(plains, Dur)!.ExternalId);
    }

    [Fact]
    public void Best_tie_goes_to_list_order()
    {
        var all = new[] { Synced("LRCLIB", 201, "primero"), Synced("LRCLIB", 199, "segundo") };
        Assert.Equal("primero", LyricsAggregator.Best(all, Dur)!.ExternalId);
    }

    [Fact]
    public void Best_within_window_takes_the_closest()
    {
        var all = new[] { Synced("LRCLIB", 202.5, "lejos"), Synced("LRCLIB", 200.5, "cerca"), Plain("LRCLIB", 200, "plana") };
        Assert.Equal("cerca", LyricsAggregator.Best(all, Dur)!.ExternalId);

        // Fuera de la ventana, la más cercana; pero dentro gana aunque llegue después.
        var far = new[] { Synced("LRCLIB", 210, "10s"), Synced("LRCLIB", 204, "4s"), Synced("LRCLIB", 202, "2s") };
        Assert.Equal("2s", LyricsAggregator.Best(far, Dur)!.ExternalId);
    }

    [Fact]
    public void Best_with_unknown_duration_keeps_order_and_still_prefers_synced()
    {
        var all = new[] { Plain("A", 100), Synced("B", 300, "s1"), Synced("C", 10, "s2") };
        Assert.Equal("s1", LyricsAggregator.Best(all, TimeSpan.Zero)!.ExternalId);
    }

    [Fact]
    public void Best_ignores_empty_bodies_and_returns_null_when_nothing()
    {
        Assert.Null(LyricsAggregator.Best(Array.Empty<LyricsCandidate>(), Dur));
        Assert.Null(LyricsAggregator.Best(new[] { Synced("A", 200, body: "  "), Plain("B", 200, body: "") }, Dur));
    }

    [Fact]
    public void Best_candidate_without_duration_ranks_after_ones_with_duration()
    {
        var all = new[] { Synced("A", 0, "sin"), Synced("B", 260, "con") };
        Assert.Equal("con", LyricsAggregator.Best(all, Dur)!.ExternalId);
    }

    // ---------------- FetchAsync / AllCandidatesAsync ----------------

    [Fact]
    public async Task Slow_provider_times_out_and_the_other_one_wins()
    {
        var slow = new FakeProvider("Lenta", async (_, _) =>
        {
            await Task.Delay(Timeout.Infinite, CancellationToken.None);   // ni siquiera respeta la cancelación
            return Array.Empty<LyricsCandidate>();
        });
        var ok = FakeProvider.Returning("Buena", Synced("Buena", 200));
        var agg = New(slow, ok);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await agg.FetchAsync("Song", "Artist", Dur, CancellationToken.None);
        sw.Stop();

        Assert.Equal(LyricsStatus.Found, r.Status);
        Assert.True(r.Synced);
        Assert.Equal(new[] { "uno", "dos" }, r.Lines.Select(l => l.Text));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"tardó {sw.Elapsed}");
    }

    [Fact]
    public async Task Providers_are_queried_in_parallel()
    {
        var started = 0;
        var gate = new TaskCompletionSource();
        FakeProvider Waiting(string name) => new(name, async (_, _) =>
        {
            if (Interlocked.Increment(ref started) == 2) gate.TrySetResult();
            await gate.Task;   // solo se libera si las dos están en vuelo a la vez
            return new[] { Synced(name, 200, name) };
        });

        var agg = new LyricsAggregator(new[] { Waiting("A"), Waiting("B") }, OneCandidate, TimeSpan.FromSeconds(5));
        var all = await agg.AllCandidatesAsync(new TrackQuery("Song", "Artist", null, Dur, "app"), CancellationToken.None);

        Assert.Equal(new[] { "A", "B" }, all.Select(c => c.Source));   // orden de las fuentes, no de llegada
    }

    [Fact]
    public async Task Failing_provider_does_not_take_down_the_others()
    {
        var agg = New(FakeProvider.Failing("Rota"), FakeProvider.Returning("Buena", Synced("Buena", 201)));
        var r = await agg.FetchAsync("Song", "Artist", Dur, CancellationToken.None);
        Assert.Equal(LyricsStatus.Found, r.Status);
    }

    [Fact]
    public async Task Walks_candidates_by_rank_until_something_synced()
    {
        var provider = new FakeProvider("A", (q, _) => Task.FromResult<IReadOnlyList<LyricsCandidate>>(q.Rank switch
        {
            0 => Array.Empty<LyricsCandidate>(),
            1 => new[] { Plain("A", 200, "plana") },
            2 => new[] { Synced("A", 200, "sinc") },
            _ => new[] { Synced("A", 200, "nunca") },
        }));
        // Desordenados a propósito: se recorren por rank.
        IReadOnlyList<SearchCandidate> Cands(TrackQuery q) => new[]
        {
            new SearchCandidate("c3", "x", 3), new SearchCandidate("c1", "x", 1),
            new SearchCandidate("c0", "x", 0), new SearchCandidate("c2", "x", 2),
        };
        var agg = new LyricsAggregator(new[] { provider }, Cands, Fast);

        var all = await agg.AllCandidatesAsync(new TrackQuery("t", "a", null, Dur, "app"), CancellationToken.None);

        Assert.Equal(new[] { "c0", "c1", "c2" }, provider.Queries.Select(q => q.Title));
        Assert.Equal(new[] { "plana", "sinc" }, all.Select(c => c.ExternalId));
    }

    [Fact]
    public async Task Rejected_lyrics_are_dropped_and_do_not_stop_the_walk()
    {
        // Con el título del vídeo, la búsqueda libre de LRCLIB da otra canción del mismo artista: una sincronizada de OTRA
        // canción no debe cortar la búsqueda ni ganar.
        var other = new LyricsCandidate("LRCLIB", "otra", "Vida de Rico", "Camilo", TimeSpan.FromSeconds(200), true, Lrc);
        var right = new LyricsCandidate("LRCLIB", "buena", "Índigo", "Camilo", TimeSpan.FromSeconds(210), true, Lrc);
        var lrc = new FakeProvider("LRCLIB", (q, _) => Task.FromResult<IReadOnlyList<LyricsCandidate>>(
            q.Rank == 1 ? new[] { right } : new[] { other }));
        IReadOnlyList<SearchCandidate> Cands(TrackQuery q) => new[]
        {
            new SearchCandidate("Camilo, Evaluna Montaner - Índigo", "Camilo", 0), new SearchCandidate("Índigo", "Camilo", 1),
        };
        var agg = new LyricsAggregator(new ILyricsProvider[] { lrc }, Cands, Fast, LyricsMatch.IsMatch);
        var q = new TrackQuery("Camilo, Evaluna Montaner - Índigo (Official Video)", "Camilo", null, Dur, "brave.exe");

        Assert.Equal(new[] { "buena" }, (await agg.AllCandidatesAsync(q, CancellationToken.None)).Select(c => c.ExternalId));
        Assert.Equal(2, lrc.Queries.Count);
        var r = await agg.FetchAsync(q, CancellationToken.None);
        Assert.Equal("buena", r.From!.ExternalId);
    }

    [Fact]
    public async Task Only_rejected_lyrics_is_NotFound_not_Error()
    {
        var other = new LyricsCandidate("LRCLIB", "otra", "演员", "薛之谦", Dur, true, Lrc);
        var agg = new LyricsAggregator(new[] { FakeProvider.Returning("LRCLIB", other) }, OneCandidate, Fast, LyricsMatch.IsMatch);

        var r = await agg.FetchAsync("Gasolina", "Daddy Yankee", Dur, CancellationToken.None);

        Assert.Equal(LyricsStatus.NotFound, r.Status);
    }

    [Fact]
    public async Task Without_a_filter_everything_is_kept()
    {
        var other = new LyricsCandidate("LRCLIB", "otra", "演员", "薛之谦", Dur, true, Lrc);
        var agg = New(FakeProvider.Returning("LRCLIB", other));

        Assert.Single(await agg.AllCandidatesAsync(new TrackQuery("Gasolina", "Daddy Yankee", null, Dur, ""), CancellationToken.None));
    }

    [Fact]
    public async Task Duplicates_across_candidates_are_dropped()
    {
        var provider = FakeProvider.Returning("A", Plain("A", 200, "mismo"));
        IReadOnlyList<SearchCandidate> Cands(TrackQuery q) => new[] { new SearchCandidate("x", "y", 0), new SearchCandidate("x2", "y", 1) };
        var agg = new LyricsAggregator(new[] { provider }, Cands, Fast);

        var all = await agg.AllCandidatesAsync(new TrackQuery("t", "a", null, Dur, "app"), CancellationToken.None);

        Assert.Equal(2, provider.Queries.Count);
        Assert.Single(all);
    }

    [Fact]
    public async Task Plain_lyrics_are_the_fallback()
    {
        var agg = New(FakeProvider.Returning("A", Plain("A", 200, body: "hola\r\n\r\nadiós\n")));
        var r = await agg.FetchAsync("Song", "Artist", Dur, CancellationToken.None);

        Assert.Equal(LyricsStatus.Found, r.Status);
        Assert.False(r.Synced);
        Assert.Equal("hola\n\nadiós", r.PlainText);
        Assert.Empty(r.Lines);   // el controlador de hoy la trata como «Sin letra»: nada cambia hasta la Parte C
    }

    [Fact]
    public async Task Synced_body_that_does_not_parse_falls_back_to_next_best()
    {
        var agg = New(FakeProvider.Returning("A", Synced("A", 200, "roto", body: "sin marcas"), Plain("A", 200, "plana", body: "texto")));
        var r = await agg.FetchAsync("Song", "Artist", Dur, CancellationToken.None);

        Assert.False(r.Synced);
        Assert.Equal("texto", r.PlainText);
    }

    [Fact]
    public async Task Nothing_found_is_NotFound()
    {
        var agg = New(FakeProvider.Returning("A"), FakeProvider.Failing("B"));
        var r = await agg.FetchAsync("Song", "Artist", Dur, CancellationToken.None);
        Assert.Equal(LyricsStatus.NotFound, r.Status);
    }

    [Fact]
    public async Task No_search_candidates_is_NotFound_without_queries()
    {
        var p = FakeProvider.Returning("A", Synced("A", 200));
        var agg = new LyricsAggregator(new[] { p }, _ => Array.Empty<SearchCandidate>(), Fast);
        var r = await agg.FetchAsync("", "", Dur, CancellationToken.None);

        Assert.Equal(LyricsStatus.NotFound, r.Status);
        Assert.Empty(p.Queries);
    }

    [Fact]
    public async Task All_providers_failing_is_Error()
    {
        var slow = new FakeProvider("Lenta", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Array.Empty<LyricsCandidate>();
        });
        var agg = New(FakeProvider.Failing("A"), slow, new FakeProvider("C", (_, _) => throw new HttpRequestException("x")));
        var r = await agg.FetchAsync("Song", "Artist", Dur, CancellationToken.None);
        Assert.Equal(LyricsStatus.Error, r.Status);
    }

    [Fact]
    public async Task No_providers_is_NotFound()
    {
        var agg = New();
        var r = await agg.FetchAsync("Song", "Artist", Dur, CancellationToken.None);
        Assert.Equal(LyricsStatus.NotFound, r.Status);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var never = new FakeProvider("A", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Array.Empty<LyricsCandidate>();
        });
        var agg = new LyricsAggregator(new[] { never }, OneCandidate, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agg.FetchAsync("Song", "Artist", Dur, cts.Token));
    }

    [Fact]
    public async Task FetchAsync_builds_the_track_query_for_the_candidates()
    {
        TrackQuery? seen = null;
        var agg = new LyricsAggregator(new[] { FakeProvider.Returning("A") }, q => { seen = q; return Array.Empty<SearchCandidate>(); }, Fast);
        await agg.FetchAsync("Song", "Artist", Dur, CancellationToken.None);
        Assert.Equal(new TrackQuery("Song", "Artist", null, Dur, ""), seen);
    }

    [Fact]
    public void Is_a_lyrics_source_and_validates_arguments()
    {
        Assert.True(typeof(ILyricsSource).IsAssignableFrom(typeof(LyricsAggregator)));
        Assert.Throws<ArgumentNullException>(() => new LyricsAggregator(null!, OneCandidate, Fast));
        Assert.Throws<ArgumentNullException>(() => new LyricsAggregator(Array.Empty<ILyricsProvider>(), null!, Fast));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricsAggregator(Array.Empty<ILyricsProvider>(), OneCandidate, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(6), LyricsAggregator.DefaultPerProviderTimeout);
    }

    // ---------------- Solo LRCLIB (como en la app) ----------------

    [Fact]
    public async Task With_only_LRCLIB_the_best_goes_by_duration_and_the_rest_reach_other_lyrics()
    {
        // Lo que LRCLIB devuelve para una pista: la canción (dos sincronizaciones y una plana en directo) y otra del mismo
        // artista. Con la fuente real sobre un HTTP falso y el filtro de la app.
        static string Entry(int id, string track, double duration, string? synced, string? plain) => JsonSerializer.Serialize(new
        {
            id, trackName = track, artistName = "Camilo", albumName = "x", duration, instrumental = false,
            plainLyrics = plain, syncedLyrics = synced,
        });
        var http = new FakeHttp(_ => FakeHttp.Json("[" + string.Join(",",
            Entry(1, "Índigo", 215, "[00:01.00]lejos", null),
            Entry(2, "Vida de Rico", 200, "[00:01.00]otra canción", null),
            Entry(3, "Índigo", 201, "[00:01.00]cerca", null),
            Entry(4, "Índigo (Live)", 200, null, "plana")) + "]"));
        using var lrclib = new LrclibProvider(http);
        var agg = new LyricsAggregator(new ILyricsProvider[] { lrclib }, TrackNameParser.Candidates, TimeSpan.FromSeconds(5),
            LyricsMatch.IsMatch);
        var q = new TrackQuery("Índigo", "Camilo", null, Dur, "Spotify.exe");

        var all = await agg.AllCandidatesAsync(q, CancellationToken.None);
        Assert.Equal(new[] { "1", "3", "4" }, all.Select(c => c.ExternalId));   // la de otra canción, fuera
        Assert.All(all, c => Assert.Equal(LrclibProvider.SourceName, c.Source));

        var r = await agg.FetchAsync(q, CancellationToken.None);
        Assert.Equal("3", r.From!.ExternalId);   // sincronizada a 1 s de la pista
        Assert.Equal("cerca", Assert.Single(r.Lines).Text);
    }

    // ---------------- LyricsResult ampliado ----------------

    [Fact]
    public void LyricsResult_defaults_keep_old_behaviour()
    {
        var r = new LyricsResult(LyricsStatus.Found, new[] { new LyricLine(TimeSpan.Zero, "x") });
        Assert.True(r.Synced);
        Assert.Null(r.PlainText);
        Assert.True(LyricsResult.NotFound.Synced);

        var p = LyricsResult.Plain("a\nb");
        Assert.Equal(LyricsStatus.Found, p.Status);
        Assert.False(p.Synced);
        Assert.Equal("a\nb", p.PlainText);
        Assert.Empty(p.Lines);
    }

    // ---------------- FetchAsync(TrackQuery) y origen de la letra ----------------

    [Fact]
    public async Task FetchAsync_with_track_query_passes_the_source_app_to_the_candidates()
    {
        TrackQuery? seen = null;
        var agg = new LyricsAggregator(new[] { FakeProvider.Returning("A") }, q => { seen = q; return Array.Empty<SearchCandidate>(); }, Fast);
        var q = new TrackQuery("Song", "Channel", "Album", Dur, "chrome.exe");
        await agg.FetchAsync(q, CancellationToken.None);
        Assert.Equal(q, seen);
        Assert.True(typeof(ILyricsCatalog).IsAssignableFrom(typeof(LyricsAggregator)));
    }

    [Fact]
    public async Task Found_result_carries_the_candidate_it_came_from()
    {
        var synced = Synced("LRCLIB", 200, "s1");
        var r = await New(FakeProvider.Returning("LRCLIB", synced)).FetchAsync("Song", "Artist", Dur, CancellationToken.None);
        Assert.Equal(synced, r.From);

        var plain = Plain("LRCLIB", 200, "p1");
        var rp = await New(FakeProvider.Returning("LRCLIB", plain)).FetchAsync("Song", "Artist", Dur, CancellationToken.None);
        Assert.False(rp.Synced);
        Assert.Equal(plain, rp.From);

        Assert.Null(LyricsResult.NotFound.From);
    }

    [Fact]
    public void FromCandidate_parses_synced_and_keeps_plain_text()
    {
        var s = LyricsResult.FromCandidate(Synced("A", 200));
        Assert.Equal(LyricsStatus.Found, s!.Status);
        Assert.Equal(2, s.Lines.Count);
        Assert.NotNull(s.From);

        var p = LyricsResult.FromCandidate(Plain("B", 200, body: "x\r\ny"));
        Assert.False(p!.Synced);
        Assert.Equal("x\ny", p.PlainText);

        Assert.Null(LyricsResult.FromCandidate(Synced("C", 200, body: "sin marcas")));
        Assert.Null(LyricsResult.FromCandidate(Plain("D", 200, body: "  ")));
    }

    [Fact]
    public void Describe_says_why_a_source_failed_without_the_url()
    {
        Assert.Equal("LyricsProviderException: LRCLIB respondió HTTP 503",
            LyricsAggregator.Describe(new LyricsProviderException("LRCLIB respondió HTTP 503")));
        var net = new LyricsProviderException("fallo de red o respuesta demasiado grande",
            new HttpRequestException(HttpRequestError.NameResolutionError, "https://lrclib.net/api/search?track_name=Secreto"));
        string text = LyricsAggregator.Describe(net);
        Assert.Contains("NameResolutionError", text);
        Assert.DoesNotContain("Secreto", text);   // el mensaje de red (con la URL) no se copia
        Assert.Equal("TimeoutException", LyricsAggregator.Describe(new TimeoutException("Secreto")));
    }
}