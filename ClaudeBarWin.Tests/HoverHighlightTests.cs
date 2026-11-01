using System.Drawing;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// F3 Task 2: el realce del hover. Antes la clave pasaba a null ANTES de animar 1→0 y el fade-out
/// nunca se veía; y de A a B saltaba porque el valor ya estaba en 1. Ahora el realce conserva su
/// último rect mientras se desvanece y se desliza (muelles) entre elementos. Puro, dt en segundos.
/// </summary>
public class HoverHighlightTests
{
    private static readonly RectangleF A = new(10, 20, 100, 18);
    private static readonly RectangleF B = new(10, 60, 140, 18);

    private static void Run(HoverHighlight h, double seconds)
    {
        for (double t = 0; t < seconds; t += 1 / 60.0) h.Advance(1 / 60.0);
    }

    [Fact]
    public void Leave_keeps_rect_until_faded()
    {
        var h = new HoverHighlight();
        h.Enter("a", A);
        Run(h, 1.0);
        Assert.Equal(1.0, h.Intensity, 3);

        h.Leave();
        h.Advance(1 / 60.0);
        Assert.True(h.Visible);
        Assert.Equal(A, h.Rect);               // sigue pintándose donde estaba
        Assert.InRange(h.Intensity, 0.01, 0.999);

        Run(h, 2.0);
        Assert.False(h.Visible);
        Assert.Equal(0.0, h.Intensity);
        Assert.False(h.IsAnimating);
    }

    [Fact]
    public void A_to_B_slides_rect_without_fading_out()
    {
        var h = new HoverHighlight();
        h.Enter("a", A);
        Run(h, 1.0);

        h.Enter("b", B);
        h.Advance(1 / 60.0);
        Assert.Equal(1.0, h.Intensity, 3);    // no se apaga entre A y B
        Assert.InRange(h.Rect.Y, A.Y + 0.01f, B.Y - 0.01f); // a medio camino: se desliza
        Assert.InRange(h.Rect.Width, A.Width, B.Width);

        Run(h, 2.0);
        Assert.Equal(B, h.Rect);
        Assert.Equal("b", h.Key);
    }

    [Fact]
    public void Reenter_same_key_cancels_fade()
    {
        var h = new HoverHighlight();
        h.Enter("a", A);
        Run(h, 1.0);
        h.Leave();
        Run(h, 0.03);
        double mid = h.Intensity;
        Assert.InRange(mid, 0.01, 0.999);

        h.Enter("a", A);
        double min = mid;
        for (int i = 0; i < 120; i++) { h.Advance(1 / 60.0); min = Math.Min(min, h.Intensity); }
        Assert.True(min > 0.01);               // nunca llega a apagarse
        Assert.Equal(1.0, h.Intensity, 3);
        Assert.Equal(A, h.Rect);
    }

    [Fact]
    public void Settles_and_stops_animating()
    {
        var h = new HoverHighlight();
        h.Enter("a", A);
        Assert.True(h.IsAnimating);
        Run(h, 3.0);
        Assert.False(h.IsAnimating);
        Assert.Equal(1.0, h.Intensity);
        Assert.Equal(A, h.Rect);
    }

    [Fact]
    public void First_enter_appears_in_place_without_sliding_from_origin()
    {
        var h = new HoverHighlight();
        h.Enter("b", B);
        h.Advance(1 / 60.0);
        Assert.Equal(B, h.Rect);               // nace donde está el elemento
    }

    [Fact]
    public void Snap_variants_jump_to_final_state()
    {
        var h = new HoverHighlight();
        h.Enter("a", A, snap: true);
        Assert.Equal(1.0, h.Intensity);
        Assert.False(h.IsAnimating);
        h.Leave(snap: true);
        Assert.False(h.Visible);
        Assert.False(h.IsAnimating);
    }

    // §4 #4: entra con HoverIn (95 % a ~95 ms) y sale con Rápido (95 % a ~159 ms).
    [Fact]
    public void Enters_with_hover_in_and_leaves_with_rapido()
    {
        var h = new HoverHighlight();
        h.Enter("a", A);
        int ms = 0;
        while (h.Intensity < 0.95 && ms < 1000) { h.Advance(0.001); ms++; }
        Assert.InRange(ms, 89, 101);
        Run(h, 1.0);
        h.Leave();
        ms = 0;
        while (h.Intensity > 0.05 && ms < 1000) { h.Advance(0.001); ms++; }
        Assert.InRange(ms, 153, 165);
    }

    // De A a B con intensidad > 0,3 el rect se desliza con HoverIn; por debajo se encaja y se funde.
    [Fact]
    public void Low_intensity_snaps_the_rect_instead_of_sliding()
    {
        var h = new HoverHighlight();
        h.Enter("a", A);
        Run(h, 1.0);
        h.Leave();
        while (h.Intensity > 0.25) h.Advance(0.001);
        h.Enter("b", B);
        Assert.Equal(B, h.Rect);               // encajado: no viaja desde A
        Assert.True(h.Intensity < 0.3);        // y se funde desde donde estaba
    }

    [Fact]
    public void High_intensity_slide_reaches_95_percent_quickly()
    {
        var h = new HoverHighlight();
        h.Enter("a", A);
        Run(h, 1.0);
        h.Enter("b", B);
        int ms = 0;
        float target95 = A.Y + 0.95f * (B.Y - A.Y);
        while (h.Rect.Y < target95 && ms < 1000) { h.Advance(0.001); ms++; }
        Assert.InRange(ms, 89, 101);           // HoverIn
    }
}
