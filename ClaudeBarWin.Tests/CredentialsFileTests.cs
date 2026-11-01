using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Lectura de <c>.credentials.json</c> con lista blanca: del bloque <c>claudeAiOauth</c> solo salen el token de acceso y su
/// caducidad (y, para el plan, los campos no secretos). Siempre en una carpeta temporal: NUNCA el archivo real del usuario.
/// </summary>
public sealed class CredentialsFileTests : IDisposable
{
    private const string Token = "sk-ant-oat01-SECRETO_de_prueba-123";
    private const string Refresh = "sk-ant-ort01-REFRESCO_de_prueba-456";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "claudebar-tests", "creds-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public CredentialsFileTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, ".credentials.json");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // Forma real del archivo de Claude Code, con el refreshToken DELANTE del token y claves que no interesan.
    private const string RealShape = $$$"""
        {"mcpOAuth":{"srv":{"accessToken":"otro","nested":[1,{"a":"b"}]}},
         "claudeAiOauth":{"refreshToken":"{{{Refresh}}}","accessToken":"{{{Token}}}","expiresAt":1790000000000,
                          "scopes":["user:inference","user:profile"],"subscriptionType":"max","rateLimitTier":"default_claude_max_5x"}}
        """;

    [Fact]
    public void ReadToken_takes_only_the_access_token_and_its_expiry()
    {
        File.WriteAllText(_path, RealShape);
        var c = CredentialsFile.ReadToken(_path);
        Assert.NotNull(c);
        Assert.Equal(Token, c!.AccessToken);
        Assert.Equal(1790000000000, c.ExpiresAtMs);
    }

    [Fact]
    public void Creds_has_no_room_for_the_refresh_token()
    {
        var members = typeof(Creds).GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => m.Name);
        Assert.DoesNotContain(members, n => n.Contains("refresh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Creds_never_prints_the_token()
    {
        File.WriteAllText(_path, RealShape);
        var c = CredentialsFile.ReadToken(_path)!;
        Assert.Equal("Creds(***)", c.ToString());
        Assert.Equal("token: Creds(***)", $"token: {c}");
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(c));
        Assert.DoesNotContain(Token, JsonSerializer.Serialize<object>(c));
        Assert.Equal("Creds(***)", typeof(Creds).GetCustomAttribute<DebuggerDisplayAttribute>()!.Value);
    }

    [Fact]
    public void Creds_is_not_a_record_with_a_generated_ToString()
    {
        // Un record generaría PrintMembers con el token dentro (y un ToString que lo llama).
        Assert.Null(typeof(Creds).GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Null(typeof(Creds).GetMethod("<Clone>$"));
    }

    [Fact]
    public void Reading_does_not_touch_the_file_nor_leave_anything_on_disk()
    {
        File.WriteAllText(_path, RealShape);
        var written = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_path, written);

        Assert.NotNull(CredentialsFile.ReadToken(_path));
        Assert.NotNull(CredentialsFile.ReadState(_path));

        Assert.Equal(RealShape, File.ReadAllText(_path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(_path));
        Assert.Equal(new[] { _path }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void Plan_state_reads_the_plan_and_whether_there_is_a_token()
    {
        File.WriteAllText(_path, RealShape);
        var st = CredentialsFile.ReadState(_path)!;
        Assert.Equal(new PlanInfo("max", "default_claude_max_5x"), st.Plan);
        Assert.True(st.HasToken);
        Assert.DoesNotContain(Token, st.ToString());
    }

    [Fact]
    public void Utf8_bom_and_escaped_keys_are_understood()
    {
        File.WriteAllBytes(_path, Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("{\"claudeAiOauth\":{\"access\\u0054oken\":\"" + Token + "\"}}")).ToArray());
        var c = CredentialsFile.ReadToken(_path);
        Assert.Equal(Token, c?.AccessToken);
        Assert.Null(c!.ExpiresAtMs);
    }

    [Theory]
    [InlineData("")]                                                         // vacío
    [InlineData("[]")]                                                       // no es un objeto
    [InlineData("{}")]                                                       // /logout: sin bloque
    [InlineData("{\"claudeAiOauth\":null}")]
    [InlineData("{\"claudeAiOauth\":{\"accessToken\":\"\"}}")]               // token vacío = sin sesión
    [InlineData("{\"claudeAiOauth\":{\"accessToken\":42}}")]                 // de otro tipo
    [InlineData("{\"claudeAiOauth\":{\"refreshToken\":\"r\"}}")]             // solo el de refresco: no se usa
    [InlineData("{\"claudeAiOauth\":{\"accessToken\":\"sk-ant-oat01-x\"")]   // a medio escribir
    [InlineData("{\"claudeAiOauth\":{\"accessToken\":\"sk-ant-oat01-x\"}} basura")]
    public void Anything_but_a_usable_token_is_no_token(string content)
    {
        File.WriteAllText(_path, content);
        Assert.Null(CredentialsFile.ReadToken(_path));
    }

    [Fact]
    public void A_missing_file_is_no_token()
        => Assert.Null(CredentialsFile.ReadToken(Path.Combine(_dir, "no-existe.json")));

    [Fact]
    public void A_huge_file_is_not_read()
    {
        var pad = new string(' ', CredentialsFile.MaxBytes);
        File.WriteAllText(_path, "{\"claudeAiOauth\":{\"accessToken\":\"" + Token + "\"}}" + pad);
        Assert.Null(CredentialsFile.ReadToken(_path));
        Assert.Null(CredentialsFile.ReadState(_path));
    }

    [Fact]
    public void Expiry_that_is_not_an_integer_is_ignored_but_the_token_is_kept()
    {
        File.WriteAllText(_path, "{\"claudeAiOauth\":{\"expiresAt\":\"mañana\",\"accessToken\":\"" + Token + "\"}}");
        var c = CredentialsFile.ReadToken(_path);
        Assert.Equal(Token, c?.AccessToken);
        Assert.Null(c!.ExpiresAtMs);
    }

    [Fact]
    public void The_token_is_read_while_claude_code_holds_the_file_open_for_writing()
    {
        File.WriteAllText(_path, RealShape);
        using var writer = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal(Token, CredentialsFile.ReadToken(_path)?.AccessToken);
    }

    [Fact]
    public void Plan_state_cache_opens_the_file_only_when_claude_code_rewrites_it()
    {
        File.WriteAllText(_path, RealShape);
        File.SetLastWriteTimeUtc(_path, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        var cache = new CredentialsStateCache(() => _path);

        var first = cache.Read()!;
        for (int i = 0; i < 5; i++) Assert.Same(first, cache.Read());   // cada tick del temporizador
        Assert.Equal(1, cache.Reads);

        File.WriteAllText(_path, RealShape.Replace("\"max\"", "\"pro\""));   // /login a otro plan
        File.SetLastWriteTimeUtc(_path, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        Assert.Equal("pro", cache.Read()!.Plan.SubscriptionType);
        Assert.Equal(2, cache.Reads);
    }

    [Fact]
    public void Plan_state_cache_tells_signed_out_and_does_not_remember_an_unreadable_file()
    {
        var cache = new CredentialsStateCache(() => _path);
        Assert.False(cache.Read()!.HasToken);                 // sin archivo: sesión cerrada
        Assert.Equal(0, cache.Reads);

        File.WriteAllText(_path, "{\"claudeAiOauth\":{\"subscri");   // a medio escribir
        Assert.Null(cache.Read());
        File.WriteAllText(_path, RealShape);                  // misma fecha posible: no se quedó con el null
        Assert.True(cache.Read()!.HasToken);
    }

    [Fact]
    public void Stamp_changes_when_the_file_is_rewritten_and_is_null_without_file()
    {
        Assert.Null(CredentialsFile.Stamp(_path));
        File.WriteAllText(_path, RealShape);
        File.SetLastWriteTimeUtc(_path, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        var a = CredentialsFile.Stamp(_path);
        File.SetLastWriteTimeUtc(_path, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        Assert.NotEqual(a, CredentialsFile.Stamp(_path));
    }
}
