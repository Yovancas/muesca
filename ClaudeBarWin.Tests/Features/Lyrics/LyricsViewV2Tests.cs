using System.Drawing;
using System.Globalization;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features.Lyrics;

/// <summary>
/// Vista de letra v2, lógica pura: desfase aplicado a la línea actual y al plazo del repintado, letra plana con tiempos
/// estimados (<see cref="PlainLyricsTiming"/>), botones −0,5 s / +0,5 s y «Otra letra» en la fila de los tiempos
/// (<see cref="MusicLayout"/>) y los textos que enseñan (<see cref="MusicFormat"/>).
/// </summary>
public class LyricsViewV2Tests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    private static NowPlaying Track(double posSec = 10, double durSec = 200, bool playing = true) =>
        new("Song", "Artist", "Album", playing, S(posSec), S(durSec), T0, null, AlbumAccent.Default, "Spotify.exe");

    private static RectangleF Inner(float s, bool withAlert = false)
    {
        var size = NotchLayout.PageSize(NotchPage.Music, default, withAlert, withDots: true, s);
        float pad = 18f * s, top = NotchLayout.ExpandedPadTop * s;
        if (withAlert) top += (NotchLayout.ExpandedLine + NotchLayout.ExpandedAlertGap) * s;
        float bottom = size.Height - (NotchLayout.ExpandedPadBottom + NotchLayout.PageDotsRow) * s;
        return RectangleF.FromLTRB(100f + pad, top, 100f + size.Width - pad, bottom);
    }

    private static PointF C(RectangleF x) => new(x.X + x.Width / 2, x.Y + x.Height / 2);

    private static readonly LyricLine[] Synced =
    {
        new(S(5), "uno"), new(S(10), "dos"), new(S(15), "tres"), new(S(20), "cuatro"),
    };

    // ---------------------------------------------------------------- desfase

    [Theory]
    [InlineData(0.0, 1)]    // 10 s → «dos»
    [InlineData(1.0, 0)]    // la letra va 1 s más tarde: a los 10 s del reproductor la letra está en 9 s → «uno»
    [InlineData(-5.0, 2)]   // va 5 s antes: letra en 15 s → «tres»
    [InlineData(20.0, -1)]  // más tarde que el principio: aún antes de la primera línea (y nunca un instante negativo)
    public void Offset_moves_the_current_line_positive_means_later(double offset, int expected)
    {
        var catalog = new LazyLyricsCatalog();
        using var c = new LyricsController(catalog);
        var pos = c.LyricsPosition(S(10));   // sin letra: desfase 0
        Assert.Equal(S(10), pos);

        // La cuenta que hace la ventana: posición del reproductor − desfase.
        var at = S(10) - S(offset);
        Assert.Equal(expected, LrcParser.IndexAt(Synced, at, TimeSpan.Zero));
    }

    [Fact]
    public void Tick_with_offset_waits_for_the_shifted_line()
    {
        // Reproductor en 10 s; siguiente línea de la letra a los 15 s; letra 2 s más tarde ⇒ toca a los 17 s del
        // reproductor: 7 s menos el adelanto de 80 ms, acotado al máximo de 250 ms.
        var d = MusicClock.NextTickIn(Track(posSec: 16.9), T0, Synced, 0f, S(2));
        Assert.Equal(TimeSpan.FromMilliseconds(20), d);   // 17 − 0,08 − 16,9 = 0,02 s
        var noShift = MusicClock.NextTickIn(Track(posSec: 16.9), T0, Synced, 0f);
        Assert.NotEqual(d, noShift);   // sin desfase la siguiente es la de los 20 s
        var early = MusicClock.NextTickIn(Track(posSec: 12.9), T0, Synced, 0f, S(-2));
        Assert.Equal(TimeSpan.FromMilliseconds(20), early);   // 15 − 2 − 0,08 − 12,9
    }

    // ---------------------------------------------------------------- letra plana

    [Fact]
    public void Plain_lines_spread_over_the_song_without_blank_lines()
    {
        var lines = PlainLyricsTiming.Estimate("uno\n\n dos \r\ntres\r\n\r\ncuatro\n", S(200));
        Assert.Equal(new[] { "uno", "dos", "tres", "cuatro" }, lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.Zero, lines[0].At);
        for (int i = 1; i < lines.Count; i++) Assert.True(lines[i].At > lines[i - 1].At);
        Assert.Equal(200 * PlainLyricsTiming.Span * 3 / 4, lines[3].At.TotalSeconds, 3);
        Assert.True(lines[^1].At < S(200) * PlainLyricsTiming.Span);
    }

    [Fact]
    public void Plain_lines_follow_the_position_fraction()
    {
        var lines = PlainLyricsTiming.Estimate(string.Join("\n", Enumerable.Range(1, 10).Select(i => $"línea {i}")), S(100));
        // A mitad de canción (50 s) y 92 s útiles para 10 líneas (9,2 s cada una) ⇒ la sexta (índice 5).
        Assert.Equal(5, LrcParser.IndexAt(lines, S(50), MusicClock.LyricLead));
        Assert.Equal(0, LrcParser.IndexAt(lines, S(0), MusicClock.LyricLead));
        Assert.Equal(9, LrcParser.IndexAt(lines, S(99), MusicClock.LyricLead));
    }

    [Fact]
    public void Plain_lines_without_duration_go_at_a_steady_pace_and_empty_text_gives_nothing()
    {
        var lines = PlainLyricsTiming.Estimate("a\nb\nc", TimeSpan.Zero);
        Assert.Equal(new[] { TimeSpan.Zero, PlainLyricsTiming.FallbackLine, PlainLyricsTiming.FallbackLine * 2 }, lines.Select(l => l.At));
        Assert.Empty(PlainLyricsTiming.Estimate(null, S(100)));
        Assert.Empty(PlainLyricsTiming.Estimate(" \n \r\n", S(100)));
    }

    [Fact]
    public void Plain_lines_schedule_the_repaint_like_synced_ones()
    {
        var lines = PlainLyricsTiming.Estimate("a\nb", S(100));   // b a los 46 s
        var d = MusicClock.NextTickIn(Track(posSec: 45.9, durSec: 100), T0, lines, 0f);
        Assert.Equal(TimeSpan.FromMilliseconds(20), d);
    }

    // ---------------------------------------------------------------- botones de la vista de letra

    public static IEnumerable<object[]> Scales() => new[]
    {
        new object[] { 1f, false }, new object[] { 1.25f, false }, new object[] { 1.5f, false },
        new object[] { 0.85f, false }, new object[] { 1f, true }, new object[] { 1.5f, true },
    };

    [Theory]
    [MemberData(nameof(Scales))]
    public void Tool_buttons_sit_in_the_time_row_without_covering_anything(float s, bool withAlert)
    {
        var inner = Inner(s, withAlert);
        var r = MusicLayout.Compute(inner, s, lyrics: true);
        var tools = new[] { ("back", r.NudgeBack), ("forward", r.NudgeForward), ("other", r.OtherLyrics) };
        var others = new List<(string, RectangleF)>
        {
            ("elapsed", r.Elapsed), ("total", r.Total), ("barHit", r.BarHit), ("art", r.Art), ("prev", r.Previous),
            ("play", r.PlayPause), ("next", r.Next), ("lyricsBtn", r.LyricsButton), ("column", r.LyricsColumn),
        };
        foreach (var (name, t) in tools)
        {
            Assert.True(t.Width > 0 && t.Height > 0, $"{name} vacío");
            Assert.True(t.Left >= inner.Left - 0.01f && t.Right <= inner.Right + 0.01f, $"{name} se sale a lo ancho");
            Assert.True(t.Top >= inner.Top - 0.01f && t.Bottom <= inner.Bottom + 0.01f, $"{name} se sale a lo alto: {t} en {inner}");
            foreach (var (on, o) in others)
            {
                var x = RectangleF.Intersect(t, o);
                Assert.True(x.Width < 0.5f || x.Height < 0.5f, $"{name} pisa {on}");
            }
        }
        Assert.True(r.NudgeBack.Right <= r.NudgeForward.Left && r.NudgeForward.Right <= r.OtherLyrics.Left);
        Assert.True(r.OtherLyrics.Width >= 80f * s, "«LRCLIB 12/15» tiene que caber");
        var notice = r.LyricsNotice(nudge: true);
        Assert.True(notice.Right <= r.NudgeBack.Left && notice.Left >= r.Elapsed.Right, $"aviso {notice}");
        Assert.True(r.LyricsNotice(nudge: false).Right > notice.Right, "sin desfase, el aviso ocupa también su hueco");
    }

    [Fact]
    public void Tool_buttons_do_not_exist_outside_the_lyrics_view_or_when_they_do_not_fit()
    {
        var normal = MusicLayout.Compute(Inner(1f), 1f, lyrics: false);
        Assert.True(normal.NudgeBack.IsEmpty && normal.NudgeForward.IsEmpty && normal.OtherLyrics.IsEmpty);
        Assert.True(normal.LyricsNotice(true).IsEmpty);

        var narrow = MusicLayout.Compute(new RectangleF(0, 0, 220, 98), 1f, lyrics: true);
        Assert.True(narrow.OtherLyrics.IsEmpty, "sin sitio no se ponen encima del tiempo transcurrido");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void HitTest_finds_the_tool_buttons_only_when_they_are_shown(float s)
    {
        var inner = Inner(s);
        var r = MusicLayout.Compute(inner, s, lyrics: true);
        const LyricsTools all = LyricsTools.Nudge | LyricsTools.Other;

        Assert.Equal(MusicControl.NudgeBack, MusicLayout.HitTest(C(r.NudgeBack), inner, s, true, all).Control);
        Assert.Equal(MusicControl.NudgeForward, MusicLayout.HitTest(C(r.NudgeForward), inner, s, true, all).Control);
        Assert.Equal(MusicControl.OtherLyrics, MusicLayout.HitTest(C(r.OtherLyrics), inner, s, true, all).Control);

        // Letra plana: sin desfase, pero «Otra letra» sí.
        Assert.Equal(MusicControl.None, MusicLayout.HitTest(C(r.NudgeBack), inner, s, true, LyricsTools.Other).Control);
        Assert.Equal(MusicControl.OtherLyrics, MusicLayout.HitTest(C(r.OtherLyrics), inner, s, true, LyricsTools.Other).Control);
        // Sin herramientas (buscando, sin letra) o sin la vista de letra: nada.
        Assert.Equal(MusicControl.None, MusicLayout.HitTest(C(r.OtherLyrics), inner, s, true).Control);
        Assert.Equal(MusicControl.None, MusicLayout.HitTest(C(r.OtherLyrics), inner, s, false, all).Control);
        // Los de siempre siguen donde estaban.
        Assert.Equal(MusicControl.Lyrics, MusicLayout.HitTest(C(r.LyricsButton), inner, s, true, all).Control);
        Assert.Equal(MusicControl.Progress, MusicLayout.HitTest(C(r.Bar), inner, s, true, all).Control);
        Assert.Equal(MusicControl.LyricLine, MusicLayout.HitTest(C(r.LyricLine(1)), inner, s, true, all).Control);
    }

    // ---------------------------------------------------------------- textos

    [Theory]
    [InlineData("LRCLIB", -1, 0, false, "LRCLIB")]
    [InlineData("LRCLIB", 0, 3, false, "LRCLIB 1/3")]
    [InlineData("LRCLIB", 1, 3, false, "LRCLIB 2/3")]
    [InlineData("LRCLIB", 2, 3, true, "LRCLIB …")]
    [InlineData(null, -1, 0, false, "Otra letra")]
    [InlineData("LRCLIB", -1, 2, false, "LRCLIB")]   // la que se ve ya no está entre los candidatos
    public void Source_label_shows_the_source_and_the_position(string? source, int index, int count, bool loading, string expected) =>
        Assert.Equal(expected, MusicFormat.SourceLabel(source, index, count, loading, "Otra letra"));

    [Fact]
    public void Offset_text_has_sign_one_decimal_and_the_culture_separator()
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        var en = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("+1,5 s", MusicFormat.Offset(1.5, es));
        Assert.Equal("−0,5 s", MusicFormat.Offset(-0.5, es));
        Assert.Equal("+10.0 s", MusicFormat.Offset(10, en));
        Assert.Equal("0 s", MusicFormat.Offset(0, en));
    }

    [Fact]
    public void Lyrics_view_texts_exist_in_spanish_and_english()
    {
        var es = Localization.Get("es");
        var en = Localization.Get("en");
        Assert.Equal("Letra sin sincronizar", es.NotchLyricsUnsynced);
        Assert.Equal("Otra letra", es.NotchLyricsOther);
        Assert.Equal("−0,5 s", es.NotchLyricsEarlier);
        Assert.Equal("+0,5 s", es.NotchLyricsLater);
        Assert.Equal("−0.5 s", en.NotchLyricsEarlier);
        Assert.NotEqual(en.NotchLyricsUnsynced, es.NotchLyricsUnsynced);
        Assert.NotEqual(en.NotchLyricsOther, es.NotchLyricsOther);
        Assert.NotEqual(en.NotchLyricsOffsetFmt, es.NotchLyricsOffsetFmt);
        Assert.Contains("{0}", es.NotchLyricsOffsetFmt);
        Assert.Contains("{0}", en.NotchLyricsOffsetFmt);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("nl")]
    public void Nudge_buttons_use_the_same_decimal_separator_as_the_offset_notice(string lang)
    {
        // Textos en inglés con cultura de coma: botones y aviso con el mismo separador.
        var s = Localization.Get(lang);
        Assert.Equal("−0,5 s", s.NotchLyricsEarlier);
        Assert.Equal("+0,5 s", s.NotchLyricsLater);
        Assert.Contains(",", MusicFormat.Offset(1.5, s.Culture));

        // «system»: textos ingleses con la cultura del SO.
        var sys = Localization.Get("en") with { Culture = System.Globalization.CultureInfo.GetCultureInfo("es-ES") };
        Assert.Equal("+0,5 s", sys.NotchLyricsLater);
    }
}
