using System.Drawing;
using System.Drawing.Imaging;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// F5 · B7: el reposo pinta el reloj (hora + fecha) en vez de los anillos cuando le toca, y el gato (ojos) en el reposo, la
/// vista previa y sobre la raya, siempre dentro de la pastilla. GDI+: solo corre en Windows.
/// </summary>
public class NotchIdlePaintTests
{
    private static readonly Size Surface = new(700, 200);

    private static RealUsage Quota(double five) => new()
    {
        FiveHour = new UsageWindow(five, DateTimeOffset.Now.AddHours(2)),
        SevenDay = new UsageWindow(40, DateTimeOffset.Now.AddDays(3)),
    };

    private static Bitmap Paint(NotchMode mode, IdleView? idle, float scale = 1f, double five = 42)
    {
        int layout = (idle?.Clock == true ? IdleScene.LayoutClock : 0) | (idle?.Mascot is not null ? IdleScene.LayoutMascot : 0);
        var size = IdleScene.SizeFor(mode, layout, 90f, scale);
        var surface = new Size((int)(Surface.Width * scale), (int)(Surface.Height * scale));
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, scale, mode, null,
            Array.Empty<SessionState>(), Quota(five), 0f, 1f, Idle: idle));
        return bmp;
    }

    private static RectangleF Pill(NotchMode mode, int layout, float scale = 1f)
    {
        var size = IdleScene.SizeFor(mode, layout, 90f, scale);
        return NotchGeometry.PillRect(size.Width, size.Height,
            new Size((int)(Surface.Width * scale), (int)(Surface.Height * scale)));
    }

    /// <summary>Píxeles claros (texto, anillos, ojos) dentro de <paramref name="r"/>.</summary>
    private static int Bright(Bitmap b, RectangleF r)
    {
        int n = 0;
        for (int y = Math.Max(0, (int)r.Top); y < Math.Min(b.Height, (int)r.Bottom); y++)
        for (int x = Math.Max(0, (int)r.Left); x < Math.Min(b.Width, (int)r.Right); x++)
        {
            var c = b.GetPixel(x, y);
            if (c.A > 0 && c.R + c.G + c.B > 240) n++;
        }
        return n;
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Clock_face_draws_text_instead_of_the_rings(float k)
    {
        using var rings = Paint(NotchMode.Idle, null, k);
        using var clock = Paint(NotchMode.Idle, new IdleView(true, "18:55", "jue 29 oct"), k);
        var pill = Pill(NotchMode.Idle, IdleScene.LayoutClock, k);
        Assert.True(Bright(clock, pill) > 20);
        bool differs = false;
        for (int x = (int)pill.Left; x < (int)pill.Right && !differs; x++)
            for (int y = 0; y < (int)pill.Bottom && !differs; y++)
                differs = rings.GetPixel(x, y) != clock.GetPixel(x, y);
        Assert.True(differs);
        Assert.Equal(0, clock.GetPixel(2, clock.Height - 2).A);
    }

    [Fact]
    public void Clock_text_fits_the_idle_pill_in_spanish_24h()
    {
        using var painter = new NotchContentPainter(Localization.Get("es"));
        float w = painter.MeasureClock(1f, "18:55", "jue 29 oct");
        Assert.InRange(w, 40f, 112f - 2 * IdleScene.ClockPadX);
    }

    [Theory]
    [InlineData(NotchMode.Idle, 1f)]
    [InlineData(NotchMode.Peek, 1f)]
    [InlineData(NotchMode.Minimal, 1f)]
    [InlineData(NotchMode.Minimal, 1.5f)]
    public void Mascot_eyes_are_drawn_where_the_scene_says(NotchMode mode, float k)
    {
        var look = new MascotLook(MascotState.Awake, Point.Empty, false);
        using var bmp = Paint(mode, new IdleView(false, "", "", look), k);
        var pill = Pill(mode, IdleScene.LayoutMascot, k);
        var (l, r) = IdleScene.EyeRects(IdleScene.MascotCenter(pill, mode, k), look, k);
        Assert.True(Bright(bmp, RectangleF.Inflate(l, 1f, 1f)) > 0, $"sin ojo izquierdo en {l}");
        Assert.True(Bright(bmp, RectangleF.Inflate(r, 1f, 1f)) > 0, $"sin ojo derecho en {r}");
        Assert.Equal(0, bmp.GetPixel(2, bmp.Height - 2).A);
    }

    [Fact]
    public void Line_without_the_cat_keeps_its_quota_color()
    {
        using var bmp = Paint(NotchMode.Minimal, null, 1f, five: 85);
        var pill = Pill(NotchMode.Minimal, 0);
        var c = bmp.GetPixel((int)(pill.Left + pill.Width / 2), 1);
        Assert.True(c.R + c.G + c.B > 100, $"la raya ≥ 80 % debería ir en color: {c}");
    }

    [Fact]
    public void Cat_on_the_line_carries_the_quota_color_in_its_eyes()
    {
        var look = new MascotLook(MascotState.Awake, Point.Empty, false);
        using var bmp = Paint(NotchMode.Minimal, new IdleView(false, "", "", look), 1f, five: 85);
        var pill = Pill(NotchMode.Minimal, IdleScene.LayoutMascot);
        var (l, _) = IdleScene.EyeRects(IdleScene.MascotCenter(pill, NotchMode.Minimal, 1f), look, 1f);
        var eye = bmp.GetPixel((int)(l.Left + l.Width / 2), (int)(l.Top + l.Height / 2));
        Assert.True(eye.R > eye.B + 40, $"ojo sin el color de aviso: {eye}");
        var corner = bmp.GetPixel((int)pill.Left + 8, (int)pill.Top + 1);
        Assert.True(corner.R + corner.G + corner.B < 30, $"la cápsula del gato es negra: {corner}");
    }
}
