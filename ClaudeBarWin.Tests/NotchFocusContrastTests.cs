using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El botón principal del pomodoro (Empezar / Reanudar / la oferta) lleva texto encima del color de la fase. Con el ratón
/// encima y al pulsarlo el relleno cambia, y la tinta se calcula sobre el relleno que se pinta de verdad: nunca baja de AA
/// (4.5:1). Antes el hover aclaraba +22 cada canal también los rellenos oscuros: el blanco sobre el violeta de Muesca bajaba de
/// ~4.8:1 a ~3.9:1 justo mientras se apuntaba al botón.
/// </summary>
public class NotchFocusContrastTests
{
    private const double AA = 4.5;

    public static IEnumerable<object[]> PhasesAndThemes()
    {
        foreach (var (name, theme) in new[] { ("dark", Theme.Dark), ("light", Theme.Light), ("cli", Theme.Cli) })
            foreach (var phase in Enum.GetValues<FocusPhase>())
                yield return new object[] { name, phase, theme };
    }

    [Theory]
    [MemberData(nameof(PhasesAndThemes))]
    public void The_primary_button_text_stays_AA_while_hovered_and_pressed(string theme, FocusPhase phase, Theme t)
    {
        var baseFill = FocusPalette.Fill(phase, t);
        foreach (float hover in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            foreach (bool pressed in new[] { false, true })
            {
                var fill = NotchContentPainter.FocusPressFill(baseFill, hover, pressed);
                Assert.Equal(255, fill.A);
                var ink = NotchContentPainter.FocusPrimaryInk(fill, pause: false, t.Foreground);
                double ratio = ColorMath.ContrastRatio(ink, fill);
                Assert.True(ratio >= AA, $"{theme}/{phase} hover {hover} pulsado {pressed}: {ratio:0.00}:1 sobre {fill}");
            }
    }

    [Fact]
    public void Hovering_the_violet_never_lightens_it()
    {
        // Con texto blanco, el realce oscurece (ColorMath.HoverFill), no aclara.
        var violet = FocusPalette.Fill(FocusPhase.Focus, Theme.Dark);
        var hovered = NotchContentPainter.FocusPressFill(violet, 1f, pressed: false);
        Assert.Equal(Color.White, ColorMath.Contrast(violet));
        Assert.True(ColorMath.RelativeLuminance(hovered) < ColorMath.RelativeLuminance(violet));
        Assert.Equal(ColorMath.HoverFill(violet, 1.0), hovered);
    }

    [Fact]
    public void The_ink_follows_the_painted_fill_not_the_phase_color()
    {
        // Un relleno ya realzado/pulsado es el que manda: la tinta es la que más contrasta con él.
        var dark = Color.FromArgb(255, 40, 40, 60);
        var light = Color.FromArgb(255, 220, 230, 240);
        Assert.Equal(ColorMath.Contrast(dark), NotchContentPainter.FocusPrimaryInk(dark, pause: false, Color.Red));
        Assert.Equal(ColorMath.Contrast(light), NotchContentPainter.FocusPrimaryInk(light, pause: false, Color.Red));
        // En pausa el botón es translúcido y lleva el color de texto del notch.
        Assert.Equal(Color.Red, NotchContentPainter.FocusPrimaryInk(light, pause: true, Color.Red));
    }

    [Fact]
    public void Translucent_buttons_gain_alpha_on_hover_and_keep_their_color()
    {
        var glass = Color.FromArgb(24, 255, 255, 255);
        var hovered = NotchContentPainter.FocusPressFill(glass, 1f, pressed: false);
        Assert.True(hovered.A > glass.A);
        Assert.Equal((glass.R, glass.G, glass.B), (hovered.R, hovered.G, hovered.B));
        Assert.Equal(glass, NotchContentPainter.FocusPressFill(glass, 0f, pressed: false));
    }
}
