using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeBarWin.Shared;

/// <summary>
/// Lo que la app (ClaudeBarWin.exe) y el helper (claudebar-helper.exe) tienen que hacer igual con el paquete de la Store
/// (docs/msix.md). <b>Este archivo lo compilan los dos proyectos</b>: la app por su glob y el helper enlazado
/// (<c>&lt;Compile Include="..\Shared\PackageShared.cs" Link="…"/&gt;</c> en ClaudeBarWin.Helper.csproj). Así no hay dos copias
/// que puedan separarse: si la carpeta de datos de uno cambiase, los snapshots de la statusline que escribe el helper
/// dejarían de llegar a la app. <c>SharedSourcesTests</c> compara la ruta que calcula cada uno.
/// <para>Solo .NET base (sin WinForms ni WinRT) y compatible con NativeAOT: el helper se publica así.</para>
/// </summary>
internal static class PackageShared
{
    /// <summary>Variable de entorno que fija la carpeta de datos (tests y E2E).</summary>
    public const string DataDirVariable = "CLAUDEBAR_DATA_DIR";

    internal const int ErrorSuccess = 0, ErrorInsufficientBuffer = 122, AppModelErrorNoPackage = 15700;

    /// <summary>
    /// La carpeta de datos que la app comparte con el helper: <paramref name="custom"/> (<see cref="DataDirVariable"/>) si
    /// viene; en la edición de la Store, la carpeta de datos del paquete (<see cref="PackageDataDir"/>); si no,
    /// <c>%LOCALAPPDATA%\ClaudeBarWin</c>. PURO.
    /// </summary>
    public static string DataDir(string? custom, string localAppData, string? packageFamilyName)
    {
        if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
        return string.IsNullOrWhiteSpace(packageFamilyName)
            ? Path.Combine(localAppData, "ClaudeBarWin")
            : PackageDataDir(localAppData, packageFamilyName.Trim());
    }

    /// <summary>
    /// <c>%LOCALAPPDATA%\Packages\&lt;familia&gt;\LocalState\ClaudeBarWin</c>: dentro de la carpeta de datos del paquete (la de
    /// <c>ApplicationData.LocalFolder</c>). Es una ruta real, la misma con identidad o sin ella (Git Bash lanza el helper sin
    /// identidad), que no pasa por la virtualización de AppData y que Windows borra al desinstalar. PURO.
    /// </summary>
    public static string PackageDataDir(string localAppData, string packageFamilyName) =>
        Path.Combine(localAppData, "Packages", packageFamilyName, "LocalState", "ClaudeBarWin");

    /// <summary>Package family name de este proceso (<c>GetCurrentPackageFamilyName</c>, <c>Nombre_IdEditor</c>); null sin
    /// identidad de paquete.</summary>
    public static string? CurrentFamilyName() => Query(GetCurrentPackageFamilyName);

    /// <summary>Carpeta de instalación del paquete de este proceso (<c>GetCurrentPackagePath</c>); null sin identidad.
    /// Windows la protege: «After deployment, package files are marked read-only, and are heavily locked down».</summary>
    public static string? CurrentPath() => Query(GetCurrentPackagePath);

    /// <summary>Lo que devuelven las funciones <c>GetCurrentPackage*</c> con el búfer vacío → ¿hay paquete? Sin identidad
    /// devuelven <c>APPMODEL_ERROR_NO_PACKAGE</c> (15700); con ella, <c>ERROR_INSUFFICIENT_BUFFER</c> y el tamaño. PURO.</summary>
    internal static bool HasPackage(int result) => result == ErrorInsufficientBuffer;

    private delegate int PackageQuery(ref uint length, StringBuilder? buffer);

    private static string? Query(PackageQuery query)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            uint length = 0;
            if (!HasPackage(query(ref length, null)) || length == 0 || length > 32767) return null;
            var buffer = new StringBuilder((int)length);
            return query(ref length, buffer) == ErrorSuccess && buffer.Length > 0 ? buffer.ToString() : null;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;   // Windows anterior a 8: no hay paquetes
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackagePath(ref uint pathLength, StringBuilder? path);
}
