using System.Drawing;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="AdaptiveContrast"/> (F5 · B6): muestrea el fondo SOLO cuando se le pide (arranque, cambio de primer plano,
/// fin de arrastre, fin de pantalla completa), con un debounce de 300 ms, y nunca en bucle. El muestreo real es Win32
/// (<c>BitBlt</c>); aquí va un doble que cuenta las capturas.
/// </summary>
public class AdaptiveContrastTests
{
    private static readonly Rectangle Strip = new(100, 31, 112, 1);
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private sealed class FakeSampler : IBackdropSampler
    {
        public byte? Gray = 0x20;              // null = la captura falla
        public int Calls;
        public Rectangle Last;
        public bool ShortBuffer;

        public byte[]? Capture(Rectangle strip)
        {
            Calls++;
            Last = strip;
            if (Gray is not { } v) return null;
            var px = new byte[(ShortBuffer ? strip.Width - 1 : strip.Width) * strip.Height * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = v; px[i + 1] = v; px[i + 2] = v; px[i + 3] = 255; }
            return px;
        }
    }

    [Fact]
    public void Does_not_sample_without_a_trigger()
    {
        var s = new FakeSampler();
        var c = new AdaptiveContrast(s);
        Assert.Null(c.NextWakeIn(Ms(0)));
        Assert.False(c.Tick(Ms(10_000), Strip));
        Assert.Equal(0, s.Calls);
        Assert.Equal(NotchContrast.OnLight, c.Current);
    }

    [Fact]
    public void Burst_of_triggers_samples_once_300ms_after_the_last()
    {
        var s = new FakeSampler();
        var c = new AdaptiveContrast(s);
        c.Request(Ms(0));
        c.Request(Ms(100));
        c.Request(Ms(250));
        Assert.Equal(Ms(300), c.NextWakeIn(Ms(250)));

        Assert.False(c.Tick(Ms(300), Strip));       // el primero ya habría vencido, pero el debounce se reinició
        Assert.False(c.Tick(Ms(549), Strip));
        Assert.Equal(0, s.Calls);

        Assert.True(c.Tick(Ms(550), Strip));
        Assert.Equal(1, s.Calls);
        Assert.Equal(Strip, s.Last);
        Assert.Equal(NotchContrast.OnDark, c.Current);
    }

    [Fact]
    public void Reset_forgets_the_old_contrast_and_the_pending_sample()
    {
        var s = new FakeSampler { Gray = 0x20 };
        var c = new AdaptiveContrast(s);
        c.Request(Ms(0));
        Assert.True(c.Tick(Ms(300), Strip));
        Assert.Equal(NotchContrast.OnDark, c.Current);

        c.Request(Ms(1000));
        c.Reset();   // ajuste apagado: al volver a encenderlo se pinta OnLight hasta el primer muestreo
        Assert.Equal(NotchContrast.OnLight, c.Current);
        Assert.Null(c.NextWakeIn(Ms(1000)));
        Assert.False(c.Tick(Ms(5000), Strip));
        Assert.Equal(1, s.Calls);

        c.Request(Ms(6000));   // y el primer muestreo tras encenderlo sí avisa del cambio
        Assert.True(c.Tick(Ms(6300), Strip));
        Assert.Equal(NotchContrast.OnDark, c.Current);
    }

    [Fact]
    public void After_sampling_nothing_is_pending_so_there_is_no_loop()
    {
        var s = new FakeSampler();
        var c = new AdaptiveContrast(s);
        c.Request(Ms(0));
        c.Tick(Ms(300), Strip);
        Assert.Null(c.NextWakeIn(Ms(300)));
        for (int t = 400; t < 60_000; t += 1000) Assert.False(c.Tick(Ms(t), Strip));
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void Same_backdrop_again_reports_no_change()
    {
        var s = new FakeSampler { Gray = 0x20 };
        var c = new AdaptiveContrast(s);
        c.Request(Ms(0));
        Assert.True(c.Tick(Ms(300), Strip));
        c.Request(Ms(1000));
        Assert.False(c.Tick(Ms(1300), Strip));
        Assert.Equal(2, s.Calls);
        Assert.Equal(NotchContrast.OnDark, c.Current);
    }

    [Fact]
    public void Back_to_a_light_backdrop_returns_to_OnLight()
    {
        var s = new FakeSampler { Gray = 0x10 };
        var c = new AdaptiveContrast(s);
        c.Request(Ms(0));
        c.Tick(Ms(300), Strip);
        s.Gray = 0xF0;
        c.Request(Ms(1000));
        Assert.True(c.Tick(Ms(1300), Strip));
        Assert.Equal(NotchContrast.OnLight, c.Current);
    }

    [Fact]
    public void Middle_gray_keeps_the_previous_contrast()
    {
        // #8A8A8A ≈ 0,25 de luminancia: entre 0,22 y 0,30 ⇒ histéresis, sin parpadeo.
        var s = new FakeSampler { Gray = 0x10 };
        var c = new AdaptiveContrast(s);
        c.Request(Ms(0));
        c.Tick(Ms(300), Strip);
        s.Gray = 0x8A;
        c.Request(Ms(1000));
        Assert.False(c.Tick(Ms(1300), Strip));
        Assert.Equal(NotchContrast.OnDark, c.Current);
    }

    [Fact]
    public void Failed_capture_keeps_the_previous_contrast_and_does_not_retry()
    {
        var s = new FakeSampler { Gray = null };
        var c = new AdaptiveContrast(s);
        c.Request(Ms(0));
        Assert.False(c.Tick(Ms(300), Strip));
        Assert.Equal(NotchContrast.OnLight, c.Current);
        Assert.Null(c.NextWakeIn(Ms(300)));

        s.Gray = 0x20;
        s.ShortBuffer = true;                       // búfer corto: tampoco decide
        c.Request(Ms(1000));
        Assert.False(c.Tick(Ms(1300), Strip));
        Assert.Equal(NotchContrast.OnLight, c.Current);
    }

    [Fact]
    public void Empty_strip_does_not_capture()
    {
        var s = new FakeSampler();
        var c = new AdaptiveContrast(s);
        c.Request(Ms(0));
        Assert.False(c.Tick(Ms(300), Rectangle.Empty));
        Assert.Equal(0, s.Calls);
        Assert.Null(c.NextWakeIn(Ms(300)));
    }

    [Fact]
    public void Cancel_drops_the_pending_sample()
    {
        var s = new FakeSampler();
        var c = new AdaptiveContrast(s);
        c.Request(Ms(0));
        c.Cancel();
        Assert.Null(c.NextWakeIn(Ms(0)));
        Assert.False(c.Tick(Ms(300), Strip));
        Assert.Equal(0, s.Calls);
    }

    [Fact]
    public void NextWakeIn_never_goes_negative()
    {
        var c = new AdaptiveContrast(new FakeSampler());
        c.Request(Ms(0));
        Assert.Equal(TimeSpan.Zero, c.NextWakeIn(Ms(900)));
    }

    [Theory]
    [InlineData(0, 0, 700, 112f, 30f, 294, 31, 112)]           // 100 %: pastilla 112 × 30 centrada en 700
    [InlineData(1000, 0, 1050, 168f, 45f, 1441, 46, 168)]      // 150 %, segundo monitor a x = 1000
    [InlineData(0, -1080, 700, 112.4f, 30.2f, 294, -1048, 112)] // monitor encima (y negativa); fracciones al px
    public void Strip_is_one_px_tall_centered_just_below_the_pill(int left, int top, int surfaceWidth,
        float pillW, float pillH, int x, int y, int w)
    {
        var r = AdaptiveContrast.StripBelow(new Point(left, top), surfaceWidth, new SizeF(pillW, pillH));
        Assert.Equal(new Rectangle(x, y, w, 1), r);
    }

    [Fact]
    public void Strip_of_an_empty_pill_is_empty()
        => Assert.True(AdaptiveContrast.StripBelow(Point.Empty, 700, SizeF.Empty).IsEmpty);
}
