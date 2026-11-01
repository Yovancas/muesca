namespace ClaudeBarWin.Services;

/// <summary>
/// Carpeta de configuración de Claude Code. Respeta <c>CLAUDE_CONFIG_DIR</c> (la variable oficial
/// para moverla); antes la ruta ~/.claude estaba repetida y hardcodeada en 4 sitios y quien usaba
/// esa variable veía "sin credenciales" y 0 € de gasto.
/// </summary>
public static class ClaudePaths
{
    public static string ConfigDir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                try
                {
                    var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(custom.Trim()));
                    if (Directory.Exists(full)) return full;
                }
                catch { /* ruta inválida en la variable: caer al default */ }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        }
    }

    public static string CredentialsPath => Path.Combine(ConfigDir, ".credentials.json");
    public static string ProjectsDir => Path.Combine(ConfigDir, "projects");
}
