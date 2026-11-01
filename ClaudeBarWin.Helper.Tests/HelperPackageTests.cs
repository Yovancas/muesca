using ClaudeBarWin.Helper;
using ClaudeBarWin.Shared;

/// <summary>Edición de la Store (MSIX): de qué paquete es el helper y dónde deja los datos que comparte con la app.</summary>
public sealed class HelperPackageTests
{
    private const string Pfn = "Bekay.Muesca_8wekyb3d8bbwe";

    [Theory]
    [InlineData("Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbwe", Pfn)]
    [InlineData("Bekay.Muesca_1.2.3.0_arm64__8wekyb3d8bbwe", Pfn)]
    [InlineData("Bekay.Muesca_65535.1.0.0_neutral_~_8wekyb3d8bbwe", Pfn)]
    [InlineData("12345Bekay-Muesca_1.0.0.0_x64__abcdefghjkmnp", "12345Bekay-Muesca_abcdefghjkmnp")]
    public void Family_name_comes_from_name_and_publisher_id(string fullName, string expected) =>
        Assert.Equal(expected, HelperPackage.FamilyNameFromFullName(fullName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bekay.Muesca")]
    [InlineData("Bekay.Muesca_8wekyb3d8bbwe")]                         // ya es una familia, no un nombre completo
    [InlineData("Bekay.Muesca_1.0.0_x64__8wekyb3d8bbwe")]               // versión de tres números
    [InlineData("Bekay.Muesca_1.0.0.x_x64__8wekyb3d8bbwe")]
    [InlineData("Bekay.Muesca_1.0.0.0_x64__8WEKYB3D8BBWE")]             // el id del editor va en minúsculas
    [InlineData("Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbw")]              // 12 caracteres
    [InlineData("Be_1.0.0.0_x64__8wekyb3d8bbwe")]                       // nombre de 2 caracteres
    [InlineData("Bekay Muesca_1.0.0.0_x64__8wekyb3d8bbwe")]             // espacio en el nombre
    [InlineData("Bekay.Muesca_1.0.0.0__x64__8wekyb3d8bbwe")]            // seis partes
    [InlineData("..\\..\\x_1.0.0.0_x64__8wekyb3d8bbwe")]
    public void Anything_else_is_not_a_package_full_name(string? fullName) =>
        Assert.Null(HelperPackage.FamilyNameFromFullName(fullName));

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbwe\", Pfn)]
    [InlineData(@"C:\Program Files\WindowsApps\Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbwe", Pfn)]
    [InlineData(@"D:\WindowsApps\Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbwe\", Pfn)]
    [InlineData(@"c:\program files\windowsapps\Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbwe\", Pfn)]
    public void An_install_folder_under_WindowsApps_gives_the_family(string dir, string expected) =>
        Assert.Equal(expected, HelperPackage.FamilyNameFromInstallDir(dir));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Users\ana\AppData\Local\ClaudeBarWin\bin\")]                        // GitHub / Velopack
    [InlineData(@"C:\Users\ana\AppData\Local\ClaudeBar\current\")]
    [InlineData(@"C:\dev\Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbwe\")]                     // no cuelga de WindowsApps
    [InlineData(@"C:\Users\ana\AppData\Local\Microsoft\WindowsApps\Bekay.Muesca_8wekyb3d8bbwe\")]   // carpeta del alias
    [InlineData(@"C:\Program Files\WindowsApps\")]
    public void Other_folders_have_no_package(string? dir) =>
        Assert.Null(HelperPackage.FamilyNameFromInstallDir(dir));

    [Fact]
    public void The_trusted_package_folder_is_the_identity_one_or_the_one_it_runs_from()
    {
        const string installed = @"C:\Program Files\WindowsApps\Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbwe";
        const string alias = @"C:\Users\ana\AppData\Local\Microsoft\WindowsApps\Bekay.Muesca_8wekyb3d8bbwe";
        // Con identidad manda la carpeta de instalación, aunque el helper crea correr desde la del alias.
        Assert.Equal(installed, HelperPackage.TrustedPackageDir(installed, alias));
        // Sin identidad, la carpeta del paquete desde la que corre (Git Bash).
        Assert.Equal(installed, HelperPackage.TrustedPackageDir(null, installed));
        Assert.Equal(installed, HelperPackage.TrustedPackageDir("  ", installed));
        // Ni paquete ni identidad: ninguna (edición de GitHub, o la carpeta del alias sin identidad).
        Assert.Null(HelperPackage.TrustedPackageDir(null, @"C:\Users\ana\AppData\Local\ClaudeBarWin\bin"));
        Assert.Null(HelperPackage.TrustedPackageDir(null, alias));
        Assert.Null(HelperPackage.TrustedPackageDir(null, null));
    }

    [Fact]
    public void Store_edition_shares_data_in_the_package_folder()
    {
        string? Env(string key) => key == "LOCALAPPDATA" ? @"C:\L" : null;
        var expected = Path.Combine(@"C:\L", "Packages", Pfn, "LocalState", "ClaudeBarWin");
        Assert.Equal(expected, HelperPaths.DataDir(Env, Pfn));
        Assert.Equal(expected, PackageShared.PackageDataDir(@"C:\L", Pfn));   // el archivo que compila también la app
        Assert.Equal(Path.Combine(expected, "ratelimits"), HelperPaths.RateLimitsDir(Env, Pfn));
        // Sin paquete, la de siempre (GitHub / Velopack).
        Assert.Equal(Path.Combine(@"C:\L", "ClaudeBarWin"), HelperPaths.DataDir(Env, null));
        Assert.Equal(Path.Combine(@"C:\L", "ClaudeBarWin"), HelperPaths.DataDir(Env, "  "));
    }

    [Fact]
    public void CLAUDEBAR_DATA_DIR_still_wins_in_the_store_edition()
    {
        string? Env(string key) => key == "CLAUDEBAR_DATA_DIR" ? @"D:\x" : key == "LOCALAPPDATA" ? @"C:\L" : null;
        Assert.Equal(@"D:\x", HelperPaths.DataDir(Env, Pfn));
    }

    [Fact]
    public void Outside_a_package_there_is_no_identity()
    {
        // Los tests no corren empaquetados.
        Assert.Null(HelperPackage.CurrentPath());
        Assert.Null(HelperPackage.CurrentFamilyName());
        Assert.Null(HelperPackage.FamilyName());
    }

    [Theory]
    [InlineData(PackageShared.ErrorInsufficientBuffer, true)]
    [InlineData(PackageShared.AppModelErrorNoPackage, false)]
    [InlineData(PackageShared.ErrorSuccess, false)]
    public void Only_insufficient_buffer_means_there_is_a_package(int result, bool expected)
    {
        Assert.Equal(expected, PackageShared.HasPackage(result));
    }
}
