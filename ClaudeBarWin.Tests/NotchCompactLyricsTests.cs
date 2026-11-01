using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Letra compacta en el notch pequeño: cómo la pinta <see cref="NotchContentPainter"/> (ranura izquierda con carátula o
/// «♪», línea centrada que nunca pisa las ranuras, anillo de 5 h a la derecha: atenuado en reposo, entero y con el % con
/// el ratón encima o el aviso de cuota, sin que la línea salte) y sus ajustes (por defecto, de ida y vuelta, filas en
/// Ajustes → Notch y textos es/en).
/// </summary>
public class NotchCompactLyricsTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static readonly RealUsage Quota = new()
    {
        FiveHour = new UsageWindow(42, DateTimeOffset.Now.AddHours(2)),
        SevenDay = new UsageWindow(60, DateTimeOffset.Now.AddDays(3)),
    };

    internal static byte[] SolidPng(Color c)
    {
        using var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(c);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    internal static NowPlaying Song(byte[]? art) => new("Canción", "Artista", "Disco", true, TimeSpan.FromSeconds(11),
        TimeSpan.FromSeconds(200), T0, art, Color.FromArgb(255, 80, 200, 255), "Spotify.exe");

    /// <summary>Pinta el reposo con la letra compacta; devuelve la imagen, la pastilla y los rectángulos de la geometría.</summary>
    internal static (Bitmap Bmp, RectangleF Pill, CompactLyricsRects R) Paint(CompactLyricsView v, byte[]? art, float scale = 1f,
        float extra = 0f, RealUsage? quota = null)
    {
        var size = CompactLyrics.Size(extra, scale);
        var surface = new Size((int)Math.Ceiling(700 * scale), (int)Math.Ceiling(200 * scale));
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var painter = new NotchContentPainter(Localization.Get("es")))
            painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, scale, NotchMode.Idle, null,
                Array.Empty<SessionState>(), quota ?? Quota, 0f, 1f,
                Music: new MusicView(Song(art), TimeSpan.Zero, IdleFace: true, Compact: v)));
        var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
        return (bmp, pill, CompactLyrics.Compute(pill, scale, v.Slots));
    }

    private static bool Lit(Color c) => c.A > 200 && (c.R > 90 || c.G > 90 || c.B > 90);

    private static int LitIn(Bitmap bmp, RectangleF r)
    {
        int n = 0;
        for (int x = (int)r.Left; x < (int)r.Right; x++)
            for (int y = (int)r.Top; y < (int)r.Bottom; y++)
                if (Lit(bmp.GetPixel(x, y))) n++;
        return n;
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Art_slot_shows_the_mini_art_and_the_ring_sits_on_the_right(float scale)
    {
        var (bmp, pill, r) = Paint(new CompactLyricsView("Stay stocked up, real shit", QuotaEmphasis: 1f), SolidPng(Color.Red), scale);
        using (bmp)
        {
            var c = bmp.GetPixel((int)(r.Left.X + r.Left.Width / 2), (int)(r.Left.Y + r.Left.Height / 2));
            Assert.True(c.R > 200 && c.G < 60, $"mini carátula {c}");
            Assert.True(LitIn(bmp, r.Right) > 0, "anillo de cuota");
            Assert.True(LitIn(bmp, r.Text) > 20, "línea");
            Assert.Equal(0, bmp.GetPixel((int)pill.Right + 12, 2).A);   // nada fuera de la pastilla
        }
    }

    [Fact]
    public void Without_art_or_with_the_setting_off_the_left_slot_is_a_muted_note()
    {
        foreach (var (art, setting) in new[] { ((byte[]?)null, true), (SolidPng(Color.Red), false) })
        {
            var (bmp, _, r) = Paint(new CompactLyricsView("Hola", Art: setting), art);
            using (bmp)
            {
                int note = 0, red = 0;
                for (int x = (int)r.Left.Left; x < (int)r.Left.Right; x++)
                    for (int y = (int)r.Left.Top; y < (int)r.Left.Bottom; y++)
                    {
                        var c = bmp.GetPixel(x, y);
                        if (c.R > 200 && c.G < 60) red++;
                        if (Lit(c)) note++;
                    }
                Assert.Equal(0, red);
                Assert.True(note > 6, $"nota «♪» ({note} px)");
            }
        }
    }

    [Fact]
    public void Without_the_quota_slot_there_is_no_ring()
    {
        var (bmp, pill, _) = Paint(new CompactLyricsView("Hola", Quota: false), SolidPng(Color.Red));
        using (bmp)
        {
            // Donde iría el anillo no hay nada (la línea corta va centrada lejos de ahí).
            var ring = new RectangleF(pill.Right - 30f, pill.Top + 8f, 18f, 18f);
            Assert.Equal(0, LitIn(bmp, ring));
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void A_very_long_line_is_trimmed_and_never_paints_over_the_slots(bool art, bool quota)
    {
        string longLine = string.Concat(Enumerable.Repeat("muy larga ", 30));
        var v = new CompactLyricsView(longLine, "La anterior también bastante larga", 0.6f, 0.5f, 2, -2, art, quota);
        var (bmp, pill, r) = Paint(v, null, 1f, CompactLyrics.ExtraMax, Quota);
        using (bmp)
        {
            // Franja entre la ranura izquierda y el hueco de la línea, y entre el hueco y el anillo: vacías.
            var gapL = RectangleF.FromLTRB(r.Left.Right + 1f, pill.Top, r.Text.Left - 1f, pill.Bottom);
            Assert.Equal(0, LitIn(bmp, gapL));
            var gapR = RectangleF.FromLTRB(r.Text.Right + 1f, pill.Top, (quota ? r.Right.Left : pill.Right) - 1f, pill.Bottom);
            Assert.Equal(0, LitIn(bmp, gapR));
            Assert.True(LitIn(bmp, r.Text) > 50);
        }
    }

    [Fact]
    public void Measured_line_drives_the_extra_width()
    {
        using var painter = new NotchContentPainter(Localization.Get("es"));
        var both = new CompactLyricsSlots(true, true);
        float shortW = painter.MeasureCompactLine("Hola", 1f);
        float longW = painter.MeasureCompactLine("Stay stocked up, real shit, all night long, every day", 1f);
        Assert.True(shortW > 0 && longW > shortW);
        Assert.Equal(0f, CompactLyrics.Extra(shortW, both));
        Assert.True(CompactLyrics.Extra(longW, both) > 0f);
        Assert.Equal(painter.MeasureCompactLine("Hola", 1.5f), shortW, 0);   // px lógicos: no depende de la escala
    }

    // ---------------------------------------------------------------- anillo de la cuota (29-oct)

    private static int Max(Color c) => Math.Max(c.R, Math.Max(c.G, c.B));

    /// <summary>Lo más brillante y la caja de lo que se ve (algún canal &gt; <paramref name="ink"/>) dentro de <paramref name="r"/>.</summary>
    private static (Color Brightest, Rectangle Box) Ink(Bitmap bmp, RectangleF r, int ink = 8)
    {
        Color best = Color.Black;
        int l = int.MaxValue, t = int.MaxValue, rr = int.MinValue, b = int.MinValue;
        for (int x = (int)r.Left; x < (int)Math.Ceiling(r.Right); x++)
            for (int y = (int)r.Top; y < (int)Math.Ceiling(r.Bottom); y++)
            {
                var c = bmp.GetPixel(x, y);
                if (Max(c) <= ink) continue;
                if (Max(c) > Max(best)) best = c;
                l = Math.Min(l, x); t = Math.Min(t, y); rr = Math.Max(rr, x); b = Math.Max(b, y);
            }
        return (best, l > rr ? Rectangle.Empty : Rectangle.FromLTRB(l, t, rr + 1, b + 1));
    }

    /// <summary>¿Hay tinta en la columna <paramref name="x"/> de la pastilla?</summary>
    private static bool InkAt(Bitmap bmp, RectangleF pill, int x)
    {
        for (int y = (int)pill.Top + 2; y < (int)pill.Bottom - 2; y++)
            if (Max(bmp.GetPixel(x, y)) > 40) return true;
        return false;
    }

    private static RealUsage Usage(double fiveHour) => new()
    {
        FiveHour = new UsageWindow(fiveHour, DateTimeOffset.Now.AddHours(2)),
        SevenDay = new UsageWindow(60, DateTimeOffset.Now.AddDays(3)),
    };

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Ring_at_rest_is_smaller_and_fainter_than_on_hover_and_stays_centered(float scale)
    {
        var (rest, _, r) = Paint(new CompactLyricsView("Hola"), null, scale);
        var (hover, _, _) = Paint(new CompactLyricsView("Hola", QuotaEmphasis: 1f, QuotaReveal: 1f), null, scale);
        using (rest)
        using (hover)
        {
            var slot = RectangleF.Inflate(r.Right, 2f * scale, 2f * scale);
            var a = Ink(rest, slot);
            var b = Ink(hover, slot);
            Assert.True(a.Box.Width > 0 && Max(a.Brightest) > 60, $"atenuado, pero se ve ({a.Brightest})");
            Assert.True(a.Box.Width <= b.Box.Width - 4f * scale, $"más pequeño: {a.Box} frente a {b.Box}");
            Assert.True(Max(a.Brightest) <= Max(b.Brightest) * 0.6f, $"más tenue: {a.Brightest} frente a {b.Brightest}");
            Assert.True(Max(b.Brightest) > 200, "entero con el ratón encima");
            float ca = a.Box.Left + a.Box.Width / 2f, cb = b.Box.Left + b.Box.Width / 2f;
            Assert.True(Math.Abs(ca - cb) <= 1.5f, "centrado en su ranura en los dos tamaños");
        }
    }

    [Fact]
    public void Quota_alert_keeps_the_ring_full_with_its_threshold_color()
    {
        var hot = Usage(85);
        var ring = new CompactQuotaRing();
        ring.Set(hover: false, alert: CompactLyrics.QuotaAlert(hot.FiveHour!.UtilizationPct), snap: true);
        var m = new CompactLyricsMotion();
        m.Jump(0, "Hola");
        var view = NotchForm.CompactViewFor(m, sameLyrics: true, art: true, quota: true, ring);

        var (alert, _, r) = Paint(view, null, quota: hot);
        var (dimmed, _, _) = Paint(view with { QuotaEmphasis = 0f }, null, quota: hot);
        using (alert)
        using (dimmed)
        {
            var full = Ink(alert, r.Right).Brightest;
            Assert.True(full.R > 190 && full.R > full.G && full.G > full.B, $"ámbar entero ({full})");
            // Aun atenuado (si no hubiera aviso) mantiene el color de umbral: ámbar, no gris.
            var faint = Ink(dimmed, r.Right).Brightest;
            Assert.True(faint.R < full.R * 0.6f && faint.R > faint.G && faint.G > faint.B, $"ámbar atenuado ({faint})");
            Assert.Equal(0, LitIn(alert, RectangleF.FromLTRB(r.Right.Left - 45f, r.Right.Top, r.Right.Left - 3f, r.Right.Bottom)));
        }
    }

    [Fact]
    public void Hover_shows_the_pct_left_of_the_ring_and_rest_does_not()
    {
        var (rest, pill, r) = Paint(new CompactLyricsView("Hola"), null);
        var (hover, _, _) = Paint(new CompactLyricsView("Hola", QuotaEmphasis: 1f, QuotaReveal: 1f), null);
        using (rest)
        using (hover)
        {
            var pct = RectangleF.FromLTRB(r.Right.Left - 45f, pill.Top + 4f, r.Right.Left - 3f, pill.Bottom - 4f);
            Assert.Equal(0, LitIn(rest, pct));
            Assert.True(LitIn(hover, pct) > 10, "«42 %» junto al anillo");
        }
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void A_short_line_does_not_move_when_the_pct_appears(float reveal)
    {
        var (rest, pill, r) = Paint(new CompactLyricsView("Hola"), null);
        var (hover, _, _) = Paint(new CompactLyricsView("Hola", QuotaEmphasis: reveal, QuotaReveal: reveal), null);
        using (rest)
        using (hover)
        {
            int lit = 0;
            for (int x = (int)r.Text.Left; x < (int)r.Text.Right - 50; x++)
                for (int y = (int)pill.Top; y < (int)pill.Bottom; y++)
                {
                    var a = rest.GetPixel(x, y);
                    Assert.Equal(a, hover.GetPixel(x, y));   // ni un píxel de la línea se mueve
                    if (Lit(a)) lit++;
                }
            Assert.True(lit > 20, "la línea está ahí");
        }
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.6f)]
    [InlineData(1f)]
    public void A_long_line_makes_room_and_never_overlaps_the_pct(float reveal)
    {
        string longLine = string.Concat(Enumerable.Repeat("muy larga ", 30));
        var (both, pill, r) = Paint(new CompactLyricsView(longLine, QuotaEmphasis: 1f, QuotaReveal: reveal), null);
        var (pctOnly, _, _) = Paint(new CompactLyricsView("", QuotaEmphasis: 1f, QuotaReveal: reveal), null);
        using (both)
        using (pctOnly)
        {
            // Donde empieza el % (sin la línea): desde ahí a la izquierda, al menos 5 columnas libres antes de la línea.
            int pctLeft = -1;
            for (int x = (int)r.Text.Left + 40; x < (int)r.Right.Left; x++)
                if (InkAt(pctOnly, pill, x)) { pctLeft = x; break; }
            Assert.True(pctLeft > 0, "se ve algo del %");
            for (int x = pctLeft - 5; x < pctLeft; x++)
                Assert.False(InkAt(both, pill, x), $"la línea pisa el % en x = {x} (empieza en {pctLeft})");
            Assert.Contains(Enumerable.Range(pctLeft - 24, 18), x => InkAt(both, pill, x));   // y llega hasta cerca
        }
    }

    // ---------------------------------------------------------------- ajustes

    private const int X = 16, W = 308;
    private const string Compact = "toggle:NotchCompactLyrics", Art = "toggle:NotchCompactLyricsArt",
        QuotaKey = "toggle:NotchCompactLyricsQuota";

    private static int Draw(AppConfig cfg, Dictionary<string, Rectangle> rects, bool draw = true)
    {
        using var bmp = new Bitmap(W + X * 2, 1600);
        using var g = Graphics.FromImage(bmp);
        return DashboardSettingsView.Draw(g, draw, X, 0, W, cfg, Localization.Get("es"), Theme.Dark, Typography.Body,
            Typography.Caption, rects);
    }

    [Fact]
    public void Settings_default_on_and_roundtrip()
    {
        var c = new AppConfig();
        Assert.True(c.NotchCompactLyrics);
        Assert.True(c.NotchCompactLyricsArt);
        Assert.True(c.NotchCompactLyricsQuota);
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(new AppConfig
        {
            NotchCompactLyrics = false, NotchCompactLyricsArt = false, NotchCompactLyricsQuota = false,
        }))!;
        Assert.False(back.NotchCompactLyrics);
        Assert.False(back.NotchCompactLyricsArt);
        Assert.False(back.NotchCompactLyricsQuota);
        var old = JsonSerializer.Deserialize<AppConfig>("""{ "NotchEnabled": true }""")!;
        Assert.True(old.NotchCompactLyrics && old.NotchCompactLyricsArt && old.NotchCompactLyricsQuota);
    }

    [Fact]
    public void Rows_sit_under_lyrics_and_the_slots_need_the_compact_lyrics_on()
    {
        var cfg = new AppConfig { NotchEnabled = true };
        var on = new Dictionary<string, Rectangle>();
        int yOn = Draw(cfg, on);
        foreach (var k in new[] { Compact, Art, QuotaKey }) Assert.Contains(k, on.Keys);
        Assert.True(on[Compact].Top > on["toggle:NotchLyrics"].Top && on[Art].Top > on[Compact].Top
            && on[QuotaKey].Top > on[Art].Top && on["toggle:NotchTasks"].Top > on[QuotaKey].Top);
        Assert.True(on[Compact].Left > on["toggle:NotchLyrics"].Left, "un nivel más dentro que «Letras»");
        Assert.True(on[Art].Left > on[Compact].Left, "las ranuras, otro más");
        Assert.Equal(Draw(cfg, new(), draw: false), yOn);   // medir == pintar

        // Con la letra pequeña apagada, sus ranuras atenuadas e inertes; sin letras, también la letra pequeña.
        var noCompact = new Dictionary<string, Rectangle>();
        Assert.Equal(yOn, Draw(new AppConfig { NotchEnabled = true, NotchCompactLyrics = false }, noCompact));
        Assert.Contains(Compact, noCompact.Keys);
        Assert.DoesNotContain(Art, noCompact.Keys);
        Assert.DoesNotContain(QuotaKey, noCompact.Keys);
        var noLyrics = new Dictionary<string, Rectangle>();
        Assert.Equal(yOn, Draw(new AppConfig { NotchEnabled = true, NotchLyrics = false }, noLyrics));
        foreach (var k in new[] { Compact, Art, QuotaKey }) Assert.DoesNotContain(k, noLyrics.Keys);
    }

    [Fact]
    public void Actions_toggle_each_setting()
    {
        var c = new AppConfig();
        foreach (var k in new[] { Compact, Art, QuotaKey }) DashboardSettingsView.ActionFor(k)!(c);
        Assert.False(c.NotchCompactLyrics || c.NotchCompactLyricsArt || c.NotchCompactLyricsQuota);
        foreach (var k in new[] { Compact, Art, QuotaKey }) DashboardSettingsView.ActionFor(k)!(c);
        Assert.True(c.NotchCompactLyrics && c.NotchCompactLyricsArt && c.NotchCompactLyricsQuota);
    }

    [Fact]
    public void Texts_exist_in_spanish_and_english()
    {
        var es = Localization.Get("es");
        var en = Localization.Get("en");
        Assert.Equal("Letra en el notch pequeño", es.NotchCompactLyrics);
        Assert.Equal("Carátula en la letra", es.NotchCompactLyricsArt);
        Assert.Equal("Cuota en la letra", es.NotchCompactLyricsQuota);
        Assert.Equal("Lyrics in the small notch", en.NotchCompactLyrics);
        foreach (var (e, s) in new[] { (en.NotchCompactLyrics, es.NotchCompactLyrics),
                     (en.NotchCompactLyricsSubtitle, es.NotchCompactLyricsSubtitle),
                     (en.NotchCompactLyricsArt, es.NotchCompactLyricsArt), (en.NotchCompactLyricsQuota, es.NotchCompactLyricsQuota) })
        {
            Assert.False(string.IsNullOrWhiteSpace(e));
            Assert.NotEqual(e, s);
        }
    }
}
