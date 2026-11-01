using System.Globalization;
using System.Text.Json;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Config;

/// <summary>
/// Lo que manda el asistente de instalación a <c>ClaudeBarWin.exe --apply-setup &lt;ruta.json&gt;</c> (versión 1):
/// <code>{ "version": 1, "profile": "coder", "liveSessions": true, "startWithWindows": true,
///   "overrides": { "Language": "system", "NotchOpenOnClick": false } }</code>
/// <c>version</c> y <c>profile</c> son obligatorios. <c>overrides</c> son ajustes de <see cref="SetupSettings"/>: una clave
/// desconocida o un valor que no vale se ignora y se anota (un instalador más nuevo no rompe una app más vieja).
/// <c>liveSessions</c> pide instalar los hooks (solo si el perfil los usa); <c>startWithWindows</c> ausente ⇒ no se toca.
/// </summary>
public sealed record SetupRequest(
    SetupProfile Profile,
    IReadOnlyDictionary<string, object> Overrides,
    bool LiveSessions,
    bool? StartWithWindows,
    IReadOnlyList<string> Ignored)
{
    public const int CurrentVersion = 1;

    /// <summary>Interpreta el JSON del asistente. Si no vale (no es JSON, versión desconocida, perfil desconocido o un campo
    /// con otro tipo) devuelve false con el motivo y no se aplica nada.</summary>
    public static bool TryParse(string json, out SetupRequest? request, out string error)
    {
        request = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "el JSON no es un objeto"; return false; }

            if (!root.TryGetProperty("version", out var ver)) { error = "falta «version»"; return false; }
            if (ver.ValueKind != JsonValueKind.Number || !ver.TryGetInt32(out int v) || v != CurrentVersion)
            {
                error = $"versión desconocida ({ver.GetRawText()}); esta app entiende la {CurrentVersion}";
                return false;
            }

            if (!root.TryGetProperty("profile", out var prof) || prof.ValueKind != JsonValueKind.String)
            {
                error = "falta «profile»";
                return false;
            }
            if (!SetupProfiles.TryParse(prof.GetString(), out var profile))
            {
                error = $"perfil desconocido «{prof.GetString()}»";
                return false;
            }

            if (!OptionalBool(root, "liveSessions", out bool? live, out error)) return false;
            if (!OptionalBool(root, "startWithWindows", out bool? startup, out error)) return false;

            var overrides = new Dictionary<string, object>(StringComparer.Ordinal);
            var ignored = new List<string>();
            if (root.TryGetProperty("overrides", out var ov) && ov.ValueKind != JsonValueKind.Null)
            {
                if (ov.ValueKind != JsonValueKind.Object) { error = "«overrides» no es un objeto"; return false; }
                foreach (var p in ov.EnumerateObject())
                {
                    if (!SetupSettings.IsKnown(p.Name)) { ignored.Add($"{p.Name}: ajuste desconocido"); continue; }
                    var value = Convert(p.Value, SetupSettings.TypeOf(p.Name)!);
                    if (!SetupSettings.IsValid(p.Name, value)) { ignored.Add($"{p.Name}: valor no válido ({p.Value.GetRawText()})"); continue; }
                    overrides[p.Name] = value!;
                }
            }

            request = new SetupRequest(profile, overrides, live ?? false, startup, ignored);
            error = "";
            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON inválido: {ex.Message}";
            return false;
        }
    }

    private static bool OptionalBool(JsonElement root, string name, out bool? value, out string error)
    {
        value = null;
        error = "";
        if (!root.TryGetProperty(name, out var e) || e.ValueKind == JsonValueKind.Null) return true;
        if (e.ValueKind is JsonValueKind.True or JsonValueKind.False) { value = e.GetBoolean(); return true; }
        error = $"«{name}» no es true/false";
        return false;
    }

    /// <summary>El valor JSON en el tipo del ajuste, o null si no encaja (un número para un bool, un decimal para un int…).</summary>
    private static object? Convert(JsonElement e, Type type)
    {
        if (type == typeof(bool)) return e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : null;
        if (type == typeof(int)) return e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int i) ? i : null;
        if (type == typeof(string)) return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        return null;
    }
}

/// <summary>Cómo acabó <c>--apply-setup</c>.</summary>
public enum SetupStatus
{
    /// <summary>Config aplicada (los hooks pueden haber fallado: ver <see cref="SetupOutcome.HooksError"/>).</summary>
    Applied,
    /// <summary>El JSON del asistente no existe. No se toca nada.</summary>
    NotFound,
    /// <summary>El JSON no vale (no parsea, versión o perfil desconocidos…). No se toca nada.</summary>
    Invalid,
    /// <summary>No se pudo copiar, leer o guardar config.json (bloqueado, disco lleno…). No se toca nada: ni config.json,
    /// ni los hooks, ni el inicio con Windows.</summary>
    Failed,
}

/// <summary>Resultado de <see cref="SetupApply.Run"/>: qué cambió, qué se ignoró, la copia y los hooks.</summary>
public sealed record SetupOutcome(
    SetupStatus Status,
    string Message,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Ignored,
    string? ConfigBackup = null,
    bool CreatedConfig = false,
    bool HooksInstalled = false,
    string? HooksError = null)
{
    /// <summary>0 = aplicado; 1 = aplicado pero los hooks fallaron; 2 = no existe el JSON; 3 = JSON no válido; 4 = no se pudo
    /// copiar, leer o guardar config.json.</summary>
    public int ExitCode => Status switch
    {
        SetupStatus.Applied => HooksError is null ? 0 : 1,
        SetupStatus.NotFound => 2,
        SetupStatus.Invalid => 3,
        _ => 4,
    };

    internal static SetupOutcome Fail(SetupStatus status, string message) =>
        new(status, message, Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>
/// Lo que <see cref="SetupApply"/> hace fuera de config.json, inyectable para los tests: saber si los hooks ya están,
/// instalarlos (el argumento = con la entrada de permisos; devuelve la copia de settings.json y lanza si falla) e iniciar o
/// no con Windows. En la app son <c>HookInstaller</c> y <c>StartupManager</c>, lo mismo que usa el menú de la bandeja.
/// </summary>
public sealed record SetupEffects(Func<bool> HooksInstalled, Func<bool, string> InstallHooks, Action<bool> SetStartWithWindows);

/// <summary>
/// <c>--apply-setup</c>: fusiona lo elegido en el asistente de instalación sobre <c>config.json</c>. Con una config previa
/// solo cambia lo elegido (perfil + respuestas) y antes guarda <c>config.json.bak-setup-&lt;fecha&gt;</c>; sin config, crea
/// una con el perfil y lo recomendado para el equipo. Carga y guarda por <see cref="AppConfig.LoadOrThrow"/> y
/// <see cref="AppConfig.SaveOrThrow"/> (normalización incluida): si config.json no se puede leer o guardar, no se aplica
/// nada. Los hooks solo se instalan si el perfil los usa, Claude Code está instalado y el usuario lo marcó, y después de
/// guardar la config. Al acabar borra el JSON (solo si es el temporal del instalador).
/// </summary>
public static class SetupApply
{
    /// <summary>Prefijo del JSON temporal que escribe el instalador ({tmp}\claudebar-setup.json). Solo esos se borran.</summary>
    public const string TempFilePrefix = "claudebar-setup";

    public static SetupOutcome Run(string requestPath, string configPath, SetupMachine machine, SetupEffects effects, DateTime nowUtc) =>
        Run(requestPath, configPath, machine, effects, nowUtc, Path.GetTempPath());

    /// <param name="tempRoot">La carpeta temporal del usuario (<see cref="Path.GetTempPath"/>; los tests ponen otra): el JSON
    /// solo se borra si está en una carpeta <c>is-*.tmp</c> suya, la <c>{tmp}</c> de Inno.</param>
    internal static SetupOutcome Run(string requestPath, string configPath, SetupMachine machine, SetupEffects effects, DateTime nowUtc,
        string tempRoot)
    {
        if (string.IsNullOrWhiteSpace(requestPath) || !File.Exists(requestPath))
            return SetupOutcome.Fail(SetupStatus.NotFound, $"no existe el archivo del asistente ({requestPath})");
        try
        {
            string json;
            try { json = File.ReadAllText(requestPath); }
            catch (Exception ex) { return SetupOutcome.Fail(SetupStatus.Invalid, $"no se pudo leer el archivo del asistente: {ex.Message}"); }

            if (!SetupRequest.TryParse(json, out var request, out var error))
                return SetupOutcome.Fail(SetupStatus.Invalid, error);
            return Apply(request!, configPath, machine, effects, nowUtc);
        }
        finally
        {
            DeleteIfSetupFile(requestPath, tempRoot);
        }
    }

    /// <summary>
    /// Cambios a aplicar, del menos al más prioritario (el último gana): el perfil, lo recomendado para el equipo (solo si
    /// no había config: con una previa no se pisa lo que no se eligió) y las respuestas del asistente.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, object>> Plan(SetupRequest request, SetupMachine machine, bool configExists)
    {
        var plan = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kv in SetupProfiles.Changes(request.Profile)) plan[kv.Key] = kv.Value;
        if (!configExists)
            foreach (var kv in SetupRecommendations.Values(request.Profile, machine)) plan.TryAdd(kv.Key, kv.Value);
        foreach (var kv in request.Overrides) plan[kv.Key] = kv.Value;
        return plan.ToList();
    }

    /// <param name="load">Lectura de config.json (los tests simulan un bloqueo); por defecto <see cref="AppConfig.LoadOrThrow"/>.</param>
    /// <param name="save">Guardado (los tests simulan un fallo); por defecto <see cref="AppConfig.SaveOrThrow"/>.</param>
    internal static SetupOutcome Apply(SetupRequest request, string configPath, SetupMachine machine, SetupEffects effects, DateTime nowUtc,
        Func<string, AppConfig>? load = null, Action<AppConfig, string>? save = null)
    {
        load ??= AppConfig.LoadOrThrow;
        save ??= (c, path) => c.SaveOrThrow(path);

        bool existed = File.Exists(configPath);
        string? backup = null;
        if (existed)
        {
            try { backup = Backup(configPath, nowUtc); }
            catch (Exception ex)
            {
                return SetupOutcome.Fail(SetupStatus.Failed, $"no se pudo copiar config.json antes de cambiarlo: {ex.Message}");
            }
        }

        // El mismo camino que la app (normaliza y, si config.json no parsea, lo aparta a .bad y sigue con los defaults), salvo
        // que si existe y no se puede leer ahora mismo (antivirus, OneDrive…) no se sigue con los defaults: se guardarían
        // encima de toda la config del usuario. Antes AppConfig.Load los devolvía sin avisar y solo quedaba la copia.
        AppConfig cfg;
        try { cfg = load(configPath); }
        catch (Exception ex)
        {
            return SetupOutcome.Fail(SetupStatus.Failed, $"no se pudo leer config.json: {ex.Message}");
        }

        bool liveBefore = cfg.LiveSessionsEnabled, permissionsBefore = cfg.NotchPermissions;
        var changed = new List<string>();
        foreach (var (key, value) in Plan(request, machine, existed))
            if (SetupSettings.Set(cfg, key, value)) changed.Add(key);
        bool permissionsWanted = cfg.NotchPermissions;

        // Sesiones en vivo: los hooks se instalan igual que desde la bandeja (con backup de settings.json); si ya estaban
        // no se reescribe nada (la app pone al día la entrada de permisos al arrancar, según el ajuste).
        bool hooksBefore = SafeHooksInstalled(effects);
        bool wantLive = request.LiveSessions && SetupRecommendations.OfferLiveSessions(request.Profile, machine);
        bool install = wantLive && !hooksBefore;

        // Primero la config, como si los hooks fueran a quedar puestos: si no se puede guardar, settings.json de Claude Code
        // no se toca (antes los hooks se quedaban instalados con la config sin LiveSessionsEnabled, y salía «aplicado»).
        SetHookDependent(cfg, wantLive, hooks: hooksBefore || install, liveBefore, permissionsWanted);
        try { save(cfg, configPath); }
        catch (Exception ex)
        {
            return SetupOutcome.Fail(SetupStatus.Failed, $"no se pudo guardar config.json: {ex.Message}");
        }

        bool hooks = hooksBefore;
        string? hooksError = null;
        if (install)
        {
            try
            {
                effects.InstallHooks(cfg.NotchEnabled && cfg.NotchPermissions);
                hooks = true;
            }
            catch (Exception ex)
            {
                hooksError = ex.Message;
                // Sin hooks: la config vuelve a lo que vale sin ellos (sin sesiones en vivo ni permisos desde el notch).
                SetHookDependent(cfg, wantLive, hooks: false, liveBefore, permissionsWanted);
                try { save(cfg, configPath); }
                catch (Exception saveEx) { hooksError += $"; y no se pudo quitar de config.json: {saveEx.Message}"; }
            }
        }
        Track(changed, nameof(AppConfig.LiveSessionsEnabled), liveBefore != cfg.LiveSessionsEnabled);
        Track(changed, nameof(AppConfig.NotchPermissions), permissionsBefore != cfg.NotchPermissions);

        if (request.StartWithWindows is bool startup)
        {
            try { effects.SetStartWithWindows(startup); }
            catch (Exception ex) { AppLog.Warn($"asistente: no se pudo cambiar «Iniciar con Windows»: {ex.Message}"); }
        }

        var msg = $"perfil {SetupProfiles.Id(request.Profile)} aplicado ({(existed ? "sobre la config existente" : "config nueva")})";
        return new SetupOutcome(SetupStatus.Applied, msg, changed, request.Ignored, backup, !existed,
            HooksInstalled: hooks && !hooksBefore, HooksError: hooksError);
    }

    /// <summary>Lo que depende de los hooks, según queden puestos o no: las sesiones en vivo se encienden con ellos (si se
    /// pidieron) y los permisos desde el notch sin ellos no tienen dónde engancharse (la bandeja tampoco deja activarlos así).
    /// Normaliza, como al guardar desde la app.</summary>
    private static void SetHookDependent(AppConfig cfg, bool wantLive, bool hooks, bool liveBefore, bool permissionsWanted)
    {
        cfg.LiveSessionsEnabled = liveBefore || (wantLive && hooks);
        cfg.NotchPermissions = permissionsWanted && hooks;
        cfg.Normalize();
    }

    /// <summary>Apunta (o quita) <paramref name="key"/> en lo que cambió según su valor final frente al de la config previa.</summary>
    private static void Track(List<string> changed, string key, bool differs)
    {
        changed.Remove(key);
        if (differs) changed.Add(key);
    }

    private static bool SafeHooksInstalled(SetupEffects effects)
    {
        try { return effects.HooksInstalled(); }
        catch { return false; }
    }

    /// <summary>Copia <c>config.json.bak-setup-yyyyMMdd-HHmmss</c> junto a la config; si ya existe una de ese segundo, -2, -3…</summary>
    internal static string Backup(string configPath, DateTime nowUtc)
    {
        var stamp = nowUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = $"{configPath}.bak-setup-{stamp}";
        for (int n = 2; File.Exists(path); n++) path = $"{configPath}.bak-setup-{stamp}-{n}";
        File.Copy(configPath, path);
        return path;
    }

    /// <summary>Borra el JSON si es el temporal del instalador (<see cref="IsInstallerTempFile"/>): una ruta cualquiera
    /// pasada a mano no se borra nunca, aunque se llame igual.</summary>
    internal static void DeleteIfSetupFile(string path, string tempRoot)
    {
        if (!IsInstallerTempFile(path, tempRoot)) return;
        try { File.Delete(path); }
        catch (Exception ex) { AppLog.Warn($"asistente: no se pudo borrar {path}: {ex.Message}"); }
    }

    /// <summary>¿Es el JSON que deja el instalador? <c>claudebar-setup*.json</c> directamente dentro de una carpeta
    /// <c>is-*.tmp</c> (la <c>{tmp}</c> de Inno) de la carpeta temporal del usuario. Antes bastaba el nombre:
    /// <c>--apply-setup "D:\docs\claudebar-setup-notas.json"</c> borraba ese archivo.</summary>
    internal static bool IsInstallerTempFile(string path, string tempRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(tempRoot) || !IsSetupFileName(path)) return false;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            var parent = dir is null ? null : Path.GetDirectoryName(dir);
            if (dir is null || parent is null) return false;
            var name = Path.GetFileName(dir);
            return name.StartsWith("is-", StringComparison.OrdinalIgnoreCase)
                   && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Path.TrimEndingDirectorySeparator(parent),
                       Path.TrimEndingDirectorySeparator(Path.GetFullPath(tempRoot)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static bool IsSetupFileName(string path)
    {
        var name = Path.GetFileName(path ?? "");
        return name.StartsWith(TempFilePrefix, StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }
}
