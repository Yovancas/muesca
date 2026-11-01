using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="PageTransition"/> es PURO: cambio de página del expandido con la variante sobria del lenguaje de
/// movimiento (§3.3, «Páginas»): cruce de 120 ms con 8 px de desplazamiento, y la píldora indicadora que se desliza
/// entre los puntos con Ajuste. Quieto ⇒ <see cref="PageTransition.IsAnimating"/> = false (0 % CPU en reposo).
/// </summary>
public class PageTransitionTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(10);
    private static TimeSpan Ms(double ms) => T0 + TimeSpan.FromMilliseconds(ms);

    /// <summary>Avanza a 60 Hz hasta <paramref name="until"/> (como el bucle de la ventana).</summary>
    private static void Run(PageTransition t, TimeSpan from, TimeSpan until)
    {
        for (var now = from; now <= until; now += TimeSpan.FromMilliseconds(16)) t.Step(now);
        t.Step(until);
    }

    [Fact]
    public void Starts_settled_on_the_initial_page()
    {
        var t = new PageTransition(NotchPage.Claude, 0);
        Assert.Equal(NotchPage.Claude, t.Current);
        Assert.Null(t.Previous);
        Assert.Equal(1f, t.Blend);
        Assert.Equal(0f, t.CurrentShift);
        Assert.Equal(0f, t.IndicatorPos);
        Assert.False(t.IsAnimating);
    }

    [Fact]
    public void Go_crossfades_in_120ms_with_an_8px_shift()
    {
        var t = new PageTransition(NotchPage.Claude, 0);
        t.Go(NotchPage.Music, 1, direction: 1, T0);
        Assert.True(t.IsAnimating);
        Assert.Equal(NotchPage.Music, t.Current);
        Assert.Equal(NotchPage.Claude, t.Previous);
        Assert.Equal(0f, t.Blend);
        Assert.Equal(PageTransition.Shift, t.CurrentShift, 3);     // la nueva entra desde la derecha
        Assert.Equal(0f, t.PreviousShift, 3);

        t.Step(Ms(60));
        Assert.InRange(t.Blend, 0.45f, 0.55f);                       // cruce lineal en alfa
        Assert.InRange(t.CurrentShift, 0.5f, PageTransition.Shift - 0.5f);
        Assert.InRange(t.PreviousShift, -PageTransition.Shift + 0.5f, -0.5f);   // la vieja sale hacia la izquierda

        t.Step(Ms(120));
        Assert.Equal(1f, t.Blend);
        Assert.Equal(0f, t.CurrentShift);
        Assert.Null(t.Previous);                                     // ya no se pinta la vieja
    }

    [Fact]
    public void Shifts_are_whole_pixels()
    {
        var t = new PageTransition(NotchPage.Claude, 0);
        t.Go(NotchPage.Music, 1, 1, T0);
        for (int ms = 0; ms <= 120; ms += 7)
        {
            t.Step(Ms(ms));
            Assert.Equal(MathF.Round(t.CurrentShift), t.CurrentShift);
            Assert.Equal(MathF.Round(t.PreviousShift), t.PreviousShift);
        }
    }

    [Fact]
    public void Going_back_enters_from_the_left()
    {
        var t = new PageTransition(NotchPage.Music, 1);
        t.Go(NotchPage.Claude, 0, direction: -1, T0);
        Assert.Equal(-PageTransition.Shift, t.CurrentShift, 3);
        t.Step(Ms(60));
        Assert.True(t.PreviousShift > 0f);
    }

    [Fact]
    public void Indicator_slides_with_Adjust_and_settles()
    {
        var t = new PageTransition(NotchPage.Claude, 0);
        t.Go(NotchPage.Tasks, 2, 1, T0);
        t.Step(Ms(100));
        Assert.InRange(t.IndicatorPos, 0.05f, 1.95f);                // a medio camino, sin pasarse
        Run(t, Ms(100), Ms(500));
        Assert.True(t.IndicatorPos <= 2f + 1e-3f);
        Run(t, Ms(500), Ms(900));
        Assert.Equal(2f, t.IndicatorPos);
        Assert.False(t.IsAnimating);                                 // asentado: el bucle se para
    }

    [Fact]
    public void Indicator_never_overshoots()
    {
        var t = new PageTransition(NotchPage.Claude, 0);
        t.Go(NotchPage.Shelf, 3, -1, T0);   // envolver hacia atrás: la píldora viaja de 0 a 3
        float max = 0f;
        for (int ms = 0; ms <= 900; ms += 16)
        {
            t.Step(Ms(ms));
            max = Math.Max(max, t.IndicatorPos);
        }
        Assert.True(max <= 3f + 1e-3f, $"se pasó: {max}");
    }

    [Fact]
    public void ReduceMotion_jumps_at_once()
    {
        var t = new PageTransition(NotchPage.Claude, 0) { ReduceMotion = true };
        t.Go(NotchPage.Music, 1, 1, T0);
        Assert.False(t.IsAnimating);
        Assert.Equal(1f, t.Blend);
        Assert.Null(t.Previous);
        Assert.Equal(1f, t.IndicatorPos);
        Assert.Equal(0f, t.CurrentShift);
    }

    [Fact]
    public void Go_mid_transition_takes_the_current_page_as_the_outgoing_one()
    {
        var t = new PageTransition(NotchPage.Claude, 0);
        t.Go(NotchPage.Music, 1, 1, T0);
        t.Step(Ms(50));
        t.Go(NotchPage.Tasks, 2, 1, Ms(50));
        Assert.Equal(NotchPage.Tasks, t.Current);
        Assert.Equal(NotchPage.Music, t.Previous);
        Assert.Equal(0f, t.Blend);
    }

    [Fact]
    public void Going_to_the_same_page_does_nothing()
    {
        var t = new PageTransition(NotchPage.Music, 1);
        t.Go(NotchPage.Music, 1, 1, T0);
        Assert.False(t.IsAnimating);
        Assert.Null(t.Previous);
    }

    [Fact]
    public void Jump_resets_without_animation()
    {
        var t = new PageTransition(NotchPage.Claude, 0);
        t.Go(NotchPage.Shelf, 3, 1, T0);
        t.Step(Ms(30));
        t.Jump(NotchPage.Claude, 0);
        Assert.False(t.IsAnimating);
        Assert.Equal(NotchPage.Claude, t.Current);
        Assert.Null(t.Previous);
        Assert.Equal(0f, t.IndicatorPos);
        Assert.Equal(1f, t.Blend);
    }

    [Fact]
    public void A_long_pause_before_the_first_step_does_not_jump_the_indicator()
    {
        // Tras un reposo largo, el primer paso no integra todo ese tiempo: arranca desde el Go.
        var t = new PageTransition(NotchPage.Claude, 0);
        t.Step(T0 - TimeSpan.FromMinutes(5));
        t.Go(NotchPage.Music, 1, 1, T0);
        t.Step(Ms(16));
        Assert.True(t.IndicatorPos < 0.5f);
    }
}
