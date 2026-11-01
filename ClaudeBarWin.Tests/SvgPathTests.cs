using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary><see cref="SvgPath"/>: el lector mínimo de trazados SVG (icono de la letra del notch, de Icons8).</summary>
public class SvgPathTests
{
    private static void Near(PointF expected, PointF actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
    }

    [Fact]
    public void Absolute_and_relative_moves_and_lines()
    {
        var s = SvgPath.Parse("M10,10 L20,10 l0,5 H30 h-5 V40 v-10 z");
        Assert.Equal(new[] { SvgSegmentKind.Move, SvgSegmentKind.Line, SvgSegmentKind.Line, SvgSegmentKind.Line,
            SvgSegmentKind.Line, SvgSegmentKind.Line, SvgSegmentKind.Line, SvgSegmentKind.Close }, s.Select(x => x.Kind));
        Near(new(20, 10), s[1].P);
        Near(new(20, 15), s[2].P);
        Near(new(30, 15), s[3].P);
        Near(new(25, 15), s[4].P);
        Near(new(25, 40), s[5].P);
        Near(new(25, 30), s[6].P);
        Near(new(10, 10), s[7].P);   // el cierre vuelve al inicio de la figura
    }

    [Fact]
    public void Implicit_parameters_after_move_are_lines()
    {
        var abs = SvgPath.Parse("M0 0 10 0 10 10");
        Assert.Equal(SvgSegmentKind.Line, abs[1].Kind);
        Near(new(10, 10), abs[2].P);
        var rel = SvgPath.Parse("m5 5 10 0 0 10");
        Near(new(15, 5), rel[1].P);
        Near(new(15, 15), rel[2].P);
    }

    [Fact]
    public void Relative_cubic_is_relative_to_its_start()
    {
        var s = SvgPath.Parse("M10 10c1 2 3 4 5 6");
        Near(new(11, 12), s[1].C1);
        Near(new(13, 14), s[1].C2);
        Near(new(15, 16), s[1].P);
    }

    [Fact]
    public void Smooth_cubic_reflects_the_previous_control_point()
    {
        var s = SvgPath.Parse("M0 0 C0 10 10 10 10 0 S20 -10 20 0");
        Near(new(10, -10), s[2].C1);   // reflejo de (10,10) sobre (10,0)
        Near(new(20, -10), s[2].C2);
        Near(new(20, 0), s[2].P);
        var rel = SvgPath.Parse("M0 0 c0 10 10 10 10 0 s10 -10 10 0");
        Near(new(10, -10), rel[2].C1);
        Near(new(20, -10), rel[2].C2);
        Near(new(20, 0), rel[2].P);
    }

    [Fact]
    public void Smooth_cubic_without_a_previous_cubic_uses_the_current_point()
    {
        var s = SvgPath.Parse("M5 5 L10 10 S20 20 30 10");
        Near(new(10, 10), s[2].C1);
    }

    [Fact]
    public void Glued_numbers_split_at_signs_and_second_dots()
    {
        var s = SvgPath.Parse("M-0.245-1.068L.5.5l1e1-2E-1");
        Near(new(-0.245f, -1.068f), s[0].P);
        Near(new(0.5f, 0.5f), s[1].P);
        Near(new(10.5f, 0.3f), s[2].P);
    }

    [Fact]
    public void After_close_a_relative_move_starts_from_the_figure_start()
    {
        var s = SvgPath.Parse("M10 10 l5 0 z m1 1 l1 0");
        Near(new(11, 11), s[3].P);
        Near(new(12, 11), s[4].P);
    }

    [Theory]
    [InlineData("L0 0")]
    [InlineData("M0 0 Q1 1 2 2")]
    [InlineData("M0 0 L1")]
    [InlineData("M0 0 L1 x")]
    public void Malformed_or_unsupported_paths_throw(string d) =>
        Assert.Throws<FormatException>(() => SvgPath.Parse(d));

    [Fact]
    public void Graphics_path_fits_the_view_box_into_the_target_without_distortion()
    {
        var segs = SvgPath.Parse("M0 0 H50 V25 H0 z");   // 50 × 25 dentro de una caja 50 × 50
        using var path = SvgPath.ToGraphicsPath(segs, new RectangleF(0, 0, 50, 50), new RectangleF(10, 20, 100, 100));
        var b = path.GetBounds();
        Assert.Equal(10f, b.Left, 2);
        Assert.Equal(20f, b.Top, 2);
        Assert.Equal(100f, b.Width, 2);
        Assert.Equal(50f, b.Height, 2);
        Assert.Equal(FillMode.Winding, path.FillMode);
    }

    [Fact]
    public void The_embedded_lyrics_icon_parses_and_paints_a_bubble_with_quote_holes()
    {
        var svg = LyricsIcon.ReadSvg();
        Assert.NotNull(svg);
        Assert.NotNull(LyricsIcon.Segments);
        Assert.Equal(new RectangleF(0, 0, 50, 50), LyricsIcon.ViewBox);
        Assert.Equal(3, LyricsIcon.Segments!.Count(x => x.Kind == SvgSegmentKind.Move));   // bocadillo + dos comillas

        using var bmp = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var path = SvgPath.ToGraphicsPath(LyricsIcon.Segments, LyricsIcon.ViewBox, new RectangleF(0, 0, 100, 100)))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.White);
            g.FillPath(brush, path);
        }
        Assert.Equal(0, bmp.GetPixel(2, 2).A);        // fuera del bocadillo
        Assert.Equal(255, bmp.GetPixel(50, 60).A);    // bocadillo macizo (entre las comillas y el borde de abajo)
        Assert.Equal(0, bmp.GetPixel(39, 41).A);      // hueco de la comilla izquierda (círculo en 19,5·20,5 ×2)
        Assert.Equal(0, bmp.GetPixel(61, 41).A);      // hueco de la comilla derecha (círculo en 30,5·20,5 ×2)
        Assert.Equal(255, bmp.GetPixel(33, 86).A);    // la cola del bocadillo, abajo a la izquierda
    }
}
