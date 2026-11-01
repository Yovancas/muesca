using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services.Mascot;

/// <summary>Humor expresivo de la mascota (registro juguetón).</summary>
public enum Mood
{
    Neutral,
    Focused,   // procesado largo: concentrada
    Alert,     // pide atención (OK / input): ¡eh!
    Happy,     // celebración (reset de cuota)
}

/// <summary>Evento que empuja el humor. <see cref="None"/> = solo paso de tiempo.</summary>
public enum MoodEvent
{
    None,
    LongProcessing,
    AttentionRequired,
    ResetCelebrated,
}

/// <summary>
/// Máquina de <b>humor</b> de la mascota: PURA, con <b>histéresis</b> (dwell mínimo antes de
/// cambiar de humor) y <b>decay</b> (vuelve a <see cref="Mood.Neutral"/> tras
/// <see cref="DecayMs"/> sin eventos). Elapsed-driven (regla de oro F3): <see cref="Update"/>
/// recibe el delta de tiempo por parámetro — jamás lee el reloj ni usa aleatoriedad, así el
/// resultado es determinista y testeable sin reloj/GDI+.
///
/// <para>La histéresis evita el parpadeo de humor cuando la fase cambia rápido: un humor recién
/// adoptado no cede a otro hasta cumplir <see cref="DwellMs"/>, salvo que el evento entrante sea
/// de <b>mayor prioridad</b> (atención &gt; contento/concentrado).</para>
/// </summary>
public sealed class MascotMood
{
    /// <summary>Dwell mínimo (ms) antes de que un humor ceda a otro de prioridad igual o menor.</summary>
    public const double DwellMs = 700.0;

    /// <summary>Tiempo (ms) sin eventos tras el cual el humor decae a <see cref="Mood.Neutral"/>.</summary>
    public const double DecayMs = 4000.0;

    private Mood _current = Mood.Neutral;
    private double _inMoodMs;     // tiempo acumulado en el humor actual (para el dwell)
    private double _sinceEventMs; // tiempo desde el último evento no-None (para el decay)

    public Mood Current => _current;

    /// <summary>
    /// Avanza la máquina <paramref name="deltaMs"/> ms con la fase y el evento actuales.
    /// El <paramref name="phase"/> se acepta para futuros matices, pero el humor lo dirige el
    /// <paramref name="ev"/> (la fase ya colorea/forma la mascota aparte).
    /// </summary>
    public void Update(SessionPhase phase, MoodEvent ev, double deltaMs)
    {
        if (deltaMs < 0) deltaMs = 0;
        _inMoodMs += deltaMs;

        if (ev != MoodEvent.None)
        {
            _sinceEventMs = 0;
            var desired = MoodFor(ev);
            // Histéresis: solo cambia si el humor actual ya cumplió su dwell, o si el nuevo humor
            // tiene mayor prioridad (las urgencias rompen el dwell).
            if (desired != _current &&
                (_inMoodMs >= DwellMs || Priority(desired) > Priority(_current)))
            {
                _current = desired;
                _inMoodMs = 0;
            }
        }
        else
        {
            _sinceEventMs += deltaMs;
            // Decay: pasado el silencio, vuelve a neutro (respetando el dwell del humor actual).
            if (_current != Mood.Neutral && _sinceEventMs >= DecayMs && _inMoodMs >= DwellMs)
            {
                _current = Mood.Neutral;
                _inMoodMs = 0;
            }
        }
    }

    private static Mood MoodFor(MoodEvent ev) => ev switch
    {
        MoodEvent.AttentionRequired => Mood.Alert,
        MoodEvent.ResetCelebrated => Mood.Happy,
        MoodEvent.LongProcessing => Mood.Focused,
        _ => Mood.Neutral,
    };

    // Mayor = más urgente; la atención rompe el dwell de contento/concentrado.
    private static int Priority(Mood m) => m switch
    {
        Mood.Alert => 3,
        Mood.Happy => 2,
        Mood.Focused => 1,
        _ => 0,
    };
}
