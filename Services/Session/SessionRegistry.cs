using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ClaudeBarWin.Services;

/// <summary>
/// Una sesión de Claude Code tal como la anuncia su archivo <c>~/.claude/sessions/&lt;pid&gt;.json</c>.
/// <see cref="ProcStartUtc"/> es la hora de inicio del proceso (anti reutilización de PID);
/// <see cref="StatusSinceUtc"/>, cuándo cambió <see cref="Status"/> por última vez.
/// </summary>
public sealed record RegistryEntry(
    int Pid,
    string SessionId,
    string Cwd,
    string Status,
    DateTime? ProcStartUtc = null,
    DateTime? StartedAtUtc = null,
    DateTime? StatusSinceUtc = null,
    string? Name = null,
    string? Entrypoint = null,
    string? Kind = null)
{
    /// <summary>¿Claude Code dice que está trabajando ahora mismo?</summary>
    public bool IsBusy => string.Equals(Status, "busy", StringComparison.OrdinalIgnoreCase);

    /// <summary>¿Claude Code dice que está esperando al usuario?</summary>
    public bool IsIdle => string.Equals(Status, "idle", StringComparison.OrdinalIgnoreCase);

    /// <summary>El proceso de Claude Code de esta sesión (necesita la hora de inicio resuelta).</summary>
    public ProcessInfo? Process => ProcStartUtc is { } st ? new ProcessInfo(Pid, "claude.exe", st) : null;
}

/// <summary>
/// Registro de sesiones abiertas que mantiene el propio Claude Code: un <c>&lt;pid&gt;.json</c> por cada
/// sesión de terminal viva en <c>&lt;ConfigDir&gt;/sessions</c>. Antes el notch solo conocía las sesiones
/// que habían mandado un hook desde que arrancó ClaudeBar (y las ociosas se podaban a los 30 min): con
/// tres terminales abiertas solo se veía la que trabajaba.
/// Lógica pura (parseo + filtro de vivas) separada de la E/S (<see cref="SessionRegistryReader"/>).
/// Los <c>.key</c> que hay al lado NUNCA se leen: solo se enumeran <c>*.json</c>.
/// </summary>
public static class SessionRegistry
{
    /// <summary>Carpeta del registro (respeta CLAUDE_CONFIG_DIR vía <see cref="ClaudePaths"/>).</summary>
    public static string DefaultDir => Path.Combine(ClaudePaths.ConfigDir, "sessions");

    /// <summary>Tolerancia al comparar procStart con la hora de inicio real del proceso.</summary>
    private static readonly TimeSpan ProcStartTolerance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Sin procStart, startedAt lo escribe Claude Code unos segundos DESPUÉS de arrancar el proceso
    /// (medido: ~3 s); se acepta si el proceso arrancó antes y no hace más de esto.
    /// </summary>
    private static readonly TimeSpan StartedAtTolerance = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Texto de un <c>&lt;pid&gt;.json</c> → registro, o null si no sirve. Tolerante: lee SOLO el primer
    /// objeto JSON (un archivo a medio reescribir puede traer basura detrás) y descarta lo que no tenga
    /// pid y sessionId. Nunca lanza.
    /// </summary>
    public static RegistryEntry? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text),
                new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            // ParseValue consume un único valor y se para: lo que venga detrás no rompe el parseo.
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!TryInt(root, "pid", out var pid) || pid <= 0) return null;
            var sessionId = Str(root, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId)) return null;

            return new RegistryEntry(
                Pid: pid,
                SessionId: sessionId,
                Cwd: Str(root, "cwd") ?? "",
                Status: Str(root, "status") ?? "",
                ProcStartUtc: FileTimeUtc(root, "procStart"),
                StartedAtUtc: EpochMsUtc(root, "startedAt"),
                StatusSinceUtc: EpochMsUtc(root, "statusUpdatedAt") ?? EpochMsUtc(root, "updatedAt"),
                Name: Str(root, "name"),
                Entrypoint: Str(root, "entrypoint"),
                Kind: Str(root, "kind"));
        }
        catch { return null; }
    }

    /// <summary>
    /// ¿Es una sesión que interesa mostrar? Terminal (cli) o SDK; sin el campo (versiones viejas) también.
    /// </summary>
    public static bool IsSupportedEntrypoint(string? entrypoint)
        => string.IsNullOrEmpty(entrypoint)
           || entrypoint.Equals("cli", StringComparison.OrdinalIgnoreCase)
           || entrypoint.StartsWith("sdk", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿El registro corresponde a un proceso vivo? <paramref name="actualStartUtc"/> es la hora de inicio
    /// real del proceso con ese PID (null si no existe). Si el PID se reutilizó, la hora no cuadra.
    /// </summary>
    public static bool Matches(RegistryEntry e, DateTime? actualStartUtc)
    {
        if (actualStartUtc is not { } actual) return false;
        if (e.ProcStartUtc is { } ps) return (actual - ps).Duration() <= ProcStartTolerance;
        if (e.StartedAtUtc is { } sa)
        {
            var lag = sa - actual;
            return lag >= -ProcStartTolerance && lag <= StartedAtTolerance;
        }
        return false; // sin ninguna hora no hay forma de descartar un PID reutilizado
    }

    /// <summary>
    /// De los registros parseados, los que pertenecen a procesos vivos y con entrypoint soportado. Rellena
    /// <see cref="RegistryEntry.ProcStartUtc"/> con la hora real (para detectar después su muerte) y, si dos
    /// archivos traen el mismo sessionId, se queda con el más reciente.
    /// </summary>
    public static IReadOnlyList<RegistryEntry> FilterLive(IEnumerable<RegistryEntry> entries, Func<int, DateTime?> startOf)
    {
        var bySession = new Dictionary<string, RegistryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (!IsSupportedEntrypoint(e.Entrypoint)) continue;
            var actual = startOf(e.Pid);
            if (!Matches(e, actual)) continue;
            var live = e with { ProcStartUtc = actual };
            if (!bySession.TryGetValue(e.SessionId, out var prev)
                || (live.StartedAtUtc ?? DateTime.MinValue) > (prev.StartedAtUtc ?? DateTime.MinValue))
                bySession[e.SessionId] = live;
        }
        return bySession.Values.ToList();
    }

    /// <summary>Hora de inicio real (UTC) del proceso, o null si no existe o no se puede abrir.</summary>
    public static DateTime? ProcessStartUtc(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.StartTime.ToUniversalTime();
        }
        catch { return null; }
    }

    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryInt(JsonElement o, string name, out int value)
    {
        value = 0;
        if (!o.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt32(out value);
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out value);
    }

    private static bool TryLong(JsonElement o, string name, out long value)
    {
        value = 0;
        if (!o.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out value);
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out value);
    }

    /// <summary>procStart viene como FILETIME de Windows (intervalos de 100 ns desde 1601) en texto.</summary>
    private static DateTime? FileTimeUtc(JsonElement o, string name)
    {
        if (!TryLong(o, name, out var ft) || ft <= 0) return null;
        try { return DateTime.FromFileTimeUtc(ft); }
        catch { return null; }
    }

    private static DateTime? EpochMsUtc(JsonElement o, string name)
    {
        if (!TryLong(o, name, out var ms) || ms <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime; }
        catch { return null; }
    }
}

/// <summary>
/// E/S del registro: enumera <c>*.json</c>, los lee compartidos (Claude Code los reescribe a menudo) y
/// devuelve las sesiones vivas. Un archivo ilegible o a medio escribir reutiliza la última lectura buena
/// de ese mismo archivo, para que la sesión no desaparezca y reaparezca (perdiendo su estado de hooks).
/// Thread-safe.
/// </summary>
public sealed class SessionRegistryReader
{
    private const int MaxFileBytes = 64 * 1024;

    private readonly object _lock = new();
    private readonly Dictionary<string, RegistryEntry> _lastGood = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<int, DateTime?> _startOf;

    public string Dir { get; }

    public SessionRegistryReader(string? dir = null, Func<int, DateTime?>? startOf = null)
    {
        Dir = dir ?? SessionRegistry.DefaultDir;
        _startOf = startOf ?? SessionRegistry.ProcessStartUtc;
    }

    /// <summary>
    /// Sesiones vivas. Carpeta inexistente → lista vacía (no hay sesiones). Si la carpeta no se puede
    /// enumerar → null: el llamador no debe sacar conclusiones (y no quitar nada).
    /// </summary>
    public IReadOnlyList<RegistryEntry>? ReadLive()
    {
        lock (_lock)
        {
            if (!Directory.Exists(Dir)) { _lastGood.Clear(); return Array.Empty<RegistryEntry>(); }

            string[] files;
            try { files = Directory.GetFiles(Dir, "*.json", SearchOption.TopDirectoryOnly); }
            catch { return null; }

            var parsed = new List<RegistryEntry>(files.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                // GetFiles con "*.json" también casa "*.jsonX" por el nombre corto 8.3: filtrar exacto.
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                seen.Add(path);
                var e = SessionRegistry.Parse(ReadShared(path));
                if (e is not null) _lastGood[path] = e;
                else if (_lastGood.TryGetValue(path, out var prev)) e = prev;
                if (e is not null) parsed.Add(e);
            }
            foreach (var gone in _lastGood.Keys.Where(k => !seen.Contains(k)).ToList()) _lastGood.Remove(gone);

            return SessionRegistry.FilterLive(parsed, _startOf);
        }
    }

    private static string? ReadShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > MaxFileBytes) return null;
            using var sr = new StreamReader(fs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch { return null; } // borrado o bloqueado entre la enumeración y la lectura: silencio
    }
}
