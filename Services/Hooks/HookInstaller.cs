using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClaudeBarWin.Services;

/// <summary>
/// Instala/desinstala el hook de ClaudeBar en ~/.claude/settings.json de forma idempotente,
/// preservando hooks ajenos y haciendo backup. La lógica de merge es pura y testeable.
///
/// v0.3.10 (auditoría cruzada):
///  • Sin PreToolUse ni SubagentStop: ambos dejaban la sesión en "Processing", donde ya estaba, y
///    PreToolUse duplicaba el coste por herramienta (dos powershell por tool call, bloqueantes).
///  • "Exec form" (command + args, Claude Code ≥ 2.1.139): se lanza powershell.exe directamente. En
///    "shell form" Claude Code lo envolvía en Git Bash → dos procesos por evento.
///  • El script y las entradas se ACTUALIZAN solos al arrancar si quedaron viejos (antes un auto-update
///    no tocaba nunca el hook instalado) y la desinstalación de la app los quita (--uninstall-hooks).
///  • Escritura atómica con relectura optimista (sin bloquear el archivo a Claude Code) y sin re-escapar
///    el resto del archivo (antes "español" pasaba a "español" al guardar).
///
/// v0.4: con claudebar-helper.exe desplegado, las entradas lanzan el helper nativo en vez de PowerShell y las del
/// .ps1 se migran solas al arrancar; sin helper se sigue usando el .ps1 (respaldo).
///
/// F2 (opt-in, <c>AppConfig.NotchPermissions</c>): una SEGUNDA entrada, solo en <c>PermissionRequest</c>, lanza
/// <c>claudebar-helper permission</c> con <c>timeout: 45</c> (el helper se rinde a los 25 s y sale sin decisión).
/// Va en su propio bloque (no junto a la entrada <c>hook</c>) para no alargarle el timeout a la de sesiones; esa
/// entrada se mantiene: sigue alimentando la fase de la sesión. Nunca <c>onFailure: "block"</c>.
/// </summary>
public static class HookInstaller
{
    public const string Marker = "claudebar-hook.ps1";

    /// <summary>F2: argumento del modo de permisos del helper.</summary>
    public const string PermissionArg = "permission";

    /// <summary>F2: <c>timeout</c> (segundos) de la entrada de permisos; el plazo interno del helper es 25 s.</summary>
    public const int PermissionTimeoutSeconds = 45;

    private const string PermissionEvent = "PermissionRequest";

    // Shell form del helper: su ruta con nombre EXACTO + " hook" (o " permission") y nada más.
    private static readonly Regex HelperShellHook = new(@"^\s*" + HelperCommand.ExePattern + @"\s+hook\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex HelperShellPermission = new(@"^\s*" + HelperCommand.ExePattern + @"\s+permission\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private enum OurKind { None, Hook, Permission }

    /// <summary>
    /// ¿Es nuestra esta entrada de hook? Reconocimiento ESTRUCTURAL (V04-02/04 de la revisión de Codex): el .ps1
    /// heredado por su nombre de script, o el helper con nombre de ejecutable exacto y argumento exacto «hook».
    /// Antes bastaba con que el texto contuviera la marca: un hook ajeno que la mencionara se borraba al desinstalar.
    /// </summary>
    private static bool IsOurHook(JsonObject h) => Classify(h) != OurKind.None;

    /// <summary>
    /// Qué entrada nuestra es: la de sesiones (<c>hook</c>: .ps1 o helper) o la de permisos (F2: helper con argumento
    /// exacto «permission», en exec form <c>args == ["permission"]</c> o en shell form <c>… permission</c> y nada más).
    /// </summary>
    private static OurKind Classify(JsonObject h)
    {
        string? cmd = h["command"] is JsonValue c && c.TryGetValue<string>(out var cs) ? cs : null;
        var args = h["args"] as JsonArray;
        static bool Str(JsonNode? n, out string s) { s = ""; return n is JsonValue v && v.TryGetValue(out s!); }

        if (cmd is not null && cmd.Contains(Marker)) return OurKind.Hook;                       // .ps1, shell form
        if (args is not null && args.Any(a => Str(a, out var s) && s.Contains(Marker))) return OurKind.Hook; // .ps1, exec form
        if (cmd is null) return OurKind.None;
        if (args is not null)                                                                  // helper, exec form
        {
            if (!HelperCommand.IsHelperExePath(cmd) || args.Count != 1 || !Str(args[0], out var a0)) return OurKind.None;
            return a0 == "hook" ? OurKind.Hook : a0 == PermissionArg ? OurKind.Permission : OurKind.None;
        }
        if (HelperShellHook.IsMatch(cmd)) return OurKind.Hook;                                 // helper, shell form
        return HelperShellPermission.IsMatch(cmd) ? OurKind.Permission : OurKind.None;
    }

    /// <summary>¿Hay alguna entrada nuestra en la sección <c>hooks</c>? (La statusline del tap NO cuenta.)</summary>
    public static bool HasOurHooks(string json)
    {
        try
        {
            if (ClaudeSettingsFile.ParseRoot(json)["hooks"] is not JsonObject hooks) return false;
            return hooks.Any(kv => kv.Value is JsonArray arr && arr.Any(EntryHasMarker));
        }
        catch { return false; }
    }

    internal static readonly string[] Events =
    {
        "UserPromptSubmit", "PostToolUse", "PermissionRequest",
        "Notification", "Stop", "SessionStart", "SessionEnd", "PreCompact",
    };

    /// <summary>Primera versión de Claude Code con hooks en exec form (campo <c>args</c>).</summary>
    internal static readonly Version ExecFormMinVersion = new(2, 1, 139);

    public static string ClaudeDir => ClaudePaths.ConfigDir;
    public static string SettingsPath => ClaudeSettingsFile.SettingsPath;
    public static string HookScriptPath => Path.Combine(ClaudeDir, "hooks", "claudebar-hook.ps1");

    private static string PowerShellExe => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Comando del hook en "shell form" (Claude Code antiguo): una sola cadena.</summary>
    public static string HookCommand() =>
        $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{HookScriptPath}\"";

    /// <summary>Entrada de hook deseada: exec form si la versión de Claude Code lo soporta.</summary>
    public static JsonObject HookSpec(bool execForm, string? scriptPath = null)
    {
        scriptPath ??= HookScriptPath;
        if (!execForm)
            return new JsonObject { ["type"] = "command", ["command"] = HookCommand() };
        return new JsonObject
        {
            ["type"] = "command",
            ["command"] = PowerShellExe,
            ["args"] = new JsonArray("-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath),
        };
    }

    /// <summary>
    /// v0.4: entrada que lanza claudebar-helper.exe (~20 ms por evento frente a ~300 ms de PowerShell). En exec
    /// form la ruta va tal cual (sin shell, sin citas); en shell form se cita para el shell de Claude Code.
    /// </summary>
    public static JsonObject HelperHookSpec(bool execForm, string helperPath, ClaudeShell shell) =>
        execForm
            ? new JsonObject { ["type"] = "command", ["command"] = helperPath, ["args"] = new JsonArray("hook") }
            : new JsonObject { ["type"] = "command", ["command"] = ShellQuote.Command(helperPath, shell) + " hook" };

    /// <summary>
    /// F2: entrada del modo de permisos del helper, con su propio <c>timeout</c> (45 s; el helper sale sin decisión a
    /// los 25 s). Sin <c>onFailure</c>: si el helper falla o no decide, Claude Code muestra su diálogo normal.
    /// </summary>
    public static JsonObject PermissionSpec(bool execForm, string helperPath, ClaudeShell shell)
    {
        var spec = execForm
            ? new JsonObject { ["type"] = "command", ["command"] = helperPath, ["args"] = new JsonArray(PermissionArg) }
            : new JsonObject { ["type"] = "command", ["command"] = ShellQuote.Command(helperPath, shell) + " " + PermissionArg };
        spec["timeout"] = PermissionTimeoutSeconds;
        return spec;
    }

    /// <summary>
    /// F2: entrada de permisos deseada, o null si el ajuste está apagado o no hay helper desplegado (el .ps1 de respaldo
    /// no tiene modo de permisos).
    /// </summary>
    public static JsonObject? DesiredPermissionSpec(string? helperPath, bool permissions) =>
        permissions && helperPath is not null && File.Exists(helperPath)
            ? PermissionSpec(SupportsExecForm(), helperPath, ClaudeShellDetector.Detect())
            : null;

    /// <summary>Helper nativo si está desplegado; si no, el .ps1 de siempre (respaldo: antivirus, build sin helper).</summary>
    public static JsonObject DesiredSpec(string? helperPath) =>
        helperPath is not null && File.Exists(helperPath)
            ? HelperHookSpec(SupportsExecForm(), helperPath, ClaudeShellDetector.Detect())
            : HookSpec(SupportsExecForm());

    private static bool UsesHelper(JsonObject spec) => HelperCommand.Mentions(spec.ToJsonString());

    private static (DateTime Mtime, long Len, bool Installed) _installedCache;

    /// <summary>
    /// Devuelve true si nuestro hook ya está en settings.json. Cacheado por (mtime, tamaño): el panel de
    /// ajustes lo consultaba en CADA repintado leyendo el archivo entero desde el hilo de UI.
    /// </summary>
    public static bool IsInstalled()
    {
        try
        {
            var fi = new FileInfo(SettingsPath);
            if (!fi.Exists) return false;
            var c = _installedCache;
            if (c.Mtime == fi.LastWriteTimeUtc && c.Len == fi.Length) return c.Installed;
            bool installed = HasOurHooks(File.ReadAllText(SettingsPath));
            _installedCache = (fi.LastWriteTimeUtc, fi.Length, installed);
            return installed;
        }
        catch { return false; }
    }

    /// <summary>Merge puro (shell form): compatibilidad con los tests y llamadas antiguas.</summary>
    public static string MergeSettings(string json, string command) =>
        MergeSettings(json, new JsonObject { ["type"] = "command", ["command"] = command });

    /// <summary>Merge puro sin la entrada de permisos (F2 apagado).</summary>
    public static string MergeSettings(string json, JsonObject hookSpec) => MergeSettings(json, hookSpec, null);

    /// <summary>
    /// Merge puro: añade nuestro hook a cada evento sin duplicar ni tocar hooks ajenos. F2: con
    /// <paramref name="permissionSpec"/> añade además, solo en <c>PermissionRequest</c>, un bloque propio con la entrada
    /// de permisos; sin ella (ajuste apagado) quita la nuestra. Una entrada de permisos nuestra en otro evento se quita
    /// siempre. Las ajenas no se tocan.
    /// </summary>
    public static string MergeSettings(string json, JsonObject hookSpec, JsonObject? permissionSpec)
    {
        var root = ClaudeSettingsFile.ParseRoot(json);
        var hooks = root["hooks"] as JsonObject;
        if (hooks is null) { hooks = new JsonObject(); root["hooks"] = hooks; }

        foreach (var ev in Events)
        {
            var arr = hooks[ev] as JsonArray;
            if (arr is null) { arr = new JsonArray(); hooks[ev] = arr; }

            bool wantPermission = permissionSpec is not null && ev == PermissionEvent;
            if (!wantPermission) RemoveEntries(arr, OurKind.Permission);

            if (!arr.Any(e => EntryHas(e, OurKind.Hook)))   // idempotente
            {
                arr.Add(new JsonObject
                {
                    ["matcher"] = "*",
                    ["hooks"] = new JsonArray(hookSpec.DeepClone()),
                });
            }
            if (wantPermission && !arr.Any(e => EntryHas(e, OurKind.Permission)))
            {
                arr.Add(new JsonObject
                {
                    ["matcher"] = "*",
                    ["hooks"] = new JsonArray(permissionSpec!.DeepClone()),
                });
            }
        }
        // Eventos que ya no instalamos: también fuera la entrada de permisos (si alguien la movió ahí).
        foreach (var ev in hooks.Select(kv => kv.Key).Where(k => !Events.Contains(k)).ToList())
        {
            if (hooks[ev] is not JsonArray arr) continue;
            if (RemoveEntries(arr, OurKind.Permission) && arr.Count == 0) hooks.Remove(ev);
        }
        return ClaudeSettingsFile.Serialize(root);
    }

    /// <summary>Quita nuestros hooks de ese tipo (ver <see cref="RemoveOurs"/>). Devuelve si quitó alguno.</summary>
    private static bool RemoveEntries(JsonArray arr, OurKind kind) => RemoveOurs(arr, h => Classify(h) == kind);

    /// <summary>
    /// Quita de cada bloque <c>{"matcher", "hooks": [...]}</c> SOLO los objetos hook que cumplen
    /// <paramref name="isOurs"/>, y el bloque entero únicamente si se queda sin hooks. Antes se borraba el bloque
    /// completo: un hook ajeno que el usuario hubiera puesto en el mismo bloque que el nuestro desaparecía al
    /// desinstalar, al apagar los permisos o al migrar. Devuelve si quitó alguno.
    /// </summary>
    private static bool RemoveOurs(JsonArray arr, Func<JsonObject, bool> isOurs)
    {
        bool removed = false;
        for (int i = arr.Count - 1; i >= 0; i--)
        {
            if (arr[i] is not JsonObject entry || entry["hooks"] is not JsonArray inner) continue;
            bool removedHere = false;
            for (int j = inner.Count - 1; j >= 0; j--)
            {
                if (inner[j] is not JsonObject h || !isOurs(h)) continue;
                inner.RemoveAt(j);
                removedHere = true;
            }
            if (!removedHere) continue;
            removed = true;
            if (inner.Count == 0) arr.RemoveAt(i);   // bloque vacío: fuera; con hooks ajenos se queda
        }
        return removed;
    }

    /// <summary>Quita solo nuestros hooks (reconocidos por su estructura), dejando el resto intacto.</summary>
    public static string RemoveHooks(string json)
    {
        var root = ClaudeSettingsFile.ParseRoot(json);
        if (root["hooks"] is not JsonObject hooks)
            return ClaudeSettingsFile.Serialize(root);

        foreach (var ev in hooks.Select(kv => kv.Key).ToList())
        {
            if (hooks[ev] is not JsonArray arr) continue;
            RemoveOurs(arr, IsOurHook);
            if (arr.Count == 0) hooks.Remove(ev);
        }
        return ClaudeSettingsFile.Serialize(root);
    }

    /// <summary>
    /// JSON deseado si nuestras entradas están desfasadas (otros eventos, shell vs exec form, otra
    /// ruta), o null si ya está al día. Puro: el test cubre la migración desde la v0.3.9.
    /// </summary>
    public static string? UpgradedSettings(string json, JsonObject hookSpec) => UpgradedSettings(json, hookSpec, null);

    /// <summary>
    /// Como <see cref="UpgradedSettings(string, JsonObject)"/> con la entrada de permisos de F2: la añade, la quita o la
    /// pone al día según <paramref name="permissionSpec"/> (null = ajuste apagado). Si los hooks no están instalados no
    /// toca nada: activar el ajuste sin sesiones en vivo no instala hooks.
    /// </summary>
    public static string? UpgradedSettings(string json, JsonObject hookSpec, JsonObject? permissionSpec)
    {
        if (!HasOurHooks(json)) return null; // no instalado: no se toca (la statusline del tap no cuenta)
        // Quitar las nuestras y volver a añadirlas con la spec actual; si el resultado coincide con el
        // archivo (normalizado), ya estaba al día. Las nuestras van al final de cada evento: si estaban
        // en otro orden se reescribe UNA vez y a partir de ahí es estable.
        var desired = MergeSettings(RemoveHooks(json), hookSpec, permissionSpec);
        var normalized = ClaudeSettingsFile.Serialize(ClaudeSettingsFile.ParseRoot(json));
        return desired == normalized ? null : desired;
    }

    /// <summary>
    /// F2: JSON con SOLO la entrada de permisos cambiada (quitada de todos los eventos y, con
    /// <paramref name="permissionSpec"/>, añadida en su bloque de <c>PermissionRequest</c>), o null si ya estaba así o
    /// los hooks no están instalados. Las entradas de sesiones (.ps1 o helper) y las ajenas no se tocan: el interruptor
    /// del ajuste no migra nada. Puro.
    /// </summary>
    public static string? PermissionEntryUpdated(string json, JsonObject? permissionSpec)
    {
        if (!HasOurHooks(json)) return null;
        var root = ClaudeSettingsFile.ParseRoot(json);
        var hooks = (JsonObject)root["hooks"]!;   // HasOurHooks ⇒ existe y es un objeto
        foreach (var ev in hooks.Select(kv => kv.Key).ToList())
        {
            if (hooks[ev] is not JsonArray arr) continue;
            if (RemoveEntries(arr, OurKind.Permission) && arr.Count == 0) hooks.Remove(ev);
        }
        if (permissionSpec is not null)
        {
            if (hooks[PermissionEvent] is not JsonArray arr) { arr = new JsonArray(); hooks[PermissionEvent] = arr; }
            arr.Add(new JsonObject { ["matcher"] = "*", ["hooks"] = new JsonArray(permissionSpec.DeepClone()) });
        }
        var desired = ClaudeSettingsFile.Serialize(root);
        var normalized = ClaudeSettingsFile.Serialize(ClaudeSettingsFile.ParseRoot(json));
        return desired == normalized ? null : desired;
    }

    private static bool EntryHasMarker(JsonNode? entry)
    {
        if (entry is not JsonObject obj || obj["hooks"] is not JsonArray inner) return false;
        return inner.Any(h => h is JsonObject ho && IsOurHook(ho));
    }

    private static bool EntryHas(JsonNode? entry, OurKind kind)
    {
        if (entry is not JsonObject obj || obj["hooks"] is not JsonArray inner) return false;
        return inner.Any(h => h is JsonObject ho && Classify(ho) == kind);
    }

    // ---------------- E/S ----------------

    public static string Install(string hookScriptContents, string backupStamp) => Install(hookScriptContents, backupStamp, null);

    /// <summary>
    /// Instala los hooks (con el helper si está desplegado; si no, escribe el .ps1), hace backup de settings.json
    /// y mergea. Devuelve la ruta del backup.
    /// </summary>
    public static string Install(string hookScriptContents, string backupStamp, string? helperPath) =>
        Install(hookScriptContents, backupStamp, helperPath, permissions: false);

    /// <summary>F2: con <paramref name="permissions"/> (y helper desplegado) instala también la entrada de permisos.</summary>
    public static string Install(string hookScriptContents, string backupStamp, string? helperPath, bool permissions)
    {
        var spec = DesiredSpec(helperPath);
        var perm = DesiredPermissionSpec(helperPath, permissions);
        if (!UsesHelper(spec)) AtomicFile.WriteAllText(HookScriptPath, hookScriptContents);
        return ClaudeSettingsFile.Rewrite(backupStamp, current => MergeSettings(RemoveHooks(current), spec, perm));
    }

    /// <summary>
    /// F2: añade o quita la entrada de permisos según el ajuste (con backup de settings.json) sin tocar el resto. Solo con
    /// los hooks instalados; devuelve la ruta del backup, o null si no había nada que cambiar.
    /// </summary>
    public static string? ApplyPermissions(string backupStamp, string? helperPath, bool permissions)
    {
        if (!IsInstalled()) return null;
        var perm = DesiredPermissionSpec(helperPath, permissions);
        if (PermissionEntryUpdated(File.ReadAllText(SettingsPath), perm) is null) return null;
        return ClaudeSettingsFile.Rewrite(backupStamp, cur => PermissionEntryUpdated(cur, perm) ?? cur);
    }

    /// <summary>Quita el hook de settings.json (con backup) y borra el script.</summary>
    public static string Uninstall(string backupStamp)
    {
        var backup = ClaudeSettingsFile.Rewrite(backupStamp, RemoveHooks);
        try { if (File.Exists(HookScriptPath)) File.Delete(HookScriptPath); } catch { }
        return backup;
    }

    /// <summary>
    /// Al arrancar: si el hook está instalado pero el script o las entradas son de una versión anterior,
    /// los pone al día (con backup). No hace nada si no está instalado o ya está al día.
    /// </summary>
    public static bool UpgradeIfOutdated(string hookScriptContents) => UpgradeIfOutdated(hookScriptContents, null);

    /// <summary>
    /// v0.4: con el helper desplegado migra las entradas del .ps1 al helper y borra el script viejo; sin helper,
    /// mantiene el .ps1 al día como antes.
    /// </summary>
    public static bool UpgradeIfOutdated(string hookScriptContents, string? helperPath) =>
        UpgradeIfOutdated(hookScriptContents, helperPath, permissions: false);

    /// <summary>F2: también pone la entrada de permisos de acuerdo con el ajuste (la añade, la quita o la actualiza).</summary>
    public static bool UpgradeIfOutdated(string hookScriptContents, string? helperPath, bool permissions)
    {
        try
        {
            if (!IsInstalled()) return false;
            bool changed = false;
            var spec = DesiredSpec(helperPath);
            var perm = DesiredPermissionSpec(helperPath, permissions);

            if (!UsesHelper(spec))
            {
                string? onDisk = File.Exists(HookScriptPath) ? File.ReadAllText(HookScriptPath) : null;
                if (onDisk != hookScriptContents)
                {
                    AtomicFile.WriteAllText(HookScriptPath, hookScriptContents);
                    changed = true;
                }
            }

            var current = File.ReadAllText(SettingsPath);
            if (UpgradedSettings(current, spec, perm) is not null)
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                ClaudeSettingsFile.Rewrite(stamp, cur => UpgradedSettings(cur, spec, perm) ?? cur);
                changed = true;
            }
            // Migrado al helper: el .ps1 ya no lo usa nadie (solo tras reescribir settings.json).
            if (UsesHelper(spec) && File.Exists(HookScriptPath))
            {
                try { File.Delete(HookScriptPath); changed = true; } catch { /* en uso: la próxima vez */ }
            }
            if (changed) AppLog.Info("hook de sesiones en vivo actualizado a la versión de esta app");
            return changed;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudo actualizar el hook: {ex.Message}");
            return false;
        }
    }

    /// <summary>¿La versión instalada de Claude Code admite exec form? Si no se puede saber: no (shell form).</summary>
    internal static bool SupportsExecForm() => ClaudeCodeVersion() is { } v && v >= ExecFormMinVersion;

    private static Version? _cachedVersion;

    internal static Version? ClaudeCodeVersion()
    {
        if (_cachedVersion is not null) return _cachedVersion;
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c claude --version")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var outTask = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(8000)) { try { p.Kill(true); } catch { } return null; }
            return _cachedVersion = ParseVersion(outTask.Result);
        }
        catch { return null; }
    }

    internal static Version? ParseVersion(string s)
    {
        var m = Regex.Match(s ?? "", @"(\d+)\.(\d+)\.(\d+)");
        return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    }
}
