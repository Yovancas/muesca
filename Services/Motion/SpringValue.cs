namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Rigidez (k), amortiguación (c) y umbrales de reposo de un muelle con masa 1. Los presets viven en
/// <see cref="Motion"/>. Los umbrales dependen de la unidad (lenguaje de movimiento §2.1: px, opacidad,
/// grados, %); sin ellos valen los globales <see cref="Motion.SpringEpsX"/> / <see cref="Motion.SpringEpsV"/>.
/// </summary>
public readonly record struct SpringPreset(double Stiffness, double Damping,
    double EpsX = Motion.SpringEpsX, double EpsV = Motion.SpringEpsV);

/// <summary>
/// Valor escalar movido por un <b>muelle</b> (masa 1) hacia un objetivo. PURO y elapsed-driven
/// como <see cref="AnimatedValue"/>: <see cref="Advance"/> recibe el delta en <b>segundos</b> por
/// parámetro — nunca lee el reloj por dentro (regla del repo).
///
/// <para>Integración semiimplícita (Euler simpléctico): primero la velocidad, luego la posición
/// con la velocidad nueva. Es estable con los presets de <see cref="Motion"/> y barata.</para>
/// <list type="bullet">
/// <item>El delta se recorta a <see cref="Motion.SpringMaxDt"/> (un frame de 5 s tras suspender
/// el PC no puede lanzar el valor al infinito) y se trocea en subpasos de como mucho
/// <see cref="Motion.SpringSubstep"/>: así 60 Hz y 240 Hz dibujan prácticamente la misma curva.</item>
/// <item>Cuando posición y velocidad bajan de los umbrales del preset (<see cref="SpringPreset.EpsX"/> /
/// <see cref="SpringPreset.EpsV"/>) el valor se <b>encaja</b> exacto en el objetivo con velocidad 0:
/// sin residuos que mantengan vivo el bucle de render (0 % CPU en reposo).</item>
/// <item><see cref="SetTarget"/> no toca la velocidad: re-apuntar a media animación no da tirón.</item>
/// </list>
/// </summary>
public sealed class SpringValue
{
    private SpringPreset _preset;

    public SpringValue(double initial, SpringPreset preset)
    {
        Value = Target = initial;
        Velocity = 0;
        _preset = preset;
    }

    /// <summary>Posición actual.</summary>
    public double Value { get; private set; }

    /// <summary>Velocidad actual (unidades/segundo).</summary>
    public double Velocity { get; private set; }

    /// <summary>Objetivo hacia el que tira el muelle.</summary>
    public double Target { get; private set; }

    /// <summary>Preset vigente (rigidez, amortiguación y umbrales).</summary>
    public SpringPreset Preset => _preset;

    /// <summary>En reposo: cerca del objetivo y casi quieto (mismos umbrales del preset que el encaje).</summary>
    public bool IsSettled =>
        Math.Abs(Value - Target) < _preset.EpsX && Math.Abs(Velocity) < _preset.EpsV;

    /// <summary>Cambia el objetivo conservando posición y velocidad (continuidad).</summary>
    public void SetTarget(double t) => Target = t;

    /// <summary>Cambia rigidez/amortiguación en caliente; el estado (posición, velocidad) se mantiene.</summary>
    public void SetPreset(SpringPreset p) => _preset = p;

    /// <summary>Suma una velocidad instantánea (p. ej. el bote de atención). Despierta un muelle en reposo.</summary>
    public void Impulse(double velocity) => Velocity += velocity;

    /// <summary>Salto inmediato: posición y objetivo a <paramref name="v"/>, velocidad 0 (puerta del reduce-motion).</summary>
    public void Snap(double v)
    {
        Value = Target = v;
        Velocity = 0;
    }

    /// <summary>Integra <paramref name="dtSeconds"/> segundos (recortado y en subpasos fijos).</summary>
    public void Advance(double dtSeconds)
    {
        // dt no positivo o NaN: no hay tiempo que integrar.
        if (!(dtSeconds > 0)) return;
        double dt = Math.Min(dtSeconds, Motion.SpringMaxDt);
        int n = Math.Max(1, (int)Math.Ceiling(dt / Motion.SpringSubstep));
        double h = dt / n;
        double k = _preset.Stiffness, c = _preset.Damping;
        double x = Value, v = Velocity;
        for (int i = 0; i < n; i++)
        {
            v += (-(x - Target) * k - c * v) * h;
            x += v * h;
        }
        if (Math.Abs(x - Target) < _preset.EpsX && Math.Abs(v) < _preset.EpsV)
        {
            x = Target;
            v = 0;
        }
        Value = x;
        Velocity = v;
    }
}
