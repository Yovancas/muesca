using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Packaging;
using ClaudeBarWin.Services.Updates;
using ClaudeBarWin.UI;
using ClaudeBarWin.UI.Notch;
using Windows.ApplicationModel;

/// <summary>
/// Edición de la Store (docs/msix.md): «Iniciar con Windows» por la tarea de inicio del paquete, actualizaciones de la Store
/// (sin Velopack) y el Pro por <c>StoreContext</c>. Fuera de un paquete todo sigue como en la edición de GitHub.
/// </summary>
public class StoreEditionTests
{
    // ── Tarea de inicio ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(StartupTaskState.Enabled, true)]
    [InlineData(StartupTaskState.EnabledByPolicy, true)]
    [InlineData(StartupTaskState.Disabled, false)]
    [InlineData(StartupTaskState.DisabledByUser, false)]
    [InlineData(StartupTaskState.DisabledByPolicy, false)]
    public void Startup_is_on_only_when_the_task_is_enabled(StartupTaskState state, bool on) =>
        Assert.Equal(on, StoreStartupTask.IsOn(state));

    [Fact]
    public void Without_a_readable_task_startup_is_off() => Assert.False(StoreStartupTask.IsOn(null));

    [Theory]
    [InlineData(StartupTaskState.Disabled, StoreStartupTask.EnableStep.Request)]
    [InlineData(StartupTaskState.Enabled, StoreStartupTask.EnableStep.AlreadyOn)]
    [InlineData(StartupTaskState.EnabledByPolicy, StoreStartupTask.EnableStep.AlreadyOn)]
    // «If a user disables a task, you can't programmatically re-enable it»: solo desde Configuración.
    [InlineData(StartupTaskState.DisabledByUser, StoreStartupTask.EnableStep.OnlyInSettings)]
    [InlineData(StartupTaskState.DisabledByPolicy, StoreStartupTask.EnableStep.Blocked)]
    public void Enabling_depends_on_who_turned_it_off(StartupTaskState state, StoreStartupTask.EnableStep step) =>
        Assert.Equal(step, StoreStartupTask.NextStep(state));

    [Fact]
    public void An_unknown_state_is_left_alone()
    {
        Assert.Equal(StoreStartupTask.EnableStep.Blocked, StoreStartupTask.NextStep(null));
        Assert.Equal(StoreStartupTask.EnableStep.Blocked, StoreStartupTask.NextStep((StartupTaskState)99));
    }

    // ── Lo que pinta el interruptor: «comprobando…» y «cambiando…» no son «apagado» ───────────────────────

    [Fact]
    public void Before_the_first_read_the_state_is_unknown_not_off()
    {
        var state = StoreStartupTask.Describe(changing: false, known: false, cached: null);
        Assert.Null(state.On);
        Assert.False(state.Changing);
        Assert.False(state.Known);
        // Ni la bienvenida ni el interruptor pueden aplicar nada desde ahí.
        Assert.Null(state.Applicable);
        Assert.False(StoreStartupTask.AcceptsToggle(state));
    }

    [Theory]
    [InlineData(StartupTaskState.Enabled, true)]
    [InlineData(StartupTaskState.Disabled, false)]
    [InlineData(StartupTaskState.DisabledByUser, false)]
    public void After_a_read_the_last_known_state_is_shown(StartupTaskState cached, bool on)
    {
        var state = StoreStartupTask.Describe(changing: false, known: true, cached);
        Assert.Equal(on, state.On);
        Assert.Equal(on, state.Applicable);
        Assert.True(StoreStartupTask.AcceptsToggle(state));
    }

    [Fact]
    public void While_changing_the_toggle_keeps_its_last_state_and_waits()
    {
        var state = StoreStartupTask.Describe(changing: true, known: true, StartupTaskState.Enabled);
        Assert.True(state.On);          // el pill no salta a «apagado» mientras tanto
        Assert.True(state.Changing);
        Assert.Null(state.Applicable);  // el resultado aún no se sabe
        Assert.False(StoreStartupTask.AcceptsToggle(state));
    }

    [Fact]
    public void The_toggle_subtitle_says_checking_or_changing()
    {
        var s = Localization.Get("es");
        Assert.Equal(s.StartupChecking, DashboardSettingsView.StartupSubtitle(new StartupState(null, false), s));
        Assert.Equal(s.StartupChanging, DashboardSettingsView.StartupSubtitle(new StartupState(true, true), s));
        Assert.Equal(s.StartupChanging, DashboardSettingsView.StartupSubtitle(new StartupState(null, true), s));
        Assert.Null(DashboardSettingsView.StartupSubtitle(new StartupState(true, false), s));
        Assert.Null(DashboardSettingsView.StartupSubtitle(new StartupState(false, false), s));
    }

    [Fact]
    public void Outside_the_Store_startup_is_always_known()
    {
        // La clave Run se lee al momento: nunca «comprobando…» en la edición de GitHub.
        Assert.True(StartupManager.State().Known);
        Assert.False(StartupManager.State().Changing);
    }

    [Fact]
    public void The_settings_page_is_the_startup_apps_one() =>
        Assert.Equal("ms-settings:startupapps", StoreStartupTask.SettingsUri);

    [Fact]
    public void Outside_a_package_the_app_was_not_launched_by_the_startup_task() =>
        Assert.False(StoreStartupTask.LaunchedAtLogon());

    // ── Actualizaciones: las da la Store ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Store_updates_never_offer_anything_nor_do_anything()
    {
        using var updates = new StoreUpdates();
        int changed = 0;
        updates.Changed += () => changed++;
        updates.StartLoop();
        await updates.CheckNowAsync();
        await updates.ApplyAndRestartAsync();

        Assert.Equal(UpdateStage.Idle, updates.Stage);
        Assert.Null(updates.AvailableVersion);
        Assert.Equal(0, updates.DownloadProgress);
        Assert.Equal(0, changed);
        // El notch no enseña oferta de actualización.
        Assert.Null(UpdateOfferView.From(updates.Stage, updates.AvailableVersion, updates.DownloadProgress));
    }

    // ── Edición y licencia ─────────────────────────────────────────────────────────────────────────────

    private sealed class NoStore : IProStateStore
    {
        public ProSnapshot Load() => default;
        public void SaveTrialStart(DateTime startUtc) { }
        public void SaveLastKnownPro(bool owns) { }
    }

    [Fact]
    public void The_edition_follows_package_identity()
    {
        // Sin paquete (los tests): la edición de GitHub, con su licencia gratis y el enlace a la Store.
        Assert.False(StoreEdition.IsActive);
        using var license = ProLicenseFactory.Create(StoreEdition.IsActive, () => throw new InvalidOperationException("sin Store"),
            new NoStore(), a => a(), null, _ => null);
        Assert.IsType<FreeEditionLicense>(license);
        Assert.False(license.CanPurchaseHere);

        // Con paquete, la licencia pregunta a la Store (StoreContext); aquí el backend falso lanza al crearse.
        Assert.Throws<InvalidOperationException>(() => ProLicenseFactory.Create(true,
            () => throw new InvalidOperationException("sin Store"), new NoStore(), a => a(), null, _ => null));
    }

    [Fact]
    public void Outside_the_store_startup_and_helper_behave_as_always()
    {
        // StartupManager y HelperDeployer solo cambian de camino con identidad de paquete.
        Assert.Null(StoreEdition.HelperAliasPath());
        Assert.EndsWith(Path.Combine("bin", HelperDeployer.ExeName), HelperDeployer.TargetPath);
    }
}
