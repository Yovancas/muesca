using System.Text.Json;
using ClaudeBarWin.Config;
using static ClaudeBarWin.Tests.SetupProfilesTests;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <c>--apply-setup</c>: el JSON del asistente (<see cref="SetupRequest"/>) y su fusión sobre config.json
/// (<see cref="SetupApply"/>), con hooks e inicio con Windows falsos. Cada test usa su propia carpeta temporal.
/// </summary>
public sealed class SetupApplyTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 15, 30, 0, DateTimeKind.Utc);
    private const string Stamp = "20261029-153000";

    // _dir hace de carpeta temporal del usuario (y guarda config.json); InnoTmp, de la {tmp} de Inno dentro de ella.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-setup-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_dir, "config.json");
    private string InnoTmp => Path.Combine(_dir, "is-K8F2Q.tmp");

    public SetupApplyTests() => Directory.CreateDirectory(InnoTmp);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>Efectos falsos: cuenta las instalaciones de hooks y guarda el último «Iniciar con Windows».</summary>
    private sealed class FakeEffects
    {
        public bool Installed;
        public bool FailInstall;
        public Action? OnInstall;
        public List<bool> InstallCalls { get; } = new();
        public List<bool> StartupCalls { get; } = new();

        public SetupEffects Effects => new(
            HooksInstalled: () => Installed,
            InstallHooks: permissions =>
            {
                InstallCalls.Add(permissions);
                OnInstall?.Invoke();
                if (FailInstall) throw new IOException("settings.json bloqueado");
                Installed = true;
                return "settings.json.bak";
            },
            SetStartWithWindows: on => StartupCalls.Add(on));
    }

    private static readonly SetupMachine Desk = Machine(true, Mon(@"\\.\DISPLAY1", 0, 0, 2560, 1440, primary: true));
    private static readonly SetupMachine SmallLaptop = Machine(true, Mon(@"\\.\DISPLAY1", 0, 0, 1920, 1080, primary: true, dpi: 144));
    private static readonly SetupMachine NoClaude = Machine(false, Mon(@"\\.\DISPLAY1", 0, 0, 2560, 1440, primary: true));

    private string WriteRequest(string json, string name = "claudebar-setup.json", string? dir = null)
    {
        var path = Path.Combine(dir ?? InnoTmp, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private SetupOutcome Run(string json, SetupMachine? machine = null, FakeEffects? fx = null, string name = "claudebar-setup.json",
        string? dir = null) =>
        SetupApply.Run(WriteRequest(json, name, dir), ConfigPath, machine ?? Desk, (fx ?? new FakeEffects()).Effects, Now,
            tempRoot: _dir);

    private static SetupRequest Request(string json)
    {
        Assert.True(SetupRequest.TryParse(json, out var r, out var error), error);
        return r!;
    }

    private void WriteConfig(AppConfig c) => c.Save(ConfigPath);

    private AppConfig ReadConfig() => JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath))!;

    private string[] Backups() => Directory.GetFiles(_dir, "config.json.bak-setup-*").Select(f => Path.GetFileName(f)!).ToArray();

    // ---- lectura del JSON ----

    [Fact]
    public void Parse_minimal_request()
    {
        Assert.True(SetupRequest.TryParse("""{ "version": 1, "profile": "coder" }""", out var r, out var error), error);
        Assert.Equal(SetupProfile.Coder, r!.Profile);
        Assert.Empty(r.Overrides);
        Assert.False(r.LiveSessions);
        Assert.Null(r.StartWithWindows);
        Assert.Empty(r.Ignored);
    }

    [Fact]
    public void Parse_full_request_with_every_value_type()
    {
        const string json = """
            {
              // lo escribe el instalador; comentarios y comas finales se toleran
              "version": 1,
              "profile": "custom",
              "liveSessions": true,
              "startWithWindows": false,
              "overrides": {
                "Language": "es",
                "NotchEnabled": true,
                "NotchSizePercent": 115,
                "NotchMonitor": "\\\\.\\DISPLAY2",
              },
              "futureField": { "ignored": true }
            }
            """;
        Assert.True(SetupRequest.TryParse(json, out var r, out var error), error);
        Assert.Equal(SetupProfile.Custom, r!.Profile);
        Assert.True(r.LiveSessions);
        Assert.False(r.StartWithWindows);
        Assert.Equal("es", r.Overrides["Language"]);
        Assert.True((bool)r.Overrides["NotchEnabled"]);
        Assert.Equal(115, r.Overrides["NotchSizePercent"]);
        Assert.Equal(@"\\.\DISPLAY2", r.Overrides["NotchMonitor"]);
        Assert.Empty(r.Ignored);
    }

    [Theory]
    [InlineData("{ esto no es json", "JSON inválido")]
    [InlineData("", "JSON inválido")]
    [InlineData("[1, 2]", "no es un objeto")]
    [InlineData("""{ "profile": "coder" }""", "falta «version»")]
    [InlineData("""{ "version": 2, "profile": "coder" }""", "versión desconocida")]
    [InlineData("""{ "version": "1", "profile": "coder" }""", "versión desconocida")]
    [InlineData("""{ "version": 1.5, "profile": "coder" }""", "versión desconocida")]
    [InlineData("""{ "version": 1 }""", "falta «profile»")]
    [InlineData("""{ "version": 1, "profile": "gamer" }""", "perfil desconocido")]
    [InlineData("""{ "version": 1, "profile": "coder", "liveSessions": "yes" }""", "liveSessions")]
    [InlineData("""{ "version": 1, "profile": "coder", "startWithWindows": 1 }""", "startWithWindows")]
    [InlineData("""{ "version": 1, "profile": "coder", "overrides": [] }""", "overrides")]
    public void Parse_rejects_invalid_requests_with_a_reason(string json, string reason)
    {
        Assert.False(SetupRequest.TryParse(json, out var r, out var error));
        Assert.Null(r);
        Assert.Contains(reason, error);
    }

    [Fact]
    public void Unknown_or_invalid_overrides_are_ignored_and_reported()
    {
        const string json = """
            { "version": 1, "profile": "coder", "overrides": {
                "LiveSessionsEnabled": true, "Theme": "light", "NotchSizePercent": 120,
                "NotchEnabled": "yes", "Language": "xx", "NotchSound": false } }
            """;
        Assert.True(SetupRequest.TryParse(json, out var r, out _));
        Assert.Equal(new[] { "NotchSound" }, r!.Overrides.Keys.ToArray());
        Assert.Equal(5, r.Ignored.Count);
        Assert.Contains(r.Ignored, i => i.StartsWith("LiveSessionsEnabled: ajuste desconocido"));
        Assert.Contains(r.Ignored, i => i.StartsWith("NotchSizePercent: valor no válido"));
    }

    // ---- errores controlados: no se toca nada ----

    [Fact]
    public void Missing_request_file_is_a_controlled_error()
    {
        var outcome = SetupApply.Run(Path.Combine(_dir, "claudebar-setup.json"), ConfigPath, Desk, new FakeEffects().Effects, Now);
        Assert.Equal(SetupStatus.NotFound, outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
        Assert.False(File.Exists(ConfigPath));
        Assert.Equal(2, SetupApply.Run("", ConfigPath, Desk, new FakeEffects().Effects, Now).ExitCode);
    }

    [Theory]
    [InlineData("{ roto")]
    [InlineData("""{ "version": 7, "profile": "coder" }""")]
    [InlineData("""{ "version": 1, "profile": "nope" }""")]
    public void Invalid_request_leaves_the_existing_config_untouched(string json)
    {
        WriteConfig(new AppConfig { Theme = "light" });
        var before = File.ReadAllBytes(ConfigPath);
        var fx = new FakeEffects();

        var outcome = Run(json, fx: fx);

        Assert.Equal(SetupStatus.Invalid, outcome.Status);
        Assert.Equal(3, outcome.ExitCode);
        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
        Assert.Empty(Backups());
        Assert.Empty(fx.InstallCalls);
        Assert.Empty(fx.StartupCalls);
        // El temporal del instalador se borra igualmente.
        Assert.False(File.Exists(Path.Combine(InnoTmp, "claudebar-setup.json")));
    }

    [Fact]
    public void A_config_that_cannot_be_read_right_now_is_not_overwritten_with_defaults()
    {
        // La copia sale bien y justo después config.json queda bloqueado un momento (antivirus, OneDrive): antes Load daba
        // los valores por defecto sin avisar y el perfil se guardaba encima de toda la configuración.
        WriteConfig(new AppConfig { Theme = "light", NotchEnabled = false, RefreshSeconds = 300 });
        var before = File.ReadAllBytes(ConfigPath);
        var fx = new FakeEffects();

        var outcome = SetupApply.Apply(Request("""{ "version": 1, "profile": "coder", "liveSessions": true, "startWithWindows": true }"""),
            ConfigPath, Desk, fx.Effects, Now, load: _ => throw new IOException("config.json en uso por otro proceso"));

        Assert.Equal(SetupStatus.Failed, outcome.Status);
        Assert.Equal(4, outcome.ExitCode);
        Assert.Contains("leer", outcome.Message);
        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
        Assert.Empty(fx.InstallCalls);
        Assert.Empty(fx.StartupCalls);
    }

    [Fact]
    public void A_config_locked_by_another_process_is_left_alone()
    {
        // Lo mismo con un bloqueo de verdad: con el archivo abierto en exclusiva ni siquiera sale la copia.
        WriteConfig(new AppConfig { Theme = "light" });
        var fx = new FakeEffects();
        SetupOutcome outcome;
        using (new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            outcome = Run("""{ "version": 1, "profile": "full", "liveSessions": true }""", fx: fx);
        Assert.Equal(SetupStatus.Failed, outcome.Status);
        Assert.Equal("light", ReadConfig().Theme);
        Assert.Empty(fx.InstallCalls);
    }

    [Fact]
    public void A_config_that_cannot_be_saved_is_reported_and_nothing_else_is_touched()
    {
        WriteConfig(new AppConfig { Theme = "light" });
        var before = File.ReadAllBytes(ConfigPath);
        var fx = new FakeEffects();

        var outcome = SetupApply.Apply(Request("""{ "version": 1, "profile": "coder", "liveSessions": true, "startWithWindows": true }"""),
            ConfigPath, Desk, fx.Effects, Now, save: (_, _) => throw new IOException("disco lleno"));

        Assert.Equal(SetupStatus.Failed, outcome.Status);
        Assert.NotEqual(0, outcome.ExitCode);               // antes: 0, «aplicado», y en el log «cambia …»
        Assert.Contains("guardar", outcome.Message);
        Assert.Empty(outcome.Changed);
        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
        Assert.Empty(fx.InstallCalls);                      // sin config guardada, settings.json de Claude Code ni se toca
        Assert.Empty(fx.StartupCalls);
    }

    [Fact]
    public void Invalid_request_does_not_create_a_config()
    {
        Run("""{ "version": 1, "profile": "nope" }""");
        Assert.False(File.Exists(ConfigPath));
    }

    // ---- config nueva ----

    [Fact]
    public void Without_config_it_creates_one_with_the_profile_and_the_machine_recommendations()
    {
        var outcome = Run("""{ "version": 1, "profile": "coder" }""", SmallLaptop);

        Assert.Equal(SetupStatus.Applied, outcome.Status);
        Assert.Equal(0, outcome.ExitCode);
        Assert.True(outcome.CreatedConfig);
        Assert.Null(outcome.ConfigBackup);
        Assert.Empty(Backups());
        var c = ReadConfig();
        Assert.True(c.NotchEnabled);
        Assert.False(c.NotchClock);
        Assert.True(c.NotchOpenOnClick);   // recomendado: Full HD al 150 % son 1280 px lógicos
        Assert.Equal("system", c.Language);
    }

    [Fact]
    public void Without_config_the_answers_win_over_profile_and_recommendations()
    {
        Run("""{ "version": 1, "profile": "coder", "overrides": { "NotchOpenOnClick": false, "NotchClock": true, "Language": "en" } }""",
            SmallLaptop);
        var c = ReadConfig();
        Assert.False(c.NotchOpenOnClick);
        Assert.True(c.NotchClock);
        Assert.Equal("en", c.Language);
    }

    // ---- fusión sobre una config existente (modo actualización) ----

    private static AppConfig Existing() => new()
    {
        Theme = "light", Language = "es", NotchQuotaThresholds = new[] { 70, 90 }, RefreshSeconds = 300,
        NotchOpenOnClick = false, NotchEnabled = false, NotchClock = true, DashboardPosition = "TopLeft",
    };

    [Fact]
    public void Existing_config_only_changes_what_was_chosen_and_is_backed_up_first()
    {
        WriteConfig(Existing());
        var before = File.ReadAllText(ConfigPath);

        var outcome = Run("""{ "version": 1, "profile": "coder" }""", SmallLaptop);

        Assert.Equal(SetupStatus.Applied, outcome.Status);
        Assert.False(outcome.CreatedConfig);
        Assert.Equal(new[] { $"config.json.bak-setup-{Stamp}" }, Backups());
        Assert.Equal(Path.Combine(_dir, $"config.json.bak-setup-{Stamp}"), outcome.ConfigBackup);
        Assert.Equal(before, File.ReadAllText(outcome.ConfigBackup!));

        var c = ReadConfig();
        // Lo del perfil, cambiado…
        Assert.True(c.NotchEnabled);
        Assert.False(c.NotchClock);
        // …y lo demás, intacto: con una config previa no se aplican las recomendaciones (aquí sería «con clic»).
        Assert.False(c.NotchOpenOnClick);
        Assert.Equal("light", c.Theme);
        Assert.Equal("es", c.Language);
        Assert.Equal(new[] { 70, 90 }, c.NotchQuotaThresholds);
        Assert.Equal(300, c.RefreshSeconds);
        Assert.Equal("TopLeft", c.DashboardPosition);
        Assert.Equal(new[] { "NotchEnabled", "NotchClock", "NotchMascot", "NotchCompactLyrics" }.Order(), outcome.Changed.Order());
    }

    [Fact]
    public void Existing_config_with_custom_profile_changes_only_the_answers()
    {
        WriteConfig(Existing());
        var outcome = Run("""{ "version": 1, "profile": "custom", "overrides": { "Language": "en" } }""");
        var c = ReadConfig();
        Assert.Equal(new[] { "Language" }, outcome.Changed.ToArray());
        Assert.Equal("en", c.Language);
        Assert.False(c.NotchEnabled);
        Assert.Equal("light", c.Theme);
    }

    [Fact]
    public void Existing_config_that_already_matches_reports_no_changes_but_still_has_a_backup()
    {
        WriteConfig(SetupProfilesTests.ApplyProfile(SetupProfile.Minimal));
        var outcome = Run("""{ "version": 1, "profile": "minimal" }""");
        Assert.Empty(outcome.Changed);
        Assert.Single(Backups());
    }

    [Fact]
    public void Backups_of_the_same_second_do_not_overwrite_each_other()
    {
        WriteConfig(Existing());
        Run("""{ "version": 1, "profile": "coder" }""");
        Run("""{ "version": 1, "profile": "minimal" }""");
        Run("""{ "version": 1, "profile": "full" }""");
        Assert.Equal(new[] { $"config.json.bak-setup-{Stamp}", $"config.json.bak-setup-{Stamp}-2", $"config.json.bak-setup-{Stamp}-3" },
            Backups().Order().ToArray());
    }

    [Fact]
    public void A_broken_existing_config_is_backed_up_set_aside_and_replaced()
    {
        File.WriteAllText(ConfigPath, "{ esto no parsea");
        var outcome = Run("""{ "version": 1, "profile": "full" }""");
        Assert.Equal(SetupStatus.Applied, outcome.Status);
        Assert.Equal("{ esto no parsea", File.ReadAllText(outcome.ConfigBackup!));
        Assert.True(File.Exists(ConfigPath + ".bad"));      // lo de AppConfig.Load de siempre
        Assert.True(ReadConfig().NotchEnabled);
    }

    [Fact]
    public void Saved_config_is_normalized_like_the_app_does()
    {
        WriteConfig(new AppConfig { NotchSizePercent = 115 });
        File.WriteAllText(ConfigPath, File.ReadAllText(ConfigPath).Replace("\"RefreshSeconds\": 60", "\"RefreshSeconds\": 2"));
        Run("""{ "version": 1, "profile": "quota" }""");
        Assert.Equal(AppConfig.MinRefreshSeconds, ReadConfig().RefreshSeconds);
        Assert.Equal(115, ReadConfig().NotchSizePercent);
    }

    // ---- sesiones en vivo (hooks) ----

    [Fact]
    public void Live_sessions_install_the_hooks_when_the_profile_uses_them_and_the_user_checked_it()
    {
        var fx = new FakeEffects();
        var outcome = Run("""{ "version": 1, "profile": "coder", "liveSessions": true }""", fx: fx);
        Assert.Equal(new[] { false }, fx.InstallCalls.ToArray());   // sin permisos: son opt-in
        Assert.True(outcome.HooksInstalled);
        Assert.Null(outcome.HooksError);
        Assert.True(ReadConfig().LiveSessionsEnabled);
        Assert.Contains("LiveSessionsEnabled", outcome.Changed);
    }

    [Fact]
    public void Notch_permissions_ride_along_with_the_hooks_when_chosen()
    {
        var fx = new FakeEffects();
        Run("""{ "version": 1, "profile": "full", "liveSessions": true, "overrides": { "NotchPermissions": true } }""", fx: fx);
        Assert.Equal(new[] { true }, fx.InstallCalls.ToArray());
        var c = ReadConfig();
        Assert.True(c.NotchPermissions);
        Assert.True(c.LiveSessionsEnabled);
    }

    [Fact]
    public void Without_the_checkbox_no_hooks_are_installed()
    {
        var fx = new FakeEffects();
        var outcome = Run("""{ "version": 1, "profile": "coder" }""", fx: fx);
        Assert.Empty(fx.InstallCalls);
        Assert.False(outcome.HooksInstalled);
        Assert.False(ReadConfig().LiveSessionsEnabled);
    }

    [Theory]
    [InlineData("minimal")]
    [InlineData("quota")]
    public void Profiles_that_do_not_use_live_sessions_never_install_hooks(string profile)
    {
        var fx = new FakeEffects();
        Run($$"""{ "version": 1, "profile": "{{profile}}", "liveSessions": true }""", fx: fx);
        Assert.Empty(fx.InstallCalls);
        Assert.False(ReadConfig().LiveSessionsEnabled);
    }

    [Fact]
    public void Without_claude_code_no_hooks_are_installed()
    {
        var fx = new FakeEffects();
        Run("""{ "version": 1, "profile": "coder", "liveSessions": true }""", NoClaude, fx);
        Assert.Empty(fx.InstallCalls);
        Assert.False(ReadConfig().LiveSessionsEnabled);
    }

    [Fact]
    public void Hooks_already_installed_are_not_rewritten()
    {
        var fx = new FakeEffects { Installed = true };
        var outcome = Run("""{ "version": 1, "profile": "coder", "liveSessions": true }""", fx: fx);
        Assert.Empty(fx.InstallCalls);
        Assert.False(outcome.HooksInstalled);
        Assert.True(ReadConfig().LiveSessionsEnabled);
    }

    [Fact]
    public void The_hooks_are_installed_only_once_the_config_is_saved()
    {
        bool? liveOnDisk = null;
        var fx = new FakeEffects();
        fx.OnInstall = () => liveOnDisk = ReadConfig().LiveSessionsEnabled;
        Run("""{ "version": 1, "profile": "coder", "liveSessions": true }""", fx: fx);
        Assert.Single(fx.InstallCalls);
        Assert.True(liveOnDisk);   // al instalar, config.json ya dice que hay sesiones en vivo
    }

    [Fact]
    public void A_failed_hook_install_still_saves_the_config_and_reports_it()
    {
        var fx = new FakeEffects { FailInstall = true };
        var outcome = Run("""{ "version": 1, "profile": "coder", "liveSessions": true, "overrides": { "NotchPermissions": true } }""",
            fx: fx);
        Assert.Equal(SetupStatus.Applied, outcome.Status);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("bloqueado", outcome.HooksError);
        var c = ReadConfig();
        Assert.True(c.NotchEnabled);
        Assert.False(c.LiveSessionsEnabled);
        Assert.False(c.NotchPermissions);   // sin hooks no hay dónde engancharlos
    }

    [Fact]
    public void Notch_permissions_without_hooks_are_turned_off()
    {
        var outcome = Run("""{ "version": 1, "profile": "custom", "overrides": { "NotchEnabled": true, "NotchPermissions": true } }""");
        Assert.False(ReadConfig().NotchPermissions);
        Assert.DoesNotContain("NotchPermissions", outcome.Changed);
    }

    [Fact]
    public void Existing_notch_permissions_survive_when_the_hooks_are_there()
    {
        WriteConfig(new AppConfig { NotchEnabled = true, NotchPermissions = true, LiveSessionsEnabled = true });
        var fx = new FakeEffects { Installed = true };
        Run("""{ "version": 1, "profile": "full" }""", fx: fx);
        Assert.True(ReadConfig().NotchPermissions);
        Assert.Empty(fx.InstallCalls);
    }

    // ---- iniciar con Windows ----

    [Theory]
    [InlineData("true", new[] { true })]
    [InlineData("false", new[] { false })]
    [InlineData("null", new bool[0])]
    public void Start_with_windows_is_only_touched_when_answered(string value, bool[] expected)
    {
        var fx = new FakeEffects();
        Run($$"""{ "version": 1, "profile": "quota", "startWithWindows": {{value}} }""", fx: fx);
        Assert.Equal(expected, fx.StartupCalls.ToArray());
    }

    // ---- el JSON temporal ----

    [Fact]
    public void The_installer_temp_file_is_deleted_after_applying()
    {
        Run("""{ "version": 1, "profile": "quota" }""");
        Assert.False(File.Exists(Path.Combine(InnoTmp, "claudebar-setup.json")));
        Run("""{ "version": 1, "profile": "quota" }""", name: "ClaudeBar-Setup-1234.JSON");
        Assert.False(File.Exists(Path.Combine(InnoTmp, "ClaudeBar-Setup-1234.JSON")));
    }

    [Fact]
    public void Any_other_file_passed_by_hand_is_never_deleted()
    {
        var outcome = Run("""{ "version": 1, "profile": "quota" }""", name: "mi-perfil.json");
        Assert.Equal(SetupStatus.Applied, outcome.Status);
        Assert.True(File.Exists(Path.Combine(InnoTmp, "mi-perfil.json")));
        Run("{ roto", name: "notas.txt");
        Assert.True(File.Exists(Path.Combine(InnoTmp, "notas.txt")));
    }

    [Fact]
    public void A_setup_named_file_outside_the_installer_temp_folder_is_never_deleted()
    {
        // --apply-setup "D:\docs\claudebar-setup-notas.json" lo borraba: solo se miraba el nombre.
        var docs = Path.Combine(_dir, "docs");
        Assert.Equal(SetupStatus.Applied, Run("""{ "version": 1, "profile": "quota" }""", name: "claudebar-setup-notas.json", dir: docs).Status);
        Assert.True(File.Exists(Path.Combine(docs, "claudebar-setup-notas.json")));
        Run("""{ "version": 1, "profile": "quota" }""", dir: _dir);                          // suelto en la carpeta temporal
        Assert.True(File.Exists(Path.Combine(_dir, "claudebar-setup.json")));
        var nested = Path.Combine(docs, "is-ABCDE.tmp");                                     // un is-*.tmp que no es de Inno
        Run("""{ "version": 1, "profile": "quota" }""", dir: nested);
        Assert.True(File.Exists(Path.Combine(nested, "claudebar-setup.json")));
    }

    [Fact]
    public void Only_a_setup_json_directly_in_an_inno_tmp_folder_of_the_temp_root_counts_as_the_installers()
    {
        var temp = Path.Combine(_dir, "Temp");
        Assert.True(SetupApply.IsInstallerTempFile(Path.Combine(temp, "is-K8F2Q.tmp", "claudebar-setup.json"), temp));
        Assert.True(SetupApply.IsInstallerTempFile(Path.Combine(temp, "IS-K8F2Q.TMP", "ClaudeBar-Setup.JSON"),
            temp + Path.DirectorySeparatorChar));
        Assert.False(SetupApply.IsInstallerTempFile(Path.Combine(temp, "is-K8F2Q.tmp", "notas.json"), temp));
        Assert.False(SetupApply.IsInstallerTempFile(Path.Combine(temp, "claudebar-setup.json"), temp));
        Assert.False(SetupApply.IsInstallerTempFile(Path.Combine(temp, "x", "is-K8F2Q.tmp", "claudebar-setup.json"), temp));
        Assert.False(SetupApply.IsInstallerTempFile(Path.Combine(temp, "otra.tmp", "claudebar-setup.json"), temp));
        Assert.False(SetupApply.IsInstallerTempFile(Path.Combine(_dir, "is-K8F2Q.tmp", "claudebar-setup.json"), temp));
        Assert.False(SetupApply.IsInstallerTempFile("", temp));
        Assert.False(SetupApply.IsInstallerTempFile(Path.Combine(temp, "is-K8F2Q.tmp", "claudebar-setup.json"), ""));
    }

    [Fact]
    public void Ignored_overrides_are_reported_in_the_outcome()
    {
        var outcome = Run("""{ "version": 1, "profile": "quota", "overrides": { "Theme": "dark", "Language": "de" } }""");
        Assert.Equal(SetupStatus.Applied, outcome.Status);
        Assert.Single(outcome.Ignored);
        Assert.Equal("de", ReadConfig().Language);
        Assert.Equal("system", ReadConfig().Theme);
    }

    [Fact]
    public void Plan_layers_profile_then_recommendations_then_answers()
    {
        Assert.True(SetupRequest.TryParse("""{ "version": 1, "profile": "minimal", "overrides": { "NotchSound": true } }""",
            out var r, out _));
        var fresh = SetupApply.Plan(r!, SmallLaptop, configExists: false).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True((bool)fresh["NotchSound"]);           // la respuesta gana al perfil
        Assert.True((bool)fresh["NotchMinimal"]);         // del perfil
        Assert.True((bool)fresh["NotchOpenOnClick"]);     // recomendado para este equipo
        var existing = SetupApply.Plan(r!, SmallLaptop, configExists: true).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.DoesNotContain("NotchOpenOnClick", existing.Keys);
        Assert.DoesNotContain("Language", existing.Keys);
    }
}
