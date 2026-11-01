using System.Drawing;
using System.Drawing.Imaging;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Notch.Platform;

namespace ClaudeBarWin.Tests.Platform;

/// <summary>
/// <see cref="AlbumArt.Process"/>: la carátula que da Windows (PNG/JPEG de cualquier tamaño) → PNG 64×64 recortado
/// al centro + color de acento. Bytes que no son imagen → null, sin excepción.
/// </summary>
public class AlbumArtTests
{
    private static byte[] Encode(int w, int h, Action<Graphics> paint, ImageFormat format)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) paint(g);
        using var ms = new MemoryStream();
        bmp.Save(ms, format);
        return ms.ToArray();
    }

    private static Size DecodedSize(byte[] png)
    {
        using var ms = new MemoryStream(png);
        using var img = Image.FromStream(ms);
        Assert.Equal(ImageFormat.Png.Guid, img.RawFormat.Guid);
        return img.Size;
    }

    [Fact]
    public void Resizes_to_64_png_and_extracts_accent()
    {
        var jpeg = Encode(300, 300, g => g.Clear(Color.FromArgb(200, 20, 20)), ImageFormat.Jpeg);
        var art = AlbumArt.Process(jpeg);
        Assert.NotNull(art);
        Assert.Equal(new Size(AlbumArt.Size, AlbumArt.Size), DecodedSize(art!.Png));
        float hue = art.Accent.GetHue();
        Assert.True(hue < 12 || hue > 348, $"tono {hue}");
    }

    [Fact]
    public void Non_square_art_is_center_cropped()
    {
        // 300×100: laterales rojos y el cuadrado central verde → tras recortar al centro solo queda verde.
        var png = Encode(300, 100, g =>
        {
            g.Clear(Color.FromArgb(220, 0, 0));
            using var b = new SolidBrush(Color.FromArgb(0, 200, 60));
            g.FillRectangle(b, 100, 0, 100, 100);
        }, ImageFormat.Png);
        var art = AlbumArt.Process(png);
        Assert.NotNull(art);
        Assert.Equal(new Size(64, 64), DecodedSize(art!.Png));
        Assert.InRange(art.Accent.GetHue(), 120f, 160f);
    }

    [Fact]
    public void Gray_art_uses_default_accent()
    {
        var png = Encode(64, 64, g => g.Clear(Color.FromArgb(90, 90, 90)), ImageFormat.Png);
        Assert.Equal(AlbumAccent.Default, AlbumArt.Process(png)!.Accent);
    }

    [Fact]
    public void Garbage_bytes_return_null()
    {
        Assert.Null(AlbumArt.Process(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Null(AlbumArt.Process(Array.Empty<byte>()));
    }
}
