using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Color de acento de una carátula. PURO: recibe los píxeles BGRA de 32 bpp (los de un <c>LockBits</c>).
/// <list type="bullet">
/// <item>Histograma de tono de 12 cubos de 30°; cada píxel pesa <c>s²·v</c> (HSV), así lo saturado y luminoso
/// manda sobre fondos desvaídos aunque ocupe menos. Los píxeles con alfa &lt; 128 no cuentan.</item>
/// <item>Gana el cubo de más peso; su color es la media ponderada de tono, saturación y valor dentro del cubo
/// (un cubo no cruza 0°/360°, así que la media lineal del tono vale).</item>
/// <item>Si el cubo ganador pesa poco por píxel (&lt; <see cref="MinChroma"/>: carátula gris, blanca o negra)
/// → <see cref="Default"/>.</item>
/// <item>Legible sobre el negro del notch: valor ≥ <see cref="MinValue"/> y saturación en
/// [<see cref="MinSaturation"/>, <see cref="MaxSaturation"/>], sin tocar el tono.</item>
/// </list>
/// </summary>
public static class AlbumAccent
{
    public const int Buckets = 12;
    public const float MinChroma = 0.02f;
    public const float MinValue = 0.75f;
    public const float MinSaturation = 0.35f;
    public const float MaxSaturation = 0.90f;

    /// <summary>Acento por defecto: gris claro.</summary>
    public static readonly Color Default = Color.FromArgb(255, 0xD0, 0xD0, 0xD0);

    public static Color FromBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        long n = (long)width * height;
        if (bgra.Length < n * 4) throw new ArgumentException($"Se esperaban al menos {n * 4} bytes.", nameof(bgra));

        Span<double> weight = stackalloc double[Buckets];
        Span<double> sumH = stackalloc double[Buckets];
        Span<double> sumS = stackalloc double[Buckets];
        Span<double> sumV = stackalloc double[Buckets];
        weight.Clear(); sumH.Clear(); sumS.Clear(); sumV.Clear();
        long opaque = 0;

        for (long i = 0; i < n; i++)
        {
            int o = (int)(i * 4);
            if (bgra[o + 3] < 128) continue;
            opaque++;
            int b = bgra[o], g = bgra[o + 1], r = bgra[o + 2];
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max == 0 || max == min) continue;   // negro o gris puro: s = 0 → peso 0

            double v = max / 255.0;
            double s = (max - min) / (double)max;
            double h = Hue(r, g, b, max, max - min);
            int k = Math.Min(Buckets - 1, (int)(h / (360.0 / Buckets)));
            double w = s * s * v;
            weight[k] += w;
            sumH[k] += w * h;
            sumS[k] += w * s;
            sumV[k] += w * v;
        }

        if (opaque == 0) return Default;
        int best = 0;
        for (int k = 1; k < Buckets; k++) if (weight[k] > weight[best]) best = k;
        if (weight[best] / opaque < MinChroma) return Default;

        double hue = sumH[best] / weight[best];
        double sat = Math.Clamp(sumS[best] / weight[best], MinSaturation, MaxSaturation);
        double val = Math.Max(sumV[best] / weight[best], MinValue);
        return FromHsv(hue, sat, val);
    }

    private static double Hue(int r, int g, int b, int max, int delta)
    {
        double h = max == r ? (g - b) / (double)delta
                 : max == g ? 2 + (b - r) / (double)delta
                 : 4 + (r - g) / (double)delta;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s;
        double hp = h / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        (double r, double g, double b) = (int)hp switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        double m = v - c;
        return Color.FromArgb(255, To8(r + m), To8(g + m), To8(b + m));
    }

    private static int To8(double c) => (int)Math.Round(Math.Clamp(c, 0, 1) * 255);
}
