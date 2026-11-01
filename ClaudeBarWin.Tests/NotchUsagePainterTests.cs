using System.Drawing;
using System.Drawing.Imaging;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Pintado de la página Uso: cada estado (cargando, vacío, gasto, cuota con proyección, hover, sin Pro, en plena transición) a
/// cada escala, todo dentro de la pastilla; el chip «Pro» solo sin el Pro; el hover pinta su tooltip. Y los anillos que abren
/// la página aciertan la ventana con la geometría de lo que se pinta.
/// </summary>
public class NotchUsagePainterTests
{
    private static readonly Strings Es = Localization.Get("es");

    private static Bitmap Paint(NotchFrame f, out Size surface)
    {
        surface = NotchPlacement.Surface(f.Scale);
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Es);
        painter.Paint(g, surface, f);
        return bmp;
    }

    public static IEnumerable<object[]> States()
    {
        foreach (var s in new[] { 1f, 1.5f, 2f })
        {
            var numbers = UsageFixtures.Numbers(Es, QuotaFocus.Five);
            var spend = UsageFixtures.SpendChart(Es);
            var quota = UsageFixtures.QuotaChart(Es);
            yield return new object[] { s, null!, false };
            yield return new object[] { s, new UsageView(numbers, UsageChartBuilder.Loading(UsageChartMode.Spend, ChartRange.Day1),
                UsageChartMode.Spend, ChartRange.Day1), false };
            yield return new object[] { s, new UsageView(numbers, UsageChartBuilder.Spend(new List<HistoryBucket>(), ChartRange.Day1,
                new Dictionary<string, int>(), new HashSet<string>(), Es), UsageChartMode.Spend, ChartRange.Day1), true };
            yield return new object[] { s, new UsageView(numbers, spend, UsageChartMode.Spend, ChartRange.Day1,
                Bucket: UsageFixtures.MidBucket(spend), TipIndex: UsageFixtures.MidBucket(spend), TipAlpha: 1f), false };
            yield return new object[] { s, new UsageView(numbers, quota, UsageChartMode.Quota, ChartRange.Hours5,
                Bucket: 20.4f, TipIndex: 20, TipAlpha: 0.6f, Hover: new UsageHit(UsageControl.Range, 1), HoverIntensity: 1f), true };
            yield return new object[] { s, new UsageView(numbers, spend, UsageChartMode.Spend, ChartRange.Day1, Locked: true,
                Interactive: false), false };
            yield return new object[] { s, new UsageView(numbers, quota, UsageChartMode.Quota, ChartRange.Hours5,
                UsageMorphKind.Fade, 0.4f, Previous: spend), false };
        }
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Every_state_paints_inside_the_pill(float s, UsageView? view, bool alert)
    {
        var f = UsageFixtures.Frame(s, view, alert);
        using var bmp = Paint(f, out var surface);
        var pill = NotchGeometry.PillRect(f.Width, f.Height, surface);
        float ears = NotchMotion.EarSize(f.Width, s) + 2f;
        int painted = 0;
        for (int y = 0; y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
            {
                if (bmp.GetPixel(x, y).A == 0) continue;
                painted++;
                Assert.True(x >= pill.Left - ears && x <= pill.Right + ears && y <= pill.Bottom + 1, $"({x},{y}) fuera de {pill}");
            }
        Assert.True(painted > 1000);
    }

    [Fact]
    public void A_morph_mid_flight_paints_the_intermediate_shape()
    {
        var morph = new UsageChartMorph();
        morph.Jump(UsageFixtures.SpendChart(Es));
        morph.Go(UsageFixtures.SpendChart(Es, new HashSet<string> { "Opus" }), reduceMotion: false, TimeSpan.Zero);
        Assert.Equal(UsageMorphKind.Morph, morph.Kind);
        Bitmap Now()
        {
            var view = new UsageView(UsageFixtures.Numbers(Es), morph.To!, UsageChartMode.Spend, ChartRange.Day1, morph.Kind,
                morph.Progress, morph.Current, morph.From);
            return Paint(UsageFixtures.Frame(1f, view), out _);
        }
        var f = UsageFixtures.Frame(1f, new UsageView(UsageFixtures.Numbers(Es), morph.To!, UsageChartMode.Spend, ChartRange.Day1));
        var plot = Rectangle.Round(UsageLayout.Compute(NotchContentPainter.PageInner(NotchGeometry.PillRect(f.Width, f.Height,
            NotchPlacement.Surface(1f)), 1f, false, true), 1f).Plot);

        using var start = Now();   // p = 0
        var t = TimeSpan.Zero;
        while (morph.Progress < 0.4f && t < TimeSpan.FromSeconds(2)) morph.Step(t += TimeSpan.FromMilliseconds(4));
        Assert.Equal(UsageMorphKind.Morph, morph.Kind);
        Assert.InRange(morph.Progress, 0.4f, 0.65f);
        using var mid = Now();     // p ≈ 0,5
        using var end = Paint(f, out _);   // p = 1: la de llegada, quieta

        Assert.True(Diff(start, mid, plot) > 50, $"a p≈{morph.Progress:0.00} se pinta lo mismo que a p=0");
        Assert.True(Diff(mid, end, plot) > 50, $"a p≈{morph.Progress:0.00} se pinta ya la forma final");
    }

    private static int Diff(Bitmap a, Bitmap b, Rectangle area)
    {
        int n = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++;
        return n;
    }

    [Fact]
    public void Without_pro_the_toolbar_shows_the_pro_chip_and_with_it_the_ranges()
    {
        var spend = UsageFixtures.SpendChart(Es);
        var locked = new UsageView(UsageFixtures.Numbers(Es), spend, UsageChartMode.Spend, ChartRange.Day1, Locked: true, Interactive: false);
        var f = UsageFixtures.Frame(1f, locked);
        var surface = NotchPlacement.Surface(1f);
        var pill = NotchGeometry.PillRect(f.Width, f.Height, surface);
        var r = UsageLayout.Compute(NotchContentPainter.PageInner(pill, 1f, false, true), 1f);
        var chip = Rectangle.Round(r.ProChip);
        Assert.True(AccentPixels(f, chip) > 6, "sin chip «Pro»");
        Assert.Equal(0, AccentPixels(UsageFixtures.Frame(1f, locked with { Locked = false, Interactive = true }), chip));
    }

    [Fact]
    public void Hover_paints_the_line_dots_and_tooltip()
    {
        var spend = UsageFixtures.SpendChart(Es);
        var still = new UsageView(UsageFixtures.Numbers(Es), spend, UsageChartMode.Spend, ChartRange.Day1);
        int i = UsageFixtures.MidBucket(spend);
        var hover = still with { Bucket = i, TipIndex = i, TipAlpha = 1f };
        using var a = Paint(UsageFixtures.Frame(1f, still), out var surface);
        using var b = Paint(UsageFixtures.Frame(1f, hover), out _);
        var f = UsageFixtures.Frame(1f, hover);
        var r = UsageLayout.Compute(NotchContentPainter.PageInner(NotchGeometry.PillRect(f.Width, f.Height, surface), 1f, false, true), 1f);
        int diff = 0;
        var area = Rectangle.Round(r.Right);
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) diff++;
        Assert.True(diff > 400, $"solo {diff} píxeles distintos con el hover");
        // Sin interacción (sin el Pro o en otra pantalla) el hover no se pinta.
        using var c = Paint(UsageFixtures.Frame(1f, hover with { Interactive = false }), out _);
        for (int y = area.Top; y < area.Bottom; y += 3)
            for (int x = area.Left; x < area.Right; x += 3)
                Assert.Equal(a.GetPixel(x, y), c.GetPixel(x, y));
    }

    [Fact]
    public void The_chart_of_the_previous_range_is_dimmed_with_loading_or_the_failure_on_top()
    {
        var quota = UsageFixtures.QuotaChart(Es);
        var ready = new UsageView(UsageFixtures.Numbers(Es), quota, UsageChartMode.Quota, ChartRange.Month1);
        var f = UsageFixtures.Frame(1f, ready);
        var surface = NotchPlacement.Surface(1f);
        var plot = Rectangle.Round(UsageLayout.Compute(NotchContentPainter.PageInner(NotchGeometry.PillRect(f.Width, f.Height,
            surface), 1f, false, true), 1f).Plot);
        using var a = Paint(f, out _);
        using var loading = Paint(UsageFixtures.Frame(1f, ready with { Status = UsageChartStatus.Loading }), out _);
        using var failed = Paint(UsageFixtures.Frame(1f, ready with { Status = UsageChartStatus.Failed }), out _);
        // Atenuada: el color de las líneas pierde fuerza (la parte de arriba de la gráfica, lejos del texto del centro).
        Assert.True(Saturated(loading, plot) * 2 < Saturated(a, plot), "la gráfica del rango anterior no se atenúa");
        int diff = 0;
        for (int y = plot.Top; y < plot.Bottom; y++)
            for (int x = plot.Left; x < plot.Right; x++)
                if (loading.GetPixel(x, y) != failed.GetPixel(x, y)) diff++;
        Assert.True(diff > 50, "«Cargando…» y el aviso de fallo se pintan igual");
    }

    [Fact]
    public void A_lone_sample_after_a_gap_is_a_dot_joined_by_a_dashed_link()
    {
        // 7D (casillas de 2 h): la app se abrió hace 3 días y se ha vuelto a abrir ahora. La última casilla, sola tras el hueco.
        var now = UsageFixtures.Now;
        var points = new List<PctPoint> { new(now.AddDays(-3), 20, 30), new(now, 50, 60) };
        var q = new UsageQuotaInputs(null, null, null, null, new[] { 80 });
        var m = UsageChartBuilder.Quota(points, ChartRange.Week1, now, QuotaFocus.Five, q, new HashSet<string>(), false, Es);
        int i = m.Count - 1;
        Assert.True(!double.IsNaN(m.Series[0].Values[i]) && double.IsNaN(m.Series[0].Values[i - 1]));
        int last = Array.FindLastIndex(m.Series[0].Values, i - 1, v => !double.IsNaN(v));
        Assert.True(last > 0 && last < i - 1);
        var view = new UsageView(UsageFixtures.Numbers(Es), m, UsageChartMode.Quota, ChartRange.Week1);
        var f = UsageFixtures.Frame(1f, view);
        var plot = UsageLayout.Compute(NotchContentPainter.PageInner(NotchGeometry.PillRect(f.Width, f.Height,
            NotchPlacement.Surface(1f)), 1f, false, true), 1f).Plot;
        using var bmp = Paint(f, out _);
        foreach (int k in new[] { 0, 1 })
        {
            int x = (int)MathF.Round(plot.Left + UsageChartHit.X(i, m.Count, m.PastFraction, m.Shape.FirstX) * plot.Width);
            int y = (int)MathF.Round(plot.Bottom - m.Tops[k][i] * plot.Height);
            var c = Theme.Dark.ChartSeries[m.Series[k].Color];
            int hits = 0;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    var px = bmp.GetPixel(x + dx, y + dy);
                    if (Math.Abs(px.R - c.R) + Math.Abs(px.G - c.G) + Math.Abs(px.B - c.B) < 60) hits++;
                }
            Assert.True(hits >= 6, $"serie {m.Series[k].Key}: el punto aislado no se pinta ({hits} px de su color)");

            // El hueco: una línea fina discontinua entre la última casilla antes del hueco y el punto, sin área debajo.
            float t0 = UsageChartHit.X(last, m.Count, m.PastFraction, m.Shape.FirstX), t1 = UsageChartHit.X(i, m.Count, m.PastFraction, m.Shape.FirstX);
            float mx = plot.Left + (t0 + t1) / 2f * plot.Width;
            float my = plot.Bottom - (m.Tops[k][last] + m.Tops[k][i]) / 2f * plot.Height;
            var below = bmp.GetPixel((int)mx, (int)my + 14);
            int link = 0;
            for (int dx = -4; dx <= 4; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    var px = bmp.GetPixel((int)mx + dx, (int)my + dy);
                    if (px.R + px.G + px.B > below.R + below.G + below.B + 40) link++;
                }
            Assert.True(link >= 2, $"serie {m.Series[k].Key}: el hueco no se une con la línea discontinua ({link} px)");
            Assert.True(below.R + below.G + below.B < 120, $"serie {m.Series[k].Key}: el hueco se rellena con área ({below})");
        }
    }

    private static int Saturated(Bitmap bmp, Rectangle area)
    {
        int n = 0;
        for (int y = area.Top; y < area.Top + area.Height / 3; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A > 0 && c.GetSaturation() > 0.3f && Math.Max(c.R, Math.Max(c.G, c.B)) > 110) n++;
            }
        return n;
    }

    private static int AccentPixels(NotchFrame f, Rectangle area)
    {
        using var bmp = Paint(f, out _);
        float accentHue = Theme.Dark.Accent.GetHue();
        int n = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A < 40 || c.GetSaturation() < 0.35f || Math.Max(c.R, Math.Max(c.G, c.B)) < 50) continue;
                float d = Math.Abs(c.GetHue() - accentHue);
                if (Math.Min(d, 360 - d) <= 12f) n++;
            }
        return n;
    }

    // ---------------------------------------------------------------- anillos que abren la página

    [Fact]
    public void The_ring_pair_splits_halfway_between_the_two_groups()
    {
        var area = new RectangleF(100, 0, 200, 34);
        // Par de 120 px centrado: empieza en 140; el grupo de 5 h mide 55 y el hueco 11 → corte en 140 + 55 + 5,5.
        Assert.Equal(200.5f, NotchContentPainter.QuotaPairSplit(area, 1f, 120f, 55f), 3);
        // A 2x todo se dobla (y el área empieza en 200): 200 + 80 + 110 + 11.
        Assert.Equal(401f, NotchContentPainter.QuotaPairSplit(new RectangleF(200, 0, 400, 68), 2f, 240f, 110f), 3);
    }

    [Fact]
    public void The_ring_pair_only_answers_on_its_rings_and_the_rest_of_the_peek_opens_claude()
    {
        using var painter = new NotchContentPainter(Es);
        var quota = UsageFixtures.Snapshot().Usage;
        var sessions = Array.Empty<SessionState>();
        // Vista previa estrecha sin sesión (180 px: no cabe «7d %» a la derecha), el par centrado en ella.
        var pill = new RectangleF(100, 0, 180, 34);
        float pair = painter.MeasureQuotaPair(1f, withPct: true, UsageFormat.Percent(64, Es.Culture), UsageFormat.Percent(38, Es.Culture));
        float left = pill.Left + (pill.Width - pair) / 2f;
        Assert.True(left - pill.Left > 12f, "el par no deja margen a los lados");
        QuotaFocus At(float x) => painter.PeekQuotaAt(new PointF(x, 17), pill, 1f, mascot: false, sessions, quota);
        Assert.Equal(QuotaFocus.Five, At(left + 10));            // el anillo de 5 h
        Assert.Equal(QuotaFocus.Seven, At(left + pair - 3));     // el % de 7 d
        Assert.Equal(QuotaFocus.None, At(pill.Left + 3));        // fuera del par: el clic de siempre (abre Claude)
        Assert.Equal(QuotaFocus.None, At(pill.Right - 3));
        // Lo mismo, puro.
        Assert.Equal(QuotaFocus.None, NotchContentPainter.QuotaPairAt(pill.Left + 3, pill, 1f, pair, pair / 2f));
        Assert.Equal(QuotaFocus.Five, NotchContentPainter.QuotaPairAt(left, pill, 1f, pair, pair / 2f));
        Assert.Equal(QuotaFocus.Seven, NotchContentPainter.QuotaPairAt(left + pair, pill, 1f, pair, pair / 2f));
        Assert.Equal(QuotaFocus.None, NotchContentPainter.QuotaPairAt(left + pair + 8, pill, 1f, pair, pair / 2f));
    }

    [Fact]
    public void Wide_peek_slots_follow_the_painted_geometry()
    {
        var area = new RectangleF(0, 0, 320, 34);
        Assert.Equal(QuotaFocus.Five, NotchContentPainter.PeekSlotAt(310, area, 1f, active: true));    // «5h %» a la derecha
        Assert.Equal(QuotaFocus.Seven, NotchContentPainter.PeekSlotAt(240, area, 1f, active: true));   // «7d %» a su izquierda
        Assert.Equal(QuotaFocus.None, NotchContentPainter.PeekSlotAt(100, area, 1f, active: true));    // el texto de la sesión
        Assert.Equal(QuotaFocus.Five, NotchContentPainter.PeekSlotAt(20, area, 1f, active: false));    // el anillo de 5 h
    }

    [Fact]
    public void Claude_page_quota_column_maps_to_each_window()
    {
        var size = NotchLayout.PageSize(NotchPage.Claude, new PageRows(3, 0, 0), false, true, 1f);
        var pill = NotchGeometry.PillRect(size.Width, size.Height, new Size(700, 272));
        var inner = NotchContentPainter.PageInner(pill, 1f, false, true);
        var p5 = new PointF(inner.Left + 30, inner.Top + 10);
        var p7 = new PointF(inner.Left + 30, inner.Top + 2 * NotchLayout.ExpandedLine + 14);
        Assert.Equal(QuotaFocus.Five, NotchContentPainter.ClaudeQuotaAt(p5, pill, 1f, false, true));
        Assert.Equal(QuotaFocus.Seven, NotchContentPainter.ClaudeQuotaAt(p7, pill, 1f, false, true));
        Assert.Equal(QuotaFocus.None, NotchContentPainter.ClaudeQuotaAt(new PointF(inner.Right - 10, inner.Top + 10), pill, 1f, false, true));
    }
}
