using ClaudeBarWin.Services.Packaging;
using ClaudeBarWin.Services.Updates;
using Microsoft.Win32;

namespace ClaudeBarWin.Services;

/// <summary>
/// Cómo está «Iniciar con Windows» para pintarlo y para decidir. <paramref name="On"/> null = <b>desconocido</b> (edición de
/// la Store antes de la primera lectura de la tarea de inicio), que no es «apagado»; <paramref name="Changing"/> = hay un
/// cambio en marcha (edición de la Store; el pill enseña el último estado conocido mientras tanto).
/// </summary>
public readonly record struct StartupState(bool? On, bool Changing)
{
    /// <summary>¿Se sabe cómo está?</summary>
    public bool Known => On is not null;

    /// <summary>Desde dónde se puede aplicar algo: el estado si se sabe y no está cambiando; si no, null (no aplicar).</summary>
    public bool? Applicable => Changing ? null : On;
}

/// <summary>
/// "Start with Windows": no admin, fully reversible (the toggle just deletes the entry).
/// v0.5 (Velopack): un valor en HKCU\Software\Microsoft\Windows\CurrentVersion\Run con el lanzador estable de la raíz de la
/// instalación y <c>--autostart</c> (<see cref="AppLocation.AutostartCommand"/>), no la carpeta current\ que Update.exe
/// cambia en cada actualización. Velopack no lo borra al desinstalar: lo hace su hook (<see cref="UninstallCleanup"/>).
/// Hasta la 0.4 era un acceso directo en la carpeta Inicio; al arrancar se pasa a la clave Run (<see cref="RepairIfStale"/>).
/// <para>Edición de la Store (MSIX): la tarea de inicio del paquete (<see cref="StoreStartupTask"/>). Lo que una app empaquetada
/// escribe en HKCU va a una copia privada del paquete, así que la clave Run no serviría; y no hay nada que reparar al arrancar
/// ni que quitar al desinstalar: Windows lo gestiona.</para>
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    /// <summary>Lo que el usuario apagó en el Administrador de tareas (Inicio): <see cref="AppLocation.DisabledByUser"/>.</summary>
    private const string ApprovedRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolderKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    /// <summary>Nombre del valor en la clave Run.</summary>
    public const string RunValueName = "ClaudeBarWin";
    private const string LegacyShortcutName = "ClaudeBarWin.lnk";

    private static string LegacyShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), LegacyShortcutName);

    /// <summary>¿Arrancó esta ejecución con Windows (con <see cref="AppLocation.AutostartArg"/>)? Lo pone <c>Main</c>.</summary>
    public static bool StartedWithWindows { get; internal set; }

    /// <summary>Lo que se pinta en Ajustes: en la edición de la Store, la tarea de inicio (puede ser desconocida o estar
    /// cambiando); en la de GitHub, la clave Run, que se lee al momento.</summary>
    public static StartupState State() =>
        StoreEdition.IsActive ? StoreStartupTask.State() : new StartupState(IsEnabledInRegistry(), Changing: false);

    /// <summary>¿Arranca con Windows? null si no se sabe ahora (comprobando o cambiando): quien lo use no debe aplicar nada
    /// desde ahí (la bienvenida no lo toca si no se toca el interruptor).</summary>
    public static bool? IsEnabled() => State().Applicable;

    /// <summary>La CLI (<c>--apply-setup</c>) espera, con plazo, a que acabe un cambio en marcha antes de salir.</summary>
    public static void WaitForPendingChange(TimeSpan timeout)
    {
        if (StoreEdition.IsActive && !StoreStartupTask.WaitForPendingChange(timeout))
            AppLog.Warn("inicio con Windows (Store): el cambio no acabó a tiempo");
    }

    /// <summary>Encendido si hay entrada (la de la clave Run o el acceso directo de la 0.4) y el usuario no la apagó en el
    /// Administrador de tareas.</summary>
    private static bool IsEnabledInRegistry()
    {
        try
        {
            if (ReadRunCommand() is not null) return !AppLocation.DisabledByUser(ReadApproved(ApprovedRunKey, RunValueName));
            return File.Exists(LegacyShortcutPath)
                   && !AppLocation.DisabledByUser(ReadApproved(ApprovedFolderKey, LegacyShortcutName));
        }
        catch { return false; }
    }

    public static void Toggle()
    {
        if (StoreEdition.IsActive)
        {
            StoreStartupTask.Toggle();
            return;
        }
        if (IsEnabledInRegistry()) Disable();
        else Enable();
    }

    /// <summary>Lo pide el usuario: escribe la entrada y, si la tenía apagada en el Administrador de tareas, la vuelve a encender.</summary>
    public static void Enable()
    {
        if (StoreEdition.IsActive)
        {
            StoreStartupTask.Enable();
            return;
        }
        if (!Write()) return;
        try { DeleteApproved(ApprovedRunKey, RunValueName); }
        catch (Exception ex) { AppLog.Warn($"inicio con Windows: no se pudo reactivar en el Administrador de tareas: {ex.Message}"); }
    }

    /// <summary>
    /// v0.3.10: la entrada guardaba la ruta del exe que la activó y no se revisaba nunca; si esa ruta desaparece
    /// (reinstalación en otra carpeta, build de desarrollo borrada) Windows no arrancaba la app. Solo se reapunta si el
    /// destino YA NO EXISTE, para no secuestrar una entrada que apunta a otra copia válida. v0.5: también si apunta a la
    /// carpeta current\ de Velopack o a la instalación vieja de Inno, o si le falta --autostart
    /// (<see cref="AppLocation.RunEntryNeedsRewrite"/>); y el acceso directo de la carpeta Inicio de la 0.4 pasa a la clave
    /// Run. Sin tocar lo que el usuario apagó en el Administrador de tareas.
    /// </summary>
    public static void RepairIfStale()
    {
        if (StoreEdition.IsActive) return;
        try
        {
            var stable = AppLocation.StableLaunchPath(Environment.ProcessPath, File.Exists);
            if (string.IsNullOrEmpty(stable)) return;
            var legacy = AppLocation.LegacyInnoInstallDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

            if (ReadRunCommand() is { } command)
            {
                if (AppLocation.RunEntryNeedsRewrite(command, stable, legacy, File.Exists))
                {
                    AppLog.Info($"inicio con Windows: {command}; se reapunta a {stable}");
                    Write();
                }
                // Con la entrada de la clave Run, el acceso directo de la 0.4 sobra (arrancaría dos veces).
                else if (File.Exists(LegacyShortcutPath) && AppLocation.LegacyShortcutShouldMigrate(ShortcutTarget(), stable, legacy, File.Exists))
                    DeleteLegacyShortcut();
                return;
            }

            if (!File.Exists(LegacyShortcutPath)) return;
            var target = ShortcutTarget();
            if (!AppLocation.LegacyShortcutShouldMigrate(target, stable, legacy, File.Exists)) return;
            // Si estaba apagado en el Administrador de tareas, sigue apagado en la clave Run.
            var approved = ReadApproved(ApprovedFolderKey, LegacyShortcutName);
            AppLog.Info($"inicio con Windows: el acceso directo de la carpeta Inicio ({target}) pasa a la clave Run");
            if (!Write()) return;
            if (AppLocation.DisabledByUser(approved)) WriteApproved(ApprovedRunKey, RunValueName, approved!);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"inicio con Windows: no se pudo revisar la entrada: {ex.Message}");
        }
    }

    public static void Disable()
    {
        if (StoreEdition.IsActive)
        {
            StoreStartupTask.Disable();
            return;
        }
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            run?.DeleteValue(RunValueName, throwOnMissingValue: false);
            DeleteApproved(ApprovedRunKey, RunValueName);
        }
        catch (Exception ex) { AppLog.Warn($"inicio con Windows: no se pudo quitar la entrada: {ex.Message}"); }
        DeleteLegacyShortcut();
    }

    /// <summary>
    /// Al desinstalar la de Velopack (<see cref="UninstallCleanup"/>): quita la entrada de la clave Run y el acceso directo de
    /// la 0.4 solo si apuntan a esta instalación (<see cref="AppLocation.BelongsToVelopackInstall"/>). Uno que apunta a otra
    /// copia (una build de desarrollo, la de Inno que siga instalada) es de esa y se deja.
    /// </summary>
    public static void RemoveIfOwnedBy(string? velopackRoot)
    {
        if (StoreEdition.IsActive) return;
        try
        {
            if (ReadRunCommand() is { } command)
            {
                if (AppLocation.RunEntryBelongsTo(command, velopackRoot))
                {
                    using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                        run?.DeleteValue(RunValueName, throwOnMissingValue: false);
                    DeleteApproved(ApprovedRunKey, RunValueName);
                }
                else AppLog.Info($"inicio con Windows: la entrada es de otra copia ({command}); se deja");
            }
        }
        catch (Exception ex) { AppLog.Warn($"inicio con Windows: no se pudo quitar la entrada: {ex.Message}"); }
        try
        {
            if (File.Exists(LegacyShortcutPath) && AppLocation.BelongsToVelopackInstall(ShortcutTarget(), velopackRoot))
                DeleteLegacyShortcut();
        }
        catch (Exception ex) { AppLog.Warn($"inicio con Windows: no se pudo revisar el acceso directo de la carpeta Inicio: {ex.Message}"); }
    }

    /// <summary>Escribe la clave Run con el lanzador estable y --autostart y quita el acceso directo de la 0.4.</summary>
    private static bool Write()
    {
        try
        {
            var exe = AppLocation.StableLaunchPath(Environment.ProcessPath, File.Exists);
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return false; // only meaningful for the published exe, not `dotnet exec`
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true))
                run.SetValue(RunValueName, AppLocation.AutostartCommand(exe), RegistryValueKind.String);
            DeleteLegacyShortcut();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"inicio con Windows: no se pudo escribir la entrada: {ex.Message}");
            return false;
        }
    }

    private static string? ReadRunCommand()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        return run?.GetValue(RunValueName) as string;
    }

    private static byte[]? ReadApproved(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name) as byte[];
    }

    private static void WriteApproved(string key, string name, byte[] value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key, writable: true);
        k.SetValue(name, value, RegistryValueKind.Binary);
    }

    private static void DeleteApproved(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>Destino del acceso directo de la carpeta Inicio de la 0.4 ("" si no se puede leer).</summary>
    private static string ShortcutTarget()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) return "";
        dynamic shell = Activator.CreateInstance(type)!;
        return (string)shell.CreateShortcut(LegacyShortcutPath).TargetPath;
    }

    private static void DeleteLegacyShortcut()
    {
        try
        {
            if (File.Exists(LegacyShortcutPath)) File.Delete(LegacyShortcutPath);
            DeleteApproved(ApprovedFolderKey, LegacyShortcutName);
        }
        catch (Exception ex) { AppLog.Warn($"inicio con Windows: no se pudo quitar el acceso directo de la carpeta Inicio: {ex.Message}"); }
    }
}
