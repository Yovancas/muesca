using System.Runtime.InteropServices;
using ClaudeBarWin.Shared;

namespace ClaudeBarWin.Services.Licensing;

/// <summary>
/// ¿Corre la app con identidad de paquete (MSIX, la edición de la Store)? <c>GetCurrentPackageFullName</c> con un búfer de 0
/// devuelve <c>ERROR_INSUFFICIENT_BUFFER</c> si la hay y <c>APPMODEL_ERROR_NO_PACKAGE</c> si no (Velopack, desarrollo). La
/// familia y la carpeta del paquete se preguntan igual que en el helper: con el archivo que compilan los dos
/// (<see cref="PackageShared"/>).
/// </summary>
public static class PackageIdentity
{
    internal const int ErrorSuccess = PackageShared.ErrorSuccess,
        ErrorInsufficientBuffer = PackageShared.ErrorInsufficientBuffer,
        AppModelErrorNoPackage = PackageShared.AppModelErrorNoPackage;

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    public static bool HasIdentity()
    {
        try
        {
            uint length = 0;
            return Interpret(GetCurrentPackageFullName(ref length, IntPtr.Zero));
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;   // Windows anterior a 8: no hay paquetes
        }
    }

    /// <summary>Package family name (<c>Nombre_IdEditor</c>, lo que Partner Center llama PFN); null sin identidad.</summary>
    public static string? FamilyName() => PackageShared.CurrentFamilyName();

    /// <summary>Carpeta de instalación del paquete (<c>C:\Program Files\WindowsApps\&lt;nombre completo&gt;</c>); null sin
    /// identidad.</summary>
    public static string? InstallPath() => PackageShared.CurrentPath();

    /// <summary>Lo que devuelve <c>GetCurrentPackageFullName</c> con búfer vacío → ¿hay paquete? PURO.</summary>
    internal static bool Interpret(int result) => PackageShared.HasPackage(result);
}
