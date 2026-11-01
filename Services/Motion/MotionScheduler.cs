namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Decide, de forma PURA, cuándo y a qué cadencia debe latir el reloj de animación. Es la
/// pieza que protege la CPU 24/7 (regla de oro F3): con el panel <b>oculto</b> no quiere
/// ningún tick rápido — devuelve "parar" — pase lo que pase con la animación. Sin estado de
/// reloj, sin red: solo función de (¿visible?, ¿algo animando?).
///
/// <para>Cadencias (de <see cref="Motion"/>):</para>
/// <list type="bullet">
/// <item><b>Panel visible + algo animando</b> ⇒ tick rápido (<see cref="Motion.FastTickMs"/>, ~30 fps).</item>
/// <item><b>Panel visible sin animación</b> ⇒ tick lento (<see cref="Motion.SlowTickMs"/>, refresco del countdown).</item>
/// <item><b>Panel oculto</b> ⇒ parar (intervalo &lt;= 0): cero trabajo extra en reposo.</item>
/// </list>
///
/// <para>Con <c>reduceMotion</c> activo (Tarea 7) el fast-tick queda desactivado por una sola
/// puerta: nunca se pide la cadencia rápida; el panel visible sigue refrescando el countdown
/// a 1 s. Es el primo "scheduler" del gate único de reduce-motion.</para>
/// </summary>
public sealed class MotionScheduler
{
    private readonly bool _reduceMotion;

    public MotionScheduler(bool reduceMotion = false)
    {
        _reduceMotion = reduceMotion;
    }

    /// <summary>
    /// ¿Debe correr el tick rápido (~33 ms)? Solo si el panel es <paramref name="visible"/>,
    /// hay algo <paramref name="animating"/> y reduce-motion NO está activo. Con el panel
    /// oculto SIEMPRE devuelve <c>false</c> (invariante de CPU 24/7).
    /// </summary>
    public bool WantsFastTick(bool visible, bool animating)
        => visible && animating && !_reduceMotion;

    /// <summary>
    /// Intervalo del timer (ms) que se desea ahora. <c>&lt;= 0</c> ⇒ "parar el timer" (panel
    /// oculto). <see cref="Motion.FastTickMs"/> si quiere fast-tick; si no,
    /// <see cref="Motion.SlowTickMs"/> mientras el panel sea visible (countdown).
    /// </summary>
    public int DesiredIntervalMs(bool visible, bool animating)
    {
        if (!visible) return 0; // parar: nada útil que repintar con el panel oculto
        return WantsFastTick(visible, animating) ? Motion.FastTickMs : Motion.SlowTickMs;
    }

    /// <summary>
    /// Como <see cref="DesiredIntervalMs(bool, bool)"/> pero, quieto, duerme hasta el siguiente cambio
    /// de un texto del reloj (<paramref name="idleWakeMs"/>, de <see cref="IdleCadence"/>) en lugar de
    /// latir a 1 Hz (lenguaje de movimiento §4, «Cadencia del panel»).
    /// </summary>
    public int DesiredIntervalMs(bool visible, bool animating, int idleWakeMs)
    {
        if (!visible) return 0;
        return WantsFastTick(visible, animating) ? Motion.FastTickMs
            : Math.Clamp(idleWakeMs, IdleCadence.MinIdleMs, IdleCadence.MaxIdleMs);
    }

    /// <summary>¿Debe el timer estar en marcha? Verdadero solo con el panel visible.</summary>
    public bool IsRunning(bool visible, bool animating) => visible;
}
