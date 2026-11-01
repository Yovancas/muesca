using System.Text.Json;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Onboarding;
using static ClaudeBarWin.Tests.SetupProfilesTests;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Asistente de bienvenida del notch (<see cref="OnboardingWizard"/>): páginas, navegación y las reglas del instalador; y lo
/// elegido (<see cref="SetupAnswers"/>) convertido en la petición de <see cref="SetupApply"/>, también aplicada de verdad
/// sobre un config.json temporal.
/// </summary>
public sealed class OnboardingWizardTests : IDisposable
{
    private static readonly SetupMonitor Desk = Mon(@"\\.\DISPLAY1", 0, 0, 2560, 1440, primary: true);
    private static readonly SetupMonitor Laptop = Mon(@"\\.\DISPLAY1", 0, 0, 1366, 768, primary: true);
    private static readonly SetupMonitor Above = Mon(@"\\.\DISPLAY2", 0, -1440, 2560, 1440);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-onboarding-" + Guid.NewGuid().ToString("N"));

    public OnboardingWizardTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static OnboardingWizard Wizard(bool claude = true, bool fresh = true, AppConfig? current = null, bool hooks = false,
        bool? startup = false, params SetupMonitor[] monitors) =>
        new(new OnboardingContext(Machine(claude, monitors.Length == 0 ? new[] { Desk } : monitors),
            current ?? new AppConfig().Normalize(), hooks, startup, fresh));

    private static void Walk(OnboardingWizard w, OnboardingPage to)
    {
        for (int i = 0; i < 10 && w.Page != to; i++) Assert.False(w.Next());
        Assert.Equal(to, w.Page);
    }

    // ---- páginas y navegación

    [Fact]
    public void First_run_with_Claude_Code_recommends_the_coder_profile_and_offers_live_sessions()
    {
        var w = Wizard();
        Assert.Equal(SetupProfile.Coder, w.Profile);
        Assert.Equal(new[] { OnboardingPage.Welcome, OnboardingPage.Profile, OnboardingPage.Features, OnboardingPage.Settings,
            OnboardingPage.ClaudeCode, OnboardingPage.Summary }, w.Pages);
        Assert.True(w.LiveSessions);
        Assert.False(w.Permissions);   // opt-in: ningún perfil los marca
    }

    [Fact]
    public void Without_Claude_Code_the_quota_profile_is_recommended_and_there_is_no_Claude_Code_page()
    {
        var w = Wizard(claude: false);
        Assert.Equal(SetupProfile.QuotaOnly, w.Profile);
        Assert.DoesNotContain(OnboardingPage.ClaudeCode, w.Pages);
        Walk(w, OnboardingPage.Summary);
        Assert.Equal(4, w.PageIndex);
        Assert.True(w.Next());   // «Empezar»
    }

    [Fact]
    public void Back_and_next_move_one_page_and_the_first_page_has_no_back()
    {
        var w = Wizard();
        Assert.False(w.CanGoBack);
        w.Next();
        Assert.Equal(OnboardingPage.Profile, w.Page);
        Assert.True(w.CanGoBack);
        w.Back();
        Assert.Equal(OnboardingPage.Welcome, w.Page);
    }

    [Fact]
    public void Dots_only_reach_pages_already_seen()
    {
        var w = Wizard();
        Assert.False(w.GoTo(3));
        Walk(w, OnboardingPage.Settings);
        Assert.True(w.GoTo(1));
        Assert.Equal(OnboardingPage.Profile, w.Page);
        Assert.True(w.GoTo(3));
        Assert.Equal(OnboardingPage.Settings, w.Page);
        Assert.False(w.GoTo(4));   // Claude Code aún no se ha visto
    }

    [Fact]
    public void Changing_to_a_profile_without_live_sessions_drops_the_Claude_Code_page()
    {
        var w = Wizard();
        Walk(w, OnboardingPage.Summary);
        w.GoTo(1);
        w.SelectProfile(SetupProfile.Minimal);
        Assert.DoesNotContain(OnboardingPage.ClaudeCode, w.Pages);
        Assert.True(w.GoTo(4));   // el resumen es ahora la quinta
        Assert.Equal(OnboardingPage.Summary, w.Page);
        Assert.Equal(4, w.Furthest);
    }

    // ---- perfiles y «Qué se activa»

    [Fact]
    public void Leaving_the_profile_page_fills_the_switches_with_the_new_profile()
    {
        var w = Wizard();
        Walk(w, OnboardingPage.Profile);
        w.SelectProfile(SetupProfile.Minimal);
        w.Next();
        Assert.Equal(SetupProfile.Minimal, w.FeaturesProfile);
        Assert.True(w.FeatureOn(SetupFeature.Minimal));
        Assert.True(w.FeatureOn(SetupFeature.ReduceMotion));
        Assert.False(w.FeatureOn(SetupFeature.Lyrics));
        Assert.True(w.FeatureOn(SetupFeature.Tasks));   // Minimalista no fija las tareas: las de la config (por defecto, sí)
    }

    [Fact]
    public void Hand_made_changes_survive_going_back_to_the_same_profile()
    {
        var w = Wizard();
        Walk(w, OnboardingPage.Features);
        w.ToggleFeature(SetupFeature.Clock);
        Assert.True(w.FeatureOn(SetupFeature.Clock));
        w.Back();
        w.Next();
        Assert.True(w.FeatureOn(SetupFeature.Clock));
        w.ResetFeatures();
        Assert.False(w.FeatureOn(SetupFeature.Clock));   // Programo apaga el reloj
    }

    [Fact]
    public void A_profile_change_reaches_the_switches_even_when_leaving_by_the_dots()
    {
        var w = Wizard();
        Walk(w, OnboardingPage.Settings);
        w.GoTo(1);
        w.SelectProfile(SetupProfile.FullNotch);
        Assert.True(w.GoTo(2));
        Assert.True(w.FeatureOn(SetupFeature.CompactLyrics));
        Assert.True(w.FeatureOn(SetupFeature.Mascot));
    }

    [Fact]
    public void Custom_on_a_first_run_starts_from_the_recommended_profile()
    {
        var w = Wizard();
        Walk(w, OnboardingPage.Profile);
        w.SelectProfile(SetupProfile.Custom);
        w.Next();
        Assert.True(w.FeatureOn(SetupFeature.Notch));          // de Programo
        Assert.False(w.FeatureOn(SetupFeature.CompactLyrics));
    }

    [Fact]
    public void Custom_when_set_up_again_starts_from_the_current_settings()
    {
        var current = new AppConfig { NotchEnabled = true, NotchClock = true, NotchCompactLyrics = false, NotchMinimal = true }.Normalize();
        var w = Wizard(fresh: false, current: current);
        Assert.Equal(SetupProfile.Custom, w.Profile);   // no coincide con ningún perfil
        Walk(w, OnboardingPage.Features);
        Assert.True(w.FeatureOn(SetupFeature.Notch));
        Assert.True(w.FeatureOn(SetupFeature.Minimal));
        Assert.True(w.FeatureOn(SetupFeature.Clock));
        Assert.False(w.FeatureOn(SetupFeature.CompactLyrics));
    }

    [Fact]
    public void Switches_depend_on_the_notch_music_and_lyrics()
    {
        var w = Wizard();
        Walk(w, OnboardingPage.Features);
        w.ToggleFeature(SetupFeature.Music);
        Assert.False(w.FeatureEnabled(SetupFeature.Lyrics));
        Assert.False(w.FeatureEnabled(SetupFeature.CompactLyrics));
        bool lyrics = w.FeatureOn(SetupFeature.Lyrics);
        w.ToggleFeature(SetupFeature.Lyrics);   // deshabilitada: no cambia
        Assert.Equal(lyrics, w.FeatureOn(SetupFeature.Lyrics));

        w.ToggleFeature(SetupFeature.Notch);
        foreach (var f in SetupFeatures.All)
            Assert.Equal(f is SetupFeature.Notch or SetupFeature.ReduceMotion, w.FeatureEnabled(f));
        Assert.False(w.NotchSettingsEnabled);
    }

    // ---- ajustes recomendados

    [Fact]
    public void First_run_settings_are_the_recommended_ones()
    {
        var w = Wizard(monitors: Laptop);
        Assert.Equal("system", w.Language);
        Assert.Equal("", w.MonitorDevice);
        Assert.Equal(0, w.SizePercent);
        Assert.True(w.OpenOnClick);   // 1366 lógicos < 1440: con un clic
        Assert.Equal(OpenReason.SmallScreen, w.OpenReason);
        Assert.True(w.StartWithWindows);
    }

    [Fact]
    public void Setting_up_again_starts_from_the_current_values()
    {
        var current = new AppConfig { Language = "es", NotchMonitor = @"\\.\DISPLAY2", NotchSizePercent = 130, NotchOpenOnClick = true }.Normalize();
        var w = Wizard(fresh: false, current: current, startup: false, monitors: new[] { Desk, Above });
        Assert.Equal("es", w.Language);
        Assert.Equal(@"\\.\DISPLAY2", w.MonitorDevice);
        Assert.Equal(130, w.SizePercent);
        Assert.True(w.OpenOnClick);
        Assert.False(w.StartWithWindows);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Setting_up_again_reapplies_a_known_startup_state(bool startup)
    {
        var w = Wizard(fresh: false, startup: startup);
        Assert.False(w.StartupUnknown);
        Assert.Equal(startup, w.Answers().ToRequest(w.Context.Machine).StartWithWindows);
    }

    [Fact]
    public void Setting_up_again_with_an_unknown_startup_state_leaves_it_alone()
    {
        // Edición de la Store: la tarea de inicio aún no se ha podido leer. Sin tocar el interruptor no se aplica nada: un
        // «apagado» por no saber apagaría una tarea que estaba encendida.
        var w = Wizard(fresh: false, startup: null);
        Assert.True(w.StartupUnknown);
        Assert.Null(w.Answers().StartWithWindows);
        Assert.Null(w.Answers().ToRequest(w.Context.Machine).StartWithWindows);
    }

    [Fact]
    public void Touching_an_unknown_startup_toggle_applies_what_was_chosen()
    {
        var w = Wizard(fresh: false, startup: null);
        w.ToggleStartup();
        Assert.False(w.StartupUnknown);
        Assert.True(w.Answers().StartWithWindows);
        w.ToggleStartup();
        Assert.False(w.Answers().StartWithWindows);

        var reset = Wizard(fresh: false, startup: null);
        reset.ResetSettings();   // «Restablecer recomendados» también es elegirlo
        Assert.Equal(SetupRecommendations.StartWithWindows, reset.Answers().StartWithWindows);
    }

    [Fact]
    public void The_first_time_applies_the_recommendation_even_if_the_state_is_unknown()
    {
        // Primera vez: el interruptor sale con lo recomendado y se aplica (encender lee el estado de verdad en ese momento).
        var w = Wizard(fresh: true, startup: null);
        Assert.False(w.StartupUnknown);
        Assert.Equal(SetupRecommendations.StartWithWindows, w.Answers().StartWithWindows);
    }

    [Fact]
    public void Setting_up_again_keeps_an_untouched_disconnected_monitor_and_the_language()
    {
        // El notch iba en un monitor que ahora no está: el selector enseña «el principal», pero sin tocarlo no se pierde.
        var current = new AppConfig { Language = "en", NotchEnabled = true, NotchMonitor = @"\\.\DISPLAY3" }.Normalize();
        var w = Wizard(fresh: false, current: current, monitors: new[] { Desk, Above });
        Assert.Equal(0, w.MonitorIndex);
        Assert.Equal("", w.MonitorDevice);
        var a = w.Answers();
        Assert.Equal(@"\\.\DISPLAY3", a.NotchMonitor);
        Assert.Equal("en", a.Language);
        Assert.Equal(@"\\.\DISPLAY3", a.ToRequest(w.Context.Machine).Overrides[nameof(AppConfig.NotchMonitor)]);

        // Tocados, va lo que dice el selector.
        Assert.True(w.NotchSettingsEnabled);
        w.StepMonitor(+1);
        w.StepLanguage(+1);
        a = w.Answers();
        Assert.Equal(@"\\.\DISPLAY1", a.NotchMonitor);
        Assert.Equal(w.Language, a.Language);
        Assert.NotEqual("en", a.Language);

        // «Restablecer recomendados» también es elegir: el principal.
        w.ResetSettings();
        Assert.Equal("", w.Answers().NotchMonitor);
        Assert.Equal("system", w.Answers().Language);
    }

    [Fact]
    public void The_first_run_applies_what_the_selectors_show()
    {
        var current = new AppConfig { Language = "en", NotchMonitor = @"\\.\DISPLAY3" }.Normalize();
        var a = Wizard(fresh: true, current: current, monitors: new[] { Desk, Above }).Answers();
        Assert.Equal("system", a.Language);
        Assert.Equal("", a.NotchMonitor);
    }

    [Fact]
    public void While_untouched_the_opening_follows_the_chosen_monitor()
    {
        var w = Wizard(monitors: new[] { Desk, Above });
        Assert.True(w.AskMonitor);
        Assert.True(w.OpenOnClick);   // el principal tiene otro monitor justo encima
        Assert.Equal(OpenReason.MonitorAbove, w.OpenReason);
        w.StepMonitor(+1);            // \\.\DISPLAY1 (el mismo, elegido a mano)
        w.StepMonitor(+1);            // \\.\DISPLAY2: nada encima y 2560 de ancho
        Assert.Equal(@"\\.\DISPLAY2", w.MonitorDevice);
        Assert.False(w.OpenOnClick);
        Assert.Equal(115, w.AutoPercent);

        w.SetOpenOnClick(true);
        w.StepMonitor(+1);            // vuelve a «el principal»: ya tocado, se respeta
        Assert.Equal("", w.MonitorDevice);
        Assert.True(w.OpenOnClick);
        w.ResetSettings();
        Assert.False(w.OpenTouched);
    }

    [Fact]
    public void Language_and_size_cycle_through_their_options()
    {
        var w = Wizard();
        w.StepLanguage(-1);
        Assert.Equal(OnboardingWizard.LanguageCodes[^1], w.Language);
        w.StepLanguage(+1);
        w.StepLanguage(+1);
        Assert.Equal("en", w.Language);
        w.StepSize(+1);
        Assert.Equal(100, w.SizePercent);
        w.StepSize(+9);
        Assert.Equal(115, w.SizePercent);   // un paso por pulsación
        w.SetSize(150);
        w.StepSize(+1);
        Assert.Equal(150, w.SizePercent);
        w.SetSize(42);
        Assert.Equal(150, w.SizePercent);
    }

    [Fact]
    public void With_the_notch_off_its_settings_do_not_change()
    {
        var w = Wizard(monitors: new[] { Desk, Above });
        Walk(w, OnboardingPage.Features);
        w.ToggleFeature(SetupFeature.Notch);
        w.SetSize(130);
        w.StepMonitor(+1);
        bool open = w.OpenOnClick;
        w.SetOpenOnClick(!open);
        Assert.Equal(0, w.SizePercent);
        Assert.Equal("", w.MonitorDevice);
        Assert.Equal(open, w.OpenOnClick);
    }

    // ---- Claude Code

    [Fact]
    public void Live_sessions_are_only_ticked_with_the_coder_profile()
    {
        var w = Wizard();
        Walk(w, OnboardingPage.Profile);
        w.SelectProfile(SetupProfile.FullNotch);
        w.Next();
        Assert.False(w.LiveSessions);
        w.Back();
        w.SelectProfile(SetupProfile.Coder);
        w.Next();
        Assert.True(w.LiveSessions);
    }

    [Fact]
    public void Installed_live_sessions_stay_on_and_cannot_be_unticked()
    {
        var w = Wizard(hooks: true);
        Walk(w, OnboardingPage.Profile);
        w.SelectProfile(SetupProfile.FullNotch);
        w.Next();
        Assert.True(w.LiveSessionsOn);
        Assert.False(w.LiveSessionsEnabled);
        w.ToggleLiveSessions();
        Assert.True(w.LiveSessionsOn);
    }

    [Fact]
    public void Permissions_need_the_notch_and_live_sessions()
    {
        var w = Wizard();
        Walk(w, OnboardingPage.ClaudeCode);
        Assert.True(w.PermissionsAvailable);
        w.TogglePermissions();
        Assert.True(w.PermissionsValue);
        w.ToggleLiveSessions();
        Assert.False(w.PermissionsAvailable);
        Assert.False(w.PermissionsValue);
        w.TogglePermissions();   // sin sesiones en vivo no se puede tocar
        w.ToggleLiveSessions();
        Assert.True(w.PermissionsValue);
        Assert.True(w.Answers().NotchPermissions);
    }

    // ---- saltar

    [Fact]
    public void Escape_asks_for_confirmation_inside_the_notch_and_blocks_navigation_meanwhile()
    {
        var w = Wizard();
        w.RequestSkip();
        Assert.Equal(OnboardingOverlay.ConfirmSkip, w.Overlay);
        Assert.False(w.Next());
        Assert.Equal(OnboardingPage.Welcome, w.Page);
        w.RequestSkip();   // Esc otra vez: vuelve a la página
        Assert.Equal(OnboardingOverlay.None, w.Overlay);
        Assert.True(w.SkipApplies);
        Assert.False(Wizard(fresh: false).SkipApplies);   // desde la bandeja, saltar no cambia nada
    }

    [Fact]
    public void Skipping_applies_the_recommended_profile_without_touching_Claude_Code()
    {
        var w = Wizard(monitors: Laptop);
        Walk(w, OnboardingPage.Profile);
        w.SelectProfile(SetupProfile.Minimal);
        var skip = w.SkipAnswers();
        Assert.Equal(SetupProfile.Coder, skip.Profile);
        Assert.True(skip.On(SetupFeature.Notch));
        Assert.False(skip.LiveSessions);
        Assert.False(skip.NotchPermissions);
        Assert.True(skip.NotchOpenOnClick);
        Assert.True(skip.StartWithWindows);
        Assert.False(skip.ToRequest(w.Context.Machine).LiveSessions);
    }

    [Fact]
    public void The_error_overlay_keeps_its_message_until_dismissed()
    {
        var w = Wizard();
        w.ShowError("config.json bloqueado");
        Assert.Equal(OnboardingOverlay.Error, w.Overlay);
        Assert.Equal("config.json bloqueado", w.Error);
        w.CancelOverlay();
        Assert.Equal(OnboardingOverlay.None, w.Overlay);
        Assert.Equal("", w.Error);
    }

    // ---- respuestas → SetupApply

    [Fact]
    public void Answers_become_a_request_with_everything_that_was_shown()
    {
        var w = Wizard(monitors: new[] { Desk, Above });
        Walk(w, OnboardingPage.Settings);
        w.StepMonitor(+1);
        w.StepMonitor(+1);
        w.SetSize(130);
        var request = w.Answers().ToRequest(w.Context.Machine);

        Assert.Equal(SetupProfile.Coder, request.Profile);
        Assert.Empty(request.Ignored);
        Assert.True(request.LiveSessions);
        Assert.True(request.StartWithWindows);
        foreach (var f in SetupFeatures.All) Assert.True(request.Overrides.ContainsKey(SetupFeatures.Key(f)), f.ToString());
        Assert.Equal(@"\\.\DISPLAY2", request.Overrides[nameof(AppConfig.NotchMonitor)]);
        Assert.Equal(130, request.Overrides[nameof(AppConfig.NotchSizePercent)]);
        Assert.Equal("system", request.Overrides[nameof(AppConfig.Language)]);
        Assert.Equal(false, request.Overrides[nameof(AppConfig.NotchPermissions)]);
    }

    [Fact]
    public void Profiles_without_live_sessions_neither_install_hooks_nor_send_permissions()
    {
        var w = Wizard();
        Walk(w, OnboardingPage.Profile);
        w.SelectProfile(SetupProfile.QuotaOnly);
        var request = w.Answers().ToRequest(w.Context.Machine);
        Assert.False(request.LiveSessions);
        Assert.False(request.Overrides.ContainsKey(nameof(AppConfig.NotchPermissions)));
        Assert.Equal(false, request.Overrides[nameof(AppConfig.NotchEnabled)]);
    }

    [Fact]
    public void Invalid_answers_are_set_aside_like_in_the_json()
    {
        var answers = Wizard().Answers() with { NotchSizePercent = 42, Language = "xx" };
        var request = answers.ToRequest(Machine(true, Desk));
        Assert.False(request.Overrides.ContainsKey(nameof(AppConfig.NotchSizePercent)));
        Assert.False(request.Overrides.ContainsKey(nameof(AppConfig.Language)));
        Assert.Equal(2, request.Ignored.Count);
    }

    [Fact]
    public void Applying_the_answers_in_memory_writes_the_profile_installs_hooks_and_leaves_the_rest()
    {
        var configPath = Path.Combine(_dir, "config.json");
        var start = new AppConfig { RefreshSeconds = 120, Theme = "light", OnboardingPending = true }.Normalize();
        start.Save(configPath);
        var installs = new List<bool>();
        var startup = new List<bool>();
        bool installed = false;
        var effects = new SetupEffects(() => installed, permissions =>
        {
            installs.Add(permissions);
            installed = true;
            return "settings.json.bak";
        }, on => startup.Add(on));

        var w = Wizard(current: start);
        Walk(w, OnboardingPage.ClaudeCode);
        w.TogglePermissions();
        var outcome = SetupApply.Apply(w.Answers().ToRequest(w.Context.Machine), configPath, w.Context.Machine, effects,
            new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(SetupStatus.Applied, outcome.Status);
        Assert.Null(outcome.HooksError);
        Assert.Equal(new[] { true }, installs);   // con la entrada de permisos
        Assert.Equal(new[] { true }, startup);
        var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath))!;
        Assert.True(cfg.NotchEnabled);
        Assert.True(cfg.LiveSessionsEnabled);
        Assert.True(cfg.NotchPermissions);
        Assert.False(cfg.NotchClock);
        Assert.Equal(120, cfg.RefreshSeconds);    // lo que el asistente no toca se queda
        Assert.Equal("light", cfg.Theme);
        Assert.True(cfg.OnboardingPending);       // lo apaga quien lo abrió, no SetupApply
        Assert.NotNull(outcome.ConfigBackup);
    }

    [Fact]
    public void The_pending_onboarding_is_cleared_only_when_applying_succeeds()
    {
        var configPath = Path.Combine(_dir, "config.json");
        new AppConfig { OnboardingPending = true }.Normalize().Save(configPath);
        var effects = new SetupEffects(() => false, _ => "settings.json.bak", _ => { });
        var w = Wizard(claude: false);
        var answers = w.SkipAnswers();
        // La bandeja: MutateConfig carga, cambia y guarda.
        void Mutate(Action<AppConfig> change)
        {
            var c = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath))!;
            change(c);
            c.Save(configPath);
        }
        bool Pending() => JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath))!.OnboardingPending;

        // Falla (config.json bloqueado): sigue pendiente, también si el apply lanza.
        var failed = SetupEnvironment.ApplyOnboarding(answers, _ => SetupOutcome.Fail(SetupStatus.Failed, "bloqueado"), Mutate);
        Assert.Equal(SetupStatus.Failed, failed.Status);
        Assert.True(Pending());
        Assert.Throws<IOException>(() => SetupEnvironment.ApplyOnboarding(answers, _ => throw new IOException("en uso"), Mutate));
        Assert.True(Pending());

        // Bien: con el núcleo de verdad de --apply-setup, deja de estar pendiente.
        var ok = SetupEnvironment.ApplyOnboarding(answers, a => SetupApply.Apply(a.ToRequest(w.Context.Machine), configPath,
            w.Context.Machine, effects, new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc)), Mutate);
        Assert.Equal(SetupStatus.Applied, ok.Status);
        Assert.False(Pending());
    }

    [Fact]
    public void Onboarding_is_not_pending_by_default_and_survives_the_config_file()
    {
        Assert.False(new AppConfig().OnboardingPending);
        Assert.False(JsonSerializer.Deserialize<AppConfig>("{}")!.OnboardingPending);   // config de antes: no sale el asistente
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(new AppConfig { OnboardingPending = true }))!;
        Assert.True(back.OnboardingPending);
    }

    // ---- el perfil de la config

    [Fact]
    public void The_matching_profile_is_the_one_whose_values_the_config_has()
    {
        Assert.Equal(SetupProfile.QuotaOnly, SetupProfiles.Matching(new AppConfig()));   // notch apagado por defecto
        Assert.Equal(SetupProfile.Coder, SetupProfiles.Matching(ApplyProfile(SetupProfile.Coder)));
        Assert.Equal(SetupProfile.FullNotch, SetupProfiles.Matching(ApplyProfile(SetupProfile.FullNotch)));
        Assert.Equal(SetupProfile.Minimal, SetupProfiles.Matching(ApplyProfile(SetupProfile.Minimal)));
        var tweaked = ApplyProfile(SetupProfile.Coder);
        tweaked.NotchClock = true;
        Assert.Equal(SetupProfile.Custom, SetupProfiles.Matching(tweaked));
    }

    [Fact]
    public void Prefill_takes_what_the_profile_fixes_and_the_rest_from_the_config()
    {
        var current = new AppConfig { NotchTasks = false, NotchHideInFullscreen = false };
        var v = SetupFeatures.Prefill(SetupProfile.Coder, current, fresh: true, SetupProfile.Coder);
        Assert.True(v[SetupFeature.Notch]);
        Assert.False(v[SetupFeature.Tasks]);
        Assert.False(v[SetupFeature.HideFullscreen]);
        Assert.Equal(SetupFeatures.All.Count, v.Count);
    }
}
