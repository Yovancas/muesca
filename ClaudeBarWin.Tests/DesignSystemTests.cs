using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class DesignSystemTests
{
    [Fact]
    public void Spacing_values_are_multiples_of_four()
    {
        foreach (var v in new[] { Spacing.Xs, Spacing.Sm, Spacing.Md, Spacing.Lg, Spacing.Xl, Spacing.Xxl })
            Assert.Equal(0, v % 4);
    }

    [Fact]
    public void Lerp_returns_endpoints_at_0_and_1()
    {
        var a = Color.FromArgb(10, 20, 30);
        var b = Color.FromArgb(200, 200, 200);
        Assert.Equal(a, ColorMath.Lerp(a, b, 0));
        Assert.Equal(b, ColorMath.Lerp(a, b, 1));
    }

    // --- T9: ratio de contraste WCAG (verificación del tema claro) ---

    [Fact]
    public void ContrastRatio_black_on_white_is_21()
    {
        // El máximo teórico del ratio WCAG 2.x es 21:1 (negro sobre blanco).
        Assert.Equal(21.0, ColorMath.ContrastRatio(Color.Black, Color.White), 1);
    }

    [Fact]
    public void ContrastRatio_is_symmetric()
    {
        var a = Color.FromArgb(108, 108, 114);
        var b = Color.FromArgb(250, 250, 250);
        Assert.Equal(ColorMath.ContrastRatio(a, b), ColorMath.ContrastRatio(b, a), 6);
    }

    [Fact]
    public void ContrastRatio_same_color_is_1()
    {
        var c = Color.FromArgb(120, 130, 140);
        Assert.Equal(1.0, ColorMath.ContrastRatio(c, c), 6);
    }

    // --- T6a: Contrast decide negro/blanco por luminancia relativa WCAG, no por luma 0.299/0.587/0.114 ---

    [Fact]
    public void Contrast_picks_black_on_cli_green()
    {
        // El verde CLI #00D959 tiene luminancia relativa ~0.50: el negro contrasta 11.1:1 y el blanco
        // solo 1.9:1. La luma antigua (137.5 < umbral 140) elegía BLANCO → texto ilegible en los chips
        // activos, el knob del TogglePill y el % del badge del tray con el tema CLI.
        Assert.Equal(Color.Black, ColorMath.Contrast(Theme.Cli.Accent));
    }

    [Fact]
    public void Contrast_picks_black_on_amber_and_green_fills()
    {
        // Ámbar #D97706 (negro 6.6:1 vs blanco 3.2:1) y verde #16A34A (negro 6.4:1 vs blanco 3.3:1):
        // la luma antigua elegía blanco en ambos pese a ser el lado peor.
        Assert.Equal(Color.Black, ColorMath.Contrast(Theme.Dark.Warn));
        Assert.Equal(Color.Black, ColorMath.Contrast(Theme.Dark.Ok));
    }

    [Fact]
    public void Contrast_keeps_white_on_red_and_dark_fills()
    {
        // El rojo #DC2626 (L≈0.167) y los fondos oscuros quedan bajo el punto de cruce (~0.179): blanco.
        Assert.Equal(Color.White, ColorMath.Contrast(Theme.Dark.Critical));
        Assert.Equal(Color.White, ColorMath.Contrast(Theme.Dark.Neutral));
        Assert.Equal(Color.White, ColorMath.Contrast(Theme.Dark.Background));
    }

    public static IEnumerable<object[]> ThemeFills()
    {
        foreach (var t in new[] { Theme.Dark, Theme.Light, Theme.Cli })
            foreach (var (name, c) in new (string, Color)[]
            {
                ("Background", t.Background), ("Foreground", t.Foreground), ("Accent", t.Accent),
                ("Ok", t.Ok), ("Warn", t.Warn), ("Critical", t.Critical), ("Neutral", t.Neutral),
                ("Track", t.Track), ("BgElevated", t.BgElevated),
            })
                yield return new object[] { $"{t.Id}.{name}", c };
    }

    [Theory]
    [MemberData(nameof(ThemeFills))]
    public void Contrast_always_picks_the_better_of_black_and_white(string id, Color bg)
    {
        // Propiedad: para cualquier relleno de los 3 temas, el lado elegido (negro/blanco) nunca
        // contrasta peor que el descartado. Con luma-140 fallaba en CLI.Accent/Ok y Dark.Warn/Ok.
        Color chosen = ColorMath.Contrast(bg);
        Color other = chosen.ToArgb() == Color.Black.ToArgb() ? Color.White : Color.Black;
        double rc = ColorMath.ContrastRatio(bg, chosen), ro = ColorMath.ContrastRatio(bg, other);
        Assert.True(rc >= ro, $"{id}: Contrast eligió el lado peor ({rc:0.00}:1 vs {ro:0.00}:1)");
    }

    // F3 Task 5: el espaciado se proyecta al DPI en el punto de uso; a factor 1.0 es idéntico.
    [Fact]
    public void At_factor_1_spacing_equals_current_constants()
    {
        var sp = Spacing.At(1.0f);
        Assert.Equal((Spacing.Xs, Spacing.Sm, Spacing.Md, Spacing.Lg, Spacing.Xl, Spacing.Xxl),
                     (sp.Xs, sp.Sm, sp.Md, sp.Lg, sp.Xl, sp.Xxl));
        Assert.Equal(Spacing.At(1.0f), Spacing.Scaled); // el hilo de tests está a 96 DPI
    }

    [Fact]
    public void At_factor_1_5_spacing_scales()
    {
        var sp = Spacing.At(1.5f);
        Assert.Equal((6, 12, 18, 24, 36, 48), (sp.Xs, sp.Sm, sp.Md, sp.Lg, sp.Xl, sp.Xxl));
        Assert.Equal(9f, Dpi.ScaleF(6, 1.5f));
    }
}
