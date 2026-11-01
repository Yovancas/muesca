using ClaudeBarWin.Services;

public class QuotaPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact] public void No_statusline_calls_every_tick() => Assert.True(QuotaPolicy.ShouldCallApi(null, Now.AddSeconds(-60), Now));
    [Fact] public void Stale_statusline_calls_every_tick() => Assert.True(QuotaPolicy.ShouldCallApi(Now.AddMinutes(-11), Now.AddSeconds(-60), Now));
    [Fact]
    public void Never_twice_in_under_a_minute_whatever_the_refresh()
    {
        // Refresco de 30 s, 15 s a mano o «Actualizar ahora» repetido: el límite de /usage es por cuenta.
        Assert.False(QuotaPolicy.ShouldCallApi(null, Now.AddSeconds(-5), Now));
        Assert.False(QuotaPolicy.ShouldCallApi(null, Now.AddSeconds(-30), Now));
        Assert.True(QuotaPolicy.ShouldCallApi(null, Now - QuotaPolicy.MinApiGap, Now));
        // El tick de 1 min puede llegar unos ms antes: no se salta una vuelta.
        Assert.True(QuotaPolicy.ShouldCallApi(null, Now.AddSeconds(-59.98), Now));
    }
    [Fact] public void Fresh_statusline_waits_15_min() => Assert.False(QuotaPolicy.ShouldCallApi(Now.AddSeconds(-30), Now.AddMinutes(-14), Now));
    [Fact] public void Fresh_statusline_calls_after_15_min() => Assert.True(QuotaPolicy.ShouldCallApi(Now.AddSeconds(-30), Now.AddMinutes(-15), Now));
    [Fact] public void First_tick_always_calls() => Assert.True(QuotaPolicy.ShouldCallApi(Now, DateTime.MinValue, Now));
    [Fact] public void Fresh_boundary_is_ten_minutes()
    {
        Assert.True(QuotaPolicy.IsFresh(Now.AddMinutes(-9.9), Now));
        Assert.False(QuotaPolicy.IsFresh(Now.AddMinutes(-10), Now));
        Assert.False(QuotaPolicy.IsFresh(null, Now));
    }
    [Fact] public void Hub_takes_only_readings_of_this_run()
    {
        // Caché de horas antes de arrancar: fuera (gastaría la gracia del tracker). Statusline recién escrita o
        // cualquier lectura posterior al arranque: dentro.
        Assert.False(QuotaPolicy.IsCurrentForRun(Now.AddHours(-3), Now));
        Assert.False(QuotaPolicy.IsCurrentForRun(Now.AddMinutes(-10), Now));
        Assert.True(QuotaPolicy.IsCurrentForRun(Now.AddMinutes(-9.9), Now));
        Assert.True(QuotaPolicy.IsCurrentForRun(Now.AddSeconds(30), Now));
    }
}
