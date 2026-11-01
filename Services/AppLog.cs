using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeBarWin.Services;

/// <summary>
/// Log de diagnóstico local y privado: %APPDATA%\ClaudeBarWin\claudebar.log (rotado a .1 al pasar
/// de 1 MB). Antes la app no escribía NADA y era imposible saber desde fuera por qué llevaba 20 min
/// sin datos (429 compartido con ccstatusline, token caducado, red...). Nunca sale del equipo.
/// Redacta cualquier cosa con pinta de token (sk-ant-…, Bearer …, "accessToken"/"refreshToken", access_token=…), también
/// en los mensajes y trazas de las excepciones que se registran.
/// Desactivado por defecto: solo lo activa la app real (Program.Main), así los tests no tocan
/// el %APPDATA% del usuario.
/// </summary>
public static partial class AppLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static bool Enabled { get; set; }

    public static string LogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeBarWin", "claudebar.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    // Tres formas de token: (1) las claves de Anthropic (sk-ant-oat…, sk-ant-ort…, sk-ant-api…); (2) «Bearer x» con el
    // alfabeto completo de RFC 6750 (~ + / y = de relleno); (3) clave «access/refresh token» seguida de su valor, en JSON
    // ("accessToken": "…"), JSON escapado dentro de otro texto (\"refreshToken\":\"…\") o query/form (access_token=…).
    // En (3) se conserva la clave y se tapa solo el valor.
    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]+|(?i:bearer)\s+[A-Za-z0-9._~+/\-]+=*|((?i:\\?""?(?:access|refresh)_?token\\?""?\s*[:=]\s*\\?""?))[^""\\\s&,;}]+")]
    private static partial Regex SecretPattern();

    /// <summary>Quita secretos del texto antes de persistirlo (público para tests).</summary>
    public static string Redact(string s) =>
        SecretPattern().Replace(s, m => m.Groups[1].Success ? m.Groups[1].Value + "***" : "***");

    private static void Write(string level, string message)
    {
        if (!Enabled) return;
        try
        {
            var line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {level} {Redact(message)}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Move(LogPath, LogPath + ".1", overwrite: true);
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // El log nunca debe tumbar la app.
        }
    }
}
