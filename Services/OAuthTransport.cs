using System.Net.Http;
using System.Net.Http.Headers;

namespace ClaudeBarWin.Services;

/// <summary>
/// Transporte cerrado de las únicas dos peticiones que llevan el token OAuth de Claude Code
/// (<c>/api/oauth/usage</c> y <c>/api/oauth/profile</c>):
/// <list type="bullet">
/// <item>solo HTTPS a <c>api.anthropic.com</c> (puerto 443, sin usuario en la URL), comprobado ANTES de poner la cabecera
/// y otra vez sobre la URI final de la respuesta;</item>
/// <item>sin redirecciones: el handler no las sigue y cualquier 3xx es un error, así la cabecera nunca viaja al
/// <c>Location</c>;</item>
/// <item>timeout corto, cuerpo acotado a <see cref="MaxResponseBytes"/>, sin cookies ni credenciales de Windows; el
/// proxy es el del sistema, como en el resto de la app;</item>
/// <item>User-Agent propio de ClaudeBar (nunca se hace pasar por <c>claude-cli</c>);</item>
/// <item>la cabecera Authorization se quita de la petición en cuanto llega la respuesta.</item>
/// </list>
/// Los errores son <see cref="HttpRequestException"/> con un texto fijo: ni el token ni el <c>Location</c> van dentro.
/// </summary>
internal static class OAuthTransport
{
    internal const string AllowedHost = "api.anthropic.com";
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    /// <summary>Tope del cuerpo de la respuesta: /usage y /profile ocupan 1–2 KB.</summary>
    internal const int MaxResponseBytes = 64 * 1024;

    private const string ProjectUrl = Brand.RepoUrl;
    internal static string UserAgent => $"ClaudeBarWin/{AppVersion()} (+{ProjectUrl})";

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        UseProxy = true, // sin Proxy propio = HttpClient.DefaultProxy: el del sistema, como hasta ahora
        ConnectTimeout = ConnectTimeout,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>Cliente para las peticiones con token. <paramref name="handler"/> solo para tests (HTTP falso).</summary>
    internal static HttpClient CreateClient(HttpMessageHandler? handler = null) =>
        new(handler ?? CreateHandler(), disposeHandler: true)
        {
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = MaxResponseBytes,
        };

    /// <summary>¿Puede ir el token a esta URI? Solo <c>https://api.anthropic.com</c> en el puerto 443 y sin usuario.</summary>
    internal static bool IsAllowed(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Port == 443
        && uri.UserInfo.Length == 0
        && string.Equals(uri.IdnHost, AllowedHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// GET con el token. Lanza <see cref="HttpRequestException"/> sin enviar nada si el destino no está permitido, y
    /// también si la respuesta es una redirección o viene de otro sitio. El llamador la trata como error de red.
    /// </summary>
    internal static async Task<HttpResponseMessage> GetAsync(HttpClient http, Uri uri, Creds creds, string beta)
    {
        if (!IsAllowed(uri)) throw new HttpRequestException("destino no permitido para el token OAuth");

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.TryAddWithoutValidation("anthropic-beta", beta);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
        HttpResponseMessage? resp = null;
        try
        {
            resp = await http.SendAsync(req).ConfigureAwait(false);
            int code = (int)resp.StatusCode;
            if (code is >= 300 and <= 399)
                throw new HttpRequestException($"redirección rechazada ({code}): el token no sale de {AllowedHost}");
            // Sin redirecciones la URI final es la pedida; se comprueba igual por si un handler las siguiera.
            if (!IsAllowed(resp.RequestMessage?.RequestUri ?? uri))
                throw new HttpRequestException("la respuesta no llegó de " + AllowedHost);
            var ok = resp;
            resp = null;
            return ok;
        }
        finally
        {
            resp?.Dispose();
            req.Headers.Authorization = null; // que el token no siga colgado de la petición (ni de resp.RequestMessage)
        }
    }

    private static string AppVersion()
    {
        var v = typeof(OAuthTransport).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
