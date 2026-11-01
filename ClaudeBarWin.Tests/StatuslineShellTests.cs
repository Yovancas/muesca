using System.Diagnostics;
using System.Text;
using ClaudeBarWin.Services;

/// <summary>
/// El enganche resistente de la statusline (<see cref="StatuslineInstaller"/>), ejecutado de verdad en el shell en el que lo
/// lanza Claude Code: Git Bash (<c>bash -c</c>) y PowerShell (<c>-Command</c>). Con el helper, pasa por él (y deja su rastro);
/// sin él (Muesca desinstalada sin apagarlo), la statusline del usuario recibe el mismo JSON, byte a byte, y sale igual.
/// <para>Cada test usa el shell que encuentre: Git Bash o <c>/bin/bash</c>, y <c>powershell.exe</c> o <c>pwsh</c>; sin él, no
/// comprueba nada. Con el helper: un script en Linux; en Windows, el helper compilado de la solución (si está).</para>
/// </summary>
public sealed class StatuslineShellTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-sl-" + Guid.NewGuid().ToString("N"));

    public StatuslineShellTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    // Sin caracteres fuera de ASCII en lo que se compara byte a byte con el helper delante: el helper lo reescribe en ASCII
    // (AsciiJson) para que sobreviva a la tubería de PowerShell 5.1, y eso cambia los bytes aunque no el significado.
    private const string AsciiJson = """{"session_id":"sl-shell","cwd":"C:/Proyectos/x","rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":4102444800}}}""";
    private const string UnicodeJson = """{"session_id":"sl-shell","cwd":"C:/Proyectos/español 🚀","model":{"display_name":"«ñ»"}}""";

    private static string? FindBash()
    {
        if (!OperatingSystem.IsWindows()) return File.Exists("/bin/bash") ? "/bin/bash" : null;
        return new[]
            {
                Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH"),
                Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? "", "Git", "bin", "bash.exe"),
                Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Programs", "Git", "bin", "bash.exe"),
            }
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }

    private static string? FindPowerShell()
    {
        if (OperatingSystem.IsWindows())
        {
            var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(ps) ? ps : null;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var pwsh = Path.Combine(dir, "pwsh");
            if (File.Exists(pwsh)) return pwsh;
        }
        return null;
    }

    /// <summary>La statusline «del usuario» de la prueba: devuelve lo que le llega por stdin, tal cual.</summary>
    private static string Passthrough(ClaudeShell shell) =>
        OperatingSystem.IsWindows() && shell == ClaudeShell.PowerShell ? "findstr \"^\"" : "cat";

    private static (byte[] Out, string Err, int Code) Run(string shellExe, ClaudeShell shell, string command, byte[] stdin,
        IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(shellExe)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (shell == ClaudeShell.GitBash) { psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(command); }
        else foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) psi.ArgumentList.Add(a);
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;

        using var p = Process.Start(psi)!;
        using var output = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(output);
        var err = p.StandardError.ReadToEndAsync();
        p.StandardInput.BaseStream.Write(stdin);
        p.StandardInput.Close();
        if (!p.WaitForExit(60_000)) { p.Kill(entireProcessTree: true); throw new TimeoutException(command); }
        copy.Wait();
        p.WaitForExit();
        return (output.ToArray(), err.Result, p.ExitCode);
    }

    private static string Text(byte[] b) => Encoding.UTF8.GetString(b).TrimEnd('\r', '\n');

    private string? ShellFor(ClaudeShell shell) => shell == ClaudeShell.GitBash ? FindBash() : FindPowerShell();

    /// <summary>Un helper de prueba en <paramref name="dir"/>: el compilado (Windows) o un script que deja una marca y devuelve
    /// stdin (fuera de Windows). Null si en este equipo no hay ninguno.</summary>
    private static string? FakeHelper(string dir)
    {
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "claudebar-helper.exe");
        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(exe, "#!/bin/sh\necho ran > \"$(dirname \"$0\")/ran\"\nexec cat\n");
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return exe;
        }
        // El de la solución: ClaudeBarWin.Helper/bin/<config>/net9.0, con su dll y su runtimeconfig.
        var config = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Release";
        var built = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ClaudeBarWin.Helper", "bin", config, "net9.0"));
        if (!File.Exists(Path.Combine(built, "claudebar-helper.exe"))) return null;
        foreach (var f in Directory.GetFiles(built)) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: true);
        return exe;
    }

    private static bool HelperRan(string dir, string dataDir) =>
        File.Exists(Path.Combine(dir, "ran")) || File.Exists(Path.Combine(dataDir, "ratelimits", "sl-shell.json"));

    public static IEnumerable<object[]> ShellsAndDirs()
    {
        foreach (var shell in new[] { ClaudeShell.GitBash, ClaudeShell.PowerShell })
            foreach (var sub in new[] { "bin", "John O'Brien bin" })
                yield return new object[] { shell, sub };
    }

    [Theory]
    [MemberData(nameof(ShellsAndDirs))]
    public void Without_the_helper_the_users_statusline_gets_the_same_json(ClaudeShell shell, string sub)
    {
        if (ShellFor(shell) is not { } exe) return;
        var helper = Path.Combine(_dir, sub, "claudebar-helper.exe");   // no existe: Muesca desinstalada
        var original = Passthrough(shell);
        var command = StatuslineInstaller.TeeCommand(helper, shell, original);

        foreach (var json in new[] { AsciiJson, UnicodeJson })
        {
            var input = new UTF8Encoding(false).GetBytes(json);
            var alone = Run(exe, shell, original, input);
            var wrapped = Run(exe, shell, command, input);
            Assert.True(wrapped.Code == 0, $"{shell}: salió con {wrapped.Code}: {wrapped.Err}\n{command}");
            Assert.Equal(json, Text(alone.Out));
            Assert.Equal(Text(alone.Out), Text(wrapped.Out));
        }
    }

    [Theory]
    [MemberData(nameof(ShellsAndDirs))]
    public void With_the_helper_the_json_goes_through_it(ClaudeShell shell, string sub)
    {
        if (ShellFor(shell) is not { } exe) return;
        var dir = Path.Combine(_dir, sub);
        if (FakeHelper(dir) is not { } helper) return;
        var dataDir = Path.Combine(_dir, "data");
        var original = Passthrough(shell);
        var command = StatuslineInstaller.TeeCommand(helper, shell, original);

        var input = new UTF8Encoding(false).GetBytes(AsciiJson);
        var wrapped = Run(exe, shell, command, input, new Dictionary<string, string> { ["CLAUDEBAR_DATA_DIR"] = dataDir });
        Assert.True(wrapped.Code == 0, $"{shell}: salió con {wrapped.Code}: {wrapped.Err}\n{command}");
        Assert.Equal(AsciiJson, Text(wrapped.Out));
        Assert.True(HelperRan(dir, dataDir), $"{shell}: el helper no se ejecutó\n{command}\n{wrapped.Err}");
    }
}
