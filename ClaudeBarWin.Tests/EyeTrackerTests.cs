using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Ojos que miran el cursor (lenguaje de movimiento §5): solo con el cursor dentro del panel
/// (WM_MOUSEMOVE, sin hooks ni sondeo). Desplazamiento horizontal clamp(dx/60, ±2) px y vertical
/// clamp(dy/80, ±1) px, en enteros y suavizado con Ajuste; al salir el cursor vuelven al centro a los
/// 1,5 s. Con el cursor quieto, cero fotogramas. Puro.
/// </summary>
public class EyeTrackerTests
{
    private static void Run(EyeTracker e, double ms, ref double now)
    {
        for (int i = 0; i < ms; i++) { now += 1; e.Advance(0.001, now); }
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(60, 0, 1, 0)]
    [InlineData(300, 300, 2, 1)]
    [InlineData(-300, -300, -2, -1)]
    [InlineData(-90, 40, -2, 1)]     // −1,5 → −2 (redondeo lejos de 0); 0,5 → 1
    public void Target_is_clamped_and_rounded(double dx, double dy, int ex, int ey)
        => Assert.Equal((ex, ey), EyeTracker.TargetFor(dx, dy, scale: 1));

    [Fact]
    public void Target_scales_with_dpi()
        => Assert.Equal((3, 2), EyeTracker.TargetFor(300, 300, scale: 1.5));

    [Fact]
    public void Eyes_glide_with_ajuste_and_then_stop()
    {
        var e = new EyeTracker();
        double now = 0;
        Assert.False(e.IsAnimating);
        e.Track(300, 0, 1, now);
        Assert.True(e.IsAnimating);
        Run(e, 100, ref now);
        Assert.InRange(e.Offset.Dx, 0, 2);
        Run(e, 900, ref now);
        Assert.Equal((2, 0), e.Offset);
        Assert.False(e.IsAnimating);                 // cursor quieto ⇒ sin fotogramas
        e.Track(300, 0, 1, now);                     // mismo objetivo: nada que animar
        Assert.False(e.IsAnimating);
    }

    [Fact]
    public void Leaving_returns_to_center_after_1_5s()
    {
        var e = new EyeTracker();
        double now = 0;
        e.Track(-300, 300, 1, now);
        Run(e, 1000, ref now);
        e.Leave(now);
        Assert.Equal(now + 1500, e.NextDeadlineMs);
        Run(e, 1400, ref now);
        Assert.Equal((-2, 1), e.Offset);             // aún esperando
        Run(e, 1200, ref now);
        Assert.Equal((0, 0), e.Offset);
        Assert.Null(e.NextDeadlineMs);
        Assert.False(e.IsAnimating);
    }

    [Fact]
    public void Coming_back_cancels_the_return()
    {
        var e = new EyeTracker();
        double now = 0;
        e.Track(300, 0, 1, now);
        Run(e, 1000, ref now);
        e.Leave(now);
        Run(e, 500, ref now);
        e.Track(300, 0, 1, now);
        Assert.Null(e.NextDeadlineMs);
        Run(e, 2000, ref now);
        Assert.Equal((2, 0), e.Offset);
    }

    [Fact]
    public void Reset_centers_instantly()
    {
        var e = new EyeTracker();
        double now = 0;
        e.Track(300, 300, 1, now);
        Run(e, 50, ref now);
        e.Reset();
        Assert.Equal((0, 0), e.Offset);
        Assert.False(e.IsAnimating);
        Assert.Null(e.NextDeadlineMs);
        Assert.Equal(1500, Motion.EyesReturnMs);
    }
}
