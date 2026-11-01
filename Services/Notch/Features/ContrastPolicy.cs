namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Sobre qué fondo está el notch: claro (como ahora) u oscuro (necesita contorno).</summary>
public enum NotchContrast { OnLight, OnDark }

/// <summary>Resumen de la franja de fondo bajo el notch: luminancia relativa media (0..1) y su varianza.</summary>
public readonly record struct BackdropSample(float MeanLuminance, float Variance);

/// <summary>
/// Política de contraste del notch. PURO: recibe los píxeles (BGRA de 32 bpp, como los deja un BitBlt) y el
/// estado anterior; quien muestrea decide cuándo (solo con eventos, nunca en bucle).
/// <list type="bullet">
/// <item>Luminancia relativa sRGB (linealizada, coeficientes Rec. 709). El alfa se ignora: tras un BitBlt
/// del escritorio no significa nada.</item>
/// <item>Histéresis: pasa a <see cref="NotchContrast.OnDark"/> por debajo de <see cref="ToDark"/> (0,22) y
/// vuelve a <see cref="NotchContrast.OnLight"/> por encima de <see cref="ToLight"/> (0,30); entre medias se
/// queda como estaba, así un fondo intermedio no hace parpadear el contorno.</item>
/// </list>
/// </summary>
public static class ContrastPolicy
{
    public const float ToDark = 0.22f;
    public const float ToLight = 0.30f;

    // sRGB 8 bits → lineal, precalculado.
    private static readonly float[] Linear = BuildLinear();

    /// <summary>Analiza <paramref name="width"/> × <paramref name="height"/> píxeles BGRA contiguos (filas sin relleno;
    /// si el búfer es más largo, el resto se ignora).</summary>
    public static BackdropSample Analyze(ReadOnlySpan<byte> bgra, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        long n = (long)width * height;
        if (bgra.Length < n * 4) throw new ArgumentException($"Se esperaban al menos {n * 4} bytes.", nameof(bgra));

        double sum = 0, sumSq = 0;
        for (long i = 0; i < n; i++)
        {
            int o = (int)(i * 4);
            double y = 0.0722 * Linear[bgra[o]] + 0.7152 * Linear[bgra[o + 1]] + 0.2126 * Linear[bgra[o + 2]];
            sum += y;
            sumSq += y * y;
        }
        double mean = sum / n;
        double variance = Math.Max(0, sumSq / n - mean * mean);
        return new BackdropSample((float)mean, (float)variance);
    }

    public static NotchContrast Decide(BackdropSample s, NotchContrast previous) => previous switch
    {
        NotchContrast.OnLight when s.MeanLuminance < ToDark => NotchContrast.OnDark,
        NotchContrast.OnDark when s.MeanLuminance > ToLight => NotchContrast.OnLight,
        _ => previous,
    };

    private static float[] BuildLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            t[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        return t;
    }
}
