using System.Drawing;
using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// F3 Task 6: relayout atómico con el alto animado anclado al borde, chip de celebración con
/// escala+alfa y crossfade del color de la mascota. Todo puro (geometría y curvas), sin GDI+.
/// </summary>
public class PanelTransitionsTests
{
    private static readonly Rectangle Wa = new(0, 0, 1920, 1040);
    private const int M = 8;

    [Theory]
    [InlineData("BottomRight")]
    [InlineData("BottomLeft")]
    [InlineData("")]               // por defecto: abajo a la derecha
    public void Anchored_height_keeps_bottom_edge(string position)
    {
        var a = PanelPlacement.Bounds(position, Wa, new Size(340, 300), M, custom: null);
        var b = PanelPlacement.Bounds(position, Wa, new Size(340, 457), M, custom: null);
        Assert.Equal(a.Bottom, b.Bottom);
        Assert.Equal(Wa.Bottom - M, b.Bottom);
        Assert.Equal(a.X, b.X);
        Assert.Equal(457, b.Height);
    }

    [Theory]
    [InlineData("TopRight")]
    [InlineData("TopLeft")]
    public void Top_anchored_height_keeps_top_edge(string position)
    {
        var a = PanelPlacement.Bounds(position, Wa, new Size(340, 300), M, custom: null);
        var b = PanelPlacement.Bounds(position, Wa, new Size(340, 457), M, custom: null);
        Assert.Equal(a.Top, b.Top);
        Assert.Equal(Wa.Top + M, b.Top);
    }

    [Fact]
    public void Custom_keeps_its_top_left_and_center_stays_centered()
    {
        var c = PanelPlacement.Bounds("Custom", Wa, new Size(340, 500), M, custom: new Point(100, 200));
        Assert.Equal(new Rectangle(100, 200, 340, 500), c);

        var a = PanelPlacement.Bounds("Center", Wa, new Size(340, 300), M, custom: null);
        var b = PanelPlacement.Bounds("Center", Wa, new Size(340, 500), M, custom: null);
        Assert.InRange(Math.Abs((a.Top + a.Bottom) - (b.Top + b.Bottom)), 0, 1);
    }

    [Fact]
    public void Color_crossfade_blends_and_settles()
    {
        var cf = new ColorCrossfade(Color.FromArgb(255, 0, 0, 0));
        Assert.False(cf.IsAnimating);
        cf.SetTarget(Color.FromArgb(255, 200, 100, 50));
        Assert.True(cf.IsAnimating);
        cf.Advance(1 / 60.0);
        var mid = cf.Current;
        Assert.InRange(mid.R, 1, 199);                            // mezcla, no salto
        for (int i = 0; i < 120; i++) cf.Advance(1 / 60.0);
        Assert.False(cf.IsAnimating);
        Assert.Equal(Color.FromArgb(255, 200, 100, 50).ToArgb(), cf.Current.ToArgb());

        // Cambio a mitad: parte del color mostrado, sin saltar.
        cf.SetTarget(Color.FromArgb(255, 0, 0, 255));
        cf.Advance(1 / 60.0);
        var shown = cf.Current;
        cf.SetTarget(Color.FromArgb(255, 0, 255, 0));
        Assert.Equal(shown.ToArgb(), cf.Current.ToArgb());

        cf.SetTarget(Color.White, snap: true);                    // reduce-motion / cambio de tema
        Assert.False(cf.IsAnimating);
        Assert.Equal(Color.White.ToArgb(), cf.Current.ToArgb());
    }
}
