using System.Security.Cryptography;
using ClaudeBarWin.Services.Packaging;
using ClaudeBarWin.Shared;

namespace ClaudeBarWin.Services;

/// <summary>
/// v0.4 — copia claudebar-helper.exe (que viaja junto al exe de la app) a %LOCALAPPDATA%\ClaudeBarWin\bin: ruta
/// estable para settings.json entre build de desarrollo e instalada, y una actualización nunca choca con un helper
/// corriendo: el destino viejo se RENOMBRA (NTFS lo permite con el exe en uso) y se borra en arranques posteriores.
/// <para>Edición de la Store (MSIX): no se copia nada. Lo que la app crea en AppData va a una copia privada del paquete que
/// Claude Code no ve, así que los hooks lanzan el helper del paquete por su alias de ejecución
/// (<see cref="StoreEdition.HelperAliasPath()"/>).</para>
/// </summary>
public static class HelperDeployer
{
    public const string ExeName = "claudebar-helper.exe";

    /// <summary>Carpeta de datos compartida con el helper: %CLAUDEBAR_DATA_DIR% (tests/E2E), la del paquete en la edición de
    /// la Store (<see cref="StoreEdition.DataDir"/>) o %LOCALAPPDATA%\ClaudeBarWin.</summary>
    public static string DataDir => ResolveDataDir(Environment.GetEnvironmentVariable(PackageShared.DataDirVariable),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StoreEdition.FamilyName);

    /// <summary>La misma regla que usa el helper (<c>HelperPaths.DataDir</c>): está en el archivo que compilan los dos
    /// (<see cref="PackageShared.DataDir"/>). PURO.</summary>
    internal static string ResolveDataDir(string? custom, string localAppData, string? familyName) =>
        PackageShared.DataDir(custom, localAppData, familyName);

    public static string TargetPath => Path.Combine(DataDir, "bin", ExeName);

    /// <summary>Ruta del helper desplegado tras el último <see cref="Ensure()"/> (null = no disponible).</summary>
    public static string? Current { get; private set; }

    private static bool _storeAliasLogged;

    public static string? Ensure()
    {
        if (StoreEdition.IsActive)
        {
            Current = StoreEdition.HelperAliasPath();
            if (!_storeAliasLogged)
            {
                _storeAliasLogged = true;
                if (Current is null)
                    AppLog.Warn($"edición de la Store: no se encuentra el alias {StoreEdition.HelperAlias} del paquete; los hooks " +
                                "usan el script de PowerShell, sin permisos desde el notch ni statusline");
                else AppLog.Info($"edición de la Store: helper por su alias, {Current}");
            }
            return Current;
        }

        var src = Path.Combine(AppContext.BaseDirectory, ExeName);
        Current = Ensure(File.Exists(src) ? src : null, TargetPath);
        if (Current is null) AppLog.Warn("claudebar-helper.exe no está junto a la app: integración con Claude Code no disponible");
        return Current;
    }

    internal static string? Ensure(string? source, string target) => Ensure(source, target, (from, to) => File.Move(from, to));

    /// <summary>
    /// <paramref name="move"/> es inyectable para probar fallos. Si promover el nuevo falla después de apartar el
    /// viejo (antivirus, cuarentena, lock), el viejo VUELVE a su sitio: settings.json apunta a esta ruta y sin ella la
    /// statusline quedaría en blanco y los hooks mudos (V04-07 de la revisión de Codex).
    /// </summary>
    internal static string? Ensure(string? source, string target, Action<string, string> move)
    {
        string? staged = null, old = null;
        try
        {
            var dir = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(dir);
            CleanOld(dir);
            if (source is null) return File.Exists(target) ? target : null;
            if (File.Exists(target) && SameHash(source, target)) return target;

            staged = $"{target}.{Environment.ProcessId}.{Guid.NewGuid():N}.new";
            File.Copy(source, staged, overwrite: false);
            if (File.Exists(target))
            {
                old = Path.Combine(dir, $"claudebar-helper.old-{DateTime.UtcNow.Ticks}.exe");
                move(target, old);
            }
            move(staged, target);
            staged = null;
            CleanOld(dir);
            return target;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudo desplegar claudebar-helper: {ex.Message}");
            if (old is not null && !File.Exists(target))
            {
                try { File.Move(old, target); } catch (Exception back) { AppLog.Warn($"no se pudo restaurar el helper anterior: {back.Message}"); }
            }
            return File.Exists(target) ? target : null;
        }
        finally
        {
            if (staged is not null) { try { File.Delete(staged); } catch { /* best-effort */ } }
        }
    }

    private static bool SameHash(string a, string b)
    {
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
    }

    private static void CleanOld(string dir)
    {
        foreach (var pattern in new[] { "claudebar-helper.old-*", "claudebar-helper.exe.*.new" })
            foreach (var f in Directory.EnumerateFiles(dir, pattern))
                try { File.Delete(f); } catch { /* aún en uso: la próxima vez */ }
    }
}
