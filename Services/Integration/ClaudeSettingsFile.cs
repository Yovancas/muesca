using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeBarWin.Services;

/// <summary>
/// Lectura/escritura segura de ~/.claude/settings.json, compartida por hooks y statusline (v0.4; antes vivía dentro
/// de HookInstaller): admite comentarios y comas finales, no re-escapa ñ/emoji del usuario, y escribe con backup +
/// escritura atómica y relectura optimista (si Claude Code escribe entre medias se rehace el merge sobre lo nuevo;
/// tras 3 choques se aborta sin tocar nada — CR-04 de la 2ª revisión de Codex).
/// </summary>
public static class ClaudeSettingsFile
{
    internal static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string SettingsPath => Path.Combine(ClaudePaths.ConfigDir, "settings.json");

    public static JsonObject ParseRoot(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        return node as JsonObject ?? throw new JsonException("settings.json no es un objeto JSON");
    }

    public static string Serialize(JsonObject root) => root.ToJsonString(WriteOpts);

    /// <summary>Lee aunque otro proceso lo tenga abierto para escribir (File.ReadAllText fallaría con su lock).</summary>
    private static string ReadShared(string path)
    {
        if (!File.Exists(path)) return "{}";
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    private static readonly object IoLock = new();

    /// <summary>read → transformar → validar → backup → escritura atómica. Devuelve la ruta del backup.</summary>
    public static string Rewrite(string backupStamp, Func<string, string> transform)
    {
        lock (IoLock) // Install/Uninstall/Upgrade de hooks y statusline nunca se solapan dentro del proceso
        {
            Directory.CreateDirectory(ClaudePaths.ConfigDir);
            var path = SettingsPath;
            var backup = path + ".claudebar-bak-" + backupStamp;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                var current = ReadShared(path);
                var next = transform(current);
                ParseRoot(next); // validar antes de tocar el disco
                File.WriteAllText(backup, current);
                // La relectura va con el temporal YA preparado, pegada al reemplazo (V04-01): si Claude Code escribió
                // mientras tanto, no se pisa su cambio; se rehace el merge sobre lo nuevo.
                if (AtomicFile.TryWriteAllText(path, next, () => ReadShared(path) == current)) return backup;
            }
            throw new IOException("settings.json cambió en cada intento; no se ha modificado nada.");
        }
    }
}
