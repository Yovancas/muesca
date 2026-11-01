using System.Xml.Linq;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Packaging;

/// <summary>
/// Lo que la app y el helper tienen que hacer igual con el paquete de la Store (la carpeta de datos que comparten y las
/// preguntas a Windows por la identidad) vive en un solo archivo, <c>Shared/PackageShared.cs</c>, que compilan los dos
/// proyectos: la app por su glob y el helper enlazado. Aquí se compilan también <c>HelperPaths</c> y <c>HelperPackage</c> del
/// helper (enlazados en este proyecto de tests) para comparar de verdad la ruta que calcula cada uno.
/// </summary>
public class SharedSourcesTests
{
    private const string Local = @"C:\Users\ana\AppData\Local";
    private const string Family = "Bekay.Muesca_8wekyb3d8bbwe";

    private static string? Env(string name) => name == "LOCALAPPDATA" ? Local : null;

    [Fact]
    public void The_app_and_the_helper_use_the_same_package_data_folder()
    {
        var app = StoreEdition.DataDir(Local, Family);
        Assert.Equal(Path.Combine(Local, "Packages", Family, "LocalState", "ClaudeBarWin"), app);
        Assert.Equal(app, ClaudeBarWin.Helper.HelperPaths.DataDir(Env, Family));
        // Los snapshots de la statusline: el helper los escribe donde la app los lee.
        Assert.Equal(Path.Combine(app, "ratelimits"), ClaudeBarWin.Helper.HelperPaths.RateLimitsDir(Env, Family));
    }

    [Fact]
    public void Outside_the_Store_both_use_the_same_folder_and_the_same_override()
    {
        Assert.Equal(Path.Combine(Local, "ClaudeBarWin"), ClaudeBarWin.Helper.HelperPaths.DataDir(Env, null));
        Assert.Equal(Path.Combine(Local, "ClaudeBarWin"), HelperDeployer.ResolveDataDir(null, Local, null));
        Assert.Equal(StoreEdition.DataDir(Local, Family), HelperDeployer.ResolveDataDir("  ", Local, Family));
        Assert.Equal(@"D:\x", HelperDeployer.ResolveDataDir(@" D:\x ", Local, Family));
        Assert.Equal(@"D:\x", ClaudeBarWin.Helper.HelperPaths.DataDir(n => n == "CLAUDEBAR_DATA_DIR" ? @" D:\x " : Env(n), Family));
    }

    [Fact]
    public void Without_a_package_neither_finds_an_identity()
    {
        // Los tests no corren empaquetados.
        Assert.Null(ClaudeBarWin.Helper.HelperPackage.CurrentFamilyName());
        Assert.Null(ClaudeBarWin.Helper.HelperPackage.CurrentPath());
        Assert.Null(PackageIdentity.FamilyName());
        Assert.Null(PackageIdentity.InstallPath());
    }

    [Fact]
    public void The_helper_project_links_the_shared_file_instead_of_copying_it()
    {
        var project = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "projects", "ClaudeBarWin.Helper.csproj"));
        var compiled = project.Descendants("Compile").Select(e => ((string?)e.Attribute("Include"))?.Replace('/', '\\')).ToList();
        Assert.Contains(@"..\Shared\PackageShared.cs", compiled);

        // Y la app no lo saca de su glob (lo compila como cualquier otro archivo suyo).
        var app = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "projects", "ClaudeBarWin.csproj"));
        var removed = app.Descendants("Compile").Select(e => (string?)e.Attribute("Remove") ?? "").ToList();
        Assert.DoesNotContain(removed, r => r.Contains("Shared", StringComparison.OrdinalIgnoreCase));
    }
}
