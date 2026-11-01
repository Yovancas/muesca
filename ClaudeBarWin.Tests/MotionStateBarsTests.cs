using System.Drawing;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Barras y cifras del panel (lenguaje de movimiento §4 #5 y micro-interacción 2): un muelle
/// <see cref="Motion.AjustePct"/> por barra que se reapunta; la cifra sale del mismo muelle, redondeada
/// a entero mientras se mueve y con clamp monótono (nunca cruza el objetivo); al abrir, las barras se
/// llenan desde 0 con 25 ms entre ellas; el reset se vacía con <see cref="Motion.BlandoPct"/> y deja un
/// rastro al 25 % de alfa que se funde con Rápido; el color se mezcla con Rápido.
/// </summary>
public class MotionStateBarsTests
{
    private static void Ms(MotionState m, int ms) { for (int i = 0; i < ms; i++) m.Advance(1); }

    [Fact]
    public void Bars_use_ajuste_pct_by_default()
    {
        var m = new MotionState();
        m.Display("bar:5h", 0, false);
        m.SetTarget("bar:5h", 100, false);
        int t = 0;
        while (m.Display("bar:5h", 100, false) < 95 && t < 2000) { m.Advance(1); t++; }
        Assert.InRange(t, 271, 287);              // Ajuste: 95 % a 279 ms
        Assert.Equal(Motion.AjustePct, m.PresetOf("bar:5h"));
    }

    [Fact]
    public void Number_is_rounded_while_moving_and_exact_when_settled()
    {
        var m = new MotionState();
        m.Display("bar:5h", 10, false);
        m.SetTarget("bar:5h", 42.5, false);
        Ms(m, 60);
        double n = m.DisplayNumber("bar:5h", 42.5, false);
        Assert.Equal(Math.Round(n), n);
        Assert.InRange(n, 11, 42);
        Ms(m, 2000);
        Assert.Equal(42.5, m.DisplayNumber("bar:5h", 42.5, false));
    }

    [Fact]
    public void Reset_drains_with_blando_without_crossing_the_target()
    {
        var m = new MotionState();
        m.Display("bar:5h", 87, false);
        m.Reset("bar:5h", 3, false);
        Assert.Equal(Motion.BlandoPct, m.PresetOf("bar:5h"));
        double minBar = 100, minNum = 100;
        int t95 = -1;
        for (int t = 1; t <= 2000; t++)
        {
            m.Advance(1);
            double bar = m.Display("bar:5h", 3, false);
            minBar = Math.Min(minBar, bar);
            minNum = Math.Min(minNum, m.DisplayNumber("bar:5h", 3, false));
            if (t95 < 0 && bar <= 87 - 0.95 * 84) t95 = t;
        }
        Assert.Equal(3, minBar, 9);               // la barra no baja de 3 %
        Assert.Equal(3, minNum, 9);               // ni la cifra pasa por 2, 1 o 0
        Assert.InRange(t95, 365, 385);            // Blando: 95 % a 375 ms
    }

    [Fact]
    public void Reset_leaves_a_25_percent_trail_that_fades_with_rapido()
    {
        var m = new MotionState();
        m.Display("bar:7d", 87, false);
        m.Reset("bar:7d", 3, false);
        var trail = m.TrailOf("bar:7d");
        Assert.NotNull(trail);
        Assert.Equal(87, trail!.Value.Pct, 9);
        Assert.Equal(0.25, trail.Value.Alpha, 3);
        Ms(m, 159);
        Assert.InRange(m.TrailOf("bar:7d")!.Value.Alpha, 0.25 * 0.04, 0.25 * 0.06); // Rápido al 95 %
        Ms(m, 400);
        Assert.Null(m.TrailOf("bar:7d"));
        Ms(m, 1000);                              // y el vaciado Blando termina (~690 ms)
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Reset_with_reduce_motion_snaps_without_trail()
    {
        var m = new MotionState();
        m.Display("bar:7d", 87, false);
        m.Reset("bar:7d", 3, true);
        Assert.Null(m.TrailOf("bar:7d"));
        Assert.False(m.IsAnimating);
        Assert.Equal(3, m.Display("bar:7d", 3, false));
    }

    [Fact]
    public void Opening_fill_starts_from_zero_after_its_delay()
    {
        var m = new MotionState();
        m.Display("bar:5h", 60, false);
        m.Display("bar:7d", 40, false);
        m.Fill("bar:5h", 60, delayMs: 0, reduceMotion: false);
        m.Fill("bar:7d", 40, delayMs: Motion.BarFillStaggerMs, reduceMotion: false);
        Assert.Equal(0, m.Display("bar:5h", 60, false));
        Assert.Equal(0, m.Display("bar:7d", 40, false));
        Assert.True(m.IsAnimating);
        Ms(m, 20);
        Assert.True(m.Display("bar:5h", 60, false) > 0);
        Assert.Equal(0, m.Display("bar:7d", 40, false));   // aún esperando sus 25 ms
        Ms(m, 10);
        Assert.True(m.Display("bar:7d", 40, false) > 0);
        Ms(m, 2000);
        Assert.Equal(60, m.Display("bar:5h", 60, false), 9);
        Assert.Equal(40, m.Display("bar:7d", 40, false), 9);
        Assert.False(m.IsAnimating);
        Assert.Equal(25, Motion.BarFillStaggerMs);
    }

    [Fact]
    public void Fill_with_reduce_motion_is_instant()
    {
        var m = new MotionState();
        m.Fill("bar:5h", 60, 25, reduceMotion: true);
        Assert.Equal(60, m.Display("bar:5h", 60, false));
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Bar_color_crossfades_with_rapido_and_seeds_without_animating()
    {
        var m = new MotionState();
        Assert.Null(m.DisplayColor("bar:5h"));
        m.SetColorTarget("bar:5h", Color.FromArgb(255, 0, 200, 0), snap: false);   // primera vez: siembra
        Assert.False(m.IsAnimating);
        m.SetColorTarget("bar:5h", Color.FromArgb(255, 200, 0, 0), snap: false);
        Assert.True(m.IsAnimating);
        Ms(m, 80);
        var mid = m.DisplayColor("bar:5h")!.Value;
        Assert.InRange(mid.R, 1, 199);
        Ms(m, 600);
        Assert.Equal(Color.FromArgb(255, 200, 0, 0).ToArgb(), m.DisplayColor("bar:5h")!.Value.ToArgb());
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Retarget_sets_the_preset_of_the_transition()
    {
        var m = new MotionState();
        m.Display("bar:5h", 87, false);
        m.Reset("bar:5h", 3, false);
        Ms(m, 50);
        m.SetTarget("bar:5h", 10, false);   // actualización normal a mitad del vaciado
        Assert.Equal(Motion.AjustePct, m.PresetOf("bar:5h"));
    }
}
