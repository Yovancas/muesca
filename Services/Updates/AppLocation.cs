namespace ClaudeBarWin.Services.Updates;

/// <summary>
/// Dónde vive la app instalada y con qué ruta conviene lanzarla. Con Velopack la app corre desde
/// <c>%LOCALAPPDATA%\ClaudeBar\current\ClaudeBarWin.exe</c>, una carpeta que Update.exe sustituye entera en cada
/// actualización; lo que se guarde fuera de la app («Iniciar con Windows») apunta al lanzador estable que vpk deja en la
/// raíz (<see cref="VelopackStubName"/>), que abre la versión instalada y le pasa los argumentos.
/// </summary>
public static class AppLocation
{
    public const string ExeName = "ClaudeBarWin.exe";

    /// <summary><c>--packId</c> de vpk: la carpeta de instalación, %LOCALAPPDATA%\ClaudeBar. A propósito NO es la carpeta de
    /// datos (%LOCALAPPDATA%\ClaudeBarWin, con el helper de los hooks y los snapshots): Update.exe para todo lo que corre en
    /// la suya al actualizar o desinstalar, y Setup.exe la vacía si reinstala. No se cambia nunca.</summary>
    public const string VelopackPackId = "ClaudeBar";

    /// <summary>Lanzador estable de la raíz de Velopack: vpk lo llama como <c>--packTitle</c> + «.exe»
    /// (scripts/release-velopack.ps1 pasa «Muesca», el nombre que se ve en el menú Inicio y en «Aplicaciones»;
    /// ReleaseScriptTests vigila que cuadren). Pasó de «ClaudeBar» a «Muesca» antes de la primera release con Velopack (la
    /// 0.5): ninguna instalación tiene el lanzador viejo. A partir de esa release ya no se cambia (docs/branding.md).</summary>
    public const string VelopackStubName = "Muesca.exe";

    /// <summary>La carpeta que Velopack sustituye en cada actualización.</summary>
    public const string VelopackCurrentDir = "current";

    /// <summary>Argumento con el que arranca la app el inicio con Windows (el lanzador estable lo reenvía).</summary>
    public const string AutostartArg = "--autostart";

    /// <summary>
    /// Raíz de la instalación de Velopack si <paramref name="exePath"/> es la app dentro de su <c>current\</c> (con
    /// Update.exe al lado de esa carpeta); null si es otra cosa (build de desarrollo, la instalación vieja de Inno).
    /// </summary>
    public static string? VelopackRoot(string? exePath, Func<string, bool> fileExists)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        var dir = Path.GetDirectoryName(exePath);
        if (dir is null || !string.Equals(Path.GetFileName(dir), VelopackCurrentDir, StringComparison.OrdinalIgnoreCase))
            return null;
        var root = Path.GetDirectoryName(dir);
        return root is not null && fileExists(Path.Combine(root, "Update.exe")) ? root : null;
    }

    /// <summary>Ruta estable para lanzar la app: el lanzador de la raíz si es una instalación de Velopack que lo tiene; si no,
    /// el propio exe (Inno, desarrollo).</summary>
    public static string? StableLaunchPath(string? exePath, Func<string, bool> fileExists)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (VelopackRoot(exePath, fileExists) is { } root)
        {
            var stub = Path.Combine(root, VelopackStubName);
            if (fileExists(stub)) return stub;
        }
        return exePath;
    }

    /// <summary>
    /// ¿Hay que reapuntar el acceso directo de «Iniciar con Windows» (que apunta a <paramref name="target"/>) a
    /// <paramref name="stable"/>? Sí si su destino ya no existe (v0.3.10), si apunta a la carpeta <c>current\</c> de esta
    /// misma instalación de Velopack, o si apunta a la instalación vieja de Inno (<paramref name="legacyInstallDir"/>)
    /// siendo esta una de Velopack. Uno que apunta a otra copia que sigue ahí (otra build) no se toca.
    /// </summary>
    public static bool StartupNeedsRepoint(string? target, string stable, string? legacyInstallDir, Func<string, bool> fileExists)
    {
        if (string.IsNullOrEmpty(target) || !fileExists(target)) return true;
        if (SamePath(target, stable)) return false;
        var root = VelopackRoot(stable, fileExists)
                   ?? (string.Equals(Path.GetFileName(stable), VelopackStubName, StringComparison.OrdinalIgnoreCase)
                       ? Path.GetDirectoryName(stable) : null);
        if (root is null) return false;
        var targetDir = Path.GetDirectoryName(target);
        if (targetDir is null) return false;
        return SamePath(targetDir, Path.Combine(root, VelopackCurrentDir))
               || (legacyInstallDir is not null && SamePath(targetDir, legacyInstallDir));
    }

    /// <summary>Valor de la clave Run para arrancar con Windows: <c>"ruta" --autostart</c>.</summary>
    public static string AutostartCommand(string exe) => $"\"{exe}\" {AutostartArg}";

    /// <summary>
    /// Exe de un comando de la clave Run (entre comillas, o hasta el primer espacio si no las lleva) y si lleva
    /// <see cref="AutostartArg"/>. Null si no hay comando.
    /// </summary>
    public static (string? Exe, bool Autostart) ParseRunCommand(string? command)
    {
        var c = command?.Trim();
        if (string.IsNullOrEmpty(c)) return (null, false);
        string exe, rest;
        if (c[0] == '"')
        {
            int close = c.IndexOf('"', 1);
            if (close < 0) return (c.Trim('"'), false);
            exe = c[1..close];
            rest = c[(close + 1)..];
        }
        else
        {
            int space = c.IndexOf(' ');
            exe = space < 0 ? c : c[..space];
            rest = space < 0 ? "" : c[space..];
        }
        bool autostart = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(a => string.Equals(a, AutostartArg, StringComparison.OrdinalIgnoreCase));
        return (exe.Length == 0 ? null : exe, autostart);
    }

    /// <summary>¿Hay que reescribir el valor de la clave Run (<paramref name="command"/>)? Si su exe pide reapuntarlo
    /// (<see cref="StartupNeedsRepoint"/>), o si ya es el lanzador estable pero sin <see cref="AutostartArg"/>.</summary>
    public static bool RunEntryNeedsRewrite(string? command, string stable, string? legacyInstallDir, Func<string, bool> fileExists)
    {
        var (exe, autostart) = ParseRunCommand(command);
        if (StartupNeedsRepoint(exe, stable, legacyInstallDir, fileExists)) return true;
        return SamePath(exe!, stable) && !autostart;
    }

    /// <summary>
    /// ¿Es <paramref name="exe"/> de la instalación de Velopack de <paramref name="velopackRoot"/> (su lanzador estable o algo
    /// de su <c>current\</c>)? Al desinstalar, solo se borra lo de «Iniciar con Windows» que apunte aquí: una entrada que
    /// apunte a otra copia (una build de desarrollo, la de Inno) no es de esta. Sin raíz o sin ruta legible, no.
    /// </summary>
    public static bool BelongsToVelopackInstall(string? exe, string? velopackRoot)
    {
        if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(velopackRoot)) return false;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(exe));
            return dir is not null
                   && (SamePath(dir, velopackRoot) || SamePath(dir, Path.Combine(velopackRoot, VelopackCurrentDir)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>¿Apunta el valor de la clave Run (<paramref name="command"/>) a la instalación de Velopack de
    /// <paramref name="velopackRoot"/>? (<see cref="BelongsToVelopackInstall"/>).</summary>
    public static bool RunEntryBelongsTo(string? command, string? velopackRoot) =>
        BelongsToVelopackInstall(ParseRunCommand(command).Exe, velopackRoot);

    /// <summary>¿Se pasa a la clave Run el acceso directo de la carpeta Inicio de hasta la 0.4 (que apunta a
    /// <paramref name="target"/>)? Sí si ya apunta a esta app o pide reapuntarlo; uno que apunta a otra copia válida (otra
    /// build) se deja como está.</summary>
    public static bool LegacyShortcutShouldMigrate(string? target, string stable, string? legacyInstallDir, Func<string, bool> fileExists) =>
        (!string.IsNullOrEmpty(target) && SamePath(target, stable)) || StartupNeedsRepoint(target, stable, legacyInstallDir, fileExists);

    /// <summary>
    /// ¿Lo apagó el usuario en el Administrador de tareas (Inicio)? Windows lo guarda en
    /// <c>Explorer\StartupApproved\Run</c> (o <c>\StartupFolder</c>): el primer byte par es activado, impar desactivado.
    /// Sin valor, activado.
    /// </summary>
    public static bool DisabledByUser(byte[]? startupApproved) =>
        startupApproved is { Length: > 0 } && (startupApproved[0] & 1) != 0;

    /// <summary>Carpeta del instalador de Inno (hasta la 0.4): <c>%LOCALAPPDATA%\Programs\ClaudeBarWin</c>.</summary>
    public static string LegacyInnoInstallDir(string localAppData) => Path.Combine(localAppData, "Programs", "ClaudeBarWin");

    private static bool SamePath(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        StringComparison.OrdinalIgnoreCase);
}
