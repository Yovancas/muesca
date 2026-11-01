using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Flecha de página del expandido: el valor es el sentido (−1 = anterior, +1 = siguiente).</summary>
public enum PageArrow { None = 0, Previous = -1, Next = 1 }

/// <summary>
/// Cómo se pinta una flecha en este frame. <see cref="Alpha"/> [0, 1]: aparición × disponible (en la primera o la última
/// página la que no lleva a ningún sitio se desvanece). <see cref="Hover"/> [0, 1]: mezcla hacia el acento.
/// <see cref="Scale"/>: tamaño relativo (crece al pasar por encima). <see cref="Offset"/>: px lógicos hacia SU dirección
/// (negativo = hacia los puntos: el deslizamiento al aparecer).
/// </summary>
public readonly record struct PageArrowLook(float Alpha, float Hover, float Scale, float Offset);

/// <summary>Las dos flechas en este frame (las calcula <see cref="PageArrows"/>; el painter solo pinta).</summary>
public readonly record struct PageArrowsView(PageArrowLook Previous, PageArrowLook Next)
{
    public PageArrowLook this[PageArrow side] => side == PageArrow.Previous ? Previous : Next;
}

/// <summary>
/// Flechas ‹ › a los lados de los puntos de página. PURO: el tiempo llega por <see cref="Advance"/>; quieto ⇒
/// <see cref="IsAnimating"/> = false (el bucle se para: 0 % CPU en reposo). Muelles de <see cref="Motion.Motion"/>, como el
/// resto del notch:
/// <list type="bullet">
/// <item>Aparecen con un fundido y un deslizamiento corto (<see cref="EnterSlide"/> px desde los puntos) al pasar el ratón
/// por el expandido; se van igual.</item>
/// <item>Encima de una: crece (<see cref="HoverGrow"/>) y se desplaza <see cref="HoverNudge"/> px hacia su dirección.</item>
/// <item>Al pulsar: un empujón hacia su dirección (<see cref="PushVelocity"/>) que vuelve con el muelle de Atención.</item>
/// <item>No dan la vuelta: en la primera página la anterior se desvanece; en la última, la siguiente
/// (<see cref="Target"/>). Con una sola página no hay flechas.</item>
/// </list>
/// Con reducir movimiento todo salta (sin deslizamiento, sin crecer y sin empujón): solo cambian alfa y color.
/// </summary>
public sealed class PageArrows
{
    /// <summary>Deslizamiento al aparecer (px lógicos, desde los puntos hacia fuera).</summary>
    public const float EnterSlide = 4f;

    /// <summary>Crecimiento relativo con el ratón encima (1 → 1,25).</summary>
    public const float HoverGrow = 0.25f;

    /// <summary>Desplazamiento hacia su dirección con el ratón encima (px lógicos).</summary>
    public const float HoverNudge = 2f;

    /// <summary>Velocidad inicial del empujón al pulsar (px lógicos/s): con el muelle de Atención llega a unos 3 px y vuelve
    /// con un rebote pequeño.</summary>
    public const double PushVelocity = 90;

    private sealed class Side
    {
        public readonly SpringValue Available = new(0, Motion.Motion.Rapido);
        public readonly SpringValue Hover = new(0, Motion.Motion.HoverIn);
        public readonly SpringValue Push = new(0, Motion.Motion.Atencion);

        public bool IsAnimating => !Available.IsSettled || !Hover.IsSettled || !Push.IsSettled;

        public void Advance(double dt)
        {
            Available.Advance(dt);
            Hover.Advance(dt);
            Push.Advance(dt);
        }

        public void Settle()
        {
            Available.Snap(Available.Target);
            Hover.Snap(Hover.Target);
            Push.Snap(0);
        }
    }

    private readonly SpringValue _shown = new(0, Motion.Motion.Rapido);
    private readonly Side _prev = new(), _next = new();

    /// <summary>Saltar en vez de animar (y sin deslizamiento, crecimiento ni empujón).</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>¿Queda algún muelle en movimiento?</summary>
    public bool IsAnimating => !_shown.IsSettled || _prev.IsAnimating || _next.IsAnimating;

    /// <summary>
    /// Página a la que lleva la flecha <paramref name="side"/> desde <paramref name="index"/> de <paramref name="count"/>, o −1 si
    /// no lleva a ningún sitio (sin dar la vuelta, con una sola página o sin flecha).
    /// </summary>
    public static int Target(int index, int count, PageArrow side)
    {
        if (side == PageArrow.None || count < 2 || index < 0 || index >= count) return -1;
        int to = index + (int)side;
        return to >= 0 && to < count ? to : -1;
    }

    /// <summary>
    /// Objetivos de este instante: <paramref name="visible"/> (ratón sobre el expandido, en la ventana interactiva), la página
    /// <paramref name="index"/> de <paramref name="count"/> y la flecha bajo el ratón (<paramref name="hover"/>; si no lleva a
    /// ningún sitio no se realza). Sin reservas: se puede llamar en cada frame.
    /// </summary>
    public void Update(bool visible, int index, int count, PageArrow hover)
    {
        bool on = visible && count > 1;
        bool prevOk = Target(index, count, PageArrow.Previous) >= 0, nextOk = Target(index, count, PageArrow.Next) >= 0;
        // Ocultas del todo, la disponibilidad salta: al volver a aparecer no se ve un fundido a destiempo.
        bool hidden = !on && _shown.Value <= 0 && _shown.IsSettled;
        Set(_shown, on ? 1 : 0, ReduceMotion);
        Set(_prev.Available, prevOk ? 1 : 0, ReduceMotion || hidden);
        Set(_next.Available, nextOk ? 1 : 0, ReduceMotion || hidden);
        Set(_prev.Hover, on && prevOk && hover == PageArrow.Previous ? 1 : 0, ReduceMotion);
        Set(_next.Hover, on && nextOk && hover == PageArrow.Next ? 1 : 0, ReduceMotion);
    }

    /// <summary>Pulsación sobre <paramref name="side"/>: el empujón (si la flecha está y lleva a algún sitio).</summary>
    public void Press(PageArrow side)
    {
        if (ReduceMotion || side == PageArrow.None) return;
        var s = side == PageArrow.Previous ? _prev : _next;
        if (s.Available.Target <= 0 || _shown.Target <= 0) return;
        s.Push.Impulse(PushVelocity);
    }

    /// <summary>Avanza los muelles <paramref name="dtSeconds"/>.</summary>
    public void Advance(double dtSeconds)
    {
        _shown.Advance(dtSeconds);
        _prev.Advance(dtSeconds);
        _next.Advance(dtSeconds);
    }

    /// <summary>Todo a su objetivo sin animación (al cerrar el expandido).</summary>
    public void Settle()
    {
        _shown.Snap(_shown.Target);
        _prev.Settle();
        _next.Settle();
    }

    /// <summary>Lo que se pinta de las dos flechas en este frame.</summary>
    public PageArrowsView View => new(Look(_prev), Look(_next));

    private PageArrowLook Look(Side s)
    {
        float shown = Clamp01(_shown.Value), hover = Clamp01(s.Hover.Value);
        float alpha = shown * Clamp01(s.Available.Value);
        if (ReduceMotion) return new PageArrowLook(alpha, hover, 1f, 0f);
        float offset = HoverNudge * hover + (float)s.Push.Value - EnterSlide * (1f - shown);
        return new PageArrowLook(alpha, hover, 1f + HoverGrow * hover, offset);
    }

    private static float Clamp01(double v) => (float)Math.Clamp(v, 0.0, 1.0);

    private static void Set(SpringValue v, double target, bool snap)
    {
        v.SetTarget(target);
        if (snap) v.Snap(target);
    }
}
