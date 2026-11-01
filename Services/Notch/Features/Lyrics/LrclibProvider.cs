using System.Net;
using System.Text.Json;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// LRCLIB como <see cref="ILyricsProvider"/> (letras v2 · B2). Misma API y garantías de red que
/// <see cref="LrclibClient"/> (host <see cref="LrclibClient.Host"/>, sin redirecciones, 8 s, 1 MB), pero devuelve TODOS
/// los resultados de <c>/api/search</c>: los sincronizados (<c>syncedLyrics</c>) y los que solo tienen letra plana
/// (<c>plainLyrics</c>). Se saltan las instrumentales y las entradas sin texto.
/// <list type="bullet">
/// <item>Primero <c>track_name</c> (+ <c>artist_name</c> si hay artista); si no hay nada, búsqueda libre <c>q=</c>.</item>
/// <item>503/429 (LRCLIB ocupado, visto en ráfagas de búsquedas): un único reintento tras <see cref="BusyRetryDelay"/>.</item>
/// <item>404 → nada. Otro HTTP no 2xx, JSON roto, timeout, otro host o 3xx → <see cref="LyricsProviderException"/>.</item>
/// <item>Sin caché ni más reintentos: los pone quien la use (agregador y caché en disco).</item>
/// </list>
/// </summary>
public sealed class LrclibProvider : ILyricsProvider, IDisposable
{
    public const string SourceName = "LRCLIB";

    /// <summary>Pausa antes de repetir una búsqueda que LRCLIB contestó con 503/429.</summary>
    public static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(1);

    private readonly LyricsHttp _http;
    private readonly TimeSpan _busyDelay;

    public LrclibProvider(HttpMessageHandler? handler = null)
        : this(handler, LrclibClient.DefaultTimeout)
    {
    }

    /// <summary>Para tests: timeout por petición y pausa del reintento inyectables.</summary>
    internal LrclibProvider(HttpMessageHandler? handler, TimeSpan timeout, TimeSpan? busyRetryDelay = null)
    {
        _http = new LyricsHttp(LrclibClient.Host, handler, timeout);
        _busyDelay = busyRetryDelay ?? BusyRetryDelay;
    }

    public string Name => SourceName;

    public async Task<IReadOnlyList<LyricsCandidate>> SearchAsync(SearchCandidate q, TimeSpan duration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var title = (q.Title ?? "").Trim();
        var artist = (q.Artist ?? "").Trim();
        if (title.Length == 0) return Array.Empty<LyricsCandidate>();

        var exact = artist.Length == 0
            ? $"{LyricsQuery.SearchUrl}?track_name={Uri.EscapeDataString(title)}"
            : $"{LyricsQuery.SearchUrl}?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        var found = await SearchUrlAsync(exact, ct).ConfigureAwait(false);
        if (found.Count > 0) return found;

        var free = $"{LyricsQuery.SearchUrl}?q={Uri.EscapeDataString((title + " " + artist).Trim())}";
        return await SearchUrlAsync(free, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

    private async Task<IReadOnlyList<LyricsCandidate>> SearchUrlAsync(string url, CancellationToken ct)
    {
        var uri = new Uri(url);
        var (status, body) = await _http.GetAsync(uri, ct).ConfigureAwait(false);
        if (status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
        {
            await Task.Delay(_busyDelay, ct).ConfigureAwait(false);
            (status, body) = await _http.GetAsync(uri, ct).ConfigureAwait(false);
        }
        if (status == HttpStatusCode.NotFound) return Array.Empty<LyricsCandidate>();
        if ((int)status is < 200 or >= 300) throw new LyricsProviderException($"LRCLIB respondió HTTP {(int)status}");
        try
        {
            return Parse(body);
        }
        catch (JsonException ex)
        {
            throw new LyricsProviderException("LRCLIB devolvió un JSON inválido", ex);
        }
    }

    private static List<LyricsCandidate> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("se esperaba un array");

        var list = new List<LyricsCandidate>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (e.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True) continue;

            var id = e.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number
                ? i.GetRawText()
                : Str(e, "id") ?? "";
            var title = Str(e, "trackName") ?? "";
            var artist = Str(e, "artistName") ?? "";
            var duration = e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.GetDouble() > 0
                ? TimeSpan.FromSeconds(d.GetDouble())
                : TimeSpan.Zero;

            if (Str(e, "syncedLyrics") is { } synced && !string.IsNullOrWhiteSpace(synced))
                list.Add(new LyricsCandidate(SourceName, id, title, artist, duration, true, synced));
            else if (Str(e, "plainLyrics") is { } plain && !string.IsNullOrWhiteSpace(plain))
                list.Add(new LyricsCandidate(SourceName, id, title, artist, duration, false, plain));
        }
        return list;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
