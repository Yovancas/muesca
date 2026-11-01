using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;
using Microsoft.Data.Sqlite;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Lo que la bandeja lee para la gráfica del % de la página Uso, con un SQLite de verdad en un archivo temporal: agrupado por
/// TIEMPO (vale la última muestra de cada casilla: un reinicio es una caída, no una rampa; los huecos de la app cerrada se
/// quedan en hueco) y con la hora a la que se leyó como «ahora».
/// </summary>
public sealed class UsageSeriesLoaderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 30, 17, 20, 0, DateTimeKind.Utc);
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"cb-usage-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }

    private static RealUsage Usage(double five, double seven) =>
        new() { FiveHour = new UsageWindow(five, null), SevenDay = new UsageWindow(seven, null) };

    /// <summary>24 h de muestras cada 5 min: 5 h en 90 % hasta que se reinicia hace 10 h y luego sube despacio; la app
    /// estuvo cerrada de −8 h a −5 h.</summary>
    private UsageHistoryStore Day()
    {
        var store = new UsageHistoryStore(_db);
        for (var t = Now.AddHours(-24); t <= Now; t = t.AddMinutes(5))
        {
            if (t > Now.AddHours(-8) && t < Now.AddHours(-5)) continue;
            double five = t < Now.AddHours(-10) ? 90 : (t - Now.AddHours(-10)).TotalMinutes * 0.1;
            store.Append(Usage(five, 40), t);
        }
        return store;
    }

    [Fact]
    public void The_percent_goes_by_time_with_the_last_sample_and_keeps_the_gaps()
    {
        var store = Day();
        var (points, at) = UsageSeriesLoader.ReadPercent(ChartRange.Day1, store, Now);
        Assert.Equal(Now, at);
        int n = UsageRanges.PercentBuckets(ChartRange.Day1);   // 96 casillas de 15 min
        var five = UsageBuckets.Percent(points, p => p.FivePct, Now.AddHours(-24), Now, n, UsageBuckets.MaxGap(TimeSpan.FromHours(24), n));

        // El reinicio de hace 10 h (inicio de la casilla 56): 90 % justo antes y ~1 % justo después, sin rampa.
        Assert.Equal(90, five[55], 3);
        Assert.InRange(five[56], 0, 1.5);
        // La app cerrada de −8 h (casilla 64) a −5 h (casilla 76): hueco en medio.
        Assert.False(double.IsNaN(five[64]));
        for (int i = 65; i < 76; i++) Assert.True(double.IsNaN(five[i]), $"casilla {i} rellena en pleno hueco");
        Assert.False(double.IsNaN(five[76]));

        // Aligerar no cambia nada: lo mismo que con todas las muestras, con como mucho cuatro por casilla.
        var all = store.QueryPercent(Now.AddHours(-24), Now, maxPoints: 0);
        Assert.Equal(UsageBuckets.Percent(all, p => p.FivePct, Now.AddHours(-24), Now, n, UsageBuckets.MaxGap(TimeSpan.FromHours(24), n)), five);
        Assert.Equal(UsageBuckets.Percent(all, p => p.SevenPct, Now.AddHours(-24), Now, n, UsageBuckets.MaxGap(TimeSpan.FromHours(24), n)),
            UsageBuckets.Percent(points, p => p.SevenPct, Now.AddHours(-24), Now, n, UsageBuckets.MaxGap(TimeSpan.FromHours(24), n)));
        Assert.True(points.Count <= 4 * n);
    }

    [Fact]
    public async Task The_chart_now_is_the_one_the_percent_was_read_at()
    {
        var store = new UsageHistoryStore(_db);
        store.Append(Usage(10, 40), Now.AddMinutes(-10));
        store.Append(Usage(64, 41), Now.AddMilliseconds(300));
        // El reloj avanza de llamada en llamada (la muestra de +300 ms se escribe entre la primera y la segunda): con un
        // «ahora» tomado antes de leer, esa muestra quedaría después de «ahora» y la gráfica no la contaría.
        var clock = new Queue<DateTime>(new[] { Now.AddMilliseconds(200), Now.AddMilliseconds(400), Now.AddMilliseconds(600) });
        var data = await UsageSeriesLoader.LoadAsync(ChartRange.Hours5, Task.FromResult(new List<HistoryBucket>()), store,
            () => clock.Count > 1 ? clock.Dequeue() : clock.Peek());
        Assert.All(data.Percent, p => Assert.True(p.TsUtc <= data.AtUtc, $"{p.TsUtc:O} después de ahora ({data.AtUtc:O})"));
        var five = UsageBuckets.Percent(data.Percent, p => p.FivePct, data.AtUtc - TimeSpan.FromHours(5), data.AtUtc,
            UsageRanges.PercentBuckets(ChartRange.Hours5), UsageBuckets.MaxGap(TimeSpan.FromHours(5), 60));
        Assert.Equal(data.Percent[^1].FivePct, five[^1]);   // lo último leído es lo último que se pinta

        // Lo escrito antes de leer cuenta: «ahora» es el de la lectura.
        var later = await UsageSeriesLoader.LoadAsync(ChartRange.Hours5, Task.FromResult(new List<HistoryBucket>()), store,
            () => Now.AddMilliseconds(500));
        Assert.Equal(Now.AddMilliseconds(500), later.AtUtc);
        Assert.Equal(64, later.Percent[^1].FivePct);
    }

    [Fact]
    public void Edges_per_bucket_keep_the_first_and_last_sample_of_each_series()
    {
        var from = Now.AddHours(-1);
        var pts = new List<PctPoint>
        {
            new(from.AddMinutes(0.2), 10, 20),       // la primera de las dos series
            new(from.AddMinutes(0.5), 11, null),   // la última con 5 h de la casilla 0
            new(from.AddMinutes(0.4), null, 22),   // la última con 7 d (fuera de orden: cuenta la hora)
            new(from.AddMinutes(-5), 99, 99),      // fuera del tramo
        };
        var kept = UsageBuckets.EdgesPerBucket(pts, from, Now, 60);
        Assert.Equal(new[] { pts[0], pts[2], pts[1] }, kept);
        Assert.Empty(UsageBuckets.EdgesPerBucket(pts, Now, Now, 60));
    }
}
