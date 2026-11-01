using System.Drawing;

namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Crossfade de color con muelle (F3 Task 6): el color de fase/humor de la mascota cambiaba de golpe.
/// Al cambiar de objetivo mezcla (<see cref="ColorMath.Lerp"/>) desde el color que se está mostrando
/// —sin saltos aunque llegue otro cambio a mitad— con un <see cref="SpringValue"/>
/// <see cref="Motion.Rapido"/> (lenguaje de movimiento §4 #8: color mezclado con Rápido, 95 % a 159 ms).
/// PURO: <see cref="Advance"/> recibe segundos.
/// </summary>
public sealed class ColorCrossfade
{
    // Progreso 0..1 con el umbral fino de opacidad/mezcla del preset (§2.1).
    private readonly SpringValue _t = new(1, Motion.Rapido);
    private Color _from;
    private Color _to;

    public ColorCrossfade(Color initial) => _from = _to = initial;

    /// <summary>Color a pintar ahora.</summary>
    public Color Current => ColorMath.Lerp(_from, _to, Math.Clamp(_t.Value, 0.0, 1.0));

    /// <summary>Color hacia el que va.</summary>
    public Color Target => _to;

    public bool IsAnimating => !_t.IsSettled;

    /// <summary>Nuevo objetivo; con <paramref name="snap"/> (reduce-motion, cambio de tema) salta.</summary>
    public void SetTarget(Color c, bool snap = false)
    {
        if (snap)
        {
            _from = _to = c;
            _t.Snap(1);
            return;
        }
        if (c.ToArgb() == _to.ToArgb()) return;
        _from = Current;
        _to = c;
        _t.Snap(0);
        _t.SetTarget(1);
    }

    public void Advance(double dtSeconds) => _t.Advance(dtSeconds);
}
