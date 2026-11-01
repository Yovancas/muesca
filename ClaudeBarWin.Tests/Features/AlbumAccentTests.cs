using System.Drawing;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="AlbumAccent"/> es PURO: color de acento de una carátula (BGRA) por histograma de tono de 12 cubos
/// ponderado <c>s²·v</c>. Sin color dominante → gris claro por defecto; el acento sale siempre legible sobre el
/// negro del notch.
/// </summary>
public class AlbumAccentTests
{
    private static byte[] Solid(int w, int h, Color c, byte a = 255)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = a; }
        return px;
    }

    /// <summary>Pinta las <paramref name="count"/> primeras posiciones (en píxeles) con <paramref name="c"/>.</summary>
    private static void Fill(byte[] px, int fromPixel, int count, Color c, byte a = 255)
    {
        for (int p = fromPixel; p < fromPixel + count; p++)
        {
            int o = p * 4;
            px[o] = c.B; px[o + 1] = c.G; px[o + 2] = c.R; px[o + 3] = a;
        }
    }

    private static void AssertHue(Color c, float expected, float tolerance = 12f)
    {
        float d = Math.Abs(c.GetHue() - expected);
        d = Math.Min(d, 360 - d);
        Assert.True(d <= tolerance, $"tono {c.GetHue():0.#}° y se esperaba ~{expected}° ({c})");
    }

    private static float Value(Color c) => Math.Max(c.R, Math.Max(c.G, c.B)) / 255f;

    [Fact]
    public void Solid_red_gives_red_accent()
    {
        var c = AlbumAccent.FromBgra(Solid(8, 8, Color.FromArgb(220, 30, 30)), 8, 8);
        AssertHue(c, 0);
        Assert.True(c.GetSaturation() > 0.5f, c.ToString());
        Assert.Equal(255, c.A);
    }

    [Fact]
    public void Grayscale_art_gives_default_light_gray()
    {
        var px = Solid(8, 8, Color.FromArgb(128, 128, 128));
        Fill(px, 0, 16, Color.White);
        Fill(px, 16, 16, Color.Black);
        Assert.Equal(AlbumAccent.Default, AlbumAccent.FromBgra(px, 8, 8));
    }

    [Fact]
    public void Small_saturated_patch_beats_large_washed_out_area()
    {
        // 80 % de un beige desvaído (s≈0,2) y 20 % de azul eléctrico: s² premia lo saturado.
        var px = Solid(10, 10, Color.FromArgb(200, 190, 160));
        Fill(px, 0, 20, Color.FromArgb(30, 60, 255));
        AssertHue(AlbumAccent.FromBgra(px, 10, 10), 230);
    }

    [Fact]
    public void Dominant_hue_wins_over_minor_hue()
    {
        var px = Solid(10, 10, Color.FromArgb(255, 140, 0)); // naranja, 40 %
        Fill(px, 0, 60, Color.FromArgb(20, 60, 250));          // azul, 60 %
        AssertHue(AlbumAccent.FromBgra(px, 10, 10), 225);
    }

    [Fact]
    public void Transparent_pixels_are_ignored()
    {
        var px = Solid(10, 10, Color.FromArgb(255, 0, 0), a: 0);  // rojo invisible, 90 %
        Fill(px, 0, 10, Color.FromArgb(0, 200, 60));               // verde opaco, 10 %
        AssertHue(AlbumAccent.FromBgra(px, 10, 10), 138);
    }

    [Fact]
    public void Dark_accent_is_lifted_for_contrast()
    {
        var c = AlbumAccent.FromBgra(Solid(8, 8, Color.FromArgb(10, 20, 90)), 8, 8); // azul marino oscuro
        AssertHue(c, 233);
        Assert.True(Value(c) >= AlbumAccent.MinValue - 0.01f, $"V={Value(c):0.00}");
    }

    [Fact]
    public void Rejects_short_buffer()
    {
        Assert.Throws<ArgumentException>(() => AlbumAccent.FromBgra(new byte[10], 2, 2));
    }
}
