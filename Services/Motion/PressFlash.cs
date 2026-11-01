using System.Drawing;

namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Destello al pulsar (micro-interacción 3 del lenguaje de movimiento §4): al hacer clic en una fila
/// de sesión su fondo salta al color de pulsación (+14 % de luminosidad) y vuelve con
/// <see cref="Motion.Rapido"/> (~160 ms). Confirma el clic sin abrir un toast. PURO: <see cref="Advance"/>
/// recibe segundos; con reduce-motion el llamante pasa <c>snap: true</c> y no hay destello.
/// </summary>
public sealed class PressFlash
{
    private readonly SpringValue _intensity = new(0, Motion.Rapido);

    /// <summary>Clave del elemento pulsado (para seguirlo si se mueve con el contenido).</summary>
    public string? Key { get; private set; }

    /// <summary>Rect del elemento pulsado.</summary>
    public RectangleF Rect { get; private set; }

    /// <summary>Intensidad (0..1) del destello.</summary>
    public double Intensity => Math.Clamp(_intensity.Value, 0.0, 1.0);

    public bool Visible => Intensity > 0;

    public bool IsAnimating => !_intensity.IsSettled;

    /// <summary>Dispara el destello en <paramref name="rect"/>.</summary>
    public void Trigger(string key, RectangleF rect, bool snap = false)
    {
        Key = key;
        Rect = rect;
        if (snap) { _intensity.Snap(0); return; }
        _intensity.Snap(1);
        _intensity.SetTarget(0);
    }

    /// <summary>El elemento se movió con el contenido: el destello lo acompaña.</summary>
    public void Follow(RectangleF rect) => Rect = rect;

    /// <summary>Apaga el destello al instante (panel oculto).</summary>
    public void Reset()
    {
        Key = null;
        _intensity.Snap(0);
    }

    public void Advance(double dtSeconds) => _intensity.Advance(dtSeconds);
}
