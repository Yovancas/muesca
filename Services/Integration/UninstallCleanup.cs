namespace ClaudeBarWin.Services;

/// <summary>
/// Lo que se deshace al desinstalar: el hook de desinstalación de Velopack y <c>--uninstall-hooks</c> (el [UninstallRun] del
/// instalador de Inno). v0.3.10: sin esto los hooks se quedaban puestos y cada evento de Claude Code seguía lanzando un
/// proceso para siempre, sin nadie escuchando. v0.4: también devuelve la statusline del usuario a su estado original y
/// retira el helper. Nunca lanza: una desinstalación no puede fallar por esto. No toca %APPDATA%\ClaudeBarWin (config e
/// historial).
/// </summary>
public static class UninstallCleanup
{
    /// <param name="removeStartupShortcut">«Iniciar con Windows». Inno borraba el acceso directo con su [UninstallDelete];
    /// con Velopack, la entrada de la clave Run la quita este hook (Velopack no la toca y sin la app apuntaría a nada), pero
    /// solo si apunta a esta instalación: la de otra copia se deja.</param>
    public static void Run(bool removeStartupShortcut)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        try
        {
            if (HookInstaller.IsInstalled())
                HookInstaller.Uninstall(stamp);
        }
        catch (Exception ex) { AppLog.Warn($"desinstalación: no se pudieron quitar los hooks: {ex.Message}"); }
        try
        {
            if (StatuslineInstaller.IsInstalled())
                ClaudeSettingsFile.Rewrite(stamp + "-sl", StatuslineInstaller.Disable);
        }
        catch (Exception ex) { AppLog.Warn($"desinstalación: no se pudo restaurar la statusline: {ex.Message}"); }
        try
        {
            new RateLimitSnapshotStore(RateLimitSnapshotStore.DefaultDir).Clear();
            if (File.Exists(HelperDeployer.TargetPath)) File.Delete(HelperDeployer.TargetPath);
            // Las carpetas, solo si se han quedado vacías (la de datos no es la de Velopack: no la borra nadie más).
            foreach (var dir in new[] { Path.GetDirectoryName(HelperDeployer.TargetPath)!, RateLimitSnapshotStore.DefaultDir, HelperDeployer.DataDir })
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (Exception ex) { AppLog.Warn($"desinstalación: no se pudo retirar el helper: {ex.Message}"); }
        // «Iniciar con Windows»: la clave Run (Velopack no la borra) y el acceso directo de la 0.4, si son de esta instalación.
        if (removeStartupShortcut)
            StartupManager.RemoveIfOwnedBy(Updates.AppLocation.VelopackRoot(Environment.ProcessPath, File.Exists));
    }
}
