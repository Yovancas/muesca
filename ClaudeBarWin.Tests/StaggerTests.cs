using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="Stagger"/> (lenguaje de movimiento §4 #3): entrada escalonada de las secciones del panel,
/// PURA y sin estado. 30 ms por índice con 5 pasos como máximo; una sola traslación desde la bandeja
/// y(t) = 6(1 + ωt)e^(−ωt) con ω = √300, en px enteros (llega a 0 a los ~230 ms); y un fundido por
/// sección con Rápido. El <c>y</c> de layout NO depende de esto: solo desplaza el dibujo de su celda.
/// </summary>
public class StaggerTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 30)]
    [InlineData(4, 120)]
    [InlineData(5, 120)]   // a partir de la 6.ª entran con la 5.ª
    [InlineData(9, 120)]
    public void Start_is_30ms_per_index_with_at_most_five_steps(int index, double startMs)
        => Assert.Equal(startMs, Stagger.StartMs(index));

    [Fact]
    public void Offset_starts_at_6px_from_the_tray_and_reaches_0_around_230ms()
    {
        Assert.Equal(6, Stagger.OffsetPx(0, 0));
        Assert.Equal(0, Stagger.OffsetPx(260, 0));
        int zeroAt = -1;
        for (int t = 0; t <= 400; t++)
            if (Stagger.OffsetPx(t, 0) == 0) { zeroAt = t; break; }
        Assert.InRange(zeroAt, 220, 245);
    }

    [Fact]
    public void Offset_matches_the_closed_critical_curve_in_whole_pixels()
    {
        double w = Math.Sqrt(300);
        for (int t = 0; t <= 300; t += 7)
        {
            double s = t / 1000.0;
            int expected = (int)Math.Round(6 * (1 + w * s) * Math.Exp(-w * s), MidpointRounding.AwayFromZero);
            Assert.Equal(expected, Stagger.OffsetPx(t, 0));
        }
    }

    [Fact]
    public void Offset_never_increases_and_is_held_before_the_section_starts()
    {
        Assert.Equal(6, Stagger.OffsetPx(10, 2));   // la 3.ª arranca a 60 ms
        int prev = int.MaxValue;
        for (int t = 0; t <= 500; t += 3)
        {
            int o = Stagger.OffsetPx(t, 3);
            Assert.True(o <= prev);
            prev = o;
        }
    }

    // Con la bandeja arriba las secciones bajan: −6 → 0.
    [Fact]
    public void Direction_flips_the_sign()
    {
        Assert.Equal(-6, Stagger.OffsetPx(0, 0, direction: -1));
        Assert.Equal(0, Stagger.OffsetPx(400, 0, direction: -1));
    }

    [Fact]
    public void Offset_scales_with_dpi()
        => Assert.Equal(9, Stagger.OffsetPx(0, 0, scale: 1.5));

    [Fact]
    public void Fade_follows_rapido_from_the_section_start()
    {
        Assert.Equal(0.0, Stagger.FadeAlpha(0, 0), 9);
        Assert.Equal(0.0, Stagger.FadeAlpha(25, 1), 9);   // aún no ha empezado
        double a95 = Stagger.FadeAlpha(159, 0);
        Assert.InRange(a95, 0.94, 0.96);                   // Rápido: 95 % a 159 ms
        Assert.Equal(1.0, Stagger.FadeAlpha(1000, 0), 9);
    }

    [Fact]
    public void Active_until_the_last_section_settles()
    {
        // Apertura completa ≈ 120 + 230 ≈ 350 ms con 5 o más secciones.
        Assert.True(Stagger.IsActive(300, lastIndex: 6));
        Assert.False(Stagger.IsActive(450, lastIndex: 6));
        Assert.False(Stagger.IsActive(double.PositiveInfinity, lastIndex: 6));
        Assert.InRange(Stagger.SettleMs(), 230, 300);
    }
}
