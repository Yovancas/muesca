using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ClaudeBarWin.Services.Telemetry;

/// <summary>Manda un aviso y devuelve el código HTTP, o null si no hubo respuesta (red, DNS, tiempo agotado).</summary>
public interface IUsageStatsSender : IDisposable
{
    Task<int?> SendAsync(UsagePingPayload payload, CancellationToken ct);
}

/// <summary>Interruptores de entorno del contador. PURO.</summary>
public static class UsageStatsEnvironment
{
    /// <summary>Otro destino (para pruebas). HTTPS, o HTTP solo a la propia máquina.</summary>
    public const string UrlVariable = "CLAUDEBAR_STATS_URL";
    /// <summary><c>1</c> = apagado, diga lo que diga el ajuste.</summary>
    public const string OffVariable = "CLAUDEBAR_NO_STATS";
    /// <summary>En compilaciones Debug no se manda nada salvo con <c>1</c>.</summary>
    public const string DebugVariable = "CLAUDEBAR_STATS_DEBUG";

#if DEBUG
    public const bool IsDebugBuild = true;
#else
    public const bool IsDebugBuild = false;
#endif

    public static bool Allows(Func<string, string?> env, bool debugBuild)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (env(OffVariable)?.Trim() == "1") return false;
        return !debugBuild || env(DebugVariable)?.Trim() == "1";
    }
}

/// <summary>
/// El cliente del contador: su propio <see cref="HttpClient"/>, HTTPS solo a <see cref="DefaultUrl"/> (o al de
/// <see cref="UsageStatsEnvironment.UrlVariable"/>), 10 s de tiempo máximo, sin cookies, sin redirecciones y con el
/// User-Agent <c>ClaudeBar</c> a secas. El cuerpo es <see cref="UsagePingPayload.ToJson"/> y nada más.
/// </summary>
public sealed class UsageStatsClient : IUsageStatsSender
{
    public const string DefaultUrl = "https://claudebar-stats.claudebar-stats-worker.workers.dev/v1/ping";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public UsageStatsClient(Uri endpoint, HttpMessageHandler? handler = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        handler ??= new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            // Una petición al día (o a la hora): la conexión no se queda abierta entre comprobaciones.
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout };
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClaudeBar", null));
        _http.DefaultRequestHeaders.ExpectContinue = false;
    }

    /// <summary>
    /// Destino: sin variable, <see cref="DefaultUrl"/>; con ella, una URL absoluta HTTPS o HTTP a la propia máquina
    /// (loopback). Cualquier otra cosa, null: no se manda nada.
    /// </summary>
    public static Uri? ResolveEndpoint(string? overrideUrl)
    {
        if (string.IsNullOrWhiteSpace(overrideUrl)) return new Uri(DefaultUrl);
        if (!Uri.TryCreate(overrideUrl.Trim(), UriKind.Absolute, out var uri)) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (uri.Scheme == Uri.UriSchemeHttps) return uri;
        if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) return uri;
        return null;
    }

    public async Task<int?> SendAsync(UsagePingPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(payload.ToJson(), Encoding.UTF8, "application/json"),
            Version = HttpVersion.Version11,
        };
        try
        {
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return (int)resp.StatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
