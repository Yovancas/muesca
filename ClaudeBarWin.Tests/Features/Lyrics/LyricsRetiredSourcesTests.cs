using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// NetEase y Musixmatch se retiraron el 29-oct: lo que una versión anterior dejó en <c>lyrics-cache.json</c> (letras y
/// elecciones de «Otra letra» de esas fuentes) cuenta como si no hubiera nada. No se enseña, la letra se vuelve a pedir
/// a LRCLIB y lo nuevo sustituye a lo viejo; lo de LRCLIB sigue saliendo de la caché sin red, y «Otra letra» cicla entre
/// los candidatos de LRCLIB. Con el filtro de la app (<see cref="LazyLyricsCatalog.Supports"/>), caché en un directorio
/// temporal y catálogo falso: nada sale a la red.
/// </summary>
public sealed class LyricsRetiredSourcesTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Dur = TimeSpan.FromSeconds(200);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-lyrics-retired-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string CachePath => Path.Combine(_dir, "lyrics-cache.json");

    private LyricsCache Cache() => new(CachePath, LyricsCache.DefaultMax, () => T0);

    private static NowPlaying Track(string title) =>
        new(title, "Artist", "Album", true, TimeSpan.Zero, Dur, T0, null, AlbumAccent.Default, "Spotify.exe");

    private static string Key(string title) => LyricsCache.Key(new TrackQuery(title, "Artist", "Album", Dur, "Spotify.exe"));

    private static LyricsCandidate Synced(string id, string text) =>
        new(LrclibProvider.SourceName, id, "t", "a", Dur, true, $"[00:01.00]{text}\n[00:02.00]dos");

    /// <summary>Catálogo falso: cada petición queda pendiente hasta que el test la resuelve.</summary>
    private sealed class FakeCatalog : ILyricsCatalog
    {
        public readonly List<TaskCompletionSource<LyricsResult>> Fetches = new();
        public readonly List<TaskCompletionSource<IReadOnlyList<LyricsCandidate>>> Alls = new();

        public Task<LyricsResult> FetchAsync(TrackQuery q, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<LyricsResult>();
            ct.Register(() => tcs.TrySetCanceled(ct));
            Fetches.Add(tcs);
            return tcs.Task;
        }

        public Task<IReadOnlyList<LyricsCandidate>> AllCandidatesAsync(TrackQuery q, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<IReadOnlyList<LyricsCandidate>>();
            ct.Register(() => tcs.TrySetCanceled(ct));
            Alls.Add(tcs);
            return tcs.Task;
        }
    }

    /// <summary>El controlador como lo crea la ventana del notch: con la caché del disco y el filtro de la app.</summary>
    private (LyricsController C, FakeCatalog Cat) App()
    {
        var cat = new FakeCatalog();
        return (new LyricsController(cat, Cache(), LazyLyricsCatalog.Supports), cat);
    }

    /// <summary><c>lyrics-cache.json</c> como lo dejaba la versión con NetEase y Musixmatch (mismo formato: la fuente es
    /// un texto más).</summary>
    private void WriteOldCache(Action<LyricsCache> fill)
    {
        Directory.CreateDirectory(_dir);
        using var old = Cache();
        fill(old);
        Assert.True(File.Exists(CachePath));
    }

    [Theory]
    [InlineData("NetEase")]
    [InlineData("Musixmatch")]
    public void Cached_lyrics_of_a_retired_source_are_not_shown_and_LRCLIB_is_asked(string retired)
    {
        WriteOldCache(c => c.Put(Key("A"), new CachedLyrics(retired, "7", true, $"[00:01.00]de {retired}", Dur, T0)));
        var (c, cat) = App();

        c.Update(Track("A"), true);
        Assert.Single(cat.Fetches);   // a la red (LRCLIB), no a la caché
        Assert.Equal(LyricsState.Loading, c.State);
        Assert.Empty(c.Lines);
        Assert.Null(c.Source);

        cat.Fetches[0].SetResult(LyricsResult.FromCandidate(Synced("1", "de LRCLIB"))!);
        Assert.Equal(LrclibProvider.SourceName, c.Source);
        Assert.Equal("de LRCLIB", c.Lines[0].Text);

        // Lo nuevo sustituye a lo viejo: tras reiniciar sale de la caché, sin red.
        Assert.True(Cache().TryGet(Key("A"), out var saved));
        Assert.Equal(LrclibProvider.SourceName, saved.Source);
        var (again, cat2) = App();
        again.Update(Track("A"), true);
        Assert.Empty(cat2.Fetches);
        Assert.Equal("de LRCLIB", again.Lines[0].Text);
    }

    [Fact]
    public void A_retired_source_with_nothing_on_LRCLIB_shows_nothing()
    {
        WriteOldCache(c => c.Put(Key("A"), new CachedLyrics("Musixmatch", "9", false, "letra plana de Musixmatch", Dur, T0)));
        var (c, cat) = App();

        c.Update(Track("A"), true);
        cat.Fetches[0].SetResult(LyricsResult.NotFound);
        Assert.Equal(LyricsState.NotFound, c.State);
        Assert.Null(c.PlainText);
        Assert.Null(c.Source);
    }

    [Fact]
    public void A_choice_of_a_retired_source_is_ignored_with_its_offset()
    {
        WriteOldCache(c =>
        {
            c.Put(Key("A"), new CachedLyrics(LrclibProvider.SourceName, "1", true, "[00:01.00]de LRCLIB", Dur, T0));
            c.SetChoice(Key("A"), new LyricsChoice("Musixmatch", "9", 2));
        });
        var (c, cat) = App();

        c.Update(Track("A"), true);
        Assert.Empty(cat.Fetches);   // la de LRCLIB sigue saliendo de la caché
        Assert.Empty(cat.Alls);      // sin buscar la elección retirada entre los candidatos
        Assert.Equal(LrclibProvider.SourceName, c.Source);
        Assert.Equal("de LRCLIB", c.Lines[0].Text);
        Assert.Equal(0, c.OffsetSeconds);   // el desfase era de la otra letra
    }

    [Fact]
    public void Retired_lyrics_and_choice_together_go_to_LRCLIB_like_nothing_was_saved()
    {
        WriteOldCache(c =>
        {
            c.Put(Key("A"), new CachedLyrics("NetEase", "b", true, "[00:01.00]de NetEase", Dur, T0));
            c.SetChoice(Key("A"), new LyricsChoice("NetEase", "b", 1.5));
        });
        var (c, cat) = App();

        c.Update(Track("A"), true);
        Assert.Empty(cat.Alls);       // búsqueda normal, no la de una elección
        Assert.Single(cat.Fetches);
        Assert.Equal(0, c.OffsetSeconds);
        cat.Fetches[0].SetResult(LyricsResult.FromCandidate(Synced("1", "de LRCLIB"))!);
        Assert.Equal("de LRCLIB", c.Lines[0].Text);
        Assert.Equal(0, c.OffsetSeconds);
    }

    [Fact]
    public async Task Other_lyrics_cycles_through_the_LRCLIB_candidates_and_replaces_the_retired_choice()
    {
        WriteOldCache(c => c.SetChoice(Key("A"), new LyricsChoice("Musixmatch", "9", 2)));
        var (c, cat) = App();
        c.Update(Track("A"), true);
        var first = Synced("1", "primera");
        cat.Fetches[0].SetResult(LyricsResult.FromCandidate(first)!);

        var pending = c.NextCandidate();
        cat.Alls[0].SetResult(new[] { first, Synced("2", "segunda"), Synced("3", "tercera") });
        await pending;
        Assert.Equal(3, c.CandidateCount);
        Assert.Equal("segunda", c.Lines[0].Text);
        await c.NextCandidate();
        Assert.Equal("tercera", c.Lines[0].Text);
        await c.NextCandidate();
        Assert.Equal("primera", c.Lines[0].Text);
        await c.NextCandidate();
        Assert.Equal("segunda", c.Lines[0].Text);
        Assert.Single(cat.Alls);

        Assert.Equal(new LyricsChoice(LrclibProvider.SourceName, "2", 0), Cache().Choice(Key("A")));
        var (again, cat2) = App();
        again.Update(Track("A"), true);
        Assert.Empty(cat2.Fetches);
        Assert.Equal("segunda", again.Lines[0].Text);
    }
}
