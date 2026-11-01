using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="QuotaThresholdTracker"/> es PURO: decide cuándo avisar de que una ventana de cuota
/// (5 h / 7 d) cruza 80 % o 95 %. Gracia de arranque (la primera lectura nunca avisa), histéresis
/// para no repetir el aviso con el ruido de la API y rearme cuando la ventana se renueva.
/// </summary>
public class QuotaThresholdTrackerTests
{
    private static readonly DateTimeOffset A = new(2026, 10, 28, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset B = A.AddHours(5);

    private static QuotaThresholdTracker New() => new(new[] { 80, 95 });

    // 5 h con el pct dado y la 7 d con otro (por defecto, baja y estable).
    private static RealUsage U(double fiveHour, double sevenDay, DateTimeOffset? resets5h = null) => new()
    {
        FiveHour = new UsageWindow(fiveHour, resets5h ?? A),
        SevenDay = new UsageWindow(sevenDay, A.AddDays(3)),
    };

    [Fact]
    public void First_observation_never_fires()
    {
        // Arrancar con la cuota ya alta no avisa: no sabemos cuándo cruzó.
        var t = New();
        Assert.Empty(t.Observe(U(85, 10)));
    }

    [Fact]
    public void Crossing_80_fires_once()
    {
        var t = New();
        t.Observe(U(70, 10));
        var c = Assert.Single(t.Observe(U(81, 10)));
        Assert.Equal(QuotaWindowKind.FiveHour, c.Window);
        Assert.Equal(80, c.Threshold);
        Assert.Equal(81, c.Pct);
        Assert.Equal(A, c.ResetsAt);
        Assert.Empty(t.Observe(U(83, 10)));
    }

    [Fact]
    public void Jump_from_70_to_96_fires_only_95()
    {
        var t = New();
        t.Observe(U(70, 10));
        var c = t.Observe(U(96, 10));
        Assert.Equal(new[] { 95 }, c.Select(x => x.Threshold));
        // El 80 queda desarmado también: el ruido (93 → 96) no repite el 95 ni suelta el 80.
        Assert.Empty(t.Observe(U(93, 10)));
        Assert.Empty(t.Observe(U(96, 10)));
    }

    [Fact]
    public void Hysteresis_requires_drop_below_threshold_minus_3()
    {
        // 81 → 78.5 → 81: no baja de 77, sigue desarmado.
        var t = New();
        t.Observe(U(70, 10));
        Assert.Single(t.Observe(U(81, 10)));
        Assert.Empty(t.Observe(U(78.5, 10)));
        Assert.Empty(t.Observe(U(81, 10)));

        // 81 → 76 → 81: bajó de 77, se rearma y vuelve a avisar.
        var t2 = New();
        t2.Observe(U(70, 10));
        Assert.Single(t2.Observe(U(81, 10)));
        Assert.Empty(t2.Observe(U(76, 10)));
        Assert.Single(t2.Observe(U(81, 10)));
    }

    [Fact]
    public void Starting_above_threshold_rearms_after_dropping_below_hysteresis()
    {
        // Review Focus 2: arranque ≥80 → nada hasta que baje y vuelva a cruzar.
        var t = New();
        Assert.Empty(t.Observe(U(85, 10)));
        Assert.Empty(t.Observe(U(88, 10)));
        Assert.Empty(t.Observe(U(70, 10)));
        Assert.Single(t.Observe(U(82, 10)));
    }

    [Fact]
    public void Reset_rearms()
    {
        var t = New();
        Assert.Empty(t.Observe(U(81, 10, A)));          // gracia de arranque
        Assert.Empty(t.Observe(U(5, 10, B)));           // ventana nueva (B posterior a A)
        var c = Assert.Single(t.Observe(U(82, 10, B)));
        Assert.Equal(80, c.Threshold);
        Assert.Equal(B, c.ResetsAt);
    }

    [Fact]
    public void Reset_arriving_already_above_threshold_fires_in_the_new_window()
    {
        // Si la primera lectura de la ventana nueva ya supera el umbral, ese cruce es de la ventana nueva.
        var t = New();
        t.Observe(U(70, 10, A));
        Assert.Single(t.Observe(U(81, 10, A)));
        var c = Assert.Single(t.Observe(U(83, 10, B)));
        Assert.Equal(80, c.Threshold);
    }

    [Fact]
    public void ResetsAt_jitter_does_not_rearm()
    {
        // La API mueve el resets_at unos segundos entre lecturas: eso NO es una ventana nueva.
        var t = New();
        t.Observe(U(70, 10, A));
        Assert.Single(t.Observe(U(81, 10, A)));
        Assert.Empty(t.Observe(U(82, 10, A.AddSeconds(40))));
        Assert.Empty(t.Observe(U(83, 10, A.AddMinutes(2))));
    }

    [Fact]
    public void ResetsAt_moving_backwards_does_not_rearm()
    {
        var t = New();
        t.Observe(U(70, 10, B));
        Assert.Single(t.Observe(U(81, 10, B)));
        Assert.Empty(t.Observe(U(82, 10, A)));
    }

    [Fact]
    public void Windows_are_independent()
    {
        var t = New();
        t.Observe(U(70, 50));
        var c = Assert.Single(t.Observe(U(81, 60)));
        Assert.Equal(QuotaWindowKind.FiveHour, c.Window);

        // Ahora cruza la 7 d (a 95 directamente) mientras la 5 h sigue arriba sin repetir.
        var c2 = Assert.Single(t.Observe(U(82, 96)));
        Assert.Equal(QuotaWindowKind.SevenDay, c2.Window);
        Assert.Equal(95, c2.Threshold);
    }

    [Fact]
    public void Missing_window_is_ignored_and_keeps_state()
    {
        var t = New();
        t.Observe(U(70, 10));
        Assert.Empty(t.Observe(new RealUsage { FiveHour = null, SevenDay = null }));
        Assert.Single(t.Observe(U(81, 10)));
    }

    [Fact]
    public void Exactly_at_threshold_fires()
    {
        var t = New();
        t.Observe(U(79.9, 10));
        Assert.Equal(new[] { 80 }, t.Observe(U(80, 10)).Select(x => x.Threshold));
    }

    [Fact]
    public void Thresholds_are_accepted_in_any_order()
    {
        var t = new QuotaThresholdTracker(new[] { 95, 80 });
        t.Observe(U(70, 10));
        Assert.Equal(new[] { 95 }, t.Observe(U(97, 10)).Select(x => x.Threshold));
    }
}
