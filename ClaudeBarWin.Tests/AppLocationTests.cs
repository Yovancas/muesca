using ClaudeBarWin.Services.Updates;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El acceso directo de «Iniciar con Windows» apunta al lanzador estable de Velopack (la raíz), no a current\, que Update.exe
/// cambia en cada actualización; y se reapunta el que se quedó en la instalación vieja de Inno. Rutas y existencia de
/// archivos simuladas.
/// </summary>
public class AppLocationTests
{
    private static readonly string Local = Path.Combine(Path.GetTempPath(), "claudebar-loc", "Local");
    private static readonly string Root = Path.Combine(Local, AppLocation.VelopackPackId);
    private static readonly string CurrentExe = Path.Combine(Root, "current", "ClaudeBarWin.exe");
    private static readonly string Stub = Path.Combine(Root, AppLocation.VelopackStubName);
    private static readonly string UpdateExe = Path.Combine(Root, "Update.exe");
    private static readonly string Legacy = Path.Combine(Local, "Programs", "ClaudeBarWin");
    private static readonly string LegacyExe = Path.Combine(Legacy, "ClaudeBarWin.exe");
    private static readonly string DevExe = Path.Combine(Path.GetTempPath(), "claudebar-loc", "src", "bin", "ClaudeBarWin.exe");

    private static Func<string, bool> Existing(params string[] files)
    {
        var set = new HashSet<string>(files.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        return p => set.Contains(Path.GetFullPath(p));
    }

    private static readonly Func<string, bool> Velopack = Existing(CurrentExe, Stub, UpdateExe);

    [Fact]
    public void A_Velopack_install_launches_through_the_stub_in_the_root()
    {
        Assert.Equal(Root, AppLocation.VelopackRoot(CurrentExe, Velopack));
        Assert.Equal(Stub, AppLocation.StableLaunchPath(CurrentExe, Velopack));
    }

    [Fact]
    public void Without_the_stub_it_falls_back_to_the_exe()
    {
        var noStub = Existing(CurrentExe, UpdateExe);
        Assert.Equal(CurrentExe, AppLocation.StableLaunchPath(CurrentExe, noStub));
    }

    [Fact]
    public void A_folder_called_current_without_UpdateExe_is_not_Velopack()
    {
        var notVelopack = Existing(CurrentExe, Stub);
        Assert.Null(AppLocation.VelopackRoot(CurrentExe, notVelopack));
        Assert.Equal(CurrentExe, AppLocation.StableLaunchPath(CurrentExe, notVelopack));
    }

    [Fact]
    public void Inno_and_development_builds_launch_their_own_exe()
    {
        Assert.Equal(LegacyExe, AppLocation.StableLaunchPath(LegacyExe, Existing(LegacyExe)));
        Assert.Equal(DevExe, AppLocation.StableLaunchPath(DevExe, Existing(DevExe)));
        Assert.Null(AppLocation.StableLaunchPath(null, Velopack));
        Assert.Null(AppLocation.StableLaunchPath("", Velopack));
    }

    [Fact]
    public void A_broken_shortcut_is_always_repointed()
    {
        Assert.True(AppLocation.StartupNeedsRepoint(LegacyExe, Stub, Legacy, Velopack));   // el puente ya borró la de Inno
        Assert.True(AppLocation.StartupNeedsRepoint("", Stub, Legacy, Velopack));
        Assert.True(AppLocation.StartupNeedsRepoint(null, Stub, Legacy, Velopack));
        Assert.True(AppLocation.StartupNeedsRepoint(LegacyExe, DevExe, Legacy, Existing(DevExe)));
    }

    [Fact]
    public void A_shortcut_to_the_current_folder_moves_to_the_stub()
    {
        Assert.True(AppLocation.StartupNeedsRepoint(CurrentExe, Stub, Legacy, Velopack));
    }

    [Fact]
    public void A_shortcut_to_the_old_Inno_install_moves_to_Velopack_even_if_it_is_still_there()
    {
        var both = Existing(CurrentExe, Stub, UpdateExe, LegacyExe);
        Assert.True(AppLocation.StartupNeedsRepoint(LegacyExe, Stub, Legacy, both));
    }

    [Fact]
    public void A_shortcut_already_on_the_stub_or_on_another_valid_copy_is_left_alone()
    {
        Assert.False(AppLocation.StartupNeedsRepoint(Stub, Stub, Legacy, Velopack));
        var withDev = Existing(CurrentExe, Stub, UpdateExe, DevExe);
        Assert.False(AppLocation.StartupNeedsRepoint(DevExe, Stub, Legacy, withDev));
        // Una build de desarrollo no se lleva el acceso directo de la instalada (ni de la de Inno).
        var devAndLegacy = Existing(DevExe, LegacyExe, CurrentExe, Stub, UpdateExe);
        Assert.False(AppLocation.StartupNeedsRepoint(Stub, DevExe, Legacy, devAndLegacy));
        Assert.False(AppLocation.StartupNeedsRepoint(LegacyExe, DevExe, Legacy, devAndLegacy));
    }

    [Fact]
    public void The_Run_entry_starts_the_stable_launcher_with_autostart()
    {
        Assert.Equal($"\"{Stub}\" --autostart", AppLocation.AutostartCommand(Stub));
        Assert.Equal((Stub, true), AppLocation.ParseRunCommand(AppLocation.AutostartCommand(Stub)));
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("   ", null, false)]
    [InlineData("\"C:\\Program Files\\X\\ClaudeBar.exe\" --autostart", @"C:\Program Files\X\ClaudeBar.exe", true)]
    [InlineData("\"C:\\X\\ClaudeBar.exe\"", @"C:\X\ClaudeBar.exe", false)]
    [InlineData("C:\\X\\ClaudeBarWin.exe --AutoStart", @"C:\X\ClaudeBarWin.exe", true)]
    [InlineData("C:\\X\\ClaudeBarWin.exe", @"C:\X\ClaudeBarWin.exe", false)]
    [InlineData("\"C:\\X\\ClaudeBar.exe\" --autostartx", @"C:\X\ClaudeBar.exe", false)]
    [InlineData("\"C:\\X\\ClaudeBar.exe", @"C:\X\ClaudeBar.exe", false)]   // comillas sin cerrar
    public void Run_commands_are_parsed_like_Windows_reads_them(string? command, string? exe, bool autostart)
    {
        Assert.Equal((exe, autostart), AppLocation.ParseRunCommand(command));
    }

    [Fact]
    public void The_Run_entry_is_rewritten_only_when_needed()
    {
        var both = Existing(CurrentExe, Stub, UpdateExe, LegacyExe, DevExe);
        Assert.False(AppLocation.RunEntryNeedsRewrite(AppLocation.AutostartCommand(Stub), Stub, Legacy, both));
        Assert.True(AppLocation.RunEntryNeedsRewrite($"\"{Stub}\"", Stub, Legacy, both));                         // sin --autostart
        Assert.True(AppLocation.RunEntryNeedsRewrite(AppLocation.AutostartCommand(CurrentExe), Stub, Legacy, both)); // a current\
        Assert.True(AppLocation.RunEntryNeedsRewrite(AppLocation.AutostartCommand(LegacyExe), Stub, Legacy, both));  // a Inno
        Assert.True(AppLocation.RunEntryNeedsRewrite("\"C:\\no\\existe.exe\" --autostart", Stub, Legacy, both));
        Assert.True(AppLocation.RunEntryNeedsRewrite("", Stub, Legacy, both));
        // Otra copia válida (una build de desarrollo) no se secuestra.
        Assert.False(AppLocation.RunEntryNeedsRewrite(AppLocation.AutostartCommand(DevExe), Stub, Legacy, both));
    }

    [Fact]
    public void The_old_Startup_folder_shortcut_moves_to_the_Run_key_unless_it_belongs_to_another_copy()
    {
        var both = Existing(CurrentExe, Stub, UpdateExe, LegacyExe, DevExe);
        Assert.True(AppLocation.LegacyShortcutShouldMigrate(Stub, Stub, Legacy, both));        // el que deja el puente
        Assert.True(AppLocation.LegacyShortcutShouldMigrate(LegacyExe, Stub, Legacy, both));   // el de Inno
        Assert.True(AppLocation.LegacyShortcutShouldMigrate(CurrentExe, Stub, Legacy, both));
        Assert.True(AppLocation.LegacyShortcutShouldMigrate("", Stub, Legacy, both));          // roto o ilegible
        Assert.False(AppLocation.LegacyShortcutShouldMigrate(DevExe, Stub, Legacy, both));
    }

    [Fact]
    public void What_the_user_turned_off_in_Task_Manager_stays_off()
    {
        Assert.False(AppLocation.DisabledByUser(null));
        Assert.False(AppLocation.DisabledByUser([]));
        Assert.False(AppLocation.DisabledByUser([0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));   // activado
        Assert.False(AppLocation.DisabledByUser([0x06, 0, 0, 0]));
        Assert.True(AppLocation.DisabledByUser([0x03, 0, 0, 0, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x01]));   // apagado
    }

    [Fact]
    public void Uninstalling_only_removes_the_Run_entry_of_this_Velopack_install()
    {
        // La de esta instalación: el lanzador estable (lo normal) o la de current\ (de antes de reapuntarla).
        Assert.True(AppLocation.RunEntryBelongsTo(AppLocation.AutostartCommand(Stub), Root));
        Assert.True(AppLocation.RunEntryBelongsTo($"\"{CurrentExe}\"", Root));
        Assert.True(AppLocation.RunEntryBelongsTo(AppLocation.AutostartCommand(Stub.ToUpperInvariant()), Root + Path.DirectorySeparatorChar));
        // La de otra copia (una build de desarrollo, la de Inno, otra instalación de Velopack) se deja.
        Assert.False(AppLocation.RunEntryBelongsTo(AppLocation.AutostartCommand(DevExe), Root));
        Assert.False(AppLocation.RunEntryBelongsTo(AppLocation.AutostartCommand(LegacyExe), Root));
        Assert.False(AppLocation.RunEntryBelongsTo(AppLocation.AutostartCommand(Path.Combine(Local, "ClaudeBar2", "ClaudeBar.exe")), Root));
        Assert.False(AppLocation.RunEntryBelongsTo(AppLocation.AutostartCommand(Path.Combine(Root, "packages", "x.exe")), Root));
        // Sin entrada, sin raíz (no se sabe cuál es esta instalación) o con basura, tampoco.
        Assert.False(AppLocation.RunEntryBelongsTo(null, Root));
        Assert.False(AppLocation.RunEntryBelongsTo("", Root));
        Assert.False(AppLocation.RunEntryBelongsTo(AppLocation.AutostartCommand(Stub), null));
        Assert.False(AppLocation.RunEntryBelongsTo("\"\0\"", Root));
    }

    [Fact]
    public void The_legacy_Inno_folder_is_the_one_of_the_old_installer()
    {
        Assert.Equal(Legacy, AppLocation.LegacyInnoInstallDir(Local));
    }
}
