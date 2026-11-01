using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Cambio de página del expandido con la variante sobria del lenguaje de movimiento (§3.3, «Páginas»): la vieja y la
/// nueva se cruzan en <see cref="Duration"/> (lineal en alfa, como el cruce dentro del mismo diseño) mientras se
/// desplazan <see cref="Shift"/> px en el sentido del cambio, en px enteros; la píldora indicadora se desliza entre los
/// puntos con Ajuste (sin pasarse). El alto de la pastilla lo reajusta <see cref="NotchAnimator"/> (mismo modo ⇒ Ajuste).
///
/// <para>PURO: el instante llega por parámetro y no hay timers. Quieto ⇒ <see cref="IsAnimating"/> = false, así la
/// ventana para su bucle (0 % CPU en reposo). Con reduce-motion todo salta en el mismo fotograma.</para>
/// </summary>
public sealed class PageTransition
{
    /// <summary>Cruce de las dos páginas.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(120);

    /// <summary>Desplazamiento (px lógicos) de la página que entra y de la que sale.</summary>
    public const float Shift = 8f;

    // Ajuste en unidades de página: 0,02 de página ≈ 0,4 px con el paso de los puntos (umbral de geometría, §2.1).
    private static readonly SpringPreset IndicatorPreset = Motion.Motion.SpringAdjust with { EpsX = 0.02, EpsV = 0.2 };

    private readonly SpringValue _indicator;
    private TimeSpan _at, _last;
    private bool _fading;
    private int _direction = 1;

    public PageTransition(NotchPage initial, int index)
    {
        Current = initial;
        _indicator = new SpringValue(index, IndicatorPreset);
    }

    /// <summary>Saltar en vez de animar. Se aplica en el siguiente <see cref="Go"/>/<see cref="Step"/>.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Página que entra (o la única, en reposo).</summary>
    public NotchPage Current { get; private set; }

    /// <summary>Página que sale; <c>null</c> cuando ya no se pinta.</summary>
    public NotchPage? Previous { get; private set; }

    /// <summary>Alfa de <see cref="Current"/> [0, 1]; la de <see cref="Previous"/> es <c>1 − Blend</c>.</summary>
    public float Blend { get; private set; } = 1f;

    /// <summary>Desplazamiento horizontal de <see cref="Current"/> (px lógicos enteros): de ±8 a 0.</summary>
    public float CurrentShift => MathF.Round(_direction * Shift * (1f - Blend));

    /// <summary>Desplazamiento horizontal de <see cref="Previous"/> (px lógicos enteros): de 0 a ∓8.</summary>
    public float PreviousShift => MathF.Round(-_direction * Shift * Blend);

    /// <summary>Posición de la píldora indicadora en unidades de página (0 = primer punto).</summary>
    public float IndicatorPos => (float)_indicator.Value;

    /// <summary>¿Queda cruce o indicador en movimiento?</summary>
    public bool IsAnimating => _fading || !_indicator.IsSettled;

    /// <summary>
    /// Va a <paramref name="to"/> (índice <paramref name="index"/>). <paramref name="direction"/>: +1 = siguiente (entra
    /// por la derecha), −1 = anterior. A media transición, la página que estaba entrando pasa a ser la que sale.
    /// </summary>
    public void Go(NotchPage to, int index, int direction, TimeSpan now)
    {
        if (to == Current && !_fading) { _indicator.SetTarget(index); return; }
        if (!IsAnimating) _last = now;   // tras un reposo, el primer paso no integra todo ese tiempo
        Previous = Current;
        Current = to;
        _direction = direction < 0 ? -1 : 1;
        _indicator.SetTarget(index);
        if (ReduceMotion) { Settle(); return; }
        _at = now;
        _fading = true;
        Blend = 0f;
    }

    /// <summary>Coloca <paramref name="to"/> sin animación (al abrir el expandido).</summary>
    public void Jump(NotchPage to, int index)
    {
        Current = to;
        _indicator.Snap(index);
        _fading = false;
        Previous = null;
        Blend = 1f;
    }

    /// <summary>Avanza hasta <paramref name="now"/>.</summary>
    public void Step(TimeSpan now)
    {
        double dt = (now - _last).TotalSeconds;
        _last = now;
        if (ReduceMotion) { Settle(); return; }
        if (dt > 0) _indicator.Advance(dt);
        if (!_fading) return;
        double k = (now - _at).TotalMilliseconds / Duration.TotalMilliseconds;
        if (k >= 1) { _fading = false; Previous = null; Blend = 1f; return; }
        Blend = (float)Math.Clamp(k, 0, 1);
    }

    private void Settle()
    {
        _indicator.Snap(_indicator.Target);
        _fading = false;
        Previous = null;
        Blend = 1f;
    }
}
