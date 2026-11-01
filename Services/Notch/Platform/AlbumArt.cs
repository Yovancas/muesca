using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>Carátula lista para pintar: PNG de <see cref="AlbumArt.Size"/>² y su color de acento.</summary>
public sealed record AlbumArtResult(byte[] Png, Color Accent);

/// <summary>
/// Carátula que entrega Windows (PNG/JPEG de cualquier tamaño) → PNG 64×64 recortado al centro (como
/// <c>cover</c>) + acento de <see cref="AlbumAccent"/> sobre esos mismos 64×64 píxeles (barato y estable).
/// GDI+ y nada más; bytes que no son una imagen → null, sin excepción.
/// </summary>
public static class AlbumArt
{
    public const int Size = 64;

    /// <summary>Tope de lo que se acepta decodificar (una carátula real ronda decenas de KB).</summary>
    public const int MaxInputBytes = 4 * 1024 * 1024;

    public static AlbumArtResult? Process(byte[] encoded)
    {
        if (encoded is null || encoded.Length == 0 || encoded.Length > MaxInputBytes) return null;
        try
        {
            using var input = new MemoryStream(encoded, writable: false);
            using var src = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
            if (src.Width <= 0 || src.Height <= 0) return null;

            using var dst = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                int side = Math.Min(src.Width, src.Height);
                var from = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
                using var wrap = new ImageAttributes();
                wrap.SetWrapMode(WrapMode.TileFlipXY);   // sin halo en los bordes al reescalar
                g.DrawImage(src, new Rectangle(0, 0, Size, Size), from.X, from.Y, from.Width, from.Height,
                    GraphicsUnit.Pixel, wrap);
            }

            var accent = Accent(dst);
            using var output = new MemoryStream();
            dst.Save(output, ImageFormat.Png);
            return new AlbumArtResult(output.ToArray(), accent);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            return null;   // GDI+ da ArgumentException con datos inválidos y OutOfMemory con formatos raros
        }
    }

    private static Color Accent(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = bmp.Width * 4;
            var px = new byte[rowBytes * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, px, y * rowBytes, rowBytes);
            return AlbumAccent.FromBgra(px, bmp.Width, bmp.Height);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
