using System.Drawing;
using System.Drawing.Imaging;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Contraste adaptativo en el pintado (F5 · B6): sobre fondo oscuro, contorno de 1 px blanco al 14 % (sin raya en el
/// borde de la pantalla) y raya de Mínimo gris claro; sobre claro, como siempre.
/// </summary>
public class NotchContrastPaintTests
{
    private static readonly Size Surface = new(700, 200);

    private static Bitmap Paint(NotchContrast contrast, NotchMode mode, float w, float h, RealUsage? quota = null)
    {
        var bmp = new Bitmap(Surface.Width, Surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("en")) { Contrast = contrast };
        painter.Paint(g, Surface, new NotchFrame(w, h, 1f, mode, null, Array.Empty<SessionState>(), quota, 0, 1));
        return bmp;
    }

    [Fact]
    public void Minimal_bar_is_light_gray_on_dark()
    {
        using var light = Paint(NotchContrast.OnLight, NotchMode.Minimal, 44, 4);
        using var dark = Paint(NotchContrast.OnDark, NotchMode.Minimal, 44, 4);
        Assert.Equal(Color.Black.ToArgb(), light.GetPixel(350, 1).ToArgb());
        Assert.Equal(NotchContentPainter.MinimalOnDark.ToArgb(), dark.GetPixel(350, 1).ToArgb());
    }

    [Fact]
    public void Quota_color_still_wins_on_the_minimal_bar()
    {
        var q = new RealUsage { FiveHour = new UsageWindow(85, null) };
        using var dark = Paint(NotchContrast.OnDark, NotchMode.Minimal, 44, 4, q);
        Assert.Equal(Theme.Dark.Warn.ToArgb(), dark.GetPixel(350, 1).ToArgb());
    }

    [Fact]
    public void OnDark_outlines_the_pill_but_not_the_screen_edge()
    {
        using var light = Paint(NotchContrast.OnLight, NotchMode.Idle, 112, 30);
        using var dark = Paint(NotchContrast.OnDark, NotchMode.Idle, 112, 30);
        var pill = NotchGeometry.PillRect(112, 30, Surface);
        int x = (int)pill.Left, mid = Surface.Width / 2, bottom = (int)pill.Bottom - 1;

        // Borde inferior en el centro: negro puro sobre claro; con contorno, más claro pero opaco.
        var lb = light.GetPixel(mid, bottom);
        var db = dark.GetPixel(mid, bottom);
        Assert.Equal(Color.Black.ToArgb(), lb.ToArgb());
        Assert.Equal(255, db.A);
        Assert.InRange(db.R, 20, 60);                                   // 14 % de blanco sobre negro ≈ 36
        // Lateral izquierdo a media altura: también contorneado.
        Assert.True(dark.GetPixel(x, 15).R > light.GetPixel(x, 15).R);
        // El borde de la pantalla (fila 0) no lleva contorno, y fuera de la forma sigue transparente.
        Assert.Equal(Color.Black.ToArgb(), dark.GetPixel(mid, 0).ToArgb());
        Assert.Equal(0, dark.GetPixel(mid, (int)pill.Bottom + 2).A);
    }

    [Fact]
    public void OnLight_paints_exactly_as_before()
    {
        using var a = Paint(NotchContrast.OnLight, NotchMode.Idle, 112, 30);
        var b = new Bitmap(Surface.Width, Surface.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(b))
        using (var painter = new NotchContentPainter(Localization.Get("en")))
            painter.Paint(g, Surface, new NotchFrame(112, 30, 1f, NotchMode.Idle, null, Array.Empty<SessionState>(), null, 0, 1));
        using (b)
            for (int y = 0; y < 40; y++)
                for (int x = 230; x < 470; x++)
                    Assert.Equal(b.GetPixel(x, y).ToArgb(), a.GetPixel(x, y).ToArgb());
    }
}
