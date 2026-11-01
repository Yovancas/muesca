using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Letras v2 · C: <see cref="LyricsController"/> con catálogo y caché (en un directorio temporal, reloj inyectado).
/// Caché primero, letra plana como estado propio, desfase acotado y guardado, «Otra letra» en ciclo con la elección
/// recordada, y las respuestas tardías (de otra pista o con la vista cerrada) siempre descartadas. Nada sale a la red.
/// </summary>
public sealed class LyricsControllerV2Tests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Dur = TimeSpan.FromSeconds(200);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-lyrics-ctl-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = T0;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private LyricsCache Cache() => new(Path.Combine(_dir, "lyrics-cache.json"), LyricsCache.DefaultMax, () => _now);

    private static NowPlaying Track(string title, string artist = "Artist", string app = "Spotify.exe") =>
        new(title, artist, "Album", true, TimeSpan.Zero, Dur, T0, null, AlbumAccent.Default, app);

    private static TrackQuery Query(string title, string artist = "Artist", string app = "Spotify.exe") =>
        new(title, artist, "Album", Dur, app);

    private static LyricsCandidate Synced(string source, string id, string text = "hola") =>
        new(source, id, "t", "a", Dur, true, $"[00:01.00]{text} {source}\n[00:02.00]dos");

    private static LyricsCandidate Plain(string source, string id) =>
        new(source, id, "t", "a", Dur, false, "letra\nplana");

    /// <summary>Catálogo falso: cada petición queda pendiente hasta que el test la resuelve.</summary>
    private sealed class FakeCatalog : ILyricsCatalog
    {
        public readonly List<(TrackQuery Q, TaskCompletionSource<LyricsResult> Tcs, CancellationToken Ct)> Fetches = new();
        public readonly List<(TrackQuery Q, TaskCompletionSource<IReadOnlyList<LyricsCandidate>> Tcs, CancellationToken Ct)> Alls = new();

        public Task<LyricsResult> FetchAsync(TrackQuery q, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<LyricsResult>();
            ct.Register(() => tcs.TrySetCanceled(ct));
            Fetches.Add((q, tcs, ct));
            return tcs.Task;
        }

        public Task<IReadOnlyList<LyricsCandidate>> AllCandidatesAsync(TrackQuery q, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<IReadOnlyList<LyricsCandidate>>();
            ct.Register(() => tcs.TrySetCanceled(ct));
            Alls.Add((q, tcs, ct));
            return tcs.Task;
        }
    }

    private (LyricsController C, FakeCatalog Cat) New(LyricsCache? cache)
    {
        var cat = new FakeCatalog();
        return (new LyricsController(cat, cache, () => _now), cat);
    }

    // ---------------- Consulta y caché ----------------

    [Fact]
    public void The_query_carries_album_duration_and_source_app()
    {
        var (c, cat) = New(null);
        c.Update(Track("Song (Official Video)", "Channel", "chrome.exe"), true);
        Assert.Equal(Query("Song (Official Video)", "Channel", "chrome.exe"), Assert.Single(cat.Fetches).Q);
    }

    [Fact]
    public void Cache_is_checked_before_the_network()
    {
        var cache = Cache();
        cache.Put(LyricsCache.Key(Query("A")), new CachedLyrics("LRCLIB", "1", true, "[00:01.00]de caché", Dur, T0));
        var (c, cat) = New(cache);

        c.Update(Track("A"), true);
        Assert.Empty(cat.Fetches);
        Assert.Equal(LyricsState.Found, c.State);
        Assert.Equal("de caché", Assert.Single(c.Lines).Text);
        Assert.Equal("LRCLIB", c.Source);
    }

    [Fact]
    public void Nothing_is_read_or_asked_while_not_wanted()
    {
        var cache = Cache();
        cache.Put(LyricsCache.Key(Query("A")), new CachedLyrics("LRCLIB", "1", true, "[00:01.00]x", Dur, T0));
        var (c, cat) = New(cache);
        c.Update(Track("A"), false);
        Assert.Equal(LyricsState.Idle, c.State);
        Assert.Empty(cat.Fetches);
    }

    [Fact]
    public void Found_lyrics_are_saved_and_survive_a_restart()
    {
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(Synced("LRCLIB", "7"))!);
        Assert.Equal(LyricsState.Found, c.State);
        Assert.Equal("LRCLIB", c.Source);

        var (again, cat2) = New(Cache());
        again.Update(Track("A"), true);
        Assert.Empty(cat2.Fetches);
        Assert.Equal("LRCLIB", again.Source);
        Assert.Equal(2, again.Lines.Count);
    }

    [Fact]
    public void NotFound_and_Error_are_not_cached()
    {
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.NotFound);
        c.Update(Track("B"), true);
        cat.Fetches[1].Tcs.SetResult(LyricsResult.Error);

        var (again, cat2) = New(Cache());
        again.Update(Track("A"), true);
        Assert.Single(cat2.Fetches);
    }

    // ---------------- Letra plana ----------------

    [Fact]
    public void Plain_lyrics_are_their_own_state_with_text_and_no_lines()
    {
        var cache = Cache();
        var (c, cat) = New(cache);
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(Plain("LRCLIB", "p"))!);

        Assert.Equal(LyricsState.Plain, c.State);
        Assert.Equal("letra\nplana", c.PlainText);
        Assert.Empty(c.Lines);
        Assert.False(c.CanNudge);
        Assert.Equal("LRCLIB", c.Source);

        c.Update(Track("A"), true);   // no se vuelve a pedir
        Assert.Single(cat.Fetches);

        Assert.True(Cache().TryGet(LyricsCache.Key(Query("A")), out var saved));
        Assert.False(saved.Synced);
    }

    [Fact]
    public void Plain_from_an_old_style_source_is_shown_as_plain_too()
    {
        var src = new PlainSource();
        var c = new LyricsController(src);
        c.Update(Track("A"), true);
        Assert.Equal(LyricsState.Plain, c.State);
        Assert.Null(c.Source);   // la fuente de siempre no dice de dónde salió
    }

    private sealed class PlainSource : ILyricsSource
    {
        public Task<LyricsResult> FetchAsync(string title, string artist, TimeSpan duration, CancellationToken ct) =>
            Task.FromResult(LyricsResult.Plain("a\nb"));
    }

    // ---------------- Desfase ----------------

    [Fact]
    public void Offset_moves_in_steps_is_clamped_and_persisted()
    {
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(Synced("LRCLIB", "1"))!);

        int changes = 0;
        c.Changed += () => changes++;
        c.NudgeOffset(LyricsCache.OffsetStep);
        c.NudgeOffset(LyricsCache.OffsetStep);
        Assert.Equal(1.0, c.OffsetSeconds);
        Assert.Equal(2, changes);
        Assert.Equal(TimeSpan.FromSeconds(9), c.LyricsPosition(TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.Zero, c.LyricsPosition(TimeSpan.FromSeconds(0.5)));

        for (int i = 0; i < 40; i++) c.NudgeOffset(LyricsCache.OffsetStep);
        Assert.Equal(LyricsCache.MaxOffsetSeconds, c.OffsetSeconds);
        int before = changes;
        c.NudgeOffset(LyricsCache.OffsetStep);   // ya en el tope: nada cambia
        Assert.Equal(before, changes);

        c.NudgeOffset(-0.5);
        var choice = Cache().Choice(LyricsCache.Key(Query("A")));
        Assert.Equal(new LyricsChoice("LRCLIB", "1", 9.5), choice);

        var (again, cat2) = New(Cache());
        again.Update(Track("A"), true);
        Assert.Empty(cat2.Fetches);
        Assert.Equal(9.5, again.OffsetSeconds);
    }

    [Fact]
    public void Offset_is_per_track_and_only_with_synced_lyrics()
    {
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        c.NudgeOffset(0.5);   // aún cargando
        Assert.Equal(0, c.OffsetSeconds);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(Synced("LRCLIB", "1"))!);
        c.NudgeOffset(0.5);
        Assert.Equal(0.5, c.OffsetSeconds);

        c.Update(Track("B"), true);
        Assert.Equal(0, c.OffsetSeconds);
        cat.Fetches[1].Tcs.SetResult(LyricsResult.FromCandidate(Plain("LRCLIB", "p"))!);
        c.NudgeOffset(0.5);
        Assert.Equal(0, c.OffsetSeconds);
    }

    // ---------------- «Otra letra» ----------------

    [Fact]
    public async Task Next_candidate_asks_once_cycles_and_remembers_the_choice()
    {
        // Las letras de LRCLIB para la misma pista (varias entradas: otra versión, otra sincronización, solo plana).
        var a = Synced("LRCLIB", "a");
        var b = Synced("LRCLIB", "b", "otra");
        var p = Plain("LRCLIB", "p");
        var broken = new LyricsCandidate("X", "roto", "t", "a", Dur, true, "sin marcas");
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(a)!);
        Assert.Equal(0, c.CandidateCount);
        Assert.Equal(-1, c.CandidateIndex);

        var pending = c.NextCandidate();
        Assert.True(c.CandidatesLoading);
        await c.NextCandidate();   // con la lista en camino, otro clic no hace nada
        Assert.Single(cat.Alls);
        Assert.Equal(Query("A"), cat.Alls[0].Q);
        cat.Alls[0].Tcs.SetResult(new[] { a, broken, b, p });
        await pending;

        Assert.False(c.CandidatesLoading);
        Assert.Equal(3, c.CandidateCount);   // la rota no cuenta
        Assert.Equal(1, c.CandidateIndex);
        Assert.Equal("otra LRCLIB", c.Lines[0].Text);
        Assert.Equal(LyricsState.Found, c.State);

        await c.NextCandidate();
        Assert.Equal(LyricsState.Plain, c.State);
        Assert.Equal(2, c.CandidateIndex);
        await c.NextCandidate();
        Assert.Equal("hola LRCLIB", c.Lines[0].Text);
        Assert.Equal(0, c.CandidateIndex);
        await c.NextCandidate();
        Assert.Equal(1, c.CandidateIndex);
        Assert.Single(cat.Alls);   // una sola petición por pista

        // La elección se respeta al volver a la pista y tras reiniciar (sin red: letra y elección en la caché).
        c.Update(Track("B"), true);
        c.Update(Track("A"), true);
        Assert.Equal("otra LRCLIB", c.Lines[0].Text);
        var (again, cat2) = New(Cache());
        again.Update(Track("A"), true);
        Assert.Empty(cat2.Fetches);
        Assert.Equal("otra LRCLIB", again.Lines[0].Text);
        Assert.Equal("LRCLIB", again.Source);
        Assert.Equal(new LyricsChoice("LRCLIB", "b", 0), Cache().Choice(LyricsCache.Key(Query("A"))));
    }

    [Fact]
    public async Task Choosing_another_lyric_resets_the_offset()
    {
        var a = Synced("LRCLIB", "a");
        var b = Synced("LRCLIB", "b");
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(a)!);
        c.NudgeOffset(1.5);
        var pending = c.NextCandidate();
        cat.Alls[0].Tcs.SetResult(new[] { a, b });
        await pending;
        Assert.Equal(0, c.OffsetSeconds);
    }

    [Fact]
    public async Task A_single_candidate_that_is_already_shown_changes_nothing()
    {
        var a = Synced("LRCLIB", "a");
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(a)!);
        var lines = c.Lines;
        var pending = c.NextCandidate();
        cat.Alls[0].Tcs.SetResult(new[] { a });
        await pending;
        Assert.Same(lines, c.Lines);
        Assert.Equal(1, c.CandidateCount);
        Assert.Equal(0, c.CandidateIndex);
    }

    [Fact]
    public async Task Next_candidate_does_nothing_without_lyrics_on_screen()
    {
        var (c, cat) = New(Cache());
        await c.NextCandidate();
        c.Update(Track("A"), true);
        await c.NextCandidate();   // cargando
        cat.Fetches[0].Tcs.SetResult(LyricsResult.NotFound);
        await c.NextCandidate();   // sin letra
        Assert.Empty(cat.Alls);
    }

    [Fact]
    public void Expired_lyrics_with_a_saved_choice_look_for_that_choice()
    {
        var a = Synced("LRCLIB", "a");
        var b = Synced("LRCLIB", "b", "otra");
        var key = LyricsCache.Key(Query("A"));
        var cache = Cache();
        cache.Put(key, new CachedLyrics("LRCLIB", "b", true, b.Body, Dur, T0));
        cache.SetChoice(key, new LyricsChoice("LRCLIB", "b", 2));
        _now = T0 + LyricsCache.MaxAge + TimeSpan.FromDays(1);

        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        Assert.Empty(cat.Fetches);
        cat.Alls[0].Tcs.SetResult(new[] { a, b });
        Assert.Equal(1, c.CandidateIndex);   // la elegida, no la mejor
        Assert.Equal("otra LRCLIB", c.Lines[0].Text);
        Assert.Equal(2, c.OffsetSeconds);
        Assert.Equal(2, c.CandidateCount);   // la lista ya pedida sirve para «Otra letra»
        Assert.True(Cache().TryGet(key, out _));   // y la letra vuelve a la caché
    }

    [Fact]
    public void A_saved_choice_that_no_longer_exists_falls_back_to_the_best()
    {
        var key = LyricsCache.Key(Query("A"));
        Cache().SetChoice(key, new LyricsChoice("LRCLIB", "desaparecida", 0));
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Alls[0].Tcs.SetResult(new[] { Plain("LRCLIB", "p"), Synced("LRCLIB", "s") });
        Assert.Equal(LyricsState.Found, c.State);   // la mejor: sincronizada antes que plana

        // Pasa a ser la elección: la próxima visita sale de la caché, sin red.
        Assert.Equal(new LyricsChoice("LRCLIB", "s", 0), Cache().Choice(key));
        var (again, cat2) = New(Cache());
        again.Update(Track("A"), true);
        Assert.Empty(cat2.Alls);
        Assert.Empty(cat2.Fetches);
        Assert.Equal(LyricsState.Found, again.State);
    }

    [Fact]
    public void A_saved_choice_with_no_candidates_uses_the_normal_search()
    {
        var key = LyricsCache.Key(Query("A"));
        Cache().SetChoice(key, new LyricsChoice("LRCLIB", "x", 0));
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Alls[0].Tcs.SetResult(Array.Empty<LyricsCandidate>());
        cat.Fetches[0].Tcs.SetResult(LyricsResult.Error);
        Assert.Equal(LyricsState.Error, c.State);
    }

    // ---------------- Respuestas tardías ----------------

    [Fact]
    public async Task Late_candidates_from_the_previous_track_are_discarded()
    {
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(Synced("LRCLIB", "a"))!);
        var pending = c.NextCandidate();
        var ctA = cat.Alls[0].Ct;

        c.Update(Track("B"), true);
        Assert.True(ctA.IsCancellationRequested);
        cat.Alls[0].Tcs.TrySetResult(new[] { Synced("LRCLIB", "x") });   // aunque no respete la cancelación
        await pending;

        Assert.Equal(LyricsState.Loading, c.State);
        Assert.Null(c.Source);
        Assert.Equal(0, c.CandidateCount);
        Assert.False(c.CandidatesLoading);
        Assert.Null(Cache().Choice(LyricsCache.Key(Query("A"))));
    }

    [Fact]
    public async Task Hiding_the_view_cancels_the_candidates_in_flight()
    {
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(Synced("LRCLIB", "a"))!);
        var pending = c.NextCandidate();
        c.Update(Track("A"), false);
        Assert.True(cat.Alls[0].Ct.IsCancellationRequested);
        Assert.False(c.CandidatesLoading);
        await pending;
        Assert.Equal(LyricsState.Found, c.State);   // la letra que había se queda
        Assert.Equal("LRCLIB", c.Source);

        c.Update(Track("A"), true);
        var again = c.NextCandidate();   // se puede volver a pedir
        Assert.Equal(2, cat.Alls.Count);
        cat.Alls[1].Tcs.SetResult(Array.Empty<LyricsCandidate>());
        await again;
    }

    [Fact]
    public void Late_fetch_after_a_track_change_is_not_cached_nor_shown()
    {
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        c.Update(Track("B"), true);
        cat.Fetches[0].Tcs.TrySetResult(LyricsResult.FromCandidate(Synced("LRCLIB", "a"))!);
        Assert.Equal(LyricsState.Loading, c.State);
        Assert.False(Cache().TryGet(LyricsCache.Key(Query("A")), out _));
    }

    // ---------------- Duración que llega tarde ----------------

    private static NowPlaying TrackD(string title, TimeSpan duration) =>
        new(title, "Artist", "Album", true, TimeSpan.Zero, duration, T0, null, AlbumAccent.Default, "Spotify.exe");

    private static TrackQuery QueryD(string title, TimeSpan duration) => new(title, "Artist", "Album", duration, "Spotify.exe");

    [Fact]
    public void Duration_arriving_while_loading_restarts_with_the_right_key()
    {
        var cache = Cache();
        cache.Put(LyricsCache.Key(Query("A")), new CachedLyrics("LRCLIB", "1", true, "[00:01.00]de caché", Dur, T0));
        cache.SetChoice(LyricsCache.Key(Query("A")), new LyricsChoice("LRCLIB", "1", 1.5));
        var (c, cat) = New(cache);

        c.Update(TrackD("A", TimeSpan.Zero), true);   // primera publicación: aún sin duración
        Assert.Single(cat.Fetches);
        c.Update(Track("A"), true);                   // llega la duración: caché con la clave buena
        Assert.True(cat.Fetches[0].Ct.IsCancellationRequested);
        Assert.Single(cat.Fetches);
        Assert.Equal(LyricsState.Found, c.State);
        Assert.Equal(1.5, c.OffsetSeconds);
    }

    [Fact]
    public void Duration_arriving_while_loading_asks_again_with_it_only_once()
    {
        var (c, cat) = New(Cache());
        var other = Dur + TimeSpan.FromSeconds(10);
        c.Update(TrackD("A", TimeSpan.FromSeconds(90)), true);   // la duración de la pista anterior
        c.Update(Track("A"), true);
        Assert.Equal(2, cat.Fetches.Count);
        Assert.Equal(Dur, cat.Fetches[1].Q.Duration);

        c.Update(TrackD("A", other), true);   // otro cambio con la petición en vuelo: no se vuelve a pedir
        Assert.Equal(2, cat.Fetches.Count);
        cat.Fetches[1].Tcs.SetResult(LyricsResult.FromCandidate(Synced("LRCLIB", "1"))!);
        Assert.True(Cache().TryGet(LyricsCache.Key(QueryD("A", other)), out _));   // con la última duración
    }

    [Fact]
    public void Duration_changing_after_the_lyrics_moves_lyrics_and_offset_to_the_new_key()
    {
        var (c, cat) = New(Cache());
        c.Update(TrackD("A", TimeSpan.Zero), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(Synced("LRCLIB", "1"))!);
        c.NudgeOffset(LyricsCache.OffsetStep);

        c.Update(Track("A"), true);
        Assert.Single(cat.Fetches);   // sin red
        Assert.Equal(0.5, c.OffsetSeconds);
        c.NudgeOffset(LyricsCache.OffsetStep);

        var (again, cat2) = New(Cache());
        again.Update(Track("A"), true);
        Assert.Empty(cat2.Fetches);
        Assert.Equal(1.0, again.OffsetSeconds);
    }

    // (Lo guardado de NetEase y Musixmatch, fuentes retiradas: LyricsRetiredSourcesTests.)

    // ---------------- «Otra letra» que falla ----------------

    [Fact]
    public async Task Failed_or_empty_candidates_are_asked_again_on_the_next_click()
    {
        var (c, cat) = New(Cache());
        c.Update(Track("A"), true);
        cat.Fetches[0].Tcs.SetResult(LyricsResult.FromCandidate(Synced("LRCLIB", "a"))!);

        var first = c.NextCandidate();
        cat.Alls[0].Tcs.SetException(new HttpRequestException("caída"));
        await first;
        Assert.False(c.CandidatesLoading);
        Assert.Equal(0, c.CandidateCount);
        Assert.Equal("LRCLIB", c.Source);

        var second = c.NextCandidate();
        Assert.Equal(2, cat.Alls.Count);   // se reintenta
        cat.Alls[1].Tcs.SetResult(Array.Empty<LyricsCandidate>());
        await second;

        var third = c.NextCandidate();
        Assert.Equal(3, cat.Alls.Count);   // vacía también: se puede volver a probar
        cat.Alls[2].Tcs.SetResult(new[] { Synced("LRCLIB", "a"), Synced("LRCLIB", "b", "otra") });
        await third;
        Assert.Equal(1, c.CandidateIndex);
        Assert.Equal("otra LRCLIB", c.Lines[0].Text);
        Assert.Equal(2, c.CandidateCount);
    }
}
