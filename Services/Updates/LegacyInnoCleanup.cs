using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace ClaudeBarWin.Services.Updates;

/// <summary>
/// Retira la instalación vieja de Inno (hasta la 0.4) desde el hook de instalación de Velopack, igual que el puente
/// (installer/bridge/ClaudeBarWin-Bridge.iss, <c>KillRunningApp</c>, <c>RemoveLegacyInno</c> y <c>RepointShortcuts</c>):
/// cierra la app vieja si sigue abierta, borra sus archivos con nombre conocido, la carpeta si queda vacía, su acceso directo
/// del menú Inicio y su entrada de «Aplicaciones», y reapunta a la app nueva «Iniciar con Windows» y el acceso directo del
/// escritorio si apuntaban a ella. Hace falta también aquí porque alguien con la 0.4 puede instalar la nueva con el Setup.exe
/// de la página de releases, sin pasar por el puente: con las dos en «Aplicaciones», quitar la vieja ejecutaría su
/// desinstalador, cuyo [UninstallRun] (<c>--uninstall-hooks</c>) quita los hooks, devuelve la statusline a su estado original
/// y borra el helper que también usa la app nueva, y nada los vuelve a poner.
/// NUNCA ejecuta ese desinstalador, ni toca la config (%APPDATA%\ClaudeBarWin), los hooks o el helper de
/// %LOCALAPPDATA%\ClaudeBarWin\bin. Sin la clave de Inno no hace nada. Archivos, carpetas vacías, dos accesos directos y una
/// clave del registro, más como mucho <see cref="CloseBudget"/> esperando a que salga la app vieja: dentro de los 15-60 s
/// del hook. Nunca lanza.
/// </summary>
public static class LegacyInnoCleanup
{
    /// <summary>AppId de installer/ClaudeBarWin.iss (sin la llave doble con que Inno escapa la primera).</summary>
    public const string LegacyAppId = "{A7C3E1F2-5B8D-4E9A-9C1F-3D2E4F6A8B0C}";

    /// <summary>Su entrada de «Aplicaciones», en HKCU (se instalaba sin admin).</summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + LegacyAppId + "_is1";

    /// <summary>Lo que dejaba Inno en su carpeta (el puente borra los mismos).</summary>
    internal static readonly string[] AppFiles = [AppLocation.ExeName, "claudebar-helper.exe"];
    internal static readonly string[] UninstallerFiles = ["unins000.exe", "unins000.dat", "unins000.msg"];

    private const string DefaultGroup = "ClaudeBar";
    private const string StartMenuShortcut = "ClaudeBar.lnk";
    /// <summary>{userstartup}\ClaudeBarWin.lnk: «Iniciar con Windows» hasta la 0.4 (StartupManager lo pasa luego a la clave Run).</summary>
    internal const string StartupShortcut = "ClaudeBarWin.lnk";
    /// <summary>{userdesktop}\ClaudeBar.lnk: la tarea opcional desktopicon de installer/ClaudeBarWin.iss.</summary>
    internal const string DesktopShortcut = "ClaudeBar.lnk";

    /// <summary>Cuánto se espera, como mucho y en total, a que salga la app vieja tras cerrarla: el hook es un Fast callback
    /// y Velopack lo mata a los 15-60 s.</summary>
    internal static readonly TimeSpan CloseBudget = TimeSpan.FromSeconds(3);

    /// <summary>Lo que se borra: archivos, carpetas (solo si quedan vacías) y la clave de «Aplicaciones». <paramref name="AppDir"/>:
    /// la carpeta de Inno si es segura (<see cref="IsSafeLegacyDir"/>); null si no se toca.</summary>
    internal sealed record Plan(IReadOnlyList<string> Files, IReadOnlyList<string> EmptyDirs, string? AppDir = null);

    /// <summary>Qué hacer con un acceso directo de fuera de la app (carpeta Inicio, escritorio).</summary>
    internal enum ShortcutFix { Keep, Repoint, Delete }

    /// <summary>
    /// Qué retirar, o null si no hay instalación de Inno (<paramref name="keyExists"/>). Los valores de su clave
    /// (<c>InstallLocation</c>, <c>UninstallString</c>, <c>Inno Setup: Icon Group</c>) pueden faltar: entonces, lo de por
    /// defecto. La carpeta no se toca si no es una ruta absoluta o si es (o está dentro de) la de Velopack
    /// (<paramref name="velopackRoot"/>). Los unins000.* solo si el desinstalador de la clave es justo ese de esa carpeta; si
    /// no, sin la entrada de «Aplicaciones» ya nadie lo lanza.
    /// </summary>
    internal static Plan? Decide(bool keyExists, string? installLocation, string? uninstallString, string? iconGroup,
        string localAppData, string userPrograms, string? velopackRoot)
    {
        if (!keyExists) return null;
        var files = new List<string>();
        var dirs = new List<string>();
        string? appDir = null;

        var dir = string.IsNullOrWhiteSpace(installLocation)
            ? AppLocation.LegacyInnoInstallDir(localAppData)
            : installLocation.Trim().Trim('"');
        if (IsSafeLegacyDir(dir, velopackRoot))
        {
            appDir = dir;
            files.AddRange(AppFiles.Select(f => Path.Combine(dir, f)));
            if (SamePath(AppLocation.ParseRunCommand(uninstallString).Exe, Path.Combine(dir, UninstallerFiles[0])))
                files.AddRange(UninstallerFiles.Select(f => Path.Combine(dir, f)));
            dirs.Add(dir);
        }

        // El acceso directo del menú Inicio ({group}\ClaudeBar.lnk) y su carpeta; el de Velopack va en la raíz del menú.
        var group = IsPlainFolderName(iconGroup) ? iconGroup!.Trim() : DefaultGroup;
        var groupDir = Path.Combine(userPrograms, group);
        files.Add(Path.Combine(groupDir, StartMenuShortcut));
        dirs.Add(groupDir);
        return new Plan(files, dirs, appDir);
    }

    /// <summary>
    /// ¿Se cierra este proceso ClaudeBarWin (exe <paramref name="exePath"/>) antes de borrar? Solo si su exe está dentro de la
    /// carpeta de Inno (<paramref name="innoDir"/>): con la app vieja abierta su exe está bloqueado y quedaría una copia
    /// huérfana. Nunca el propio proceso (<paramref name="currentPid"/>) ni nada de la carpeta de Velopack, ni uno cuya ruta
    /// no se pudo leer.
    /// </summary>
    internal static bool ShouldClose(string? exePath, int pid, int currentPid, string? innoDir, string? velopackRoot) =>
        pid != currentPid && !string.IsNullOrWhiteSpace(innoDir)
        && IsInside(exePath, innoDir) && !IsInside(exePath, velopackRoot);

    /// <summary>
    /// Un acceso directo que apunta a <paramref name="target"/>: si apunta a la carpeta de Inno (<paramref name="innoDir"/>),
    /// se reapunta al lanzador de la app nueva (<paramref name="launcher"/>) o, sin él, se borra (roto, no lleva a nada). Uno
    /// que apunta a otro sitio, o cuyo destino no se pudo leer, se deja.
    /// </summary>
    internal static ShortcutFix DecideShortcut(string? target, string? innoDir, string? launcher)
    {
        if (string.IsNullOrWhiteSpace(innoDir) || !IsInside(target, innoDir)) return ShortcutFix.Keep;
        return !string.IsNullOrWhiteSpace(launcher) && !IsInside(launcher, innoDir) ? ShortcutFix.Repoint : ShortcutFix.Delete;
    }

    /// <summary>Desde el hook de instalación de Velopack (<see cref="VelopackStartup"/>), con la app nueva ya en su carpeta.</summary>
    public static void RetireIfPresent()
    {
        try
        {
            string? location, uninstall, group;
            using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                if (key is null) return;
                location = key.GetValue("InstallLocation") as string;
                uninstall = key.GetValue("UninstallString") as string;
                group = key.GetValue("Inno Setup: Icon Group") as string;
            }
            var velopackRoot = AppLocation.VelopackRoot(Environment.ProcessPath, File.Exists);
            var plan = Decide(keyExists: true, location, uninstall, group,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                velopackRoot);
            if (plan is null) return;

            // Con la app vieja abierta (Setup.exe directo, sin el puente que la cierra) su exe está bloqueado.
            CloseLegacyApp(plan.AppDir, velopackRoot);

            foreach (var file in plan.Files)
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (Exception ex) { AppLog.Warn($"velopack: no se pudo borrar {file} de la instalación de Inno: {ex.Message}"); }
            }
            foreach (var dir in plan.EmptyDirs)
            {
                try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                catch (Exception ex) { AppLog.Warn($"velopack: no se pudo quitar la carpeta {dir}: {ex.Message}"); }
            }
            // El lanzador estable de la raíz (o, si aún no está, la app de current\, que el arranque reapunta después).
            var launcher = velopackRoot is null ? null : AppLocation.StableLaunchPath(Environment.ProcessPath, File.Exists);
            // «Iniciar con Windows» de la 0.4: reapuntado a la app nueva, el arranque lo pasa a la clave Run conservando si
            // el usuario lo tenía apagado en el Administrador de tareas (StartupManager.RepairIfStale).
            RepairShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcut),
                plan.AppDir, launcher, AppLocation.AutostartArg, Brand.Name);
            // El del escritorio (tarea desktopicon del .iss viejo): sin esto quedaría roto. Velopack no crea uno ahí
            // (--shortcuts StartMenuRoot), así que no pisa nada suyo.
            RepairShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DesktopShortcut),
                plan.AppDir, launcher, "", Brand.Name);
            // La entrada de «Aplicaciones» se quita aunque quede algún archivo (la app vieja abierta): lo que importa es que
            // nadie pueda lanzar su desinstalador.
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
            AppLog.Info($"velopack: retirada la instalación de Inno ({location ?? "carpeta por defecto"}) sin su desinstalador");
        }
        catch (Exception ex) { AppLog.Warn($"velopack: no se pudo retirar la instalación de Inno: {ex.Message}"); }
    }

    /// <summary>
    /// Cierra (a la fuerza, como el taskkill /F del puente) los ClaudeBarWin que corren desde la carpeta de Inno
    /// (<see cref="ShouldClose"/>) y espera a que salgan, como mucho <see cref="CloseBudget"/> entre todos. Si alguno no sale
    /// a tiempo, el borrado de su exe fallará y quedará en el log, como antes.
    /// </summary>
    private static void CloseLegacyApp(string? innoDir, string? velopackRoot)
    {
        if (string.IsNullOrWhiteSpace(innoDir)) return;
        var closing = new List<Process>();
        try
        {
            int self = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppLocation.ExeName)))
            {
                bool close;
                try { close = ShouldClose(p.MainModule?.FileName, p.Id, self, innoDir, velopackRoot); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { close = false; }
                if (!close) { p.Dispose(); continue; }
                try
                {
                    p.Kill();
                    closing.Add(p);
                    AppLog.Info($"velopack: cerrada la app de Inno que seguía abierta (pid {p.Id})");
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    AppLog.Warn($"velopack: no se pudo cerrar la app de Inno (pid {p.Id}): {ex.Message}");
                    p.Dispose();
                }
            }
            var deadline = Stopwatch.StartNew();
            foreach (var p in closing)
            {
                var left = CloseBudget - deadline.Elapsed;
                if (left <= TimeSpan.Zero || !p.WaitForExit(left))
                {
                    AppLog.Warn($"velopack: la app de Inno (pid {p.Id}) no ha salido a tiempo");
                    break;
                }
            }
        }
        catch (Exception ex) { AppLog.Warn($"velopack: no se pudo mirar si la app de Inno seguía abierta: {ex.Message}"); }
        finally
        {
            foreach (var p in closing) p.Dispose();
        }
    }

    /// <summary>Reapunta o borra el acceso directo <paramref name="lnk"/> si apunta a la carpeta de Inno (<see cref="DecideShortcut"/>).</summary>
    private static void RepairShortcut(string lnk, string? innoDir, string? launcher, string arguments, string description)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(innoDir) || !File.Exists(lnk)) return;
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            switch (DecideShortcut((string)shortcut.TargetPath, innoDir, launcher))
            {
                case ShortcutFix.Repoint:
                    shortcut.TargetPath = launcher!;
                    shortcut.Arguments = arguments;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(launcher!) ?? "";
                    shortcut.IconLocation = launcher + ",0";
                    shortcut.Description = description;
                    shortcut.Save();
                    AppLog.Info($"velopack: {lnk} apuntaba a la instalación de Inno; reapuntado a {launcher}");
                    break;
                case ShortcutFix.Delete:
                    File.Delete(lnk);
                    AppLog.Info($"velopack: {lnk} apuntaba a la instalación de Inno y no hay a qué reapuntarlo; borrado");
                    break;
            }
        }
        catch (Exception ex) { AppLog.Warn($"velopack: no se pudo revisar el acceso directo {lnk}: {ex.Message}"); }
    }

    /// <summary>¿Está <paramref name="path"/> dentro de <paramref name="dir"/> (a cualquier profundidad)? Rutas ilegibles o
    /// relativas, no.</summary>
    private static bool IsInside(string? path, string? dir)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(dir)) return false;
        try
        {
            if (!Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(dir)) return false;
            var full = Path.GetFullPath(path);
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            return full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>Una ruta absoluta que no es la carpeta de Velopack ni está dentro de ella, ni la raíz de una unidad.</summary>
    internal static bool IsSafeLegacyDir(string dir, string? velopackRoot)
    {
        try
        {
            if (!Path.IsPathFullyQualified(dir)) return false;
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            if (Path.GetPathRoot(full) is { } root && SamePath(root, full)) return false;
            if (string.IsNullOrWhiteSpace(velopackRoot)) return true;
            var velopack = Path.TrimEndingDirectorySeparator(Path.GetFullPath(velopackRoot));
            return !SamePath(full, velopack)
                   && !full.StartsWith(velopack + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>Un nombre de carpeta del menú Inicio sin rutas: nada de «..», separadores ni caracteres no válidos.</summary>
    private static bool IsPlainFolderName(string? name)
    {
        var n = name?.Trim();
        return !string.IsNullOrEmpty(n) && n != "." && n != ".." && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
