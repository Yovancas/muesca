using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Fase del gato del notch.</summary>
public enum MascotState { Hidden, Awake, LookAt, Sleepy, Asleep, Waking }

/// <summary>
/// Director del gato del notch. PURO: el instante llega por parámetro, nada lee el reloj ni usa
/// <c>Random</c>; el parpadeo tiene jitter determinista por hash del instante.
///
/// <list type="bullet">
/// <item><b>Aparece</b> tras <see cref="AppearAfter"/> (15 s) de reposo. Dónde se dibuja (en la pastilla o
/// sobre la raya si el encogido automático está activo) lo decide la UI.</item>
/// <item><b>Mira al cursor</b> (<see cref="MascotState.LookAt"/>) solo cuando está sobre el notch:
/// <c>gaze = (clamp(dx/60, ±3,5), clamp(dy/80, −1..2))</c>, con el cursor relativo al centro del notch.</item>
/// <item><b>Se duerme</b> <see cref="SleepAfter"/> (60 s) después de aparecer: <see cref="MascotState.Sleepy"/>
/// durante <see cref="SleepyFor"/> y luego <see cref="MascotState.Asleep"/> (sin despertares).</item>
/// <item><b>Cualquier actividad</b> (<c>since = null</c> o un «desde» distinto, es decir, un reposo nuevo)
/// lo esconde; si estaba dormido o adormilado, antes pasa <see cref="WakingFor"/> en
/// <see cref="MascotState.Waking"/>.</item>
/// <item><b>Parpadeo</b> solo despierto: cada 2,6–5 s (de inicio a inicio), <see cref="BlinkFor"/> de duración.</item>
/// </list>
/// Los cambios por tiempo se aplican en <see cref="Update"/> (también lo llaman <see cref="OnIdleSince"/> y
/// <see cref="OnPointer"/>); el llamador se despierta con <see cref="NextWakeIn"/>.
/// </summary>
public sealed class NotchMascot
{
    public static readonly TimeSpan AppearAfter = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SleepAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SleepyFor = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan WakingFor = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan BlinkFor = TimeSpan.FromMilliseconds(130);
    public static readonly TimeSpan BlinkMin = TimeSpan.FromMilliseconds(2600);
    public static readonly TimeSpan BlinkMax = TimeSpan.FromMilliseconds(5000);

    public const float GazeMaxX = 3.5f;
    public const float GazeMinY = -1f;
    public const float GazeMaxY = 2f;
    private const float GazeDivX = 60f;
    private const float GazeDivY = 80f;

    private DateTime? _idleSince;
    private PointF? _pointer;
    private DateTime _wakingUntil;
    private DateTime? _blinkStart;     // parpadeo en curso o el siguiente

    public MascotState State { get; private set; } = MascotState.Hidden;

    /// <summary>Desplazamiento de los ojos (−3,5..3,5, −1..2); (0, 0) si no mira al cursor.</summary>
    public PointF Gaze { get; private set; }

    /// <summary>Ojos cerrados por el parpadeo (solo en <see cref="MascotState.Awake"/>/<see cref="MascotState.LookAt"/>).</summary>
    public bool Blinking { get; private set; }

    /// <summary>
    /// Reposo desde <paramref name="since"/> (<c>null</c> = hay actividad). Un <paramref name="since"/> distinto
    /// del anterior también cuenta como actividad: hubo algo entre medias.
    /// </summary>
    public void OnIdleSince(DateTime? since, DateTime now)
    {
        bool activity = since is null || (_idleSince is { } prev && since != prev);
        if (activity)
        {
            if (State is MascotState.Sleepy or MascotState.Asleep)
            {
                State = MascotState.Waking;
                _wakingUntil = now + WakingFor;
            }
            else if (State is MascotState.Awake or MascotState.LookAt)
            {
                State = MascotState.Hidden;
            }
            _blinkStart = null;
        }
        _idleSince = since;
        Update(now);
    }

    /// <summary>Cursor relativo al centro del notch, o <c>null</c> si no está encima.</summary>
    public void OnPointer(PointF? cursorRelative, DateTime now)
    {
        _pointer = cursorRelative;
        Update(now);
    }

    /// <summary>Aplica los cambios por tiempo (aparecer, dormirse, fin del despertar, parpadeo).</summary>
    public void Update(DateTime now)
    {
        if (State == MascotState.Waking)
        {
            if (now < _wakingUntil)
            {
                SetLook(awake: false);
                return;
            }
            State = MascotState.Hidden;
        }

        if (_idleSince is not { } since)
        {
            State = MascotState.Hidden;
            _blinkStart = null;
            SetLook(awake: false);
            return;
        }

        var appearAt = since + AppearAfter;
        var sleepyAt = appearAt + SleepAfter;
        if (now < appearAt)
        {
            State = MascotState.Hidden;
            _blinkStart = null;
            SetLook(awake: false);
        }
        else if (now < sleepyAt)
        {
            State = _pointer is null ? MascotState.Awake : MascotState.LookAt;
            SetLook(awake: true);
            Blinking = AdvanceBlink(appearAt, now);
        }
        else
        {
            State = now < sleepyAt + SleepyFor ? MascotState.Sleepy : MascotState.Asleep;
            _blinkStart = null;
            SetLook(awake: false);
        }
    }

    /// <summary>Hasta el siguiente cambio que hay que dibujar; <c>null</c> si no hay ninguno pendiente
    /// (escondido sin reposo o dormido: 0 % CPU).</summary>
    public TimeSpan? NextWakeIn(DateTime now)
    {
        TimeSpan? until(DateTime at) => at > now ? at - now : TimeSpan.Zero;

        switch (State)
        {
            case MascotState.Waking:
                return until(_wakingUntil);
            case MascotState.Asleep:
                return null;
        }
        if (_idleSince is not { } since) return null;
        var appearAt = since + AppearAfter;
        var sleepyAt = appearAt + SleepAfter;
        switch (State)
        {
            case MascotState.Hidden:
                return now < appearAt ? until(appearAt) : null;
            case MascotState.Sleepy:
                return until(sleepyAt + SleepyFor);
            default: // Awake / LookAt
                var next = sleepyAt;
                if (_blinkStart is { } b)
                {
                    var edge = now < b ? b : b + BlinkFor;
                    if (edge > now && edge < next) next = edge;
                }
                return until(next);
        }
    }

    private void SetLook(bool awake)
    {
        Blinking = false;
        if (!awake || _pointer is not { } p)
        {
            Gaze = PointF.Empty;
            return;
        }
        Gaze = new PointF(
            Math.Clamp(p.X / GazeDivX, -GazeMaxX, GazeMaxX),
            Math.Clamp(p.Y / GazeDivY, GazeMinY, GazeMaxY));
    }

    // Avanza el calendario de parpadeos hasta now y dice si los ojos están cerrados. Acotado: solo se
    // llama despierto (≤ 60 s / 2,6 s ≈ 24 vueltas aunque Update llegue tarde).
    private bool AdvanceBlink(DateTime appearAt, DateTime now)
    {
        _blinkStart ??= appearAt + BlinkInterval(appearAt);
        while (now >= _blinkStart.Value + BlinkFor)
            _blinkStart = _blinkStart.Value + BlinkInterval(_blinkStart.Value);
        return now >= _blinkStart.Value;
    }

    // 2,6–5 s según un hash (splitmix64) del instante: determinista y sin Random.
    private static TimeSpan BlinkInterval(DateTime at)
    {
        ulong z = unchecked((ulong)at.Ticks + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        z ^= z >> 31;
        long span = (BlinkMax - BlinkMin).Ticks / TimeSpan.TicksPerMillisecond + 1;
        return BlinkMin + TimeSpan.FromMilliseconds((long)(z % (ulong)span));
    }
}
