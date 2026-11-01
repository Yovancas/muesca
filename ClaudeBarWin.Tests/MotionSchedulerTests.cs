using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El <see cref="MotionScheduler"/> es la pieza que protege la CPU 24/7: decide cuándo el
/// tick rápido (~33 ms) puede correr. Regla dura: con el panel oculto, CERO trabajo extra
/// (WantsFastTick==false aunque haya animación). Todo puro, sin reloj ni red.
/// </summary>
public class MotionSchedulerTests
{
    // --- WantsFastTick ----------------------------------------------------

    [Fact]
    public void Not_visible_never_wants_fast_tick_even_if_animating()
    {
        var s = new MotionScheduler();
        Assert.False(s.WantsFastTick(visible: false, animating: true));
        Assert.False(s.WantsFastTick(visible: false, animating: false));
    }

    [Fact]
    public void Visible_and_animating_wants_fast_tick()
    {
        var s = new MotionScheduler();
        Assert.True(s.WantsFastTick(visible: true, animating: true));
    }

    [Fact]
    public void Visible_without_animation_does_not_want_fast_tick()
    {
        var s = new MotionScheduler();
        Assert.False(s.WantsFastTick(visible: true, animating: false));
    }

    [Fact]
    public void Reduce_motion_suppresses_fast_tick_even_when_visible_and_animating()
    {
        var s = new MotionScheduler(reduceMotion: true);
        Assert.False(s.WantsFastTick(visible: true, animating: true));
    }

    // --- DesiredIntervalMs ------------------------------------------------

    [Fact]
    public void Active_interval_is_fast_tick()
    {
        var s = new MotionScheduler();
        Assert.Equal(Motion.FastTickMs, s.DesiredIntervalMs(visible: true, animating: true));
    }

    [Fact]
    public void Visible_idle_interval_is_slow_countdown()
    {
        var s = new MotionScheduler();
        Assert.Equal(Motion.SlowTickMs, s.DesiredIntervalMs(visible: true, animating: false));
    }

    [Fact]
    public void Hidden_reports_stopped()
    {
        var s = new MotionScheduler();
        // Convención: <=0 significa "parar el timer" (no hay tick útil con el panel oculto).
        Assert.True(s.DesiredIntervalMs(visible: false, animating: true) <= 0);
        Assert.True(s.DesiredIntervalMs(visible: false, animating: false) <= 0);
        Assert.False(s.IsRunning(visible: false, animating: true));
    }

    [Fact]
    public void Reduce_motion_stays_on_slow_countdown_while_visible()
    {
        var s = new MotionScheduler(reduceMotion: true);
        // Con reduce-motion no hay fast-tick, pero el countdown del panel visible sigue a 1 s.
        Assert.Equal(Motion.SlowTickMs, s.DesiredIntervalMs(visible: true, animating: true));
        Assert.Equal(Motion.SlowTickMs, s.DesiredIntervalMs(visible: true, animating: false));
    }

    // --- IsRunning --------------------------------------------------------

    [Fact]
    public void IsRunning_true_when_visible_false_when_hidden()
    {
        var s = new MotionScheduler();
        Assert.True(s.IsRunning(visible: true, animating: false));  // countdown
        Assert.True(s.IsRunning(visible: true, animating: true));   // fast tick
        Assert.False(s.IsRunning(visible: false, animating: false));
        Assert.False(s.IsRunning(visible: false, animating: true));
    }
}
