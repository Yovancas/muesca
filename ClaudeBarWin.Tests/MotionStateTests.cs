using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="MotionState"/> es el contenedor de valores animados por clave que el render muestrea.
/// Desde F3 Task 4 cada clave es un muelle críticamente amortiguado (<see cref="SpringValue"/>): ya no
/// hay duración fija, así que los tests que antes avanzaban exactamente <c>Dur</c> esperando el
/// objetivo avanzan ahora <see cref="Settle"/> (tiempo de sobra para asentarse). Su núcleo es PURO y
/// elapsed-driven: <c>Set</c> rearma el objetivo, <c>Advance</c> integra el tiempo y <c>Display</c>
/// muestrea. Con <c>reduceMotion</c> devuelve el target al instante (puerta de la Tarea 7).
/// </summary>
public class MotionStateTests
{
    private const double Dur = 220.0;
    private const double Settle = 2000.0; // ms: un muelle crítico se asienta muy por debajo

    [Fact]
    public void First_display_seeds_at_target_without_animating()
    {
        var m = new MotionState();
        // La primera lectura de una clave nueva siembra el valor: no hay tween "desde 0".
        Assert.Equal(80.0, m.Display("bar:5h", 80.0, reduceMotion: false), 9);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Set_then_half_advance_displays_between_prev_and_target()
    {
        var m = new MotionState();
        m.Display("bar:5h", 20.0, reduceMotion: false); // siembra en 20
        m.Set("bar:5h", 80.0, Dur, Easing.OutCubic);    // anima 20→80
        Assert.True(m.IsAnimating);

        m.Advance(Dur / 2);
        double mid = m.Display("bar:5h", 80.0, reduceMotion: false);
        Assert.True(mid > 20.0 && mid < 80.0, $"esperado en (20,80), got {mid}");
    }

    [Fact]
    public void Full_advance_reaches_target_and_stops_animating()
    {
        var m = new MotionState();
        m.Display("bar:7d", 10.0, reduceMotion: false);
        m.Set("bar:7d", 60.0, Dur, Easing.OutCubic);
        m.Advance(Settle);
        Assert.Equal(60.0, m.Display("bar:7d", 60.0, reduceMotion: false), 6);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Reduce_motion_displays_target_immediately()
    {
        var m = new MotionState();
        m.Display("num:crit", 5.0, reduceMotion: true);
        m.Set("num:crit", 95.0, Dur, Easing.OutCubic);
        // Con reduce-motion el muestreo es el target, sin tween intermedio.
        Assert.Equal(95.0, m.Display("num:crit", 95.0, reduceMotion: true), 9);
    }

    [Fact]
    public void Reduce_motion_does_not_request_animation()
    {
        var m = new MotionState();
        m.Display("pace", 0.0, reduceMotion: true);
        m.Set("pace", 100.0, Dur, Easing.OutCubic, reduceMotion: true);
        // El gate de reduce-motion colapsa el valor: nada que animar.
        Assert.False(m.IsAnimating);
        Assert.Equal(100.0, m.Display("pace", 100.0, reduceMotion: true), 9);
    }

    [Fact]
    public void Keys_are_independent()
    {
        var m = new MotionState();
        m.Display("bar:5h", 30.0, reduceMotion: false);
        m.Display("bar:7d", 70.0, reduceMotion: false);
        m.Set("bar:5h", 90.0, Dur, Easing.OutCubic);
        m.Advance(Dur / 2);

        double five = m.Display("bar:5h", 90.0, reduceMotion: false);
        double seven = m.Display("bar:7d", 70.0, reduceMotion: false);
        Assert.True(five > 30.0 && five < 90.0, $"5h en vuelo, got {five}");
        Assert.Equal(70.0, seven, 9); // 7d no se tocó: sigue asentado
    }

    [Fact]
    public void IsAnimating_reflects_any_animating_key()
    {
        var m = new MotionState();
        m.Display("bar:5h", 10.0, reduceMotion: false);
        m.Display("bar:7d", 10.0, reduceMotion: false);
        Assert.False(m.IsAnimating);

        m.Set("bar:7d", 50.0, Dur, Easing.OutCubic);
        Assert.True(m.IsAnimating);
        m.Advance(Settle);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Display_uses_default_tween_duration_when_set_via_helper()
    {
        var m = new MotionState();
        m.Display("bar:5h", 0.0, reduceMotion: false);
        // SetTarget (muelle crítico): a media duración del antiguo tween va a medio camino.
        m.SetTarget("bar:5h", 100.0, reduceMotion: false);
        m.Advance(Motion.TweenMs / 2);
        double mid = m.Display("bar:5h", 100.0, reduceMotion: false);
        Assert.True(mid > 0.0 && mid < 100.0, $"got {mid}");
        m.Advance(Settle);
        Assert.Equal(100.0, m.Display("bar:5h", 100.0, reduceMotion: false), 6);
    }

    [Fact]
    public void SetTarget_with_reduce_motion_snaps()
    {
        var m = new MotionState();
        m.Display("bar:5h", 0.0, reduceMotion: true);
        m.SetTarget("bar:5h", 100.0, reduceMotion: true);
        Assert.False(m.IsAnimating);
        Assert.Equal(100.0, m.Display("bar:5h", 100.0, reduceMotion: true), 9);
    }

    // ---- F3 Task 4: muelles ----

    private static void Frames(MotionState m, int n) { for (int i = 0; i < n; i++) m.Advance(1000.0 / 60); }

    [Fact]
    public void Retarget_midway_has_no_velocity_reset()
    {
        var m = new MotionState();
        m.Display("bar:5h", 0.0, reduceMotion: false);
        m.SetTarget("bar:5h", 100.0, reduceMotion: false);
        Frames(m, 6);
        double v = m.VelocityOf("bar:5h");
        Assert.True(v > 0, $"en vuelo hacia arriba, v={v}");

        double x = m.Display("bar:5h", 100.0, reduceMotion: false);
        m.SetTarget("bar:5h", 40.0, reduceMotion: false); // re-apunta a mitad de camino
        Assert.Equal(v, m.VelocityOf("bar:5h"), 9);       // la velocidad se conserva: sin frenazo
        Assert.Equal(x, m.Display("bar:5h", 40.0, reduceMotion: false), 9); // ni salto de posición
        m.Advance(Settle);
        Assert.Equal(40.0, m.Display("bar:5h", 40.0, reduceMotion: false), 6);
    }

    [Fact]
    public void Same_target_does_not_restart()
    {
        var a = new MotionState();
        var b = new MotionState();
        foreach (var m in new[] { a, b })
        {
            m.Display("num:crit", 10.0, reduceMotion: false);
            m.SetTarget("num:crit", 90.0, reduceMotion: false);
            Frames(m, 5);
        }
        b.SetTarget("num:crit", 90.0, reduceMotion: false); // llega el mismo objetivo otra vez
        Frames(a, 5);
        Frames(b, 5);
        Assert.Equal(a.Display("num:crit", 90.0, false), b.Display("num:crit", 90.0, false), 12);
        Assert.Equal(a.VelocityOf("num:crit"), b.VelocityOf("num:crit"), 12);
    }

    [Fact]
    public void ReduceMotion_snaps()
    {
        var m = new MotionState();
        m.Display("bar:7d", 0.0, reduceMotion: false);
        m.SetTarget("bar:7d", 70.0, reduceMotion: false);
        Frames(m, 3);
        m.SetTarget("bar:7d", 30.0, reduceMotion: true);
        Assert.False(m.IsAnimating);
        Assert.Equal(0.0, m.VelocityOf("bar:7d"));
        Assert.Equal(30.0, m.Display("bar:7d", 30.0, reduceMotion: false), 12);
    }

    [Fact]
    public void Long_advance_catches_up_instead_of_freezing()
    {
        // Un único Advance largo (render offline, o un tick tras una pausa) no se queda en los 50 ms
        // que recorta SpringValue: se integra en trozos y llega al objetivo.
        var m = new MotionState();
        m.Display("pace", 0.0, reduceMotion: false);
        m.SetTarget("pace", 100.0, reduceMotion: false);
        m.Advance(Settle);
        Assert.False(m.IsAnimating);
        Assert.Equal(100.0, m.Display("pace", 100.0, false), 12);
    }

    [Fact]
    public void Spring_progress_goes_from_zero_to_one_monotonically()
    {
        Assert.Equal(0.0, MotionState.SpringProgressAt(0), 12);
        double prev = 0;
        for (double t = 10; t <= 1500; t += 10)
        {
            double p = MotionState.SpringProgressAt(t);
            Assert.True(p >= prev - 1e-9 && p <= 1.0, $"t={t} p={p}");
            prev = p;
        }
        Assert.Equal(1.0, MotionState.SpringProgressAt(1500), 12);
        Assert.InRange(MotionState.SpringProgressAt(Motion.FadeMs), 0.3, 0.95);
    }
}
