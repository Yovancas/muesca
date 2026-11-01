using ClaudeBarWin.Shared;

namespace ClaudeBarWin.Helper;

/// <summary>Resolves the data directory shared with the ClaudeBar application.</summary>
internal static class HelperPaths
{
    public static string DataDir(Func<string, string?> env) => DataDir(env, null);

    /// <param name="packageFamilyName">Edición de la Store: el paquete del helper (<see cref="HelperPackage.FamilyName"/>). Con
    /// él, los datos que comparte con la app van a la carpeta de datos del paquete (<see cref="PackageShared.PackageDataDir"/>):
    /// una ruta real, la misma con identidad o sin ella, que no depende de la virtualización de AppData y que Windows borra
    /// al desinstalar. La regla está en el archivo que compilan la app y el helper (<see cref="PackageShared"/>).</param>
    public static string DataDir(Func<string, string?> env, string? packageFamilyName) =>
        PackageShared.DataDir(env(PackageShared.DataDirVariable), LocalAppData(env), packageFamilyName);

    public static string LocalAppData(Func<string, string?> env)
    {
        var local = env("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(local))
            local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return local;
    }

    /// <summary>Carpeta de instalación por usuario del instalador de Inno, hasta la 0.4 (DefaultDirName={localappdata}\Programs\ClaudeBarWin).</summary>
    public static string InstallDir(string localAppData) =>
        Path.Combine(localAppData, "Programs", "ClaudeBarWin");

    /// <summary>Desde la 0.5 (Velopack): la app corre desde %LOCALAPPDATA%\ClaudeBar\current, la carpeta que Update.exe
    /// cambia entera en cada actualización (la raíz es la de --packId ClaudeBar, AppLocation.VelopackPackId).</summary>
    public static string VelopackAppDir(string localAppData) =>
        Path.Combine(localAppData, "ClaudeBar", "current");

    public static string RateLimitsDir(Func<string, string?> env) => RateLimitsDir(env, null);

    public static string RateLimitsDir(Func<string, string?> env, string? packageFamilyName) =>
        Path.Combine(DataDir(env, packageFamilyName), "ratelimits");

    public static string RateLimitsDir() => RateLimitsDir(Environment.GetEnvironmentVariable, HelperPackage.FamilyName());
}
