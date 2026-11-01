using System.Drawing;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Tests;

/// <summary><see cref="NotchMotion"/>: orejas, squash-stretch y opacidad de sombra (spec de movimiento §3.0).</summary>
public class NotchMotionTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Ears_go_from_4s_to_8s_with_width(float s)
    {
        Assert.Equal(4 * s, NotchMotion.EarSize(30 * s, s), 3);
        Assert.Equal(6 * s, NotchMotion.EarSize(85 * s, s), 3);
        Assert.Equal(8 * s, NotchMotion.EarSize(500 * s, s), 3);
    }

    [Fact]
    public void Squash_only_on_big_width_changes_and_saturates()
    {
        Assert.Equal(0f, NotchMotion.Squash(1500, 99, 1f));
        Assert.Equal(0.5f, NotchMotion.Squash(1000, 150, 1f), 3);
        Assert.Equal(-1f, NotchMotion.Squash(-9000, -270, 1f));
        Assert.Equal(0.5f, NotchMotion.Squash(1500, 225, 1.5f), 3);    // escala: velocidad y Δ en px físicos
    }

    [Fact]
    public void Squashed_stretches_width_and_compresses_height()
    {
        var sz = NotchMotion.Squashed(400, 140, -1f);
        Assert.Equal(408f, sz.Width, 3);
        Assert.Equal(134.4f, sz.Height, 3);
        Assert.Equal(new SizeF(300, 34), NotchMotion.Squashed(300, 34, 0));
    }

    [Theory]
    [InlineData(NotchMode.Idle, 0f)]
    [InlineData(NotchMode.Minimal, 0f)]
    [InlineData(NotchMode.Working, 0.4f)]
    [InlineData(NotchMode.QuotaHud, 0.4f)]
    [InlineData(NotchMode.Peek, 0.6f)]
    [InlineData(NotchMode.Expanded, 1f)]
    [InlineData(NotchMode.Permission, 1f)]
    public void Shadow_opacity_by_mode(NotchMode m, float e) => Assert.Equal(e, NotchMotion.ShadowOpacity(m));
}
