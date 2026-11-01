using System.Text.RegularExpressions;

namespace ClaudeBarWin.Services;

public enum ClaudeShell { GitBash, PowerShell }

/// <summary>
/// Cita la ruta de un exe para el campo <c>command</c> de settings.json (v0.4). Claude Code lo pasa por Git Bash si
/// existe o por PowerShell si no: una ruta «segura» (sin espacios ni símbolos) va sin comillas y vale en los dos;
/// si no, se cita con la sintaxis del shell que vaya a usar. Siempre con '/' (Git Bash se come las '\').
/// </summary>
public static class ShellQuote
{
    private static readonly Regex Safe = new(@"^[A-Za-z0-9:/._-]+$", RegexOptions.CultureInvariant);

    public static string Command(string exePath, ClaudeShell shell)
    {
        var p = exePath.Replace('\\', '/');
        if (Safe.IsMatch(p)) return p;
        return shell == ClaudeShell.GitBash
            ? "'" + p.Replace("'", "'\\''") + "'"
            : "& '" + p.Replace("'", "''") + "'";
    }

    /// <summary>La ruta como cadena literal de PowerShell (<c>'…'</c>, con <c>''</c> dentro y siempre con '/'), para lo que
    /// no es el comando en sí: <c>Test-Path -LiteralPath</c> del enganche resistente de la statusline.</summary>
    public static string PowerShellLiteral(string path) => "'" + path.Replace('\\', '/').Replace("'", "''") + "'";
}

/// <summary>Misma elección de shell que Claude Code en Windows (doc de statusline): Git Bash si está, si no PowerShell.</summary>
public static class ClaudeShellDetector
{
    public static ClaudeShell Detect() => Detect(Environment.GetEnvironmentVariable, File.Exists);

    public static ClaudeShell Detect(Func<string, string?> env, Func<string, bool> exists)
    {
        var explicitPath = env("CLAUDE_CODE_GIT_BASH_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && exists(explicitPath.Trim())) return ClaudeShell.GitBash;

        foreach (var dir in (env("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (!exists(Path.Combine(dir, "git.exe"))) continue;
                // git.exe vive en <Git>\cmd o en <Git>\mingw64\bin; bash.exe en <Git>\bin.
                foreach (var rel in new[] { @"..\bin\bash.exe", @"..\..\bin\bash.exe" })
                {
                    var bash = Path.GetFullPath(Path.Combine(dir, rel));
                    if (exists(bash)) return ClaudeShell.GitBash;
                }
            }
            catch { /* entrada de PATH rara */ }
        }

        foreach (var root in new[] { env("ProgramFiles"), env("LOCALAPPDATA") is { } l ? Path.Combine(l, "Programs") : null })
            if (!string.IsNullOrWhiteSpace(root) && exists(Path.Combine(root, "Git", "bin", "bash.exe"))) return ClaudeShell.GitBash;

        return ClaudeShell.PowerShell;
    }
}
