using System.Drawing;
using System.Drawing.Imaging;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary><see cref="NotchContentPainter"/>: pinta todos los modos sin lanzar, solo dentro de la pastilla,
/// y el texto de «Trabajando» sale de la sesión activa más reciente.</summary>
public class NotchContentPainterTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static SessionState S(string id, SessionPhase phase, string project, string? tool, int ageSec) => new()
    {
        SessionId = id, Cwd = @"C:\p\" + project, ProjectName = project, Phase = phase,
        LastTool = tool, LastActivityUtc = T0.AddSeconds(-ageSec),
    };

    private static readonly RealUsage Quota = new()
    {
        FiveHour = new UsageWindow(42, DateTimeOffset.Now.AddHours(2)),
        SevenDay = new UsageWindow(96, DateTimeOffset.Now.AddDays(3)),
    };

    /// <summary>Beta del 28-oct: con la vista previa a 160×34, el bloque «7d %» se pintaba encima del anillo.</summary>
    [Theory]
    [InlineData(160f, 1f, false)]
    [InlineData(160f, 1.5f, false)]
    [InlineData(340f, 1f, true)]
    [InlineData(340f * 1.5f, 1.5f, true)]
    public void Seven_day_only_shows_in_the_preview_when_it_fits(float pillWidth, float scale, bool fits)
        => Assert.Equal(fits, NotchContentPainter.SevenDayFits(pillWidth, scale, NotchMotion.RingDiameterPeek));

    /// <summary>Pedido del 28-oct: reposo = «◯ 5h  ◯ 7d» juntos y centrados; al acercar el ratón, con sus %.
    /// El grupo (con los textos más anchos posibles) cabe en la pastilla con margen a ambos lados.</summary>
    [Theory]
    [InlineData(NotchMode.Idle, false, 1f)]
    [InlineData(NotchMode.Idle, false, 1.5f)]
    [InlineData(NotchMode.Peek, true, 1f)]
    [InlineData(NotchMode.Peek, true, 1.5f)]
    public void Quota_pair_fits_its_pill(NotchMode mode, bool withPct, float s)
    {
        var pill = NotchLayout.TargetSize(mode, s, 0f);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        float w = painter.MeasureQuotaPair(s, withPct, "100 %", "100 %");
        Assert.True(w + 2 * 10f * s <= pill.Width, $"grupo {w} no cabe en {pill.Width}");
        Assert.True(w >= pill.Width * 0.55f, $"grupo {w} demasiado suelto en {pill.Width}");
    }

    public static IEnumerable<object[]> AllModes() =>
        Enum.GetValues<NotchMode>().SelectMany(m => new[] { new object[] { m, 1f }, new object[] { m, 1.5f } });

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Every_mode_paints_inside_the_pill_only(NotchMode mode, float scale)
    {
        var sessions = new[]
        {
            S("a", SessionPhase.Processing, "claudebar", "Bash", 1),
            S("b", SessionPhase.WaitingForApproval, "web", null, 5),
            S("c", SessionPhase.WaitingForInput, "api", null, 9),
            S("d", SessionPhase.Idle, "docs", null, 20),
            S("e", SessionPhase.Compacting, "infra", null, 30),
        };
        NotchAlert? alert = mode switch
        {
            NotchMode.Permission => new(NotchAlertKind.Permission, "b", "web", "Bash: rm -rf build", T0, null),
            NotchMode.Completed or NotchMode.Expanded => new(NotchAlertKind.Completed, "c", "api", null, T0, TimeSpan.FromSeconds(4)),
            NotchMode.QuotaHud => new(NotchAlertKind.QuotaHud, "quota:5h", "5h", "80%", T0, TimeSpan.FromSeconds(1.6)),
            NotchMode.QuotaAlert => new(NotchAlertKind.QuotaAlert, "quota:7d", "7d", "96%", T0, TimeSpan.FromSeconds(4.5)),
            _ => null,
        };
        var size = NotchLayout.TargetSize(mode, scale, 40);
        var surface = new Size((int)Math.Ceiling(700 * scale), (int)Math.Ceiling(200 * scale));
        using var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));

        var drawn = painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, scale, mode, alert,
            sessions, Quota, 0.3f, 1f));

        if (mode == NotchMode.Hidden)
        {
            Assert.True(drawn.IsEmpty);
            return;
        }
        var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
        Assert.True(drawn.Contains(pill));                                     // forma + orejas
        Assert.True(drawn.Width <= pill.Width + 2 * 8 * scale + 0.01f);
        // Centro de la pastilla, arriba: opaco. Fuera de la pastilla: transparente.
        Assert.Equal(255, bmp.GetPixel(surface.Width / 2, 0).A);
        Assert.Equal(0, bmp.GetPixel(2, surface.Height - 2).A);
        Assert.Equal(0, bmp.GetPixel((int)drawn.Left - 3, 1).A);
    }

    private static NotchFrame ShadowFrame() => new(310, 60, 1f, NotchMode.QuotaAlert,
        new NotchAlert(NotchAlertKind.QuotaAlert, "quota:5h", "5h", "96%", T0, null),
        Array.Empty<SessionState>(), Quota, 0, 1, ShadowAlpha: 1f, HaloAlpha: 1f);

    [Fact]
    public void Shadow_and_halo_extend_the_painted_area_and_stay_translucent()
    {
        var surface = new Size(700, 200);
        using var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("en"));

        var drawn = painter.PaintShadow(g, surface, ShadowFrame());
        var pill = NotchGeometry.PillRect(310, 60, surface);
        Assert.True(drawn.Bottom >= pill.Bottom + NotchMotion.ShadowBlurRadius);
        Assert.True(drawn.Left <= pill.Left - NotchMotion.ShadowBlurRadius);
        int a = bmp.GetPixel(350, (int)pill.Bottom + 3).A;                   // justo debajo de la pastilla
        Assert.InRange(a, 1, (int)(255 * 0.7f));
        Assert.Equal(0, bmp.GetPixel(2, 198).A);
    }

    [Fact]
    public void The_pill_surface_has_no_shadow_outside_the_hit_zone()
    {
        // La sombra va en su propia ventana (WS_EX_TRANSPARENT): en la de la pastilla, cualquier píxel con
        // alfa > 0 fuera de la zona de clic se comería los clics de otras apps.
        var surface = new Size(700, 200);
        using var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("en"));

        var drawn = painter.Paint(g, surface, ShadowFrame());
        var pill = NotchGeometry.PillRect(310, 60, surface);
        Assert.True(drawn.Bottom <= pill.Bottom + 0.01f);
        for (int y = (int)pill.Bottom + 1; y < (int)pill.Bottom + 1 + NotchMotion.ShadowBlurRadius; y++)
            Assert.Equal(0, bmp.GetPixel(350, y).A);
        Assert.Equal(0, bmp.GetPixel((int)pill.Left - 6, 30).A);
        Assert.True(painter.PaintShadow(g, surface, ShadowFrame() with { ShadowAlpha = 0f, HaloAlpha = 0f }).IsEmpty);
    }

    [Fact]
    public void ExpandedRows_counts_the_rows_that_fit_up_to_the_cap()
    {
        var full = NotchLayout.TargetSize(NotchMode.Expanded, 1f, 0);
        int rows = NotchContentPainter.ExpandedRows(full.Height, 1f, withAlert: false);
        Assert.InRange(rows, 1, NotchContentPainter.MaxListedSessions);
        Assert.True(NotchContentPainter.ExpandedRows(full.Height, 1f, withAlert: true) <= rows);
        Assert.Equal(NotchContentPainter.MaxListedSessions, NotchContentPainter.ExpandedRows(1000f, 1f, false));
        Assert.Equal(2, NotchContentPainter.ExpandedRows(26f + 2 * 22f, 1f, false));        // 12 + 2 filas + 14
        Assert.Equal(2, NotchContentPainter.ExpandedRows((26f + 2 * 22f) * 1.5f, 1.5f, false));
        Assert.Equal(1, NotchContentPainter.ExpandedRows(10f, 1f, true));                    // nunca menos de 1
    }

    [Fact]
    public void Minimal_takes_the_quota_color_from_80_percent()
    {
        var surface = new Size(700, 200);
        Color Paint(double pct)
        {
            using var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            using var painter = new NotchContentPainter(Localization.Get("en"));
            var q = new RealUsage { FiveHour = new UsageWindow(pct, null) };
            painter.Paint(g, surface, new NotchFrame(44, 4, 1f, NotchMode.Minimal, null,
                Array.Empty<SessionState>(), q, 0, 1));
            return bmp.GetPixel(350, 1);
        }
        Assert.Equal(Color.Black.ToArgb(), Paint(50).ToArgb());
        Assert.Equal(Theme.Dark.Warn.ToArgb(), Paint(85).ToArgb());
        Assert.Equal(Theme.Dark.Critical.ToArgb(), Paint(97).ToArgb());
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.51f, 0.5f)]
    [InlineData(-3f, 0f)]
    public void Content_alpha_is_quantized_to_32_steps(float a, float e) =>
        Assert.Equal(e, NotchContentPainter.Quantize(a), 4);

    [Fact]
    public void WorkingText_uses_most_recent_active_session_and_counts_the_rest()
    {
        var s = Localization.Get("es");
        var list = new[]
        {
            S("a", SessionPhase.Processing, "viejo", "Read", 60),
            S("b", SessionPhase.Processing, "nuevo", "Edit", 2),
            S("c", SessionPhase.WaitingForInput, "otro", "Bash", 1),   // no activa: no cuenta
        };
        Assert.Equal("nuevo · Edit  +1", NotchContentPainter.WorkingText(list, s));
        Assert.Equal("", NotchContentPainter.WorkingText(new[] { list[2] }, s));
        Assert.Equal("infra · " + s.SessionPhaseCompacting,
            NotchContentPainter.WorkingText(new[] { S("e", SessionPhase.Compacting, "infra", "Bash", 1) }, s));
    }

    [Fact]
    public void MeasureWorkingExtra_shrinks_for_short_text_and_grows_with_long_names()
    {
        using var painter = new NotchContentPainter(Localization.Get("en"));
        Assert.Equal(0f, painter.MeasureWorkingExtra(Array.Empty<SessionState>(), 1f));
        // Texto corto: pide MENOS ancho (sin hueco negro entre el texto y el %), en saltos de 16 px para no bailar.
        float shortExtra = painter.MeasureWorkingExtra(new[] { S("a", SessionPhase.Processing, "web", "Bash", 1) }, 1f);
        Assert.True(shortExtra < 0f);
        Assert.Equal(0f, shortExtra % NotchContentPainter.WorkingWidthStep);
        float w = NotchLayout.TargetSize(NotchMode.Working, 1f, shortExtra).Width;
        Assert.True(w - NotchContentPainter.WorkingChromeWidth < 80f, $"queda hueco: {w}");
        float extra = painter.MeasureWorkingExtra(
            new[] { S("a", SessionPhase.Processing, new string('x', 80), "Bash", 1) }, 1f);
        Assert.True(extra > 0f);
        // En px LÓGICOS: no depende de la escala (salvo redondeos del hinting).
        float extra15 = painter.MeasureWorkingExtra(
            new[] { S("a", SessionPhase.Processing, new string('x', 80), "Bash", 1) }, 1.5f);
        Assert.InRange(extra15, extra * 0.9f, extra * 1.1f);
    }

    // ---------------------------------------------------------------- páginas del expandido (F5 · B2)

    private static readonly NotchPage[] Four = { NotchPage.Claude, NotchPage.Music, NotchPage.Tasks, NotchPage.Shelf };

    private static (Bitmap Bmp, RectangleF Pill) PaintPage(NotchPagesView? pages, float scale = 1f, bool alert = false)
    {
        var sessions = new[]
        {
            S("a", SessionPhase.Processing, "claudebar", "Bash", 1),
            S("b", SessionPhase.WaitingForInput, "web", null, 5),
        };
        var page = pages?.Current ?? NotchPage.Claude;
        var size = NotchLayout.PageSize(page, new PageRows(sessions.Length, 0, 0), alert, pages is { Pages.Count: > 1 }, scale);
        var surface = new Size((int)Math.Ceiling(700 * scale), (int)Math.Ceiling(200 * scale));
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        NotchAlert? a = alert ? new(NotchAlertKind.Completed, "b", "web", null, T0, TimeSpan.FromSeconds(4)) : null;
        painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, scale, NotchMode.Expanded, a,
            sessions, Quota, 0.3f, 1f, Pages: pages));
        return (bmp, NotchGeometry.PillRect(size.Width, size.Height, surface));
    }

    public static IEnumerable<object[]> PagesAndScales() =>
        Four.SelectMany(p => new[] { new object[] { p, 1f }, new object[] { p, 1.5f } });

    [Theory]
    [MemberData(nameof(PagesAndScales))]
    public void Every_page_paints_inside_the_pill_with_its_dots(NotchPage page, float scale)
    {
        int i = Array.IndexOf(Four, page);
        var (bmp, pill) = PaintPage(new NotchPagesView(Four, page, null, 1f, 0f, 0f, i), scale);
        using (bmp)
        {
            Assert.Equal(255, bmp.GetPixel(bmp.Width / 2, 0).A);
            Assert.Equal(0, bmp.GetPixel(2, bmp.Height - 2).A);
            Assert.Equal(0, bmp.GetPixel(bmp.Width / 2, (int)pill.Bottom + 6).A);
            // La píldora indicadora (blanca) está sobre el punto de la página actual; los otros puntos, atenuados.
            var on = NotchGeometry.PageDotCenter(pill, scale, Four.Length, i);
            var off = NotchGeometry.PageDotCenter(pill, scale, Four.Length, (i + 1) % Four.Length);
            int bright = bmp.GetPixel((int)on.X, (int)on.Y).R, dim = bmp.GetPixel((int)off.X, (int)off.Y).R;
            Assert.True(bright > 180, $"indicador {bright}");
            Assert.InRange(dim, 20, bright - 60);
        }
    }

    [Fact]
    public void Mid_crossfade_paints_both_pages_without_throwing()
    {
        var (bmp, pill) = PaintPage(new NotchPagesView(Four, NotchPage.Music, NotchPage.Claude, 0.5f, 4f, -4f, 0.5f),
            alert: true);
        using (bmp) Assert.Equal(255, bmp.GetPixel(bmp.Width / 2, 0).A);
    }

    /// <summary>Brillo máximo (canal R) en la zona de clic de una flecha.</summary>
    private static int ArrowBrightness(Bitmap bmp, RectangleF pill, PageArrow side, float scale = 1f)
    {
        var r = Rectangle.Round(NotchGeometry.PageArrowHitRect(pill, scale, Four.Length, side));
        int max = 0;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
                max = Math.Max(max, bmp.GetPixel(x, y).R);
        return max;
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Arrows_paint_dim_beside_the_dots_and_the_dead_one_is_gone(float scale)
    {
        var shown = new PageArrowLook(1f, 0f, 1f, 0f);
        var arrows = new PageArrowsView(new PageArrowLook(0f, 0f, 1f, 0f), shown);   // primera página: sin anterior
        var (bmp, pill) = PaintPage(new NotchPagesView(Four, NotchPage.Claude, null, 1f, 0f, 0f, 0, arrows), scale);
        using (bmp)
        {
            Assert.Equal(0, ArrowBrightness(bmp, pill, PageArrow.Previous, scale));
            Assert.InRange(ArrowBrightness(bmp, pill, PageArrow.Next, scale), 30, 140);   // tenue, como los puntos
        }
        var (none, pill2) = PaintPage(new NotchPagesView(Four, NotchPage.Claude, null, 1f, 0f, 0f, 0), scale);
        using (none) Assert.Equal(0, ArrowBrightness(none, pill2, PageArrow.Next, scale));         // sin vista: sin flechas
    }

    [Fact]
    public void Hovered_arrow_turns_to_the_accent()
    {
        var hover = new PageArrowsView(new PageArrowLook(1f, 0f, 1f, 0f), new PageArrowLook(1f, 1f, 1.25f, 2f));
        var (bmp, pill) = PaintPage(new NotchPagesView(Four, NotchPage.Music, null, 1f, 0f, 0f, 1, hover));
        using (bmp)
        {
            var r = Rectangle.Round(NotchGeometry.PageArrowHitRect(pill, 1f, Four.Length, PageArrow.Next));
            bool accent = false;
            for (int y = r.Top; y < r.Bottom && !accent; y++)
                for (int x = r.Left; x < r.Right && !accent; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    accent = BrandPixels.IsAccent(c);   // el acento (violeta) sobre negro, no gris
                }
            Assert.True(accent);
            Assert.True(ArrowBrightness(bmp, pill, PageArrow.Next) > ArrowBrightness(bmp, pill, PageArrow.Previous));
        }
    }

    [Fact]
    public void A_single_page_paints_no_dots()
    {
        // Sin carrusel (o con una sola página) el expandido es el de siempre: ni fila de puntos ni indicador.
        foreach (var view in new[] { null, new NotchPagesView(new[] { NotchPage.Claude }, NotchPage.Claude, null, 1f, 0f, 0f, 0f) })
        {
            var (bmp, pill) = PaintPage(view);
            using (bmp)
            {
                var c = NotchGeometry.PageDotCenter(pill, 1f, 4, 1);
                // Bajo la lista de sesiones (2 filas) no hay nada: ahí irían los puntos centrados.
                for (int x = (int)pill.Left + 240; x < (int)pill.Right - 30; x += 3)
                    Assert.Equal(Color.Black.ToArgb(), bmp.GetPixel(x, (int)c.Y).ToArgb());
            }
        }
    }

    [Fact]
    public void ExpandedRows_leaves_room_for_the_dots()
    {
        float h = NotchLayout.PageSize(NotchPage.Claude, new PageRows(6, 0, 0), false, true, 1f).Height;
        Assert.Equal(6, NotchContentPainter.ExpandedRows(h, 1f, withAlert: false, withDots: true));
        float capped = NotchLayout.PageSize(NotchPage.Claude, new PageRows(6, 0, 0), true, true, 1f).Height;   // 184
        Assert.Equal(5, NotchContentPainter.ExpandedRows(capped, 1f, withAlert: true, withDots: true));
        Assert.Equal(6, NotchContentPainter.ExpandedRows(capped, 1f, withAlert: true, withDots: false));
    }

    [Fact]
    public void SessionColumn_is_the_right_column_of_the_Claude_page()
    {
        var pill = NotchGeometry.PillRect(480, 152, new Size(700, 200));
        var col = NotchContentPainter.SessionColumn(pill, 1f, withAlert: false, withDots: true);
        Assert.True(col.Left > pill.Left + pill.Width / 3f);
        Assert.True(col.Right <= pill.Right);
        Assert.True(col.Bottom <= pill.Bottom - (NotchLayout.ExpandedPadBottom + NotchLayout.PageDotsRow) + 0.01f);
        var withAlert = NotchContentPainter.SessionColumn(pill, 1f, withAlert: true, withDots: true);
        Assert.True(withAlert.Top > col.Top);
    }

    [Fact]
    public void Renderer_ignores_null_window_without_throwing()
    {
        using var r = new LayeredGdiRenderer(new NotchContentPainter(Localization.Get("en")));
        r.Render(IntPtr.Zero, IntPtr.Zero, Point.Empty, new Size(700, 200),
            new NotchFrame(150, 30, 1f, NotchMode.Idle, null, Array.Empty<SessionState>(), null, 0, 1));
        Assert.Equal(Size.Empty, r.SurfaceSize);
    }

    // ---------------------------------------------------------------- página Música (F5 · B3)

    private static byte[] SolidPng(Color c)
    {
        using var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(c);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static NowPlaying Song(byte[]? art = null, bool playing = true) => new("Una canción con un título bastante largo",
        "Artista, Otro & Más", "Disco", playing, TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(225), T0, art,
        Color.FromArgb(255, 80, 200, 255), "Spotify.exe");

    private static readonly LyricLine[] Lines =
    {
        new(TimeSpan.FromSeconds(70), "Primera línea"), new(TimeSpan.FromSeconds(80), "Segunda línea, la de ahora"),
        new(TimeSpan.FromSeconds(90), "Tercera"), new(TimeSpan.FromSeconds(100), LrcParser.PauseText),
    };

    private static (Bitmap Bmp, RectangleF Pill) PaintMusic(MusicView music, float scale = 1f, bool alert = false)
    {
        var view = new NotchPagesView(Four, NotchPage.Music, null, 1f, 0f, 0f, 1);
        var size = NotchLayout.PageSize(NotchPage.Music, default, alert, true, scale);
        var surface = new Size((int)Math.Ceiling(700 * scale), (int)Math.Ceiling(200 * scale));
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        NotchAlert? a = alert ? new(NotchAlertKind.Completed, "b", "web", null, T0, TimeSpan.FromSeconds(4)) : null;
        painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, scale, NotchMode.Expanded, a,
            Array.Empty<SessionState>(), Quota, 0f, 1f, Pages: view, Music: music));
        return (bmp, NotchGeometry.PillRect(size.Width, size.Height, surface));
    }

    public static IEnumerable<object[]> MusicViews()
    {
        var art = SolidPng(Color.Red);
        foreach (var s in new[] { 1f, 1.5f })
        {
            yield return new object[] { new MusicView(null, TimeSpan.Zero), s };
            yield return new object[] { new MusicView(Song(), TimeSpan.FromSeconds(83)), s };
            yield return new object[] { new MusicView(Song(art), TimeSpan.FromSeconds(83), SplitBlend: 1f,
                LyricsState: LyricsState.Found, Lines: Lines, Line: 1), s };
            yield return new object[] { new MusicView(Song(art, playing: false), TimeSpan.FromSeconds(83), SplitBlend: 0.5f,
                LyricsState: LyricsState.Found, Lines: Lines, Line: 2, PreviousLine: 1, LineBlend: 0.4f,
                Hover: MusicControl.PlayPause, HoverIntensity: 0.6f, Pressed: MusicControl.Next), s };
            yield return new object[] { new MusicView(Song(art), TimeSpan.FromSeconds(83), SplitBlend: 1f,
                LyricsState: LyricsState.Loading), s };
            yield return new object[] { new MusicView(Song(), TimeSpan.FromSeconds(83), SplitBlend: 1f,
                LyricsState: LyricsState.NotFound, Hover: MusicControl.LyricLine, HoverLine: 1, HoverIntensity: 1f), s };
            yield return new object[] { new MusicView(Song(), TimeSpan.FromSeconds(1), SplitBlend: 1f,
                LyricsState: LyricsState.Found, Lines: Lines, Line: -1, Hover: MusicControl.Progress, HoverIntensity: 1f), s };
            // Letras v2: botones del desfase y «Otra letra» (con hover y pulsado), aviso del desfase y letra plana.
            yield return new object[] { new MusicView(Song(art), TimeSpan.FromSeconds(83), SplitBlend: 1f,
                LyricsState: LyricsState.Found, Lines: Lines, Line: 1, Hover: MusicControl.NudgeForward, HoverIntensity: 1f,
                Pressed: MusicControl.OtherLyrics, Tools: LyricsTools.Nudge | LyricsTools.Other, SourceLabel: "LRCLIB 2/3",
                Notice: "Desfase +1,5 s"), s };
            yield return new object[] { new MusicView(Song(), TimeSpan.FromSeconds(83), SplitBlend: 1f,
                LyricsState: LyricsState.Plain, Lines: Lines, Line: 2, PreviousLine: 1, LineBlend: 0.5f,
                Tools: LyricsTools.Other, SourceLabel: "LRCLIB …", SourceBusy: true, Notice: "Letra sin sincronizar"), s };
            yield return new object[] { new MusicView(Song(), TimeSpan.FromSeconds(83), SplitBlend: 1f,
                LyricsState: LyricsState.Plain, Lines: Array.Empty<LyricLine>()), s };
        }
    }

    [Fact]
    public void Lyrics_tool_buttons_are_painted_where_the_layout_puts_them_and_only_when_shown()
    {
        var music = new MusicView(Song(), TimeSpan.FromSeconds(83), SplitBlend: 1f, LyricsState: LyricsState.Found,
            Lines: Lines, Line: 1, Hover: MusicControl.NudgeBack, HoverIntensity: 1f);
        var (with, pill) = PaintMusic(music with { Tools = LyricsTools.Nudge | LyricsTools.Other, SourceLabel = "LRCLIB 1/3" });
        var (without, _) = PaintMusic(music);
        using (with)
        using (without)
        {
            var inner = NotchContentPainter.PageInner(pill, 1f, withAlert: false, withDots: true);
            var r = MusicLayout.Compute(inner, 1f, lyrics: true);
            foreach (var b in new[] { r.NudgeBack, r.NudgeForward, r.OtherLyrics })
            {
                // Borde izquierdo de la cápsula, a media altura (sin texto encima): más claro con el botón que sin él.
                int x = (int)(b.Left + b.Height / 2f), y = (int)(b.Top + b.Height / 2f);
                Assert.True(with.GetPixel(x, y).R > without.GetPixel(x, y).R, $"no se ve el botón en {b}");
            }
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Lyrics_button_paints_the_icon_centered_in_its_capsule(float scale)
    {
        var (bmp, pill) = PaintMusic(new MusicView(Song(), TimeSpan.FromSeconds(83)), scale);
        using (bmp)
        {
            var inner = NotchContentPainter.PageInner(pill, scale, withAlert: false, withDots: true);
            var b = MusicLayout.Compute(inner, scale, lyrics: false).LyricsButton;
            // Centro del icono = bocadillo macizo entre las dos comillas: color de primer plano.
            var c = bmp.GetPixel((int)(b.Left + b.Width / 2f), (int)(b.Top + b.Height / 2f));
            Assert.True(c.R > 200 && c.G > 200 && c.B > 200, $"centro del icono {c}");
            // Borde de la cápsula, lejos del icono: solo el relleno translúcido (oscuro).
            var edge = bmp.GetPixel((int)(b.Left + b.Height / 2f), (int)(b.Top + b.Height / 2f));
            Assert.True(edge.R < 80, $"borde de la cápsula {edge}");
        }
    }

    [Fact]
    public void Searching_lyrics_moves_letters_above_the_text_and_holds_still_with_reduce_motion()
    {
        var music = new MusicView(Song(), TimeSpan.FromSeconds(83), SplitBlend: 1f, LyricsState: LyricsState.Loading);
        static long Sum(Bitmap bmp, RectangleF area)
        {
            long sum = 0;
            for (int y = (int)area.Top; y < (int)area.Bottom; y++)
                for (int x = (int)area.Left; x < (int)area.Right; x++) sum += bmp.GetPixel(x, y).R;
            return sum;
        }
        var (a, pill) = PaintMusic(music with { SearchSeconds = 0.3 });
        var (b, _) = PaintMusic(music with { SearchSeconds = 1.2 });
        var (stillA, _) = PaintMusic(music with { SearchSeconds = 0.3, ReduceMotion = true });
        var (stillB, _) = PaintMusic(music with { SearchSeconds = 5.7, ReduceMotion = true });
        using (a) using (b) using (stillA) using (stillB)
        {
            var inner = NotchContentPainter.PageInner(pill, 1f, withAlert: false, withDots: true);
            var r = MusicLayout.Compute(inner, 1f, lyrics: true);
            var above = RectangleF.FromLTRB(r.LyricsColumn.Left, r.LyricsColumn.Top, r.LyricsColumn.Right, r.LyricLine(1).Top);
            Assert.True(Sum(a, above) > 0, "no se ven letras subiendo");
            Assert.NotEqual(Sum(a, above), Sum(b, above));
            Assert.Equal(Sum(stillA, above), Sum(stillB, above));
            Assert.True(Sum(a, r.LyricLine(1)) > 0, "falta «Buscando la letra…»");
        }
    }

    [Theory]
    [MemberData(nameof(MusicViews))]
    public void Music_page_paints_every_state_inside_the_pill(MusicView music, float scale)
    {
        var (bmp, pill) = PaintMusic(music, scale);
        using (bmp)
        {
            Assert.Equal(255, bmp.GetPixel(bmp.Width / 2, 0).A);
            Assert.Equal(0, bmp.GetPixel(2, bmp.Height - 2).A);
            Assert.Equal(0, bmp.GetPixel(bmp.Width / 2, (int)pill.Bottom + 6).A);
        }
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(1.5f, true)]
    public void Music_page_draws_the_album_art_where_the_layout_puts_it(float scale, bool alert)
    {
        var (bmp, pill) = PaintMusic(new MusicView(Song(SolidPng(Color.Red)), TimeSpan.FromSeconds(83)), scale, alert);
        using (bmp)
        {
            var inner = NotchContentPainter.PageInner(pill, scale, alert, withDots: true);
            var art = MusicLayout.Compute(inner, scale, lyrics: false).Art;
            var c = bmp.GetPixel((int)(art.X + art.Width / 2), (int)(art.Y + art.Height / 2));
            Assert.True(c.R > 200 && c.G < 60 && c.B < 60, $"carátula {c}");
            // La barra de progreso lleva el acento de la carátula hasta la posición (83 de 225 s ≈ 37 %).
            var bar = MusicLayout.Compute(inner, scale, lyrics: false).Bar;
            var filled = bmp.GetPixel((int)(bar.Left + bar.Width * 0.2f), (int)(bar.Top + bar.Height / 2));
            var empty = bmp.GetPixel((int)(bar.Left + bar.Width * 0.8f), (int)(bar.Top + bar.Height / 2));
            Assert.True(filled.B > 200 && filled.R < 140, $"relleno {filled}");
            Assert.True(empty.B < 120, $"pista {empty}");
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Idle_music_face_shows_the_mini_art_inside_its_pill(float scale)
    {
        var size = NotchLayout.IdleMusicSize(scale);
        var surface = new Size((int)Math.Ceiling(700 * scale), (int)Math.Ceiling(200 * scale));
        using var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, scale, NotchMode.Idle, null,
            Array.Empty<SessionState>(), Quota, 0f, 1f, Music: new MusicView(Song(SolidPng(Color.Red)), TimeSpan.Zero, IdleFace: true)));
        var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
        var art = NotchContentPainter.IdleMusicArt(pill, scale);
        Assert.True(art.Left >= pill.Left && art.Bottom <= pill.Bottom);
        var c = bmp.GetPixel((int)(art.X + art.Width / 2), (int)(art.Y + art.Height / 2));
        Assert.True(c.R > 200 && c.G < 60, $"mini carátula {c}");
        Assert.Equal(0, bmp.GetPixel((int)pill.Right + 12, 2).A);
    }

    [Fact]
    public void Idle_without_the_music_face_still_paints_the_rings()
    {
        // Sin IdleFace el reposo es el de siempre: «◯ 5h  ◯ 7d».
        var size = NotchLayout.TargetSize(NotchMode.Idle, 1f, 0f);
        var surface = new Size(700, 200);
        using var a = new Bitmap(700, 200, PixelFormat.Format32bppPArgb);
        using var b = new Bitmap(700, 200, PixelFormat.Format32bppPArgb);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        using (var g = Graphics.FromImage(a))
            painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, 1f, NotchMode.Idle, null,
                Array.Empty<SessionState>(), Quota, 0f, 1f));
        using (var g = Graphics.FromImage(b))
            painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, 1f, NotchMode.Idle, null,
                Array.Empty<SessionState>(), Quota, 0f, 1f, Music: new MusicView(Song(), TimeSpan.Zero)));
        for (int x = 280; x < 420; x += 3)
            for (int y = 0; y < 30; y += 3)
                Assert.Equal(a.GetPixel(x, y), b.GetPixel(x, y));
    }

    // ---------------------------------------------------------------- página Bandeja (F5 · B4)

    private static ShelfItem Shelf(ShelfKind kind, string value, string display) => new(Guid.NewGuid(), kind, value, display, T0);

    private static readonly ShelfItem[] ShelfItems =
    {
        Shelf(ShelfKind.File, @"C:\docs\informe.pdf", "informe.pdf"),
        Shelf(ShelfKind.Folder, @"C:\docs\fotos", "fotos"),
        Shelf(ShelfKind.Link, "https://example.com/una/ruta/larga", "example.com/una/ruta/larga"),
        Shelf(ShelfKind.Text, "Una nota suelta bastante larga para que se corte con puntos suspensivos al final", "Una nota suelta"),
        Shelf(ShelfKind.File, @"C:\docs\borrado.txt", "borrado.txt"),
        Shelf(ShelfKind.File, @"C:\docs\otro.txt", "otro.txt"),
        Shelf(ShelfKind.File, @"C:\docs\mas.txt", "mas.txt"),
    };

    private static (Bitmap Bmp, RectangleF Pill) PaintShelf(ShelfView? shelf, float scale = 1f, bool alert = false)
    {
        var view = new NotchPagesView(Four, NotchPage.Shelf, null, 1f, 0f, 0f, 3);
        var size = NotchLayout.PageSize(NotchPage.Shelf, new PageRows(0, 0, shelf?.Items.Count ?? 0), alert, true, scale);
        var surface = new Size((int)Math.Ceiling(700 * scale), (int)Math.Ceiling(200 * scale));
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        NotchAlert? a = alert ? new(NotchAlertKind.Completed, "b", "web", null, T0, TimeSpan.FromSeconds(4)) : null;
        painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, scale, NotchMode.Expanded, a,
            Array.Empty<SessionState>(), Quota, 0f, 1f, Pages: view, Shelf: shelf));
        return (bmp, NotchGeometry.PillRect(size.Width, size.Height, surface));
    }

    private static ShelfView ShelfOf(int n, float scroll = 0f, IReadOnlyCollection<Guid>? selected = null, ShelfHit hover = default,
        float hoverIntensity = 0f, ShelfHit pressed = default, bool drop = false, Guid? copied = null,
        IReadOnlyList<Bitmap?>? thumbs = null, IReadOnlyList<string>? details = null)
    {
        var items = ShelfItems.Take(n).ToArray();
        return new ShelfView(items, items.Select(i => i.Display != "borrado.txt").ToArray(), selected ?? Array.Empty<Guid>(),
            scroll, hover, hoverIntensity, pressed, drop, copied, thumbs, details);
    }

    /// <summary>Miniatura de prueba: un cuadrado de <paramref name="px"/> de un solo color.</summary>
    private static Bitmap Solid(Color c, int px = 48)
    {
        var b = new Bitmap(px, px, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }

    private static readonly Bitmap RedThumb = Solid(Color.Red);

    /// <summary>Miniaturas para las <paramref name="n"/> primeras entradas: roja solo en <paramref name="red"/>.</summary>
    private static Bitmap?[] ThumbsWithRedAt(int n, int red) =>
        Enumerable.Range(0, n).Select(i => i == red ? RedThumb : null).ToArray();

    public static IEnumerable<object[]> ShelfViews()
    {
        foreach (var s in new[] { 1f, 1.5f })
        {
            yield return new object[] { null!, s, false };
            yield return new object[] { ShelfOf(0), s, false };
            yield return new object[] { ShelfOf(0, drop: true), s, true };
            yield return new object[] { ShelfOf(3, selected: new[] { ShelfItems[1].Id }, thumbs: ThumbsWithRedAt(3, 0),
                details: new[] { "2,4 MB", "Carpeta", "Enlace" }), s, false };
            // 7 tarjetas: no caben, flechas; a medio deslizar, con hover, pulsación y «Copiado».
            yield return new object[] { ShelfOf(7, scroll: ShelfLayout.CardStride * 1.5f, hover: new ShelfHit(ShelfControl.Card, 2),
                hoverIntensity: 0.7f, pressed: new ShelfHit(ShelfControl.Next), copied: ShelfItems[3].Id,
                thumbs: ThumbsWithRedAt(7, 4)), s, true };
            yield return new object[] { ShelfOf(7, scroll: ShelfLayout.ScrollFor(ShelfLayout.MaxFirst(7)),
                hover: new ShelfHit(ShelfControl.Previous), hoverIntensity: 1f), s, false };
            yield return new object[] { ShelfOf(5, selected: ShelfItems.Take(3).Select(i => i.Id).ToArray(),
                hover: new ShelfHit(ShelfControl.Clear), hoverIntensity: 1f, drop: true), s, false };
        }
    }

    [Theory]
    [MemberData(nameof(ShelfViews))]
    public void Shelf_page_paints_every_state_inside_the_pill(ShelfView? shelf, float scale, bool alert)
    {
        var (bmp, pill) = PaintShelf(shelf, scale, alert);
        using (bmp)
        {
            Assert.Equal(255, bmp.GetPixel(bmp.Width / 2, 0).A);
            Assert.Equal(0, bmp.GetPixel(2, bmp.Height - 2).A);
            Assert.Equal(0, bmp.GetPixel(bmp.Width / 2, (int)pill.Bottom + 6).A);
            Assert.Equal(0, bmp.GetPixel((int)pill.Right + 14, (int)pill.Top + 20).A);
        }
    }

    /// <summary>Geometría de la página tal como la pinta el painter (pastilla a su tamaño final).</summary>
    private static ShelfRects ShelfRectsOf(RectangleF pill, float scale, int count) =>
        ShelfLayout.Compute(NotchContentPainter.PageInner(pill, scale, withAlert: false, withDots: true), scale, count);

    private static Color At(Bitmap bmp, PointF p) => bmp.GetPixel((int)p.X, (int)p.Y);

    private static PointF Mid(RectangleF r) => new(r.Left + r.Width / 2f, r.Top + r.Height / 2f);

    /// <summary>Punto de la miniatura fuera del glifo dibujado (que va en el centro) y dentro del azulejo.</summary>
    private static PointF InThumb(RectangleF thumb, float s) => new(thumb.Left + 10f * s, thumb.Top + 10f * s);

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Selected_cards_are_highlighted_where_the_layout_puts_them(float scale)
    {
        var shelf = ShelfOf(3, selected: new[] { ShelfItems[1].Id });
        var (bmp, pill) = PaintShelf(shelf, scale);
        using (bmp)
        {
            var r = ShelfRectsOf(pill, scale, 3);
            // Margen izquierdo de la tarjeta, a media altura (fuera de la miniatura y del texto): con selección se ve el
            // relleno; sin ella, negro.
            PointF Edge(RectangleF card) => new(card.Left + 3f * scale, card.Top + card.Height / 2f);
            int sel = At(bmp, Edge(r.Card(1, 0f))).R;
            int plain = At(bmp, Edge(r.Card(0, 0f))).R;
            Assert.True(sel > plain + 12, $"seleccionada {sel} vs normal {plain}");
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void System_thumbnail_is_drawn_in_its_slot_and_others_get_the_drawn_icon(float scale)
    {
        var (bmp, pill) = PaintShelf(ShelfOf(3, thumbs: ThumbsWithRedAt(3, 0)), scale);
        using (bmp)
        {
            var r = ShelfRectsOf(pill, scale, 3);
            var photo = At(bmp, Mid(r.Thumb(r.Card(0, 0f))));
            Assert.True(photo.R > 200 && photo.G < 60 && photo.B < 60, $"miniatura {photo}");
            // La carpeta, sin miniatura: azulejo gris tenue (no negro, no rojo).
            var folder = At(bmp, InThumb(r.Thumb(r.Card(1, 0f)), scale));
            Assert.True(folder.R is > 8 and < 120 && Math.Abs(folder.R - folder.G) < 16, $"icono dibujado (gris) {folder}");
        }
    }

    /// <summary>Una entrada cuyo archivo ya no existe: sin miniatura del sistema el icono dibujado y, si la caché aún la tenía,
    /// atenuada (40 %): nunca se pinta como si siguiera ahí.</summary>
    [Fact]
    public void Missing_entry_is_dimmed()
    {
        // ShelfItems[4] es «borrado.txt» (no disponible en ShelfOf).
        var thumbs = Enumerable.Range(0, 5).Select(i => i is 0 or 4 ? RedThumb : null).ToArray();
        var (bmp, pill) = PaintShelf(ShelfOf(5, thumbs: thumbs));
        using (bmp)
        {
            var r = ShelfRectsOf(pill, 1f, 5);
            int live = At(bmp, Mid(r.Thumb(r.Card(0, 0f)))).R;
            int gone = At(bmp, Mid(r.Thumb(r.Card(4, 0f)))).R;
            Assert.True(live > 200, $"disponible {live}");
            Assert.InRange(gone, 70, 140);
        }
        // Sin miniatura no lanza: el icono genérico, atenuado.
        var (plain, _) = PaintShelf(ShelfOf(5));
        plain.Dispose();
    }

    /// <summary>El rojo más alto en <paramref name="r"/> (el glifo de una flecha es fino: no se acierta con un solo píxel).</summary>
    private static int MaxRed(Bitmap bmp, RectangleF r)
    {
        int max = 0;
        for (int x = (int)r.Left; x < (int)r.Right; x++)
            for (int y = (int)r.Top; y < (int)r.Bottom; y++)
                max = Math.Max(max, bmp.GetPixel(x, y).R);
        return max;
    }

    [Fact]
    public void Arrows_only_show_when_the_cards_overflow()
    {
        var (few, pill) = PaintShelf(ShelfOf(3));
        var (many, _) = PaintShelf(ShelfOf(7));
        using (few)
        using (many)
        {
            var r = ShelfRectsOf(pill, 1f, 7);
            // > 170 y no > 200: el glifo es un trazo de 1,5 px y el suavizado de GDI en Windows deja su máximo en ~191.
            Assert.True(MaxRed(many, r.Next) > 170, "› con 7 tarjetas");
            Assert.True(MaxRed(few, r.Next) < 10, "sin desbordar no hay flechas");
            // ‹ al principio: atenuada (no hay nada a la izquierda), pero ahí está.
            Assert.InRange(MaxRed(many, r.Previous), 40, 150);
        }
    }

    [Fact]
    public void Cards_slide_with_the_scroll_and_are_clipped_to_the_viewport()
    {
        // Miniatura roja en la sexta tarjeta (índice 5): al principio no se ve; en la última página ocupa el cuarto sitio.
        var thumbs = ThumbsWithRedAt(7, 5);
        float end = ShelfLayout.ScrollFor(ShelfLayout.MaxFirst(7)), half = ShelfLayout.CardStride / 2f;
        var (start, pill) = PaintShelf(ShelfOf(7, thumbs: thumbs));
        var (last, _) = PaintShelf(ShelfOf(7, scroll: end, thumbs: thumbs));
        var (sliding, _) = PaintShelf(ShelfOf(7, scroll: half, thumbs: thumbs));
        using (start)
        using (last)
        using (sliding)
        {
            var r = ShelfRectsOf(pill, 1f, 7);
            var slot = InThumb(r.Thumb(r.Card(5, end)), 1f);
            Assert.True(At(last, slot).R > 200, "en la última página, la sexta se ve");
            Assert.True(At(start, slot).R < 200, "al principio, en ese sitio va otra");

            // A medio deslizar, la sexta asoma por la derecha: se ve dentro del visor y se corta en su borde.
            var thumb = r.Thumb(r.Card(5, half));
            Assert.True(thumb.Left < r.Viewport.Right && thumb.Right > r.Viewport.Right + 4f);
            Assert.True(At(sliding, new PointF(r.Viewport.Right - 3f, Mid(thumb).Y)).R > 200, "dentro del visor");
            Assert.True(At(sliding, new PointF(r.Viewport.Right + 3f, Mid(thumb).Y)).R < 60, "fuera del visor, recortada");
        }
    }

    [Fact]
    public void Strip_width_is_the_page_content_width()
    {
        foreach (var s in new[] { 1f, 1.25f, 1.5f })
        {
            var pill = NotchGeometry.PillRect(NotchLayout.ExpandedWidth * s, 152 * s, new Size((int)(700 * s), (int)(200 * s)));
            var inner = NotchContentPainter.PageInner(pill, s, withAlert: false, withDots: true);
            Assert.Equal(ShelfLayout.StripWidth * s, inner.Width, 2);
        }
    }

    [Fact]
    public void Drop_target_outlines_the_page_and_the_empty_shelf_does_not()
    {
        var (on, pill) = PaintShelf(ShelfOf(0, drop: true));
        var (off, _) = PaintShelf(ShelfOf(0));
        using (on)
        using (off)
        {
            var inner = NotchContentPainter.PageInner(pill, 1f, withAlert: false, withDots: true);
            int x = (int)(inner.Left + inner.Width / 2), y = (int)Math.Round(inner.Top - 2f);
            int lit = Enumerable.Range(y - 3, 7).Max(yy => on.GetPixel(x, yy).R);
            int dark = Enumerable.Range(y - 3, 7).Max(yy => off.GetPixel(x, yy).R);
            Assert.True(lit > dark + 30, $"contorno al soltar {lit} vs sin arrastre {dark}");
        }
    }

    [Fact]
    public void PageInner_matches_the_session_column_box()
    {
        var pill = NotchGeometry.PillRect(480, 152, new Size(700, 200));
        var inner = NotchContentPainter.PageInner(pill, 1f, withAlert: true, withDots: true);
        var col = NotchContentPainter.SessionColumn(pill, 1f, withAlert: true, withDots: true);
        Assert.Equal(inner.Top, col.Top);
        Assert.Equal(inner.Bottom, col.Bottom);
        Assert.Equal(inner.Right, col.Right);
    }
}
