using ClaudeBarWin.Models;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Services.Mascot;

/// <summary>
/// Identidad única del gato (lenguaje de movimiento §5), fuera de <c>DashboardForm</c>: guarda la fase y
/// su reloj, el humor (<see cref="MascotMood"/>: 700 ms de inercia, neutro tras 4 s), la semilla del
/// parpadeo y la hora del último Salto. Pensado para que panel y notch lo compartan y parpadeen y salten
/// en el mismo fotograma; hoy solo lo usa el panel.
///
/// <para>PURO y elapsed-driven: cada llamada recibe la hora (ms de un reloj monótono). Los saltos de
/// atención siguen <see cref="Motion.Motion.AttentionHopScheduleMs"/> desde que empieza la atención; al
/// terminar, el bote en curso acaba solo y no se programan más. Con reduce-motion no hay saltos.</para>
/// </summary>
public sealed class MascotDirector
{
    private readonly MascotMood _mood = new();
    private double _lastSyncMs = double.NaN;
    private double _attentionStartMs = double.NegativeInfinity; // −∞ = sin atención en curso
    private double _lastHopStartMs = double.NegativeInfinity;    // último salto ya lanzado (−∞ = ninguno)

    public MascotDirector(int seed = 0x5EED) => Seed = seed;

    /// <summary>Semilla determinista del parpadeo y del verbo.</summary>
    public int Seed { get; }

    /// <summary>Fase global vigente.</summary>
    public SessionPhase Phase { get; private set; } = SessionPhase.Idle;

    /// <summary>Hora (ms) a la que empezó la fase vigente.</summary>
    public double PhaseStartMs { get; private set; }

    /// <summary>Humor vigente.</summary>
    public Mood Mood => _mood.Current;

    /// <summary>ms en la fase vigente.</summary>
    public double PhaseElapsedMs(double nowMs) => nowMs - PhaseStartMs;

    /// <summary>
    /// Sincroniza fase, humor y calendario de saltos. <paramref name="wantsHops"/> = algo pide atención
    /// (fase WaitingFor* o celebración de la cuota). Idempotente dentro del mismo instante.
    /// </summary>
    public void Sync(SessionPhase phase, MoodEvent ev, double nowMs, bool wantsHops, bool reduceMotion = false)
    {
        if (phase != Phase)
        {
            Phase = phase;
            PhaseStartMs = nowMs;
        }
        double delta = double.IsNaN(_lastSyncMs) ? 0 : Math.Max(0, nowMs - _lastSyncMs);
        _lastSyncMs = nowMs;
        _mood.Update(phase, ev, delta);

        if (reduceMotion)
        {
            _attentionStartMs = double.NegativeInfinity;
            _lastHopStartMs = double.NegativeInfinity;
            return;
        }
        if (wantsHops)
        {
            if (double.IsNegativeInfinity(_attentionStartMs)) _attentionStartMs = nowMs;
        }
        else if (!double.IsNegativeInfinity(_attentionStartMs))
        {
            // La atención termina: el bote en curso acaba solo, pero no se programan más.
            _lastHopStartMs = LastHopStartMs(nowMs);
            _attentionStartMs = double.NegativeInfinity;
        }
    }

    /// <summary>Hora (ms) de inicio del último salto lanzado hasta <paramref name="nowMs"/> (−∞ si ninguno).</summary>
    public double LastHopStartMs(double nowMs)
    {
        if (double.IsNegativeInfinity(_attentionStartMs)) return _lastHopStartMs;
        return Hop.LatestScheduledMs(nowMs - _attentionStartMs) is { } at ? _attentionStartMs + at : _lastHopStartMs;
    }

    /// <summary>Hora (ms) del siguiente salto programado, o null si no queda ninguno.</summary>
    public double? NextHopAtMs(double nowMs)
    {
        if (double.IsNegativeInfinity(_attentionStartMs)) return null;
        return Hop.NextScheduledMs(nowMs - _attentionStartMs) is { } at ? _attentionStartMs + at : null;
    }

    private double SinceHop(double nowMs) => nowMs - LastHopStartMs(nowMs);

    /// <summary>¿Pide fotogramas el Salto (botes + última cara de aterrizaje)?</summary>
    public bool HopActive(double nowMs)
    {
        double t = SinceHop(nowMs);
        return t >= 0 && t < Hop.ActiveMs(Motion.Motion.HopPanelV0);
    }

    /// <summary>Desplazamiento hacia arriba (px enteros ≥ 0) del Salto del panel a la escala de DPI dada.</summary>
    public int HopOffsetPx(double nowMs, double scale) => Hop.OffsetPx(SinceHop(nowMs), Motion.Motion.HopPanelV0, scale);

    /// <summary>¿Acaba de tocar suelo? (cara alternativa 90 ms, micro-interacción 1).</summary>
    public bool Landing(double nowMs) => Hop.IsLanding(SinceHop(nowMs), Motion.Motion.HopPanelV0);

    /// <summary>
    /// Cara del aterrizaje: <c>^.^</c> si el gato está contento y <c>o.o</c> en el resto; si coincide con
    /// la cara base, la otra, para que el toque se lea.
    /// </summary>
    public static string LandingFace(string baseFace, Mood mood)
    {
        string face = mood == Mood.Happy ? "^.^" : "o.o";
        if (face == baseFace) face = face == "^.^" ? "o.o" : "^.^";
        return face;
    }
}
