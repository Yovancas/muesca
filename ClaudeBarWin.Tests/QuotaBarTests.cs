using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

public class QuotaBarTests
{
    [Fact]
    public void MarkerX_returns_midpoint_at_fifty_pct()
    {
        Assert.Equal(50, QuotaBarGeometry.MarkerX(0, 100, 50));
    }

    [Fact]
    public void MarkerX_returns_start_at_zero_and_end_at_hundred()
    {
        Assert.Equal(0, QuotaBarGeometry.MarkerX(0, 100, 0));
        Assert.Equal(100, QuotaBarGeometry.MarkerX(0, 100, 100));
    }

    [Fact]
    public void MarkerX_respects_x_offset()
    {
        Assert.Equal(20, QuotaBarGeometry.MarkerX(20, 100, 0));
        Assert.Equal(70, QuotaBarGeometry.MarkerX(20, 100, 50));
        Assert.Equal(120, QuotaBarGeometry.MarkerX(20, 100, 100));
    }

    [Fact]
    public void MarkerX_clamps_to_bar_bounds()
    {
        // pct fuera de [0,100] se recorta a los extremos de la barra [x, x+w].
        Assert.Equal(0, QuotaBarGeometry.MarkerX(0, 100, -30));
        Assert.Equal(100, QuotaBarGeometry.MarkerX(0, 100, 150));
    }

    [Fact]
    public void TickX_matches_markerX_for_same_pct()
    {
        // El tick de umbral comparte la misma proyección que el marker.
        Assert.Equal(QuotaBarGeometry.MarkerX(0, 100, 70), QuotaBarGeometry.TickX(0, 100, 70));
        Assert.Equal(QuotaBarGeometry.MarkerX(10, 200, 90), QuotaBarGeometry.TickX(10, 200, 90));
    }

    [Fact]
    public void Threshold_ticks_fall_within_bar_and_are_ordered()
    {
        // Warn (70%) y Critical (90%) por defecto: ambos ticks dentro del ancho y ordenados warn < crit.
        const int x = 20, w = 200;
        int warn = QuotaBarGeometry.TickX(x, w, 70);
        int crit = QuotaBarGeometry.TickX(x, w, 90);

        Assert.InRange(warn, x, x + w);
        Assert.InRange(crit, x, x + w);
        Assert.True(warn < crit, "el tick de warn debe quedar a la izquierda del de critical");
    }

    // ================= T3a: el track de una fila compacta se corta antes del texto right-aligned =================

    [Fact]
    public void CompactTrackWidth_stops_before_the_right_text()
    {
        // Fila de 300px con texto de 40px y gap de 8: el track ocupa 252px (no tacha el %).
        Assert.Equal(252, QuotaBarGeometry.CompactTrackWidth(300, 40, 8));
    }

    [Fact]
    public void CompactTrackWidth_is_never_negative()
    {
        // Texto más ancho que la fila (locales largos / panel estrecho): el track desaparece, no revienta.
        Assert.Equal(0, QuotaBarGeometry.CompactTrackWidth(30, 40, 8));
        Assert.Equal(0, QuotaBarGeometry.CompactTrackWidth(48, 40, 8));
    }

    // ================= T3c: el triángulo del pace no invade la fila del label =================

    [Fact]
    public void PaceTriangle_never_rises_above_the_marker_overshoot()
    {
        // El ▾ antiguo subía hasta barY-5 e invadía los descendentes de la fila etiqueta/%; el clamp lo
        // deja a la altura donde ya arrancaba la línea vertical (barY - MarkerOvershoot).
        const int barY = 50;
        var tri = QuotaBarGeometry.PaceTriangle(mx: 100, barY: barY);
        Assert.All(tri, p => Assert.True(p.Y >= barY - QuotaBarGeometry.MarkerOvershoot,
            $"punto del ▾ en y={p.Y}, por encima del clamp {barY - QuotaBarGeometry.MarkerOvershoot}"));
    }

    [Fact]
    public void PaceTriangle_points_down_into_the_bar_centered_on_marker()
    {
        var tri = QuotaBarGeometry.PaceTriangle(mx: 100, barY: 50);
        var tip = tri.OrderByDescending(p => p.Y).First();
        Assert.Equal(100, tip.X);                            // la punta sigue señalando el marker
        Assert.True(tip.Y > tri.Min(p => p.Y), "el ▾ debe conservar altura (seguir siendo un triángulo)");
    }

    [Fact]
    public void Draw_pace_marker_stays_out_of_the_label_row()
    {
        // Pinta una barra con pace al 50% y comprueba que la banda [barY-5, barY-3] — la que el ▾
        // antiguo invadía — queda libre de píxeles TextMuted alrededor del marker.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(50, DateTimeOffset.UtcNow);
        var pace = new PaceResult("5h", 50, 1.0, 50.0, null, null, false, PaceStatus.Ok);
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", win, pace, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        int barY = y0 + 22;                                   // la barra arranca 22px bajo la fila del label
        int mx = QuotaBarGeometry.MarkerX(x, w, 50);
        var mk = Theme.Dark.TextMuted;
        for (int py = barY - 5; py <= barY - 3; py++)
            for (int px = mx - 6; px <= mx + 6; px++)
            {
                var p = bmp.GetPixel(px, py);
                bool muted = Math.Abs(p.R - mk.R) <= 8 && Math.Abs(p.G - mk.G) <= 8 && Math.Abs(p.B - mk.B) <= 8;
                Assert.False(muted, $"píxel del marker en ({px},{py}) invade la fila del label (barY={barY})");
            }
    }

    // ================= T3b: ticks de umbral visibles sobre el tramo vacío del track =================

    [Fact]
    public void Draw_threshold_ticks_are_visible_on_the_empty_track()
    {
        // Con 0% de uso, el tramo de los ticks es track puro: el tick debe contrastar ≥3:1 (WCAG 1.4.11)
        // con el Track. Con theme.Separator (≈ Track en los 3 temas) el tick era invisible (~1:1).
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(0, DateTimeOffset.UtcNow);
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", win, pace: null, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        int barY = y0 + 22;
        int wx = QuotaBarGeometry.TickX(x, w, cfg.WarnThresholdPct);
        bool visible = false;
        for (int px = wx - 1; px <= wx + 1 && !visible; px++)
        {
            var p = bmp.GetPixel(px, barY + 5);               // mitad de la barra (BarH=11)
            if (ColorMath.ContrastRatio(p, Theme.Dark.Track) >= 3.0) visible = true;
        }
        Assert.True(visible, "el tick de warn no se distingue del track (necesita TickOnTrack, ≥3:1)");
    }

    // ================= T8c: la línea de reset se elide al ancho de la fila =================

    [Fact]
    public void Draw_reset_line_stays_within_the_row_width()
    {
        // "resetea en 1d 5h · jue 02:12" es más ancha que una fila estrecha (locales largos / panel
        // angosto): debe elidirse, no seguir de largo más allá de x+w.
        using var bmp = new Bitmap(420, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(40, DateTimeOffset.UtcNow.AddHours(29)); // countdown largo "1d 5h"
        var cfg = new AppConfig();
        var s = Localization.Get("es");

        const int x = 16, y0 = 10, w = 100;
        QuotaBar.Draw(g, draw: true, "5h", win, pace: null, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        // Banda de la línea de reset: tras la fila label/% (22) y la barra (BarH 11 + 3).
        int resetY = y0 + 22 + 11 + 3;
        var bg = Theme.Dark.Background;
        for (int px = x + w + 2; px < bmp.Width; px++)
            for (int py = resetY; py < resetY + 14; py++)
            {
                var p = bmp.GetPixel(px, py);
                Assert.True(p.R == bg.R && p.G == bg.G && p.B == bg.B,
                    $"píxel pintado en ({px},{py}), fuera del ancho w={w}: la línea de reset no se elide");
            }
    }

    // ================= T6b + F4(g2): el % crítico POR % REAL usa la variante de TEXTO (CriticalText) =================

    [Fact]
    public void Draw_critical_percent_text_uses_critical_text_variant()
    {
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        // AA en escala de grises (sin ClearType subpixel): cada píxel del texto queda EXACTAMENTE en la
        // recta fondo→color del brush, así el muestreo por tolerancia es determinista.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        // F4 (g2): el % sigue el % REAL, no el pace. 95% (≥ crit 90) ⇒ texto Critical aunque el ritmo
        // sea Ok — prueba que el color del texto lo decide el %, no el pace marker.
        var win = new UsageWindow(95, DateTimeOffset.UtcNow.AddHours(2));
        var pace = new PaceResult("5h", 95, 0.8, 60.0, null, null, false, PaceStatus.Ok);
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", win, pace, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        // La fila etiqueta/% ocupa [y0, y0+18); la barra (cuyo RELLENO sí sigue siendo Critical al 95%)
        // arranca en y0+22. En la fila de texto: ni un píxel del rojo de relleno #DC2626 (3.7:1)
        // y al menos uno del rojo de texto CriticalText (#F87171, 6.4:1).
        var fill = Theme.Dark.Critical;
        var txt = Theme.Dark.CriticalText;
        bool foundText = false;
        for (int py = y0; py < y0 + 18; py++)
            for (int px = x; px < x + w; px++)
            {
                var p = bmp.GetPixel(px, py);
                bool isFill = Math.Abs(p.R - fill.R) <= 10 && Math.Abs(p.G - fill.G) <= 10 && Math.Abs(p.B - fill.B) <= 10;
                Assert.False(isFill, $"({px},{py}): el % crítico sigue pintado con el rojo de RELLENO #DC2626");
                if (Math.Abs(p.R - txt.R) <= 10 && Math.Abs(p.G - txt.G) <= 10 && Math.Abs(p.B - txt.B) <= 10)
                    foundText = true;
            }
        Assert.True(foundText, "el glifo/% crítico no usa CriticalText (#F87171) en tema oscuro");
    }

    // ================= F4 (v0.3.9 g2): relleno por % REAL, no por pace =================

    // Cuenta los píxeles de relleno (no-fondo, no-texto) en la banda de la barra y devuelve el color
    // medio de los que están dentro del tramo lleno. Aísla el relleno del track y de los ticks/marker.
    private static Color SampleFillColor(Bitmap bmp, Theme theme, int x, int barY, int fillW)
    {
        // Centro vertical de la barra (BarH=11 ⇒ ~+5) y mitad izquierda del relleno (lejos del marker/ticks).
        long r = 0, gg = 0, b = 0; int n = 0;
        var track = theme.Track;
        for (int px = x + 2; px < x + Math.Max(3, fillW - 2); px++)
        {
            var p = bmp.GetPixel(px, barY + 5);
            bool isTrack = Math.Abs(p.R - track.R) <= 6 && Math.Abs(p.G - track.G) <= 6 && Math.Abs(p.B - track.B) <= 6;
            if (isTrack) continue;
            r += p.R; gg += p.G; b += p.B; n++;
        }
        return n == 0 ? track : Color.FromArgb((int)(r / n), (int)(gg / n), (int)(b / n));
    }

    private static Color DrawAndSampleFill(double util, PaceStatus paceStatus)
    {
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(util, DateTimeOffset.UtcNow.AddHours(2));
        var pace = new PaceResult("5h", util, paceStatus == PaceStatus.Ok ? 0.5 : 1.5, 40.0, null, null, false, paceStatus);
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", win, pace, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        int barY = y0 + 22;
        int fillW = (int)Math.Round(w * Math.Min(util / 100.0, 1.0));
        return SampleFillColor(bmp, Theme.Dark, x, barY, fillW);
    }

    [Fact]
    public void Fill_color_tracks_percent_not_pace_57_red_84_green_inverted_is_fixed()
    {
        // El caso de la auditoría: 57% con pace CRÍTICO vs 84% con pace OK. Antes el relleno seguía el
        // pace ⇒ 57% ROJA, 84% VERDE (color invertido respecto a la longitud). Ahora va por % real:
        // el relleno del 84% debe ser MÁS cálido (más R, menos G) que el del 57% pese a sus paces opuestos.
        var fill57crit = DrawAndSampleFill(57, PaceStatus.Critical);
        var fill84ok = DrawAndSampleFill(84, PaceStatus.Ok);

        Assert.True(fill84ok.R >= fill57crit.R,
            $"84% (R={fill84ok.R}) debería ser ≥ cálido que 57% (R={fill57crit.R}) — el relleno sigue al pace, no al %");
        Assert.True(fill84ok.G <= fill57crit.G,
            $"84% (G={fill84ok.G}) debería tener ≤ verde que 57% (G={fill57crit.G}) — color invertido por pace");
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(20, 50)]
    [InlineData(50, 80)]
    [InlineData(80, 100)]
    public void Fill_color_is_monotonic_with_percent_regardless_of_pace(double lo, double hi)
    {
        // Monotonía: a más %, relleno no menos cálido (R no decrece) — comparando con paces OPUESTOS para
        // demostrar que el pace ya no influye. El % bajo lleva pace Critical; el alto, pace Ok.
        var fillLo = DrawAndSampleFill(lo, PaceStatus.Critical);
        var fillHi = DrawAndSampleFill(hi, PaceStatus.Ok);
        Assert.True(fillHi.R >= fillLo.R - 4,
            $"relleno a {hi}% (R={fillHi.R}) no debería ser menos cálido que a {lo}% (R={fillLo.R})");
    }

    [Fact]
    public void Fill_color_matches_RiskColor_of_real_percent()
    {
        // El relleno es EXACTAMENTE RiskColor(%real): pace Critical no lo altera al 50%.
        var cfg = new AppConfig();
        var expected = ColorMath.RiskColor(50, Theme.Dark, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
        var sampled = DrawAndSampleFill(50, PaceStatus.Critical);
        Assert.True(Math.Abs(sampled.R - expected.R) <= 12 && Math.Abs(sampled.G - expected.G) <= 12
            && Math.Abs(sampled.B - expected.B) <= 12,
            $"relleno {sampled} ≠ RiskColor(50) {expected} — el % no decide el color del relleno");
    }

    // ================= F4 (v0.3.9 g2): el pace-marker ▾ recibe el color de PaceStatus =================

    private static bool MarkerHasColor(double util, double idealPct, PaceStatus paceStatus, Color want)
    {
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(util, DateTimeOffset.UtcNow.AddHours(2));
        var pace = new PaceResult("5h", util, 1.0, idealPct, null, null, false, paceStatus);
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", win, pace, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        int barY = y0 + 22;
        int mx = QuotaBarGeometry.MarkerX(x, w, idealPct);
        // El marcador sobresale por encima de la barra (MarkerOvershoot): muestrear esa banda evita
        // confundirlo con el relleno. Tolerancia amplia por el AA de la línea de 2px.
        for (int py = barY - QuotaBarGeometry.MarkerOvershoot; py <= barY; py++)
            for (int px = mx - 3; px <= mx + 3; px++)
            {
                var p = bmp.GetPixel(px, py);
                if (Math.Abs(p.R - want.R) <= 24 && Math.Abs(p.G - want.G) <= 24 && Math.Abs(p.B - want.B) <= 24)
                    return true;
            }
        return false;
    }

    [Fact]
    public void Pace_marker_is_colored_by_pace_status_critical()
    {
        // Marcador en idealPct distinto del relleno (idealPct=20, util=60 ⇒ marker sobre tramo lleno):
        // pace Critical ⇒ el ▾ usa CriticalText, NO el neutro TextMuted de antes.
        Assert.True(MarkerHasColor(60, 20, PaceStatus.Critical, Theme.Dark.CriticalText),
            "el pace-marker no recibe el color CriticalText de PaceStatus.Critical");
        Assert.False(MarkerHasColor(60, 20, PaceStatus.Critical, Theme.Dark.TextMuted),
            "el pace-marker sigue pintado con el TextMuted neutro de antes");
    }

    [Fact]
    public void Pace_marker_is_colored_by_pace_status_over()
    {
        // pace Over ⇒ WarnText en el marcador (idealPct=70 sobre track vacío al util=10).
        Assert.True(MarkerHasColor(10, 70, PaceStatus.Over, Theme.Dark.WarnText),
            "el pace-marker no recibe el color WarnText de PaceStatus.Over");
    }

    [Fact]
    public void Pace_marker_is_colored_by_pace_status_ok()
    {
        // pace Ok ⇒ Ok en el marcador (idealPct=70 sobre track vacío al util=10).
        Assert.True(MarkerHasColor(10, 70, PaceStatus.Ok, Theme.Dark.Ok),
            "el pace-marker no recibe el color Ok de PaceStatus.Ok");
    }

    // ================= T10: el % right-aligned queda FLUSH con el borde derecho (§3 #16) =================

    [Fact]
    public void Draw_percent_value_is_flush_with_the_right_edge()
    {
        // Antes: medir con el StringFormat por defecto (padding ~1/6 em por lado) y pintar igual dejaba
        // la tinta del % varios px antes de x+w, a una distancia distinta que las demás filas (columna
        // "dentada"). Con la medida central tipográfica la tinta termina pegada a x+w (≤3px de bearing).
        using var bmp = new Bitmap(420, 120);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(87, DateTimeOffset.UtcNow.AddHours(2));
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", win, pace: null, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        // Banda de la fila etiqueta/% (la barra arranca en y0+22): la tinta más a la derecha es el %.
        int ink = TextMetricsTests.RightmostInk(bmp, Theme.Dark.Background, y0, y0 + 18);
        Assert.True(ink >= 0, "no se pintó la fila etiqueta/%");
        Assert.InRange(ink, x + w - 3, x + w);
    }
}
