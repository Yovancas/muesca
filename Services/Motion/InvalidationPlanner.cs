namespace ClaudeBarWin.Services.Motion;

/// <summary>Qué parte del panel hay que repintar en un tick.</summary>
public enum InvalidationKind { None, MascotOnly, Full }

/// <summary>
/// Firma del tick lento: minuto de pared + texto del pie. Si no cambia, el panel en reposo no
/// tiene nada nuevo que enseñar (las cuentas atrás van por minutos y el relativo vive en el pie).
/// </summary>
public readonly record struct SlowTickSignature(long Minute, string Footer)
{
    public static SlowTickSignature From(DateTime utcNow, string footer)
        => new(utcNow.Ticks / TimeSpan.TicksPerMinute, footer);
}

/// <summary>
/// Decide, de forma PURA, cuánto invalidar en cada tick (F3 Task 1). Con Claude trabajando lo único
/// que se mueve es la mascota (spinner, parpadeo, verbo, bote): basta con repintar su celda en vez
/// del panel entero a ~30 fps. Cualquier otra animación (fade, entrada escalonada, tween de
/// barras/números, realce de hover) afecta a zonas arbitrarias ⇒ panel entero.
/// </summary>
public static class InvalidationPlanner
{
    public static InvalidationKind Plan(bool fading, bool staggering, bool tweening,
                                        bool hoverAnimating, bool mascotAnimating)
    {
        if (fading || staggering || tweening || hoverAnimating) return InvalidationKind.Full;
        return mascotAnimating ? InvalidationKind.MascotOnly : InvalidationKind.None;
    }

    /// <summary>¿Cambió la firma del tick lento? Sin firma previa siempre se repinta.</summary>
    public static bool SlowTickNeedsRepaint(SlowTickSignature? last, SlowTickSignature now)
        => last is not { } l || l != now;
}
