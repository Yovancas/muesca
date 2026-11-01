using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Colores de las fases del pomodoro (<see cref="FocusPalette"/>): con el acento violeta de Muesca, el foco tiene que seguir
/// distinguiéndose de los dos descansos (sobre todo del azul cielo del largo) y verse sobre el negro del notch.
/// </summary>
public class FocusPaletteTests
{
    private static readonly Theme Notch = Theme.Dark;   // el notch pinta siempre con el tema oscuro

    [Fact]
    public void Focus_uses_the_accent_and_breaks_keep_their_own_colors()
    {
        Assert.Equal(Notch.Accent, FocusPalette.Fill(FocusPhase.Focus, Notch));
        Assert.Equal(Color.FromArgb(52, 211, 153), FocusPalette.Fill(FocusPhase.ShortBreak, Notch));
        Assert.Equal(Color.FromArgb(56, 189, 248), FocusPalette.Fill(FocusPhase.LongBreak, Notch));
        Assert.Equal(Notch.AccentText, FocusPalette.Text(FocusPhase.Focus, Notch));
        Assert.Equal(FocusPalette.LongBreak, FocusPalette.Text(FocusPhase.LongBreak, Notch));
    }

    [Theory]
    [InlineData(FocusPhase.ShortBreak)]
    [InlineData(FocusPhase.LongBreak)]
    public void Focus_is_clearly_distinct_from_each_break(FocusPhase brk)
    {
        // Como relleno (anillo, puntos, botón) y como texto («¡Foco terminado!»): al menos 40° de tono de diferencia.
        float fill = FocusPalette.HueDistance(FocusPalette.Fill(FocusPhase.Focus, Notch), FocusPalette.Fill(brk, Notch));
        float text = FocusPalette.HueDistance(FocusPalette.Text(FocusPhase.Focus, Notch), FocusPalette.Text(brk, Notch));
        Assert.True(fill >= 40f, $"foco y {brk} a {fill:0}° como relleno");
        Assert.True(text >= 40f, $"foco y {brk} a {text:0}° como texto");
    }

    [Fact]
    public void No_phase_is_the_red_of_the_alerts()
    {
        foreach (var phase in Enum.GetValues<FocusPhase>())
            Assert.True(FocusPalette.HueDistance(FocusPalette.Fill(phase, Notch), Notch.Critical) >= 60f, $"{phase} parece un aviso");
    }

    [Theory]
    [InlineData(FocusPhase.Focus)]
    [InlineData(FocusPhase.ShortBreak)]
    [InlineData(FocusPhase.LongBreak)]
    public void Every_phase_reads_on_the_black_notch(FocusPhase phase)
    {
        // El arco y los puntos: contraste no textual (≥3:1); la línea de la fase: texto pequeño (≥4.5:1).
        Assert.True(ColorMath.ContrastRatio(FocusPalette.Fill(phase, Notch), Color.Black) >= 3.0);
        Assert.True(ColorMath.ContrastRatio(FocusPalette.Text(phase, Notch), Color.Black) >= 4.5);
    }

    [Theory]
    [InlineData(FocusPhase.Focus)]
    [InlineData(FocusPhase.ShortBreak)]
    [InlineData(FocusPhase.LongBreak)]
    public void The_primary_button_text_meets_aa_on_every_phase_color(FocusPhase phase)
    {
        // «Empezar» / «Reanudar» van en el color de la fase con el texto que más contrasta (blanco sobre el violeta).
        var fill = FocusPalette.Fill(phase, Notch);
        Assert.True(ColorMath.ContrastRatio(ColorMath.Contrast(fill), fill) >= 4.5);
    }

    [Theory]
    [InlineData(10f, 350f, 20f)]
    [InlineData(256f, 198f, 58f)]
    [InlineData(0f, 180f, 180f)]
    public void Hue_distance_takes_the_short_way_round(float a, float b, float expected)
    {
        static Color FromHue(float h)
        {
            // Saturación y valor completos: GetHue devuelve el mismo tono.
            float x = 1f - Math.Abs(h / 60f % 2f - 1f);
            var (r, g, bl) = (int)(h / 60f) switch
            {
                0 => (1f, x, 0f), 1 => (x, 1f, 0f), 2 => (0f, 1f, x), 3 => (0f, x, 1f), 4 => (x, 0f, 1f), _ => (1f, 0f, x),
            };
            return Color.FromArgb((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(bl * 255));
        }
        Assert.Equal(expected, FocusPalette.HueDistance(FromHue(a), FromHue(b)), 0);
    }
}
