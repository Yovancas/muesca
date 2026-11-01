using Velopack.Sources;

namespace ClaudeBarWin.Services.Updates;

/// <summary>Un downloader que puede bajar un archivo pequeño con un tope de bytes, sin cargarlo entero antes de mirarlo.</summary>
internal interface IBoundedDownloader
{
    /// <summary>Los bytes de <paramref name="url"/>; lanza <see cref="InvalidDataException"/> en cuanto pasa de
    /// <paramref name="maxBytes"/>.</summary>
    Task<byte[]> DownloadBytes(string url, IDictionary<string, string> headers, int maxBytes);
}

/// <summary>
/// El downloader HTTP de Velopack (paquetes y API, igual que siempre) con <see cref="IBoundedDownloader"/> para el manifiesto
/// y su <c>.sig</c>, que <see cref="SignedGithubSource"/> baja antes de verificar nada: un asset enorme en una release no
/// puede hacer que la app reserve memoria sin límite. Si el servidor dice el tamaño (Content-Length) y pasa del tope, ni se
/// empieza a leer; si no lo dice, se corta al pasarlo.
/// </summary>
internal sealed class BoundedHttpDownloader : HttpClientFileDownloader, IBoundedDownloader
{
    /// <summary>Minutos (la unidad de Velopack) para un archivo pequeño; los paquetes siguen con los 30 de Velopack.</summary>
    private const double SmallFileTimeoutMinutes = 2;

    public async Task<byte[]> DownloadBytes(string url, IDictionary<string, string> headers, int maxBytes)
    {
        using var client = CreateHttpClient(headers, SmallFileTimeoutMinutes);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        CheckDeclaredLength(response.Content.Headers.ContentLength, maxBytes);
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await ReadAtMost(stream, maxBytes).ConfigureAwait(false);
    }

    /// <summary>Lanza si el tamaño que anuncia el servidor ya pasa del tope (sin anunciarlo, se mira al leer).</summary>
    internal static void CheckDeclaredLength(long? contentLength, int maxBytes)
    {
        if (contentLength > maxBytes) throw TooBig(maxBytes);
    }

    /// <summary>Lee <paramref name="stream"/> hasta el final, pero corta (y lanza) en cuanto pasa de <paramref name="maxBytes"/>:
    /// nunca guarda más de eso.</summary>
    internal static async Task<byte[]> ReadAtMost(Stream stream, int maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw TooBig(maxBytes);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    internal static InvalidDataException TooBig(int maxBytes) => new($"pasa de {maxBytes} bytes");
}
