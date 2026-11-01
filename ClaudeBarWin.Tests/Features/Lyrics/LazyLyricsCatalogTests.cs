using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Letras v2 · C: <see cref="LazyLyricsCatalog"/> (solo LRCLIB desde que se retiraron NetEase y Musixmatch): la fuente se
/// crea al primer uso, las letras de otra canción se filtran, y liberado no pregunta a nadie. Con dobles, sin red.
/// </summary>
public sealed class LazyLyricsCatalogTests
{
    private static readonly TrackQuery Q = new("Song", "Artist", null, TimeSpan.FromSeconds(200), "Spotify.exe");

    /// <summary>Fuente falsa: devuelve lo que se le dé; anota las búsquedas y si la liberaron.</summary>
    private sealed class FakeProvider : ILyricsProvider, IDisposable
    {
        private readonly LyricsCandidate[] _all;
        public FakeProvider(params LyricsCandidate[] all) => _all = all;
        public string Name => LrclibProvider.SourceName;
        public bool Disposed { get; private set; }
        public int Searches { get; private set; }

        public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(SearchCandidate q, TimeSpan duration, CancellationToken ct)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>(_all);
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void The_only_real_source_is_LRCLIB()
    {
        var providers = LazyLyricsCatalog.CreateProviders();   // crearla no sale a la red
        try
        {
            Assert.Equal(new[] { LrclibProvider.SourceName }, providers.Select(p => p.Name));
        }
        finally
        {
            foreach (var p in providers) (p as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData("LRCLIB", true)]
    [InlineData("NetEase", false)]
    [InlineData("Musixmatch", false)]
    [InlineData("lrclib", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_what_LRCLIB_saved_is_supported(string? source, bool expected) =>
        Assert.Equal(expected, LazyLyricsCatalog.Supports(source));

    [Fact]
    public async Task The_source_is_created_on_first_use_only()
    {
        int created = 0;
        var fake = new FakeProvider();
        using var cat = new LazyLyricsCatalog(() => { created++; return new ILyricsProvider[] { fake }; });
        Assert.Equal(0, created);

        await cat.FetchAsync(Q, CancellationToken.None);
        await cat.AllCandidatesAsync(Q, CancellationToken.None);
        Assert.Equal(1, created);
        Assert.True(fake.Searches > 0);
    }

    [Fact]
    public async Task Lyrics_of_another_song_are_filtered_out_and_the_rest_reach_other_lyrics()
    {
        // LRCLIB devuelve varias entradas: la de otra canción se descarta; las de esta llegan a «Otra letra».
        var other = new LyricsCandidate("LRCLIB", "otra", "Seven Nation Army", "The White Stripes", TimeSpan.FromSeconds(200), true, "[00:01.00]x");
        var right = new LyricsCandidate("LRCLIB", "buena", "Song", "Artist", TimeSpan.FromSeconds(200), true, "[00:01.00]y");
        var live = new LyricsCandidate("LRCLIB", "directo", "Song (Live)", "Artist", TimeSpan.FromSeconds(230), true, "[00:01.00]z");
        using var cat = new LazyLyricsCatalog(() => new ILyricsProvider[] { new FakeProvider(other, right, live) });

        Assert.Equal(new[] { "buena", "directo" }, (await cat.AllCandidatesAsync(Q, CancellationToken.None)).Select(c => c.ExternalId));
        Assert.Equal("buena", (await cat.FetchAsync(Q, CancellationToken.None)).From!.ExternalId);   // la de la duración
    }

    [Fact]
    public async Task Disposed_catalog_is_an_error_without_asking_anyone()
    {
        int created = 0;
        var fake = new FakeProvider();
        var cat = new LazyLyricsCatalog(() => { created++; return new ILyricsProvider[] { fake }; });
        await cat.FetchAsync(Q, CancellationToken.None);
        int searches = fake.Searches;
        cat.Dispose();
        cat.Dispose();   // dos veces: nada

        Assert.True(fake.Disposed);
        Assert.Equal(LyricsStatus.Error, (await cat.FetchAsync(Q, CancellationToken.None)).Status);
        Assert.Empty(await cat.AllCandidatesAsync(Q, CancellationToken.None));
        Assert.Equal(1, created);
        Assert.Equal(searches, fake.Searches);
    }
}
