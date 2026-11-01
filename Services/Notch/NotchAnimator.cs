using System.Drawing;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Coreografía del notch (lenguaje de movimiento §3.0, §3.9). PURA: el instante llega por parámetro
/// (<see cref="TimeSpan"/> monotónico del <c>Stopwatch</c> de la ventana) y no hay timers dentro; la
/// ventana pregunta <see cref="IsAnimating"/> para saber si su bucle sigue vivo.
///
/// <list type="bullet">
/// <item><b>Forma:</b> un muelle por eje (px físicos, objetivos enteros). Cada eje que crece usa Abrir y
/// cada eje que encoge, Cerrar. Al crecer, el alto arranca 40 ms después del ancho; al encoger, la forma
/// espera 40 ms al contenido y luego va el alto (40 ms) antes que el ancho (80 ms). Sin desfase si el alto
/// cambia ≤ 6 px. Un cambio de tamaño dentro del mismo modo usa Ajuste y no toca el contenido.</item>
/// <item><b>Contenido:</b> al cambiar de modo (o de aviso) el viejo sale en 70 ms (ease-in); el nuevo
/// entra cuando el eje que más cambia lleva p 0,6 (0,7 al encoger), en 140 ms (ease-out) y, al crecer,
/// sube de 4 px a 0 en px enteros. <see cref="ShownMode"/>/<see cref="ShownAlert"/> son lo que se PINTA.</item>
/// <item><b>Alfas</b> (pastilla, sombra, halo) con el muelle Rápido, guardadas ×100 para que el encaje del
/// muelle (0,05) sea un 0,05 % invisible y no un salto del 5 %.</item>
/// <item><b>Squash</b> solo mientras el ancho abre (Abrir) con Δ ≥ 100 px; nunca al cerrar.</item>
/// <item><b>Reduce-motion:</b> todo salta al objetivo en el mismo fotograma.</item>
/// <item><b>Aparecer:</b> el primer objetivo (o tras <see cref="Reset"/>) parte de 44 × 0 con alfa 0.
/// <b>Ocultar</b> (<see cref="NotchMode.Hidden"/>): el contenido sale, la forma cierra hacia 44 × 0 y el alfa
/// baja a 0; <see cref="IsGone"/> avisa cuando ya no se ve.</item>
/// </list>
/// Fuera de F1 (llegan con F2/F3): acentos (impulso de atención, salto de la cara), Vista previa desde
/// Mínimo con toque, cruce simultáneo del texto dentro del mismo diseño.
/// </summary>
public sealed class NotchAnimator
{
    private enum Fade { None, Out, Wait, In, Gone }

    private const double AlphaUnits = 100.0;   // alfas ×100 (ver resumen)
    private const float MinimalWidth = 44f;    // ancho de partida al aparecer y de llegada al ocultar

    private readonly SpringValue _w = new(0, Motion.Motion.SpringOpen);
    private readonly SpringValue _h = new(0, Motion.Motion.SpringOpen);
    private readonly SpringValue _pill = new(0, Motion.Motion.SpringFast);
    private readonly SpringValue _shadow = new(0, Motion.Motion.SpringFast);
    private readonly SpringValue _halo = new(0, Motion.Motion.SpringFast);

    private bool _started;
    private TimeSpan _last;
    private float _scale = 1f;
    private bool _dimmed;

    private NotchMode _targetMode = NotchMode.Hidden;
    private NotchMode _targetContent = NotchMode.Hidden;   // modo cuyo contenido se pide (el de _targetMode si no se dice otro)
    private NotchAlert? _targetAlert;
    private int _targetLayout;

    // Arranques diferidos de cada eje (desfase ancho/alto y espera al encoger).
    private (TimeSpan At, double Target, SpringPreset Preset)? _wPending, _hPending;
    private bool _widthOpening;     // el ancho va con Abrir: compuerta del squash
    private float _deltaWidth;      // Δancho de la transición en curso

    // Progreso de la transición: eje que más cambia.
    private bool _progressOnWidth = true;
    private double _progressFrom, _progressTo;
    private bool _growing;

    private Fade _fade = Fade.None;
    private TimeSpan _fadeAt;
    private float _fadeFrom = 1f;

    /// <summary>Saltar en vez de animar (ajuste propio o el de Windows). Se aplica en el siguiente paso.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Modo cuyo contenido se está pintando (durante la salida del viejo, aún el viejo).</summary>
    public NotchMode ShownMode { get; private set; } = NotchMode.Hidden;

    /// <summary>Aviso cuyo contenido se está pintando.</summary>
    public NotchAlert? ShownAlert { get; private set; }

    /// <summary>Maquetación que se PINTA dentro de <see cref="ShownMode"/> (F5: p. ej. reposo con anillos = 0 o con la
    /// canción = 1). La define quien llama a <see cref="SetTarget"/>; aquí solo decide cuándo se cambia.</summary>
    public int ShownLayout { get; private set; }

    /// <summary>Caja de la pastilla en px físicos (lo que sale de los muelles).</summary>
    public float Width => (float)Math.Max(0, _w.Value);
    public float Height => (float)Math.Max(0, _h.Value);

    /// <summary>Alfa de toda la ventana [0, 1].</summary>
    public float PillAlpha => Alpha(_pill);

    public float ShadowAlpha => Alpha(_shadow);
    public float HaloAlpha => Alpha(_halo);

    /// <summary>Fundido del contenido [0, 1].</summary>
    public float ContentAlpha { get; private set; } = 1f;

    /// <summary>Subida del contenido que entra, en px enteros a escala 1 (4 → 0).</summary>
    public float ContentOffsetY { get; private set; }

    /// <summary>Squash-stretch de la forma [−1, 1]; 0 salvo mientras el ancho abre un cambio grande.</summary>
    public float Squash => ReduceMotion || !_widthOpening ? 0f : NotchMotion.Squash((float)_w.Velocity, _deltaWidth, _scale);

    /// <summary>¿Queda algo en movimiento (muelle sin asentar, arranque diferido o fundido)? Si no, el bucle
    /// de la ventana se para: quieto = 0 % de CPU.</summary>
    public bool IsAnimating => _started && (_wPending is not null || _hPending is not null
        || !_w.IsSettled || !_h.IsSettled || !_pill.IsSettled || !_shadow.IsSettled || !_halo.IsSettled
        || _fade is Fade.Out or Fade.Wait or Fade.In);

    /// <summary>
    /// La forma ya no se mueve bajo el cursor (sin arranques diferidos, ambos ejes a &lt; 1 px del objetivo y con
    /// |v| &lt; 40 px/s, escalados a la pantalla): condición de la spec de movimiento para armar los botones del
    /// Permiso, además de los 400 ms de la máquina. El clic se acierta sobre el rectángulo animado.
    /// </summary>
    public bool ShapeSettled
    {
        get
        {
            if (_wPending is not null || _hPending is not null) return false;
            double d = NotchMotion.ArmSettleDistance * _scale, v = NotchMotion.ArmSettleVelocity * _scale;
            return Math.Abs(_w.Value - _w.Target) < d && Math.Abs(_h.Value - _h.Target) < d
                && Math.Abs(_w.Velocity) < v && Math.Abs(_h.Velocity) < v;
        }
    }

    /// <summary>El objetivo es ocultarse y ya no se ve (la ventana puede hacer <c>Hide</c>).</summary>
    public bool IsGone => _targetMode == NotchMode.Hidden && (!_started || PillAlpha < NotchMotion.GoneAlpha);

    /// <summary>¿Se pinta el spinner? (contenido de Trabajando/Vista previa con alguna sesión activa y sin reduce-motion).</summary>
    public bool SpinnerVisible(bool anyActive) => SpinnerVisible(anyActive, sessionsOnScreen: false);

    /// <summary>Igual, y además en el expandido mientras se ve la lista de sesiones (<paramref name="sessionsOnScreen"/>):
    /// sus filas activas llevan el mismo spinner, que se congelaba al asentarse la apertura porque el bucle se paraba.</summary>
    public bool SpinnerVisible(bool anyActive, bool sessionsOnScreen) =>
        anyActive && !ReduceMotion && _fade != Fade.Gone
        && (ShownMode is NotchMode.Working or NotchMode.Peek || (sessionsOnScreen && ShownMode == NotchMode.Expanded));

    /// <summary>
    /// Nuevo objetivo. <paramref name="size"/> en px físicos (ya × <paramref name="scale"/>), como lo da
    /// <see cref="NotchLayout.TargetSize"/>. Cambio de modo o de aviso ⇒ transición con crossfade; mismo
    /// modo y aviso ⇒ solo se reajusta el tamaño. <paramref name="layout"/>: otra maquetación dentro del mismo modo
    /// (F5) también es un cambio de contenido (el viejo sale, el nuevo entra a p 0,6; lenguaje de movimiento §3.0).
    /// <paramref name="content"/>: el contenido que se pinta es el de ese modo y no el de <paramref name="mode"/> (la letra
    /// compacta sigue en la Vista previa: <see cref="ShownMode"/> se queda en Reposo y no hay cruce). La forma, la sombra
    /// y el halo siguen a <paramref name="mode"/>.
    /// </summary>
    public void SetTarget(NotchMode mode, NotchAlert? alert, SizeF size, float scale, TimeSpan now, int layout = 0,
        NotchMode? content = null)
    {
        _scale = scale > 0 ? scale : 1f;
        var shownAs = mode == NotchMode.Hidden ? mode : content ?? mode;
        if (!IsAnimating) _last = now;   // tras un reposo, el primer paso no debe integrar todo ese tiempo

        double tw, th;
        if (mode == NotchMode.Hidden) { tw = Math.Round(MinimalWidth * _scale); th = 0; }
        else { tw = Math.Round(Math.Max(0f, size.Width)); th = Math.Round(Math.Max(0f, size.Height)); }

        if (!_started)
        {
            // Aparecer: desde 44 × 0, transparente y sin contenido hasta que la forma abra.
            _started = true;
            _w.Snap(Math.Round(MinimalWidth * _scale));
            _h.Snap(0);
            _pill.Snap(0);
            _shadow.Snap(0);
            _halo.Snap(0);
            _wPending = _hPending = null;
            _fade = Fade.Wait;
            ContentAlpha = 0f;
            ContentOffsetY = 0f;
            ShownMode = shownAs;
            ShownAlert = alert;
            ShownLayout = layout;
            _targetMode = _targetContent = NotchMode.Hidden;   // fuerza el cambio de contenido de abajo
        }

        bool contentChanged = shownAs != _targetContent || !SameAlert(alert, _targetAlert) || layout != _targetLayout;
        _targetMode = mode;
        _targetContent = shownAs;
        _targetAlert = alert;
        _targetLayout = layout;
        RetargetAlphas();

        if (ReduceMotion)
        {
            SnapToTargets(tw, th);
            return;
        }

        if (!contentChanged)
        {
            // Mismo contenido y aviso: el aviso puede traer datos nuevos (mismo id) y el tamaño se reajusta.
            if (ShownMode == shownAs) ShownAlert = alert;
            Adjust(_w, ref _wPending, tw);
            Adjust(_h, ref _hPending, th);
            return;
        }

        // ---- transición de modo: forma desfasada + crossfade de contenido
        double dw = tw - _w.Value, dh = th - _h.Value;
        _growing = tw * th >= _w.Value * _h.Value;
        bool stagger = Math.Abs(dh) > NotchMotion.AxisStaggerMinDeltaHeight * _scale;
        var d = NotchMotion.AxisStagger;
        var wDelay = _growing ? TimeSpan.Zero : (stagger ? d + d : d);
        var hDelay = _growing ? (stagger ? d : TimeSpan.Zero) : d;

        var wPreset = dw > 0.5 ? Motion.Motion.SpringOpen : Motion.Motion.SpringClose;
        var hPreset = dh > 0.5 ? Motion.Motion.SpringOpen : Motion.Motion.SpringClose;
        _wPending = (now + wDelay, tw, wPreset);
        _hPending = (now + hDelay, th, hPreset);
        _deltaWidth = (float)dw;
        _widthOpening = false;      // se arma cuando arranque el ancho

        _progressOnWidth = Math.Abs(dw) >= Math.Abs(dh);
        _progressFrom = _progressOnWidth ? _w.Value : _h.Value;
        _progressTo = _progressOnWidth ? tw : th;

        switch (_fade)
        {
            case Fade.Wait:
            case Fade.Gone:
                // No se ve contenido: el nuevo pasa a ser el pintado y espera a la forma.
                if (mode != NotchMode.Hidden) { ShownMode = shownAs; ShownAlert = alert; ShownLayout = layout; _fade = Fade.Wait; }
                break;
            default:
                _fade = Fade.Out;
                _fadeAt = now;
                _fadeFrom = ContentAlpha;
                ContentOffsetY = 0f;
                break;
        }
        ApplyPending(now);
    }

    /// <summary>Atenúa toda la pastilla mientras se arrastra una ventana (Snap Layouts).</summary>
    public void SetDimmed(bool dimmed, TimeSpan now)
    {
        if (_dimmed == dimmed) return;
        if (!IsAnimating) _last = now;
        _dimmed = dimmed;
        RetargetAlphas();
    }

    /// <summary>Olvida el estado: el siguiente objetivo vuelve a aparecer desde 44 × 0 (tras ocultarse de
    /// golpe por pantalla completa o al cambiar de monitor).</summary>
    public void Reset()
    {
        _started = false;
        _wPending = _hPending = null;
        _widthOpening = false;
        _fade = Fade.None;
        _targetMode = _targetContent = NotchMode.Hidden;
        _targetAlert = null;
        _targetLayout = 0;
        ShownMode = NotchMode.Hidden;
        ShownAlert = null;
        ShownLayout = 0;
        ContentAlpha = 1f;
        ContentOffsetY = 0f;
    }

    /// <summary>Avanza hasta <paramref name="now"/>.</summary>
    public void Step(TimeSpan now)
    {
        if (!_started) return;
        if (ReduceMotion)
        {
            SnapToTargets(_wPending?.Target ?? _w.Target, _hPending?.Target ?? _h.Target);
            _last = now;
            return;
        }

        double dt = (now - _last).TotalSeconds;
        _last = now;
        ApplyPending(now);
        if (dt > 0)
        {
            _w.Advance(dt);
            _h.Advance(dt);
            _pill.Advance(dt);
            _shadow.Advance(dt);
            _halo.Advance(dt);
        }
        if (_widthOpening && _w.IsSettled) _widthOpening = false;
        UpdateFade(now);
    }

    // ---------------------------------------------------------------- interno

    private void ApplyPending(TimeSpan now)
    {
        if (_wPending is { } w && now >= w.At)
        {
            _wPending = null;
            _w.SetPreset(w.Preset);
            _w.SetTarget(w.Target);
            _widthOpening = w.Preset == Motion.Motion.SpringOpen;
        }
        if (_hPending is { } h && now >= h.At)
        {
            _hPending = null;
            _h.SetPreset(h.Preset);
            _h.SetTarget(h.Target);
        }
    }

    /// <summary>Reajuste dentro del mismo modo: si el eje está en plena transición solo cambia el destino
    /// (conserva su preset); si estaba quieto, Ajuste.</summary>
    private static void Adjust(SpringValue s, ref (TimeSpan At, double Target, SpringPreset Preset)? pending,
        double target)
    {
        if (pending is { } p) { pending = (p.At, target, p.Preset); return; }
        if (s.IsSettled) s.SetPreset(Motion.Motion.SpringAdjust);
        s.SetTarget(target);
    }

    private void UpdateFade(TimeSpan now)
    {
        switch (_fade)
        {
            case Fade.Out:
            {
                double k = (now - _fadeAt).TotalMilliseconds / NotchMotion.CrossfadeOut.TotalMilliseconds;
                if (k < 1)
                {
                    ContentAlpha = _fadeFrom * (float)(1 - k * k);   // ease-in
                    return;
                }
                ContentAlpha = 0f;
                if (_targetMode == NotchMode.Hidden) { _fade = Fade.Gone; return; }
                ShownMode = _targetContent;
                ShownAlert = _targetAlert;
                ShownLayout = _targetLayout;
                _fade = Fade.Wait;
                goto case Fade.Wait;
            }
            case Fade.Wait:
            {
                ContentAlpha = 0f;
                float threshold = _growing ? NotchMotion.CrossfadeInAtProgress : NotchMotion.CollapseInAtProgress;
                bool shapeIdle = _wPending is null && _hPending is null && _w.IsSettled && _h.IsSettled;
                if (Progress() < threshold && !shapeIdle) return;
                _fade = Fade.In;
                _fadeAt = now;
                goto case Fade.In;
            }
            case Fade.In:
            {
                double k = (now - _fadeAt).TotalMilliseconds / NotchMotion.CrossfadeIn.TotalMilliseconds;
                if (k >= 1)
                {
                    _fade = Fade.None;
                    ContentAlpha = 1f;
                    ContentOffsetY = 0f;
                    return;
                }
                double e = 1 - (1 - k) * (1 - k) * (1 - k);       // ease-out
                ContentAlpha = (float)e;
                ContentOffsetY = _growing ? MathF.Round(NotchMotion.ContentRise * (float)(1 - e)) : 0f;
                return;
            }
        }
    }

    /// <summary>p = 1 − |objetivo − x| / |objetivo − x₀| en el eje que más cambia (1 si apenas cambia).</summary>
    private double Progress()
    {
        double span = Math.Abs(_progressTo - _progressFrom);
        if (span < 0.5) return 1;
        double x = _progressOnWidth ? _w.Value : _h.Value;
        return 1 - Math.Abs(_progressTo - x) / span;
    }

    private void RetargetAlphas()
    {
        bool hidden = _targetMode == NotchMode.Hidden;
        _pill.SetTarget(hidden ? 0 : (_dimmed ? NotchMotion.SnapDimAlpha : 1f) * AlphaUnits);
        _shadow.SetTarget(hidden ? 0 : NotchMotion.ShadowOpacity(_targetMode) * AlphaUnits);
        _halo.SetTarget(!hidden && _targetMode == NotchMode.QuotaAlert ? AlphaUnits : 0);
    }

    private void SnapToTargets(double tw, double th)
    {
        _wPending = _hPending = null;
        _widthOpening = false;
        _w.Snap(tw);
        _h.Snap(th);
        _pill.Snap(_pill.Target);
        _shadow.Snap(_shadow.Target);
        _halo.Snap(_halo.Target);
        if (_targetMode == NotchMode.Hidden)
        {
            _fade = Fade.Gone;
            ContentAlpha = 0f;
        }
        else
        {
            ShownMode = _targetContent;
            ShownAlert = _targetAlert;
            ShownLayout = _targetLayout;
            _fade = Fade.None;
            ContentAlpha = 1f;
        }
        ContentOffsetY = 0f;
    }

    private static float Alpha(SpringValue s) => (float)Math.Clamp(s.Value / AlphaUnits, 0, 1);

    private static bool SameAlert(NotchAlert? a, NotchAlert? b) =>
        a is null ? b is null : b is not null && a.Kind == b.Kind && a.SessionId == b.SessionId;
}
