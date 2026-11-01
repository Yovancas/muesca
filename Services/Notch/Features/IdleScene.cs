using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Lo que se pinta del gato: fase, mirada en px lógicos enteros y si parpadea.</summary>
public readonly record struct MascotLook(MascotState State, Point Gaze, bool Blinking);

/// <summary>
/// Reposo del notch (F5 · B7): junta <see cref="IdleCarousel"/> (anillos 7 s ↔ reloj 5 s) y <see cref="NotchMascot"/> (el
/// gato) y dice qué maquetación pintar, de qué tamaño y cuándo despertar. PURO: el instante llega por parámetro.
///
/// <list type="bullet">
/// <item><b>Calma</b> (la decide quien llama): sin Claude trabajando, sin avisos, sin la canción en el reposo y visible.
/// La vista previa y el expandido siguen siendo calma: acercar el ratón no espanta al gato.</item>
/// <item><b>Reloj</b> solo en <see cref="NotchMode.Idle"/> con calma; cada vuelta al reposo empieza por los anillos. El
/// cambio de cara es otra maquetación del mismo modo (<see cref="LayoutClock"/>): el animador funde el contenido.</item>
/// <item><b>Gato</b> tras 15 s de calma, en el reposo, en la vista previa y en la raya (encogido: sustituye a la raya,
/// <see cref="MinimalMascotWidth"/>×<see cref="MinimalMascotHeight"/>); nunca en el expandido, trabajando ni en avisos.
/// Mira al cursor solo con el ratón encima (no con reduce-motion); pasar el ratón sobre el gato dormido lo despierta.</item>
/// <item><b>0 % CPU:</b> nada de timers; <see cref="NextWakeIn"/> da el siguiente cambio visible (cara del carrusel, minuto
/// del reloj a la vista, aparecer, parpadeo, dormirse). Dormido en la raya no queda ninguno.</item>
/// </list>
/// </summary>
public sealed class IdleScene
{
    /// <summary>Bits de maquetación para <see cref="NotchAnimator.SetTarget"/> (el 1 es el reposo con música de B3).</summary>
    public const int LayoutClock = 2, LayoutMascot = 4;

    public static readonly TimeSpan QuotaFor = TimeSpan.FromSeconds(7);
    public static readonly TimeSpan ClockFor = TimeSpan.FromSeconds(5);

    /// <summary>Hueco del gato a la izquierda (px lógicos, se suma al ancho) y centro de sus ojos desde el borde.</summary>
    public const float MascotSlot = 20f, MascotCenterX = 16f;

    /// <summary>Raya con el gato: un poco más alta que la de 4 px para que quepan los ojos.</summary>
    public const float MinimalMascotWidth = 44f, MinimalMascotHeight = 12f;

    /// <summary>Margen lateral del reloj cuando la hora y la fecha no caben en el reposo de siempre.</summary>
    public const float ClockPadX = 14f;

    // Ojos (px lógicos): separación entre centros, ancho y alto abiertos; cerrados son una raya.
    private const float EyeSpacing = 8f, EyeWidth = 3f, EyeHeight = 5f, ClosedWidth = 4f, ClosedHeight = 1.5f;

    private readonly IdleCarousel _carousel = new(QuotaFor, ClockFor);
    private bool _carouselOn;
    private bool _calm;
    private DateTime? _calmSince;
    private PointF? _pointer;

    public NotchMascot Mascot { get; } = new();

    public bool ClockEnabled { get; set; } = true;
    public bool MascotEnabled { get; set; } = true;

    /// <summary>Sin ojos que sigan al cursor (lenguaje de movimiento §6.2).</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Modo objetivo de la última <see cref="Update"/>.</summary>
    public NotchMode Mode { get; private set; } = NotchMode.Hidden;

    /// <summary>Cara del reposo: anillos o reloj (siempre anillos fuera del reposo en calma).</summary>
    public IdleFace Face { get; private set; }

    /// <summary>¿Se pinta el gato en el modo actual?</summary>
    public bool MascotShown => MascotEnabled && _calm && Mascot.State != MascotState.Hidden && Drawable(Mode);

    /// <summary>Maquetación del modo actual (0 = la de siempre).</summary>
    public int Layout => (Mode == NotchMode.Idle && Face == IdleFace.Clock ? LayoutClock : 0) | (MascotShown ? LayoutMascot : 0);

    public MascotLook Look => new(Mascot.State,
        Mascot.State == MascotState.LookAt ? new Point(Round(Mascot.Gaze.X), Round(Mascot.Gaze.Y)) : Point.Empty,
        Mascot.Blinking);

    /// <summary>Modo objetivo de la máquina y si hay calma (ver resumen). Aplica los cambios por tiempo.</summary>
    public void Update(NotchMode mode, bool calm, DateTime now)
    {
        Mode = mode;
        _calm = calm;
        if (calm && MascotEnabled) _calmSince ??= now;
        else _calmSince = null;
        Mascot.OnIdleSince(_calmSince, now);

        bool carousel = ClockEnabled && calm && mode == NotchMode.Idle;
        if (carousel && !_carouselOn) _carousel.Restart(now);
        _carouselOn = carousel;
        Face = carousel ? _carousel.Face(now) : IdleFace.Quota;
    }

    /// <summary>
    /// Cursor relativo al centro de los ojos (px lógicos), o <c>null</c> si no está sobre el notch. Devuelve si cambia lo que
    /// se pinta (la mirada va en px enteros: moverse dentro del mismo píxel no repinta).
    /// </summary>
    public bool OnPointer(PointF? relative, DateTime now)
    {
        var before = Look;
        if (relative is not null && _pointer is null && _calmSince is not null
            && Mascot.State is MascotState.Sleepy or MascotState.Asleep)
        {
            // Pasar por encima del gato dormido lo despierta (600 ms) y se queda mirando: la calma «empezó» hace 15 s.
            _calmSince = now - NotchMascot.AppearAfter;
            Mascot.OnIdleSince(_calmSince, now);
        }
        _pointer = relative;
        Mascot.OnPointer(ReduceMotion ? null : relative, now);
        return Look != before;
    }

    /// <summary>Hasta el siguiente cambio visible; <c>null</c> si no hay ninguno (0 % CPU).</summary>
    /// <param name="local">Hora local de ahora (el minuto del reloj).</param>
    public TimeSpan? NextWakeIn(DateTime now, DateTime local)
    {
        TimeSpan? next = null;
        if (_carouselOn)
        {
            next = _carousel.NextChangeIn(now);
            if (Face == IdleFace.Clock) next = Min(next, UntilNextMinute(local));
        }
        if (MascotEnabled && _calm && Drawable(Mode)) next = Min(next, Mascot.NextWakeIn(now));
        return next;
    }

    /// <summary>Hasta el siguiente minuto en punto (siempre &gt; 0).</summary>
    public static TimeSpan UntilNextMinute(DateTime local) =>
        TimeSpan.FromTicks(TimeSpan.TicksPerMinute - local.Ticks % TimeSpan.TicksPerMinute);

    /// <summary>
    /// Tamaño (px físicos) del modo con la maquetación <paramref name="layout"/>. Sin bits es el de <see cref="NotchLayout"/>.
    /// <paramref name="clockText"/>: ancho en px lógicos de «hora  fecha» (el reposo se ensancha si no cabe).
    /// </summary>
    public static SizeF SizeFor(NotchMode mode, int layout, float clockText, float scale)
    {
        var b = NotchLayout.TargetSize(mode, 1f, 0f);
        bool cat = (layout & LayoutMascot) != 0;
        float w = b.Width, h = b.Height;
        if (mode == NotchMode.Minimal)
        {
            if (cat) (w, h) = (MinimalMascotWidth, MinimalMascotHeight);
        }
        else
        {
            if (mode == NotchMode.Idle && (layout & LayoutClock) != 0) w = Math.Max(w, clockText + 2 * ClockPadX);
            if (cat) w += MascotSlot;
        }
        return new SizeF(w * scale, h * scale);
    }

    /// <summary>Zona del contenido (anillos, reloj, vista previa) dentro de la pastilla: con el gato, a su derecha.</summary>
    public static RectangleF ContentArea(RectangleF pill, bool mascot, float s) =>
        mascot ? RectangleF.FromLTRB(pill.Left + MascotSlot * s, pill.Top, pill.Right, pill.Bottom) : pill;

    /// <summary>Centro de los ojos (px físicos): en la raya, su centro; si no, el hueco de la izquierda.</summary>
    public static PointF MascotCenter(RectangleF pill, NotchMode mode, float s) => mode == NotchMode.Minimal
        ? new PointF(pill.Left + pill.Width / 2f, pill.Top + pill.Height / 2f)
        : new PointF(pill.Left + MascotCenterX * s, pill.Top + pill.Height / 2f);

    /// <summary>
    /// Los dos ojos (px físicos) alrededor de <paramref name="center"/>: abiertos 3×5 desplazados por la mirada; adormilado o
    /// despertando, entornados; parpadeando o dormido, una raya (dormido, un píxel más abajo: «-.-»).
    /// </summary>
    public static (RectangleF Left, RectangleF Right) EyeRects(PointF center, MascotLook look, float s)
    {
        bool closed = look.Blinking || look.State == MascotState.Asleep;
        float w = (closed ? ClosedWidth : EyeWidth) * s;
        float h = (closed ? ClosedHeight : look.State switch
        {
            MascotState.Sleepy => 2f,
            MascotState.Waking => 3f,
            _ => EyeHeight,
        }) * s;
        float cx = center.X + look.Gaze.X * s;
        float cy = center.Y + (look.Gaze.Y + (look.State == MascotState.Asleep ? 1f : 0f)) * s;
        float half = EyeSpacing / 2f * s;
        return (new RectangleF(cx - half - w / 2f, cy - h / 2f, w, h), new RectangleF(cx + half - w / 2f, cy - h / 2f, w, h));
    }

    private static bool Drawable(NotchMode m) => m is NotchMode.Idle or NotchMode.Minimal or NotchMode.Peek;

    private static int Round(float v) => (int)MathF.Round(v, MidpointRounding.AwayFromZero);

    private static TimeSpan? Min(TimeSpan? a, TimeSpan? b) => a is null ? b : b is null ? a : (a < b ? a : b);
}
