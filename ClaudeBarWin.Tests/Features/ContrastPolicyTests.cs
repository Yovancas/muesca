using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="ContrastPolicy"/> es PURO: analiza una franja BGRA del fondo bajo el notch (luminancia relativa
/// sRGB) y decide con histéresis si el notch está sobre claro u oscuro.
/// </summary>
public class ContrastPolicyTests
{
    private static byte[] Solid(int w, int h, byte b, byte g, byte r, byte a = 255)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a; }
        return px;
    }

    private static BackdropSample Gray(byte v) => ContrastPolicy.Analyze(Solid(4, 1, v, v, v), 4, 1);

    [Fact]
    public void Black_tab_bar_is_OnDark()
    {
        var s = ContrastPolicy.Analyze(Solid(300, 1, 0x20, 0x20, 0x20), 300, 1);
        Assert.InRange(s.MeanLuminance, 0.010f, 0.020f); // #202020 → 0,0144
        Assert.Equal(0f, s.Variance, 6);
        Assert.Equal(NotchContrast.OnDark, ContrastPolicy.Decide(s, NotchContrast.OnLight));
    }

    [Fact]
    public void White_page_is_OnLight()
    {
        var s = ContrastPolicy.Analyze(Solid(300, 1, 0xFF, 0xFF, 0xFF), 300, 1);
        Assert.Equal(1f, s.MeanLuminance, 4);
        Assert.Equal(NotchContrast.OnLight, ContrastPolicy.Decide(s, NotchContrast.OnDark));
    }

    [Fact]
    public void Luminance_weights_channels_in_BGRA_order()
    {
        // Rojo puro: Y = 0,2126; verde puro: 0,7152; azul puro: 0,0722.
        Assert.Equal(0.2126f, ContrastPolicy.Analyze(Solid(1, 1, 0, 0, 255), 1, 1).MeanLuminance, 3);
        Assert.Equal(0.7152f, ContrastPolicy.Analyze(Solid(1, 1, 0, 255, 0), 1, 1).MeanLuminance, 3);
        Assert.Equal(0.0722f, ContrastPolicy.Analyze(Solid(1, 1, 255, 0, 0), 1, 1).MeanLuminance, 3);
    }

    [Fact]
    public void Variance_of_half_black_half_white()
    {
        var px = new byte[2 * 4];
        px[4] = px[5] = px[6] = 255;
        var s = ContrastPolicy.Analyze(px, 2, 1);
        Assert.Equal(0.5f, s.MeanLuminance, 4);
        Assert.Equal(0.25f, s.Variance, 4);
    }

    [Fact]
    public void Hysteresis_avoids_flapping()
    {
        // Gris #808080 → 0,216 (< 0,22): pasa a oscuro. #8E8E8E → 0,270: en la banda muerta, se queda como está.
        var mid = Gray(0x8E);
        Assert.InRange(mid.MeanLuminance, 0.22f, 0.30f);
        Assert.Equal(NotchContrast.OnLight, ContrastPolicy.Decide(mid, NotchContrast.OnLight));
        Assert.Equal(NotchContrast.OnDark, ContrastPolicy.Decide(mid, NotchContrast.OnDark));

        Assert.Equal(NotchContrast.OnDark, ContrastPolicy.Decide(Gray(0x80), NotchContrast.OnLight));
        Assert.Equal(NotchContrast.OnDark, ContrastPolicy.Decide(new BackdropSample(0.30f, 0), NotchContrast.OnDark));
        Assert.Equal(NotchContrast.OnLight, ContrastPolicy.Decide(new BackdropSample(0.301f, 0), NotchContrast.OnDark));
        Assert.Equal(NotchContrast.OnLight, ContrastPolicy.Decide(new BackdropSample(0.22f, 0), NotchContrast.OnLight));
        Assert.Equal(NotchContrast.OnDark, ContrastPolicy.Decide(new BackdropSample(0.2199f, 0), NotchContrast.OnLight));
    }

    [Fact]
    public void Analyze_ignores_alpha()
    {
        // BitBlt del escritorio deja basura en el alfa: no debe influir.
        var opaque = ContrastPolicy.Analyze(Solid(8, 2, 0xF0, 0xF0, 0xF0, 255), 8, 2);
        var zero = ContrastPolicy.Analyze(Solid(8, 2, 0xF0, 0xF0, 0xF0, 0), 8, 2);
        var junk = ContrastPolicy.Analyze(Solid(8, 2, 0xF0, 0xF0, 0xF0, 37), 8, 2);
        Assert.Equal(opaque, zero);
        Assert.Equal(opaque, junk);
    }

    [Fact]
    public void Analyze_validates_size()
    {
        Assert.Throws<ArgumentException>(() => ContrastPolicy.Analyze(new byte[7], 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContrastPolicy.Analyze(new byte[4], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContrastPolicy.Analyze(new byte[4], 1, -1));
        // Un búfer mayor (stride con relleno al final) se acepta: solo se leen w × h píxeles.
        Assert.Equal(1f, ContrastPolicy.Analyze(Solid(2, 1, 255, 255, 255), 1, 1).MeanLuminance, 4);
    }
}
