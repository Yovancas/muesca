using System.Net;
using System.Text.Json;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Cliente de LRCLIB (<see cref="LyricsQuery.SearchUrl"/>): el ÚNICO destino de red del notch. Solo se le
/// llama con la página de Música abierta y el ajuste de letras activo (lo decide la UI).
///
/// <list type="bullet">
/// <item>Prueba los candidatos de <see cref="LyricsQuery.Candidates"/> en orden; en cada respuesta elige la
/// entrada con <c>syncedLyrics</c> (no instrumental) de duración más cercana; empate → la primera.</item>
/// <item>Sin letra sincronizada en ninguno → <see cref="LyricsStatus.NotFound"/>, sin reintentos y en caché.</item>
/// <item>Fallo de red, HTTP ≠ 2xx/404, JSON roto o timeout (8 s por petición) → <see cref="LyricsStatus.Error"/>:
/// hasta 2 reintentos a los 3 s. Los errores no se guardan en caché.</item>
/// <item>Caché LRU de 20 pistas en memoria por (título, artista) normalizados.</item>
/// <item>La cancelación del llamador (cambio de canción) se propaga como <see cref="OperationCanceledException"/>.</item>
/// <item>Único host garantizado: no se siguen redirecciones (<c>AllowAutoRedirect = false</c>; un 3xx es un error), solo
/// se pide a <c>https://lrclib.net</c> y se comprueba el host de la petición que produjo la respuesta. Respuesta
/// de como mucho <see cref="MaxResponseBytes"/> (más grande → error).</item>
/// </list>
/// </summary>
public sealed class LrclibClient : ILyricsSource, IDisposable
{
    public const int CacheSize = 20;
    public const int Retries = 2;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private const string ProjectUrl = Brand.RepoUrl;

    /// <summary>Tope del cuerpo de cada respuesta (1 MB); una búsqueda real de LRCLIB pesa unas decenas de KB.</summary>
    public const int MaxResponseBytes = 1 << 20;

    /// <summary>El único host al que se habla (el de <see cref="LyricsQuery.SearchUrl"/>).</summary>
    public static readonly string Host = new Uri(LyricsQuery.SearchUrl).Host;

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly object _gate = new();
    private readonly LinkedList<(string Key, LyricsResult Result)> _lru = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, LyricsResult Result)>> _index = new();

    public LrclibClient(HttpMessageHandler? handler = null)
        : this(handler, DefaultTimeout, Task.Delay)
    {
    }

    /// <summary>Para tests: timeout por petición y espera entre reintentos inyectables.</summary>
    internal LrclibClient(HttpMessageHandler? handler, TimeSpan timeout, Func<TimeSpan, CancellationToken, Task> delay)
    {
        // Sin redirecciones: lrclib.net no puede reenviar la consulta (título y artista) a otro host.
        _http = handler is null
            ? new HttpClient(CreateDefaultHandler(), disposeHandler: true)
            : new HttpClient(handler, disposeHandler: false);
        _http.MaxResponseContentBufferSize = MaxResponseBytes;   // más grande → HttpRequestException → error
        _http.Timeout = Timeout.InfiniteTimeSpan;   // el plazo lo pone cada petición (distingue timeout de cancelación)
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _timeout = timeout;
        _delay = delay;
    }

    /// <summary>User-Agent de ClaudeBar para las fuentes de letras (también lo usan las de <c>Features/Lyrics</c>).</summary>
    internal static string UserAgent => $"ClaudeBarWin/{AppVersion()} (+{ProjectUrl})";

    /// <summary>Handler de la app: sin seguir redirecciones (un 3xx llega como tal y se trata como error).</summary>
    internal static SocketsHttpHandler CreateDefaultHandler() => new() { AllowAutoRedirect = false };

    public async Task<LyricsResult> FetchAsync(string title, string artist, TimeSpan duration, CancellationToken ct)
    {
        var urls = LyricsQuery.Candidates(title, artist);
        if (urls.Count == 0) return LyricsResult.NotFound;

        var key = Normalize(title) + "\u001F" + Normalize(artist);
        if (TryGetCached(key, out var cached)) return cached;

        for (int attempt = 0; attempt <= Retries; attempt++)
        {
            if (attempt > 0) await _delay(RetryDelay, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var r = await OnePassAsync(urls, duration, ct).ConfigureAwait(false);
            if (r.Status != LyricsStatus.Error)
            {
                Remember(key, r);
                return r;
            }
        }
        return LyricsResult.Error;
    }

    public void Dispose() => _http.Dispose();

    private async Task<LyricsResult> OnePassAsync(IReadOnlyList<string> urls, TimeSpan duration, CancellationToken ct)
    {
        foreach (var url in urls)
        {
            var body = await GetAsync(url, ct).ConfigureAwait(false);
            if (body is null) return LyricsResult.Error;      // la red falla para todos: no seguir con otros candidatos
            if (body.Length == 0) continue;                   // 404

            string? synced;
            try { synced = PickSynced(body, duration); }
            catch (JsonException) { return LyricsResult.Error; }

            if (synced is null) continue;
            var lines = LrcParser.Parse(synced);
            if (lines.Count > 0) return new LyricsResult(LyricsStatus.Found, lines);
        }
        return LyricsResult.NotFound;
    }

    // Cuerpo de la respuesta, "" si 404, null si error (red, HTTP, timeout, otro host, respuesta demasiado grande).
    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsLrclib(uri)) return null;   // nunca a otro host
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            using var resp = await _http.GetAsync(uri, cts.Token).ConfigureAwait(false);
            // Defensa extra por si un handler siguiera redirecciones: la respuesta tiene que venir de lrclib.net.
            if (resp.RequestMessage?.RequestUri is { } final && !IsLrclib(final)) return null;
            if (resp.StatusCode == HttpStatusCode.NotFound) return "";
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // timeout propio
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static bool IsLrclib(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase);

    // La entrada sincronizada de duración más cercana (empate → la primera); null si no hay ninguna.
    private static string? PickSynced(string json, TimeSpan duration)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("se esperaba un array");

        string? best = null;
        double bestDiff = double.MaxValue;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (e.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True) continue;
            if (!e.TryGetProperty("syncedLyrics", out var s) || s.ValueKind != JsonValueKind.String) continue;
            var text = s.GetString();
            if (string.IsNullOrWhiteSpace(text)) continue;

            double diff = 0;
            if (duration > TimeSpan.Zero)
                diff = e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
                    ? Math.Abs(d.GetDouble() - duration.TotalSeconds)
                    : double.MaxValue / 2;
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = text;
            }
        }
        return best;
    }

    private bool TryGetCached(string key, out LyricsResult result)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                result = node.Value.Result;
                return true;
            }
        }
        result = LyricsResult.NotFound;
        return false;
    }

    private void Remember(string key, LyricsResult result)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var old)) _lru.Remove(old);
            _index[key] = _lru.AddFirst((key, result));
            while (_lru.Count > CacheSize)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _index.Remove(last.Value.Key);
            }
        }
    }

    private static string Normalize(string s) => (s ?? "").Trim().ToLowerInvariant();

    private static string AppVersion()
    {
        var v = typeof(LrclibClient).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
