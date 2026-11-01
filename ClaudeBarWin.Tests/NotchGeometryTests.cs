using System.Drawing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary><see cref="NotchGeometry"/>: pastilla pegada arriba y centrada, zona de clic con margen 4·s y mínimo.</summary>
public class NotchGeometryTests
{
    [Fact]
    public void HitRect_is_centered_top_with_4s_margin()
    {
        // s = 1: pastilla 150×30 en una superficie 700×200 → zona 158×34 centrada arriba.
        var hit = NotchGeometry.HitRect(150, 30, 1f, new Size(700, 200));
        Assert.Equal(new Rectangle(271, 0, 158, 34), hit);

        // s = 1,5: pastilla 225×45 en 1050×300 → 237×51; centrado al píxel (sin perder la pastilla).
        var pill = NotchGeometry.PillRect(225, 45, new Size(1050, 300));
        var hit15 = NotchGeometry.HitRect(225, 45, 1.5f, new Size(1050, 300));
        Assert.Equal(0, hit15.Top);
        Assert.Equal(51, hit15.Height);
        Assert.InRange(hit15.Width, 237, 238);
        Assert.True(hit15.Left <= pill.Left - 6f && hit15.Right >= pill.Right + 6f);
        Assert.InRange(Math.Abs((hit15.Left + hit15.Right) / 2f - 525f), 0f, 0.5f);
    }

    [Fact]
    public void HitRect_has_minimum_size_in_Minimal()
    {
        // Minimal 44×4: la raya es fina, pero el ratón tiene que poder encontrarla → 100·s × 10·s.
        Assert.Equal(new Rectangle(300, 0, 100, 10), NotchGeometry.HitRect(44, 4, 1f, new Size(700, 200)));
        Assert.Equal(new Rectangle(600, 0, 200, 20), NotchGeometry.HitRect(88, 8, 2f, new Size(1400, 400)));
    }

    [Fact]
    public void HitRect_is_empty_when_hidden_and_clamped_to_surface()
    {
        Assert.Equal(Rectangle.Empty, NotchGeometry.HitRect(0, 0, 1f, new Size(700, 200)));
        var big = NotchGeometry.HitRect(900, 300, 1f, new Size(700, 200));
        Assert.Equal(new Rectangle(0, 0, 700, 200), big);
    }

    [Fact]
    public void PillPath_bounds_match_rect()
    {
        foreach (var (rect, radius) in new[]
        {
            (new RectangleF(275, 0, 150, 30), NotchLayout.CornerRadius(150, 1f)),   // Idle (radio > alto/2)
            (new RectangleF(328, 0, 44, 4), NotchLayout.CornerRadius(44, 1f)),      // Minimal
            (new RectangleF(90, 0, 520, 150), NotchLayout.CornerRadius(520, 1f)),   // Expanded
            (new RectangleF(10.5f, 0, 80.25f, 20), 0f),                              // sin esquinas
        })
        {
            using var path = NotchGeometry.PillPath(rect, radius);
            var b = path.GetBounds();
            Assert.Equal(rect.X, b.X, 2);
            Assert.Equal(rect.Y, b.Y, 2);
            Assert.Equal(rect.Width, b.Width, 2);
            Assert.Equal(rect.Height, b.Height, 2);
        }
    }

    [Fact]
    public void PillPath_is_square_on_top_and_rounded_below()
    {
        var r = new RectangleF(100, 0, 300, 60);
        using var path = NotchGeometry.PillPath(r, 20);
        // A 2 px de cada esquina (GDI+ rasteriza el borde al píxel): arriba dentro, abajo fuera del arco.
        Assert.True(path.IsVisible(r.Left + 2f, r.Top + 2f));          // esquina superior: recta
        Assert.True(path.IsVisible(r.Right - 2f, r.Top + 2f));
        Assert.False(path.IsVisible(r.Left + 2f, r.Bottom - 2f));      // esquina inferior: redondeada
        Assert.False(path.IsVisible(r.Right - 2f, r.Bottom - 2f));
        Assert.True(path.IsVisible(r.Left + r.Width / 2, r.Bottom - 1f));
    }

    [Fact]
    public void Ears_widen_only_the_top_edge()
    {
        var r = new RectangleF(100, 0, 300, 60);
        using var path = NotchGeometry.PillPath(r, 20, ears: 8);
        var b = path.GetBounds();
        Assert.Equal(r.Left - 8, b.Left, 2);
        Assert.Equal(r.Right + 8, b.Right, 2);
        Assert.Equal(r.Bottom, b.Bottom, 2);
        Assert.True(path.IsVisible(r.Left - 1.5f, 1f));               // la oreja toca el borde
        Assert.False(path.IsVisible(r.Left - 2f, 6f));                // y es cóncava
        Assert.False(path.IsVisible(r.Left - 2f, 30f));               // el lateral sigue recto
    }

    [Fact]
    public void Ears_never_exceed_the_straight_side()
    {
        // Minimal 44×4: radio 2 → la oreja cabe en los 2 px de lateral que quedan.
        var r = new RectangleF(328, 0, 44, 4);
        using var path = NotchGeometry.PillPath(r, 14, ears: 4.5f);
        var b = path.GetBounds();
        Assert.Equal(r.Left - 2, b.Left, 2);
        Assert.Equal(r.Bottom, b.Bottom, 2);
    }

    [Fact]
    public void HitTest_follows_the_rounded_contour_with_4s_margin()
    {
        var surface = new Size(700, 200);
        var pill = NotchGeometry.PillRect(300, 60, surface);    // 200..500 × 0..60, radio 30
        Assert.True(NotchGeometry.HitTest(new Point(350, 30), 300, 60, 1f, surface));
        Assert.True(NotchGeometry.HitTest(new Point(197, 10), 300, 60, 1f, surface));   // margen lateral
        Assert.True(NotchGeometry.HitTest(new Point(350, 63), 300, 60, 1f, surface));   // margen inferior
        Assert.False(NotchGeometry.HitTest(new Point(350, 65), 300, 60, 1f, surface));
        Assert.False(NotchGeometry.HitTest(new Point((int)pill.Left - 1, (int)pill.Bottom + 2), 300, 60, 1f, surface)); // esquina
        Assert.True(NotchGeometry.HitTest(new Point(330, 8), 44, 4, 1f, surface));    // Minimal: zona mínima
        Assert.False(NotchGeometry.HitTest(new Point(350, 1), 0, 0, 1f, surface));     // Hidden
    }

    [Fact]
    public void PillRect_is_centered_on_top_edge()
    {
        Assert.Equal(new RectangleF(275, 0, 150, 30), NotchGeometry.PillRect(150, 30, new Size(700, 200)));
    }

    // ---------------------------------------------------------------- puntos de página (F5 · B2)

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Page_dots_are_centered_at_the_bottom_with_a_fixed_pitch(float s)
    {
        var pill = NotchGeometry.PillRect(480 * s, 140 * s, new Size((int)(700 * s), (int)(200 * s)));
        var c0 = NotchGeometry.PageDotCenter(pill, s, 4, 0);
        var c3 = NotchGeometry.PageDotCenter(pill, s, 4, 3);
        Assert.Equal(pill.Left + pill.Width / 2f, (c0.X + c3.X) / 2f, 2);                 // grupo centrado
        Assert.Equal(3 * NotchGeometry.PageDotPitch * s, c3.X - c0.X, 2);
        Assert.Equal(c0.Y, c3.Y, 3);
        // Dentro de la fila de puntos, justo encima del margen inferior.
        float rowTop = pill.Bottom - (NotchLayout.ExpandedPadBottom + NotchLayout.PageDotsRow) * s;
        Assert.InRange(c0.Y, rowTop, pill.Bottom - NotchLayout.ExpandedPadBottom * s);
        // La píldora indicadora (16 px) no toca el punto vecino (6 px).
        Assert.True(NotchGeometry.PageDotPitch - NotchGeometry.PageIndicatorWidth / 2f - NotchGeometry.PageDotDiameter / 2f >= 4f);
    }

    [Fact]
    public void PageDotAt_hits_each_dot_cell_and_nothing_else()
    {
        var pill = NotchGeometry.PillRect(480, 140, new Size(700, 200));
        for (int i = 0; i < 4; i++)
        {
            var c = NotchGeometry.PageDotCenter(pill, 1f, 4, i);
            Assert.Equal(i, NotchGeometry.PageDotAt(Point.Round(c), pill, 1f, 4));
            // Un poco a los lados todavía es su celda (los puntos de 6 px serían imposibles de acertar).
            Assert.Equal(i, NotchGeometry.PageDotAt(new Point((int)c.X + 8, (int)c.Y + 3), pill, 1f, 4));
        }
        var first = NotchGeometry.PageDotCenter(pill, 1f, 4, 0);
        Assert.Equal(-1, NotchGeometry.PageDotAt(new Point((int)(first.X - 30), (int)first.Y), pill, 1f, 4));
        Assert.Equal(-1, NotchGeometry.PageDotAt(new Point((int)first.X, (int)(pill.Top + 20)), pill, 1f, 4));
        Assert.Equal(-1, NotchGeometry.PageDotAt(Point.Round(first), pill, 1f, 1));        // una sola página: sin puntos
        Assert.Equal(-1, NotchGeometry.PageDotAt(Point.Round(first), RectangleF.Empty, 1f, 4));
    }

    /// <summary>Cambio de página (bug del 30-oct): mientras la pastilla encoge, el cursor que estaba sobre la forma vieja
    /// sigue «dentro» (unión de la vieja y la nueva); sin retención, solo cuenta la forma animada.</summary>
    [Fact]
    public void HitTest_with_a_hover_hold_is_the_union_of_old_and_new_shapes()
    {
        var surface = new Size(800, 400);
        var oldShape = new SizeF(420, 300);
        var newShape = new SizeF(420, 120);
        var cursor = new Point(400, 250);                 // sobre la página vieja, debajo de la nueva
        Assert.False(NotchGeometry.HitTest(cursor, 420, 200, 1f, surface));                  // a media animación: fuera
        var hold = HoverHold.Start(default, oldShape) with { To = newShape };
        Assert.True(NotchGeometry.HitTest(cursor, 420, 200, 1f, surface, hold));
        Assert.False(NotchGeometry.HitTest(cursor, 420, 200, 1f, surface, default));
        Assert.False(NotchGeometry.HitTest(new Point(400, 330), 420, 200, 1f, surface, hold)); // fuera de las dos

        // Página que crece: la nueva también cuenta aunque la animada aún no llegue.
        var grow = HoverHold.Start(default, newShape) with { To = oldShape };
        Assert.True(NotchGeometry.HitTest(cursor, 420, 140, 1f, surface, grow));
    }

    [Fact]
    public void Chained_page_changes_keep_the_largest_starting_shape()
    {
        var first = HoverHold.Start(default, new SizeF(420, 300)) with { To = new SizeF(420, 120) };
        var second = HoverHold.Start(first, new SizeF(400, 200));
        Assert.Equal(new SizeF(420, 300), second.From);
        Assert.True(second.Active);
        Assert.False(default(HoverHold).Active);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(2f)]
    public void Page_arrows_sit_beside_the_dots_with_a_24px_scaled_hit_area(float s)
    {
        var pill = new RectangleF(100 * s, 0, 480 * s, 160 * s);
        const int n = 4;
        var prev = NotchGeometry.PageArrowHitRect(pill, s, n, PageArrow.Previous);
        var next = NotchGeometry.PageArrowHitRect(pill, s, n, PageArrow.Next);
        Assert.True(prev.Width >= 24f * s - 0.01f && prev.Height >= 24f * s - 0.01f);
        Assert.Equal(prev.Size, next.Size);
        // Misma fila que los puntos y simétricas respecto al centro.
        var dot = NotchGeometry.PageDotCenter(pill, s, n, 0);
        Assert.Equal(dot.Y, prev.Top + prev.Height / 2f, 3);
        Assert.Equal(pill.Left + pill.Width / 2f, (prev.Left + next.Right) / 2f, 2);
        // No pisan las celdas de los puntos ni se salen de la pastilla.
        float cellsLeft = dot.X - NotchGeometry.PageDotPitch * s / 2f;
        float cellsRight = NotchGeometry.PageDotCenter(pill, s, n, n - 1).X + NotchGeometry.PageDotPitch * s / 2f;
        Assert.True(prev.Right <= cellsLeft && next.Left >= cellsRight);
        Assert.True(prev.Left >= pill.Left && next.Right <= pill.Right && prev.Bottom <= pill.Bottom);

        Assert.Equal(PageArrow.Previous, NotchGeometry.PageArrowAt(Point.Truncate(new PointF(prev.Left + 1, prev.Top + 1)), pill, s, n));
        Assert.Equal(PageArrow.Next, NotchGeometry.PageArrowAt(Point.Truncate(new PointF(next.Right - 2, next.Bottom - 2)), pill, s, n));
        Assert.Equal(PageArrow.None, NotchGeometry.PageArrowAt(Point.Round(dot), pill, s, n));           // un punto, no una flecha
        Assert.Equal(-1, NotchGeometry.PageDotAt(Point.Truncate(new PointF(prev.Left + prev.Width / 2, prev.Top + prev.Height / 2)), pill, s, n));
    }

    [Fact]
    public void No_page_arrows_with_one_page_or_a_pill_too_small()
    {
        var pill = new RectangleF(100, 0, 480, 160);
        var c = NotchGeometry.PageArrowCenter(pill, 1f, 4, PageArrow.Next);
        Assert.Equal(PageArrow.None, NotchGeometry.PageArrowAt(Point.Round(c), pill, 1f, 1));
        Assert.Equal(PageArrow.None, NotchGeometry.PageArrowAt(Point.Round(c), RectangleF.Empty, 1f, 4));
        Assert.Equal(PageArrow.None, NotchGeometry.PageArrowAt(new Point(340, 5), new RectangleF(100, 0, 480, 20), 1f, 4));
    }
}
