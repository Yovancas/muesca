using System.Drawing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Letra compacta en el notch pequeño (pedido del 29-oct, como Nusa): cuándo se quiere la letra (red), qué cara elige el
/// reposo con música, ranuras y ancho resultante (300 + extra a saltos de 16, tope 200), la línea actual con el desfase,
/// las líneas vacías como «♪», el despertar en la siguiente línea y el cruce al cambiar de línea.
/// </summary>
public class CompactLyricsTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static NowPlaying Track(bool playing = true, TimeSpan position = default) =>
        new("Canción", "Artista", "Disco", playing, position, TimeSpan.FromSeconds(200), T0, null,
            AlbumAccent.Default, "Spotify.exe");

    private static readonly LyricLine[] Lines =
    {
        new(TimeSpan.FromSeconds(10), "Stay stocked up, real shit"),
        new(TimeSpan.FromSeconds(14), LrcParser.PauseText),
        new(TimeSpan.FromSeconds(20), "Tercera"),
    };

    // ---------------------------------------------------------------- red: Wanted

    [Fact]
    public void Compact_lyrics_want_the_lyrics_while_a_track_plays_even_with_the_view_closed()
    {
        // Lo de siempre sigue igual.
        Assert.True(LyricsController.Wanted(true, musicPageVisible: true, lyricsViewOpen: true));
        Assert.False(LyricsController.Wanted(true, musicPageVisible: true, lyricsViewOpen: false));
        // Letra pequeña activa + sonando: se quiere sin la vista.
        Assert.True(LyricsController.Wanted(true, false, false, compactEnabled: true, compactCanShow: true));
        // Si no puede verse (sin pista, notch oculto, «Minimalista»: ver CanShow) o con el ajuste apagado: no.
        Assert.False(LyricsController.Wanted(true, false, false, compactEnabled: true, compactCanShow: false));
        Assert.False(LyricsController.Wanted(true, false, false, compactEnabled: false, compactCanShow: true));
        // Con «Letras» apagado, nunca: ni la vista ni la letra pequeña.
        Assert.False(LyricsController.Wanted(false, true, true, compactEnabled: true, compactCanShow: true));
    }

    [Fact]
    public void Compact_wanted_still_asks_once_per_track()
    {
        var src = new CountingSource(Lines);
        using var c = new LyricsController(src);
        bool wanted = LyricsController.Wanted(true, false, false, compactEnabled: true, compactCanShow: true);
        for (int i = 0; i < 5; i++) c.Update(Track(position: TimeSpan.FromSeconds(i)), wanted);
        Assert.Equal(1, src.Calls);
        Assert.Equal(LyricsState.Found, c.State);
        // Aunque deje de quererse, la letra encontrada se queda (no se vuelve a pedir al volver).
        c.Update(Track(playing: false), LyricsController.Wanted(true, false, false, true, compactCanShow: false));
        c.Update(Track(), wanted);
        Assert.Equal(1, src.Calls);
    }

    [Fact]
    public void Compact_can_show_only_when_the_idle_can_show_the_song()
    {
        // Pista (sonando o en pausa) y notch a la vista, en reposo, trabajando o con el panel abierto: sí.
        Assert.True(CompactLyrics.CanShow(true, false, false, NotchMode.Idle));
        Assert.True(CompactLyrics.CanShow(true, false, false, NotchMode.Working));
        Assert.True(CompactLyrics.CanShow(true, false, false, NotchMode.Expanded));
        // Sin pista, en pantalla completa u oculto: no.
        Assert.False(CompactLyrics.CanShow(false, false, false, NotchMode.Idle));
        Assert.False(CompactLyrics.CanShow(true, true, false, NotchMode.Idle));
        Assert.False(CompactLyrics.CanShow(true, false, false, NotchMode.Hidden));
        // «Minimalista» (el reposo nunca enseña la canción, ni siquiera tras trabajar): no.
        Assert.False(CompactLyrics.CanShow(true, false, true, NotchMode.Minimal));
        Assert.False(CompactLyrics.CanShow(true, false, true, NotchMode.Working));
        // Encogido SOLO (sin «Minimalista»): sí, porque al llegar la letra el notch se despierta (KeepAwake, 29-oct).
        Assert.True(CompactLyrics.CanShow(true, false, false, NotchMode.Minimal));
    }

    /// <summary>Pedido del 29-oct: una canción con letra sonando no deja que el notch se encoja solo.</summary>
    [Fact]
    public void Keep_awake_only_while_a_song_with_synced_lyrics_is_playing()
    {
        var playing = Track(playing: true);
        Assert.True(CompactLyrics.KeepAwake(true, true, true, playing, LyricsState.Found, 12));
        Assert.False(CompactLyrics.KeepAwake(true, true, true, Track(playing: false), LyricsState.Found, 12));   // en pausa
        Assert.False(CompactLyrics.KeepAwake(true, true, true, playing, LyricsState.Plain, 0));       // sin sincronizar
        Assert.False(CompactLyrics.KeepAwake(true, true, true, playing, LyricsState.Loading, 0));
        Assert.False(CompactLyrics.KeepAwake(true, true, true, null, LyricsState.Found, 12));
        Assert.False(CompactLyrics.KeepAwake(false, true, true, playing, LyricsState.Found, 12));    // sin música
        Assert.False(CompactLyrics.KeepAwake(true, false, true, playing, LyricsState.Found, 12));    // sin letras
        Assert.False(CompactLyrics.KeepAwake(true, true, false, playing, LyricsState.Found, 12));    // sin letra pequeña
    }

    [Fact]
    public void Minimal_with_the_view_closed_never_asks_for_lyrics()
    {
        var src = new CountingSource(Lines);
        using var c = new LyricsController(src);
        foreach (var mode in new[] { NotchMode.Minimal, NotchMode.Working, NotchMode.Expanded })
            c.Update(Track(), LyricsController.Wanted(true, false, false, compactEnabled: true,
                CompactLyrics.CanShow(true, false, minimalSetting: true, mode)));
        Assert.Equal(0, src.Calls);
        Assert.Equal(LyricsState.Idle, c.State);
    }

    [Fact]
    public void Pausing_while_loading_keeps_a_single_request()
    {
        var src = new GatedSource();
        using var c = new LyricsController(src);
        bool Want(NowPlaying t) => LyricsController.Wanted(true, false, false, compactEnabled: true,
            CompactLyrics.CanShow(t is not null, false, false, NotchMode.Idle));
        var playing = Track();
        var paused = Track(playing: false);
        c.Update(playing, Want(playing));
        Assert.Equal(LyricsState.Loading, c.State);
        c.Update(paused, Want(paused));       // pausa breve (un salto, el búfer)…
        c.Update(playing, Want(playing));     // …y sigue
        Assert.Single(src.Calls);
        Assert.False(src.Calls[0].IsCancellationRequested);
        Assert.Equal(LyricsState.Loading, c.State);
    }

    [Fact]
    public void Compact_view_never_paints_a_line_of_other_lyrics()
    {
        var m = new CompactLyricsMotion();
        m.Show(0, "Línea de la canción vieja", TimeSpan.Zero);
        m.Show(1, "Otra línea vieja", TimeSpan.Zero);   // a mitad de cruce
        var same = NotchForm.CompactViewFor(m, sameLyrics: true, art: true, quota: false);
        Assert.Equal("Otra línea vieja", same.Line);
        Assert.Equal("Línea de la canción vieja", same.PreviousLine);
        Assert.False(same.Quota);

        // Cambio de pista: el controlador ya no tiene esas líneas; ni la línea ni la que sale se pintan.
        var other = NotchForm.CompactViewFor(m, sameLyrics: false, art: true, quota: true);
        Assert.Equal(LrcParser.PauseText, other.Line);
        Assert.Null(other.PreviousLine);
        Assert.Equal(1f, other.Alpha);
        Assert.Equal(0, other.Offset);
    }

    // ---------------------------------------------------------------- cara del reposo

    [Theory]
    [InlineData(true, true, true, true, LyricsState.Found, 3, IdleMusicFace.Lyrics)]
    [InlineData(false, true, true, true, LyricsState.Found, 3, IdleMusicFace.None)]      // sin reposo con música
    [InlineData(true, true, true, false, LyricsState.Found, 3, IdleMusicFace.Title)]     // en pausa: el título
    [InlineData(true, true, false, true, LyricsState.Found, 3, IdleMusicFace.Title)]     // letra pequeña apagada
    [InlineData(true, false, true, true, LyricsState.Found, 3, IdleMusicFace.Title)]     // letras apagadas
    [InlineData(true, true, true, true, LyricsState.Loading, 0, IdleMusicFace.Title)]
    [InlineData(true, true, true, true, LyricsState.NotFound, 0, IdleMusicFace.Title)]
    [InlineData(true, true, true, true, LyricsState.Error, 0, IdleMusicFace.Title)]
    [InlineData(true, true, true, true, LyricsState.Plain, 0, IdleMusicFace.Title)]      // plana: sin tiempos de verdad
    [InlineData(true, true, true, true, LyricsState.Idle, 0, IdleMusicFace.Title)]
    [InlineData(true, true, true, true, LyricsState.Found, 0, IdleMusicFace.Title)]
    public void Face_picks_the_compact_lyrics_only_with_synced_lyrics_while_playing(bool idleMusic, bool lyrics,
        bool compact, bool playing, LyricsState state, int lines, IdleMusicFace expected)
        => Assert.Equal(expected, CompactLyrics.Face(idleMusic, lyrics, compact, Track(playing), state, lines));

    [Fact]
    public void No_track_is_never_the_compact_face()
        => Assert.Equal(IdleMusicFace.Title, CompactLyrics.Face(true, true, true, null, LyricsState.Found, 3));

    // ---------------------------------------------------------------- ranuras y ancho

    public static IEnumerable<object[]> Slots() => new[]
    {
        new object[] { false, false }, new object[] { true, false }, new object[] { false, true }, new object[] { true, true },
    };

    [Theory]
    [MemberData(nameof(Slots))]
    public void Short_line_keeps_the_base_300_and_long_line_grows_in_steps_of_16_up_to_200(bool art, bool quota)
    {
        var slots = new CompactLyricsSlots(art, quota);
        float room = CompactLyrics.TextRoom(slots);
        Assert.Equal(0f, CompactLyrics.Extra(40f, slots));
        Assert.Equal(0f, CompactLyrics.Extra(room, slots));
        Assert.Equal(16f, CompactLyrics.Extra(room + 0.5f, slots));
        Assert.Equal(32f, CompactLyrics.Extra(room + 17f, slots));
        Assert.Equal(200f, CompactLyrics.Extra(room + 1000f, slots));
        for (float w = 0; w < 800; w += 7)
        {
            float e = CompactLyrics.Extra(w, slots);
            Assert.True(e >= 0 && e <= CompactLyrics.ExtraMax && (e % CompactLyrics.WidthStep == 0 || e == CompactLyrics.ExtraMax),
                $"extra {e} para {w}");   // el tope (200, como Trabajando) no es múltiplo de 16
        }
        var size = CompactLyrics.Size(CompactLyrics.Extra(room + 17f, slots), 1.5f);
        Assert.Equal(new SizeF(332f * 1.5f, 34f * 1.5f), size);
    }

    [Fact]
    public void Extra_discounts_what_the_active_slots_take()
    {
        // La izquierda va siempre (carátula o «♪»: mismo ancho, así una pista sin carátula no cambia el ancho); la cuota
        // le quita hueco a la línea, así que la misma línea pide más extra con ella.
        var none = new CompactLyricsSlots(false, false);
        var art = new CompactLyricsSlots(true, false);
        var quota = new CompactLyricsSlots(false, true);
        var both = new CompactLyricsSlots(true, true);
        Assert.Equal(CompactLyrics.TextRoom(none), CompactLyrics.TextRoom(art));
        Assert.Equal(CompactLyrics.TextRoom(quota), CompactLyrics.TextRoom(both));
        Assert.True(CompactLyrics.TextRoom(none) > CompactLyrics.TextRoom(quota));
        Assert.Equal(300f - 36f - 14f, CompactLyrics.TextRoom(none));
        Assert.Equal(300f - 36f - 38f, CompactLyrics.TextRoom(both));
        Assert.Equal(0f, CompactLyrics.Extra(240f, none));
        Assert.Equal(16f, CompactLyrics.Extra(240f, both));
    }

    [Theory]
    [MemberData(nameof(Slots))]
    public void Rects_center_the_line_between_the_slots_and_never_overlap(bool art, bool quota)
    {
        foreach (var s in new[] { 1f, 1.5f })
        {
            var size = CompactLyrics.Size(48f, s);
            var pill = new RectangleF(100f, 0f, size.Width, size.Height);
            var r = CompactLyrics.Compute(pill, s, new CompactLyricsSlots(art, quota));
            Assert.True(pill.Contains(r.Left) && r.Left.Width == 18f * s);
            Assert.True(r.Text.Left >= r.Left.Right + 8f * s - 0.01f);
            Assert.Equal(pill.Height, r.Text.Height);
            if (quota)
            {
                Assert.True(pill.Contains(r.Right));
                Assert.True(r.Text.Right <= r.Right.Left - 8f * s + 0.01f);
            }
            else
            {
                Assert.True(r.Right.IsEmpty);
                Assert.Equal(pill.Right - 14f * s, r.Text.Right, 3);
            }
            // El hueco a 300 + extra es exactamente el hueco base + extra.
            Assert.Equal((CompactLyrics.TextRoom(new(art, quota)) + 48f) * s, r.Text.Width, 3);
        }
    }

    // ---------------------------------------------------------------- línea actual

    [Fact]
    public void Current_line_uses_the_controller_offset()
    {
        var src = new CountingSource(Lines);
        using var c = new LyricsController(src);
        c.Update(Track(), wanted: true);
        Assert.Equal(LyricsState.Found, c.State);
        var player = TimeSpan.FromSeconds(10.5);
        Assert.Equal("Stay stocked up, real shit", CompactLyrics.LineText(c.Lines, CompactLyrics.LineIndex(c.Lines, c.LyricsPosition(player))));

        // +1 s = la letra va más tarde: a los 10,5 s del reproductor aún no ha empezado (antes de la primera: «♪»).
        c.NudgeOffset(0.5);
        c.NudgeOffset(0.5);
        Assert.Equal(1.0, c.OffsetSeconds);
        int i = CompactLyrics.LineIndex(c.Lines, c.LyricsPosition(player));
        Assert.Equal(-1, i);
        Assert.Equal(LrcParser.PauseText, CompactLyrics.LineText(c.Lines, i));
        Assert.Equal(0, CompactLyrics.LineIndex(c.Lines, c.LyricsPosition(TimeSpan.FromSeconds(11))));
    }

    [Fact]
    public void Lead_switches_the_line_80_ms_early_like_the_music_page()
    {
        Assert.Equal(-1, CompactLyrics.LineIndex(Lines, TimeSpan.FromSeconds(9.9)));
        Assert.Equal(0, CompactLyrics.LineIndex(Lines, TimeSpan.FromSeconds(9.93)));
    }

    [Fact]
    public void Empty_lines_and_gaps_show_a_note()
    {
        var parsed = LrcParser.Parse("[00:01.00]Hola\n[00:03.00]\n[00:05.00]   \n[00:07.00]Adiós");
        Assert.Equal(LrcParser.PauseText, CompactLyrics.LineText(parsed, 1));
        Assert.Equal(LrcParser.PauseText, CompactLyrics.LineText(parsed, 2));
        Assert.Equal("Adiós", CompactLyrics.LineText(parsed, 3));
        Assert.Equal(LrcParser.PauseText, CompactLyrics.LineText(parsed, -1));
        Assert.Equal(LrcParser.PauseText, CompactLyrics.LineText(parsed, 99));
        Assert.Equal(LrcParser.PauseText, CompactLyrics.LineText(new[] { new LyricLine(TimeSpan.Zero, "  ") }, 0));
    }

    // ---------------------------------------------------------------- despertar

    [Fact]
    public void Wakes_at_the_next_line_not_every_frame()
    {
        // A los 11 s la vigente es la 0; la siguiente (14 s) toca a los 14 − 0,08.
        Assert.Equal(TimeSpan.FromSeconds(14) - MusicClock.LyricLead - TimeSpan.FromSeconds(11),
            CompactLyrics.NextLineIn(Lines, TimeSpan.FromSeconds(11)));
        // Antes de la primera: la primera.
        Assert.Equal(TimeSpan.FromSeconds(10) - MusicClock.LyricLead - TimeSpan.FromSeconds(2),
            CompactLyrics.NextLineIn(Lines, TimeSpan.FromSeconds(2)));
        // Tras la última, nada que despertar; sin letra, tampoco.
        Assert.Null(CompactLyrics.NextLineIn(Lines, TimeSpan.FromSeconds(25)));
        Assert.Null(CompactLyrics.NextLineIn(Array.Empty<LyricLine>(), TimeSpan.FromSeconds(1)));
        // Justo en el plazo (el timer saltó un pelo antes): se mira de nuevo en 1 ms, nunca 0 ni negativo.
        var edge = CompactLyrics.NextLineIn(Lines, TimeSpan.FromSeconds(14) - MusicClock.LyricLead - TimeSpan.FromTicks(1));
        Assert.Equal(TimeSpan.FromMilliseconds(1), edge);
    }

    [Fact]
    public void The_window_wakes_for_the_next_line_unless_hidden()
    {
        var line = TimeSpan.FromMilliseconds(700);
        Assert.Equal(line, NotchForm.NextWake(TimeSpan.FromSeconds(5), null, TimeSpan.FromSeconds(3), false, line));
        Assert.Equal(TimeSpan.FromMilliseconds(200),
            NotchForm.NextWake(TimeSpan.FromMilliseconds(200), null, null, false, line));
        Assert.Equal(line, NotchForm.NextWake(null, null, null, false, line));
        // Oculto por pantalla completa: solo la máquina.
        Assert.Null(NotchForm.NextWake(null, null, null, true, line));
    }

    // ---------------------------------------------------------------- cruce

    [Fact]
    public void Line_change_crossfades_and_rises_in_whole_pixels()
    {
        var m = new CompactLyricsMotion();
        var t = TimeSpan.FromSeconds(1);
        m.Show(0, "Uno", t);
        Assert.False(m.IsAnimating);   // la primera, sin cruce
        Assert.Equal("Uno", m.Text);

        m.Show(1, "Dos", t);
        Assert.True(m.IsAnimating);
        Assert.Equal("Uno", m.PreviousText);
        Assert.Equal(0f, m.Alpha);
        Assert.Equal(1f, m.PreviousAlpha);
        Assert.Equal((int)CompactLyricsMotion.Rise, m.Offset);   // entra desde abajo
        Assert.Equal(0, m.PreviousOffset);

        m.Step(t + TimeSpan.FromMilliseconds(80));
        Assert.Equal(0f, m.PreviousAlpha);   // la vieja ya se fue (primer 60 %)
        Assert.True(m.Alpha > 0.9f && m.Alpha < 1f);
        Assert.Equal(-(int)CompactLyricsMotion.Rise, m.PreviousOffset);

        for (int ms = 0; ms <= 130; ms += 5)
        {
            m.Step(t + TimeSpan.FromMilliseconds(ms));
            Assert.InRange(m.Offset, 0, (int)CompactLyricsMotion.Rise);   // px enteros (int): nunca medio px
            Assert.InRange(m.PreviousOffset, -(int)CompactLyricsMotion.Rise, 0);
        }
        Assert.False(m.IsAnimating);
        Assert.Null(m.PreviousText);
        Assert.Equal(0, m.Offset);

        // La misma línea no vuelve a cruzar; la misma letra en otra línea (estribillo repetido), sí.
        m.Show(1, "Dos", t);
        Assert.False(m.IsAnimating);
        m.Show(2, "Dos", t);
        Assert.True(m.IsAnimating);
    }

    [Fact]
    public void Reduce_motion_cuts_and_jump_never_crossfades()
    {
        var m = new CompactLyricsMotion { ReduceMotion = true };
        m.Show(0, "Uno", TimeSpan.Zero);
        m.Show(1, "Dos", TimeSpan.Zero);
        Assert.False(m.IsAnimating);
        Assert.Null(m.PreviousText);
        Assert.Equal(0, m.Offset);
        Assert.Equal(1f, m.Alpha);

        var n = new CompactLyricsMotion();
        n.Show(0, "Uno", TimeSpan.Zero);
        n.Jump(5, "Otra pista");
        Assert.False(n.IsAnimating);
        Assert.Equal("Otra pista", n.Text);
    }

    private sealed class GatedSource : ILyricsSource
    {
        public readonly List<CancellationToken> Calls = new();

        public Task<LyricsResult> FetchAsync(string title, string artist, TimeSpan duration, CancellationToken ct)
        {
            Calls.Add(ct);
            return new TaskCompletionSource<LyricsResult>().Task;   // no contesta: se queda buscando
        }
    }

    private sealed class CountingSource : ILyricsSource
    {
        private readonly IReadOnlyList<LyricLine> _lines;
        public CountingSource(IReadOnlyList<LyricLine> lines) => _lines = lines;
        public int Calls { get; private set; }

        public Task<LyricsResult> FetchAsync(string title, string artist, TimeSpan duration, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new LyricsResult(LyricsStatus.Found, _lines));
        }
    }
}
