namespace ClaudeBarWin.Services;

/// <summary>
/// «Quitar la integración con Claude Code» (Ajustes): de una vez, con una sola copia de seguridad, quita de
/// <c>~/.claude/settings.json</c> solo lo de Muesca: los hooks (sesiones en vivo y permisos desde el notch,
/// <see cref="HookInstaller"/>) y el enganche de la statusline (<see cref="StatuslineInstaller"/>: vuelve la del usuario tal
/// cual, o se quita la nuestra). Lo ajeno no se toca: el reconocimiento es el mismo, estructural y con nombre exacto, que al
/// apagar cada cosa. Pensado para antes de desinstalar la edición de la Store, que no ejecuta nada nuestro al desinstalar
/// (docs/msix.md); la ficha de la Store lo pide en «Antes de desinstalar».
/// </summary>
public static class ClaudeIntegration
{
    /// <summary>¿Hay algo de Muesca en este settings.json (hooks o statusline)? PURO.</summary>
    public static bool HasAny(string json) =>
        HookInstaller.HasOurHooks(json) || StatuslineInstaller.State(json) != TapState.NotInstalled;

    /// <summary>El mismo settings.json sin nada de Muesca. PURO.</summary>
    public static string RemoveAll(string json) => StatuslineInstaller.Disable(HookInstaller.RemoveHooks(json));

    /// <summary>¿Hay algo de Muesca en el settings.json de verdad? (Cacheado por archivo, como cada parte.)</summary>
    public static bool IsInstalled() => HookInstaller.IsInstalled() || StatuslineInstaller.IsInstalled();

    /// <summary>
    /// Lo quita del settings.json de verdad (lectura, copia de seguridad y escritura atómica: <see cref="ClaudeSettingsFile.Rewrite"/>)
    /// y retira el script de respaldo de los hooks. Devuelve la ruta de la copia, o null si no había nada de Muesca (entonces no
    /// se escribe nada).
    /// </summary>
    public static string? Remove(string backupStamp)
    {
        string? backup = null;
        var path = ClaudeSettingsFile.SettingsPath;
        bool any;
        try { any = File.Exists(path) && HasAny(File.ReadAllText(path)); }
        catch (IOException) { any = true; }   // ocupado ahora: Rewrite lo lee compartido y lo reintenta
        if (any) backup = ClaudeSettingsFile.Rewrite(backupStamp, RemoveAll);
        try { if (File.Exists(HookInstaller.HookScriptPath)) File.Delete(HookInstaller.HookScriptPath); }
        catch (Exception ex) { AppLog.Warn($"integración: no se pudo borrar {HookInstaller.HookScriptPath}: {ex.Message}"); }
        return backup;
    }
}
