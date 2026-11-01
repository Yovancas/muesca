using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="ResetDetector"/> es PURO: detecta que una ventana de cuota <b>se ha reseteado</b> —el
/// <c>ResetsAt</c> salta hacia adelante (más allá de un umbral) o la utilización cae en picado— para
/// disparar la celebración in-panel una <b>sola vez</b>. El predicado <see cref="ResetDetector.Detect"/>
/// es estático y sin estado; la instancia recuerda la última lectura por clave (5h/7d) y NO re-dispara
/// con la misma lectura. Sin reloj/red por dentro → determinista.
/// </summary>
public class ResetDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);

    // --- Predicado puro Detect --------------------------------------------

    [Fact]
    public void Detect_is_false_on_normal_readings()
    {
        // El reset apenas se mueve y la utilización sube/igual: lectura normal, no es un reset.
        var prev = new UsageWindow(40, T0.AddHours(3));
        var next = new UsageWindow(45, T0.AddHours(3));
        Assert.False(ResetDetector.Detect(prev, next));
    }

    [Fact]
    public void Detect_is_true_when_resets_at_jumps_forward()
    {
        // La ventana se renovó: el ResetsAt salta muy hacia adelante (nueva ventana).
        var prev = new UsageWindow(90, T0.AddMinutes(2));
        var next = new UsageWindow(5, T0.AddHours(5));
        Assert.True(ResetDetector.Detect(prev, next));
    }

    [Fact]
    public void Detect_is_true_when_utilization_falls_sharply()
    {
        // Aunque el ResetsAt no se conozca, una caída en picado de utilización delata el reset.
        var prev = new UsageWindow(88, null);
        var next = new UsageWindow(6, null);
        Assert.True(ResetDetector.Detect(prev, next));
    }

    [Fact]
    public void Detect_ignores_a_small_utilization_dip()
    {
        // Ruido normal (la utilización baja un poco): NO es un reset.
        var prev = new UsageWindow(50, T0.AddHours(3));
        var next = new UsageWindow(46, T0.AddHours(3));
        Assert.False(ResetDetector.Detect(prev, next));
    }

    [Fact]
    public void Detect_is_false_when_resets_at_moves_backward_or_barely()
    {
        var prev = new UsageWindow(40, T0.AddHours(3));
        var back = new UsageWindow(42, T0.AddHours(2));         // hacia atrás: no es reset
        var barely = new UsageWindow(42, T0.AddHours(3).AddMinutes(1)); // salto mínimo
        Assert.False(ResetDetector.Detect(prev, back));
        Assert.False(ResetDetector.Detect(prev, barely));
    }

    [Fact]
    public void Detect_handles_nulls_without_throwing()
    {
        Assert.False(ResetDetector.Detect(null, null));
        Assert.False(ResetDetector.Detect(null, new UsageWindow(10, T0)));
        // util alta→null util: sin señal fiable de caída, no dispara.
        Assert.False(ResetDetector.Detect(new UsageWindow(90, null), null));
    }

    // --- Instancia con estado (dispara una sola vez por clave) -------------

    [Fact]
    public void Observe_first_reading_does_not_fire()
    {
        // Sin lectura previa no hay nada con qué comparar: no celebra al arrancar.
        var d = new ResetDetector();
        Assert.False(d.Observe("5h", new UsageWindow(30, T0.AddHours(3))));
    }

    [Fact]
    public void Observe_fires_once_on_a_reset_then_stays_quiet()
    {
        var d = new ResetDetector();
        d.Observe("5h", new UsageWindow(90, T0.AddMinutes(1)));         // baseline
        bool first = d.Observe("5h", new UsageWindow(4, T0.AddHours(5))); // reset → dispara
        bool again = d.Observe("5h", new UsageWindow(4, T0.AddHours(5))); // misma lectura → silencio
        Assert.True(first);
        Assert.False(again);
    }

    [Fact]
    public void Observe_tracks_keys_independently()
    {
        var d = new ResetDetector();
        d.Observe("5h", new UsageWindow(90, T0.AddMinutes(1)));
        d.Observe("7d", new UsageWindow(70, T0.AddDays(1)));
        // Solo la ventana 5h se resetea: la 7d sigue normal.
        Assert.True(d.Observe("5h", new UsageWindow(3, T0.AddHours(5))));
        Assert.False(d.Observe("7d", new UsageWindow(72, T0.AddDays(1))));
    }

    [Fact]
    public void Observe_can_fire_again_on_a_subsequent_reset()
    {
        var d = new ResetDetector();
        d.Observe("5h", new UsageWindow(90, T0.AddMinutes(1)));
        Assert.True(d.Observe("5h", new UsageWindow(5, T0.AddHours(5))));   // 1er reset
        // Tras consumir cuota de nuevo…
        d.Observe("5h", new UsageWindow(80, T0.AddHours(5)));
        Assert.True(d.Observe("5h", new UsageWindow(6, T0.AddHours(10))));  // 2º reset (otra lectura)
    }

    [Fact]
    public void Observe_null_window_is_safe()
    {
        var d = new ResetDetector();
        Assert.False(d.Observe("5h", null));
        d.Observe("5h", new UsageWindow(50, T0));
        Assert.False(d.Observe("5h", null));
    }
}
