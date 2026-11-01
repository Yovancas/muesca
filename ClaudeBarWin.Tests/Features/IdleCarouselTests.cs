using System.Globalization;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="IdleCarousel"/> alterna en reposo la cuota (7 s) y el reloj (5 s). PURO: el instante llega
/// por parámetro. <see cref="ClockText"/> formatea hora y fecha corta según la cultura.
/// </summary>
public class IdleCarouselTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 18, 55, 0);

    private static IdleCarousel New() => new(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(5));

    [Fact]
    public void Starts_on_quota_and_switches_after_7s()
    {
        var c = New();
        c.Restart(T0);
        Assert.Equal(IdleFace.Quota, c.Face(T0));
        Assert.Equal(IdleFace.Quota, c.Face(T0.AddMilliseconds(6999)));
        Assert.Equal(IdleFace.Clock, c.Face(T0.AddSeconds(7)));
    }

    [Fact]
    public void Clock_lasts_5s_then_back()
    {
        var c = New();
        c.Restart(T0);
        Assert.Equal(IdleFace.Clock, c.Face(T0.AddMilliseconds(11999)));
        Assert.Equal(IdleFace.Quota, c.Face(T0.AddSeconds(12)));
        // El ciclo se repite: 12 + 7 = 19 s vuelve el reloj.
        Assert.Equal(IdleFace.Clock, c.Face(T0.AddSeconds(19)));
    }

    [Fact]
    public void Restart_resets_to_quota()
    {
        var c = New();
        c.Restart(T0);
        var t = T0.AddSeconds(8);
        Assert.Equal(IdleFace.Clock, c.Face(t));
        c.Restart(t);
        Assert.Equal(IdleFace.Quota, c.Face(t));
        Assert.Equal(IdleFace.Clock, c.Face(t.AddSeconds(7)));
    }

    [Fact]
    public void NextChangeIn_points_to_the_boundary()
    {
        var c = New();
        c.Restart(T0);
        Assert.Equal(TimeSpan.FromSeconds(7), c.NextChangeIn(T0));
        Assert.Equal(TimeSpan.FromSeconds(4), c.NextChangeIn(T0.AddSeconds(3)));
        Assert.Equal(TimeSpan.FromSeconds(5), c.NextChangeIn(T0.AddSeconds(7)));
        Assert.Equal(TimeSpan.FromSeconds(1), c.NextChangeIn(T0.AddSeconds(11)));
        // Nunca cero: en la frontera exacta apunta a la siguiente.
        Assert.True(c.NextChangeIn(T0.AddSeconds(12)) > TimeSpan.Zero);
    }

    [Fact]
    public void Clock_going_backwards_restarts_on_quota()
    {
        var c = New();
        c.Restart(T0);
        var back = T0.AddMinutes(-1);
        Assert.Equal(IdleFace.Quota, c.Face(back));
        Assert.Equal(TimeSpan.FromSeconds(7), c.NextChangeIn(back));
    }

    [Fact]
    public void First_use_without_Restart_starts_on_quota()
    {
        var c = New();
        Assert.Equal(IdleFace.Quota, c.Face(T0));
        Assert.Equal(IdleFace.Clock, c.Face(T0.AddSeconds(7)));
    }

    [Fact]
    public void Rejects_non_positive_durations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdleCarousel(TimeSpan.Zero, TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdleCarousel(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Formats_24h_es()
    {
        var (time, date) = ClockText.Format(T0, CultureInfo.GetCultureInfo("es-ES"), use24h: true);
        Assert.Equal("18:55", time);
        Assert.Equal("mié 28 oct", date); // 28-oct-2026 es miércoles

        var (morning, _) = ClockText.Format(new DateTime(2026, 10, 28, 6, 5, 0), CultureInfo.GetCultureInfo("es-ES"), use24h: true);
        Assert.Equal("06:05", morning);
    }

    [Fact]
    public void Formats_12h_en()
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        var (time, date) = ClockText.Format(T0, en, use24h: false);
        Assert.Equal("6:55 " + en.DateTimeFormat.PMDesignator, time);
        Assert.Equal("6:55 PM", time);
        Assert.Equal("Wed Oct 28", date);
    }

    [Fact]
    public void Formats_12h_es_uses_culture_designator()
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        var (time, _) = ClockText.Format(T0, es, use24h: false);
        Assert.StartsWith("6:55 ", time);
        Assert.EndsWith(es.DateTimeFormat.PMDesignator.Trim(), time);
    }

    [Fact]
    public void Prefers24h_follows_the_culture_pattern()
    {
        Assert.True(ClockText.Prefers24h(CultureInfo.GetCultureInfo("es-ES")));
        Assert.False(ClockText.Prefers24h(CultureInfo.GetCultureInfo("en-US")));
    }
}
