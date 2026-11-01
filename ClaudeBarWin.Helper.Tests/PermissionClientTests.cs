using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ClaudeBarWin.Helper;

public sealed class PermissionClientTests
{
    private static readonly byte[] HookInput = Encoding.UTF8.GetBytes(
        """{"session_id":"s1","cwd":"C:\\repo","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"echo hola"}}""");

    private static string Unique() => "cbperm-test-" + Guid.NewGuid().ToString("N");

    private static PermissionVerdict Ask(string pipe, int connectMs = 300, int decisionMs = 5000, Func<int, bool>? trusted = null, byte[]? input = null) =>
        PermissionClient.Ask(input ?? HookInput, pipe, connectMs, decisionMs, trusted ?? (_ => true));

    /// <summary>Servidor de prueba: acepta un cliente, lee su línea (o null si cierra sin escribir) y ejecuta <paramref name="respond"/>.</summary>
    private static Task<string?> Serve(string name, Func<NamedPipeServerStream, string, Task> respond)
    {
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        return Task.Run(async () =>
        {
            // Si el cliente cuelga antes de tiempo (servidor no fiable, conexión vacía, respuesta demasiado
            // grande), Windows lanza «pipe cerrándose» al aceptar, leer, escribir o cerrar en vez de devolver 0.
            // Para estos tests es lo mismo que «no llegó petición / no se entregó respuesta»: lo que se comprueba
            // es el veredicto del cliente.
            string? line = null;
            try
            {
                await using (server)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await server.WaitForConnectionAsync(timeout.Token);
                    line = await ReadLineAsync(server, timeout.Token);
                    if (line is not null)
                        await respond(server, line);
                }
            }
            catch (IOException) { }
            return line;
        });
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one, token) == 1)
        {
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(one[0]);
        }
        return null;
    }

    private static async Task Write(Stream stream, string text)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
        await stream.FlushAsync();
    }

    private static string RequestId(string request)
    {
        using var document = JsonDocument.Parse(request);
        return document.RootElement.GetProperty("request_id").GetString()!;
    }

    private static Func<NamedPipeServerStream, string, Task> Reply(string decision) =>
        (server, request) => Write(server, $$"""{"v":1,"request_id":"{{RequestId(request)}}","decision":"{{decision}}"}""" + "\n");

    /// <summary>La ruta del servidor se lee con QueryFullProcessImageName (vale entre niveles de admin distintos).</summary>
    [Fact]
    public void Process_image_path_is_read_without_MainModule()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(PermissionClient.TryGetProcessImagePath(Environment.ProcessId, out var path));
        Assert.Equal(Environment.ProcessPath, path, StringComparer.OrdinalIgnoreCase);
        Assert.False(PermissionClient.TryGetProcessImagePath(0, out _));
    }

    [Fact]
    public void No_server_returns_None_quickly()
    {
        var sw = Stopwatch.StartNew();
        Assert.Equal(PermissionVerdict.None, Ask(Unique(), connectMs: 300));
        Assert.True(sw.ElapsedMilliseconds < 1500, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Untrusted_server_returns_None_and_never_gets_the_request()
    {
        var name = Unique();
        var server = Serve(name, Reply("allow"));
        var seenPid = -1;

        Assert.Equal(PermissionVerdict.None, Ask(name, trusted: pid => { seenPid = pid; return false; }));
        Assert.Null(await server);
        if (OperatingSystem.IsWindows())
            Assert.Equal(Environment.ProcessId, seenPid);
    }

    [Fact]
    public async Task Trust_check_that_throws_returns_None()
    {
        var name = Unique();
        var server = Serve(name, Reply("allow"));
        Assert.Equal(PermissionVerdict.None, Ask(name, trusted: _ => throw new InvalidOperationException()));
        Assert.Null(await server);
    }

    [Fact]
    public async Task Server_allow_returns_Allow()
    {
        var name = Unique();
        var server = Serve(name, Reply("allow"));
        var seenPid = -1;

        Assert.Equal(PermissionVerdict.Allow, Ask(name, trusted: pid => { seenPid = pid; return true; }));

        var request = await server;
        Assert.NotNull(request);
        using var document = JsonDocument.Parse(request);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        Assert.True(Guid.TryParse(root.GetProperty("request_id").GetString(), out _));
        Assert.Equal("s1", root.GetProperty("session_id").GetString());
        Assert.Equal("Bash", root.GetProperty("tool").GetString());
        Assert.Equal("echo hola", root.GetProperty("tool_input_preview").GetString());
        if (OperatingSystem.IsWindows())
            Assert.Equal(Environment.ProcessId, seenPid);
    }

    [Fact]
    public async Task Server_deny_returns_Deny()
    {
        var name = Unique();
        var server = Serve(name, Reply("deny"));
        Assert.Equal(PermissionVerdict.Deny, Ask(name));
        Assert.NotNull(await server);
    }

    [Fact]
    public async Task Each_request_has_a_fresh_request_id()
    {
        var first = Unique();
        var firstServer = Serve(first, Reply("allow"));
        Ask(first);
        var second = Unique();
        var secondServer = Serve(second, Reply("allow"));
        Ask(second);

        Assert.NotEqual(RequestId((await firstServer)!), RequestId((await secondServer)!));
    }

    [Fact]
    public async Task Server_silent_times_out_to_None()
    {
        var name = Unique();
        var release = new TaskCompletionSource();
        var server = Serve(name, async (_, _) => await release.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        var sw = Stopwatch.StartNew();
        Assert.Equal(PermissionVerdict.None, Ask(name, decisionMs: 500));
        Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");

        release.SetResult();
        await server;
    }

    [Fact]
    public async Task Server_closes_midway_returns_None()
    {
        var name = Unique();
        var server = Serve(name, (stream, request) => Write(stream, $$"""{"v":1,"request_id":"{{RequestId(request)}}","dec"""));
        Assert.Equal(PermissionVerdict.None, Ask(name));
        await server;
    }

    [Fact]
    public async Task Server_closes_without_writing_returns_None()
    {
        var name = Unique();
        var server = Serve(name, (_, _) => Task.CompletedTask);
        Assert.Equal(PermissionVerdict.None, Ask(name));
        await server;
    }

    [Fact]
    public async Task Response_without_line_break_returns_None()
    {
        var name = Unique();
        var server = Serve(name, (stream, request) => Write(stream, $$"""{"v":1,"request_id":"{{RequestId(request)}}","decision":"allow"}"""));
        Assert.Equal(PermissionVerdict.None, Ask(name));
        await server;
    }

    [Fact]
    public async Task Oversized_response_returns_None()
    {
        var name = Unique();
        var server = Serve(name, (stream, _) => Write(stream, new string(' ', 64 * 1024)));
        Assert.Equal(PermissionVerdict.None, Ask(name));
        await server;
    }

    [Fact]
    public async Task Wrong_request_id_returns_None()
    {
        var name = Unique();
        var server = Serve(name, (stream, _) => Write(stream, $$"""{"v":1,"request_id":"{{Guid.NewGuid()}}","decision":"allow"}""" + "\n"));
        Assert.Equal(PermissionVerdict.None, Ask(name));
        await server;
    }

    [Fact]
    public async Task Malformed_response_returns_None()
    {
        var name = Unique();
        var server = Serve(name, (stream, request) => Write(stream, $$"""{"v":1,"request_id":"{{RequestId(request)}}","decision":"yes"}""" + "\n"));
        Assert.Equal(PermissionVerdict.None, Ask(name));
        await server;
    }

    [Theory]
    [InlineData("")]
    [InlineData("{roto")]
    [InlineData("[]")]
    public async Task Invalid_hook_input_returns_None_without_contacting_the_server(string raw)
    {
        var name = Unique();
        var server = Serve(name, Reply("allow"));
        Assert.Equal(PermissionVerdict.None, Ask(name, input: Encoding.UTF8.GetBytes(raw)));

        // Nunca se conectó: el servidor sigue esperando; una conexión vacía lo libera.
        Assert.False(server.IsCompleted);
        using (var probe = new NamedPipeClientStream(".", name, PipeDirection.InOut))
            probe.Connect(2000);
        Assert.Null(await server);
    }

    [Fact]
    public void Trusted_path_is_ClaudeBarWin_exe_in_helper_dir_or_install_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "cbperm-" + Guid.NewGuid().ToString("N"));
        var helperDir = Path.Combine(root, "data", "bin");
        var localAppData = Path.Combine(root, "local");
        var installDir = Path.Combine(localAppData, "Programs", "ClaudeBarWin");

        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(helperDir, "ClaudeBarWin.exe"), helperDir, localAppData));
        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(helperDir, "ClaudeBarWin.exe"), helperDir + Path.DirectorySeparatorChar, localAppData));
        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(installDir, "ClaudeBarWin.exe"), helperDir, localAppData));
        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(installDir, "CLAUDEBARWIN.EXE"), helperDir, localAppData));
        // v0.5 (Velopack): la app corre desde %LOCALAPPDATA%\ClaudeBar\current.
        var velopackDir = Path.Combine(localAppData, "ClaudeBar", "current");
        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(velopackDir, "ClaudeBarWin.exe"), helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(localAppData, "ClaudeBar", "ClaudeBarWin.exe"), helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(localAppData, "ClaudeBarWin", "current", "ClaudeBarWin.exe"), helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(velopackDir, "sub", "ClaudeBarWin.exe"), helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(velopackDir, "Muesca.exe"), helperDir, localAppData));

        Assert.False(PermissionClient.IsTrustedServerPath(null, helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath("", helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(installDir, "NotClaudeBarWin.exe"), helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(installDir, "ClaudeBarWin.exe.bak"), helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(root, "evil", "ClaudeBarWin.exe"), helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(installDir, "sub", "ClaudeBarWin.exe"), helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(installDir + "2", "ClaudeBarWin.exe"), helperDir, localAppData));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(root, "evil", "ClaudeBarWin.exe"), helperDir, null));
    }

    /// <summary>
    /// Edición de la Store: el helper corre con identidad de paquete (lanzado por su alias) y solo se fía de un ClaudeBarWin.exe
    /// de la carpeta de su paquete, que Windows protege. Ni la carpeta del alias (escribible por el usuario) ni las de
    /// Velopack o Inno.
    /// </summary>
    [Fact]
    public void Packaged_helper_only_trusts_the_app_of_its_own_package()
    {
        var root = Path.Combine(Path.GetTempPath(), "cbperm-" + Guid.NewGuid().ToString("N"));
        var localAppData = Path.Combine(root, "local");
        var packageDir = Path.Combine(root, "WindowsApps", "Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbwe");
        var aliasDir = Path.Combine(localAppData, "Microsoft", "WindowsApps", "Bekay.Muesca_8wekyb3d8bbwe");
        var velopackDir = Path.Combine(localAppData, "ClaudeBar", "current");
        var innoDir = Path.Combine(localAppData, "Programs", "ClaudeBarWin");

        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(packageDir, "ClaudeBarWin.exe"), aliasDir, localAppData, packageDir));
        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(packageDir, "claudebarwin.EXE"), aliasDir, localAppData,
            packageDir + Path.DirectorySeparatorChar));

        // La carpeta del alias (desde la que el helper puede creer que corre) no vale: el usuario puede escribir en ella.
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(aliasDir, "ClaudeBarWin.exe"), aliasDir, localAppData, packageDir));
        // Con identidad, ni Velopack ni Inno.
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(velopackDir, "ClaudeBarWin.exe"), aliasDir, localAppData, packageDir));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(innoDir, "ClaudeBarWin.exe"), aliasDir, localAppData, packageDir));
        // Otro paquete, otra versión o una subcarpeta del nuestro.
        Assert.False(PermissionClient.IsTrustedServerPath(
            Path.Combine(root, "WindowsApps", "Evil.Muesca_1.0.0.0_x64__8wekyb3d8bbwe", "ClaudeBarWin.exe"), aliasDir, localAppData, packageDir));
        Assert.False(PermissionClient.IsTrustedServerPath(
            Path.Combine(root, "WindowsApps", "Bekay.Muesca_0.9.0.0_x64__8wekyb3d8bbwe", "ClaudeBarWin.exe"), aliasDir, localAppData, packageDir));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(packageDir, "VFS", "ClaudeBarWin.exe"), aliasDir, localAppData, packageDir));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(packageDir, "sub", "..", "..", "x", "ClaudeBarWin.exe"), aliasDir,
            localAppData, packageDir));
        // Otro exe de nuestro paquete (el propio helper) no es el servidor.
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(packageDir, "claudebar-helper.exe"), aliasDir, localAppData, packageDir));

        // Sin identidad pero desde la carpeta del paquete (Git Bash ejecuta el exe del paquete directamente): la carpeta del
        // paquete es la del propio helper, y tampoco valen Velopack ni Inno, que el usuario puede escribir.
        var noIdentity = HelperPackage.TrustedPackageDir(null, packageDir);
        Assert.Equal(packageDir, noIdentity);
        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(packageDir, "ClaudeBarWin.exe"), packageDir, localAppData, noIdentity));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(velopackDir, "ClaudeBarWin.exe"), packageDir, localAppData, noIdentity));
        Assert.False(PermissionClient.IsTrustedServerPath(Path.Combine(innoDir, "ClaudeBarWin.exe"), packageDir, localAppData, noIdentity));

        // Fuera de un paquete (edición de GitHub), las reglas de siempre.
        Assert.Null(HelperPackage.TrustedPackageDir(null, Path.Combine(localAppData, "ClaudeBarWin", "bin")));
        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(velopackDir, "ClaudeBarWin.exe"), aliasDir, localAppData, null));
        Assert.True(PermissionClient.IsTrustedServerPath(Path.Combine(velopackDir, "ClaudeBarWin.exe"), aliasDir, localAppData, " "));
    }

    [Fact]
    public void Real_trust_check_rejects_invalid_pids_and_non_ClaudeBar_processes()
    {
        Assert.False(PermissionClient.IsTrustedServer(0));
        Assert.False(PermissionClient.IsTrustedServer(-1));
        // El propio proceso de tests no es ClaudeBarWin.exe.
        Assert.False(PermissionClient.IsTrustedServer(Environment.ProcessId));
    }

    // ── Integridad (revisión cruzada, punto 2): un servidor con menos nivel nunca decide ─────────────────

    private const int Medium = 0x2000, High = 0x3000, Low = 0x1000, SystemLevel = 0x4000;

    [Theory]
    [InlineData(false, Medium, false, Medium, true)]    // los dos normales
    [InlineData(false, Medium, true, High, true)]       // Claude normal, ClaudeBar admin: el servidor tiene más
    [InlineData(true, High, true, High, true)]          // los dos admin
    [InlineData(true, High, false, Medium, false)]      // Claude admin, ClaudeBar normal: salto de UAC ⇒ None
    [InlineData(false, High, false, Medium, false)]     // más integridad que el servidor (sin UAC)
    [InlineData(false, Medium, false, Low, false)]      // servidor en caja de arena (integridad baja)
    [InlineData(true, High, false, High, false)]        // elevado frente a no elevado aunque la etiqueta coincida
    [InlineData(true, High, true, SystemLevel, true)]
    public void Server_with_less_level_than_the_helper_is_rejected(bool helperElevated, int helperIl, bool serverElevated,
        int serverIl, bool allowed) =>
        Assert.Equal(allowed, PermissionClient.IntegrityAllows(new(helperElevated, helperIl), new(serverElevated, serverIl)));

    [Fact]
    public void Own_token_level_is_readable_and_equal_to_itself()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(PermissionClient.TryGetProcessTokenLevel(Environment.ProcessId, out var level));
        Assert.InRange(level.Integrity, Low, SystemLevel);
        Assert.True(PermissionClient.IsServerIntegrityOk(Environment.ProcessId));   // mismo token: mismo nivel
    }

    [Fact]
    public void Integrity_check_fails_closed()
    {
        Assert.False(PermissionClient.IsServerIntegrityOk(0));
        Assert.False(PermissionClient.IsServerIntegrityOk(-5));
        Assert.False(PermissionClient.TryGetProcessTokenLevel(0, out _));
        // Un PID que no existe (o ya salió): no se puede leer ⇒ no confiable.
        Assert.False(PermissionClient.IsServerIntegrityOk(int.MaxValue - 3));
    }

    [Fact]
    public void Elevated_helper_rejects_a_medium_integrity_process()
    {
        // Con el proceso de tests elevado, el Explorador del usuario (integridad media) no puede hacer de servidor.
        if (!OperatingSystem.IsWindows()
            || !PermissionClient.TryGetProcessTokenLevel(Environment.ProcessId, out var self) || !self.Elevated) return;
        var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
        if (explorer is null
            || !PermissionClient.TryGetProcessTokenLevel(explorer.Id, out var level) || level.Elevated) return;
        Assert.False(PermissionClient.IsServerIntegrityOk(explorer.Id));
    }
}
