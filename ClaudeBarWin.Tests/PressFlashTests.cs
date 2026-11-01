using System.Drawing;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Micro-interacción 3 (lenguaje de movimiento §4): al hacer clic en una fila de sesión su fondo
/// destella al +14 % de luminosidad y vuelve con Rápido (~160 ms). Puro, dt en segundos.
/// </summary>
public class PressFlashTests
{
    private static readonly RectangleF R = new(10, 20, 200, 16);

    [Fact]
    public void Flash_starts_full_and_fades_with_rapido()
    {
        var f = new PressFlash();
        Assert.False(f.Visible);
        f.Trigger("live:abc", R);
        Assert.True(f.Visible);
        Assert.Equal(1.0, f.Intensity);
        Assert.Equal(R, f.Rect);
        Assert.Equal("live:abc", f.Key);
        int ms = 0;
        while (f.Intensity > 0.05 && ms < 1000) { f.Advance(0.001); ms++; }
        Assert.InRange(ms, 153, 165);
        while (f.IsAnimating) f.Advance(0.001);
        Assert.False(f.Visible);
    }

    [Fact]
    public void Snap_skips_the_flash()
    {
        var f = new PressFlash();
        f.Trigger("live:abc", R, snap: true);
        Assert.False(f.Visible);
        Assert.False(f.IsAnimating);
    }

    // Luminosidades del §4: hover +8 % y destello +14 % hacia el primer plano.
    [Fact]
    public void Theme_hover_and_press_tokens_follow_the_motion_language()
    {
        foreach (var t in new[] { Theme.Dark, Theme.Light })
        {
            Assert.Equal(ColorMath.Lerp(t.Background, t.Foreground, 0.08), t.HoverBg);
            var expected = ColorMath.Lerp(t.Background, t.Foreground, 0.14);
            Assert.InRange(Math.Abs(t.PressBg.R - expected.R), 0, 1);
            Assert.InRange(Math.Abs(t.PressBg.G - expected.G), 0, 1);
            Assert.InRange(Math.Abs(t.PressBg.B - expected.B), 0, 1);
        }
        Assert.NotEqual(Theme.Cli.HoverBg.ToArgb(), Theme.Cli.PressBg.ToArgb());
    }
}
