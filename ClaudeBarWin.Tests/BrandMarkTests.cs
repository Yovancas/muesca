using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El logo de Muesca pintado en vector (<see cref="BrandMark"/>, <see cref="MuescaMarkLayout"/>): que la geometría siga a
/// branding/muesca.svg a cualquier tamaño y que lo pintado tenga los colores de la marca donde tocan.
/// </summary>
public class BrandMarkTests
{
    // ---------------------------------------------------------------- geometría (pura)

    [Fact]
    public void At_canvas_size_the_layout_is_the_svg()
    {
        var m = MuescaMarkLayout.For(new RectangleF(0, 0, 512, 512));
        Assert.Equal(new RectangleF(24, 24, 464, 464), m.Tile);
        Assert.Equal(112f, m.TileRadius, 3);
        Assert.Equal(96f, m.NotchLeft, 3);
        Assert.Equal(416f, m.NotchRight, 3);
        Assert.Equal(360f, m.NotchBottom, 3);
        Assert.Equal(110f, m.NotchRadius, 3);
        Assert.Equal(new PointF(256, 212), m.RingCenter);
        Assert.Equal(96f, m.OuterRadius, 3);
        Assert.Equal(34f, m.OuterStroke, 3);
        Assert.Equal(48f, m.InnerRadius, 3);
        Assert.Equal(26f, m.InnerStroke, 3);
    }

    [Fact]
    public void The_arcs_cover_the_dashes_of_the_svg()
    {
        var m = MuescaMarkLayout.For(new RectangleF(0, 0, 512, 512));
        // stroke-dasharray 410 de 2π·96 ≈ 603 y 110 de 2π·48 ≈ 302.
        Assert.Equal(244.7f, m.OuterSweep, 1);
        Assert.Equal(131.3f, m.InnerSweep, 1);
        Assert.InRange(m.OuterSweep, 180f, 270f);   // el grande pasa de la mitad, sin cerrarse
        Assert.InRange(m.InnerSweep, 90f, 180f);
    }

    [Fact]
    public void The_notch_starts_on_the_rounded_corner_of_the_tile()
    {
        var m = MuescaMarkLayout.For(new RectangleF(0, 0, 512, 512));
        // x = 96 corta el arco de la esquina (centro 136, 136; radio 112) en y = 136 − √(112² − 40²) ≈ 31,4.
        Assert.Equal(31.39f, m.NotchTop, 2);
        float cx = m.Tile.Left + m.TileRadius, cy = m.Tile.Top + m.TileRadius;
        float dist = MathF.Sqrt(MathF.Pow(m.NotchLeft - cx, 2) + MathF.Pow(m.NotchTop - cy, 2));
        Assert.Equal(m.TileRadius, dist, 2);
        Assert.InRange(m.NotchCornerAngle, 240f, 260f);   // entre la izquierda (180°) y lo alto (270°), cerca de lo alto
    }

    [Theory]
    [InlineData(16f)]
    [InlineData(96f)]
    [InlineData(300f)]
    public void The_layout_scales_with_the_box(float side)
    {
        var a = MuescaMarkLayout.For(new RectangleF(0, 0, 512, 512));
        var b = MuescaMarkLayout.For(new RectangleF(10, 20, side, side));
        float k = side / 512f;
        Assert.Equal(10 + a.Tile.Left * k, b.Tile.Left, 3);
        Assert.Equal(20 + a.NotchBottom * k, b.NotchBottom, 3);
        Assert.Equal(a.OuterStroke * k, b.OuterStroke, 3);
        Assert.Equal(a.OuterSweep, b.OuterSweep, 3);   // los ángulos no dependen del tamaño
        Assert.Equal(a.NotchCornerAngle, b.NotchCornerAngle, 2);
    }

    [Fact]
    public void A_box_that_is_not_square_centers_the_biggest_square()
    {
        var m = MuescaMarkLayout.For(new RectangleF(0, 0, 200, 100));
        Assert.Equal(100f * 464f / 512f, m.Tile.Width, 3);
        Assert.Equal(100f, m.Tile.Left + m.Tile.Width / 2f, 3);
        Assert.Equal(50f, m.Tile.Top + m.Tile.Height / 2f, 3);
    }

    [Fact]
    public void The_rings_sit_inside_the_notch()
    {
        // El anillo grande, con su trazo, cabe entre los lados del notch y por encima de su borde de abajo.
        var m = MuescaMarkLayout.For(new RectangleF(0, 0, 512, 512));
        float outer = m.OuterRadius + m.OuterStroke / 2f;
        Assert.True(m.RingCenter.X - outer > m.NotchLeft);
        Assert.True(m.RingCenter.X + outer < m.NotchRight);
        Assert.True(m.RingCenter.Y + outer < m.NotchBottom);
    }

    // ---------------------------------------------------------------- pintado

    private static Bitmap Paint(int side, float alpha = 1f)
    {
        var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        BrandMark.Paint(g, new RectangleF(0, 0, side, side), alpha);
        return bmp;
    }

    private static Color At(Bitmap bmp, float svgX, float svgY) =>
        bmp.GetPixel((int)(svgX / 512f * bmp.Width), (int)(svgY / 512f * bmp.Height));

    private static bool Near(Color a, Color b, int tol) =>
        Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol;

    [Theory]
    [InlineData(48)]
    [InlineData(96)]
    [InlineData(256)]
    public void Paints_the_brand_colors_where_the_svg_has_them(int side)
    {
        using var bmp = Paint(side);
        // Fuera del cuadrado (la esquina redondeada), nada.
        Assert.Equal(0, At(bmp, 30, 30).A);
        // El fondo: violeta abajo a la izquierda (mitad del degradado diagonal) y azul en la esquina de abajo a la derecha.
        var low = At(bmp, 440, 440);
        Assert.Equal(255, low.A);
        Assert.True(low.B > 200 && low.G > low.R, $"azul abajo a la derecha: {low}");
        var left = At(bmp, 60, 200);
        Assert.True(left.B > 200 && left.R > left.G, $"violeta a la izquierda: {left}");
        // El notch: negro de la marca entre los anillos y el borde de abajo.
        Assert.True(Near(At(bmp, 256, 340), Brand.Ink, 12), $"notch: {At(bmp, 256, 340)}");
        // El hueco del centro de los anillos también es el negro del notch.
        Assert.True(Near(At(bmp, 256, 212), Brand.Ink, 12), $"centro: {At(bmp, 256, 212)}");
        // Anillo grande a las 3 (dentro del arco): el degradado lavanda → azul, claro.
        var ring = At(bmp, 352, 212);
        Assert.True(ring.B > 220 && ring.R > 60 && ring.G > 120, $"anillo grande: {ring}");
        // Anillo grande a las 9 (fuera del arco: pasó de largo): la pista oscura.
        Assert.True(Near(At(bmp, 160, 212), Brand.RingTrack, 24), $"pista del grande: {At(bmp, 160, 212)}");
        // Anillo pequeño a las 3: blanco.
        var inner = At(bmp, 304, 212);
        Assert.True(inner.R > 220 && inner.G > 220 && inner.B > 220, $"anillo pequeño: {inner}");
    }

    [Fact]
    public void The_notch_is_open_at_the_top_edge_of_the_tile()
    {
        // El notch cuelga del borde de arriba: en lo alto del cuadrado, en el centro, ya es negro (no hay borde de color).
        using var bmp = Paint(256);
        Assert.True(Near(At(bmp, 256, 28), Brand.Ink, 16), $"arriba en el centro: {At(bmp, 256, 28)}");
        // A los lados del notch, en lo alto, el degradado.
        Assert.True(At(bmp, 60, 120).B > 200, "fondo a la izquierda del notch");
    }

    [Fact]
    public void Edges_are_antialiased()
    {
        // Vector con suavizado: en el borde redondeado hay píxeles a medio alfa (no un recorte duro).
        using var bmp = Paint(96);
        bool partial = false;
        for (int x = 0; x < 30 && !partial; x++)
            for (int y = 0; y < 30 && !partial; y++)
            {
                int a = bmp.GetPixel(x, y).A;
                partial = a > 20 && a < 235;
            }
        Assert.True(partial);
    }

    [Fact]
    public void Alpha_fades_everything_and_zero_paints_nothing()
    {
        using var half = Paint(96, 0.5f);
        int a = half.GetPixel(48, 90).A;
        Assert.InRange(a, 110, 145);
        using var none = Paint(96, 0f);
        for (int y = 0; y < 96; y += 4)
            for (int x = 0; x < 96; x += 4)
                Assert.Equal(0, none.GetPixel(x, y).A);
    }

    [Fact]
    public void Leaves_the_graphics_as_it_was()
    {
        using var bmp = new Bitmap(64, 64);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TranslateTransform(3f, 4f);
        BrandMark.Paint(g, new RectangleF(0, 0, 48, 48));
        Assert.Equal(SmoothingMode.None, g.SmoothingMode);
        Assert.Equal(PixelOffsetMode.Half, g.PixelOffsetMode);
        using var transform = g.Transform;
        Assert.Equal(3f, transform.OffsetX);
        Assert.Equal(4f, transform.OffsetY);
    }

    [Fact]
    public void Tiny_or_empty_boxes_paint_nothing_and_do_not_throw()
    {
        using var bmp = new Bitmap(8, 8);
        using var g = Graphics.FromImage(bmp);
        BrandMark.Paint(g, RectangleF.Empty);
        BrandMark.Paint(g, new RectangleF(0, 0, 1, 1));
        Assert.Equal(0, bmp.GetPixel(0, 0).A);
    }

    // ---------------------------------------------------------------- caché (BrandMarkCache)

    private static Bitmap PaintCached(BrandMarkCache cache, int side, float alpha = 1f, float scale = 1f)
    {
        var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        cache.Paint(g, new RectangleF(0, 0, side, side), alpha, scale);
        return bmp;
    }

    /// <summary>La mayor diferencia entre dos imágenes, en valores premultiplicados (en los bordes casi transparentes el color
    /// suelto no significa nada).</summary>
    private static int WorstDifference(Bitmap a, Bitmap b)
    {
        int worst = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                int Pm(int c, int alpha) => c * alpha / 255;
                worst = Math.Max(worst, Math.Abs(p.A - q.A));
                worst = Math.Max(worst, Math.Abs(Pm(p.R, p.A) - Pm(q.R, q.A)));
                worst = Math.Max(worst, Math.Abs(Pm(p.G, p.A) - Pm(q.G, q.A)));
                worst = Math.Max(worst, Math.Abs(Pm(p.B, p.A) - Pm(q.B, q.A)));
            }
        return worst;
    }

    [Theory]
    [InlineData(16)]
    [InlineData(48)]
    [InlineData(120)]
    public void The_cached_mark_is_the_vector_one(int side)
    {
        using var vector = Paint(side);
        using var cache = new BrandMarkCache();
        using var cached = PaintCached(cache, side);
        Assert.True(WorstDifference(vector, cached) <= 3, $"{side} px: difiere en {WorstDifference(vector, cached)}");
    }

    [Fact]
    public void Every_frame_reuses_one_bitmap_per_size_and_dpi()
    {
        using var cache = new BrandMarkCache();
        using var bmp = new Bitmap(260, 200, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        // Una animación: se mueve (en posiciones fraccionarias) y cambia de alfa, siempre a 96 px y 1×.
        for (int i = 1; i <= 30; i++) cache.Paint(g, new RectangleF(10.3f + i * 1.7f, 20.6f, 96f, 96f), i / 30f, 1f);
        Assert.Equal(1, cache.Renders);
        // Otro DPI (125 %): otro bitmap, una vez.
        cache.Paint(g, new RectangleF(0, 0, 120f, 120f), 1f, 1.25f);
        cache.Paint(g, new RectangleF(3, 4, 120f, 120f), 0.5f, 1.25f);
        Assert.Equal(2, cache.Renders);
    }

    [Fact]
    public void The_key_is_the_pixel_size_and_the_dpi()
    {
        Assert.Equal((96, 96, 120), BrandMarkCache.KeyFor(new RectangleF(5.5f, 7f, 95.6f, 96.4f), 1.25f));
        Assert.Equal((16, 16, 96), BrandMarkCache.KeyFor(new RectangleF(0, 0, 16f, 16f), 1f));
        Assert.NotEqual(BrandMarkCache.KeyFor(new RectangleF(0, 0, 32f, 32f), 1f), BrandMarkCache.KeyFor(new RectangleF(0, 0, 32f, 32f), 2f));
    }

    [Fact]
    public void Changing_the_dpi_and_closing_free_the_bitmap()
    {
        var cache = new BrandMarkCache();
        using (PaintCached(cache, 48)) Assert.True(cache.HasBitmap);
        cache.Invalidate();   // lo que hace la bienvenida al cambiar de escala
        Assert.False(cache.HasBitmap);
        using (PaintCached(cache, 48)) Assert.True(cache.HasBitmap);
        Assert.Equal(2, cache.Renders);

        cache.Dispose();
        Assert.False(cache.HasBitmap);
        // Cerrado, sigue pintando (en vector) pero no crea otro bitmap que nadie liberaría.
        using var after = PaintCached(cache, 48);
        Assert.False(cache.HasBitmap);
        Assert.Equal(2, cache.Renders);
        Assert.True(At(after, 256, 440).A > 200);   // el cuadrado, abajo en el centro
        cache.Dispose();   // dos veces, sin error
    }

    [Fact]
    public void With_a_scaling_transform_it_paints_in_vector()
    {
        using var cache = new BrandMarkCache();
        using var bmp = new Bitmap(96, 96, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.ScaleTransform(2f, 2f);   // copiar el bitmap aquí lo emborronaría
            cache.Paint(g, new RectangleF(0, 0, 48, 48));
        }
        Assert.Equal(0, cache.Renders);
        Assert.False(cache.HasBitmap);
        Assert.True(At(bmp, 256, 440).A > 200);   // el cuadrado, abajo en el centro
    }

    [Fact]
    public void A_faded_cached_mark_is_translucent_and_the_graphics_is_left_as_it_was()
    {
        using var cache = new BrandMarkCache();
        using var full = PaintCached(cache, 64);
        using var half = PaintCached(cache, 64, alpha: 0.5f);
        var a = At(full, 256, 440).A;
        var b = At(half, 256, 440).A;
        Assert.InRange(b, a / 2 - 8, a / 2 + 8);
        Assert.Equal(1, cache.Renders);   // el alfa no rehace el bitmap

        using var bmp = new Bitmap(64, 64);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.None;
        cache.Paint(g, new RectangleF(0, 0, 64, 64), 0.7f);
        Assert.Equal(InterpolationMode.HighQualityBicubic, g.InterpolationMode);
        Assert.Equal(PixelOffsetMode.None, g.PixelOffsetMode);
    }
}
