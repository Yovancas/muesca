using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Chip «cuota renovada» (lenguaje de movimiento §4 #6): a los 80 ms de la celebración el fondo pasa
/// del 70 al 100 % de ancho con Abrir y de 0 a 1 de opacidad con Rápido; el texto entra a +60 ms con
/// alfa 0 → 1 en 120 ms, sin escalar; se mantiene <see cref="Motion.CelebrationMs"/> y sale con
/// opacidad (Rápido) y una subida de 3 px. Puro, función del tiempo.
/// </summary>
public class ChipMotionTests
{
    [Fact]
    public void Nothing_before_80ms()
    {
        var f = ChipMotion.Sample(79);
        Assert.Equal(0.0, f.BgAlpha, 9);
        Assert.Equal(0.0, f.TextAlpha, 9);
        Assert.Equal(0.7, f.BgWidthFrac, 9);
        Assert.Equal(0, f.RisePx);
    }

    [Fact]
    public void Background_blooms_from_70_percent_with_abrir_and_fades_in_with_rapido()
    {
        Assert.InRange(ChipMotion.Sample(80 + 159).BgAlpha, 0.94, 0.96);     // Rápido 95 %
        double w95 = 0.7 + 0.3 * 0.95;
        Assert.True(ChipMotion.Sample(80 + 180).BgWidthFrac < w95);
        Assert.True(ChipMotion.Sample(80 + 192).BgWidthFrac >= w95);          // Abrir 95 % a 186 ms
        double max = 0;
        for (int t = 0; t < 1500; t++) max = Math.Max(max, ChipMotion.Sample(t).BgWidthFrac);
        Assert.InRange(max, 1.005, 1.01);                                     // florece: 2,9 % de 0,3
        Assert.Equal(1.0, ChipMotion.Sample(1500).BgWidthFrac, 3);
    }

    [Fact]
    public void Text_enters_at_140ms_in_120ms_without_scaling()
    {
        Assert.Equal(0.0, ChipMotion.Sample(140).TextAlpha, 9);
        double mid = ChipMotion.Sample(200).TextAlpha;
        Assert.InRange(mid, 0.3, 0.99);
        Assert.Equal(1.0, ChipMotion.Sample(260).TextAlpha, 9);
    }

    [Fact]
    public void Holds_then_leaves_with_opacity_and_a_3px_rise()
    {
        var hold = ChipMotion.Sample(Motion.CelebrationMs - 1);
        Assert.Equal(1.0, hold.TextAlpha, 9);
        Assert.Equal(1.0, hold.BgAlpha, 3);
        Assert.Equal(0, hold.RisePx);
        var out95 = ChipMotion.Sample(Motion.CelebrationMs + 159);
        Assert.InRange(out95.BgAlpha, 0.04, 0.06);
        Assert.InRange(out95.TextAlpha, 0.04, 0.06);
        Assert.Equal(3, out95.RisePx);
        Assert.Equal(3, ChipMotion.Sample(Motion.CelebrationMs + 1000, scale: 1).RisePx);
        Assert.Equal(5, ChipMotion.Sample(Motion.CelebrationMs + 1000, scale: 1.5).RisePx);
    }

    [Fact]
    public void Total_covers_the_exit_and_animating_only_while_moving()
    {
        Assert.InRange(ChipMotion.TotalMs, Motion.CelebrationMs + 250, Motion.CelebrationMs + 320);
        Assert.True(ChipMotion.IsAnimating(10));
        Assert.True(ChipMotion.IsAnimating(300));
        Assert.False(ChipMotion.IsAnimating(1500));                         // pleno y quieto
        Assert.True(ChipMotion.IsAnimating(Motion.CelebrationMs + 50));
        Assert.False(ChipMotion.IsAnimating(ChipMotion.TotalMs + 1));
        Assert.False(ChipMotion.IsAnimating(-1));
    }

    [Fact]
    public void Dirty_rect_grows_6px_upward()
        => Assert.Equal(6, Motion.ChipDirtyUpPx);
}
