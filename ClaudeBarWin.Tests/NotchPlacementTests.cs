using System.Drawing;
using System.Drawing.Drawing2D;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary><see cref="NotchPlacement"/> (escala, superficie y posición del notch) y la zona de clic con
/// alfa 1/255 de <see cref="NotchGeometry.HitPadPath"/>.</summary>
public class NotchPlacementTests
{
    [Theory]
    [InlineData(96, 0, 1920, 1.0f)]      // Auto, 1080p → 1
    [InlineData(144, 0, 3840, 1.725f)]   // 150 % en 4K: 2560 lógicos → 1,15 × 1,5
    [InlineData(96, 0, 3840, 1.3f)]      // 4K al 100 %: 3840 lógicos → 1,3
    [InlineData(96, 130, 1920, 1.3f)]    // tamaño fijo, ignora el monitor
    [InlineData(120, 150, 1920, 1.875f)]
    [InlineData(0, 100, 1920, 1.0f)]     // DPI inválido → 96
    public void Scale_combines_dpi_and_size(int dpi, int pct, int physWidth, float expected) =>
        Assert.Equal(expected, NotchPlacement.Scale(dpi, pct, physWidth), 3);

    [Theory]
    [InlineData(1f, 700, 272)]
    [InlineData(1.5f, 1050, 408)]
    [InlineData(1.15f, 805, 313)]       // 700 × 1,15f no debe subir a 806 por coma flotante
    [InlineData(1.725f, 1208, 470)]
    public void Surface_is_700_by_272_scaled_up(float s, int w, int h) =>
        Assert.Equal(new Size(w, h), NotchPlacement.Surface(s));

    // ---- Decisión del dueño: la superficie solo crece a 272 con la página Uso (y en su transición) ----

    [Theory]
    [InlineData(1f, 700, 200)]
    [InlineData(1.5f, 1050, 300)]
    [InlineData(1.15f, 805, 230)]
    public void The_surface_is_200_high_unless_the_usage_page_needs_it(float s, int w, int h)
    {
        Assert.Equal(new Size(w, h), NotchPlacement.SurfaceFor(s, tall: false));
        Assert.Equal(NotchPlacement.Surface(s), NotchPlacement.SurfaceFor(s, tall: true));
    }

    [Fact]
    public void Every_page_but_usage_fits_the_short_surface_with_its_shadow()
    {
        float room = NotchPlacement.CompactSurfaceHeight - ClaudeBarWin.Services.Notch.NotchMotion.ShadowBlurRadius
            - ClaudeBarWin.Services.Notch.NotchMotion.ShadowOffsetY;
        Assert.True(ClaudeBarWin.Services.Notch.NotchLayout.ExpandedMaxHeight <= room);
    }

    [Fact]
    public void The_tall_surface_is_kept_while_the_usage_page_is_active_or_its_shape_is_still_shrinking()
    {
        const float s = 1.5f;
        float usage = ClaudeBarWin.Services.Notch.NotchLayout.UsagePageMaxHeight * s, claude = 120f * s;
        // Otras páginas, quietas: superficie baja.
        Assert.False(NotchPlacement.NeedsTallSurface(usagePage: false, claude, default, s));
        // Uso activa (objetivo, o en el cruce de páginas): alta, aunque la forma aún no haya crecido.
        Assert.True(NotchPlacement.NeedsTallSurface(usagePage: true, claude, default, s));
        // Saliendo de Uso: alta mientras la forma pintada siga más alta que el tope de las demás…
        Assert.True(NotchPlacement.NeedsTallSurface(usagePage: false, usage - 10f, default, s));
        // …y mientras la zona de hover retenga la forma vieja (unión vieja ∪ nueva).
        Assert.True(NotchPlacement.NeedsTallSurface(usagePage: false, claude, new HoverHold(new SizeF(600, usage), new SizeF(480, claude)), s));
        // Ya asentada en una página normal: vuelve a la baja.
        Assert.False(NotchPlacement.NeedsTallSurface(usagePage: false, ClaudeBarWin.Services.Notch.NotchLayout.ExpandedMaxHeight * s,
            default, s));
    }

    [Fact]
    public void Surface_fits_the_largest_mode_with_ears_and_shadow()
    {
        var surface = NotchPlacement.Surface(1f);
        float widest = 520f + 2 * 8f + 2 * 24f;     // Expandido + orejas + sombra
        Assert.True(surface.Width >= widest * 1.02f);
        Assert.True(surface.Height >= 150f + 24f + 3f);
    }

    [Fact]
    public void TopLeft_is_centered_on_the_monitor_top_edge()
    {
        Assert.Equal(new Point(610, 0), NotchPlacement.TopLeft(new Rectangle(0, 0, 1920, 1080), new Size(700, 200)));
        Assert.Equal(new Point(-1310, -200),
            NotchPlacement.TopLeft(new Rectangle(-1920, -200, 1920, 1080), new Size(700, 200)));
    }

    [Fact]
    public void HitPad_matches_the_hit_test_away_from_its_edge()
    {
        var surface = new Size(700, 200);
        using var pad = NotchGeometry.HitPadPath(150, 30, 1f, surface);
        // Dentro: centro, margen lateral (pastilla 275..425 + 4) y margen inferior (30 + 4).
        foreach (var p in new[] { new Point(350, 15), new Point(427, 15), new Point(350, 32) })
        {
            Assert.True(pad.IsVisible(p.X + 0.5f, p.Y + 0.5f), p.ToString());
            Assert.True(NotchGeometry.HitTest(p, 150, 30, 1f, surface), p.ToString());
        }
        // Fuera: más allá del margen y en la esquina inferior redondeada.
        foreach (var p in new[] { new Point(432, 15), new Point(350, 37), new Point(271, 33) })
        {
            Assert.False(pad.IsVisible(p.X + 0.5f, p.Y + 0.5f), p.ToString());
            Assert.False(NotchGeometry.HitTest(p, 150, 30, 1f, surface), p.ToString());
        }
    }

    [Fact]
    public void HitPad_covers_the_minimum_zone_in_Minimal()
    {
        var surface = new Size(700, 200);
        using var pad = NotchGeometry.HitPadPath(44, 4, 1f, surface);
        Assert.True(pad.IsVisible(305.5f, 8.5f));     // 100 × 10 centrado: 300..400
        Assert.False(pad.IsVisible(295.5f, 2.5f));
        Assert.False(pad.IsVisible(350.5f, 12.5f));
    }

    [Fact]
    public void HitPad_is_empty_when_hidden()
    {
        using var pad = NotchGeometry.HitPadPath(0, 0, 1f, new Size(700, 200));
        Assert.Equal(0, pad.PointCount);
    }
}
