using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// F3 Task 1: el <see cref="InvalidationPlanner"/> decide QUÉ repintar en cada tick. Con Claude
/// trabajando solo se mueve la mascota ⇒ basta con su celda; cualquier tween/fade/entrada/hover ⇒
/// panel entero; nada ⇒ no se invalida. En tick lento solo se repinta si cambia la firma
/// (minuto, texto del pie). Todo puro, sin reloj ni GDI+.
/// </summary>
public class MotionInvalidationTests
{
    [Fact]
    public void Only_mascot_gives_MascotOnly()
    {
        Assert.Equal(InvalidationKind.MascotOnly,
            InvalidationPlanner.Plan(fading: false, staggering: false, tweening: false,
                hoverAnimating: false, mascotAnimating: true));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Any_tween_gives_Full(bool fading, bool staggering, bool tweening, bool hover)
    {
        // Con o sin mascota viva: si algo más se mueve, el panel entero manda.
        Assert.Equal(InvalidationKind.Full,
            InvalidationPlanner.Plan(fading, staggering, tweening, hover, mascotAnimating: false));
        Assert.Equal(InvalidationKind.Full,
            InvalidationPlanner.Plan(fading, staggering, tweening, hover, mascotAnimating: true));
    }

    [Fact]
    public void Nothing_gives_None()
    {
        Assert.Equal(InvalidationKind.None,
            InvalidationPlanner.Plan(false, false, false, false, false));
    }

    [Fact]
    public void Slow_tick_same_minute_and_footer_does_not_invalidate()
    {
        var t0 = new DateTime(2026, 10, 28, 12, 34, 5, DateTimeKind.Utc);
        var a = SlowTickSignature.From(t0, "Actualizado 12:30 · hace 4 min");
        var b = SlowTickSignature.From(t0.AddSeconds(40), "Actualizado 12:30 · hace 4 min");
        Assert.False(InvalidationPlanner.SlowTickNeedsRepaint(a, b));

        // Cambia el minuto de pared ⇒ repinta (cuentas atrás "se renueva en 2 h 13 min").
        var c = SlowTickSignature.From(t0.AddSeconds(60), "Actualizado 12:30 · hace 4 min");
        Assert.True(InvalidationPlanner.SlowTickNeedsRepaint(a, c));

        // Cambia el pie (el relativo avanza a destiempo del minuto de pared) ⇒ repinta.
        var d = SlowTickSignature.From(t0.AddSeconds(10), "Actualizado 12:30 · hace 5 min");
        Assert.True(InvalidationPlanner.SlowTickNeedsRepaint(a, d));

        // Sin firma previa (primer tick tras abrir) ⇒ repinta.
        Assert.True(InvalidationPlanner.SlowTickNeedsRepaint(null, a));
    }
}
