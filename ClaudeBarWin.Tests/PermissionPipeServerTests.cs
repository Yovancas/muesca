using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="PermissionPipeServer"/>: pipe dúplex de permisos. Por conexión: valida que el cliente desciende de
/// Claude Code, lee UNA petición, la registra en el <see cref="PermissionBroker"/> y escribe la respuesta solo si
/// hay decisión explícita; si no, cierra sin escribir (el helper sale sin decisión). Cada test usa un pipe propio.
/// </summary>
public class PermissionPipeServerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static string Unique() => "claudebar-perm-test-" + Guid.NewGuid().ToString("N");

    private static readonly IReadOnlyList<ProcessInfo> FromClaude = new[]
    {
        new ProcessInfo(100, "pwsh.exe", DateTime.UtcNow),
        new ProcessInfo(99, "claude.exe", DateTime.UtcNow),
        new ProcessInfo(98, "WindowsTerminal.exe", DateTime.UtcNow),
    };

    private static readonly IReadOnlyList<ProcessInfo> NotFromClaude = new[]
    {
        new ProcessInfo(100, "testhost.exe", DateTime.UtcNow),
        new ProcessInfo(98, "explorer.exe", DateTime.UtcNow),
    };

    private static PermissionPipeServer Start(string name, PermissionBroker broker, IReadOnlyList<ProcessInfo>? ancestors = null)
    {
        var server = new PermissionPipeServer(broker, name, _ => ancestors ?? FromClaude);
        server.Start();
        return server;
    }

    private static string Request(string id, string session = "s1") =>
        $$"""{"v":1,"request_id":"{{id}}","session_id":"{{session}}","cwd":"C:\\repo\\demo","tool":"Bash","tool_input_preview":"echo hola","preview_complete":true}""";

    /// <summary>Cliente como el del helper: dúplex, solo del usuario actual (valida el dueño del pipe).</summary>
    private static async Task<NamedPipeClientStream> Connect(string name)
    {
        var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await c.ConnectAsync(3000);
        return c;
    }

    private static async Task Send(Stream s, string line)
    {
        await s.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        await s.FlushAsync();
    }

    /// <summary>Todo lo que mande el servidor hasta que cierre (o <see cref="Wait"/>). Cierre sin escribir = "".</summary>
    private static async Task<string> ReadToEnd(Stream s)
    {
        using var cts = new CancellationTokenSource(Wait);
        var ms = new MemoryStream();
        var buf = new byte[256];
        try
        {
            int n;
            while ((n = await s.ReadAsync(buf, cts.Token)) > 0) ms.Write(buf, 0, n);
        }
        catch (IOException) { /* pipe roto = cerrado */ }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task<bool> Until(Func<bool> cond)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Wait)
        {
            if (cond()) return true;
            await Task.Delay(10);
        }
        return cond();
    }

    [Fact]
    public async Task Allow_round_trip_writes_the_exact_response_line()
    {
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        using var server = Start(name, broker);
        using var c = await Connect(name);

        await Send(c, Request("r-allow"));
        Assert.True(await Until(() => broker.Pending.Count == 1));
        var p = broker.Pending[0];
        Assert.Equal(("r-allow", "s1", "demo", "Bash", "echo hola"), (p.RequestId, p.SessionId, p.ProjectName, p.Tool, p.Preview));

        Assert.True(broker.Resolve("r-allow", PermissionVerdict.Allow, DateTime.UtcNow));
        Assert.Equal("""{"v":1,"request_id":"r-allow","decision":"allow"}""" + "\n", await ReadToEnd(c));
    }

    [Fact]
    public async Task Deny_round_trip()
    {
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        using var server = Start(name, broker);
        using var c = await Connect(name);

        await Send(c, Request("r-deny"));
        Assert.True(await Until(() => broker.Pending.Count == 1));
        broker.Resolve("r-deny", PermissionVerdict.Deny, DateTime.UtcNow);
        Assert.Equal("""{"v":1,"request_id":"r-deny","decision":"deny"}""" + "\n", await ReadToEnd(c));
    }

    [Fact]
    public async Task Server_closes_without_writing_on_None()
    {
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        using var server = Start(name, broker);
        using var c = await Connect(name);

        await Send(c, Request("r-none"));
        Assert.True(await Until(() => broker.Pending.Count == 1));
        broker.CancelSession("s1");                     // p. ej. el usuario contestó en la terminal
        Assert.Equal("", await ReadToEnd(c));
    }

    [Fact]
    public async Task Ttl_expiry_closes_without_writing()
    {
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromMilliseconds(400));
        using var server = Start(name, broker);
        using var c = await Connect(name);

        var sw = Stopwatch.StartNew();
        await Send(c, Request("r-ttl"));
        Assert.Equal("", await ReadToEnd(c));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"tardó {sw.Elapsed}");
        Assert.Empty(broker.Pending);
        Assert.False(broker.Resolve("r-ttl", PermissionVerdict.Allow, DateTime.UtcNow));   // clic tardío: sin efecto
    }

    [Fact]
    public async Task Server_rejects_client_not_descending_from_claude()
    {
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        int registered = 0;
        broker.Changed += () => Interlocked.Increment(ref registered);
        using var server = Start(name, broker, NotFromClaude);
        using var c = await Connect(name);

        try { await Send(c, Request("r-evil")); } catch (IOException) { /* el servidor ya colgó */ }
        Assert.Equal("", await ReadToEnd(c));
        Assert.Equal(0, Volatile.Read(ref registered));
    }

    [Fact]
    public void Claude_descendant_check_uses_the_process_tree_rule()
    {
        Assert.True(PermissionPipeServer.IsClaudeDescendant(FromClaude));
        Assert.False(PermissionPipeServer.IsClaudeDescendant(NotFromClaude));
        Assert.False(PermissionPipeServer.IsClaudeDescendant(Array.Empty<ProcessInfo>()));
    }

    [Fact]
    public async Task Client_leaving_cancels_its_request()
    {
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        using var server = Start(name, broker);
        var c = await Connect(name);

        await Send(c, Request("r-gone"));
        Assert.True(await Until(() => broker.Pending.Count == 1));
        c.Dispose();                                     // Claude Code mató el helper (su timeout) o siguió sin él

        Assert.True(await Until(() => broker.Pending.Count == 0));
        Assert.False(broker.Resolve("r-gone", PermissionVerdict.Allow, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"v":2,"request_id":"r","session_id":"s","cwd":"c","tool":"Bash","tool_input_preview":""}""")]
    [InlineData("""{"v":1,"session_id":"s","cwd":"c","tool":"Bash","tool_input_preview":""}""")]
    [InlineData("""{"v":1,"request_id":"","session_id":"s","cwd":"c","tool":"Bash"}""")]
    [InlineData("""[1,2]""")]
    public async Task Malformed_request_is_closed_without_registering(string line)
    {
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        int changes = 0;
        broker.Changed += () => Interlocked.Increment(ref changes);
        using var server = Start(name, broker);
        using var c = await Connect(name);

        await Send(c, line);
        Assert.Equal("", await ReadToEnd(c));
        Assert.Equal(0, Volatile.Read(ref changes));
    }

    [Fact]
    public async Task Two_simultaneous_requests_are_independent()
    {
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        using var server = Start(name, broker);
        using var a = await Connect(name);
        using var b = await Connect(name);

        await Send(a, Request("r-a", "sa"));
        await Send(b, Request("r-b", "sb"));
        Assert.True(await Until(() => broker.Pending.Count == 2));

        broker.Resolve("r-b", PermissionVerdict.Deny, DateTime.UtcNow);
        Assert.Equal("""{"v":1,"request_id":"r-b","decision":"deny"}""" + "\n", await ReadToEnd(b));
        Assert.Equal(new[] { "r-a" }, broker.Pending.Select(p => p.RequestId));

        broker.Resolve("r-a", PermissionVerdict.Allow, DateTime.UtcNow);
        Assert.Equal("""{"v":1,"request_id":"r-a","decision":"allow"}""" + "\n", await ReadToEnd(a));
    }

    [Fact]
    public async Task Squatted_pipe_is_never_joined()
    {
        // Otro proceso del usuario crea antes el pipe: el servidor NO se une a él como segunda instancia
        // (FirstPipeInstance), así un cliente nunca acaba hablando con el pipe del intruso creyendo que es ClaudeBar
        // ni ClaudeBar recibe conexiones de un pipe ajeno.
        var name = Unique();
        using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        using var server = Start(name, broker);
        await Task.Delay(300);                           // tiempo de sobra para que el servidor intente crear su instancia

        using var first = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        await first.ConnectAsync(2000);                  // lo atiende el intruso (su única instancia)
        await squatter.WaitForConnectionAsync().WaitAsync(Wait);

        using var second = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        await Assert.ThrowsAsync<TimeoutException>(() => second.ConnectAsync(300));   // ninguna instancia nuestra
    }

    [Fact]
    public void TryParseRequest_truncates_preview_and_derives_project_name()
    {
        var at = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var longPreview = new string('x', 500);
        var line = $$"""{"v":1,"request_id":"r1","session_id":"s1","cwd":"C:\\work\\api","tool":"Bash","tool_input_preview":"{{longPreview}}"}""";

        Assert.True(PermissionPipeServer.TryParseRequest(line, at, out var info));
        Assert.Equal("api", info!.ProjectName);
        Assert.Equal(at, info.ReceivedUtc);
        Assert.True(info.Preview!.Length <= PermissionPipeServer.MaxPreviewChars);
    }

    [Theory]
    [InlineData("""{"v":1,"request_id":"r-x","session_id":"s","cwd":"c","tool":"Bash","tool_input_preview":"echo hola","preview_complete":false}""")]
    [InlineData("""{"v":1,"request_id":"r-x","session_id":"s","cwd":"c","tool":"Bash","tool_input_preview":"echo hola"}""")]
    [InlineData("""{"v":1,"request_id":"r-x","session_id":"s","cwd":"c","tool":"Bash","tool_input_preview":"echo hola","preview_complete":"true"}""")]
    [InlineData("""{"v":1,"request_id":"r-x","session_id":"s","cwd":"c","tool":"Write","tool_input_preview":"C:\\a.txt","preview_complete":true}""")]
    [InlineData("""{"v":1,"request_id":"r-x","session_id":"s","cwd":"c","tool":"mcp__github__push","tool_input_preview":"x","preview_complete":true}""")]
    [InlineData("""{"v":1,"request_id":"r-x","session_id":"s","cwd":"c","tool":"Bash","tool_input_preview":"","preview_complete":true}""")]
    public async Task Request_without_complete_preview_is_closed_at_once_without_registering(string line)
    {
        // Sin vista previa completa no hay botones: el helper tiene que soltar YA (Claude Code enseña su diálogo), sin
        // esperar al plazo del broker, y el notch ni se entera (el aviso de solo lectura lo pone el hook de sesión).
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        int changes = 0;
        broker.Changed += () => Interlocked.Increment(ref changes);
        using var server = Start(name, broker);
        using var c = await Connect(name);

        var sw = Stopwatch.StartNew();
        await Send(c, line);
        Assert.Equal("", await ReadToEnd(c));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"tardó {sw.ElapsedMilliseconds} ms");
        Assert.Equal(0, Volatile.Read(ref changes));
    }

    [Fact]
    public async Task Hidden_notch_answers_at_once_and_pending_requests_are_released()
    {
        // Notch oculto en pantalla completa: la pendiente se suelta sin decisión y la nueva no espera los 24 s.
        var name = Unique();
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        using var server = Start(name, broker);
        using var pending = await Connect(name);
        await Send(pending, Request("r-pend"));
        Assert.True(await Until(() => broker.Pending.Count == 1));

        broker.SetAccepting(false);
        Assert.Equal("", await ReadToEnd(pending));                           // cerrada sin escribir nada
        Assert.Empty(broker.Pending);

        using var late = await Connect(name);
        var sw = Stopwatch.StartNew();
        await Send(late, Request("r-late"));
        Assert.Equal("", await ReadToEnd(late));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"tardó {sw.ElapsedMilliseconds} ms");

        broker.SetAccepting(true);                                            // vuelve a verse
        using var again = await Connect(name);
        await Send(again, Request("r-again"));
        Assert.True(await Until(() => broker.Pending.Count == 1));
        broker.Resolve("r-again", PermissionVerdict.Allow, DateTime.UtcNow);
        Assert.Equal("""{"v":1,"request_id":"r-again","decision":"allow"}""" + "\n", await ReadToEnd(again));
    }

    [Fact]
    public void TryParseRequest_keeps_a_complete_preview_whole_with_its_line_breaks()
    {
        var lines = Enumerable.Range(0, 4).Select(i => "echo linea-" + i + new string('x', 100)).ToArray();
        var json = string.Join("\\n", lines);   // \n escapado en el JSON, como lo escribe el helper
        var line = $$"""{"v":1,"request_id":"r1","session_id":"s1","cwd":"C:\\work\\api","tool":"Bash","tool_input_preview":"{{json}}","preview_complete":true}""";

        Assert.True(PermissionPipeServer.TryParseRequest(line, DateTime.UtcNow, out var info));
        Assert.True(info.PreviewComplete);
        Assert.Equal(string.Join("\n", lines), info.Preview);   // entera: sin recortar a 160 y con sus saltos de línea
        Assert.True(info.Preview!.Length > PermissionPipeServer.MaxPreviewChars);
    }

    [Theory]
    [InlineData(601, false)]
    [InlineData(600, true)]
    public void TryParseRequest_complete_preview_has_a_600_char_ceiling(int length, bool complete)
    {
        var preview = new string('x', length);
        var line = $$"""{"v":1,"request_id":"r1","session_id":"s","cwd":"c","tool":"Bash","tool_input_preview":"{{preview}}","preview_complete":true}""";
        Assert.True(PermissionPipeServer.TryParseRequest(line, DateTime.UtcNow, out var info));
        Assert.Equal(complete, info.PreviewComplete);
        Assert.Equal(complete ? length : PermissionPipeServer.MaxPreviewChars, info.Preview!.Length);
    }

    [Theory]
    [InlineData(@"echo hola", true)]
    [InlineData(@"echo hola\nrm -rf build", true)]
    [InlineData(@"echo \u00f1and\u00fa \u2713 \ud83d\ude80", true)]
    [InlineData(@" echo", false)]
    [InlineData(@"echo\n", false)]
    [InlineData(@"echo\r\nrm", false)]
    [InlineData(@"echo\trm", false)]
    [InlineData(@"echo \u202emr", false)]          // RLO: invierte lo que se ve
    [InlineData(@"echo\u200bhola", false)]         // ancho cero
    [InlineData(@"echo\u2028rm", false)]           // separador de línea Unicode
    [InlineData(@"echo\u001b[2K", false)]          // escape ANSI
    [InlineData(@"echo \ud83d", false)]            // sustituto suelto
    [InlineData(@"echo \ue000", false)]            // uso privado (glifo sin significado)
    public void Complete_preview_must_be_paintable_as_is(string jsonText, bool complete)
    {
        var line = $$"""{"v":1,"request_id":"r1","session_id":"s","cwd":"c","tool":"Bash","tool_input_preview":"{{jsonText}}","preview_complete":true}""";
        // Un JSON que ni se parsea (p. ej. un sustituto suelto) tampoco registra nada: también vale como «no completa».
        bool parsed = PermissionPipeServer.TryParseRequest(line, DateTime.UtcNow, out var info);
        Assert.Equal(complete, parsed && info.PreviewComplete);
    }

    [Theory]
    [InlineData("Bash", true)]
    [InlineData("WebFetch", true)]
    [InlineData("Read", true)]
    [InlineData("Glob", true)]
    [InlineData("Grep", true)]
    [InlineData("Write", false)]
    [InlineData("Edit", false)]
    [InlineData("MultiEdit", false)]
    [InlineData("NotebookEdit", false)]
    [InlineData("ExitPlanMode", false)]
    [InlineData("Task", false)]
    [InlineData("mcp__fs__write_file", false)]
    [InlineData("bash", false)]
    public void Only_some_tools_can_be_answered(string tool, bool complete)
    {
        var line = $$"""{"v":1,"request_id":"r1","session_id":"s","cwd":"c","tool":"{{tool}}","tool_input_preview":"x","preview_complete":true}""";
        Assert.True(PermissionPipeServer.TryParseRequest(line, DateTime.UtcNow, out var info));
        Assert.Equal(complete, info.PreviewComplete);
    }

    [Fact]
    public void BuildResponse_escapes_the_request_id()
    {
        Assert.Equal("""{"v":1,"request_id":"a\"b","decision":"allow"}""",
            PermissionPipeServer.BuildResponse("a\"b", PermissionVerdict.Allow));
        Assert.Equal("""{"v":1,"request_id":"r","decision":"deny"}""",
            PermissionPipeServer.BuildResponse("r", PermissionVerdict.Deny));
        Assert.Throws<ArgumentOutOfRangeException>(() => PermissionPipeServer.BuildResponse("r", PermissionVerdict.None));
    }

    [Fact]
    public void DefaultPipeName_matches_the_helper()
        => Assert.Equal("claudebar-perm", PermissionPipeServer.DefaultPipeName);
}
