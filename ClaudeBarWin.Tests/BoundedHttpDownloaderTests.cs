using ClaudeBarWin.Services.Updates;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El tope de <see cref="BoundedHttpDownloader"/> para el manifiesto y su firma: se corta en cuanto se pasa, sin guardar más.
/// </summary>
public class BoundedHttpDownloaderTests
{
    /// <summary>Un stream que cuenta lo que se le ha leído (para ver que no se lee de más).</summary>
    private sealed class CountingStream(byte[] data) : MemoryStream(data)
    {
        public long Served;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer, cancellationToken);
            Served += read;
            return read;
        }
    }

    [Fact]
    public async Task Up_to_the_limit_reads_everything()
    {
        var data = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();

        Assert.Equal(data, await BoundedHttpDownloader.ReadAtMost(new MemoryStream(data), 1000));
        Assert.Empty(await BoundedHttpDownloader.ReadAtMost(new MemoryStream(), 10));
    }

    [Fact]
    public async Task Past_the_limit_it_stops_without_reading_the_rest()
    {
        // 5 MB servidos sin Content-Length: se corta en el primer trozo que pasa del tope.
        var stream = new CountingStream(new byte[5 * 1024 * 1024]);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => BoundedHttpDownloader.ReadAtMost(stream, 1024));

        Assert.Contains("1024", ex.Message);
        Assert.True(stream.Served <= 1024 + 16 * 1024, $"leídos {stream.Served} bytes");
    }

    [Fact]
    public void A_declared_length_over_the_limit_fails_before_reading()
    {
        BoundedHttpDownloader.CheckDeclaredLength(null, 256);   // sin Content-Length: se mira al leer
        BoundedHttpDownloader.CheckDeclaredLength(256, 256);
        Assert.Throws<InvalidDataException>(() => BoundedHttpDownloader.CheckDeclaredLength(257, 256));
        Assert.Throws<InvalidDataException>(() => BoundedHttpDownloader.CheckDeclaredLength(long.MaxValue, 256));
    }
}
