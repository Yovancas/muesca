using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary><see cref="PageArrows"/>: flechas ‹ › de página. PURA: el tiempo llega por <c>Advance</c>; quieta ⇒ sin animar.</summary>
public class PageArrowsTests
{
    private const double Frame = 1.0 / 60;

    private static void Run(PageArrows a, double seconds)
    {
        for (double t = 0; t < seconds; t += Frame) a.Advance(Frame);
    }

    [Theory]
    [InlineData(0, 5, PageArrow.Previous, -1)]   // primera: la anterior no lleva a ningún sitio (sin dar la vuelta)
    [InlineData(0, 5, PageArrow.Next, 1)]
    [InlineData(4, 5, PageArrow.Next, -1)]       // última: tampoco la siguiente
    [InlineData(4, 5, PageArrow.Previous, 3)]
    [InlineData(2, 5, PageArrow.Previous, 1)]
    [InlineData(0, 1, PageArrow.Next, -1)]       // una sola página
    [InlineData(1, 3, PageArrow.None, -1)]
    [InlineData(7, 3, PageArrow.Previous, -1)]
    public void Target_does_not_wrap(int index, int count, PageArrow side, int expected) =>
        Assert.Equal(expected, PageArrows.Target(index, count, side));

    [Fact]
    public void Appear_with_a_fade_and_a_short_slide_then_settle()
    {
        var a = new PageArrows();
        Assert.False(a.IsAnimating);
        Assert.Equal(0f, a.View.Next.Alpha);
        a.Update(visible: true, index: 1, count: 3, hover: PageArrow.None);
        Assert.True(a.IsAnimating);
        a.Advance(0.03);
        var mid = a.View.Next;
        Assert.InRange(mid.Alpha, 0.01f, 0.99f);
        Assert.True(mid.Offset < 0f && mid.Offset > -PageArrows.EnterSlide);   // entra desde los puntos hacia fuera
        Run(a, 1);
        Assert.False(a.IsAnimating);                                           // 0 % CPU quieta
        Assert.Equal(new PageArrowLook(1f, 0f, 1f, 0f), a.View.Next);
        Assert.Equal(new PageArrowLook(1f, 0f, 1f, 0f), a.View.Previous);
    }

    [Fact]
    public void At_the_ends_the_dead_arrow_fades_out()
    {
        var a = new PageArrows();
        a.Update(true, 1, 3, PageArrow.None);
        Run(a, 1);
        a.Update(true, 2, 3, PageArrow.None);   // a la última
        a.Advance(0.03);
        Assert.InRange(a.View.Next.Alpha, 0.01f, 0.99f);   // se desvanece, no salta
        Run(a, 1);
        Assert.Equal(0f, a.View.Next.Alpha);
        Assert.Equal(1f, a.View.Previous.Alpha);
    }

    [Fact]
    public void No_arrows_with_a_single_page()
    {
        var a = new PageArrows();
        a.Update(true, 0, 1, PageArrow.Next);
        Run(a, 1);
        Assert.Equal(0f, a.View.Previous.Alpha);
        Assert.Equal(0f, a.View.Next.Alpha);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void Hover_grows_and_nudges_toward_its_direction_only_if_it_goes_somewhere()
    {
        var a = new PageArrows();
        a.Update(true, 0, 3, PageArrow.Next);
        Run(a, 1);
        var next = a.View.Next;
        Assert.Equal(1f, next.Hover);
        Assert.Equal(1f + PageArrows.HoverGrow, next.Scale, 3);
        Assert.Equal(PageArrows.HoverNudge, next.Offset, 2);
        a.Update(true, 0, 3, PageArrow.Previous);   // la de la primera página no se realza
        Run(a, 1);
        Assert.Equal(0f, a.View.Previous.Hover);
        Assert.Equal(1f, a.View.Previous.Scale);
    }

    [Fact]
    public void Press_pushes_toward_its_direction_and_springs_back()
    {
        var a = new PageArrows();
        a.Update(true, 1, 3, PageArrow.None);
        Run(a, 1);
        a.Press(PageArrow.Previous);
        Assert.True(a.IsAnimating);
        float peak = 0f;
        for (int i = 0; i < 20; i++)
        {
            a.Advance(Frame);
            peak = Math.Max(peak, a.View.Previous.Offset);
        }
        Assert.InRange(peak, 2f, 5f);                 // un empujón pequeño, de unos px
        Assert.Equal(0f, a.View.Next.Offset);         // solo la pulsada
        Run(a, 2);
        Assert.False(a.IsAnimating);
        Assert.Equal(0f, a.View.Previous.Offset);
    }

    [Fact]
    public void Pressing_a_dead_or_hidden_arrow_does_nothing()
    {
        var a = new PageArrows();
        a.Press(PageArrow.Next);                       // ocultas
        Assert.False(a.IsAnimating);
        a.Update(true, 0, 3, PageArrow.None);
        Run(a, 1);
        a.Press(PageArrow.Previous);                   // primera página
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void Reduce_motion_jumps_without_slide_growth_or_push()
    {
        var a = new PageArrows { ReduceMotion = true };
        a.Update(true, 0, 3, PageArrow.Next);
        Assert.False(a.IsAnimating);
        Assert.Equal(new PageArrowLook(1f, 1f, 1f, 0f), a.View.Next);   // solo alfa y color
        Assert.Equal(0f, a.View.Previous.Alpha);
        a.Press(PageArrow.Next);
        Assert.False(a.IsAnimating);
        a.Update(false, 0, 3, PageArrow.None);
        Assert.Equal(0f, a.View.Next.Alpha);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void Hidden_arrows_reappear_with_the_right_availability_without_a_late_fade()
    {
        var a = new PageArrows();
        a.Update(true, 0, 3, PageArrow.None);
        Run(a, 1);
        a.Update(false, 0, 3, PageArrow.None);
        Run(a, 1);
        a.Update(false, 2, 3, PageArrow.None);   // oculto, se cambia de página (otra vía)
        a.Update(true, 2, 3, PageArrow.None);
        a.Advance(Frame);
        Assert.Equal(0f, a.View.Next.Alpha);     // la siguiente ya no estaba: no asoma
    }
}
