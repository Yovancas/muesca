using System.Media;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Campanita del notch: un WAV de dos tonos (Mi5 → Si5, 659,25 Hz 0,18 s y 987,77 Hz 0,30 s solapados
/// 0,12 s; ≈ 0,36 s en total) sintetizado UNA vez en memoria. Sin recursos ni archivos: el
/// <see cref="SoundPlayer"/> se crea perezoso con un <see cref="MemoryStream"/> y se reutiliza.
///
/// Suena con «Terminado», con el aviso rojo de cuota, con el recordatorio de una tarea (F5 · B5) y (F2, sonido de atención) con el Permiso que se puede contestar
/// con los botones; no con el HUD ni con el Permiso de solo lectura («Responde en la terminal»). Nunca en pantalla
/// completa ni con el ajuste apagado (<see cref="ShouldPlay"/>, puro). El fin de una fase del pomodoro lleva su propio aviso,
/// más discreto (<see cref="BuildFocusChime"/>), con su ajuste aparte (<see cref="ShouldPlayFocus"/>).
/// </summary>
public static class NotchSound
{
    private const double FirstHz = 659.25, FirstSeconds = 0.18;
    private const double SecondHz = 987.77, SecondSeconds = 0.30;
    private const double OverlapSeconds = 0.12;
    private const double AttackSeconds = 0.005;
    /// <summary>Constante de tiempo del decaimiento exponencial (relativa a la duración de cada tono).</summary>
    private const double DecayFraction = 0.28;
    /// <summary>Pico de cada tono (fracción de fondo de escala): con el solape suman &lt; 1, sin saturar.</summary>
    private const double ToneGain = 0.32;

    // Aviso del pomodoro (fin de fase): más discreto que la campanita, dos notas suaves que bajan (Sol5 → Do5), más largas y a
    // menos volumen. Se distingue de los avisos de Claude sin sobresaltar.
    private const double FocusFirstHz = 783.99, FocusFirstSeconds = 0.22, FocusSecondHz = 523.25, FocusSecondSeconds = 0.42;
    private const double FocusOverlapSeconds = 0.08, FocusGain = 0.16;

    private static readonly object Gate = new();
    private static SoundPlayer? _player, _focusPlayer;

    /// <summary>Decide si un aviso que entra debe sonar. Puro. <paramref name="answerable"/>: Permiso con
    /// <c>RequestId</c> (botones); sin él el Permiso calla, porque no hay nada que hacer en el notch.</summary>
    public static bool ShouldPlay(NotchAlertKind kind, bool soundEnabled, bool fullscreen, bool answerable = false) =>
        soundEnabled && !fullscreen && (kind is NotchAlertKind.Completed or NotchAlertKind.QuotaAlert or NotchAlertKind.Reminder
                                        || (kind == NotchAlertKind.Permission && answerable));

    /// <summary>Decide para un aviso concreto (el Permiso suena solo si lleva botones). Puro.</summary>
    public static bool ShouldPlay(NotchAlert alert, bool soundEnabled, bool fullscreen) =>
        ShouldPlay(alert.Kind, soundEnabled, fullscreen, answerable: alert.RequestId is not null);

    /// <summary>
    /// Clave para no repetir la campanita: un Permiso con botones suena UNA vez por petición (al volver a armarse por un
    /// cambio de texto el aviso es otra instancia, pero la petición es la misma). null = el resto de avisos, que se
    /// identifican por instancia. Puro.
    /// </summary>
    public static string? ChimeKey(NotchAlert alert) =>
        alert.Kind == NotchAlertKind.Permission && alert.RequestId is { } id ? "perm:" + id : null;

    /// <summary>WAV PCM mono de 16 bits con la campanita completa (cabecera de 44 bytes + muestras).</summary>
    public static byte[] BuildChime(int sampleRate = 22050) =>
        BuildTwoTones(sampleRate, FirstHz, FirstSeconds, SecondHz, SecondSeconds, OverlapSeconds, ToneGain);

    /// <summary>WAV del aviso discreto del fin de fase del pomodoro (dos notas suaves que bajan, ≈ 0,56 s).</summary>
    public static byte[] BuildFocusChime(int sampleRate = 22050) =>
        BuildTwoTones(sampleRate, FocusFirstHz, FocusFirstSeconds, FocusSecondHz, FocusSecondSeconds, FocusOverlapSeconds, FocusGain);

    /// <summary>
    /// ¿Suena el fin de una fase del pomodoro? Con el sonido del notch Y el del foco encendidos, nunca en pantalla completa. PURO.
    /// </summary>
    public static bool ShouldPlayFocus(bool notchSound, bool focusSound, bool fullscreen) => notchSound && focusSound && !fullscreen;

    /// <summary>Reproduce el aviso del pomodoro en segundo plano; sin audio, falla en silencio.</summary>
    public static void PlayFocus()
    {
        try
        {
            lock (Gate)
            {
                _focusPlayer ??= new SoundPlayer(new MemoryStream(BuildFocusChime()));
                _focusPlayer.Play();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudo reproducir el aviso del pomodoro: {ex.Message}");
        }
    }

    private static byte[] BuildTwoTones(int sampleRate, double firstHz, double firstSeconds, double secondHz, double secondSeconds,
        double overlapSeconds, double gain)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        double secondStart = firstSeconds - overlapSeconds;
        int count = (int)Math.Ceiling((secondStart + secondSeconds) * sampleRate);

        using var ms = new MemoryStream(44 + count * 2);
        using var w = new BinaryWriter(ms);
        WriteHeader(w, sampleRate, count);
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / sampleRate;
            double v = Tone(t, firstHz, firstSeconds, gain) + Tone(t - secondStart, secondHz, secondSeconds, gain);
            w.Write((short)Math.Round(Math.Clamp(v, -1.0, 1.0) * short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Reproduce la campanita en segundo plano (no bloquea el hilo de UI). Sin dispositivo de
    /// audio o con el servicio caído, falla en silencio: un aviso visual no debe tumbar el notch.</summary>
    public static void Play()
    {
        try
        {
            lock (Gate)
            {
                _player ??= new SoundPlayer(new MemoryStream(BuildChime()));
                _player.Play();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudo reproducir el sonido del notch: {ex.Message}");
        }
    }

    /// <summary>Un tono con ataque lineal de 5 ms y decaimiento exponencial; 0 fuera de [0, duración).</summary>
    private static double Tone(double t, double hz, double duration, double gain)
    {
        if (t < 0 || t >= duration) return 0;
        double attack = Math.Min(1.0, t / AttackSeconds);
        double decay = Math.Exp(-t / (duration * DecayFraction));
        // Rampa final de 10 ms: el decaimiento no llega a 0 y cortar en seco daría un chasquido.
        double release = Math.Min(1.0, (duration - t) / 0.01);
        return gain * attack * decay * release * Math.Sin(2 * Math.PI * hz * t);
    }

    private static void WriteHeader(BinaryWriter w, int sampleRate, int samples)
    {
        const short channels = 1, bits = 16;
        int dataBytes = samples * channels * bits / 8;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);                                   // tamaño del bloque fmt
        w.Write((short)1);                             // PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * bits / 8);     // bytes por segundo
        w.Write((short)(channels * bits / 8));         // alineación de bloque
        w.Write(bits);
        w.Write("data"u8);
        w.Write(dataBytes);
    }
}
