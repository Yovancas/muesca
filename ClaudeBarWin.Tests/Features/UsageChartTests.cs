using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Lo puro de la gráfica de la página Uso: ejes «bonitos», curvas monótonas (sin picos inventados), casillas del % real desde
/// SQLite, proyección del ritmo y qué casilla hay bajo el ratón.
/// </summary>
public class UsageChartTests
{
    private static readonly DateTime T0 = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------- ejes

    [Theory]
    [InlineData(3.7, 4, 2)]
    [InlineData(0.042, 0.06, 0.02)]
    [InlineData(100, 150, 50)]
    [InlineData(1, 1.5, 0.5)]
    [InlineData(12.4, 15, 5)]
    public void Nice_axis_rounds_the_top_and_the_step_to_1_2_5(double max, double top, double step)
    {
        var a = NiceScale.For(max);
        Assert.Equal(top, a.Max, 9);
        Assert.Equal(step, a.Step, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Without_data_the_axis_is_0_to_1_never_0(double max)
    {
        var a = NiceScale.For(max);
        Assert.Equal(1, a.Max);
        Assert.Equal(0.5, a.Step);
        Assert.Equal(2, a.Ticks);
    }

    [Fact]
    public void Nice_axis_always_covers_the_data_with_air_and_few_ticks()
    {
        var rnd = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            double max = Math.Pow(10, rnd.NextDouble() * 8 - 4);
            var a = NiceScale.For(max);
            Assert.True(a.Max >= max * NiceScale.Headroom - 1e-12, $"{max} → {a.Max}");
            Assert.InRange(a.Ticks, 1, 8);
            Assert.Equal(a.Max / a.Step, Math.Round(a.Max / a.Step), 6);   // el paso divide el máximo
            double f = a.Step / Math.Pow(10, Math.Floor(Math.Log10(a.Step)));
            Assert.Contains(Math.Round(f, 6), new[] { 1.0, 2.0, 5.0 });
        }
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0.5, 1)]
    [InlineData(0.25, 2)]
    [InlineData(0.02, 2)]
    [InlineData(0.002, 3)]
    [InlineData(50, 0)]
    public void Tick_labels_get_the_decimals_their_step_needs(double step, int decimals) =>
        Assert.Equal(decimals, NiceScale.Decimals(step));

    // ---------------------------------------------------------------- curvas

    [Fact]
    public void Curve_passes_through_every_point()
    {
        double[] y = { 0, 2, 2, 5, 1, 0, 0, 3 };
        var m = new double[y.Length];
        UsageCurves.Tangents(y, m);
        for (int i = 0; i < y.Length; i++) Assert.Equal(y[i], UsageCurves.Eval(y, m, i), 9);
    }

    [Fact]
    public void Curve_never_leaves_the_range_of_each_segment()
    {
        // Gasto ≥ 0 con saltos bruscos: una cúbica cualquiera se hundiría bajo cero o pasaría del pico.
        double[] y = { 0, 0, 10, 0.2, 0, 7, 7, 0, 3 };
        var m = new double[y.Length];
        UsageCurves.Tangents(y, m);
        for (double u = 0; u <= y.Length - 1; u += 0.01)
        {
            int k = Math.Min((int)u, y.Length - 2);
            double v = UsageCurves.Eval(y, m, u);
            Assert.InRange(v, Math.Min(y[k], y[k + 1]) - 1e-9, Math.Max(y[k], y[k + 1]) + 1e-9);
        }
    }

    [Fact]
    public void Monotone_data_gives_a_monotone_curve()
    {
        double[] y = { 1, 1.5, 4, 4.1, 9, 30 };
        var m = new double[y.Length];
        UsageCurves.Tangents(y, m);
        double prev = double.MinValue;
        for (double u = 0; u <= y.Length - 1; u += 0.005)
        {
            double v = UsageCurves.Eval(y, m, u);
            Assert.True(v >= prev - 1e-9, $"baja en {u}");
            prev = v;
        }
    }

    [Fact]
    public void Resample_keeps_ends_constants_and_gaps()
    {
        var output = new float[UsageShape.Samples];
        var scratch = new double[16];
        UsageCurves.Resample(new double[] { 3, 3, 3, 3 }, scratch, output);
        Assert.All(output, v => Assert.Equal(3f, v, 5));

        UsageCurves.Resample(new double[] { 1, 4, 2, 8 }, scratch, output);
        Assert.Equal(1f, output[0], 5);
        Assert.Equal(8f, output[^1], 5);

        // Hueco en medio: lo que cae entre dos NaN o junto a uno es NaN; el resto, curva.
        UsageCurves.Resample(new double[] { 1, 2, double.NaN, double.NaN, 5, 6 }, scratch, output);
        Assert.False(float.IsNaN(output[0]));
        Assert.True(float.IsNaN(output[UsageShape.Samples / 2]));
        Assert.False(float.IsNaN(output[^1]));

        UsageCurves.Resample(new double[] { 7 }, scratch, output);
        Assert.All(output, v => Assert.Equal(7f, v, 5));
    }

    // ---------------------------------------------------------------- casillas del %

    private static PctPoint P(double minutes, double? five, double? seven = null) => new(T0.AddMinutes(minutes), five, seven);

    /// <summary>Umbral fijo para probar <see cref="UsageBuckets.Percent"/> por sí sola (la gráfica usa <see cref="UsageBuckets.MaxGap"/>).</summary>
    private static readonly TimeSpan Gap30 = TimeSpan.FromMinutes(30);

    [Fact]
    public void Max_gap_is_two_hours_or_two_buckets()
    {
        Assert.Equal(TimeSpan.FromHours(2), UsageBuckets.MaxGap(TimeSpan.FromDays(1), 96));    // casillas de 15 min
        Assert.Equal(TimeSpan.FromHours(4), UsageBuckets.MaxGap(TimeSpan.FromDays(7), 84));    // de 2 h
        Assert.Equal(TimeSpan.FromHours(16), UsageBuckets.MaxGap(TimeSpan.FromDays(30), 90));  // de 8 h
    }

    [Fact]
    public void The_gap_is_measured_between_consecutive_samples_not_bucket_ends()
    {
        // Casillas de 15 min: la última de la casilla 0 a los 1 min y la primera de la 2 a los 31 min (hueco real de 30 min,
        // pero entre las ÚLTIMAS de cada casilla hay 43 min). Con un umbral de 30 min se une.
        var pts = new[] { P(1, 10), P(31, 30), P(44, 40) };
        var v = UsageBuckets.Percent(pts, p => p.FivePct, T0, T0.AddMinutes(60), 4, Gap30);
        Assert.Equal(25, v[1], 6);
        Assert.Equal(40, v[2]);
    }

    /// <summary>24 h como las del log real: una muestra cada 1-2 min, huecos de 5-17 min por los 429 y uno de 37 min.</summary>
    private static List<PctPoint> RealDay(DateTime from, DateTime to, Func<double, bool>? closed = null)
    {
        var pts = new List<PctPoint>();
        var rnd = new Random(42);
        double five = 0;
        bool long37 = false;
        for (var t = from; t <= to;)
        {
            double h = (t - from).TotalHours;
            if (closed?.Invoke(h) != true) pts.Add(new PctPoint(t, five, 37));   // 7 d plana
            five = (five + rnd.NextDouble() * 0.3) % 100;
            int r = rnd.Next(100);
            double step = r < 6 ? 5 + rnd.Next(6) : r == 6 ? 17 : rnd.Next(1, 3);
            if (!long37 && h >= 12) { step = 37; long37 = true; }
            t = t.AddMinutes(step);
        }
        return pts;
    }

    [Fact]
    public void Real_day_with_rate_limit_gaps_is_a_continuous_curve()
    {
        var to = T0.AddDays(1);
        var pts = RealDay(T0, to);
        int n = UsageRanges.PercentBuckets(ChartRange.Day1);
        var gap = UsageBuckets.MaxGap(TimeSpan.FromDays(1), n);
        var five = UsageBuckets.Percent(pts, p => p.FivePct, T0, to, n, gap);
        var seven = UsageBuckets.Percent(pts, p => p.SevenPct, T0, to, n, gap);
        Assert.All(five, x => Assert.False(double.IsNaN(x)));
        Assert.All(seven, x => Assert.Equal(37, x));   // la línea plana de 7 d sin un corte
        // Aligerar (primera y última por casilla) da lo mismo que todas las muestras.
        var kept = UsageBuckets.EdgesPerBucket(pts, T0, to, n);
        Assert.Equal(five, UsageBuckets.Percent(kept, p => p.FivePct, T0, to, n, gap));
    }

    [Fact]
    public void A_three_hour_gap_still_cuts_the_curve()
    {
        var to = T0.AddDays(1);
        var pts = RealDay(T0, to, h => h is > 10 and < 13);
        int n = UsageRanges.PercentBuckets(ChartRange.Day1);
        var seven = UsageBuckets.Percent(pts, p => p.SevenPct, T0, to, n, UsageBuckets.MaxGap(TimeSpan.FromDays(1), n));
        Assert.True(double.IsNaN(seven[46]));   // 11:30, con la app cerrada
        Assert.Equal(37, seven[38]);
        Assert.Equal(37, seven[54]);
        Assert.Equal(37, seven[^1]);
    }

    [Fact]
    public void Flat_seven_day_line_has_no_cuts_over_a_week()
    {
        var to = T0.AddDays(7);
        var pts = RealDay(T0, to);
        int n = UsageRanges.PercentBuckets(ChartRange.Week1);
        var seven = UsageBuckets.Percent(pts, p => p.SevenPct, T0, to, n, UsageBuckets.MaxGap(TimeSpan.FromDays(7), n));
        Assert.All(seven, x => Assert.Equal(37, x));
    }

    [Fact]
    public void Each_bucket_takes_the_last_sample_in_it()
    {
        var v = UsageBuckets.Percent(new[] { P(1, 10), P(4, 30), P(2, 20) }, p => p.FivePct, T0, T0.AddMinutes(10), 2,
            Gap30);
        Assert.Equal(30, v[0]);   // la última de la casilla, no la media ni la mayor por orden de llegada
        Assert.Equal(30, v[1]);   // la vacía del final sigue con el último valor (no han pasado 30 min)
    }

    [Fact]
    public void Short_gaps_are_joined_and_long_ones_stay_open()
    {
        var pts = new[] { P(0, 0), P(30, 30), P(200, 50), P(210, 60) };
        var v = UsageBuckets.Percent(pts, p => p.FivePct, T0, T0.AddMinutes(300), 30, Gap30);   // 10 min
        Assert.Equal(0, v[0]);
        Assert.Equal(10, v[1], 6);   // entre 0 (casilla 0) y 30 (casilla 3), en recta
        Assert.Equal(20, v[2], 6);
        Assert.Equal(30, v[3]);
        Assert.True(double.IsNaN(v[10]));   // 170 min sin muestras: la app estaba cerrada
        Assert.Equal(50, v[20]);
        Assert.Equal(60, v[21]);
        Assert.Equal(60, v[23]);           // ≤ 30 min tras la última
        Assert.True(double.IsNaN(v[29]));  // > 30 min tras la última
    }

    [Fact]
    public void Samples_outside_the_range_or_without_a_value_do_not_count()
    {
        var pts = new[] { P(-5, 99), P(3, null, 40), P(11, 77) };
        var five = UsageBuckets.Percent(pts, p => p.FivePct, T0, T0.AddMinutes(10), 5, Gap30);
        Assert.False(UsageBuckets.Any(five));
        var seven = UsageBuckets.Percent(pts, p => p.SevenPct, T0, T0.AddMinutes(10), 5, Gap30);
        Assert.True(double.IsNaN(seven[0]));
        Assert.Equal(40, seven[1]);
        Assert.Equal(40, seven[4]);
        Assert.Empty(UsageBuckets.Percent(pts, p => p.FivePct, T0, T0.AddMinutes(10), 0, Gap30));
    }

    // ---------------------------------------------------------------- proyección

    private static readonly DateTimeOffset Now = new(T0);

    [Fact]
    public void Domain_reserves_room_until_the_reset_up_to_half_the_span()
    {
        var (start, end, past) = UsageProjection.Domain(TimeSpan.FromHours(5), Now, Now.AddHours(1));
        Assert.Equal(Now.AddHours(-5), start);
        Assert.Equal(Now.AddHours(1), end);
        Assert.Equal(5.0 / 6, past, 6);

        (_, end, past) = UsageProjection.Domain(TimeSpan.FromHours(5), Now, Now.AddHours(4));
        Assert.Equal(Now.AddHours(2.5), end);   // como mucho la mitad del tramo
        Assert.Equal(5 / 7.5, past, 6);

        (_, end, past) = UsageProjection.Domain(TimeSpan.FromHours(5), Now, null);
        Assert.Equal(Now, end);
        Assert.Equal(1, past, 6);
    }

    [Fact]
    public void Projection_stops_at_100_when_it_runs_out_before_the_reset()
    {
        var (start, end, _) = UsageProjection.Domain(TimeSpan.FromHours(5), Now, Now.AddHours(2));
        var p = UsageProjection.Compute(60, Now.AddHours(1), Now.AddHours(2), Now, start, end)!.Value;
        Assert.True(p.Exhausts);
        Assert.Equal(60, p.FromPct);
        Assert.Equal(100, p.ToPct, 6);
        Assert.Equal(6.0 / 7, p.ToX, 6);           // a la hora en que se agota
        Assert.Equal(6.0 / 7, p.EtaX!.Value, 6);
        Assert.Equal(1, p.ResetX!.Value, 6);
        Assert.Equal(100, p.PctAtReset!.Value, 6);
    }

    [Fact]
    public void Projection_reaches_the_reset_with_the_pct_it_will_have()
    {
        var (start, end, past) = UsageProjection.Domain(TimeSpan.FromHours(5), Now, Now.AddHours(1));
        // A este ritmo llegaría a 100 % dentro de 4 h: al reiniciarse (1 h) estará en 40 + 60/4 = 55 %.
        var p = UsageProjection.Compute(40, Now.AddHours(4), Now.AddHours(1), Now, start, end)!.Value;
        Assert.False(p.Exhausts);
        Assert.Equal(past, p.FromX, 6);
        Assert.Equal(1, p.ToX, 6);
        Assert.Equal(55, p.ToPct, 6);
        Assert.Equal(55, p.PctAtReset!.Value, 6);
        Assert.Null(p.EtaX);   // se agotaría fuera de la gráfica
    }

    [Fact]
    public void Without_burn_the_projection_is_flat_and_without_room_there_is_none()
    {
        var (start, end, _) = UsageProjection.Domain(TimeSpan.FromHours(5), Now, Now.AddHours(1));
        var p = UsageProjection.Compute(30, null, Now.AddHours(1), Now, start, end)!.Value;
        Assert.Equal(30, p.ToPct);
        Assert.Equal(30, p.PctAtReset!.Value);
        Assert.False(p.Exhausts);

        (start, end, _) = UsageProjection.Domain(TimeSpan.FromHours(5), Now, null);
        Assert.Null(UsageProjection.Compute(30, Now.AddHours(1), null, Now, start, end));
    }

    // ---------------------------------------------------------------- hover

    [Theory]
    [InlineData(100f, 0)]      // borde izquierdo
    [InlineData(400f, 10)]     // borde derecho
    [InlineData(250f, 5)]      // centro
    [InlineData(262f, 5)]      // redondea a la más cercana
    [InlineData(268f, 6)]
    [InlineData(96f, 0)]       // un pelín fuera: dentro de la holgura
    [InlineData(60f, -1)]      // fuera
    [InlineData(460f, -1)]
    public void Hover_hits_the_nearest_bucket(float px, int expected) =>
        Assert.Equal(expected, UsageChartHit.Bucket(px, 100f, 300f, 11, 1f));

    [Fact]
    public void Hover_in_the_projection_area_stays_on_the_last_bucket()
    {
        // Las casillas ocupan el 75 % de la izquierda; el resto es la proyección.
        Assert.Equal(10, UsageChartHit.Bucket(390f, 100f, 300f, 11, 0.75f));
        Assert.Equal(10, UsageChartHit.Bucket(325f, 100f, 300f, 11, 0.75f));
        Assert.Equal(5, UsageChartHit.Bucket(212.5f, 100f, 300f, 11, 0.75f));
        Assert.Equal(-1, UsageChartHit.Bucket(200f, 100f, 300f, 0, 1f));
        Assert.Equal(-1, UsageChartHit.Bucket(200f, 100f, 0f, 5, 1f));
        Assert.Equal(0, UsageChartHit.Bucket(390f, 100f, 300f, 1, 1f));
    }

    [Fact]
    public void Hover_x_and_hit_are_the_same_geometry()
    {
        for (int n = 2; n < 100; n += 7)
            foreach (float past in new[] { 1f, 0.8f, 2f / 3f })
                for (int i = 0; i < n; i++)
                {
                    float x = 100f + UsageChartHit.X(i, n, past) * 300f;
                    Assert.Equal(i, UsageChartHit.Bucket(x, 100f, 300f, n, past));
                }
        Assert.Equal(0.5f, UsageChartHit.X(0, 1, 1f));
        Assert.Equal(0.25f, UsageChartHit.X(2.5f, 11, 1f), 5);   // fraccionaria: la raya se desliza
    }

    // ---------------------------------------------------------------- rangos

    [Fact]
    public void Ranges_match_the_panel_and_each_window_opens_on_its_own()
    {
        Assert.Equal(new[] { "1H", "5H", "24H", "7D", "30D" }, UsageRanges.All.Select(UsageRanges.Label));
        Assert.Equal(ChartRange.Hours5, UsageRanges.For(QuotaFocus.Five));
        Assert.Equal(ChartRange.Week1, UsageRanges.For(QuotaFocus.Seven));
        Assert.Equal(ChartRange.Hours5, UsageRanges.For(QuotaFocus.None));
        foreach (var r in UsageRanges.All)
        {
            Assert.InRange(UsageRanges.PercentBuckets(r), 30, 120);
            Assert.True(UsageRanges.Span(r) <= UsageHistory.Lookback(r), $"{r}: la bandeja lee al menos lo que se enseña");
        }
    }
}
