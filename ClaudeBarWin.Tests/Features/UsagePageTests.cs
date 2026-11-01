using System.Drawing;
using System.Reflection;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Página Uso, lo puro: geometría (cabe en 1366×768 a 100 %, escala con el DPI, el clic acierta lo que se pinta), los números
/// ya formateados, cuándo se piden los datos, cómo se alternan las series y los textos en todos los idiomas.
/// </summary>
public class UsagePageTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Strings Es = Localization.Get("es");

    /// <summary>La zona de contenido del expandido en la página Uso a escala <paramref name="s"/> (márgenes 18 / 12 / 14).</summary>
    private static RectangleF Inner(float s, bool alert = false, bool dots = true)
    {
        var size = NotchLayout.PageSize(NotchPage.Usage, default, alert, dots, s);
        float top = (NotchLayout.ExpandedPadTop + (alert ? NotchLayout.ExpandedLine + NotchLayout.ExpandedAlertGap : 0f)) * s;
        float bottom = size.Height - (NotchLayout.ExpandedPadBottom + (dots ? NotchLayout.PageDotsRow : 0f)) * s;
        return RectangleF.FromLTRB(UsageLayout.PadX * s, top, size.Width - UsageLayout.PadX * s, bottom);
    }

    // ---------------------------------------------------------------- tamaño

    [Theory]
    [InlineData(1f)]
    [InlineData(1.3f)]   // tamaño del notch al 130 %
    public void The_page_fits_a_1366x768_screen_at_100_percent(float s)
    {
        var size = NotchLayout.PageSize(NotchPage.Usage, default, withAlert: true, withDots: true, s);
        Assert.True(size.Width + 2 * (NotchMotion.EarSize(size.Width, s) + NotchMotion.ShadowMargin * s) <= 1366,
            $"{size.Width} de ancho");
        Assert.True(size.Height <= 768 * 0.45f, $"{size.Height} de alto: más de media pantalla");
        Assert.Equal(NotchLayout.UsagePageMaxHeight * s, size.Height, 3);
        Assert.Equal(UsageLayout.PageWidth * s, size.Width, 3);
    }

    [Fact]
    public void The_page_and_its_shadow_fit_in_the_notch_surface()
    {
        // NotchPlacement vive en la UI: su alto se repite aquí y lo vigila NotchLayoutTests con el valor de verdad.
        float surfaceW = 700f, surfaceH = 272f;
        var size = NotchLayout.PageSize(NotchPage.Usage, default, true, true, 1f);
        float squashed = size.Width * (1f + NotchMotion.SquashStretchX);
        Assert.True(squashed + 2 * NotchMotion.EarSize(squashed, 1f) + 2 * NotchMotion.ShadowBlurRadius <= surfaceW);
        Assert.True(size.Height + NotchMotion.ShadowBlurRadius + NotchMotion.ShadowOffsetY <= surfaceH);
        // Las demás páginas no cambian de tamaño.
        Assert.Equal(NotchLayout.ExpandedWidth, NotchLayout.PageSize(NotchPage.Music, default, false, true, 1f).Width);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void Layout_stays_inside_the_page_and_scales_with_dpi(float s)
    {
        var inner = Inner(s, alert: true);
        var r = UsageLayout.Compute(inner, s);
        var one = UsageLayout.Compute(Inner(1f, alert: true), 1f);
        Assert.Equal(one.Plot.Width * s, r.Plot.Width, 1);
        Assert.Equal(one.Plot.Height * s, r.Plot.Height, 1);
        Assert.True(r.Header.Left >= inner.Left - 0.5f && r.Header.Right <= inner.Right + 0.5f);
        Assert.True(r.Legend.Bottom <= inner.Bottom + 0.5f, $"leyenda {r.Legend} fuera de {inner}");
        Assert.True(r.Row(UsageLayout.Rows - 1).Bottom <= inner.Bottom + 0.5f);
        Assert.True(r.Left.Right < r.Right.Left);                               // columnas sin solaparse
        Assert.True(r.Segment(1).Right + 4 * s < r.Chip(0).Left, "modo y rangos sin pisarse");
        Assert.True(r.Chip(UsageRanges.All.Count - 1).Right <= r.Toolbar.Right + 0.5f);
        Assert.True(r.ProChip.Left > r.Segment(1).Right);
        Assert.True(r.RangeLabel.Width > 20 * s);
        Assert.True(r.Ring(0).Right < r.TileText(0).Left);
        Assert.True(r.Tile(0).Bottom < r.Tile(1).Top);
        Assert.True(r.Tile(1).Bottom < r.Row(0).Top);
        Assert.Equal(r.Legend.Width, r.LegendSlot(0).Width * UsageLayout.LegendSlots, 2);
        Assert.Equal(r.LegendSlot(2).Left, r.LegendSlot(2, 2).Left);
        Assert.Equal(r.Legend.Right, r.LegendSlot(2, 2).Right, 2);
        Assert.True(r.Plot.Height >= 90 * s, "la gráfica necesita aire");
    }

    [Fact]
    public void Layout_is_anchored_on_the_center_while_the_pill_grows()
    {
        // Mientras el expandido pasa de 480 a 560, la página va a su tamaño final centrada: se destapa, no se estira.
        var final = UsageLayout.Compute(Inner(1f), 1f);
        var narrow = Inner(1f);
        narrow = RectangleF.FromLTRB(narrow.Left + 40, narrow.Top, narrow.Right - 40, narrow.Bottom - 60);
        var growing = UsageLayout.Compute(narrow, 1f);
        Assert.Equal(final.Plot, growing.Plot);
        Assert.Equal(final.Tile(1), growing.Tile(1));
    }

    // ---------------------------------------------------------------- clic

    [Fact]
    public void Hit_test_finds_what_is_painted_where_it_is_painted()
    {
        var r = UsageLayout.Compute(Inner(1f), 1f);
        PointF C(RectangleF b) => new(b.Left + b.Width / 2f, b.Top + b.Height / 2f);
        Assert.Equal(new UsageHit(UsageControl.Tile, 0), UsageLayout.HitTest(C(r.Tile(0)), r, 2, true, false));
        Assert.Equal(new UsageHit(UsageControl.Tile, 1), UsageLayout.HitTest(C(r.Ring(1)), r, 2, true, false));
        Assert.Equal(new UsageHit(UsageControl.Segment, 1), UsageLayout.HitTest(C(r.Segment(1)), r, 2, true, false));
        for (int i = 0; i < UsageRanges.All.Count; i++)
            Assert.Equal(new UsageHit(UsageControl.Range, i), UsageLayout.HitTest(C(r.Chip(i)), r, 2, true, false));
        Assert.Equal(new UsageHit(UsageControl.Legend, 1), UsageLayout.HitTest(C(r.LegendSlot(1)), r, 2, true, false));
        Assert.Equal(default, UsageLayout.HitTest(C(r.LegendSlot(3)), r, 2, true, false));   // sin serie ahí
        Assert.Equal(new UsageHit(UsageControl.Plot), UsageLayout.HitTest(C(r.Plot), r, 2, true, false));
        Assert.Equal(default, UsageLayout.HitTest(C(r.Header), r, 2, true, false));
    }

    [Fact]
    public void Without_pro_the_chart_is_static_and_the_chip_opens_the_card()
    {
        var r = UsageLayout.Compute(Inner(1f), 1f);
        PointF C(RectangleF b) => new(b.Left + b.Width / 2f, b.Top + b.Height / 2f);
        Assert.Equal(new UsageHit(UsageControl.ProChip), UsageLayout.HitTest(C(r.ProChip), r, 2, false, locked: true));
        Assert.Equal(new UsageHit(UsageControl.ProChip), UsageLayout.HitTest(C(r.RangeLabel), r, 2, false, locked: true));
        Assert.Equal(default, UsageLayout.HitTest(C(r.Plot), r, 2, false, locked: true));
        Assert.Equal(default, UsageLayout.HitTest(C(r.LegendSlot(0)), r, 2, false, locked: true));
        // Navegar sigue siendo gratis: tarjetas y modo.
        Assert.Equal(new UsageHit(UsageControl.Tile, 1), UsageLayout.HitTest(C(r.Tile(1)), r, 2, false, locked: true));
        Assert.Equal(new UsageHit(UsageControl.Segment, 0), UsageLayout.HitTest(C(r.Segment(0)), r, 2, false, locked: true));
        // Con el Pro pero en una ventana que no es la interactiva: ni rangos, ni leyenda, ni hover.
        Assert.Equal(default, UsageLayout.HitTest(C(r.Chip(2)), r, 2, interactive: false, locked: false));
        Assert.Equal(default, UsageLayout.HitTest(C(r.Plot), r, 2, interactive: false, locked: false));
    }

    // ---------------------------------------------------------------- números

    private static AppSnapshot Snap(bool extra = true) => new()
    {
        Usage = new RealUsage
        {
            FiveHour = new UsageWindow(62.4, new DateTimeOffset(Now).AddHours(2).AddMinutes(13)),
            SevenDay = new UsageWindow(18, new DateTimeOffset(Now).AddDays(3).AddHours(4)),
            SevenDayOpus = new UsageWindow(31, null),
            SevenDaySonnet = new UsageWindow(0.4, null),
            ExtraUsageEnabled = extra,
        },
        LatestState = UsageFetchState.Ok,
        PaceFive = new PaceResult("5h", 62.4, 1.18, 53, new DateTimeOffset(Now).AddHours(1), new DateTimeOffset(Now).AddHours(2),
            true, PaceStatus.Over),
        PaceSeven = new PaceResult("7d", 18, 0.82, 22, null, new DateTimeOffset(Now).AddDays(3), false, PaceStatus.Ok),
        Health = new HealthStatus(HealthLevel.Degraded, "Minor"),
    };

    [Fact]
    public void Numbers_come_formatted_with_countdown_pace_models_extra_plan_and_health()
    {
        var n = UsageText.Build(Snap(), new PlanInfo("max", "default_claude_max_20x"), Now, QuotaFocus.Seven, Es);
        Assert.Equal(2, n.Tiles.Count);
        var five = n.Tiles[0];
        Assert.Equal("Sesión · 5h", five.Title);
        Assert.Equal("62%", five.Pct);
        Assert.Equal(62.4, five.Value);
        Assert.Equal("resetea en 2h 13m", five.Reset);
        Assert.StartsWith("↗ 118% · se agota ", five.PaceText);   // la hora entera: la flecha y el % ya dicen «ritmo»
        Assert.Equal(PaceStatus.Over, five.Pace);
        Assert.False(five.Focused);
        Assert.True(n.Tiles[1].Focused);
        Assert.Equal("→ ritmo 82%", n.Tiles[1].PaceText);   // sin agotarse: sin hora
        Assert.Equal(new[] { "Opus 7d", "Sonnet 7d", Es.NotchUsageExtraOn }, n.Rows.Select(r => r.Label));
        Assert.Equal("31%", n.Rows[0].Pct);
        Assert.True(n.Rows[2].Accent);
        Assert.Equal("Plan Max · 20x", n.Plan);
        Assert.Equal(Es.HealthDegraded, n.Health);
        Assert.Equal(HealthLevel.Degraded, n.HealthLevel);
    }

    [Fact]
    public void The_run_out_time_carries_the_day_when_it_is_not_today()
    {
        // En hora local: son las 16:00 y se agota mañana a las 10:00 (en menos de 20 h, pero NO hoy).
        var now = new DateTimeOffset(new DateTime(2026, 10, 30, 16, 0, 0, DateTimeKind.Local));
        var tonight = new DateTimeOffset(new DateTime(2026, 10, 30, 23, 30, 0, DateTimeKind.Local));
        var tomorrow = new DateTimeOffset(new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Local));
        static PaceResult Pace(DateTimeOffset eta) => new("5h", 80, 1.3, 60, eta, eta.AddHours(1), true, PaceStatus.Over);
        Assert.Equal("23:30", UsageText.EtaClock(tonight, now, Es.Culture));
        Assert.Equal("sáb 10:00", UsageText.EtaClock(tomorrow, now, Es.Culture));
        Assert.EndsWith(" 23:30", UsageText.PaceLine(Pace(tonight), now, Es));
        Assert.EndsWith(" sáb 10:00", UsageText.PaceLine(Pace(tomorrow), now, Es));
    }

    [Fact]
    public void Without_data_the_numbers_say_why()
    {
        var n = UsageText.Build(new AppSnapshot { LatestState = UsageFetchState.NoCredentials }, new PlanInfo("", ""), Now,
            QuotaFocus.None, Es);
        Assert.Equal("—", n.Tiles[0].Pct);
        Assert.Null(n.Tiles[0].Value);
        Assert.Equal(UsageFormat.StateMessage(UsageFetchState.NoCredentials, Es), n.Tiles[0].Reset);
        Assert.Empty(n.Rows);
        Assert.Null(n.Plan);
        Assert.Null(n.Health);
        Assert.Equal(2, UsageText.Build(null, null, Now, QuotaFocus.None, Es).Tiles.Count);
        Assert.DoesNotContain(UsageText.Build(Snap(extra: false), null, Now, QuotaFocus.None, Es).Rows, r => r.Accent);
    }

    // ---------------------------------------------------------------- peticiones

    [Fact]
    public void Feed_asks_once_per_range_until_the_data_gets_old()
    {
        var feed = new UsageFeed();
        Assert.True(feed.ShouldRequest(ChartRange.Day1, Now));
        feed.MarkRequested(ChartRange.Day1, Now);
        Assert.False(feed.ShouldRequest(ChartRange.Day1, Now.AddSeconds(5)));   // en vuelo
        Assert.True(feed.ShouldRequest(ChartRange.Week1, Now.AddSeconds(5)));   // otro rango, aparte
        Assert.True(feed.ShouldRequest(ChartRange.Day1, Now + UsageFeed.InFlight));   // la respuesta se perdió
        var data = new UsageSeriesData(new List<HistoryBucket>(), new List<PctPoint>(), Now.AddSeconds(2));
        feed.Store(ChartRange.Day1, data);
        Assert.Same(data, feed.Get(ChartRange.Day1));
        Assert.False(feed.ShouldRequest(ChartRange.Day1, Now.AddSeconds(30)));
        Assert.True(feed.ShouldRequest(ChartRange.Day1, Now.AddSeconds(2) + UsageFeed.Fresh));
        feed.MarkRequested(ChartRange.Week1, Now);
        feed.Failed(ChartRange.Week1);
        Assert.True(feed.ShouldRequest(ChartRange.Week1, Now.AddSeconds(1)));   // tras un fallo, se puede repetir ya
        Assert.True(feed.ShouldRequest(ChartRange.Day1, Now.AddSeconds(-10)));  // el reloj fue hacia atrás
    }

    [Fact]
    public void A_slow_parse_is_not_requested_twice()
    {
        // Primera carga de 30D con el índice frío: el parseo tarda más de 30 s. No se lanza otra petición idéntica.
        var feed = new UsageFeed();
        feed.MarkRequested(ChartRange.Month1, Now);
        Assert.False(feed.ShouldRequest(ChartRange.Month1, Now.AddSeconds(45)));
        Assert.False(feed.ShouldRequest(ChartRange.Month1, Now.AddSeconds(90)));
        Assert.True(UsageFeed.InFlight >= TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void The_chart_of_another_range_is_loading_or_failed_until_its_data_arrive()
    {
        var feed = new UsageFeed();
        feed.MarkRequested(ChartRange.Month1, Now);
        Assert.False(feed.HasFailed(ChartRange.Month1));
        feed.Failed(ChartRange.Month1);
        Assert.True(feed.HasFailed(ChartRange.Month1));
        feed.MarkRequested(ChartRange.Month1, Now.AddMinutes(1));   // se reintenta: vuelve a «Cargando…»
        Assert.False(feed.HasFailed(ChartRange.Month1));
        feed.Failed(ChartRange.Month1);
        feed.Store(ChartRange.Month1, new UsageSeriesData(new List<HistoryBucket>(), new List<PctPoint>(), Now));
        Assert.False(feed.HasFailed(ChartRange.Month1));

        var day = UsageChartBuilder.Loading(UsageChartMode.Spend, ChartRange.Day1) with { Loading = false };
        Assert.Equal(UsageChartStatus.Ready, UsageChartState.Of(day, ChartRange.Day1, failed: false));
        Assert.Equal(UsageChartStatus.Loading, UsageChartState.Of(day, ChartRange.Month1, failed: false));   // la de 24H sigue
        Assert.Equal(UsageChartStatus.Failed, UsageChartState.Of(day, ChartRange.Month1, failed: true));
        Assert.Equal(UsageChartStatus.Loading, UsageChartState.Of(UsageChartBuilder.Loading(UsageChartMode.Spend, ChartRange.Day1),
            ChartRange.Day1, failed: false));
        Assert.Equal(UsageChartStatus.Loading, UsageChartState.Of(null, ChartRange.Day1, failed: false));
    }

    // ---------------------------------------------------------------- series

    private static readonly string[] Keys = { "Opus", "Sonnet", "Haiku" };

    [Fact]
    public void Legend_click_toggles_a_series_but_never_hides_them_all()
    {
        var h = UsageSeriesToggle.Toggle(Keys, new HashSet<string>(), "Sonnet");
        Assert.Equal(new[] { "Sonnet" }, h);
        h = UsageSeriesToggle.Toggle(Keys, h, "Sonnet");
        Assert.Empty(h);
        h = UsageSeriesToggle.Toggle(Keys, new HashSet<string> { "Opus", "Sonnet" }, "Haiku");
        Assert.Empty(h);   // ocultar la última vuelve a enseñarlas todas
        h = UsageSeriesToggle.Toggle(Keys, new HashSet<string> { "Gone" }, "Opus");
        Assert.Equal(new[] { "Opus" }, h);   // lo de otra gráfica se olvida
    }

    [Fact]
    public void Wheel_cycles_all_then_each_one_alone()
    {
        IReadOnlySet<string> h = new HashSet<string>();
        var seen = new List<string?>();
        for (int i = 0; i < 4; i++)
        {
            h = UsageSeriesToggle.Cycle(Keys, h, +1);
            seen.Add(UsageSeriesToggle.Solo(Keys, h));
        }
        Assert.Equal(new[] { "Opus", "Sonnet", "Haiku", null }, seen);
        h = UsageSeriesToggle.Cycle(Keys, h, -1);
        Assert.Equal("Haiku", UsageSeriesToggle.Solo(Keys, h));
        Assert.Empty(UsageSeriesToggle.Cycle(new[] { "Opus" }, new HashSet<string>(), 1));   // una serie: nada que alternar
    }

    // ---------------------------------------------------------------- textos

    private static IEnumerable<PropertyInfo> UsageProperties() => typeof(Strings).GetProperties()
        .Where(p => p.Name.StartsWith("NotchUsage", StringComparison.Ordinal) || p.Name == nameof(Strings.NotchPageUsage));

    /// <summary>Palabras que en ese idioma se dicen igual que en inglés.</summary>
    private static readonly HashSet<(string, string)> SameAsEnglish = new();

    [Theory]
    [InlineData("es")]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh-Hant")]
    public void Every_usage_text_is_translated(string code)
    {
        var en = Localization.Get("en");
        var s = Localization.Get(code);
        var props = UsageProperties().ToList();
        Assert.True(props.Count >= 11, $"solo {props.Count} textos");
        foreach (var p in props)
        {
            var value = (string)p.GetValue(s)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{code}.{p.Name} vacío");
            if (!SameAsEnglish.Contains((code, p.Name)))
                Assert.True(value != (string)p.GetValue(en)!, $"{code}.{p.Name} cae al inglés");
        }
        // Lo que la página toma del panel también está traducido (las pestañas del selector).
        Assert.NotEqual(en.ChartTabSpend, s.ChartTabSpend);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh-Hant")]
    public void Usage_formats_take_their_argument(string code)
    {
        var s = Localization.Get(code);
        foreach (var fmt in new[] { s.NotchUsagePaceFmt, s.NotchUsageRunsOutFmt, s.NotchUsageRunsOutDayFmt, s.NotchUsageAtResetFmt })
        {
            Assert.Contains("{0}", fmt);
            Assert.DoesNotContain("{1}", fmt);
            _ = string.Format(s.Culture, fmt, "42%");
        }
    }
}
