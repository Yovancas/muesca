using System.Drawing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Página Música (F5 · B3), lógica pura: geometría compartida entre pintar y acertar el clic
/// (<see cref="MusicLayout"/>), formato del tiempo, plazo del siguiente repintado (<see cref="MusicClock"/>), la cara de
/// música del reposo (<see cref="IdleMusic"/>) y los cruces de la vista (<see cref="MusicMotion"/>).
/// </summary>
public class MusicPageTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private static NowPlaying Track(bool playing = true, double posSec = 10, double durSec = 200, string title = "Song",
        string artist = "Artist") =>
        new(title, artist, "Album", playing, TimeSpan.FromSeconds(posSec), TimeSpan.FromSeconds(durSec), T0, null,
            AlbumAccent.Default, "Spotify.exe");

    /// <summary>Zona de contenido de la página Música a escala <paramref name="s"/> (la misma cuenta que el painter).</summary>
    private static RectangleF Inner(float s, bool withAlert = false)
    {
        var size = NotchLayout.PageSize(NotchPage.Music, default, withAlert, withDots: true, s);
        float pad = 18f * s, top = NotchLayout.ExpandedPadTop * s;
        if (withAlert) top += (NotchLayout.ExpandedLine + NotchLayout.ExpandedAlertGap) * s;
        float bottom = size.Height - (NotchLayout.ExpandedPadBottom + NotchLayout.PageDotsRow) * s;
        return RectangleF.FromLTRB(100f + pad, top, 100f + size.Width - pad, bottom);
    }

    private static IEnumerable<(string Name, RectangleF R)> Parts(MusicRects r, bool lyrics)
    {
        yield return ("art", r.Art);
        yield return ("title", r.Title);
        yield return ("artist", r.Artist);
        yield return ("prev", r.Previous);
        yield return ("play", r.PlayPause);
        yield return ("next", r.Next);
        yield return ("lyricsBtn", r.LyricsButton);
        yield return ("bar", r.Bar);
        yield return ("elapsed", r.Elapsed);
        yield return ("total", r.Total);
        if (lyrics)
            for (int i = -1; i <= 1; i++) yield return ($"line{i}", r.LyricLine(i));
    }

    public static IEnumerable<object[]> Views() => new[]
    {
        new object[] { 1f, false, false }, new object[] { 1f, true, false },
        new object[] { 1.5f, false, false }, new object[] { 1.5f, true, false },
        new object[] { 1f, true, true }, new object[] { 1.25f, false, true },
    };

    // ---------------------------------------------------------------- MusicLayout

    [Theory]
    [MemberData(nameof(Views))]
    public void Every_part_fits_inside_the_page(float s, bool lyrics, bool withAlert)
    {
        var inner = Inner(s, withAlert);
        var r = MusicLayout.Compute(inner, s, lyrics);
        foreach (var (name, rect) in Parts(r, lyrics))
        {
            Assert.True(rect.Width > 0 && rect.Height > 0, $"{name} vacío");
            Assert.True(rect.Left >= inner.Left - 0.01f && rect.Right <= inner.Right + 0.01f, $"{name} se sale a lo ancho: {rect} en {inner}");
            Assert.True(rect.Top >= inner.Top - 0.01f && rect.Bottom <= inner.Bottom + 0.01f, $"{name} se sale a lo alto: {rect} en {inner}");
        }
        Assert.True(r.BarHit.Contains(new PointF(r.Bar.X + r.Bar.Width / 2, r.Bar.Y + r.Bar.Height / 2)));
        Assert.True(r.BarHit.Height >= 12f * s, "la barra de 4 px sola sería imposible de acertar");
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void Clickable_parts_do_not_overlap(float s, bool lyrics, bool withAlert)
    {
        var r = MusicLayout.Compute(Inner(s, withAlert), s, lyrics);
        var clickable = new List<(string, RectangleF)>
        {
            ("prev", r.Previous), ("play", r.PlayPause), ("next", r.Next), ("lyricsBtn", r.LyricsButton), ("barHit", r.BarHit),
            ("art", r.Art), ("title", r.Title),
        };
        if (lyrics)
            for (int i = -1; i <= 1; i++) clickable.Add(($"line{i}", r.LyricLine(i)));
        for (int a = 0; a < clickable.Count; a++)
            for (int b = a + 1; b < clickable.Count; b++)
            {
                var x = RectangleF.Intersect(clickable[a].Item2, clickable[b].Item2);
                Assert.True(x.Width < 0.5f || x.Height < 0.5f, $"{clickable[a].Item1} pisa {clickable[b].Item1}");
            }
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(1f, true)]
    [InlineData(1.5f, true)]
    public void HitTest_finds_each_control_at_its_center(float s, bool lyrics)
    {
        var inner = Inner(s);
        var r = MusicLayout.Compute(inner, s, lyrics);
        static PointF C(RectangleF x) => new(x.X + x.Width / 2, x.Y + x.Height / 2);

        Assert.Equal(MusicControl.Previous, MusicLayout.HitTest(C(r.Previous), inner, s, lyrics).Control);
        Assert.Equal(MusicControl.PlayPause, MusicLayout.HitTest(C(r.PlayPause), inner, s, lyrics).Control);
        Assert.Equal(MusicControl.Next, MusicLayout.HitTest(C(r.Next), inner, s, lyrics).Control);
        Assert.Equal(MusicControl.Lyrics, MusicLayout.HitTest(C(r.LyricsButton), inner, s, lyrics).Control);
        Assert.Equal(MusicControl.None, MusicLayout.HitTest(C(r.Art), inner, s, lyrics).Control);
        Assert.Equal(MusicControl.None, MusicLayout.HitTest(new PointF(inner.Left - 5, inner.Top), inner, s, lyrics).Control);

        var bar = MusicLayout.HitTest(new PointF(r.Bar.Left + r.Bar.Width * 0.25f, C(r.Bar).Y), inner, s, lyrics);
        Assert.Equal(MusicControl.Progress, bar.Control);
        Assert.Equal(0.25f, bar.Fraction, 2);
        // Fuera de la barra pero en su franja (los extremos): recortado a 0..1.
        Assert.Equal(0f, MusicLayout.HitTest(new PointF(r.Bar.Left - 0.4f, C(r.Bar).Y), inner, s, lyrics).Fraction, 3);

        if (!lyrics)
        {
            Assert.Equal(MusicControl.None, MusicLayout.HitTest(C(r.LyricLine(0)), inner, s, lyrics).Control);
            return;
        }
        for (int i = -1; i <= 1; i++)
        {
            var hit = MusicLayout.HitTest(C(r.LyricLine(i)), inner, s, lyrics);
            Assert.Equal(MusicControl.LyricLine, hit.Control);
            Assert.Equal(i, hit.LineOffset);
        }
    }

    [Fact]
    public void Split_view_narrows_the_text_and_keeps_controls_and_bar_in_place()
    {
        var inner = Inner(1f);
        var normal = MusicLayout.Compute(inner, 1f, lyrics: false);
        var split = MusicLayout.Compute(inner, 1f, lyrics: true);
        Assert.Equal(normal.Art, split.Art);
        Assert.Equal(normal.Previous, split.Previous);
        Assert.Equal(normal.PlayPause, split.PlayPause);
        Assert.Equal(normal.Next, split.Next);
        Assert.Equal(normal.Bar, split.Bar);
        Assert.True(split.Title.Width < normal.Title.Width);
        Assert.True(split.LyricsColumn.Left >= split.Title.Right);
        Assert.True(split.LyricsColumn.Width >= 180f, $"columna de letra {split.LyricsColumn.Width}");
        Assert.True(normal.LyricsColumn.IsEmpty);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Geometry_scales_with_the_notch(float s)
    {
        var a = MusicLayout.Compute(Inner(1f), 1f, true);
        var b = MusicLayout.Compute(Inner(s), s, true);
        Assert.Equal(a.Art.Width * s, b.Art.Width, 2);
        Assert.Equal(a.PlayPause.Height * s, b.PlayPause.Height, 2);
    }

    // ---------------------------------------------------------------- MusicFormat

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(9.9, "0:09")]
    [InlineData(65, "1:05")]
    [InlineData(599, "9:59")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-3, "0:00")]
    public void Time_is_m_ss_or_h_mm_ss(double sec, string expected) =>
        Assert.Equal(expected, MusicFormat.Time(TimeSpan.FromSeconds(sec)));

    // ---------------------------------------------------------------- MusicClock

    [Fact]
    public void Nothing_to_tick_without_music_or_when_paused()
    {
        Assert.Null(MusicClock.NextTickIn(null, T0, null, 300f));
        Assert.Null(MusicClock.NextTickIn(Track(playing: false), T0, null, 300f));
    }

    [Fact]
    public void Tick_points_to_the_next_lyric_line_with_its_lead()
    {
        // Posición 10 s; la siguiente línea a las 10,1 s y el adelanto de 80 ms ⇒ 20 ms.
        var lines = new[] { new LyricLine(TimeSpan.FromSeconds(5), "a"), new LyricLine(TimeSpan.FromSeconds(10.1), "b") };
        var d = MusicClock.NextTickIn(Track(), T0, lines, 300f);
        Assert.Equal(Ms(20), d);
    }

    [Fact]
    public void Tick_is_clamped_between_15_and_250_ms()
    {
        // Línea dentro de 2 ms ⇒ 15 ms (no un bucle apretado); nada cerca ⇒ como mucho 250 ms.
        var soon = new[] { new LyricLine(TimeSpan.FromSeconds(10.082), "b") };
        Assert.Equal(MusicClock.MinTick, MusicClock.NextTickIn(Track(), T0, soon, 300f));
        var far = new[] { new LyricLine(TimeSpan.FromSeconds(60), "b") };
        var d = MusicClock.NextTickIn(Track(posSec: 10.5), T0, far, 0f);
        Assert.True(d <= MusicClock.MaxTick && d >= MusicClock.MinTick, $"{d}");
    }

    [Fact]
    public void Tick_follows_the_elapsed_time_text_without_lyrics()
    {
        // 10,6 s: el «0:10» pasa a «0:11» dentro de 400 ms ⇒ tope de 250; a 10,9 s ⇒ 100 ms.
        Assert.Equal(MusicClock.MaxTick, MusicClock.NextTickIn(Track(posSec: 10.6, durSec: 0), T0, null, 0f));
        Assert.Equal(Ms(100), MusicClock.NextTickIn(Track(posSec: 10.9, durSec: 0), T0, null, 0f));
    }

    [Fact]
    public void Tick_follows_the_progress_bar_pixels()
    {
        // 100 s en 1000 px = 100 ms por px; a 10,03 s el siguiente px llega a los 70 ms.
        Assert.Equal(Ms(70), MusicClock.NextTickIn(Track(posSec: 10.03, durSec: 100), T0, null, 1000f));
    }

    // ---------------------------------------------------------------- IdleMusic

    [Fact]
    public void Idle_face_shows_while_playing_and_lingers_briefly_after_pausing()
    {
        var m = new IdleMusic();
        Assert.False(m.Show(T0));
        m.Observe(Track(), T0);
        Assert.True(m.Show(T0));
        Assert.Equal("Song", m.Track!.Title);
        Assert.Null(m.NextChangeIn(T0));                     // sonando: nada que vigilar

        m.Observe(Track(playing: false), T0.AddSeconds(5));   // pausa (o el hueco entre canciones)
        Assert.True(m.Show(T0.AddSeconds(6)));
        Assert.Equal(IdleMusic.PauseGrace - TimeSpan.FromSeconds(1), m.NextChangeIn(T0.AddSeconds(6)));
        Assert.False(m.Show(T0.AddSeconds(5) + IdleMusic.PauseGrace));
        Assert.Null(m.NextChangeIn(T0.AddSeconds(5) + IdleMusic.PauseGrace));
    }

    [Fact]
    public void Idle_face_needs_music_that_actually_played()
    {
        var m = new IdleMusic();
        m.Observe(Track(playing: false), T0);       // abrir el PC con una canción en pausa: nada
        Assert.False(m.Show(T0));
        m.Observe(Track(), T0.AddSeconds(1));
        m.Observe(null, T0.AddSeconds(2));          // el reproductor se cierra: fuera al instante
        Assert.False(m.Show(T0.AddSeconds(2)));
        Assert.Null(m.Track);
    }

    [Fact]
    public void Track_change_gap_keeps_the_face_and_shows_the_new_title()
    {
        var m = new IdleMusic();
        m.Observe(Track(title: "A"), T0);
        m.Observe(Track(playing: false, title: "A"), T0.AddSeconds(1));
        m.Observe(Track(title: "B"), T0.AddSeconds(1.3));
        Assert.True(m.Show(T0.AddSeconds(1.3)));
        Assert.Equal("B", m.Track!.Title);
        Assert.Null(m.NextChangeIn(T0.AddSeconds(1.3)));
    }

    // ---------------------------------------------------------------- MusicMotion

    [Fact]
    public void Lyrics_toggle_crossfades_the_split_in_120ms_and_then_stops()
    {
        var m = new MusicMotion();
        Assert.False(m.ShowLyrics);
        Assert.Equal(0f, m.SplitBlend);
        m.SetLyrics(true, Ms(1000));
        Assert.True(m.ShowLyrics);
        Assert.True(m.IsAnimating);
        m.Step(Ms(1060));
        Assert.Equal(0.5f, m.SplitBlend, 2);
        m.Step(Ms(1120));
        Assert.Equal(1f, m.SplitBlend);
        Assert.False(m.IsAnimating);

        m.SetLyrics(false, Ms(2000));
        m.Step(Ms(2030));
        Assert.Equal(0.75f, m.SplitBlend, 2);
        m.Step(Ms(2200));
        Assert.Equal(0f, m.SplitBlend);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Toggling_mid_fade_reverses_from_where_it_is()
    {
        var m = new MusicMotion();
        m.SetLyrics(true, Ms(0));
        m.Step(Ms(90));                      // 0,75
        m.SetLyrics(false, Ms(90));
        m.Step(Ms(90));
        Assert.Equal(0.75f, m.SplitBlend, 2); // sin salto
        m.Step(Ms(180));
        Assert.Equal(0f, m.SplitBlend);
    }

    [Fact]
    public void Line_change_is_a_simultaneous_120ms_crossfade()
    {
        var m = new MusicMotion();
        m.JumpLine(3);
        Assert.Equal(3, m.Line);
        Assert.Equal(1f, m.LineBlend);
        Assert.False(m.IsAnimating);

        m.OnLine(4, Ms(500));
        Assert.Equal(4, m.Line);
        Assert.Equal(3, m.PreviousLine);
        Assert.True(m.IsAnimating);
        m.Step(Ms(530));
        Assert.Equal(0.25f, m.LineBlend, 2);
        m.Step(Ms(620));
        Assert.Equal(1f, m.LineBlend);
        Assert.False(m.IsAnimating);

        m.OnLine(4, Ms(700));                // misma línea: nada
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Reduce_motion_jumps()
    {
        var m = new MusicMotion { ReduceMotion = true };
        m.SetLyrics(true, Ms(0));
        Assert.Equal(1f, m.SplitBlend);
        m.OnLine(2, Ms(0));
        Assert.Equal(1f, m.LineBlend);
        Assert.False(m.IsAnimating);
    }
}
