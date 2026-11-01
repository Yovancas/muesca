using ClaudeBarWin.Shared;

namespace ClaudeBarWin.Helper;

/// <summary>
/// Edición de la Store (MSIX, docs/msix.md): el helper viaja dentro del paquete y Claude Code lo lanza por el alias de
/// ejecución del paquete (<c>muesca-helper.exe</c>), con identidad de paquete. Git Bash, en cambio, trata el alias como un
/// enlace y ejecuta el exe de la carpeta del paquete directamente, sin identidad. Aquí se averigua el paquete en los dos
/// casos. Fuera de un paquete (GitHub / Velopack, desarrollo) todo devuelve null.
/// </summary>
internal static class HelperPackage
{
    /// <summary>Carpeta de instalación del paquete de ESTE proceso (<c>GetCurrentPackagePath</c>); null sin identidad.
    /// Windows la protege: «After deployment, package files are marked read-only, and are heavily locked down». La misma
    /// pregunta que hace la app (<see cref="PackageShared"/>, archivo compartido).</summary>
    public static string? CurrentPath() => PackageShared.CurrentPath();

    /// <summary>Package family name de este proceso (<c>GetCurrentPackageFamilyName</c>); null sin identidad.</summary>
    public static string? CurrentFamilyName() => PackageShared.CurrentFamilyName();

    /// <summary>El paquete del helper: el de su identidad o, sin ella, el de la carpeta de paquete desde la que corre.</summary>
    public static string? FamilyName() => CurrentFamilyName() ?? FamilyNameFromInstallDir(AppContext.BaseDirectory);

    /// <summary>
    /// La carpeta de paquete en la que tiene que estar el servidor de permisos: la de su identidad
    /// (<paramref name="currentPackagePath"/>) o, sin ella, la carpeta <c>…\WindowsApps\&lt;paquete&gt;</c> desde la que corre
    /// (<paramref name="helperDir"/>: Git Bash ejecuta el exe del paquete directamente). Null si el helper no es de un paquete
    /// (edición de GitHub). PURO.
    /// </summary>
    internal static string? TrustedPackageDir(string? currentPackagePath, string? helperDir)
    {
        if (!string.IsNullOrWhiteSpace(currentPackagePath)) return currentPackagePath;
        return FamilyNameFromInstallDir(helperDir) is not null ? helperDir : null;
    }

    /// <summary>
    /// Family name de una carpeta de instalación de paquete (<c>…\WindowsApps\&lt;package full name&gt;</c>), o null si no lo
    /// es. Es la carpeta en la que Windows instala los paquetes («C:\Program Files\WindowsApps\&lt;package_full_name&gt;», o
    /// <c>WindowsApps</c> en otro volumen). PURO.
    /// </summary>
    internal static string? FamilyNameFromInstallDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        var trimmed = dir.Trim().TrimEnd('\\', '/');
        int cut = trimmed.LastIndexOfAny(['\\', '/']);
        if (cut <= 0) return null;
        var folder = trimmed[(cut + 1)..];
        var parent = trimmed[..cut];
        int parentCut = parent.LastIndexOfAny(['\\', '/']);
        var parentName = parentCut < 0 ? parent : parent[(parentCut + 1)..];
        return string.Equals(parentName, "WindowsApps", StringComparison.OrdinalIgnoreCase) ? FamilyNameFromFullName(folder) : null;
    }

    /// <summary>
    /// <c>Nombre_Versión_Arquitectura_IdRecurso_IdEditor</c> → <c>Nombre_IdEditor</c>, o null si no tiene esa forma. El nombre
    /// es de 3 a 50 caracteres <c>[A-Za-z0-9.-]</c>, la versión de cuatro números y el id del editor de 13 caracteres en
    /// minúsculas y números (el hash del Publisher). PURO.
    /// </summary>
    internal static string? FamilyNameFromFullName(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return null;
        var parts = fullName.Split('_');
        if (parts.Length != 5) return null;
        string name = parts[0], version = parts[1], arch = parts[2], publisherId = parts[4];
        if (name.Length is < 3 or > 50 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) return null;
        var numbers = version.Split('.');
        if (numbers.Length != 4 || !numbers.All(n => n.Length > 0 && n.Length <= 5 && n.All(char.IsAsciiDigit))) return null;
        if (arch.Length == 0 || !arch.All(char.IsAsciiLetterOrDigit)) return null;
        if (publisherId.Length != 13 || !publisherId.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterLower(c))) return null;
        return name + "_" + publisherId;
    }
}
