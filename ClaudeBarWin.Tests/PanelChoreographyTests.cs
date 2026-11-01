using System.Drawing;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Apertura y cierre del panel (lenguaje de movimiento §4 #1–#2): opacidad con Rápido aplicada como
/// alfa de ventana layered, cierre con un desplazamiento de 0 → 4 px hacia la bandeja y <c>Hide</c>
/// cuando el alfa baja de 0,03. Geometría y curvas puras, sin WinForms.
/// </summary>
public class PanelChoreographyTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Tray_is_below_with_the_taskbar_at_the_bottom()
        => Assert.Equal(1, PanelChoreography.TrayDirection(Screen, new Rectangle(0, 0, 1920, 1040)));

    [Fact]
    public void Tray_is_above_with_the_taskbar_on_top()
        => Assert.Equal(-1, PanelChoreography.TrayDirection(Screen, new Rectangle(0, 40, 1920, 1040)));

    [Fact]
    public void Side_taskbars_keep_the_default_direction()
        => Assert.Equal(1, PanelChoreography.TrayDirection(Screen, new Rectangle(60, 0, 1860, 1080)));

    [Theory]
    [InlineData(1.0, 1.0, 0)]
    [InlineData(0.5, 1.0, 2)]
    [InlineData(0.0, 1.0, 4)]
    [InlineData(0.4, 0.8, 2)]     // opacidad configurada < 1: el recorrido se mide sobre el objetivo
    public void Close_shift_follows_the_opacity_in_whole_pixels(double opacity, double target, int expected)
        => Assert.Equal(expected, PanelChoreography.CloseShiftPx(opacity, target, scale: 1, direction: 1));

    [Fact]
    public void Close_shift_goes_toward_the_tray()
    {
        Assert.Equal(-4, PanelChoreography.CloseShiftPx(0, 1, 1, direction: -1));
        Assert.Equal(6, PanelChoreography.CloseShiftPx(0, 1, 1.5, direction: 1));
    }

    [Fact]
    public void Hide_when_alpha_drops_below_3_percent_around_180ms()
    {
        Assert.False(PanelChoreography.ShouldHide(0.05));
        Assert.True(PanelChoreography.ShouldHide(0.029));
        // Con Rápido, de 1 a < 0,03 en ~180 ms.
        var s = new SpringValue(1, Motion.Rapido);
        s.SetTarget(0);
        int ms = 0;
        while (!PanelChoreography.ShouldHide(s.Value) && ms < 1000) { s.Advance(0.001); ms++; }
        Assert.InRange(ms, 165, 195);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 255)]
    [InlineData(0.5, 128)]
    [InlineData(1.2, 255)]
    [InlineData(-0.1, 0)]
    public void Layered_alpha_is_a_clamped_byte(double opacity, byte expected)
        => Assert.Equal(expected, PanelChoreography.AlphaByte(opacity));

    // Apertura: Rápido, 95 % a ~159 ms.
    [Fact]
    public void Open_fade_uses_rapido()
    {
        var s = new SpringValue(0, Motion.Rapido);
        s.SetTarget(1);
        int ms = 0;
        while (s.Value < 0.95 && ms < 1000) { s.Advance(0.001); ms++; }
        Assert.InRange(ms, 153, 165);
    }
}
