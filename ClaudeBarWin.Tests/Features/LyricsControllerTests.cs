using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="LyricsController"/> (F5 · B3): cuándo se piden letras y qué se pinta. Revisión §1: sin letra →
/// «Sin letra» sin reintentos infinitos; un cambio rápido de canción nunca pinta la letra de la anterior. Y la regla de
/// red: solo se pide cuando la ventana la quiere (vista de letra abierta, o letra en el notch pequeño con una pista
/// sonando; el llamador decide «wanted» con <see cref="LyricsController.Wanted"/>).
/// </summary>
public class LyricsControllerTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static NowPlaying Track(string title, string artist = "Artist", bool playing = true) =>
        new(title, artist, "Album", playing, TimeSpan.Zero, TimeSpan.FromSeconds(200), T0, null,
            AlbumAccent.Default, "Spotify.exe");

    private static LyricsResult Found(string text) =>
        new(LyricsStatus.Found, new[] { new LyricLine(TimeSpan.Zero, text) });

    /// <summary>Fuente falsa: cada petición queda pendiente hasta que el test la resuelve.</summary>
    private sealed class FakeSource : ILyricsSource
    {
        public readonly List<(string Title, string Artist, TaskCompletionSource<LyricsResult> Tcs, CancellationToken Ct)> Calls = new();

        public Task<LyricsResult> FetchAsync(string title, string artist, TimeSpan duration, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<LyricsResult>();
            ct.Register(() => tcs.TrySetCanceled(ct));
            Calls.Add((title, artist, tcs, ct));
            return tcs.Task;
        }
    }

    private static (LyricsController C, FakeSource Src, List<int> Changes) New()
    {
        var src = new FakeSource();
        var c = new LyricsController(src);
        var changes = new List<int>();
        c.Changed += () => changes.Add(changes.Count);
        return (c, src, changes);
    }

    [Fact]
    public void Nothing_is_requested_until_the_lyrics_view_is_wanted()
    {
        var (c, src, _) = New();
        c.Update(Track("A"), wanted: false);
        Assert.Empty(src.Calls);
        Assert.Equal(LyricsState.Idle, c.State);

        c.Update(Track("A"), wanted: true);
        Assert.Single(src.Calls);
        Assert.Equal(LyricsState.Loading, c.State);

        c.Update(Track("A"), wanted: true);          // repetir no duplica la petición
        Assert.Single(src.Calls);
    }

    [Fact]
    public void Found_lines_are_published_once()
    {
        var (c, src, changes) = New();
        c.Update(Track("A"), true);
        int before = changes.Count;
        src.Calls[0].Tcs.SetResult(Found("hola"));
        Assert.Equal(LyricsState.Found, c.State);
        Assert.Equal("hola", Assert.Single(c.Lines).Text);
        Assert.Equal(before + 1, changes.Count);
        c.Update(Track("A"), true);
        Assert.Single(src.Calls);
    }

    [Fact]
    public void NotFound_shows_no_lyrics_and_never_asks_again_for_that_track()
    {
        var (c, src, _) = New();
        c.Update(Track("Instrumental"), true);
        src.Calls[0].Tcs.SetResult(LyricsResult.NotFound);
        Assert.Equal(LyricsState.NotFound, c.State);
        Assert.Empty(c.Lines);

        // Cerrar y abrir la vista, o los eventos del reproductor: ni una petición más.
        for (int i = 0; i < 5; i++)
        {
            c.Update(Track("Instrumental"), false);
            c.Update(Track("Instrumental"), true);
        }
        Assert.Single(src.Calls);
        Assert.Equal(LyricsState.NotFound, c.State);
    }

    [Fact]
    public void Error_is_not_retried_in_a_loop_only_when_the_view_is_reopened()
    {
        var (c, src, _) = New();
        c.Update(Track("A"), true);
        src.Calls[0].Tcs.SetResult(LyricsResult.Error);   // el cliente ya hizo sus 2 reintentos
        Assert.Equal(LyricsState.Error, c.State);
        c.Update(Track("A"), true);
        c.Update(Track("A"), true);
        Assert.Single(src.Calls);

        c.Update(Track("A"), false);                       // el usuario cierra la vista…
        c.Update(Track("A"), true);                        // …y la vuelve a abrir: un intento más
        Assert.Equal(2, src.Calls.Count);
    }

    [Fact]
    public void A_source_that_throws_is_an_error_not_a_crash()
    {
        var (c, src, _) = New();
        c.Update(Track("A"), true);
        src.Calls[0].Tcs.SetException(new InvalidOperationException("boom"));
        Assert.Equal(LyricsState.Error, c.State);
    }

    [Fact]
    public void Fast_track_change_never_paints_the_previous_lyrics()
    {
        var (c, src, _) = New();
        c.Update(Track("A"), true);
        c.Update(Track("B"), true);                        // salta de canción antes de que llegue la letra de A
        Assert.Equal(2, src.Calls.Count);
        Assert.True(src.Calls[0].Ct.IsCancellationRequested, "la petición de A se cancela");
        Assert.Equal(LyricsState.Loading, c.State);

        // Aunque la respuesta de A llegara igualmente (sin respetar la cancelación), se descarta.
        src.Calls[0].Tcs.TrySetResult(Found("letra de A"));
        Assert.Equal(LyricsState.Loading, c.State);
        Assert.Empty(c.Lines);

        src.Calls[1].Tcs.SetResult(Found("letra de B"));
        Assert.Equal("letra de B", Assert.Single(c.Lines).Text);
    }

    [Fact]
    public void Late_answer_after_switching_tracks_and_back_is_still_discarded()
    {
        var (c, src, _) = New();
        c.Update(Track("A"), true);
        c.Update(Track("B"), true);
        c.Update(Track("A"), true);                        // vuelve a A: petición nueva (la vieja se canceló)
        Assert.Equal(3, src.Calls.Count);
        src.Calls[0].Tcs.TrySetResult(Found("vieja"));
        Assert.Equal(LyricsState.Loading, c.State);
        src.Calls[2].Tcs.SetResult(Found("nueva"));
        Assert.Equal("nueva", Assert.Single(c.Lines).Text);
    }

    [Fact]
    public void Track_change_clears_the_lines_at_once()
    {
        var (c, src, _) = New();
        c.Update(Track("A"), true);
        src.Calls[0].Tcs.SetResult(Found("a"));
        c.Update(Track("B"), false);
        Assert.Equal(LyricsState.Idle, c.State);
        Assert.Empty(c.Lines);
        c.Update(null, true);
        Assert.Equal(LyricsState.Idle, c.State);
        Assert.Single(src.Calls);
    }

    [Fact]
    public void Hiding_the_view_cancels_the_request_in_flight()
    {
        var (c, src, _) = New();
        c.Update(Track("A"), true);
        c.Update(Track("A"), false);
        Assert.True(src.Calls[0].Ct.IsCancellationRequested);
        Assert.Equal(LyricsState.Idle, c.State);
        src.Calls[0].Tcs.TrySetResult(Found("tarde"));
        Assert.Equal(LyricsState.Idle, c.State);

        c.Update(Track("A"), true);                        // al volver, se pide otra vez (la caché del cliente ayuda)
        Assert.Equal(2, src.Calls.Count);
    }

    [Fact]
    public void Same_song_with_other_casing_or_spaces_is_the_same_track()
    {
        var (c, src, _) = New();
        c.Update(Track("Song", "Band"), true);
        c.Update(Track(" song ", "BAND"), true);
        Assert.Single(src.Calls);
    }

    [Fact]
    public void Pause_and_position_updates_do_not_restart_the_request()
    {
        var (c, src, _) = New();
        c.Update(Track("A"), true);
        c.Update(Track("A", playing: false), true);
        c.Update(Track("A") with { Position = TimeSpan.FromSeconds(42) }, true);
        Assert.Single(src.Calls);
        Assert.False(src.Calls[0].Ct.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_cancels_and_ignores_late_answers()
    {
        var (c, src, changes) = New();
        c.Update(Track("A"), true);
        int before = changes.Count;
        c.Dispose();
        Assert.True(src.Calls[0].Ct.IsCancellationRequested);
        src.Calls[0].Tcs.TrySetResult(Found("x"));
        Assert.Equal(before, changes.Count);
        c.Update(Track("B"), true);                        // tras liberar, no pide nada
        Assert.Single(src.Calls);
    }

    [Fact]
    public void LrclibClient_is_a_lyrics_source() =>
        Assert.True(typeof(ILyricsSource).IsAssignableFrom(typeof(LrclibClient)));
}
