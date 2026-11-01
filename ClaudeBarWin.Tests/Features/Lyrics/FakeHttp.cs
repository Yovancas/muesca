using System.Net;
using System.Text;

namespace ClaudeBarWin.Tests.Features;

/// <summary>Handler HTTP falso para las fuentes de letras: nada sale a la red. Guarda cada petición.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

    /// <summary>Responde según la URL (sin la parte de consulta salvo que la regla la incluya).</summary>
    public FakeHttp(Func<Uri, HttpResponseMessage> respond) : this((req, _) => Task.FromResult(respond(req.RequestUri!)))
    {
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) Requests.Add(request);
        return _respond(request, ct);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("") };

    public static HttpResponseMessage Redirect(string to)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found) { Content = new StringContent("") };
        r.Headers.Location = new Uri(to);
        return r;
    }

    /// <summary>Respuesta que dice venir de otro host (como si un handler hubiera seguido una redirección).</summary>
    public static HttpResponseMessage FromOtherHost(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/x"),
        };

    public static HttpResponseMessage TooBig() => Json("\"" + new string('a', (1 << 20) + 10) + "\"");

    /// <summary>Parámetros de la consulta de una URL, ya decodificados.</summary>
    public static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.Length > 1 ? p[1].Replace('+', ' ') : ""));
}
