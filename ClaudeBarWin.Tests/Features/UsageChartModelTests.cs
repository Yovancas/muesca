using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// La gráfica de la página Uso, ya calculada: gasto apilado por modelo y % real de la cuota con umbrales y proyección, y el
/// morphing entre dos gráficas (la pila sigue cerrada en cada fotograma, se encaja y para).
/// </summary>
public class UsageChartModelTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Strings Es = Localization.Get("es");
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static List<HistoryBucket> Buckets(params (double Opus, double Sonnet)[] values) =>
        values.Select((v, i) => new HistoryBucket(new DateTime(2026, 10, 30, 8 + i, 0, 0), $"{8 + i}h",
            new Dictionary<string, double> { ["Opus"] = v.Opus, ["Sonnet"] = v.Sonnet }
                .Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value))).ToList();

    private static readonly IReadOnlyDictionary<string, int> Slots = new Dictionary<string, int> { ["Opus"] = 0, ["Sonnet"] = 1 };

    private static UsageChartModel Spend(IReadOnlySet<string>? hidden = null, ChartRange range = ChartRange.Day1) =>
        UsageChartBuilder.Spend(Buckets((1, 0.5), (3, 1), (0, 0), (2, 2)), range, Slots, hidden ?? None, Es);

    // ---------------------------------------------------------------- gasto

    [Fact]
    public void Spend_stacks_the_biggest_family_at_the_bottom_with_a_nice_axis()
    {
        var m = Spend();
        Assert.Equal(UsageChartMode.Spend, m.Mode);
        Assert.False(m.Empty);
        Assert.Equal(4, m.Count);
        Assert.Equal(new[] { "Opus", "Sonnet" }, m.Series.Select(x => x.Key));
        Assert.Equal("$6,00", m.Series[0].Value);   // gasto del rango en la leyenda, con la cultura del idioma
        Assert.Equal("$3,50", m.Series[1].Value);
        // Pico 4 $ → eje hasta 5 con marcas de 1 en 1… o de 2 en 2: siempre «bonito» y por encima del pico.
        Assert.True(m.YTicks[^1].Fraction <= 1f + 1e-6f);
        Assert.All(m.YTicks, t => Assert.StartsWith("$", t.Label));
        Assert.Equal("total $9,50", m.Corner);
        // Puntos del hover: el de arriba de cada serie, acumulado.
        Assert.Equal(m.Tops[0][1] + (float)(1 / NiceScale.For(4).Max), m.Tops[1][1], 5);
        Assert.Equal(0f, m.Tops[1][2], 5);
    }

    [Fact]
    public void Spend_shape_is_a_closed_stack()
    {
        var s = Spend().Shape;
        Assert.True(s.Stacked);
        Assert.Equal(1f, s.PastFraction);
        for (int m = 0; m < UsageShape.Samples; m++)
        {
            Assert.Equal(0f, s.Lower[0][m]);
            Assert.Equal(s.Upper[0][m], s.Lower[1][m], 6);   // sin huecos ni solapes entre franjas
            Assert.True(s.Upper[1][m] >= s.Lower[1][m] - 1e-6f);
            Assert.InRange(s.Upper[1][m], 0f, 1f);
        }
    }

    [Fact]
    public void Hidden_families_leave_the_axis_and_the_totals_but_stay_in_the_legend()
    {
        var m = Spend(new HashSet<string> { "Opus" });
        Assert.Equal(2, m.Series.Count);
        Assert.False(m.Series[0].Visible);
        Assert.True(m.Series[1].Visible);
        Assert.Equal("total $3,50", m.Corner);
        Assert.All(m.Tops[0], v => Assert.True(float.IsNaN(v)));
        Assert.All(m.Tips, t => Assert.DoesNotContain(t.Rows, r => r.Label == "Opus"));
        // La franja oculta no tiene grosor: Sonnet empieza abajo.
        Assert.All(m.Shape.Lower[1], v => Assert.Equal(0f, v, 6));
    }

    [Fact]
    public void Hidden_families_that_hide_everything_in_this_range_do_not_blank_the_chart()
    {
        // Opus oculta en 24H y, en 1H, solo hubo Opus: se ve (no una gráfica en blanco con «total $0»).
        var onlyOpus = Buckets((1, 0), (2, 0));
        var m = UsageChartBuilder.Spend(onlyOpus, ChartRange.Hour1, Slots, new HashSet<string> { "Opus" }, Es);
        Assert.True(Assert.Single(m.Series).Visible);
        Assert.Equal("total $3,00", m.Corner);
        Assert.All(m.Tips, t => Assert.NotEmpty(t.Rows));
        // Una oculta que no está en el rango no cuenta; las que sí, siguen ocultas.
        var both = UsageChartBuilder.Spend(Buckets((1, 0.5), (3, 1)), ChartRange.Day1, Slots,
            new HashSet<string> { "Haiku", "Sonnet" }, Es);
        Assert.Equal(new[] { true, false }, both.Series.Select(x => x.Visible));
        Assert.Equal("total $4,00", both.Corner);
    }

    [Fact]
    public void Spend_tips_carry_the_slot_the_total_and_the_rows_by_value()
    {
        var m = Spend();
        var tip = m.Tips[3];
        Assert.Equal("11:00–12:00", tip.Title);
        Assert.Equal("$4,00", tip.Total);
        Assert.Equal(2, tip.Rows.Count);
        Assert.Empty(m.Tips[2].Rows);   // casilla sin gasto
        var tip1 = m.Tips[1];
        Assert.Equal(new[] { "Opus", "Sonnet" }, tip1.Rows.Select(r => r.Label));
        Assert.Equal(new[] { "$3,00", "$1,00" }, tip1.Rows.Select(r => r.Value));
        Assert.Equal(new[] { 0, 1 }, tip1.Rows.Select(r => r.Color));
        // Casillas de 08:00 a 11:00: horas redondas entre la primera y la última.
        Assert.Equal(new[] { "08:00", "09:00", "10:00", "11:00" }, m.XTicks.Select(t => t.Label));
        Assert.Equal(new[] { 0f, 1f / 3f, 2f / 3f, 1f }, m.XTicks.Select(t => t.Fraction));
    }

    [Theory]
    [InlineData(ChartRange.Day1, 23, "18:00", 6)]     // 24 h por horas desde las 17:20: cada 6 h
    [InlineData(ChartRange.Hour1, 0.9, "17:30", 0.25)] // 1 h: cada 15 min
    [InlineData(ChartRange.Hours5, 4.5, "18:00", 1)]    // 18, 19, 20 y 21: caben las cuatro
    [InlineData(ChartRange.Day1, 24, "18:00", 6)]      // tramo EXACTO de 24 h (la cuota en 24H): cada 6 h, no cada 12
    [InlineData(ChartRange.Hour1, 1, "17:30", 0.25)]   // tramo exacto de 1 h: cada 15 min, no cada 30
    public void Time_axis_uses_round_steps_aligned_to_the_clock(ChartRange range, double hours, string first, double stepHours)
    {
        var start = new DateTime(2026, 10, 29, 17, 20, 0);
        var ticks = UsageChartBuilder.TimeTicks(start, start.AddHours(hours), range, Es.Culture);
        Assert.InRange(ticks.Count, 2, UsageChartBuilder.XTickCount);
        Assert.Equal(first, ticks[0].Label);
        for (int i = 1; i < ticks.Count; i++)
        {
            Assert.True(ticks[i].Fraction > ticks[i - 1].Fraction);
            Assert.Equal(stepHours / hours, ticks[i].Fraction - ticks[i - 1].Fraction, 4);
        }
        Assert.All(ticks, t => Assert.InRange(t.Fraction, 0f, 1f));
    }

    [Fact]
    public void Time_ticks_fall_on_real_time_across_a_daylight_saving_change()
    {
        // Madrid, noche del 24 al 25 de octubre de 2026 (a las 03:00 vuelven a ser las 02:00): 24 h reales son 23 de reloj.
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        var startUtc = new DateTime(2026, 10, 24, 15, 20, 0, DateTimeKind.Utc);
        var start = TimeZoneInfo.ConvertTimeFromUtc(startUtc, tz);                   // 17:20 (CEST)
        var end = TimeZoneInfo.ConvertTimeFromUtc(startUtc.AddHours(24), tz);         // 16:20 (CET)
        var ticks = UsageChartBuilder.TimeTicks(start, end, ChartRange.Day1, Es.Culture, tz: tz);
        Assert.Equal(new[] { "18:00", "00:00", "06:00", "12:00" }, ticks.Select(t => t.Label));
        // Cada marca en su hora real del tramo (las casillas van en UTC): las 06:00 CET son 13 h 40 min después del inicio.
        var expected = new[] { 40, 400, 820, 1180 }.Select(m => m / 1440f);
        Assert.Equal(expected, ticks.Select(t => t.Fraction), new FloatComparer(1e-4f));
    }

    private sealed class FloatComparer(float eps) : IEqualityComparer<float>
    {
        public bool Equals(float a, float b) => Math.Abs(a - b) <= eps;
        public int GetHashCode(float v) => 0;
    }

    [Fact]
    public void Long_ranges_tick_on_midnights_with_days()
    {
        var start = new DateTime(2026, 10, 1, 17, 20, 0);
        var month = UsageChartBuilder.TimeTicks(start, start.AddDays(29), ChartRange.Month1, Es.Culture);
        Assert.InRange(month.Count, 3, UsageChartBuilder.XTickCount);
        Assert.Equal("2 oct", month[0].Label);   // la primera medianoche y, como por semanas serían 5, cada dos
        Assert.Equal("16 oct", month[1].Label);
        var week = UsageChartBuilder.TimeTicks(start, start.AddDays(6), ChartRange.Week1, Es.Culture);
        Assert.All(week, t => Assert.DoesNotContain(":", t.Label));
        Assert.Empty(UsageChartBuilder.TimeTicks(start, start, ChartRange.Day1, Es.Culture));
    }

    [Fact]
    public void No_spend_is_the_empty_state_not_empty_axes()
    {
        var m = UsageChartBuilder.Spend(Buckets((0, 0), (0, 0)), ChartRange.Day1, Slots, None, Es);
        Assert.True(m.Empty);
        Assert.Empty(m.YTicks);
        Assert.Empty(m.Series);
        Assert.True(UsageChartBuilder.Spend(new List<HistoryBucket>(), ChartRange.Day1, Slots, None, Es).Empty);
        var loading = UsageChartBuilder.Loading(UsageChartMode.Spend, ChartRange.Day1);
        Assert.True(loading.Loading);
        Assert.False(loading.Empty);
    }

    [Theory]
    [InlineData(ChartRange.Hour1, "11:00–11:05")]
    [InlineData(ChartRange.Hours5, "11:00–11:30")]
    [InlineData(ChartRange.Day1, "11:00–12:00")]
    public void Spend_tip_titles_follow_the_range(ChartRange range, string title) =>
        Assert.Equal(title, UsageChartBuilder.SpendTipTitle(new DateTime(2026, 10, 30, 11, 0, 0), range, Es.Culture));

    // ---------------------------------------------------------------- cuota

    private static List<PctPoint> Samples(int minutes, Func<int, double> five, Func<int, double> seven) =>
        Enumerable.Range(0, minutes).Select(i => new PctPoint(Now.AddMinutes(-minutes + i + 1), five(i), seven(i))).ToList();

    private static UsageQuotaInputs Inputs(double five = 60, double seven = 30, DateTimeOffset? eta = null) => new(
        new UsageWindow(five, new DateTimeOffset(Now).AddHours(1)), new UsageWindow(seven, new DateTimeOffset(Now).AddDays(2)),
        new PaceResult("5h", five, 1.2, 50, eta ?? new DateTimeOffset(Now).AddMinutes(40), new DateTimeOffset(Now).AddHours(1),
            true, PaceStatus.Over), null, new[] { 80, 95 });

    [Fact]
    public void Quota_draws_both_windows_with_the_alert_thresholds()
    {
        var m = UsageChartBuilder.Quota(Samples(300, i => i / 5.0, _ => 30), ChartRange.Hours5, Now, QuotaFocus.Five, Inputs(),
            None, projection: false, Es);
        Assert.Equal(UsageChartMode.Quota, m.Mode);
        Assert.Equal(new[] { "5h", "7d" }, m.Series.Select(x => x.Key));
        Assert.Equal("60%", m.Series[0].Value);   // el % de ahora (la foto), no la última casilla
        Assert.Equal(2, m.Thresholds.Count);
        Assert.Equal(0.8f, m.Thresholds[0].Fraction);
        Assert.False(m.Thresholds[0].Critical);
        Assert.True(m.Thresholds[1].Critical);
        Assert.Null(m.Projection);
        Assert.Equal(1f, m.PastFraction);
        Assert.False(m.Shape.Stacked);
        Assert.Equal(2, m.Shape.Keys.Length);
        Assert.InRange(m.Shape.Upper[0][^1], 0.5f, 0.62f);   // ~59 % al final, normalizado
        Assert.All(m.Tips, t => Assert.Null(t.Total));
    }

    [Fact]
    public void Quota_with_projection_leaves_room_until_the_reset()
    {
        var m = UsageChartBuilder.Quota(Samples(300, i => i / 5.0, _ => 30), ChartRange.Hours5, Now, QuotaFocus.Five, Inputs(),
            None, projection: true, Es);
        Assert.Equal(5f / 6f, m.PastFraction, 4);
        var p = Assert.IsType<UsageProjectionView>(m.Projection);
        Assert.True(p.Exhausts);                 // se agota a los 40 min, antes del reinicio (60)
        Assert.Equal(1f, p.ToY, 4);
        Assert.Equal(UsageChartBuilder.FiveColor, p.Color);
        Assert.StartsWith("se agota a las", p.Caption);
        Assert.Equal(1f, p.ResetX!.Value, 4);

        // Destacando 7 d, la proyección es la suya (y su reinicio, a 2 días, no cabe en el tramo de 5 h: hasta el borde).
        var seven = UsageChartBuilder.Quota(Samples(300, i => i / 5.0, _ => 30), ChartRange.Hours5, Now, QuotaFocus.Seven,
            Inputs(), None, projection: true, Es);
        Assert.Equal(UsageChartBuilder.SevenColor, seven.Projection!.Color);
        Assert.Equal(1f / 1.5f, seven.PastFraction, 4);
    }

    [Fact]
    public void The_projection_caption_carries_the_day_when_it_runs_out_another_day()
    {
        // 7 d destacada que se agota dentro de 3 días (antes de su reinicio, a 4): la hora sola se leería como hoy.
        var nowLocal = new DateTime(2026, 10, 30, 16, 0, 0, DateTimeKind.Local);
        var now = nowLocal.ToUniversalTime();
        var eta = new DateTimeOffset(nowLocal.AddDays(3).AddHours(1).AddMinutes(40));
        var q = new UsageQuotaInputs(new UsageWindow(40, new DateTimeOffset(nowLocal).AddHours(2)),
            new UsageWindow(70, new DateTimeOffset(nowLocal).AddDays(4)), null,
            new PaceResult("7d", 70, 1.4, 50, eta, new DateTimeOffset(nowLocal).AddDays(4), true, PaceStatus.Over), new[] { 80 });
        var points = Enumerable.Range(0, 300).Select(i => new PctPoint(now.AddMinutes(-300 + i), 40, 70)).ToList();
        var m = UsageChartBuilder.Quota(points, ChartRange.Week1, now, QuotaFocus.Seven, q, None, projection: true, Es);
        Assert.Equal("se agota el lun 17:40", m.Projection!.Caption);
        // Hoy mismo, solo la hora.
        var today = q with { PaceSeven = q.PaceSeven! with { EtaUtc = new DateTimeOffset(nowLocal.AddHours(2)) } };
        Assert.Equal("se agota a las 18:00",
            UsageChartBuilder.Quota(points, ChartRange.Week1, now, QuotaFocus.Seven, today, None, true, Es).Projection!.Caption);
    }

    [Fact]
    public void Each_quota_bucket_sits_at_the_time_its_tooltip_says()
    {
        // 30D: casillas de 8 h. La casilla i vale el % de su FINAL (y eso dice su tooltip): ahí va su punto, su raya y su curva.
        var points = Enumerable.Range(0, 30 * 24).Select(h => new PctPoint(Now.AddHours(-30 * 24 + h + 1), h % 50, 30)).ToList();
        var m = UsageChartBuilder.Quota(points, ChartRange.Month1, Now, QuotaFocus.Five, Inputs(), None, projection: true, Es);
        int n = m.Count;
        var span = TimeSpan.FromDays(30);
        var domain = span / m.PastFraction;   // de now − 30 d al final de la proyección
        foreach (int i in new[] { 0, n / 2, n - 1 })
        {
            var at = Now - span + span / n * (i + 1);                         // el final de la casilla (el del tooltip)
            float expected = (float)((at - (Now - span)) / domain);
            Assert.Equal(expected, UsageChartHit.X(i, n, m.PastFraction, m.Shape.FirstX), 4);
            Assert.Equal(i, UsageChartHit.Bucket(100f + expected * 300f, 100f, 300f, n, m.PastFraction, m.Shape.FirstX));
            Assert.Equal(QuotaTitle(at), m.Tips[i].Title);
        }
        // La curva va de la primera casilla a «ahora».
        Assert.Equal(m.PastFraction / n, m.Shape.X(0), 4);
        Assert.Equal(m.PastFraction, m.Shape.X(UsageShape.Samples - 1), 4);
        // El gasto (casillas por su inicio, como su eje) sigue de 0 a 1.
        Assert.Equal(0f, Spend().Shape.X(0));
    }

    private static string QuotaTitle(DateTime atUtc) =>
        UsageChartBuilder.QuotaTipTitle(atUtc.ToLocalTime(), ChartRange.Month1, Es.Culture);

    [Fact]
    public void Hidden_quota_lines_leave_the_shape_and_the_projection()
    {
        var m = UsageChartBuilder.Quota(Samples(60, _ => 10, _ => 20), ChartRange.Hour1, Now, QuotaFocus.Five, Inputs(),
            new HashSet<string> { UsageChartBuilder.FiveKey }, projection: true, Es);
        Assert.Equal(new[] { UsageChartBuilder.SevenKey }, m.Shape.Keys);
        Assert.Null(m.Projection);   // la de 5 h está oculta
        Assert.All(m.Tops[0], v => Assert.True(float.IsNaN(v)));
    }

    [Fact]
    public void An_empty_history_is_the_empty_state()
    {
        var m = UsageChartBuilder.Quota(new List<PctPoint>(), ChartRange.Day1, Now, QuotaFocus.None, Inputs(), None, true, Es);
        Assert.True(m.Empty);
        Assert.Null(m.Projection);
        Assert.Empty(m.Thresholds);
    }

    // ---------------------------------------------------------------- morphing

    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(1000.0 / 60);

    [Fact]
    public void Morph_keeps_the_stack_closed_every_frame_and_settles()
    {
        var morph = new UsageChartMorph();
        morph.Jump(Spend());
        var next = UsageChartBuilder.Spend(Buckets((0.2, 0), (0.1, 0.4), (5, 0), (1, 1), (2, 0)), ChartRange.Week1, Slots, None, Es);
        var t = TimeSpan.FromSeconds(10);
        morph.Go(next, reduceMotion: false, t);
        Assert.Equal(UsageMorphKind.Morph, morph.Kind);
        Assert.NotNull(morph.Current);
        int frames = 0;
        while (morph.IsAnimating && frames < 300)
        {
            t += Frame;
            morph.Step(t);
            frames++;
            if (morph.Current is not { } c) continue;
            for (int k = 1; k < c.Keys.Length; k++)
                for (int m = 0; m < UsageShape.Samples; m++)
                    Assert.Equal(c.Upper[k - 1][m], c.Lower[k][m], 5);   // ninguna franja pisa a otra
        }
        Assert.False(morph.IsAnimating);
        Assert.InRange(frames, 10, 60);   // ~0,3–0,5 s con el muelle Ajuste
        Assert.Same(next, morph.To);
        Assert.Null(morph.From);
        Assert.Null(morph.Current);
        Assert.Equal(1f, morph.Progress);
    }

    [Fact]
    public void A_family_that_appears_grows_from_nothing()
    {
        var morph = new UsageChartMorph();
        var onlyOpus = UsageChartBuilder.Spend(Buckets((1, 0), (2, 0)), ChartRange.Day1, Slots, None, Es);
        morph.Jump(onlyOpus);
        morph.Go(Spend(), reduceMotion: false, TimeSpan.Zero);
        var c = morph.Current!;
        int sonnet = c.IndexOf("Sonnet");
        Assert.True(sonnet >= 0);
        for (int m = 0; m < UsageShape.Samples; m++) Assert.Equal(c.Lower[sonnet][m], c.Upper[sonnet][m], 6);   // grosor 0 al empezar
    }

    [Fact]
    public void Reduce_motion_and_mode_changes_fade_instead()
    {
        var morph = new UsageChartMorph();
        morph.Jump(Spend());
        morph.Go(Spend(range: ChartRange.Week1), reduceMotion: true, TimeSpan.Zero);
        Assert.Equal(UsageMorphKind.Fade, morph.Kind);
        Assert.Null(morph.Current);
        morph.Step(TimeSpan.FromMilliseconds(60));
        Assert.Equal(0.5f, morph.Progress, 2);
        morph.Step(UsageChartMorph.FadeDuration);
        Assert.False(morph.IsAnimating);

        var quota = UsageChartBuilder.Quota(Samples(60, _ => 10, _ => 20), ChartRange.Hour1, Now, QuotaFocus.Five, Inputs(), None,
            false, Es);
        morph.Go(quota, reduceMotion: false, TimeSpan.FromSeconds(1));
        Assert.Equal(UsageMorphKind.Fade, morph.Kind);   // otro modo: no hay forma que transformar
        Assert.NotNull(morph.From);
    }

    [Fact]
    public void Quota_lines_morph_and_a_new_line_fades_in()
    {
        var morph = new UsageChartMorph();
        var five = UsageChartBuilder.Quota(Samples(60, _ => 10, _ => 20), ChartRange.Hour1, Now, QuotaFocus.Five, Inputs(),
            new HashSet<string> { UsageChartBuilder.SevenKey }, false, Es);
        var both = UsageChartBuilder.Quota(Samples(60, _ => 40, _ => 20), ChartRange.Hour1, Now, QuotaFocus.Five, Inputs(), None,
            false, Es);
        morph.Jump(five);
        morph.Go(both, reduceMotion: false, TimeSpan.Zero);
        Assert.Equal(UsageMorphKind.Morph, morph.Kind);
        var c = morph.Current!;
        Assert.Equal(1f, c.Alpha[c.IndexOf(UsageChartBuilder.FiveKey)]);
        Assert.Equal(0f, c.Alpha[c.IndexOf(UsageChartBuilder.SevenKey)]);
        Assert.Equal(0.1f, c.Upper[c.IndexOf(UsageChartBuilder.FiveKey)][^1], 3);   // empieza en lo que había
        var t = TimeSpan.Zero;
        for (int i = 0; i < 300 && morph.IsAnimating; i++) { t += Frame; morph.Step(t); }
        Assert.False(morph.IsAnimating);
    }

    [Fact]
    public void A_new_target_mid_morph_starts_from_what_is_on_screen()
    {
        var morph = new UsageChartMorph();
        morph.Jump(Spend());
        morph.Go(Spend(range: ChartRange.Week1), false, TimeSpan.Zero);
        morph.Step(TimeSpan.FromMilliseconds(80));
        var mid = morph.Current!.Upper[0].ToArray();
        var target = UsageChartBuilder.Spend(Buckets((9, 9), (9, 9)), ChartRange.Month1, Slots, None, Es);
        morph.Go(target, false, TimeSpan.FromMilliseconds(80));
        int k = morph.Current!.IndexOf("Opus");
        var c = morph.Current!;
        for (int m = 0; m < UsageShape.Samples; m++)
            Assert.Equal(mid[m], c.Upper[k][m], 4);   // sin salto
    }
}
