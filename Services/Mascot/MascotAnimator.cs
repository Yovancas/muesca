using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services.Mascot;

/// <summary>
/// Salida del animador para un instante: qué frame, si parpadea, glifo del spinner, verbo y el
/// ángulo de arranque del arco del spinner (grados). <see cref="SpinnerGlyph"/> sigue siendo la SEÑAL
/// de "spinner activo" (<c>'\0'</c> = sin spinner); el renderer ya no lo pinta como texto sino como un
/// arco GDI+ que arranca en <see cref="SpinnerAngleDeg"/> (T-v039 F2). El ángulo es elapsed-driven
/// (derivado del tiempo, sin reloj ni Random por dentro) → determinista y testeable sin GDI+.
/// <see cref="Face"/> sustituye la cara del frame (aterrizaje del Salto, §4 micro-interacción 1); null = la del frame.
/// <see cref="PrevVerbIndex"/>/<see cref="VerbBlend"/> describen el cruce del verbo (§4 #8): con
/// <c>VerbBlend</c> &lt; 1 se pintan los dos, el previo con alfa 1 − blend y el actual con blend.
/// <see cref="EyeDx"/>/<see cref="EyeDy"/> desplazan la cara dentro de los paréntesis (ojos que miran
/// el cursor, §5), en px enteros.
/// </summary>
public readonly record struct MascotState(int FrameIndex, bool Blinking, char SpinnerGlyph, int VerbIndex, float SpinnerAngleDeg = 0f,
    string? Face = null, int PrevVerbIndex = 0, double VerbBlend = 1.0, int EyeDx = 0, int EyeDy = 0);

/// <summary>
/// Da <b>vida</b> a la mascota: PURO y elapsed-driven (regla de oro F3). Dado (fase,
/// <c>elapsedMsEnFase</c>, semilla determinista) devuelve <see cref="MascotState"/>. NO usa
/// <c>Random</c> ni el reloj por dentro — el jitter del parpadeo es un <b>hash de un contador</b>
/// (la ventana de parpadeo), así el resultado es determinista para la misma semilla y los tests no
/// necesitan reloj.
///
/// <list type="bullet">
/// <item><b>Tempos por fase</b>: Processing parpadea/pulsa más vivo, Idle lento, WaitingFor* pulsa
/// con urgencia. Idle y Ended son estáticos en el bestiario (un solo frame) → <c>FrameIndex==0</c>.</item>
/// <item><b>Blink con jitter</b>: el parpadeo NO es metronómico; cada ventana arranca el parpadeo en
/// un offset jittered por hash, esporádico (no en cada tick).</item>
/// <item><b>Spinner de glifos</b>: en Processing/Compacting, cicla por <see cref="SpinnerSequence"/>
/// (señal de "trabajo vivo"). Las demás fases no llevan spinner (<c>'\0'</c>).</item>
/// <item><b>Verbo</b>: índice dentro del pool de la fase (3-5 verbos), que rota con el tiempo (registro
/// juguetón); el string localizado lo resuelve <see cref="Verb"/>.</item>
/// </list>
/// </summary>
public static class MascotAnimator
{
    /// <summary>Secuencia de glifos del spinner (braille, "trabajo vivo"). Clean-room, propia.</summary>
    public const string SpinnerSequence = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";

    /// <summary>Cada cuántos ms avanza el spinner un glifo.</summary>
    public const double SpinnerStepMs = 90.0;

    /// <summary>Periodo (ms) de una vuelta completa del arco del spinner (T-v039 F2).</summary>
    public const double SpinnerSpinMs = 900.0;

    // --- Tempos de parpadeo por fase (ms) ----------------------------------
    // Periodo = cada cuánto se considera UNA oportunidad de parpadeo; Dur = cuánto dura el guiño.
    private const double IdleBlinkPeriodMs = 2600.0;       // Idle: lento, "idle peek" ocasional
    private const double ActiveBlinkPeriodMs = 900.0;      // Processing/Compacting: vivo
    private const double AttentionBlinkPeriodMs = 520.0;   // WaitingFor*: pulso de urgencia
    private const double BlinkDurMs = 130.0;

    // --- Verbo: cada cuánto rota dentro del pool y cuánto dura el cruce ---
    /// <summary>Cada cuánto (ms) rota el verbo dentro del pool (tempo de firma).</summary>
    public const double VerbStepMs = 1700.0;

    /// <summary>Cruce simultáneo (ms, lineal en alfa) entre el verbo saliente y el entrante (§4 #8).</summary>
    public const double VerbCrossfadeMs = 120.0;

    /// <summary>
    /// Frame base estático de la mascota: sin parpadeo, sin spinner, verbo en el índice 0. Es el
    /// estado al que colapsa toda la vida de la mascota cuando <c>reduceMotion</c> está activo
    /// (puerta única de la Tarea 7): conserva color/forma por fase pero NO hay movimiento.
    /// </summary>
    public static MascotState StaticState => new(FrameIndex: 0, Blinking: false, SpinnerGlyph: '\0', VerbIndex: 0);

    /// <summary>
    /// Muestrea el estado de animación de la mascota en <paramref name="elapsedMs"/> de la fase. Con
    /// <paramref name="reduceMotion"/> colapsa al <see cref="StaticState"/> (frame base, sin spinner ni
    /// jitter) por la <b>misma puerta</b> que el resto del motor — así reduce-motion no deja ningún
    /// resto de animación (spinner congelado incluido).
    /// </summary>
    public static MascotState Sample(SessionPhase phase, double elapsedMs, int seed = 0, bool reduceMotion = false)
    {
        if (reduceMotion) return StaticState;
        if (elapsedMs < 0) elapsedMs = 0;

        int frameCount = MascotSprite.Frames(phase).Count;
        bool blinking = ComputeBlink(phase, elapsedMs, seed);

        // Frame: estáticas (1 frame) → 0; animadas → alternan según el parpadeo/pulso.
        int frameIndex = frameCount <= 1 ? 0 : (blinking ? 1 : 0);

        bool hasSpinner = HasSpinner(phase);
        char spinner = hasSpinner ? SpinnerGlyphAt(elapsedMs) : '\0';
        float spinAngle = hasSpinner ? SpinnerAngleAt(elapsedMs) : 0f;
        int verbIndex = VerbIndexAt(phase, elapsedMs, seed);
        // Cruce del verbo: durante los primeros VerbCrossfadeMs de cada paso, si el índice cambió.
        int prevVerb = verbIndex;
        double blend = 1.0;
        long step = (long)Math.Floor(elapsedMs / VerbStepMs);
        double local = elapsedMs - step * VerbStepMs;
        if (step > 0 && local < VerbCrossfadeMs)
        {
            int prev = VerbIndexAt(phase, (step - 1) * VerbStepMs, seed);
            if (prev != verbIndex) { prevVerb = prev; blend = local / VerbCrossfadeMs; }
        }

        return new MascotState(frameIndex, blinking, spinner, verbIndex, spinAngle, null, prevVerb, blend);
    }

    /// <summary>Glifo del spinner en el instante dado (cicla por <see cref="SpinnerSequence"/>).</summary>
    public static char SpinnerGlyphAt(double elapsedMs)
    {
        int step = (int)Math.Floor(elapsedMs / SpinnerStepMs);
        int i = ((step % SpinnerSequence.Length) + SpinnerSequence.Length) % SpinnerSequence.Length;
        return SpinnerSequence[i];
    }

    /// <summary>
    /// Ángulo de arranque (grados, [0,360)) del arco del spinner en el instante dado: gira a un ritmo
    /// constante (una vuelta cada <see cref="SpinnerSpinMs"/> ms). PURO y elapsed-driven (sin reloj ni
    /// Random) → determinista, lo consume el renderer en <c>g.DrawArc</c> (T-v039 F2).
    /// </summary>
    public static float SpinnerAngleAt(double elapsedMs)
    {
        if (elapsedMs < 0) elapsedMs = 0;
        double frac = (elapsedMs % SpinnerSpinMs) / SpinnerSpinMs; // [0,1)
        return (float)(frac * 360.0);
    }

    private static bool HasSpinner(SessionPhase p) =>
        p is SessionPhase.Processing or SessionPhase.Compacting;

    /// <summary>
    /// ¿La fase tiene vida (parpadeo/pulso/spinner) que justifique el fast-tick? Idle y Ended son
    /// estáticos. El llamante (DashboardForm) lo usa para alimentar al <c>MotionScheduler</c>: con el
    /// panel oculto NO se anima igual (la puerta del scheduler manda).
    /// </summary>
    public static bool IsAnimatedPhase(SessionPhase p) =>
        p is SessionPhase.Processing or SessionPhase.Compacting
          or SessionPhase.WaitingForApproval or SessionPhase.WaitingForInput;

    // Parpadeo con jitter determinista: el tiempo se trocea en ventanas de longitud `period`. En la
    // ventana n el parpadeo arranca en un offset jittered (hash de seed,n) dentro de la ventana y
    // dura BlinkDurMs. Esporádico (no en cada tick) y reproducible para la misma semilla.
    private static bool ComputeBlink(SessionPhase phase, double elapsedMs, int seed)
    {
        double period = phase switch
        {
            SessionPhase.WaitingForApproval or SessionPhase.WaitingForInput => AttentionBlinkPeriodMs,
            SessionPhase.Processing or SessionPhase.Compacting => ActiveBlinkPeriodMs,
            _ => IdleBlinkPeriodMs,
        };

        long window = (long)Math.Floor(elapsedMs / period);
        double local = elapsedMs - window * period;

        // Offset del parpadeo dentro de la ventana: hash determinista en [0, period - BlinkDurMs).
        double room = Math.Max(0.0, period - BlinkDurMs);
        double frac = Hash01(seed, window);
        double start = frac * room;

        return local >= start && local < start + BlinkDurMs;
    }

    private static int VerbIndexAt(SessionPhase phase, double elapsedMs, int seed)
    {
        int pool = VerbPoolSize(phase);
        if (pool <= 1) return 0;
        long step = (long)Math.Floor(elapsedMs / VerbStepMs);
        // Hash del paso para que la rotación no sea un barrido lineal predecible (jitter juguetón).
        uint h = Hash(seed, step);
        return (int)(h % (uint)pool);
    }

    /// <summary>Tamaño del pool de verbos de la fase (3-5). Refleja los arrays de <see cref="Strings"/>.</summary>
    public static int VerbPoolSize(SessionPhase phase) => Pool(new Strings(), phase).Length;

    /// <summary>Verbo localizado de la fase en el índice dado (acotado al tamaño del pool).</summary>
    public static string Verb(Strings s, SessionPhase phase, int index)
    {
        var pool = Pool(s, phase);
        if (pool.Length == 0) return string.Empty;
        int i = ((index % pool.Length) + pool.Length) % pool.Length;
        return pool[i];
    }

    private static string[] Pool(Strings s, SessionPhase phase) => phase switch
    {
        SessionPhase.Processing => s.MascotVerbsProcessing,
        SessionPhase.WaitingForApproval => s.MascotVerbsWaitingApproval,
        SessionPhase.WaitingForInput => s.MascotVerbsWaitingInput,
        SessionPhase.Compacting => s.MascotVerbsCompacting,
        SessionPhase.Ended => s.MascotVerbsEnded,
        _ => s.MascotVerbsIdle,
    };

    // --- Hash determinista (sin Random): mezcla seed + contador a [0,1) y a uint ---------------
    private static uint Hash(int seed, long counter)
    {
        // SplitMix-ish: mezcla barata pero bien dispersa de (seed, counter).
        ulong x = unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)counter * 0xBF58476D1CE4E5B9UL);
        x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27; x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (uint)(x & 0xFFFFFFFF);
    }

    private static double Hash01(int seed, long counter) => Hash(seed, counter) / 4294967296.0;
}
