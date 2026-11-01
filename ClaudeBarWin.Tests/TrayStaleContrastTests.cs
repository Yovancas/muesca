using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T6b — regresión de 5ef80c1: el badge STALE pintaba el fondo translúcido (alpha 150) pero decidía
/// el color del dígito sobre el color FRESCO opaco. Sobre barra de tareas oscura el color compuesto
/// real caía al otro lado del flip WCAG y el dígito quedaba a ~2:1 (12%: negro sobre #244726 = 2.00:1;
/// 68%: 2.07:1). Estos tests miden el contraste del ink REAL contra el fondo COMPUESTO (como lo ve el
/// usuario), en barra oscura y clara.
/// </summary>
public class TrayStaleContrastTests
{
    private const double Warn = 70, Crit = 90;

    /// <summary>Compone el icono sobre el color de barra de tareas, como hace Windows en el tray.</summary>
    private static Bitmap ComposeOnBar(Icon ico, Color bar)
    {
        var b = new Bitmap(48, 48);
        using var g = Graphics.FromImage(b);
        g.Clear(bar);
        using var ib = ico.ToBitmap();
        g.DrawImage(ib, 0, 0, 48, 48);
        return b;
    }

    /// <summary>
    /// Contraste efectivo del dígito: relleno muestreado en el borde izquierdo (lejos de tinta y
    /// overlay) y, como ink, el píxel del área central que MÁS contrasta con ese relleno (el núcleo
    /// del glifo; los bordes AA contrastan menos, nunca más).
    /// </summary>
    private static (double ratio, Color ink, Color fill) InkContrast(Bitmap composed)
    {
        Color fill = composed.GetPixel(3, 24);
        double best = 1.0;
        Color ink = fill;
        for (int y = 10; y <= 38; y++)
            for (int x = 8; x <= 40; x++)
            {
                var p = composed.GetPixel(x, y);
                double r = ColorMath.ContrastRatio(p, fill);
                if (r > best) { best = r; ink = p; }
            }
        return (best, ink, fill);
    }

    [Theory]
    [InlineData(12, UsageStatus.Ok)]        // regresó a 2.00:1 (negro sobre verde compuesto oscuro)
    [InlineData(68, UsageStatus.Warn)]      // regresó a 2.07:1
    [InlineData(95, UsageStatus.Critical)]  // no regresó (blanco), debe seguir cumpliendo
    public void Stale_digit_is_readable_on_dark_taskbar(int pct, UsageStatus st)
    {
        using var ico = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: true,
            pending: false, taskbarLight: false);
        using var composed = ComposeOnBar(ico, TaskbarColors.Dark);
        var (ratio, ink, fill) = InkContrast(composed);
        Assert.True(ratio >= 4.5,
            $"stale {pct}% sobre barra oscura: ink {ink} sobre relleno efectivo {fill} = {ratio:0.00}:1 (< 4.5)");
    }

    [Theory]
    [InlineData(12, UsageStatus.Ok)]
    [InlineData(68, UsageStatus.Warn)]
    [InlineData(95, UsageStatus.Critical)]
    public void Stale_digit_is_readable_on_light_taskbar(int pct, UsageStatus st)
    {
        using var ico = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: true,
            pending: false, taskbarLight: true);
        using var composed = ComposeOnBar(ico, TaskbarColors.Light);
        var (ratio, ink, fill) = InkContrast(composed);
        Assert.True(ratio >= 4.5,
            $"stale {pct}% sobre barra clara: ink {ink} sobre relleno efectivo {fill} = {ratio:0.00}:1 (< 4.5)");
    }

    [Fact]
    public void StaleFill_is_opaque_and_its_contrast_side_meets_aa_for_all_risk_colors()
    {
        // Propiedad del fix (b): el relleno stale pre-compuesto es OPACO, así Contrast() decide sobre
        // el color REAL en pantalla. En el punto de cruce WCAG (L≈0.179) negro y blanco empatan a
        // (L+0.05)/0.05 ≈ 4.58:1 — el lado elegido garantiza ≥4.5:1 para CUALQUIER relleno opaco.
        foreach (bool light in new[] { false, true })
            for (int pct = 0; pct <= 100; pct += 5)
            {
                var bg = ColorMath.RiskColor(pct, Theme.Dark, Warn, Crit);
                var fill = TrayIconRenderer.StaleFill(bg, light);
                Assert.Equal(255, fill.A);
                var ink = ColorMath.Contrast(fill);
                double ratio = ColorMath.ContrastRatio(ink, fill);
                Assert.True(ratio >= 4.5,
                    $"pct={pct} barra {(light ? "clara" : "oscura")}: {ink} sobre {fill} = {ratio:0.00}:1");
            }
    }

    [Fact]
    public void Stale_ink_flips_to_white_on_dark_taskbar_for_the_regressed_badges()
    {
        // Pin de la regresión: 12% y 68% (verde/ámbar de riesgo) compuestos sobre barra oscura caen
        // por debajo del flip → el ink debe ser BLANCO (5ef80c1 los dejó en negro al decidir sobre el
        // color fresco opaco).
        foreach (int pct in new[] { 12, 68 })
        {
            var bg = ColorMath.RiskColor(pct, Theme.Dark, Warn, Crit);
            var ink = ColorMath.Contrast(TrayIconRenderer.StaleFill(bg, taskbarLight: false));
            Assert.Equal(Color.White.ToArgb(), ink.ToArgb());
        }
    }

    [Theory]
    [InlineData(12, UsageStatus.Ok)]
    [InlineData(68, UsageStatus.Warn)]
    [InlineData(95, UsageStatus.Critical)]
    public void Fresh_badges_do_not_depend_on_the_taskbar_assumption(int pct, UsageStatus st)
    {
        // El compuesto solo aplica al estado stale: el badge FRESCO (opaco) debe ser idéntico píxel a
        // píxel con cualquier suposición de barra — el fix no puede cambiarlo.
        using var icoDark = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: false,
            pending: false, taskbarLight: false);
        using var icoLight = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: false,
            pending: false, taskbarLight: true);
        using var b0 = icoDark.ToBitmap();
        using var b1 = icoLight.ToBitmap();
        for (int y = 0; y < b0.Height; y++)
            for (int x = 0; x < b0.Width; x++)
                Assert.Equal(b0.GetPixel(x, y).ToArgb(), b1.GetPixel(x, y).ToArgb());
    }
}
