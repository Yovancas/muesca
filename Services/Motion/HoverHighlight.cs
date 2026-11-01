using System.Drawing;

namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Realce del hover (lenguaje de movimiento §4 #4): PURO y elapsed-driven (<see cref="Advance"/> recibe
/// segundos). La intensidad entra con <see cref="Motion.HoverIn"/> (95 % a 95 ms) y sale con
/// <see cref="Motion.Rapido"/> (160 ms); el rect se mueve con cuatro muelles <see cref="Motion.HoverInPx"/>.
/// <list type="bullet">
/// <item>Al salir (<see cref="Leave"/>) se conserva el último rect hasta que la intensidad llega a 0:
/// el fade-out se ve de verdad (antes la clave pasaba a null antes de animar y no había rect).</item>
/// <item>De A a B con intensidad &gt; <see cref="Motion.HoverSlideMinIntensity"/> el rect se DESLIZA
/// desde donde está, sin apagarse; por debajo se encaja en B y se funde desde la intensidad actual.</item>
/// <item>Si no había nada visible, el realce nace en su sitio (no viaja desde el último rect).</item>
/// </list>
/// Con reduce-motion el llamante pasa <c>snap: true</c> y todo salta al estado final.
/// </summary>
public sealed class HoverHighlight
{
    private const double VisibleEps = 0.01;

    private readonly SpringValue _x = new(0, Motion.HoverInPx);
    private readonly SpringValue _y = new(0, Motion.HoverInPx);
    private readonly SpringValue _w = new(0, Motion.HoverInPx);
    private readonly SpringValue _h = new(0, Motion.HoverInPx);
    private readonly SpringValue _intensity = new(0, Motion.HoverIn);

    /// <summary>Clave bajo el cursor (null tras <see cref="Leave"/>).</summary>
    public string? Key { get; private set; }

    /// <summary>Clave del último realce pintado; se conserva durante el fade-out.</summary>
    public string? LastKey { get; private set; }

    /// <summary>Rect animado del realce.</summary>
    public RectangleF Rect => new((float)_x.Value, (float)_y.Value, (float)_w.Value, (float)_h.Value);

    /// <summary>Rect hacia el que se mueve el realce.</summary>
    public RectangleF TargetRect => new((float)_x.Target, (float)_y.Target, (float)_w.Target, (float)_h.Target);

    /// <summary>Intensidad (0..1) del realce, recortada a ese rango.</summary>
    public double Intensity => Math.Clamp(_intensity.Value, 0.0, 1.0);

    /// <summary>¿Hay algo que pintar?</summary>
    public bool Visible => Intensity > VisibleEps;

    /// <summary>¿Queda algún muelle sin asentar? (alimenta al scheduler).</summary>
    public bool IsAnimating =>
        !(_x.IsSettled && _y.IsSettled && _w.IsSettled && _h.IsSettled && _intensity.IsSettled);

    /// <summary>El cursor entra en <paramref name="key"/> (o su rect cambió). Idempotente.</summary>
    public void Enter(string key, RectangleF r, bool snap = false)
    {
        bool slide = Intensity > Motion.HoverSlideMinIntensity;
        Key = key;
        LastKey = key;
        if (snap || !slide)
        {
            _x.Snap(r.X); _y.Snap(r.Y); _w.Snap(r.Width); _h.Snap(r.Height);
        }
        else
        {
            _x.SetTarget(r.X); _y.SetTarget(r.Y); _w.SetTarget(r.Width); _h.SetTarget(r.Height);
        }
        _intensity.SetPreset(Motion.HoverIn);
        if (snap) _intensity.Snap(1.0);
        else _intensity.SetTarget(1.0);
    }

    /// <summary>
    /// El elemento realzado se movió con el contenido (scroll, relayout): el realce lo acompaña al
    /// instante, sin deslizarse (no es un cambio de elemento).
    /// </summary>
    public void Follow(RectangleF r)
    {
        if (r == TargetRect) return;
        _x.Snap(r.X); _y.Snap(r.Y); _w.Snap(r.Width); _h.Snap(r.Height);
    }

    /// <summary>El cursor sale a un hueco o fuera del panel: se desvanece en el último rect.</summary>
    public void Leave(bool snap = false)
    {
        Key = null;
        _intensity.SetPreset(Motion.Rapido);
        if (snap) _intensity.Snap(0.0);
        else _intensity.SetTarget(0.0);
        if (snap) SettleRect();
    }

    /// <summary>Borra el realce al instante (panel oculto: al reabrir no queda un realce obsoleto).</summary>
    public void Reset()
    {
        Key = null;
        LastKey = null;
        _intensity.Snap(0.0);
        SettleRect();
    }

    /// <summary>Integra <paramref name="dtSeconds"/> segundos en todos los muelles.</summary>
    public void Advance(double dtSeconds)
    {
        _x.Advance(dtSeconds); _y.Advance(dtSeconds);
        _w.Advance(dtSeconds); _h.Advance(dtSeconds);
        _intensity.Advance(dtSeconds);
        // Apagado del todo: el rect ya no se ve, así que no tiene sentido seguir moviéndolo.
        if (!Visible && _intensity.IsSettled) SettleRect();
    }

    private void SettleRect()
    {
        _x.Snap(_x.Target); _y.Snap(_y.Target); _w.Snap(_w.Target); _h.Snap(_h.Target);
    }
}
