using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Letras v2 · A2: <see cref="LyricsCache"/> (JSON atómico en un directorio temporal, reloj inyectado): ida y vuelta,
/// LRU, caducidad a 30 días, clave estable y desfase manual acotado.
/// </summary>
public sealed class LyricsCacheTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-lyrics-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = T0;

    private string PathJson => Path.Combine(_dir, "lyrics-cache.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private LyricsCache New(int max = LyricsCache.DefaultMax) => new(PathJson, max, () => _now);

    private static CachedLyrics Lyrics(string id, DateTime saved, bool synced = true) =>
        new("lrclib", id, synced, synced ? "[00:01.00]Hola" : "Hola", TimeSpan.FromSeconds(200), saved);

    // ---------------- Ida y vuelta ----------------

    [Fact]
    public void Put_survives_a_restart()
    {
        var a = Lyrics("1", T0);
        var plain = Lyrics("2", T0, synced: false);
        var c = New();
        c.Put("k1", a);
        c.Put("k2", plain);

        var again = New();
        Assert.True(again.TryGet("k1", out var got1));
        Assert.Equal(a, got1);
        Assert.True(again.TryGet("k2", out var got2));
        Assert.Equal(plain, got2);
        Assert.False(again.TryGet("otra", out _));
    }

    [Fact]
    public void Choice_survives_a_restart_and_keeps_the_lyrics()
    {
        var c = New();
        c.Put("k", Lyrics("1", T0));
        c.SetChoice("k", new LyricsChoice("LRCLIB", "42", 1.5));

        var again = New();
        Assert.Equal(new LyricsChoice("LRCLIB", "42", 1.5), again.Choice("k"));
        Assert.True(again.TryGet("k", out _));
        Assert.Null(again.Choice("otra"));
    }

    [Fact]
    public void Put_does_not_drop_an_existing_choice()
    {
        var c = New();
        c.SetChoice("k", new LyricsChoice("lrclib", "7", -2));
        c.Put("k", Lyrics("7", T0));

        Assert.Equal(new LyricsChoice("lrclib", "7", -2), New().Choice("k"));
    }

    [Fact]
    public void Missing_or_corrupt_file_is_an_empty_cache()
    {
        Assert.False(New().TryGet("k", out _));

        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathJson, "{ esto no es json");
        var c = New();
        Assert.False(c.TryGet("k", out _));

        c.Put("k", Lyrics("1", T0));   // se sobrescribe en el siguiente cambio
        Assert.True(New().TryGet("k", out _));
    }

    // ---------------- LRU ----------------

    [Fact]
    public void Least_recently_used_entry_is_evicted_and_eviction_persists()
    {
        var c = New(max: 2);
        c.Put("a", Lyrics("a", T0));
        c.Put("b", Lyrics("b", T0));
        Assert.True(c.TryGet("a", out _));   // «a» pasa a ser la más reciente
        c.Put("c", Lyrics("c", T0));

        Assert.False(c.TryGet("b", out _));
        Assert.True(c.TryGet("a", out _));
        Assert.True(c.TryGet("c", out _));

        var again = New(max: 2);
        Assert.False(again.TryGet("b", out _));
        Assert.True(again.TryGet("a", out _));
        Assert.True(again.TryGet("c", out _));
    }

    [Fact]
    public void Order_of_use_survives_a_restart()
    {
        var c = New(max: 2);
        c.Put("a", Lyrics("a", T0));
        c.Put("b", Lyrics("b", T0));
        Assert.True(c.TryGet("a", out _));

        var again = New(max: 2);
        again.Put("c", Lyrics("c", T0));
        Assert.False(again.TryGet("b", out _));
        Assert.True(again.TryGet("a", out _));
    }

    [Fact]
    public void A_smaller_max_trims_on_load()
    {
        var c = New(max: 3);
        c.Put("a", Lyrics("a", T0));
        c.Put("b", Lyrics("b", T0));
        c.Put("c", Lyrics("c", T0));

        var small = New(max: 1);
        Assert.True(small.TryGet("c", out _));
        Assert.False(small.TryGet("a", out _));
        Assert.False(small.TryGet("b", out _));
    }

    [Fact]
    public void Max_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricsCache(PathJson, 0));

    // ---------------- Caducidad ----------------

    [Fact]
    public void Lyrics_expire_after_30_days_but_the_choice_stays()
    {
        var c = New();
        c.Put("viejo", Lyrics("1", T0 - TimeSpan.FromDays(31)));
        c.Put("k", Lyrics("2", T0 - TimeSpan.FromDays(29)));
        c.SetChoice("k", new LyricsChoice("lrclib", "2", 0.5));

        Assert.False(c.TryGet("viejo", out _));
        Assert.True(c.TryGet("k", out _));

        _now = T0 + TimeSpan.FromDays(2);   // ya tiene 31 días
        Assert.False(c.TryGet("k", out _));
        Assert.Equal(new LyricsChoice("lrclib", "2", 0.5), c.Choice("k"));
        Assert.False(New().TryGet("k", out _));
        Assert.Equal(new LyricsChoice("lrclib", "2", 0.5), New().Choice("k"));
    }

    // ---------------- Clave ----------------

    private static TrackQuery Q(string title, string artist, double seconds, string app = "Spotify.exe") =>
        new(title, artist, null, TimeSpan.FromSeconds(seconds), app);

    [Fact]
    public void Key_is_stable_across_case_accents_spaces_and_small_duration_jitter()
    {
        var k = LyricsCache.Key(Q("Despechá", "Rosalía", 157.2));
        Assert.Equal(k, LyricsCache.Key(Q("  DESPECHA ", "rosalia", 157.9)));
        Assert.Equal(k, LyricsCache.Key(Q("Despechá", "Rosalía", 158.4, app: "Chrome")));   // la app no cuenta
        Assert.Equal(k, LyricsCache.Key(new TrackQuery("Despechá", "Rosalía", "Motomami", TimeSpan.FromSeconds(157), "x")));
    }

    [Fact]
    public void Key_differs_for_other_track_artist_or_duration()
    {
        var k = LyricsCache.Key(Q("Song", "Artist", 200));
        Assert.NotEqual(k, LyricsCache.Key(Q("Song 2", "Artist", 200)));
        Assert.NotEqual(k, LyricsCache.Key(Q("Song", "Other", 200)));
        Assert.NotEqual(k, LyricsCache.Key(Q("Song", "Artist", 204)));
        // Título y artista no se confunden al juntarse.
        Assert.NotEqual(LyricsCache.Key(Q("a b", "c", 200)), LyricsCache.Key(Q("a", "b c", 200)));
    }

    [Fact]
    public void Key_without_duration_is_still_valid()
    {
        Assert.Equal(LyricsCache.Key(Q("Song", "Artist", 0)), LyricsCache.Key(Q("song", "ARTIST", 0)));
        Assert.Equal(LyricsCache.Key(Q("Song", "Artist", 0)), LyricsCache.Key(Q("Song", "Artist", -5)));
    }

    // ---------------- Desfase ----------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(-1.5, -1.5)]
    [InlineData(0.3, 0.5)]
    [InlineData(0.2, 0)]
    [InlineData(-0.8, -1)]
    [InlineData(10, 10)]
    [InlineData(12, 10)]
    [InlineData(-15.25, -10)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 10)]
    [InlineData(double.NegativeInfinity, -10)]
    public void Offset_is_clamped_to_ten_seconds_in_half_second_steps(double raw, double expected) =>
        Assert.Equal(expected, LyricsCache.ClampOffset(raw));

    [Fact]
    public void SetChoice_stores_the_clamped_offset()
    {
        var c = New();
        c.SetChoice("k", new LyricsChoice("lrclib", "1", 37));
        Assert.Equal(10, c.Choice("k")!.OffsetSeconds);
        Assert.Equal(10, New().Choice("k")!.OffsetSeconds);
    }

    [Fact]
    public void File_is_written_atomically_without_temporaries()
    {
        var c = New();
        c.Put("k", Lyrics("1", T0));
        c.SetChoice("k", new LyricsChoice("lrclib", "1", 0));

        Assert.True(File.Exists(PathJson));
        Assert.Equal(new[] { PathJson }, Directory.GetFiles(_dir));
    }

    // ---------------- Escritura agrupada ----------------

    [Fact]
    public void Delayed_writes_wait_and_Flush_writes_the_last_state()
    {
        using var c = new LyricsCache(PathJson, LyricsCache.DefaultMax, () => _now, TimeSpan.FromMinutes(5));
        c.Put("k", Lyrics("1", T0));
        for (int i = 1; i <= 5; i++) c.SetChoice("k", new LyricsChoice("lrclib", "1", i * LyricsCache.OffsetStep));
        Assert.True(c.TryGet("k", out _));
        Assert.False(File.Exists(PathJson));   // nada en disco todavía: ni el Put ni los cinco clics

        c.Flush();
        Assert.Equal(2.5, New().Choice("k")!.OffsetSeconds);
        var written = File.GetLastWriteTimeUtc(PathJson);
        c.Flush();   // sin cambios: no vuelve a escribir
        Assert.Equal(written, File.GetLastWriteTimeUtc(PathJson));
    }

    [Fact]
    public void Dispose_writes_what_is_pending_and_later_changes_go_straight_to_disk()
    {
        var c = new LyricsCache(PathJson, LyricsCache.DefaultMax, () => _now, TimeSpan.FromMinutes(5));
        c.SetChoice("k", new LyricsChoice("lrclib", "1", 1));
        c.Dispose();
        Assert.Equal(1, New().Choice("k")!.OffsetSeconds);

        c.SetChoice("k", new LyricsChoice("lrclib", "1", 2));
        Assert.Equal(2, New().Choice("k")!.OffsetSeconds);
    }

    [Fact]
    public async Task Delayed_writes_reach_the_disk_on_their_own()
    {
        using var c = new LyricsCache(PathJson, LyricsCache.DefaultMax, () => _now, TimeSpan.FromMilliseconds(50));
        c.SetChoice("k", new LyricsChoice("lrclib", "1", 1));
        for (int i = 0; i < 200 && !File.Exists(PathJson); i++) await Task.Delay(25);
        Assert.Equal(1, New().Choice("k")!.OffsetSeconds);
    }

    [Fact]
    public void Default_path_is_in_the_notch_folder()
    {
        Assert.EndsWith(Path.Combine("ClaudeBarWin", "notch", "lyrics-cache.json"), LyricsCache.DefaultPath);
    }
}
