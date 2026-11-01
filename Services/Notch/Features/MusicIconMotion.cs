using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Animación de los iconos de la página Música, como RESPUESTA a una acción (nunca en bucle):
/// <list type="bullet">
/// <item><b>Pausa ↔ reproducir</b>: las dos barras de pausa se transforman en el triángulo de reproducir (y al revés)
/// cuando cambia <c>Playing</c>. <see cref="Morph"/> va de 0 (barras) a 1 (triángulo) con el muelle Ajuste en unidades
/// 0..1 (sin pasarse, 95 % en ~280 ms); un cambio a media transición re-apunta el muelle conservando su velocidad, así
/// que se invierte sin saltos.</item>
/// <item><b>Siguiente / anterior</b>: el triángulo avanza hacia la barra y se recorta en ella (<see cref="SkipOut"/>),
/// queda solo la barra (<see cref="SkipGap"/>) y reaparece por el lado contrario casi entero, de golpe, para asentarse
/// en su sitio (<see cref="SkipIn"/>, desde −<see cref="SkipEntry"/>); en total <see cref="SkipDuration"/>. El
/// desplazamiento se publica normalizado (<see cref="NextOffset"/>/<see cref="PreviousOffset"/>): 0 = en su sitio,
/// +1 = metido del todo en la barra, −1 = fuera del icono por el lado de entrada. La geometría (cuántos px es «del
/// todo») la pone el painter.</item>
/// </list>
///
/// <para>Tras un salto, algunos reproductores avisan un instante de <c>Playing = false</c> mientras cambian de pista.
/// Durante <see cref="SkipHold"/> el botón central no empieza a cambiar de forma: si el estado vuelve antes, no se ve
/// nada; si sigue cambiado, el morph arranca al acabar la espera.</para>
///
/// <para>PURO: el instante llega por parámetro y no hay timers. Quieto ⇒ <see cref="IsAnimating"/> = false, así la
/// ventana para su bucle (0 % CPU en reposo). Con reduce-motion todo cambia en el mismo fotograma.</para>
/// </summary>
public sealed class MusicIconMotion
{
    /// <summary>Tramo de salida: el triángulo acelera hacia la barra (ease-in) hasta quedar recortado entero.</summary>
    public static readonly TimeSpan SkipOut = TimeSpan.FromMilliseconds(190);

    /// <summary>Tramo con solo la barra a la vista.</summary>
    public static readonly TimeSpan SkipGap = TimeSpan.FromMilliseconds(50);

    /// <summary>Tramo de entrada: el triángulo entra por el borde contrario y frena hasta su sitio (ease-out).</summary>
    public static readonly TimeSpan SkipIn = TimeSpan.FromMilliseconds(210);

    /// <summary>Duración total del deslizamiento de siguiente/anterior.</summary>
    public static readonly TimeSpan SkipDuration = SkipOut + SkipGap + SkipIn;

    /// <summary>Desde dónde reaparece el triángulo (fracción fuera del icono): poco, así se ve casi entero al volver.</summary>
    public const float SkipEntry = 0.3f;

    /// <summary>Espera tras un salto antes de que un cambio de <c>Playing</c> mueva el botón central.</summary>
    public static readonly TimeSpan SkipHold = TimeSpan.FromMilliseconds(700);

    // Ajuste (300/35, ζ ≈ 1) con los umbrales de opacidad/mezcla (§2.1): el morph es una mezcla 0..1.
    private static readonly SpringPreset MorphPreset = Motion.Motion.Ajuste with { EpsX = 0.002, EpsV = 0.02 };

    private readonly SpringValue _morph;
    private readonly SkipSlide _next = new(), _previous = new();
    private TimeSpan _last;
    private TimeSpan _holdUntil;   // hasta cuándo se aplaza el morph tras un salto
    private bool _pending;         // hay un Playing nuevo esperando a que acabe la espera

    public MusicIconMotion(bool playing)
    {
        Playing = playing;
        _morph = new SpringValue(Target(playing), MorphPreset);
    }

    /// <summary>Cambiar en vez de animar. Se aplica en la siguiente llamada.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Estado al que va el botón central (el último que se pidió).</summary>
    public bool Playing { get; private set; }

    /// <summary>Forma del botón central: 0 = dos barras (sonando: «pausa»), 1 = triángulo (en pausa: «reproducir»).</summary>
    public float Morph => (float)Math.Clamp(_morph.Value, 0, 1);

    /// <summary>¿El botón central está a medio camino entre las dos formas?</summary>
    public bool IsMorphing => !_morph.IsSettled;

    /// <summary>Desplazamiento normalizado del triángulo de «siguiente» (ver la clase).</summary>
    public float NextOffset => _next.Offset;

    /// <summary>Desplazamiento normalizado del triángulo de «anterior» (ver la clase).</summary>
    public float PreviousOffset => _previous.Offset;

    /// <summary>¿Queda algún icono en movimiento?</summary>
    public bool IsAnimating => IsMorphing || _pending || _next.Active || _previous.Active;

    /// <summary>Cambió <c>Playing</c> (al pulsar o desde el reproductor): morph hacia la forma nueva.</summary>
    public void SetPlaying(bool playing, TimeSpan now)
    {
        if (playing == Playing) return;
        if (!IsAnimating) _last = now;   // tras un reposo, el primer paso no integra todo ese tiempo
        Playing = playing;
        if (ReduceMotion) { _pending = false; _morph.Snap(Target(playing)); return; }
        if (now < _holdUntil) { _pending = true; return; }   // justo tras un salto: puede ser un parpadeo del reproductor
        _morph.SetTarget(Target(playing));
    }

    /// <summary>Coloca la forma de <paramref name="playing"/> sin animación (página no visible).</summary>
    public void JumpPlaying(bool playing)
    {
        Playing = playing;
        _pending = false;
        _morph.Snap(Target(playing));
    }

    /// <summary>
    /// Pulsación de siguiente (<paramref name="next"/>) o anterior. Repetida a media animación la reinicia desde donde
    /// esté el triángulo (sin saltos); si en ese momento solo se ve la barra, sigue su curso.
    /// </summary>
    public void Skip(bool next, TimeSpan now)
    {
        if (ReduceMotion) return;
        if (!IsAnimating) _last = now;
        _holdUntil = now + SkipHold;
        (next ? _next : _previous).Start(now);
    }

    /// <summary>Avanza hasta <paramref name="now"/>.</summary>
    public void Step(TimeSpan now)
    {
        double dt = (now - _last).TotalSeconds;
        _last = now;
        if (ReduceMotion)
        {
            _pending = false;
            _morph.Snap(Target(Playing));
            _next.Stop();
            _previous.Stop();
            return;
        }
        if (_pending && now >= _holdUntil)
        {
            _pending = false;
            _morph.SetTarget(Target(Playing));   // si volvió al estado de antes, el muelle ya está ahí y no se mueve
        }
        if (dt > 0) _morph.Advance(dt);
        _next.Step(now);
        _previous.Step(now);
    }

    private static double Target(bool playing) => playing ? 0 : 1;

    /// <summary>Un deslizamiento de siguiente o anterior: salida desde <see cref="_from"/> hasta +1, hueco y entrada
    /// desde −<see cref="SkipEntry"/> hasta 0.</summary>
    private sealed class SkipSlide
    {
        private TimeSpan _at;
        private float _from;

        public bool Active { get; private set; }
        public float Offset { get; private set; }

        public void Start(TimeSpan now)
        {
            // En pleno hueco el triángulo no se ve: reiniciar solo alargaría la espera.
            if (Active && Offset >= 1f) return;
            _from = Active ? Offset : 0f;
            _at = now;
            Active = true;
        }

        public void Stop()
        {
            Active = false;
            Offset = 0f;
        }

        public void Step(TimeSpan now)
        {
            if (!Active) return;
            double t = (now - _at).TotalMilliseconds;
            double outMs = SkipOut.TotalMilliseconds, gapEnd = outMs + SkipGap.TotalMilliseconds;
            if (t < outMs)
            {
                double k = Math.Max(0, t) / outMs;
                Offset = (float)(_from + (1 - _from) * k * k);
            }
            else if (t < gapEnd) Offset = 1f;
            else if (t < SkipDuration.TotalMilliseconds)
            {
                double k = 1 - (t - gapEnd) / SkipIn.TotalMilliseconds;
                Offset = (float)(-SkipEntry * k * k * k);
            }
            else Stop();
        }
    }
}
