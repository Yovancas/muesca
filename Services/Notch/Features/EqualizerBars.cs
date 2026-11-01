namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Ecualizador decorativo de «está sonando»: siete barras centradas en vertical recorridas por una onda (estilo
/// «audio wave» que eligió el usuario el 29-oct; dibujo propio). PURO: la altura de cada barra
/// sale del instante que se le pasa (nada de reloj ni Random dentro). No refleja el audio real: es solo una señal de
/// vida, como en los reproductores habituales.
/// </summary>
public static class EqualizerBars
{
    public const int Count = 7;

    /// <summary>Altura mínima (en pausa, o en el punto bajo de la oscilación), en fracción del alto disponible.</summary>
    public const float Low = 0.25f;

    // Una onda que recorre las barras de izquierda a derecha en bucles de ~1,2 s: cada barra va un poco por detrás de la
    // anterior, y un segundo armónico más lento le quita regularidad para que no parezca un metrónomo.
    private const double Period = 1.2;
    private const double Lag = 0.17;          // retraso entre barras vecinas (fracción del periodo)

    /// <summary>
    /// Alturas (de <see cref="Low"/> a 1) de las <see cref="Count"/> barras en el instante <paramref name="seconds"/>.
    /// En pausa todas quedan en <see cref="Low"/>; con <paramref name="still"/> (reduce-motion) se dibuja una forma
    /// fija y reconocible, sin movimiento.
    /// </summary>
    public static float[] Heights(double seconds, bool playing, bool still = false)
    {
        var h = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            if (!playing) { h[i] = Low; continue; }
            if (still) { h[i] = StillShape[i]; continue; }
            double phase = 2 * Math.PI * (seconds / Period - i * Lag);
            double wave = 0.5 + 0.5 * Math.Sin(phase);                                  // 0..1
            double slow = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (seconds / (Period * 2.7)) + i * 1.3);
            double k = Math.Clamp(0.78 * wave + 0.22 * slow, 0, 1);
            h[i] = (float)(Low + (1 - Low) * k);
        }
        return h;
    }

    private static readonly float[] StillShape = { 0.35f, 0.7f, 0.5f, 1f, 0.6f, 0.8f, 0.4f };
}
