namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Valor escalar que <b>avanza</b> hacia un objetivo con easing y duración. PURO y
/// elapsed-driven: <see cref="Advance"/> recibe el delta de tiempo (ms) por parámetro — jamás
/// lee el reloj ni <c>Math.Random</c> por dentro (regla de oro F3), así los tests son
/// deterministas sin reloj/GDI+.
///
/// <para>Semántica:</para>
/// <list type="bullet">
/// <item><see cref="Set"/> rearma la animación <b>desde el valor actual</b> (sin salto) hacia el
/// nuevo objetivo. Re-apuntar a media animación NO produce discontinuidad en <see cref="Value"/>.</item>
/// <item><see cref="Advance"/> integra el tiempo transcurrido; <see cref="Value"/> muestrea la curva.</item>
/// <item><see cref="Snap"/> (y duración &lt;= 0) llevan el valor al objetivo al instante. Esto es
/// también la puerta del reduce-motion (Tarea 7).</item>
/// </list>
/// </summary>
public sealed class AnimatedValue
{
    private double _start;        // valor al rearmar
    private double _target;       // objetivo
    private double _elapsedMs;    // tiempo integrado desde el último Set
    private double _durMs;        // duración del tramo actual
    private Func<double, double> _ease = Easing.OutCubic;

    public AnimatedValue(double initial)
    {
        _start = _target = initial;
        _elapsedMs = 0;
        _durMs = 0;
    }

    /// <summary>Valor actual muestreado de la curva (entre <c>start</c> y <c>target</c>).</summary>
    public double Value
    {
        get
        {
            if (_durMs <= 0) return _target;
            double t = _elapsedMs / _durMs;
            if (t >= 1.0) return _target;
            if (t <= 0.0) return _start;
            double k = _ease(t);
            return _start + (_target - _start) * k;
        }
    }

    /// <summary>El valor objetivo hacia el que se anima (o en el que ya está).</summary>
    public double Target => _target;

    /// <summary><c>true</c> mientras el valor no ha alcanzado el objetivo.</summary>
    public bool IsAnimating => _durMs > 0 && _elapsedMs < _durMs && _start != _target;

    /// <summary>
    /// Rearma hacia <paramref name="target"/> desde el valor actual, con la <paramref name="ease"/>
    /// y la <paramref name="durMs"/> dados. Si el objetivo no cambia, no reinicia la animación. Si
    /// <paramref name="durMs"/> &lt;= 0, salta al objetivo (instantáneo).
    /// </summary>
    public void Set(double target, double durMs, Func<double, double>? ease = null)
    {
        if (target == _target && !IsAnimating)
        {
            // Mismo objetivo y ya asentado: nada que animar.
            _target = target;
            return;
        }

        _start = Value;          // arranca desde donde estamos ahora (sin salto)
        _target = target;
        _ease = ease ?? Easing.OutCubic;
        _elapsedMs = 0;
        _durMs = durMs;

        if (_durMs <= 0)
        {
            // Instantáneo: asienta el estado para que IsAnimating sea false.
            _start = _target;
            _durMs = 0;
        }
    }

    /// <summary>Integra <paramref name="elapsedMs"/> de tiempo en la animación en curso.</summary>
    public void Advance(double elapsedMs)
    {
        if (_durMs <= 0) return;
        if (elapsedMs <= 0) return;
        _elapsedMs += elapsedMs;
        if (_elapsedMs >= _durMs)
        {
            // Llegó al final: colapsa el estado para que Value==target e IsAnimating==false.
            _elapsedMs = _durMs;
            _start = _target;
            _durMs = 0;
        }
    }

    /// <summary>Lleva el valor al objetivo al instante (puerta del reduce-motion).</summary>
    public void Snap()
    {
        _start = _target;
        _elapsedMs = 0;
        _durMs = 0;
    }
}
