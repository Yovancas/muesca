using System.Net;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// HTTP de las fuentes de letras, con las mismas garantías que <see cref="LrclibClient"/>: un único host (se comprueba
/// antes de cada petición y en la respuesta), solo HTTPS, sin seguir redirecciones (un 3xx es un fallo), timeout por
/// petición (8 s) y respuesta de como mucho <see cref="LrclibClient.MaxResponseBytes"/>. User-Agent de ClaudeBar.
/// <para><see cref="GetAsync"/> devuelve el estado y el cuerpo de cualquier respuesta que no sea 3xx (cada fuente decide
/// qué hacer con un 404 o un 401) y lanza <see cref="LyricsProviderException"/> si la red falla. La cancelación del
/// llamador se propaga como <see cref="OperationCanceledException"/>. Ningún mensaje lleva la URL.</para>
/// </summary>
internal sealed class LyricsHttp : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;

    public LyricsHttp(string host, HttpMessageHandler? handler, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        Host = host;
        _http = handler is null
            ? new HttpClient(CreateDefaultHandler(), disposeHandler: true)
            : new HttpClient(handler, disposeHandler: false);
        _http.MaxResponseContentBufferSize = LrclibClient.MaxResponseBytes;   // más grande → HttpRequestException → fallo
        _http.Timeout = Timeout.InfiniteTimeSpan;   // el plazo lo pone cada petición (distingue timeout de cancelación)
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(LrclibClient.UserAgent);
        _timeout = timeout;
    }

    public string Host { get; }

    /// <summary>El de <see cref="LrclibClient"/> (sin seguir redirecciones) y además sin cookies: ninguna fuente las
    /// necesita y así una búsqueda no arrastra estado de la anterior.</summary>
    internal static SocketsHttpHandler CreateDefaultHandler()
    {
        var h = LrclibClient.CreateDefaultHandler();
        h.UseCookies = false;
        return h;
    }

    public async Task<(HttpStatusCode Status, string Body)> GetAsync(Uri uri, CancellationToken ct)
    {
        if (!IsAllowed(uri)) throw new LyricsProviderException($"petición a un host no permitido (se esperaba {Host})");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            using var resp = await _http.GetAsync(uri, cts.Token).ConfigureAwait(false);
            // Defensa extra por si un handler siguiera redirecciones: la respuesta tiene que venir del mismo host.
            if (resp.RequestMessage?.RequestUri is { } final && !IsAllowed(final))
                throw new LyricsProviderException($"respuesta de un host no permitido (se esperaba {Host})");
            int code = (int)resp.StatusCode;
            if (code is >= 300 and < 400) throw new LyricsProviderException($"redirección no permitida (HTTP {code})");
            var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return (resp.StatusCode, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LyricsProviderException($"sin respuesta en {_timeout.TotalSeconds:0.#} s");
        }
        catch (HttpRequestException ex)
        {
            throw new LyricsProviderException("fallo de red o respuesta demasiado grande", ex);
        }
    }

    public void Dispose() => _http.Dispose();

    private bool IsAllowed(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase);
}
