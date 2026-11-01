using System.Text.RegularExpressions;
using ClaudeBarWin.Services.Updates;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Qué retira de la instalación de Inno el hook de instalación de Velopack (<see cref="LegacyInnoCleanup.Decide"/>): lo mismo
/// que el puente, sin ejecutar nunca su desinstalador, y nada si no está. Rutas simuladas.
/// </summary>
public class LegacyInnoCleanupTests
{
    private static readonly string Local = Path.Combine(Path.GetTempPath(), "claudebar-inno", "Local");
    private static readonly string Programs = Path.Combine(Path.GetTempPath(), "claudebar-inno", "Start Menu", "Programs");
    private static readonly string Root = Path.Combine(Local, AppLocation.VelopackPackId);
    private static readonly string InnoDir = Path.Combine(Local, "Programs", "ClaudeBarWin");
    private static readonly string Uninstaller = $"\"{Path.Combine(InnoDir, "unins000.exe")}\"";

    private static LegacyInnoCleanup.Plan? Decide(bool keyExists = true, string? location = null, string? uninstall = null,
        string? group = null, string? velopackRoot = null) =>
        LegacyInnoCleanup.Decide(keyExists, location, uninstall, group, Local, Programs, velopackRoot ?? Root);

    [Fact]
    public void Without_the_Inno_key_nothing_is_touched()
    {
        Assert.Null(Decide(keyExists: false, location: InnoDir, uninstall: Uninstaller));
    }

    [Fact]
    public void It_retires_the_Inno_install_like_the_bridge()
    {
        var plan = Decide(location: InnoDir + Path.DirectorySeparatorChar, uninstall: Uninstaller, group: "ClaudeBar")!;

        Assert.Equal(new[]
        {
            Path.Combine(InnoDir, "ClaudeBarWin.exe"),
            Path.Combine(InnoDir, "claudebar-helper.exe"),
            Path.Combine(InnoDir, "unins000.exe"),
            Path.Combine(InnoDir, "unins000.dat"),
            Path.Combine(InnoDir, "unins000.msg"),
            Path.Combine(Programs, "ClaudeBar", "ClaudeBar.lnk"),
        }, plan.Files.Select(Path.GetFullPath));
        // Las carpetas, solo si se quedan vacías.
        Assert.Equal(new[] { InnoDir, Path.Combine(Programs, "ClaudeBar") },
            plan.EmptyDirs.Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d))));
    }

    [Fact]
    public void Nothing_outside_known_names_and_never_the_config_hooks_or_helper()
    {
        var plan = Decide(location: InnoDir, uninstall: Uninstaller)!;
        var dataDir = Path.Combine(Local, "ClaudeBarWin");   // helper de los hooks y snapshots

        Assert.All(plan.Files, f => Assert.Contains(Path.GetFileName(f), new[]
            { "ClaudeBarWin.exe", "claudebar-helper.exe", "unins000.exe", "unins000.dat", "unins000.msg", "ClaudeBar.lnk" }));
        Assert.DoesNotContain(plan.Files, f => f.StartsWith(dataDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.Files, f => f.StartsWith(Root, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Without_values_in_the_key_it_uses_the_defaults()
    {
        var plan = Decide()!;

        Assert.Contains(Path.Combine(InnoDir, "ClaudeBarWin.exe"), plan.Files);
        Assert.Contains(Path.Combine(Programs, "ClaudeBar", "ClaudeBar.lnk"), plan.Files);
        // Sin UninstallString no se sabe cuál es su desinstalador: sin la entrada de «Aplicaciones» ya no lo lanza nadie.
        Assert.DoesNotContain(plan.Files, f => Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_uninstaller_files_go_only_if_the_key_points_to_that_uninstaller()
    {
        var elsewhere = $"\"{Path.Combine(Local, "Other", "unins000.exe")}\"";
        Assert.DoesNotContain(Decide(location: InnoDir, uninstall: elsewhere)!.Files,
            f => Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
        // Sin comillas también vale.
        Assert.Contains(Path.Combine(InnoDir, "unins000.dat"), Decide(location: InnoDir, uninstall: Path.Combine(InnoDir, "unins000.exe"))!.Files);
    }

    [Theory]
    [InlineData("relativa\\ClaudeBarWin")]
    [InlineData("C:\\")]
    public void A_folder_that_is_not_safe_keeps_its_files(string location)
    {
        var plan = Decide(location: location, uninstall: Uninstaller)!;

        Assert.Equal(new[] { Path.Combine(Programs, "ClaudeBar", "ClaudeBar.lnk") }, plan.Files);
    }

    [Fact]
    public void The_Velopack_folder_and_anything_inside_it_are_never_the_Inno_one()
    {
        foreach (var location in new[] { Root, Path.Combine(Root, "current"), Root.ToUpperInvariant() + Path.DirectorySeparatorChar })
            Assert.DoesNotContain(Decide(location: location)!.Files, f => Path.GetFileName(f) == "ClaudeBarWin.exe");
        // Una carpeta hermana con el nombre empezado igual sí es otra.
        Assert.Contains(Path.Combine(Root + "Win", "ClaudeBarWin.exe"), Decide(location: Root + "Win")!.Files);
    }

    [Theory]
    [InlineData("..\\..\\Desktop")]
    [InlineData("C:\\Windows")]
    [InlineData("..")]
    [InlineData("  ")]
    public void A_start_menu_group_with_a_path_falls_back_to_the_default(string group)
    {
        var plan = Decide(location: InnoDir, group: group)!;

        Assert.Contains(Path.Combine(Programs, "ClaudeBar", "ClaudeBar.lnk"), plan.Files);
        Assert.All(plan.Files.Where(f => f.EndsWith(".lnk")), f => Assert.StartsWith(Programs, f));
    }

    [Fact]
    public void A_custom_start_menu_group_is_kept()
    {
        Assert.Contains(Path.Combine(Programs, "Mis apps", "ClaudeBar.lnk"), Decide(location: InnoDir, group: "Mis apps")!.Files);
    }

    [Fact]
    public void The_plan_carries_the_Inno_folder_only_when_it_is_safe()
    {
        Assert.Equal(InnoDir, Decide(location: InnoDir)!.AppDir);
        Assert.Equal(InnoDir, Decide()!.AppDir);   // la de por defecto
        Assert.Null(Decide(location: Root)!.AppDir);
        Assert.Null(Decide(location: "relativa\\ClaudeBarWin")!.AppDir);
    }

    private const int Self = 100;
    private static readonly string Stub = Path.Combine(Root, AppLocation.VelopackStubName);

    [Fact]
    public void Only_the_app_running_from_the_Inno_folder_is_closed()
    {
        Assert.True(LegacyInnoCleanup.ShouldClose(Path.Combine(InnoDir, "ClaudeBarWin.exe"), 7, Self, InnoDir, Root));
        Assert.True(LegacyInnoCleanup.ShouldClose(Path.Combine(InnoDir.ToUpperInvariant(), "ClaudeBarWin.exe"), 7, Self, InnoDir, Root));
    }

    [Fact]
    public void Never_the_current_process_the_Velopack_one_or_another_copy()
    {
        // El propio hook, aunque (por lo que sea) corriera desde ahí.
        Assert.False(LegacyInnoCleanup.ShouldClose(Path.Combine(InnoDir, "ClaudeBarWin.exe"), Self, Self, InnoDir, Root));
        // La app nueva, en current\ de Velopack.
        Assert.False(LegacyInnoCleanup.ShouldClose(Path.Combine(Root, "current", "ClaudeBarWin.exe"), 7, Self, InnoDir, Root));
        // Una build de desarrollo, una carpeta hermana con el nombre empezado igual, o una ruta que no se pudo leer.
        Assert.False(LegacyInnoCleanup.ShouldClose(Path.Combine(Local, "dev", "ClaudeBarWin.exe"), 7, Self, InnoDir, Root));
        Assert.False(LegacyInnoCleanup.ShouldClose(Path.Combine(InnoDir + "2", "ClaudeBarWin.exe"), 7, Self, InnoDir, Root));
        Assert.False(LegacyInnoCleanup.ShouldClose(null, 7, Self, InnoDir, Root));
        Assert.False(LegacyInnoCleanup.ShouldClose("ClaudeBarWin.exe", 7, Self, InnoDir, Root));
        // Sin carpeta de Inno segura (el plan no la trae), nada.
        Assert.False(LegacyInnoCleanup.ShouldClose(Path.Combine(InnoDir, "ClaudeBarWin.exe"), 7, Self, null, Root));
        // Si la de Inno estuviera dentro de la de Velopack, gana Velopack.
        Assert.False(LegacyInnoCleanup.ShouldClose(Path.Combine(Root, "x", "ClaudeBarWin.exe"), 7, Self, Path.Combine(Root, "x"), Root));
    }

    [Fact]
    public void A_shortcut_to_the_Inno_app_is_repointed_to_the_Velopack_launcher()
    {
        Assert.Equal(LegacyInnoCleanup.ShortcutFix.Repoint,
            LegacyInnoCleanup.DecideShortcut(Path.Combine(InnoDir, "ClaudeBarWin.exe"), InnoDir, Stub));
    }

    [Fact]
    public void Without_a_launcher_a_shortcut_to_the_Inno_app_is_deleted()
    {
        Assert.Equal(LegacyInnoCleanup.ShortcutFix.Delete,
            LegacyInnoCleanup.DecideShortcut(Path.Combine(InnoDir, "ClaudeBarWin.exe"), InnoDir, null));
        Assert.Equal(LegacyInnoCleanup.ShortcutFix.Delete,
            LegacyInnoCleanup.DecideShortcut(Path.Combine(InnoDir, "ClaudeBarWin.exe"), InnoDir, Path.Combine(InnoDir, "ClaudeBar.exe")));
    }

    [Fact]
    public void A_shortcut_that_points_elsewhere_is_kept()
    {
        Assert.Equal(LegacyInnoCleanup.ShortcutFix.Keep, LegacyInnoCleanup.DecideShortcut(Stub, InnoDir, Stub));
        Assert.Equal(LegacyInnoCleanup.ShortcutFix.Keep,
            LegacyInnoCleanup.DecideShortcut(Path.Combine(Local, "dev", "ClaudeBarWin.exe"), InnoDir, Stub));
        Assert.Equal(LegacyInnoCleanup.ShortcutFix.Keep, LegacyInnoCleanup.DecideShortcut("", InnoDir, Stub));   // ilegible
        Assert.Equal(LegacyInnoCleanup.ShortcutFix.Keep,
            LegacyInnoCleanup.DecideShortcut(Path.Combine(InnoDir, "ClaudeBarWin.exe"), null, Stub));
    }

    [Fact]
    public void The_desktop_shortcut_of_the_old_installer_is_repointed_to_the_launcher_or_deleted()
    {
        var oldTarget = Path.Combine(InnoDir, "ClaudeBarWin.exe");
        Assert.Equal(LegacyInnoCleanup.ShortcutFix.Repoint, LegacyInnoCleanup.DecideShortcut(oldTarget, InnoDir, Stub));
        Assert.Equal(LegacyInnoCleanup.ShortcutFix.Delete, LegacyInnoCleanup.DecideShortcut(oldTarget, InnoDir, null));
        // El lanzador es el de la raíz de Velopack (el de Muesca), el mismo nombre que reapunta el puente.
        Assert.Equal("Muesca.exe", Path.GetFileName(Stub));
    }

    [Fact]
    public void The_shortcuts_are_the_ones_the_old_installer_created_and_the_bridge_repoints()
    {
        string TestData(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));
        var inno = TestData("ClaudeBarWin.iss");
        var bridge = TestData("ClaudeBarWin-Bridge.iss");

        Assert.Contains($@"{{userstartup}}\{LegacyInnoCleanup.StartupShortcut}", inno);
        Assert.Contains($@"Name: ""{{userdesktop}}\{Path.GetFileNameWithoutExtension(LegacyInnoCleanup.DesktopShortcut)}""", inno);
        Assert.Contains($@"{{userstartup}}\{LegacyInnoCleanup.StartupShortcut}", bridge);
        Assert.Contains($@"{{userdesktop}}\{LegacyInnoCleanup.DesktopShortcut}", bridge);
        Assert.Contains($"VelopackStub = '{AppLocation.VelopackStubName}';", bridge);
    }

    [Fact]
    public void The_key_is_the_one_of_the_old_installer_and_the_bridge()
    {
        string TestData(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));
        var appId = Regex.Match(TestData("ClaudeBarWin.iss"), @"^AppId=\{(\{[0-9A-Fa-f-]+\})", RegexOptions.Multiline).Groups[1].Value;

        Assert.Equal(LegacyInnoCleanup.LegacyAppId, appId);
        Assert.Contains($"LegacyAppId = '{LegacyInnoCleanup.LegacyAppId}';", TestData("ClaudeBarWin-Bridge.iss"));
        Assert.EndsWith($@"\Uninstall\{appId}_is1", LegacyInnoCleanup.UninstallKey);
        Assert.Equal(AppLocation.LegacyInnoInstallDir(Local), InnoDir);
    }
}
