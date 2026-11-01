using ClaudeBarWin.Services.Licensing;

namespace ClaudeBarWin.Services.Packaging;

/// <summary>
/// La edición de la Store (MSIX; docs/msix.md): la que corre con identidad de paquete. Lo que cambia respecto a la de GitHub:
/// <list type="bullet">
/// <item><b>Helper:</b> no se copia a AppData (MSIX virtualiza lo que se crea ahí y Claude Code no lo vería). Viaja dentro
/// del paquete y los hooks lo lanzan por su alias de ejecución (<see cref="HelperAlias"/>, declarado en
/// installer/msix/Package.appxmanifest): <see cref="HelperAliasPath()"/>.</item>
/// <item><b>Datos compartidos con el helper</b> (snapshots de la statusline): en la carpeta de datos del paquete
/// (<see cref="DataDir"/>), una ruta real que el helper calcula igual con o sin identidad.</item>
/// <item><b>Iniciar con Windows:</b> la tarea de inicio del paquete (<see cref="StartupTaskId"/>), no la clave Run (HKCU
/// también se virtualiza).</item>
/// <item><b>Actualizaciones:</b> las da la Store; Velopack no se usa. Tras actualizarla, Windows la vuelve a abrir
/// (<see cref="StoreRestart"/>).</item>
/// <item><b>Pro:</b> por <c>StoreContext</c> (<see cref="ProLicenseFactory"/>).</item>
/// </list>
/// </summary>
public static class StoreEdition
{
    /// <summary>Alias de ejecución del helper en el manifiesto (MsixManifestTests vigila que coincidan).</summary>
    public const string HelperAlias = "muesca-helper.exe";

    /// <summary>TaskId de la tarea de inicio del manifiesto.</summary>
    public const string StartupTaskId = "MuescaStartup";

    private static readonly Lazy<bool> Active = new(PackageIdentity.HasIdentity);
    private static readonly Lazy<string?> Family = new(() => Active.Value ? PackageIdentity.FamilyName() : null);
    private static readonly Lazy<string?> Install = new(() => Active.Value ? PackageIdentity.InstallPath() : null);

    /// <summary>¿Es la edición de la Store? (identidad de paquete; se mira una vez).</summary>
    public static bool IsActive => Active.Value;

    /// <summary>Package family name; null fuera de la Store.</summary>
    public static string? FamilyName => Family.Value;

    /// <summary>Carpeta de instalación del paquete; null fuera de la Store.</summary>
    public static string? InstallPath => Install.Value;

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Carpeta de datos que la app comparte con el helper: <c>%LOCALAPPDATA%\Packages\&lt;familia&gt;\LocalState\ClaudeBarWin</c>
    /// (dentro de la de <c>ApplicationData.LocalFolder</c>). El helper calcula la misma porque la regla está en un archivo que
    /// compilan los dos (<see cref="ClaudeBarWin.Shared.PackageShared.PackageDataDir"/>): los dos escriben y leen en la
    /// carpeta real, sin la virtualización de AppData en medio, y Windows la borra al desinstalar. PURO.
    /// </summary>
    public static string DataDir(string localAppData, string familyName) =>
        ClaudeBarWin.Shared.PackageShared.PackageDataDir(localAppData, familyName);

    /// <summary>La ruta del helper para los hooks y la statusline (<see cref="ResolveHelperAlias"/>); null fuera de la Store
    /// o si el alias no está.</summary>
    public static string? HelperAliasPath() =>
        FamilyName is { } family ? ResolveHelperAlias(LocalAppData, family, File.Exists, AppExecLink.TryRead) : null;

    /// <summary>
    /// Qué alias del helper se escribe en settings.json. Windows deja el alias en <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>
    /// (la carpeta que está en el PATH) y también en una subcarpeta con la familia del paquete. Solo vale la de la familia: es
    /// solo de este paquete. La general no se usa nunca: otro paquete puede declarar el mismo alias más tarde («If multiple
    /// apps register for the same alias, the system will invoke the last one that was registered») y Claude Code lanzaría su
    /// exe, también para contestar permisos, hasta el siguiente arranque. Además, si se puede leer el destino del alias de la
    /// familia (formato sin documentar), tiene que ser el helper de este paquete. Sin él: null (los hooks usan el script de
    /// PowerShell). PURO.
    /// </summary>
    internal static string? ResolveHelperAlias(string localAppData, string familyName, Func<string, bool> exists,
        Func<string, AppExecLinkInfo?> readLink)
    {
        var own = Path.Combine(localAppData, "Microsoft", "WindowsApps", familyName, HelperAlias);
        if (!exists(own)) return null;
        var link = readLink(own);
        return link is null || link.Launches(familyName, HelperDeployer.ExeName) ? own : null;
    }

    /// <summary>Para el log al arrancar: familia, carpeta de datos y alias del helper.</summary>
    public static string Describe() =>
        $"edición de la Store: paquete {FamilyName ?? "?"}, instalado en {InstallPath ?? "?"}, helper {HelperAliasPath() ?? "sin alias"}";

    /// <summary>
    /// Al arrancar la edición de la Store: lo anota en el log y comprueba que la carpeta de datos que calcula el helper está
    /// dentro de la que da Windows (<c>ApplicationData.Current.LocalFolder</c>). Si no, lo avisa en el log: los snapshots
    /// de la statusline no llegarían a la app.
    /// </summary>
    public static void LogStartup()
    {
        AppLog.Info(Describe());
        if (FamilyName is not { } family) return;
        try
        {
            var expected = Path.GetDirectoryName(DataDir(LocalAppData, family))!;
            var actual = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase))
                AppLog.Warn($"edición de la Store: LocalFolder es {actual} y no {expected}; el helper no compartirá los snapshots");
        }
        catch (Exception ex) { AppLog.Warn($"edición de la Store: no se pudo leer ApplicationData.LocalFolder: {ex.Message}"); }
    }
}
