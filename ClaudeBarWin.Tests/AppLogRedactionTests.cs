using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El log de diagnóstico nunca guarda un token, venga como venga: cabecera Bearer, clave sk-ant-…, JSON (también escapado
/// dentro de otro texto) o query string. Los tokens de aquí son inventados.
/// </summary>
public class AppLogRedactionTests
{
    private const string Oat = "sk-ant-oat01-AbC_d3-xYz";
    private const string Ort = "sk-ant-ort01-QqQ_9-zz";

    [Theory]
    [InlineData("Authorization: Bearer abc.def-123", "Authorization: ***")]
    [InlineData("authorization: bearer eyJhbGciOi.eyJzdWIi.c2lnbmF0dXJl", "authorization: ***")]
    [InlineData("Bearer a+b/c~d==", "***")]                                       // alfabeto completo de RFC 6750
    [InlineData("Authorization: Bearer " + Oat, "Authorization: ***")]
    public void Bearer_headers_are_redacted(string input, string expected)
        => Assert.Equal(expected, AppLog.Redact(input));

    [Theory]
    [InlineData("token " + Oat + " leaked", "token *** leaked")]
    [InlineData("refresh " + Ort, "refresh ***")]
    [InlineData("x-api-key: sk-ant-api03-K3y_v4lue", "x-api-key: ***")]
    public void Anthropic_keys_are_redacted(string input, string expected)
        => Assert.Equal(expected, AppLog.Redact(input));

    [Theory]
    [InlineData("{\"accessToken\": \"secreto\"}", "{\"accessToken\": \"***\"}")]
    [InlineData("{\"refreshToken\":\"r-secreto\",\"expiresAt\":1}", "{\"refreshToken\":\"***\",\"expiresAt\":1}")]
    [InlineData("{\"access_token\":\"a\",\"refresh_token\":\"b\"}", "{\"access_token\":\"***\",\"refresh_token\":\"***\"}")]
    [InlineData("{\"AccessToken\" : \"x\"}", "{\"AccessToken\" : \"***\"}")]
    [InlineData("cuerpo: {\\\"accessToken\\\":\\\"secreto\\\"}", "cuerpo: {\\\"accessToken\\\":\\\"***\\\"}")] // JSON escapado
    [InlineData("POST /token?grant_type=refresh&refresh_token=abc123&x=1", "POST /token?grant_type=refresh&refresh_token=***&x=1")]
    public void Json_and_query_token_values_are_redacted_keeping_the_key(string input, string expected)
        => Assert.Equal(expected, AppLog.Redact(input));

    [Fact]
    public void The_real_credentials_file_shape_leaves_no_secret()
    {
        string json = $$$"""
            {"claudeAiOauth":{"accessToken":"{{{Oat}}}","refreshToken":"{{{Ort}}}","expiresAt":1790000000000,"subscriptionType":"max"}}
            """;
        var red = AppLog.Redact(json);
        Assert.DoesNotContain("sk-ant", red);
        Assert.Contains("\"subscriptionType\":\"max\"", red);   // lo no secreto se queda
        Assert.Contains("\"expiresAt\":1790000000000", red);
    }

    [Theory]
    [InlineData("cuota: Ok → RateLimited (reintento a las 10:05:00Z, nivel 1)")]
    [InlineData("plan: Plan Pro → Plan Max · 5x (profile)")]
    [InlineData("sin accessToken en el archivo")]          // la palabra sola no es un secreto
    [InlineData("{\"accessToken\": \"\"}")]                 // vacío: nada que tapar
    public void Ordinary_messages_are_left_alone(string input)
        => Assert.Equal(input, AppLog.Redact(input));

    [Fact]
    public void Creds_in_a_message_prints_redacted()
        => Assert.Equal("leído Creds(***)", AppLog.Redact($"leído {new Creds(Oat, null)}"));
}

/// <summary>De punta a punta: lo que llega al archivo de log. En serie: <see cref="AppLog"/> es estático.</summary>
[CollectionDefinition("AppLogSerial", DisableParallelization = true)]
public sealed class AppLogSerialCollection { }

[Collection("AppLogSerial")]
public sealed class AppLogFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "claudebar-tests", "log-" + Guid.NewGuid().ToString("N"));
    private readonly bool _wasEnabled = AppLog.Enabled;
    private readonly string _oldPath = AppLog.LogPath;

    public AppLogFileTests()
    {
        Directory.CreateDirectory(_dir);
        AppLog.LogPath = Path.Combine(_dir, "claudebar.log");
        AppLog.Enabled = true;
    }

    public void Dispose()
    {
        AppLog.Enabled = _wasEnabled;
        AppLog.LogPath = _oldPath;
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Exceptions_and_messages_reach_the_file_without_tokens()
    {
        const string token = "sk-ant-oat01-NoDebeLlegar_al-log";
        AppLog.Info($"cabeceras: Authorization: Bearer {token}");
        AppLog.Warn($"{{\"refreshToken\":\"r-{token}\"}}");
        AppLog.Error("fallo", new InvalidOperationException($"respuesta rara con {token}"));

        var text = File.ReadAllText(AppLog.LogPath);
        Assert.DoesNotContain("NoDebeLlegar", text);
        Assert.Contains("fallo: InvalidOperationException", text);
    }
}
