using System.Drawing.Drawing2D;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class ShapesTests
{
    [Fact]
    public void FillRounded_with_nonpositive_width_does_not_throw()
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(Color.Red);

        // Width <= 0 (y altura <= 0): no debe lanzar, simplemente no pinta nada.
        var ex = Record.Exception(() => Shapes.FillRounded(g, brush, new Rectangle(0, 0, 0, 0), 5));
        Assert.Null(ex);
        var ex2 = Record.Exception(() => Shapes.FillRounded(g, brush, new Rectangle(0, 0, -10, -10), 5));
        Assert.Null(ex2);
    }

    [Fact]
    public void FillRounded_with_radius_le_one_falls_back_to_rectangle_without_throwing()
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(Color.Red);

        // radius <= 1 cae a rectángulo plano (sin arcos) y no debe lanzar.
        var ex = Record.Exception(() => Shapes.FillRounded(g, brush, new Rectangle(0, 0, 1, 1), 1));
        Assert.Null(ex);
    }

    [Fact]
    public void RoundedRectPath_returns_a_non_null_path()
    {
        using GraphicsPath path = Shapes.RoundedRectPath(new Rectangle(0, 0, 10, 10), 4);
        Assert.NotNull(path);
        Assert.True(path.PointCount > 0);
    }

    [Fact]
    public void Contrast_picks_white_on_black_and_black_on_white()
    {
        Assert.Equal(Color.White, ColorMath.Contrast(Color.Black));
        Assert.Equal(Color.Black, ColorMath.Contrast(Color.White));
    }
}
