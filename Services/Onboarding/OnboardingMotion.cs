using System.Drawing;
using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Services.Onboarding;

/// <summary>En qué punto de la coreografía va el asistente.</summary>
public enum OnboardingStage
{
    /// <summary>Aún no ha empezado.</summary>
    Hidden,
    /// <summary>El notch baja del borde de arriba hasta el tamaño de reposo, con un rebote más marcado que el de siempre.</summary>
    Dropping,
    /// <summary>Un momento en reposo con el logo dentro.</summary>
    Holding,
    /// <summary>Se despliega al tamaño del asistente (ancho primero) y entra la bienvenida.</summary>
    Unfolding,
    /// <summary>Abierto y quieto.</summary>
    Open,
    /// <summary>«Encogerse»: el contenido sale y la forma vuelve al tamaño del notch en reposo (o se mete arriba).</summary>
    Collapsing,
    /// <summary>Ya es el notch de reposo; el notch de verdad aparece debajo y este se queda un rato tapándolo.</summary>
    Landed,
    /// <summary>Se puede cerrar la ventana.</summary>
    Gone,
}

/// <summary>
/// Coreografía de entrada y salida del asistente de bienvenida (lenguaje de movimiento §3, con los muelles algo más vivos
/// que los del notch: es la primera impresión). PURA: el instante llega por parámetro y no hay timers; la ventana pregunta
/// <see cref="IsAnimating"/> para saber si su bucle sigue vivo y, abierto y quieto, se para.
///
/// <list type="bullet">
/// <item><b>Bajar</b>: de 44 × 0 (como al aparecer el notch) al tamaño de reposo, el alto con un muelle de ζ ≈ 0,42 (rebota
/// un 20 % largo) y el ancho con ζ ≈ 0,61; el logo entra cuando el alto pasa del 60 %.</item>
/// <item><b>Desplegar</b>: tras <see cref="Hold"/>, el logo sale, el ancho abre con ζ ≈ 0,62 (el Abrir de siempre es 0,75) y el
/// alto le sigue <see cref="UnfoldStagger"/> después; la bienvenida entra con el alto al 60 %, subiendo
/// <see cref="ContentRise"/> px. Squash-stretch mientras el ancho abre, como en el notch.</item>
/// <item><b>Encogerse</b>: el contenido sale en <see cref="ContentOut"/>; el alto cierra (Cerrar, sin pasarse) y el ancho le
/// sigue 40 ms después, hasta el tamaño de reposo del notch real (en px de SU escala); allí entra su contenido
/// (<see cref="LandedAlpha"/>) y, pasado <see cref="HandoffHold"/>, se acaba. Sin notch (Solo cuota), se mete en el borde
/// de arriba (44 × 0) y se apaga.</item>
/// <item><b>Reducir movimiento</b>: sin muelles ni rebotes. Entra fundiéndose ya abierto; al salir se funde, cambia a la
/// forma de reposo sin verse y vuelve a fundirse hasta ella.</item>
/// </list>
/// </summary>
public sealed class OnboardingMotion
{
    /// <summary>Muelles de la entrada (masa 1): bajar el alto con rebote, el ancho algo menos y desplegar.</summary>
    public static readonly SpringPreset DropHeight = new(320, 15, 0.5, 5);
    public static readonly SpringPreset DropWidth = new(320, 22, 0.5, 5);
    public static readonly SpringPreset Unfold = new(190, 17, 0.5, 5);

    /// <summary>Tiempos de la coreografía.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(240),
        UnfoldStagger = TimeSpan.FromMilliseconds(70),
        CollapseStagger = NotchMotion.AxisStagger,
        LogoIn = TimeSpan.FromMilliseconds(160),
        LogoOut = TimeSpan.FromMilliseconds(90),
        ContentIn = TimeSpan.FromMilliseconds(180),
        ContentOut = TimeSpan.FromMilliseconds(90),
        LandIn = NotchMotion.CrossfadeIn,
        HandoffHold = TimeSpan.FromMilliseconds(700),
        Fade = TimeSpan.FromMilliseconds(220),
        DropMax = TimeSpan.FromMilliseconds(460);

    /// <summary>Subida (px lógicos) del contenido que entra al desplegarse (el notch sube 4).</summary>
    public const float ContentRise = 6f;

    /// <summary>Progreso del alto a partir del cual entran el logo (bajando) y la bienvenida (desplegando).</summary>
    public const float RevealAtProgress = NotchMotion.CrossfadeInAtProgress;

    private const float StartWidth = 44f;   // como el notch al aparecer
    private const double AlphaUnits = 100.0;

    private readonly SpringValue _w = new(0, DropWidth);
    private readonly SpringValue _h = new(0, DropHeight);
    private readonly SpringValue _pill = new(0, Motion.Motion.SpringFast);
    private readonly SpringValue _shadow = new(0, Motion.Motion.SpringFast);

    private TimeSpan _last, _stageAt;
    private (TimeSpan At, double Target, SpringPreset Preset, bool Width)? _pending;
    private TimeSpan? _logoAt, _contentAt;
    private float _scale = 1f;
    private SizeF _idle, _wizard, _final;
    private double _progressFrom, _progressTo;
    private bool _widthOpening, _handoffTaken, _swapped;
    private float _contentFrom = 1f, _logoFrom = 1f;

    /// <summary>Solo fundidos, sin muelles. Se fija al empezar (la ventana lo lee del ajuste).</summary>
    public bool ReduceMotion { get; set; }

    public OnboardingStage Stage { get; private set; } = OnboardingStage.Hidden;

    /// <summary>Caja de la pastilla en px físicos.</summary>
    public float Width => (float)Math.Max(0, _w.Value);
    public float Height => (float)Math.Max(0, _h.Value);

    /// <summary>Alfa de toda la ventana [0, 1].</summary>
    public float PillAlpha { get; private set; }

    public float ShadowAlpha => (float)Math.Clamp(_shadow.Value / AlphaUnits, 0, 1);

    /// <summary>Fundido de la página del asistente [0, 1] y su subida al entrar (px lógicos enteros).</summary>
    public float ContentAlpha { get; private set; }
    public float ContentOffsetY { get; private set; }

    /// <summary>Logo dentro del notch de reposo mientras baja [0, 1].</summary>
    public float LogoAlpha { get; private set; }

    /// <summary>Contenido del notch de reposo al aterrizar (el que enseñará el notch de verdad) [0, 1].</summary>
    public float LandedAlpha { get; private set; }

    /// <summary>Squash-stretch de la forma [−1, 1] mientras el ancho se despliega.</summary>
    public float Squash => ReduceMotion || !_widthOpening ? 0f
        : NotchMotion.Squash((float)_w.Velocity, _wizard.Width - _idle.Width, _scale);

    /// <summary>¿Se pinta ya el contenido del asistente (y acepta clics)? Solo abierto del todo.</summary>
    public bool Interactive => Stage == OnboardingStage.Open || (Stage == OnboardingStage.Unfolding && ContentAlpha >= 1f);

    /// <summary>¿Queda algo en movimiento? Abierto y quieto, no: el bucle de la ventana se para (0 % de CPU).</summary>
    public bool IsAnimating => Stage is OnboardingStage.Dropping or OnboardingStage.Holding or OnboardingStage.Unfolding
        or OnboardingStage.Collapsing or OnboardingStage.Landed
        || (Stage == OnboardingStage.Open && (!_w.IsSettled || !_h.IsSettled || !_shadow.IsSettled));

    public bool IsGone => Stage == OnboardingStage.Gone;

    /// <summary>
    /// Empieza: <paramref name="idle"/> es el notch de reposo y <paramref name="wizard"/> el asistente abierto, los dos en px
    /// físicos a <paramref name="scale"/>.
    /// </summary>
    public void Start(SizeF idle, SizeF wizard, float scale, TimeSpan now)
    {
        _scale = scale > 0 ? scale : 1f;
        _idle = idle;
        _wizard = wizard;
        _last = _stageAt = now;
        _pending = null;
        _logoAt = _contentAt = null;
        _handoffTaken = _swapped = false;
        LandedAlpha = LogoAlpha = 0f;
        if (ReduceMotion)
        {
            _w.Snap(Math.Round(wizard.Width));
            _h.Snap(Math.Round(wizard.Height));
            _shadow.Snap(AlphaUnits);
            PillAlpha = 0f;
            ContentAlpha = 1f;
            ContentOffsetY = 0f;
            Stage = OnboardingStage.Unfolding;
            return;
        }
        _w.Snap(Math.Round(StartWidth * _scale));
        _h.Snap(0);
        _shadow.Snap(0);
        _pill.Snap(0);
        _pill.SetTarget(AlphaUnits);
        _w.SetPreset(DropWidth);
        _h.SetPreset(DropHeight);
        _w.SetTarget(Math.Round(idle.Width));
        _h.SetTarget(Math.Round(idle.Height));
        _progressFrom = 0;
        _progressTo = Math.Round(idle.Height);
        ContentAlpha = 0f;
        ContentOffsetY = 0f;
        PillAlpha = 0f;
        Stage = OnboardingStage.Dropping;
    }

    /// <summary>Otra escala con el asistente abierto (cambio de DPI): salta al tamaño nuevo, sin animar.</summary>
    public void Resize(SizeF wizard, float scale)
    {
        _wizard = wizard;
        _scale = scale > 0 ? scale : 1f;
        if (Stage is not (OnboardingStage.Open or OnboardingStage.Unfolding)) return;
        _pending = null;
        _widthOpening = false;
        _w.Snap(Math.Round(wizard.Width));
        _h.Snap(Math.Round(wizard.Height));
    }

    /// <summary>
    /// «Encogerse» hasta <paramref name="final"/> (el notch de reposo en px físicos de su propia escala), o hasta meterse en el
    /// borde de arriba si es <see cref="SizeF.Empty"/> (sin notch). Desde cualquier punto de la entrada.
    /// </summary>
    public void Finish(SizeF final, TimeSpan now)
    {
        if (Stage is OnboardingStage.Collapsing or OnboardingStage.Landed or OnboardingStage.Gone) return;
        _final = final;
        _last = _stageAt = now;
        _logoAt = null;
        LogoAlpha = 0f;
        _contentFrom = ContentAlpha;
        _swapped = false;
        Stage = OnboardingStage.Collapsing;
        if (ReduceMotion) return;
        bool hidden = final.IsEmpty;
        double tw = hidden ? Math.Round(StartWidth * _scale) : Math.Round(final.Width);
        double th = hidden ? 0 : Math.Round(final.Height);
        _widthOpening = false;
        _h.SetPreset(Motion.Motion.SpringClose);
        _h.SetTarget(th);
        _pending = (now + CollapseStagger, tw, Motion.Motion.SpringClose, true);
        _shadow.SetTarget(0);
        if (hidden) _pill.SetTarget(0);
    }

    /// <summary>¿Ya es el notch de reposo con su contenido entero? Devuelve true UNA vez: entonces la ventana enseña el notch
    /// de verdad debajo. Si un solo paso saltó de <see cref="OnboardingStage.Landed"/> a <see cref="OnboardingStage.Gone"/>
    /// (el bucle de la ventana no corre durante un menú o un arrastre) el relevo sigue pendiente y se da en <c>Gone</c>: sin
    /// él, el notch volvería sin aparecer asentado y sin el aviso de los hooks fallidos.</summary>
    public bool TakeHandoff()
    {
        if (_handoffTaken) return false;
        // A Gone con destino solo se llega pasando por Landed (sin notch, Gone llega desde Collapsing y no hay relevo).
        bool landedThenGone = Stage == OnboardingStage.Gone && !_final.IsEmpty;
        if (!landedThenGone && (Stage != OnboardingStage.Landed || LandedAlpha < 1f)) return false;
        _handoffTaken = true;
        return true;
    }

    /// <summary>Avanza hasta <paramref name="now"/>.</summary>
    public void Step(TimeSpan now)
    {
        if (Stage is OnboardingStage.Hidden or OnboardingStage.Gone) return;
        double dt = (now - _last).TotalSeconds;
        _last = now;
        if (ReduceMotion)
        {
            StepFades(now);
            return;
        }

        if (_pending is { } p && now >= p.At)
        {
            _pending = null;
            var s = p.Width ? _w : _h;
            s.SetPreset(p.Preset);
            s.SetTarget(p.Target);
            if (p.Width) _widthOpening = p.Target > s.Value + 0.5 && Stage == OnboardingStage.Unfolding;
        }
        if (dt > 0)
        {
            _w.Advance(dt);
            _h.Advance(dt);
            _pill.Advance(dt);
            _shadow.Advance(dt);
        }
        if (_widthOpening && _w.IsSettled) _widthOpening = false;
        PillAlpha = (float)Math.Clamp(_pill.Value / AlphaUnits, 0, 1);

        switch (Stage)
        {
            case OnboardingStage.Dropping:
                if (_logoAt is null && Progress() >= RevealAtProgress) _logoAt = now;
                LogoAlpha = EaseOut(_logoAt, now, LogoIn);
                // El rebote acaba de asentarse durante la pausa: no hace falta esperarlo entero aquí.
                if ((Settled && LogoAlpha >= 1f) || now - _stageAt >= DropMax) Enter(OnboardingStage.Holding, now);
                break;
            case OnboardingStage.Holding:
                LogoAlpha = 1f;
                if (now - _stageAt >= Hold) BeginUnfold(now);
                break;
            case OnboardingStage.Unfolding:
                LogoAlpha = _logoFrom * (1f - EaseIn(now - _stageAt, LogoOut));
                if (_contentAt is null && Progress() >= RevealAtProgress) _contentAt = now;
                float k = EaseOut(_contentAt, now, ContentIn);
                ContentAlpha = k;
                ContentOffsetY = MathF.Round(ContentRise * (1f - k));
                if (Settled && k >= 1f && _shadow.IsSettled) Enter(OnboardingStage.Open, now);
                break;
            case OnboardingStage.Open:
                ContentAlpha = 1f;
                ContentOffsetY = 0f;
                break;
            case OnboardingStage.Collapsing:
                ContentAlpha = _contentFrom * (1f - EaseIn(now - _stageAt, ContentOut));
                ContentOffsetY = 0f;
                if (_final.IsEmpty)
                {
                    if (PillAlpha < NotchMotion.GoneAlpha && Settled) Enter(OnboardingStage.Gone, now);
                }
                else if (Settled && ContentAlpha <= 0f) Enter(OnboardingStage.Landed, now);
                break;
            case OnboardingStage.Landed:
                LandedAlpha = EaseOut(_stageAt, now, LandIn);
                if (now - _stageAt >= LandIn + HandoffHold) Enter(OnboardingStage.Gone, now);
                break;
        }
    }

    /// <summary>Reducir movimiento: la forma no se mueve; todo es el alfa de la ventana y el del contenido.</summary>
    private void StepFades(TimeSpan now)
    {
        float k = (float)Math.Clamp((now - _stageAt).TotalMilliseconds / Fade.TotalMilliseconds, 0, 1);
        switch (Stage)
        {
            case OnboardingStage.Unfolding:
                PillAlpha = k;
                ContentAlpha = 1f;
                if (k >= 1f) Enter(OnboardingStage.Open, now);
                break;
            case OnboardingStage.Open:
                PillAlpha = 1f;
                break;
            case OnboardingStage.Collapsing:
                if (!_swapped)
                {
                    PillAlpha = 1f - k;
                    if (k < 1f) break;
                    if (_final.IsEmpty)
                    {
                        Enter(OnboardingStage.Gone, now);
                        break;
                    }
                    // Sin verse: a la forma de reposo, con su contenido, y vuelta a fundirse.
                    _swapped = true;
                    _stageAt = now;
                    _w.Snap(Math.Round(_final.Width));
                    _h.Snap(Math.Round(_final.Height));
                    _shadow.Snap(0);
                    ContentAlpha = 0f;
                    LandedAlpha = 1f;
                    break;
                }
                PillAlpha = k;
                if (k >= 1f) Enter(OnboardingStage.Landed, now);
                break;
            case OnboardingStage.Landed:
                PillAlpha = 1f;
                LandedAlpha = 1f;
                if (now - _stageAt >= HandoffHold) Enter(OnboardingStage.Gone, now);
                break;
        }
    }

    private void BeginUnfold(TimeSpan now)
    {
        Enter(OnboardingStage.Unfolding, now);
        _logoFrom = LogoAlpha;
        _w.SetPreset(Unfold);
        _w.SetTarget(Math.Round(_wizard.Width));
        _widthOpening = true;
        _pending = (now + UnfoldStagger, Math.Round(_wizard.Height), Unfold, false);
        _shadow.SetTarget(AlphaUnits);
        _progressFrom = _h.Value;
        _progressTo = Math.Round(_wizard.Height);
        _contentAt = null;
    }

    private void Enter(OnboardingStage stage, TimeSpan now)
    {
        Stage = stage;
        _stageAt = now;
        if (stage == OnboardingStage.Gone) PillAlpha = _final.IsEmpty ? 0f : PillAlpha;
    }

    private bool Settled => _pending is null && _w.IsSettled && _h.IsSettled && _pill.IsSettled;

    /// <summary>p = 1 − |objetivo − alto| / |objetivo − alto₀| (1 si apenas cambia).</summary>
    private double Progress()
    {
        double span = Math.Abs(_progressTo - _progressFrom);
        if (span < 0.5) return 1;
        return 1 - Math.Abs(_progressTo - _h.Value) / span;
    }

    private static float EaseOut(TimeSpan? start, TimeSpan now, TimeSpan duration)
    {
        if (start is not { } at) return 0f;
        double k = Math.Clamp((now - at).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        return (float)(1 - (1 - k) * (1 - k) * (1 - k));
    }

    private static float EaseIn(TimeSpan elapsed, TimeSpan duration)
    {
        double k = Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        return (float)(k * k);
    }
}
