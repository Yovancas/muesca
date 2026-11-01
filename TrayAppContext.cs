using System.Diagnostics;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.Services.Packaging;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.Services.Telemetry;
using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.Services.Updates;
using ClaudeBarWin.UI;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin;

public sealed class TrayAppContext : ApplicationContext
{
    public const string ShowSignalName = "ClaudeBarWin_Show";

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly UsageApiClient _api;
    private readonly TranscriptParser _parser;
    private readonly UsageHistoryStore _history;
    private readonly PlanResolver _planResolver;
    private PlanInfo _plan => _planResolver.Current;
    private DateTime _lastPruneUtc = DateTime.MinValue;
    private readonly DashboardForm _dashboard;
    private readonly SynchronizationContext _ui;
    private readonly EventWaitHandle _showSignal;

    private readonly SessionStore _sessions;
    private readonly SessionAggregator _sessionAgg;
    private readonly ForegroundDetector _foreground;
    private HookPipeServer? _pipe;
    // Registro de sesiones abiertas de Claude Code (~/.claude/sessions): descubre las que no han mandado hooks.
    private readonly SessionRegistryReader _registry = new();
    private FileSystemWatcher? _regWatcher;
    private System.Threading.Timer? _regDebounce;

    // Notch (F1): el hub publica sesiones y cuota en el hilo de UI; las ventanas (una, o una por monitor) solo existen con
    // NotchEnabled y las lleva NotchHost según el modo de monitor.
    private ClaudeStateHub _hub;
    private int[] _hubThresholds;
    private NotchHost? _notch;
    private NotchOptions? _notchOptions;
    private NotchMonitorMode _notchMode;
    private Strings? _notchStrings;
    // F2: pipe de permisos (opt-in). Vive solo con notch + permisos + sesiones en vivo, atado al broker del hub actual.
    private PermissionPipeServer? _permPipe;
    private PermissionBroker? _permPipeBroker;
    // Asistente de bienvenida dentro del notch (OnboardingPending o «Volver a configurar…»). Mientras esté abierto ES el
    // notch: el de verdad no existe hasta el relevo (HandOff), cuando el asistente ya se ha encogido a su tamaño de reposo.
    // Versión nueva en el notch: sale de IAppUpdates (OnUpdatesChanged → SetNotchUpdateOffer) y su botón «Actualizar» va
    // por el mismo camino que el menú (ApplyUpdateAsync). Se guarda aquí porque el host se rehace (asistente, umbrales, apagar).
    private UpdateOfferView? _updateOffer;
    private UI.Onboarding.OnboardingForm? _onboarding;
    private SetupMachine? _onboardingMachine;
    private bool _onboardingHoldsNotch;
    // Tutorial interactivo (menú › Tutorial, Ajustes › Acerca de o al final de la bienvenida): uno a la vez. No sustituye al
    // notch (es una demostración con datos de ejemplo); su progreso (capítulos hechos) va a la config al acabar cada capítulo.
    private UI.Tutorial.TutorialForm? _tutorial;
    private bool _tutorialWantsPro;
    private string? _appearSettledOn;   // relevo: el notch de ese monitor aparece ya asentado encima del asistente

    // Edición de la Store: recibe los avisos de cierre de Windows (cerrar sesión, apagar, actualizar desde la Store).
    private SessionEndWindow? _sessionEnd;

    private ToolStripMenuItem _miUpdate = null!;
    private IAppUpdates _updates = null!;
    // ClaudeBar Pro: la licencia (Store o edición gratis) decide qué funciones del notch se usan (NotchProGating, ProGate).
    private IProLicense _pro = null!;
    // Lo que dice la tarjeta de la última acción (Busy mientras la ventana de compra está abierta: nunca dos a la vez).
    private ProMessage _proMessage;
    // «Buscar actualizaciones» pulsado y la descarga sigue en segundo plano: avisar cuando acabe.
    private bool _updateCheckRequested;
    private ToolStripMenuItem _miInstallHooks = null!;
    private ToolStripMenuItem _miNotchMonitor = null!;
    private ToolStripMenuItem _miPro = null!;
    // Contador anónimo de uso (PRIVACY.md): un temporizador del pool, nada en el hilo de UI salvo guardar lo aceptado.
    private UsageStatsService? _stats;
    private System.Windows.Forms.Timer? _statsNoticeTimer;

    private AppConfig _config;
    private Strings _s;
    private Theme _theme;
    private string _menuLangCode;
    // Icono de bandeja cacheado por su especificación (F3 Task 7): sin cambios no se crea otro HICON; con
    // cambios se pinta al tamaño exacto del icono pequeño del DPI y el anterior se libera tras sustituirlo.
    private readonly TrayIconCache<Icon> _iconCache = new();

    private RealUsage? _lastUsage;
    private DateTime _lastUsageAtUtc;
    private readonly DateTime _startedUtc = DateTime.UtcNow;   // notch: el hub solo recibe cuota de esta ejecución

    // v0.4 — cuota desde la statusline (snapshots del helper) + API como respaldo lento.
    private readonly RateLimitSnapshotStore _snapshots = new(RateLimitSnapshotStore.DefaultDir);
    private FileSystemWatcher? _snapWatcher;
    private System.Windows.Forms.Timer? _snapDebounce;
    private RealUsage? _apiUsage;                       // último OK de la API (candidato de la fusión)
    private DateTime _apiAtUtc = DateTime.MinValue;
    private DateTime _lastApiAttemptUtc = DateTime.MinValue;
    private bool _refreshPending;
    private AppSnapshot? _lastSnapshot;
    private bool _busy;

    private readonly Dictionary<int, bool> _fired = new();
    private bool _milestonesInitialised;
    private bool _paceFiredFive, _paceFiredSeven;

    public TrayAppContext()
    {
        _config = AppConfig.Load();
        _s = Localization.ForConfig(_config);
        _theme = ThemeResolver.Resolve(_config);
        _menuLangCode = CurrentLangCode();
        _api = new UsageApiClient();
        _parser = new TranscriptParser();
        _history = new UsageHistoryStore();
        _history.Prune();
        // Sin red: caché del último perfil o .credentials.json (este, solo con «Leer la cuota con tu sesión…» encendido).
        _planResolver = new PlanResolver(_api, _config.QuotaOAuth);

        // Seed from the persisted last-good reading so we show something immediately
        // on startup / while rate-limited, before the first successful fetch.
        if (UsageCache.Load() is { } cached)
        {
            _lastUsage = cached.ToRealUsage();
            _lastUsageAtUtc = cached.SavedAtUtc;
            // La caché es la última cuota buena: entra en la fusión como una lectura de la API de esa hora.
            _apiUsage = _lastUsage;
            _apiAtUtc = _lastUsageAtUtc;
        }

        _dashboard = new DashboardForm();
        _ = _dashboard.Handle;
        // Contexto del hilo de UI capturado aquí (el primer Control ya lo instaló): Post no depende del HWND del
        // panel, que Windows puede destruir sin que el form se disponga (p. ej. si cae su ventana propietaria oculta).
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _dashboard.SetHistoryProvider(BuildHistoryAsync);
        _dashboard.SetPercentProvider(BuildPercentAsync);
        _dashboard.ChartModeChanged += m => { var c = AppConfig.Load(); c.ChartMode = m; c.Save(); _config = c; };
        _dashboard.ChartWindowChanged += win => { var c = AppConfig.Load(); c.ChartPctWindow = win; c.Save(); _config = c; };
        _dashboard.Moved += p =>
        {
            var c = AppConfig.Load();
            c.DashboardPosition = "Custom";
            c.DashboardX = p.X;
            c.DashboardY = p.Y;
            c.Save();
            _config = c;
        };

        _sessions = new SessionStore();
        _sessionAgg = new SessionAggregator();
        _foreground = new ForegroundDetector();
        _dashboard.SetLiveSessionsProvider(() => _sessionAgg.BuildView(_sessions.Snapshot()));
        _dashboard.SettingsChanged += a => MutateConfig(a);
        _dashboard.SpecialActionRequested += key =>
        {
            if (key == "special:importtheme") ImportItermColors();
            else if (key == "special:hooktoggle") ToggleHooks();
            else if (key == "special:statuslinetoggle") ToggleStatusline();
            else if (key == "special:notchpermtoggle") ToggleNotchPermissions();
            else if (key == DashboardSettingsView.PrivacyKey) OpenPrivacyPolicy();
            else if (key == DashboardSettingsView.TutorialKey) StartTutorial();
            else if (key == DashboardSettingsView.RemoveIntegrationKey) RemoveClaudeIntegration();
            else if (key.StartsWith(DashboardSettingsView.GetProKeyPrefix, StringComparison.Ordinal)) OpenProFromTray();
        };

        _sessions.Changed += OnSessionsChanged;

        _hubThresholds = _config.NotchQuotaThresholds;
        _hub = NewHub(_hubThresholds);

        if (_config.LiveSessionsEnabled) StartPipe();

        // v0.3.10: el hook instalado (script + eventos) se pone al día con esta versión de la app, y el
        // acceso directo de inicio se repara si apunta a un exe que ya no existe. En segundo plano:
        // detectar la versión de Claude Code lanza `claude --version`.
        // v0.4: antes se despliega el helper nativo; con él, los hooks migran del .ps1 al helper y el
        // enganche de la statusline (si está activo) se pone al día con la ruta de esta versión.
        bool notchPermissions = _config.NotchPermissions;   // F2: la entrada «permission» sigue al ajuste
        _ = Task.Run(() =>
        {
            var helper = HelperDeployer.Ensure();
            HookInstaller.UpgradeIfOutdated(HookScript.Contents(), helper, notchPermissions);
            if (helper is not null) UpgradeStatuslineIfNeeded(helper);
            StartupManager.RepairIfStale();
        });
        StartSnapshotWatcher();

        var initialSpec = TrayIconRenderer.SpecError(_theme.Neutral, sizePx: TrayIconRenderer.SmallIconSizePx());
        var initialIcon = _iconCache.Get(initialSpec, () => TrayIconRenderer.Render(initialSpec), out _);
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = Brand.Name + "…",
            Icon = initialIcon,
            ContextMenuStrip = BuildMenu()
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleDashboard();
        };

        _timer = new System.Windows.Forms.Timer { Interval = _config.RefreshIntervalMs };
        _timer.Tick += async (_, _) => await RefreshAsync();

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        new Thread(ShowSignalLoop) { IsBackground = true, Name = "ShowSignalListener" }.Start();

        // Actualizaciones (Velopack, v0.5): se descargan en segundo plano y la entrada del menú sigue la etapa. Changed ya
        // llega en el hilo de UI. Para aplicar, la app se cierra con normalidad (ExitApp) y Update.exe la vuelve a abrir.
        // En la edición de la Store las da la Store: sin Velopack y sin entrada en el menú. Al cerrar la sesión, al apagar o
        // cuando la Store la cierra para actualizarla, se cierra con normalidad dentro del propio aviso (SessionEndWindow), y
        // solo en este último caso Windows la vuelve a abrir después (StoreRestart: registrada solo en ese aviso).
        bool packaged = StoreEdition.IsActive;
        if (packaged)
        {
            StoreEdition.LogStartup();
            // «Iniciar con Windows» se lee y se cambia en segundo plano; los ajustes se repintan cuando se sabe o cambia.
            StoreStartupTask.Changed += OnStartupTaskChanged;
            StoreStartupTask.Prime();   // leído antes de que lo pinten los ajustes
            _sessionEnd = new SessionEndWindow(OnSessionEnded);
        }
        _updates = packaged ? new StoreUpdates() : VelopackUpdates.CreateDefault(PostToUi, ExitApp);
        _updates.Changed += OnUpdatesChanged;
        RefreshNotchUpdateOffer();   // una versión descargada en otra ejecución ya está lista sin que llegue Changed

        // ClaudeBar Pro: con identidad de paquete, la edición de la Store (complemento y prueba); sin ella, la gratis. Arranca
        // con el último estado conocido (sin esperar a la red) y Changed ya llega en el hilo de UI. La ventana dueña de la
        // compra se elige en cada compra (PurchaseOwner).
        _pro = ProLicenseFactory.Create(packaged,
            () => new WindowsStoreBackend(PurchaseOwner),
            new ConfigProStateStore(() => AppConfig.LoadOrThrow(AppConfig.ConfigPath), MutateConfig), PostToUi, _config.ProStoreProductId,
            Environment.GetEnvironmentVariable);
        _pro.Changed += OnProChanged;
        _dashboard.SetProState(_pro.State);
        StartUsageStats(packaged);

        ApplyNotch();   // apagado por defecto: sin ventana ni hooks

        _ = RefreshAsync();
        _timer.Start();
        _updates.StartLoop();   // comprobación silenciosa al arrancar + cada 6 h (y descarga en segundo plano)
        _ = _pro.RefreshAsync();   // la Store, en segundo plano: un fallo no cambia nada
        // Recién actualizada: Update.exe la ha vuelto a abrir tras aplicar («Reiniciar para actualizar»).
        if (VelopackStartup.UpdatedTo is { } updated)
            PostToUi(() =>
            {
                try { _tray.ShowBalloonTip(5000, Brand.Name, string.Format(_s.UpdatedFmt, updated), ToolTipIcon.Info); }
                catch { /* cerrando */ }
            });

        // Primera vez (lo marca el instalador): el asistente de bienvenida, ya con el bucle de mensajes en marcha.
        if (_config.OnboardingPending) PostToUi(() => StartOnboarding(fresh: true));
    }

    public static string DescribeMenu()
    {
        var s = Localization.Get("en");
        var sb = new System.Text.StringBuilder();
        // Menú minimal: toda la configuración vive en el panel de ajustes ⚙ del dashboard.
        sb.AppendLine(s.Dashboard);
        sb.AppendLine(s.Settings);
        sb.AppendLine(s.MenuReconfigure);
        sb.AppendLine(s.Tut.Menu);
        sb.AppendLine(s.MenuLiveSessions);
        sb.AppendLine(s.CheckUpdates);
        sb.AppendLine(NotchProText.Menu(new ProSummary(ProState.Free, null, false), s));
        sb.AppendLine(s.Exit);
        return sb.ToString();
    }

    private void ShowSignalLoop()
    {
        while (true)
        {
            try
            {
                _showSignal.WaitOne();
                PostToUi(ShowDashboard);
            }
            catch
            {
                break;
            }
        }
    }

    // ---------- Menu ----------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };

        var miDash = new ToolStripMenuItem(_s.Dashboard);
        miDash.Click += (_, _) => ToggleDashboard();

        var miSettings = new ToolStripMenuItem(_s.Settings);
        miSettings.Click += (_, _) => { ShowDashboard(); _dashboard.ShowSettings(); };

        // El asistente de bienvenida otra vez, dentro del notch (con los valores de ahora).
        var miReconfigure = new ToolStripMenuItem(_s.MenuReconfigure);
        miReconfigure.Click += (_, _) => StartOnboarding(fresh: false);

        // El tutorial interactivo (retoma en el primer capítulo sin hacer).
        var miTutorial = new ToolStripMenuItem(_s.Tut.Menu);
        miTutorial.Click += (_, _) => StartTutorial();

        // Monitor del notch (como Nusa): se rellena al abrir el menú (UpdateNotchMonitorMenu) porque los monitores
        // pueden conectarse o desconectarse con la app abierta.
        _miNotchMonitor = new ToolStripMenuItem(_s.NotchMonitorMenu) { Visible = false };

        // Activación de sesiones en vivo (instala/desinstala los hooks): única acción de "config"
        // que sobrevive en el menú porque necesita el instalador. El resto vive en el panel ⚙.
        _miInstallHooks = new ToolStripMenuItem(_s.MenuLiveSessions);
        _miInstallHooks.Click += (_, _) => ToggleHooks();

        // En la edición de la Store no hay «Buscar actualizaciones»: las da la Store (StoreUpdates). Aquí y no al crear las
        // actualizaciones, porque el menú se rehace al cambiar de idioma.
        _miUpdate = new ToolStripMenuItem(_s.CheckUpdates) { Available = !StoreEdition.IsActive };
        _miUpdate.Click += async (_, _) => await OnUpdateMenuClick();

        // ClaudeBar Pro con su estado (Gratis / Prueba: N días / Pro): el texto se pone al abrir el menú (UpdateMenuChecks),
        // porque el menú se crea antes que la licencia y el estado cambia con la app abierta.
        _miPro = new ToolStripMenuItem(_s.NotchProTitle + "…");
        _miPro.Click += (_, _) => OpenProFromTray();

        var miExit = new ToolStripMenuItem(_s.Exit);
        miExit.Click += (_, _) => ExitApp();

        menu.Items.Add(miDash);
        menu.Items.Add(miSettings);
        menu.Items.Add(miReconfigure);
        menu.Items.Add(miTutorial);
        menu.Items.Add(_miNotchMonitor);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miInstallHooks);
        menu.Items.Add(_miUpdate);
        menu.Items.Add(_miPro);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miExit);

        menu.Opening += (_, _) => UpdateMenuChecks();
        _dashboard?.AttachContextMenu(menu); // right-click on the panel opens the same menu
        return menu;
    }

    private void UpdateMenuChecks()
    {
        // Live sessions: el texto refleja si los hooks están instalados (activar / desactivar).
        _miInstallHooks.Checked = HookInstaller.IsInstalled();
        _miInstallHooks.Text = HookInstaller.IsInstalled() ? _s.MenuUninstallHooks : _s.MenuInstallHooks;

        RefreshUpdateMenuItem();
        UpdateNotchMonitorMenu();
        if (_pro is not null) _miPro.Text = NotchProText.Menu(ProSummary.From(_pro), _s);
    }

    /// <summary>
    /// «ClaudeBar Pro…» (y las filas Pro de Ajustes): la tarjeta en el notch (en la ventana interactiva; se queda un rato aunque
    /// el puntero no esté encima). Sin notch a la vista (apagado, con el asistente o en pantalla completa) no se compra a
    /// ciegas: un globo con el estado (y, en la Store, que se compra desde la tarjeta del notch); en la edición GitHub, la
    /// ficha de la Store.
    /// </summary>
    private void OpenProFromTray()
    {
        if (_exiting || _pro is null) return;
        if (_proMessage != ProMessage.Busy) _proMessage = ProMessage.None;   // la tarjeta se abre sin el mensaje de la vez anterior
        RefreshNotchProOffer();
        if (_notch?.ShowProCard() == true) return;
        if (_pro.State != ProState.Pro && !_pro.CanPurchaseHere)
        {
            OpenProStore();
            return;
        }
        try { _tray.ShowBalloonTip(5000, _s.NotchProTitle, NotchProText.Balloon(ProSummary.From(_pro), _s), ToolTipIcon.Info); }
        catch { /* cerrando */ }
    }

    /// <summary>La dueña de la ventana de compra, pedida al empezar cada compra: la ventana interactiva del notch (viva y a
    /// la vista: la compra sale de su tarjeta); si no, el panel (su HWND se rehace si Windows lo destruyó). Fuera del hilo de
    /// UI, ninguna.</summary>
    private IntPtr PurchaseOwner()
    {
        if (_dashboard.IsDisposed || _dashboard.InvokeRequired) return IntPtr.Zero;
        if (_notch?.PurchaseOwner() is { } h && h != IntPtr.Zero) return h;
        return _dashboard.Handle;
    }

    // ---------- Actualizaciones ----------

    /// <summary>Texto de la entrada según la etapa (buscar, descargando…, reiniciar para actualizar); deshabilitada mientras
    /// hay algo en marcha.</summary>
    private void RefreshUpdateMenuItem()
    {
        if (_exiting || _updates is null || _miUpdate is null) return;
        _miUpdate.Text = UpdateMenu.Text(_updates.Stage, _updates.AvailableVersion, _updates.DownloadProgress, _s);
        _miUpdate.Enabled = UpdateMenu.Action(_updates.Stage, _updates.AvailableVersion) != UpdateMenuAction.None;
    }

    private void OnUpdatesChanged()
    {
        RefreshUpdateMenuItem();
        RefreshNotchUpdateOffer();
        // Tras «Buscar actualizaciones», cuando acaba la descarga en segundo plano (bien o mal), se avisa.
        if (_updateCheckRequested && _updates.Stage is UpdateStage.ReadyToApply or UpdateStage.Failed)
        {
            _updateCheckRequested = false;
            ShowUpdateBalloon();
        }
    }

    private async Task OnUpdateMenuClick()
    {
        try
        {
            switch (UpdateMenu.Action(_updates.Stage, _updates.AvailableVersion))
            {
                case UpdateMenuAction.Apply:
                    await ApplyUpdateAsync();
                    break;
                case UpdateMenuAction.Check:
                    await _updates.CheckNowAsync();
                    ShowUpdateBalloon();
                    _updateCheckRequested = _updates.Stage == UpdateStage.Downloading;
                    break;
            }
        }
        catch (Exception ex) { AppLog.Error("actualizaciones: fallo en la acción del menú", ex); }
        RefreshUpdateMenuItem();
    }

    /// <summary>
    /// Aplicar la versión nueva y reiniciar, desde el menú o desde el botón «Actualizar» del notch. Si sale bien, la app se
    /// cierra (ExitApp) y Update.exe la vuelve a abrir ya actualizada; si falla, el aviso con la ruta del log.
    /// </summary>
    private async Task ApplyUpdateAsync()
    {
        await _updates.ApplyAndRestartAsync();
        if (_updates.Stage == UpdateStage.Failed) ShowUpdateBalloon();
    }

    /// <summary>«Actualizar» en el notch (ventana interactiva, con la versión ya lista).</summary>
    private async void OnNotchUpdateRequested()
    {
        if (_exiting) return;
        try { await ApplyUpdateAsync(); }
        catch (Exception ex) { AppLog.Error("actualizaciones: fallo al actualizar desde el notch", ex); }
        RefreshUpdateMenuItem();
    }

    /// <summary>La oferta del notch al día con <see cref="IAppUpdates"/> (Changed ya llega en el hilo de UI).</summary>
    private void RefreshNotchUpdateOffer()
    {
        if (_exiting || _updates is null) return;
        SetNotchUpdateOffer(UpdateOfferView.From(_updates.Stage, _updates.AvailableVersion, _updates.DownloadProgress));
    }

    private void ShowUpdateBalloon()
    {
        if (_exiting) return;
        var text = UpdateMenu.AfterCheck(_updates.Stage, _updates.AvailableVersion, AppVersion, AppLog.LogPath, _s);
        try
        {
            _tray.ShowBalloonTip(6000, Brand.Name, text,
                _updates.Stage == UpdateStage.Failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        catch { /* cerrando */ }
    }

    /// <summary>
    /// Rehace el submenú «Monitor del notch» con los monitores de AHORA (lectura nueva del EDID): oculto con el notch
    /// apagado o un solo monitor. Elegir guarda por <see cref="MutateConfig"/>, el mismo camino que Ajustes, y
    /// <see cref="ApplyNotch"/> mueve el notch en vivo, sin reiniciar.
    /// </summary>
    private void UpdateNotchMonitorMenu()
    {
        IReadOnlyList<MonitorInfo> monitors;
        try { monitors = ClaudeBarWin.Services.Notch.Platform.MonitorCatalog.Current(refresh: true); }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudieron leer los monitores: {ex.Message}");
            monitors = Array.Empty<MonitorInfo>();
        }

        _miNotchMonitor.Text = _s.NotchMonitorMenu;
        // Sin el Pro, un solo notch en el principal: el submenú lo enseña así (lo efectivo, sin poder cambiarlo) y su primera
        // entrada abre la tarjeta. Lo guardado en la config no se toca: con el Pro vuelve tal cual.
        bool locked = !ProGate.IsUnlocked(ProFeature.MultiMonitor, _pro.State);
        FillNotchMonitorMenu(_miNotchMonitor, _config.NotchEnabled, locked ? NotchMonitorMode.Fixed : _config.NotchMonitorMode,
            locked ? "" : _config.NotchMonitor, monitors, _s,
            mode => MutateConfig(c => NotchMonitorMenu.ChooseMode(c, mode)),
            chosen => MutateConfig(c => NotchMonitorMenu.Choose(c, chosen)),
            locked ? OpenProFromTray : null);
    }

    /// <summary>
    /// Rellena el submenú «Monitor del notch» (y lo enseña u oculta): arriba los tres modos como opciones de radio, un
    /// separador y debajo la lista de monitores, activa solo en Fijo (en los otros modos el monitor lo decide la ventana
    /// activa, o hay uno en cada monitor). Se llama desde el Opening del menú, cuando este aún no está en pantalla: ahí
    /// <c>ToolStripItem.Visible</c> devuelve false aunque se acabe de poner a true, así que la decisión va en una variable y
    /// nunca se lee de <c>Visible</c> (beta del 29-oct: salía sin monitores).
    /// </summary>
    /// <param name="openPro">Sin el Pro (varios monitores bloqueados): la entrada de arriba lo abre y el resto se ve sin poder
    /// elegirse. null = con el Pro, como siempre.</param>
    internal static void FillNotchMonitorMenu(ToolStripMenuItem menu, bool notchEnabled, NotchMonitorMode mode, string? current,
        IReadOnlyList<MonitorInfo> monitors, Strings s, Action<NotchMonitorMode> chooseMode, Action<MonitorMenuItem> choose,
        Action? openPro = null)
    {
        var old = menu.DropDownItems.Cast<ToolStripItem>().ToList();
        menu.DropDownItems.Clear();
        foreach (var it in old) it.Dispose();

        bool show = NotchMonitorMenu.IsVisible(notchEnabled, monitors.Count);
        menu.Visible = show;
        if (!show) return;

        bool locked = openPro is not null;
        if (locked)
        {
            var pro = new ToolStripMenuItem(s.ProMonitorsLocked);
            pro.Click += (_, _) => openPro!();
            menu.DropDownItems.Add(pro);
            menu.DropDownItems.Add(new ToolStripSeparator());
        }
        foreach (var item in NotchMonitorMenu.Modes(mode, s))
        {
            var mi = new ToolStripMenuItem(item.Text) { Checked = item.Checked, Enabled = !locked };
            var chosen = item.Mode;
            mi.Click += (_, _) => chooseMode(chosen);
            menu.DropDownItems.Add(mi);
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        bool monitorsOn = NotchMonitorMenu.MonitorsEnabled(mode) && !locked;
        foreach (var item in NotchMonitorMenu.Items(current, monitors, s))
        {
            // Sin el Pro se marca el principal (lo efectivo), sin poder elegir otro.
            var mi = new ToolStripMenuItem(item.Text) { Checked = item.Checked && (monitorsOn || locked), Enabled = monitorsOn };
            var chosen = item;
            mi.Click += (_, _) => choose(chosen);
            menu.DropDownItems.Add(mi);
        }
    }

    // ---------- Notch (F1) ----------

    private static ClaudeStateHub NewHub(int[] thresholds) => new(new QuotaThresholdTracker(thresholds));

    /// <summary>
    /// Lleva la ventana del notch al estado de la config: la crea (y la muestra sin foco) al activarlo, la destruye
    /// al apagarlo y, si sigue viva, le pasa ajustes e idioma nuevos. Si cambian los umbrales de cuota se rehace el
    /// hub (el tracker nuevo arranca con su gracia: no avisa de lo que ya esté por encima) y con él la ventana.
    /// Siempre en el hilo de UI (constructor, MutateConfig y RefreshAsync lo están).
    /// </summary>
    private void ApplyNotch()
    {
        ApplyNotchWindow();
        ApplyPermissionPipe();
    }

    private void ApplyNotchWindow()
    {
        var cfg = _config;
        var thresholds = cfg.NotchQuotaThresholds ?? Array.Empty<int>();
        if (!thresholds.SequenceEqual(_hubThresholds))
        {
            DestroyNotch();
            _hubThresholds = thresholds;
            _hub = NewHub(thresholds);
        }

        if (!cfg.NotchEnabled || _onboardingHoldsNotch)
        {
            DestroyNotch();
            return;
        }

        // Lo que se puede usar con el Pro de ahora (candados, un solo monitor): al cambiar el estado, en caliente.
        var (options, mode, _) = PlanNotch(cfg, _exiting, _onboardingHoldsNotch, _pro.State);
        if (_notch is null)
        {
            // El hub nuevo (o el que nunca se usó) se pone al día antes de crear la ventana: así aparece ya
            // con las sesiones y el % de 5 h, sin esperar al próximo evento ni al próximo refresco.
            // La caché vieja del arranque se ENSEÑA (si no, las barras salen vacías mientras la API da 429), pero no
            // cuenta para los avisos 80/95: el tracker gastaría su gracia con ella (QuotaPolicy.IsCurrentForRun).
            if (_hub.Quota is null && _lastUsage is not null)
                _hub.PublishQuota(_lastUsage, observe: QuotaPolicy.IsCurrentForRun(_lastUsageAtUtc, _startedUtc));
            if (_config.LiveSessionsEnabled && _hub.Sessions.Count == 0) _hub.PublishSessions(_sessions.Snapshot(), DateTime.UtcNow);
            _notchOptions = options;
            _notchMode = mode;
            _notchStrings = _s;
            _notch = new NotchHost(_hub, options, mode, _s, _appearSettledOn);
            _appearSettledOn = null;
            _notch.SetUpdateOffer(_updateOffer);
            _notch.UpdateRequested += OnNotchUpdateRequested;
            _notch.SetProOffer(BuildProOffer());
            _notch.ProActionRequested += OnNotchProAction;
            _notch.ProNoticeShown += OnProNoticeShown;
            _notch.UsageSeriesRequested += OnNotchUsageSeries;
            _notch.SetUsage(new UsageInput(_lastSnapshot, _plan));
            // El pomodoro sigue donde iba (también al rehacer el notch): primero quién guarda, luego lo guardado.
            _notch.FocusStateChanged += OnNotchFocusState;
            // Al rehacerlo, el pomodoro VIVO del notch anterior (DestroyNotch): ni se vuelve a cerrar una fase ya contada (hoy y
            // su tarea en tasks.json) ni un fallo pasajero al releer config.json lo pone a cero. Si no, lo último que pidió guardar
            // (puede seguir en cola o haber fallado) y, al arrancar, lo de disco.
            _notch.RestoreFocus(_focusLive ?? _focusToSave ?? _config.NotchFocusState);
            _focusLive = null;
            // La página Uso abre como se dejó (lo último pedido, aunque su escritura siga en vuelo; si no, lo de disco).
            _notch.UsageViewChanged += OnNotchUsageView;
            _notch.RestoreUsageView(_usageViewLast ?? _config.NotchUsageView);
            PostToUi(MaybeShowTrialEndedNotice);   // fuera de ApplyNotch: guardar que salió vuelve a pasar por aquí
            return;
        }
        if (options != _notchOptions || mode != _notchMode || !Equals(_notchStrings, _s))
        {
            _notchOptions = options;
            _notchMode = mode;
            _notchStrings = _s;
            _notch.Apply(options, mode, _s);   // en caliente: modo, monitor e idioma sin reiniciar
        }
    }

    /// <summary>
    /// F2: el pipe <c>claudebar-perm</c> solo escucha con el notch encendido, el ajuste de permisos activo y las sesiones
    /// en vivo instaladas (sin notch nadie contestaría y el helper esperaría sus 25 s en vez de caer al diálogo en
    /// &lt; 1 s). Si el hub se rehízo (umbrales nuevos), se vuelve a abrir atado a su broker. Hilo de UI.
    /// </summary>
    private void ApplyPermissionPipe()
    {
        var cfg = _config;
        bool want = PlanNotch(cfg, _exiting, _onboardingHoldsNotch, _pro.State).PermissionPipe;
        if (_permPipe is not null && (!want || !ReferenceEquals(_permPipeBroker, _hub.Permissions))) StopPermissionPipe();
        if (!want || _permPipe is not null) return;
        try
        {
            _permPipe = new PermissionPipeServer(_hub.Permissions);
            _permPipeBroker = _hub.Permissions;
            _permPipe.Start();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudo abrir el pipe de permisos: {ex.Message}");
            StopPermissionPipe();
        }
    }

    /// <summary>Lo que el notch debe ser: opciones y modo de monitores ya pasados por <see cref="NotchProGating"/>, y si escucha
    /// el pipe de permisos (<see cref="PermissionPipeWanted"/>).</summary>
    internal readonly record struct NotchPlan(NotchOptions Options, NotchMonitorMode Mode, bool PermissionPipe);

    /// <summary>
    /// El notch que toca con la config, el Pro y el momento. PURO: es lo único que deciden <see cref="ApplyNotchWindow"/> y
    /// <see cref="ApplyPermissionPipe"/> (y por tanto <see cref="OnProChanged"/>, en caliente, al comprar o acabar la prueba);
    /// así los tests prueban el desbloqueo con el mismo cálculo que la bandeja.
    /// </summary>
    internal static NotchPlan PlanNotch(AppConfig cfg, bool exiting, bool onboardingHoldsNotch, ProState pro)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        var requested = new NotchOptions(cfg.NotchOpenOnClick, cfg.NotchMinimal, cfg.NotchHideInFullscreen, cfg.NotchSound,
            cfg.NotchSizePercent, cfg.NotchMonitor ?? "", cfg.ReduceMotion, cfg.NotchAutoMinimalSeconds,
            Music: cfg.NotchMusic, Lyrics: cfg.NotchLyrics, ShelfClearOnStart: cfg.NotchShelfClearOnStart,
            Clock: cfg.NotchClock, Mascot: cfg.NotchMascot, Tasks: cfg.NotchTasks, Contrast: cfg.NotchAdaptiveContrast,
            CompactLyrics: cfg.NotchCompactLyrics, CompactLyricsArt: cfg.NotchCompactLyricsArt,
            CompactLyricsQuota: cfg.NotchCompactLyricsQuota,
            Focus: new FocusSettings(cfg.NotchFocusDurations, cfg.NotchFocusSound), ShowSpend: cfg.ShowSpendEstimate);
        var (options, mode) = NotchProGating.Apply(requested, cfg.NotchMonitorMode, pro);
        return new NotchPlan(options, mode, PermissionPipeWanted(cfg, exiting, onboardingHoldsNotch, pro));
    }

    /// <summary>
    /// ¿Escucha el pipe de permisos? Con el notch, el ajuste y las sesiones en vivo; no con el asistente a la vista (no hay
    /// notch que conteste: el helper cae al diálogo de siempre al momento) ni sin el Pro (<see cref="ProFeature.Permissions"/>:
    /// sin pipe el helper no recibe respuesta y Claude Code enseña su diálogo, el camino seguro de siempre). PURO.
    /// </summary>
    internal static bool PermissionPipeWanted(AppConfig cfg, bool exiting, bool onboardingHoldsNotch, ProState pro) =>
        cfg.NotchEnabled && cfg.NotchPermissions && cfg.LiveSessionsEnabled && !exiting && !onboardingHoldsNotch
        && ProGate.IsUnlocked(ProFeature.Permissions, pro);

    /// <summary>Cierra el pipe de permisos: la petición pendiente se cierra sin decisión (Claude Code muestra su diálogo).</summary>
    private void StopPermissionPipe()
    {
        var p = _permPipe;
        _permPipe = null;
        _permPipeBroker = null;
        try { p?.Dispose(); }
        catch (Exception ex) { AppLog.Warn($"cerrando el pipe de permisos: {ex.Message}"); }
    }

    /// <summary>La versión nueva para el notch (null = ya no hay): la tienen todas sus ventanas y la recibe cada host nuevo.</summary>
    private void SetNotchUpdateOffer(UpdateOfferView? offer)
    {
        _updateOffer = offer;
        _notch?.SetUpdateOffer(offer);
    }

    /// <summary>El estado del Pro cambió (compra, prueba que acaba, la Store que responde): ya en el hilo de UI. Candados,
    /// pipe de permisos y monitores se ponen al día en caliente, sin reiniciar.</summary>
    private void OnProChanged()
    {
        if (_exiting) return;
        if (_proMessage != ProMessage.Busy) _proMessage = ProMessage.None;   // el estado nuevo ya lo dice
        _dashboard.SetProState(_pro.State);
        ApplyNotch();
        RefreshNotchProOffer();
        MaybeShowTrialEndedNotice();
    }

    /// <summary>El Pro tal como lo ve el notch (estado, precio y lo que dice la tarjeta).</summary>
    private NotchProOffer BuildProOffer() => NotchProOffer.From(_pro, _proMessage);

    private void RefreshNotchProOffer()
    {
        if (_exiting || _pro is null) return;
        _notch?.SetProOffer(BuildProOffer());
    }

    /// <summary>
    /// El único aviso del fin de la prueba, dentro del notch y una sola vez. Se mete en la cola de cada notch nuevo mientras no
    /// se haya visto; se apunta en config.json cuando SE VE (<see cref="OnProNoticeShown"/>). Sin notch (apagado o con el
    /// asistente a la vista) espera a que lo haya; nunca un globo ni una ventana.
    /// </summary>
    private void MaybeShowTrialEndedNotice()
    {
        if (_exiting || _notch is null || !ProTrial.EndNoticeDue(_pro.State, _pro.TrialEnded, _config.ProTrialEndNoticeShown)) return;
        _notch.ShowProNotice();
    }

    /// <summary>El aviso del fin de la prueba ya se ve: se apunta (fuera del Sync de la ventana: guardar pasa por ApplyNotch).</summary>
    private void OnProNoticeShown() => PostToUi(() =>
    {
        if (_exiting || _config.ProTrialEndNoticeShown) return;
        AppLog.Info("pro: la prueba ha terminado (aviso en el notch)");
        MutateConfig(c => c.ProTrialEndNoticeShown = true);
    });

    /// <summary>Un botón de la tarjeta del Pro (ventana interactiva).</summary>
    private async void OnNotchProAction(NotchProAction action)
    {
        try { await RunProActionAsync(action); }
        catch (Exception ex) { AppLog.Error("pro: fallo en la acción de la tarjeta", ex); }
    }

    /// <summary>
    /// Comprar o restaurar (Store) mientras la tarjeta dice «Abriendo Microsoft Store…» y sus botones no responden; al acabar,
    /// cómo fue (el desbloqueo llega por <see cref="OnProChanged"/>, en caliente). En la edición GitHub, la ficha de la Store.
    /// </summary>
    private async Task RunProActionAsync(NotchProAction action)
    {
        if (_exiting || _proMessage == ProMessage.Busy) return;
        if (action == NotchProAction.OpenStore || !_pro.CanPurchaseHere)
        {
            OpenProStore();
            return;
        }
        _proMessage = ProMessage.Busy;
        RefreshNotchProOffer();
        PurchaseResult result;
        try { result = action == NotchProAction.Restore ? await _pro.RestoreAsync() : await _pro.PurchaseAsync(); }
        catch (Exception ex)
        {
            AppLog.Error("pro: la compra falló", ex);
            result = PurchaseResult.Failed;
        }
        if (_exiting) return;
        _proMessage = NotchProOffer.MessageFor(result);
        RefreshNotchProOffer();
        if (result == PurchaseResult.Purchased || (action == NotchProAction.Restore && result == PurchaseResult.AlreadyOwned))
        {
            try { _tray.ShowBalloonTip(5000, Brand.Name, _s.ProThanks, ToolTipIcon.Info); }
            catch { /* cerrando */ }
        }
    }

    /// <summary>La ficha de ClaudeBar en Microsoft Store (edición GitHub: ahí se compra el Pro).</summary>
    private void OpenProStore()
    {
        if (_pro.StoreUrl is not { } url) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn($"pro: no se pudo abrir la Store: {ex.Message}"); }
    }

    private void DestroyNotch()
    {
        var n = _notch;
        _notch = null;
        _notchOptions = null;
        _notchStrings = null;
        if (n is null) return;
        n.UpdateRequested -= OnNotchUpdateRequested;
        n.ProActionRequested -= OnNotchProAction;
        n.ProNoticeShown -= OnProNoticeShown;
        n.UsageSeriesRequested -= OnNotchUsageSeries;
        n.FocusStateChanged -= OnNotchFocusState;
        n.UsageViewChanged -= OnNotchUsageView;
        // El pomodoro tal como va, para el notch que se cree después (CreateNotch): restaurarlo de disco podría contar otra vez
        // una fase ya cerrada o perderlo si config.json no se pudo leer.
        try { _focusLive = n.Focus.Save(); }
        catch (Exception ex) { AppLog.Warn($"notch: no se pudo leer el pomodoro: {ex.Message}"); }
        try { n.Dispose(); }
        catch (Exception ex) { AppLog.Warn($"cerrando el notch: {ex.Message}"); }
    }

    // ---------- Asistente de bienvenida (dentro del notch) ----------

    /// <summary>
    /// Abre el asistente de bienvenida: en la primera vez (<paramref name="fresh"/>, <see cref="AppConfig.OnboardingPending"/>)
    /// con lo recomendado; desde la bandeja, con los valores de ahora. Si ya está abierto, lo trae al frente. Mientras dura, el
    /// notch de verdad no existe (el asistente ES el notch) y el pipe de permisos se cierra (nadie contestaría).
    /// </summary>
    private void StartOnboarding(bool fresh)
    {
        if (_exiting) return;
        _tutorialAfterOnboarding = false;
        if (_onboarding is { IsDisposed: false } open)
        {
            if (open.Result is null)
            {
                open.BringToFront(activate: true);
                return;
            }
            // Ya terminó y se está yendo (encogiéndose o tapando el notch nuevo casi un segundo): se suelta y se abre otro.
            ReleaseLeavingOnboarding(open);
        }
        UI.Onboarding.OnboardingForm? created = null;
        try
        {
            var machine = SetupEnvironment.DetectMachine();
            bool hooks;
            try { hooks = HookInstaller.IsInstalled(); }
            catch { hooks = false; }
            var cfg = AppConfig.Load();
            var context = new OnboardingContext(machine, cfg, hooks, StartupManager.IsEnabled(), fresh, ClaudePaths.ConfigDir,
                ProSummary.From(_pro));
            var services = new UI.Onboarding.OnboardingServices(ApplyOnboarding, () => _hub.Quota, () => _hub.Sessions);
            // Sale donde empezará el notch: su monitor de casa (fijo o siguiendo a la ventana activa: las ventanas de ClaudeBar,
            // este asistente incluido, no cuentan como la activa) o, con uno por monitor o sin el Pro, el principal.
            var (monitorMode, homeMonitor) = NotchProGating.Placement(cfg.NotchMonitorMode, cfg.NotchMonitor, _pro.State);
            string monitor = monitorMode == NotchMonitorMode.AllMonitors ? "" : homeMonitor;
            var form = new UI.Onboarding.OnboardingForm(context, Localization.ForConfig(cfg), services, AppConfig.Load,
                cfg.ReduceMotion, monitor);
            created = form;
            form.HandOff += OnOnboardingHandOff;
            form.FormClosed += OnOnboardingClosed;
            _onboarding = form;
            _onboardingMachine = machine;
            _onboardingHoldsNotch = true;
            ApplyNotch();
            form.Show();
        }
        catch (Exception ex)
        {
            // Sin asistente la app sigue como siempre: el notch vuelve y la próxima vez se intenta otra vez. La ventana a medio
            // crear se suelta (sin sus eventos: su cierre no debe tocar a otro asistente que se abra después).
            AppLog.Error("no se pudo abrir el asistente de bienvenida", ex);
            if (created is not null)
            {
                created.HandOff -= OnOnboardingHandOff;
                created.FormClosed -= OnOnboardingClosed;
                try { created.Dispose(); }
                catch (Exception disposeEx) { AppLog.Warn($"cerrando el asistente: {disposeEx.Message}"); }
            }
            _onboarding = null;
            _onboardingHoldsNotch = false;
            ApplyNotch();
        }
    }

    /// <summary>«Empezar» o «Saltar»: lo elegido sobre config.json con el núcleo de <c>--apply-setup</c>; si va bien, el
    /// asistente ya no está pendiente y la app se pone al día (idioma, pipe, sesiones en vivo). El notch espera al relevo.</summary>
    private SetupOutcome ApplyOnboarding(SetupAnswers answers)
    {
        var outcome = SetupEnvironment.ApplyOnboarding(answers,
            a => SetupEnvironment.Apply(a, _onboardingMachine ?? SetupEnvironment.DetectMachine()), MutateConfig);
        // Bienvenida vista: el contador puede empezar (en 2-5 min), si sigue activado.
        if (outcome.Status == SetupStatus.Applied) _stats?.Kick(UsageStatsService.RandomFirstDelay());
        return outcome;
    }

    /// <summary>El asistente ya es el notch de reposo (o se va sin notch): se crea el de verdad, que queda encima.</summary>
    private void OnOnboardingHandOff(UI.Onboarding.OnboardingForm form)
    {
        _onboardingHoldsNotch = false;
        _appearSettledOn = form.Landed ? form.MonitorDevice : null;
        ApplyNotch();
        _appearSettledOn = null;
        ShowOnboardingOutcome(form.Result);
        // «Ver el tutorial al terminar»: cuando el asistente ya se ha ido (el tutorial sale en el mismo sitio).
        if (form.Result is { Tutorial: true, Applied: true }) _tutorialAfterOnboarding = true;
    }

    private bool _tutorialAfterOnboarding;

    // ---------- Tutorial interactivo ----------

    /// <summary>
    /// Abre el tutorial (o lo trae al frente si ya está abierto). Con el asistente de bienvenida abierto, el asistente va
    /// primero. Sale en el monitor del notch y retoma en el primer capítulo sin hacer. Nunca toca los datos del usuario.
    /// </summary>
    private void StartTutorial()
    {
        if (_exiting) return;
        if (_tutorial is { IsDisposed: false } open)
        {
            open.BringToFront(activate: true);
            return;
        }
        if (_onboarding is { IsDisposed: false, Result: null } wizard)
        {
            wizard.BringToFront(activate: true);
            return;
        }
        UI.Tutorial.TutorialForm? created = null;
        try
        {
            var cfg = AppConfig.Load();
            var (monitorMode, homeMonitor) = NotchProGating.Placement(cfg.NotchMonitorMode, cfg.NotchMonitor, _pro.State);
            string monitor = monitorMode == NotchMonitorMode.AllMonitors ? "" : homeMonitor;
            var form = new UI.Tutorial.TutorialForm(TutorialMachine.Parse(cfg.TutorialChaptersDone), Localization.ForConfig(cfg),
                _pro.State, cfg.ReduceMotion, monitor);
            created = form;
            form.ProgressChanged += OnTutorialProgress;
            form.ProRequested += OnTutorialPro;
            form.FormClosed += OnTutorialClosed;
            _tutorial = form;
            _tutorialWantsPro = false;
            form.Show();
        }
        catch (Exception ex)
        {
            AppLog.Error("no se pudo abrir el tutorial", ex);
            if (created is not null)
            {
                ReleaseTutorialEvents(created);
                try { created.Dispose(); }
                catch (Exception disposeEx) { AppLog.Warn($"cerrando el tutorial: {disposeEx.Message}"); }
            }
            _tutorial = null;
        }
    }

    /// <summary>Un capítulo hecho: a la config (para retomar), sin tocar nada más.</summary>
    private void OnTutorialProgress(UI.Tutorial.TutorialForm form)
    {
        var done = TutorialMachine.Format(form.ChaptersDone);
        try { MutateConfig(c => c.TutorialChaptersDone = done); }
        catch (Exception ex) { AppLog.Warn($"tutorial: no se pudo guardar el progreso: {ex.Message}"); }
    }

    /// <summary>«Ver Pro ›»: el tutorial se encoge y, ya cerrado, sale la tarjeta del Pro en el notch (si no, lo taparía).</summary>
    private void OnTutorialPro(UI.Tutorial.TutorialForm form)
    {
        _tutorialWantsPro = true;
        form.LeaveAnimated();
    }

    private void OnTutorialClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is UI.Tutorial.TutorialForm form)
        {
            ReleaseTutorialEvents(form);
            BeginInvokeDispose(form);
        }
        _tutorial = null;
        if (_tutorialWantsPro && !_exiting)
        {
            _tutorialWantsPro = false;
            PostToUi(OpenProFromTray);
        }
    }

    private void ReleaseTutorialEvents(UI.Tutorial.TutorialForm form)
    {
        form.ProgressChanged -= OnTutorialProgress;
        form.ProRequested -= OnTutorialPro;
        form.FormClosed -= OnTutorialClosed;
    }

    private void CloseTutorial()
    {
        var form = _tutorial;
        _tutorial = null;
        if (form is null) return;
        ReleaseTutorialEvents(form);
        try { form.Dismiss(); }
        catch (Exception ex) { AppLog.Warn($"cerrando el tutorial: {ex.Message}"); }
    }

    /// <summary>Tras aplicar: aviso si los hooks fallaron o, sin notch, de que la app sigue en la bandeja.</summary>
    private void ShowOnboardingOutcome(UI.Onboarding.OnboardingResult? result)
    {
        if (result?.Outcome is not { Status: SetupStatus.Applied } outcome) return;
        try
        {
            if (outcome.HooksError is not null) _tray.ShowBalloonTip(8000, Brand.Name, _s.OnbHooksFailed, ToolTipIcon.Warning);
            else if (!_config.NotchEnabled) _tray.ShowBalloonTip(5000, Brand.Name, _s.OnbInTray, ToolTipIcon.Info);
        }
        catch { }
    }

    /// <summary>
    /// «Volver a configurar…» mientras el anterior se va: se cierra ya, sin sus eventos (su cierre no debe tocar al nuevo).
    /// Si aún no había hecho el relevo, su aviso sale ahora; el notch no se crea, porque el asistente nuevo lo sustituye.
    /// </summary>
    private void ReleaseLeavingOnboarding(UI.Onboarding.OnboardingForm old)
    {
        old.HandOff -= OnOnboardingHandOff;
        old.FormClosed -= OnOnboardingClosed;
        if (_onboardingHoldsNotch) ShowOnboardingOutcome(old.Result);
        _onboarding = null;
        _onboardingMachine = null;
        try { old.Dismiss(); }
        catch (Exception ex) { AppLog.Warn($"cerrando el asistente: {ex.Message}"); }
        BeginInvokeDispose(old);
    }

    private void OnOnboardingClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is UI.Onboarding.OnboardingForm form)
        {
            form.HandOff -= OnOnboardingHandOff;
            form.FormClosed -= OnOnboardingClosed;
            BeginInvokeDispose(form);
        }
        _onboarding = null;
        _onboardingMachine = null;
        if (_tutorialAfterOnboarding && !_exiting)
        {
            _tutorialAfterOnboarding = false;
            PostToUi(StartTutorial);
        }
        if (!_onboardingHoldsNotch) return;
        // Cerrado sin relevo (la app sale, o algo falló): el notch vuelve como estaba.
        _onboardingHoldsNotch = false;
        ApplyNotch();
    }

    /// <summary>Libera la ventana del asistente en el siguiente mensaje (no dentro de su propio FormClosed).</summary>
    private void BeginInvokeDispose(Form form) => PostToUi(() =>
    {
        try { form.Dispose(); }
        catch (Exception ex) { AppLog.Warn($"cerrando el asistente: {ex.Message}"); }
    });

    private void CloseOnboarding()
    {
        var form = _onboarding;
        _onboarding = null;
        _onboardingHoldsNotch = false;
        try { form?.Dismiss(); }
        catch (Exception ex) { AppLog.Warn($"cerrando el asistente: {ex.Message}"); }
    }

    // ---------- Live sessions (hook -> Named Pipe) ----------

    private void StartPipe()
    {
        if (_pipe is not null) return;
        _pipe = new HookPipeServer();
        _pipe.EventReceived += e => _sessions.Apply(e, DateTime.UtcNow);
        _pipe.Start();
        StartRegistryWatcher();
        KickRegistrySync(0); // pasada al arrancar: las sesiones ya abiertas aparecen sin esperar a un hook
    }

    private void StopPipe()
    {
        _pipe?.Dispose();
        _pipe = null;
        StopRegistryWatcher();
    }

    /// <summary>
    /// Vigila ~/.claude/sessions (*.json; los .key ni se miran). Cada cambio dispara, con debounce de 500 ms,
    /// una pasada del registro en un hilo del pool; el store avisa con Changed y OnSessionsChanged publica
    /// al hilo de UI como con los hooks. Si la carpeta aún no existe, el tick del refresco lo reintenta.
    /// </summary>
    private void StartRegistryWatcher()
    {
        if (_regWatcher is not null || _exiting) return;
        try
        {
            _regDebounce ??= new System.Threading.Timer(_ => SyncSessionRegistry());
            if (!Directory.Exists(_registry.Dir)) return;
            var w = new FileSystemWatcher(_registry.Dir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            FileSystemEventHandler kick = (_, _) => KickRegistrySync(500);
            w.Created += kick;
            w.Changed += kick;
            w.Deleted += kick;
            w.Renamed += (s, e) => kick(s, e);
            // Desbordamiento del buffer o carpeta borrada: el watcher queda inservible. Se suelta para que el
            // tick lo vuelva a crear (cuando la carpeta exista) y se fuerza una pasada para no perder cambios.
            w.Error += (_, ex) =>
            {
                AppLog.Warn($"watcher del registro de sesiones caído: {ex.GetException()?.Message}");
                if (Interlocked.CompareExchange(ref _regWatcher, null, w) == w)
                    try { w.Dispose(); } catch { }
                KickRegistrySync(500);
            };
            w.EnableRaisingEvents = true;
            _regWatcher = w;
        }
        catch (Exception ex) { AppLog.Warn($"sin watcher del registro de sesiones: {ex.Message}"); }
    }

    private void StopRegistryWatcher()
    {
        var w = Interlocked.Exchange(ref _regWatcher, null);
        try { w?.Dispose(); } catch { }
        try { _regDebounce?.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
    }

    /// <summary>Programa una pasada del registro dentro de <paramref name="dueMs"/> (reinicia el debounce).</summary>
    private void KickRegistrySync(int dueMs)
    {
        try { _regDebounce?.Change(dueMs, Timeout.Infinite); } catch (ObjectDisposedException) { }
    }

    /// <summary>Lee el registro y lo fusiona en el store. Barato (unos pocos JSON pequeños); thread-safe.</summary>
    private void SyncSessionRegistry()
    {
        if (_exiting || !_config.LiveSessionsEnabled) return;
        try
        {
            var live = _registry.ReadLive();
            if (live is not null) _sessions.SyncRegistry(live, DateTime.UtcNow);
        }
        catch (Exception ex) { AppLog.Warn($"leyendo el registro de sesiones: {ex.Message}"); }
    }

    private void OnSessionsChanged()
    {
        // Llega desde los hilos del pipe, del debounce del registro (pool) y del tick (UI): el diff de avisos
        // y _lastPending no son thread-safe, así que todo esto va serializado. PostToUi solo encola (no
        // bloquea) y, al encolar dentro del lock, los snapshots llegan a la UI en orden.
        lock (_sessionsChangedLock)
        {
            // Poda por proceso muerto + TTL por fase (también se llama en cada tick del refresco).
            _sessions.Prune(DateTime.UtcNow);

            // Avisos: diff + supresión por foco.
            var snap = _sessions.Snapshot();
            foreach (var s in _sessionAgg.DiffNotifications(snap, DateTime.UtcNow))
            {
                if (_config.SuppressWhenFocused && _foreground.IsSessionForeground(s)) continue;
                NotifySession(s);
            }

            // Refrescar mascota/lista en el hilo de UI. El icono de bandeja solo se re-renderiza si cambia el
            // aviso pendiente (antes: re-render + UpdateData + re-escaneo de la gráfica en CADA evento de hook,
            // o sea en cada tool call de cada sesión con el panel abierto).
            bool pending = LiveAttentionPending();
            bool pendingChanged = pending != _lastPending;
            _lastPending = pending;
            PostToUi(() =>
            {
                _hub.PublishSessions(snap, DateTime.UtcNow);   // notch: snapshot + señales de transición
                _dashboard.OnLiveSessionsChanged();
                if (pendingChanged) RefreshTrayIcon();
            });
        }
    }

    private readonly object _sessionsChangedLock = new();
    private bool _lastPending;

    private void NotifySession(Models.SessionState s)
    {
        if (!_config.NotificationsEnabled) return;
        var fmt = s.Phase == Models.SessionPhase.WaitingForApproval
            ? _s.NotifWaitingApprovalFmt : _s.NotifWaitingInputFmt;
        var icon = s.Phase == Models.SessionPhase.WaitingForApproval
            ? ToolTipIcon.Warning : ToolTipIcon.Info;
        try { _tray.ShowBalloonTip(5000, _s.LiveSessionsTitle, string.Format(fmt, s.ProjectName), icon); } catch { }
    }

    private bool LiveAttentionPending()
        => _config.LiveSessionsEnabled
           && _sessionAgg.BuildView(_sessions.Snapshot()).GlobalPhase.NeedsAttention();

    private void RefreshTrayIcon()
    {
        if (_lastSnapshot is null || _lastUsage is null) return;
        UpdateUi(_lastSnapshot, updateDashboard: false); // solo el icono; el panel ya se refresca por su lado
    }

    /// <summary>Acción especial del panel de ajustes (fila "Importar .itermcolors"): abre el diálogo,
    /// importa la paleta y la aplica como tema "imported". Necesita UI (OpenFileDialog), por eso vive aquí.</summary>
    private void ImportItermColors()
    {
        try
        {
            using var dlg = new OpenFileDialog
            {
                Title = _s.ImportTheme,
                Filter = "iTerm colors (*.itermcolors)|*.itermcolors|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            var colors = ItermColorsImporter.TryImport(dlg.FileName);
            if (colors is null) return;
            MutateConfig(c => { c.ImportedTheme = colors; c.Theme = "imported"; });
        }
        catch { }
    }

    private void ToggleHooks()
    {
        // Instalar/quitar hooks toca la configuración GLOBAL de Claude Code (~/.claude/settings.json),
        // que puede estar usándose en sesiones 24/7. Por eso ambos sentidos piden confirmación explícita
        // (botón por defecto "No" → no se dispara con un Enter/clic accidental).
        if (HookInstaller.IsInstalled())
        {
            var confirm = MessageBox.Show(
                "Vas a DESACTIVAR las sesiones en vivo de Muesca.\n\n" +
                "Se quitarán los hooks de tu configuración global de Claude Code (~/.claude/settings.json) " +
                "y la mascota y los avisos de estado dejarán de actualizarse.\n\n" +
                "Se hará una copia de seguridad de settings.json antes de tocarlo.\n\n" +
                "¿Seguro que quieres desactivarlas?",
                _s.MenuLiveSessions, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            try { HookInstaller.Uninstall(DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); }
            catch (Exception ex) { HookError(ex); return; }
            MutateConfig(c => c.LiveSessionsEnabled = false);
            StopPipe();
            _tray.ShowBalloonTip(4000, _s.LiveSessionsTitle, _s.HooksRemoved, ToolTipIcon.Info);
            return;
        }

        var ok = MessageBox.Show(
            "Vas a ACTIVAR las sesiones en vivo de Muesca.\n\n" +
            "Esto añadirá hooks a tu configuración global de Claude Code (~/.claude/settings.json) para que " +
            "Muesca reciba el estado de tus sesiones (mascota + avisos de bandeja).\n\n" +
            "Se hará una copia de seguridad de settings.json antes de modificarlo.\n\n" +
            "¿Continuar?",
            _s.MenuLiveSessions, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (ok != DialogResult.Yes) return;

        string backup;
        try
        {
            backup = HookInstaller.Install(HookScript.Contents(), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
                HelperDeployer.Current, _config.NotchPermissions);
        }
        catch (Exception ex) { HookError(ex); return; }
        MutateConfig(c => c.LiveSessionsEnabled = true);
        StartPipe();
        _tray.ShowBalloonTip(5000, _s.LiveSessionsTitle, string.Format(_s.HooksInstalledFmt, backup), ToolTipIcon.Info);
    }

    /// <summary>
    /// «Quitar la integración con Claude Code» (Ajustes, y la guía «Antes de desinstalar» de la Store): quita de una vez los
    /// hooks y el enganche de la statusline de ~/.claude/settings.json, con confirmación («No» por defecto) y una copia de
    /// seguridad (<see cref="ClaudeIntegration.Remove"/>). Lo ajeno no se toca. Después, la app queda como con todo apagado:
    /// sin sesiones en vivo ni permisos desde el notch, y la cuota vuelve a la API (si está encendida).
    /// </summary>
    private void RemoveClaudeIntegration()
    {
        if (!ClaudeIntegration.IsInstalled())
        {
            MessageBox.Show(_s.RemoveIntegrationNothing, _s.RemoveIntegration, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var ask = MessageBox.Show(_s.RemoveIntegrationConfirm, _s.RemoveIntegration, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (ask != DialogResult.Yes) return;

        string? backup;
        try { backup = ClaudeIntegration.Remove(DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); }
        catch (Exception ex) { HookError(ex); return; }
        AppLog.Info($"integración con Claude Code quitada (copia: {backup ?? "no hacía falta"})");
        _snapshots.Clear();   // sin statusline enganchada, la cuota vuelve a la API al momento
        MutateConfig(c =>
        {
            c.LiveSessionsEnabled = false;   // MutateConfig para el pipe de los hooks
            c.NotchPermissions = false;
        });
        try
        {
            _tray.ShowBalloonTip(6000, Brand.Name,
                backup is null ? _s.RemoveIntegrationNothing : string.Format(_s.RemoveIntegrationDoneFmt, backup), ToolTipIcon.Info);
        }
        catch { /* cerrando */ }
    }

    /// <summary>
    /// F2: activa/desactiva los permisos desde el notch. Añade/quita la entrada <c>permission</c> de PermissionRequest en
    /// la config GLOBAL de Claude Code: confirmación en ambos sentidos con «No» por defecto y backup de settings.json
    /// (igual que la statusline). Sin sesiones en vivo o sin helper no hay nada que enganchar: se avisa y no se toca.
    /// </summary>
    private void ToggleNotchPermissions()
    {
        bool on = _config.NotchPermissions;
        var helper = HelperDeployer.Current;
        if (!on && helper is null)
        {
            try { _tray.ShowBalloonTip(5000, Brand.Name, _s.TapUnavailable, ToolTipIcon.Warning); } catch { }
            return;
        }
        if (!on && !HookInstaller.IsInstalled())
        {
            try { _tray.ShowBalloonTip(6000, Brand.Name, _s.NotchPermissionsNeedHooks, ToolTipIcon.Info); } catch { }
            return;
        }
        var ask = MessageBox.Show(on ? _s.NotchPermissionsDisableConfirm : _s.NotchPermissionsEnableConfirm, _s.NotchPermissions,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (ask != DialogResult.Yes) return;

        string? backup;
        try { backup = HookInstaller.ApplyPermissions(DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), helper, permissions: !on); }
        catch (Exception ex) { HookError(ex); return; }
        MutateConfig(c => c.NotchPermissions = !on);   // abre o cierra el pipe (ApplyNotch)
        try
        {
            if (on) _tray.ShowBalloonTip(4000, Brand.Name, _s.NotchPermissionsDisabled, ToolTipIcon.Info);
            else _tray.ShowBalloonTip(5000, Brand.Name, string.Format(_s.NotchPermissionsEnabledFmt, backup ?? "—"), ToolTipIcon.Info);
        }
        catch { }
    }

    // ---------- v0.4: cuota desde la statusline ----------

    /// <summary>
    /// Activa/desactiva el enganche de la statusline. Toca la config GLOBAL de Claude Code: confirmación en ambos
    /// sentidos con «No» por defecto y backup de settings.json (igual que los hooks).
    /// </summary>
    private void ToggleStatusline()
    {
        var helper = HelperDeployer.Current;
        if (helper is null)
        {
            try { _tray.ShowBalloonTip(5000, Brand.Name, _s.TapUnavailable, ToolTipIcon.Warning); } catch { }
            return;
        }
        bool on = StatuslineInstaller.IsInstalled();
        bool api = _config.QuotaOAuth; // sin API, quitar la statusline deja a ClaudeBar sin cuota: se avisa
        var ask = MessageBox.Show(on ? (api ? _s.TapDisableConfirm : _s.TapDisableConfirmNoApi) : _s.TapEnableConfirm, _s.QuotaSourceHeader,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (ask != DialogResult.Yes) return;

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        try
        {
            if (on)
            {
                ClaudeSettingsFile.Rewrite(stamp, StatuslineInstaller.Disable);
                _snapshots.Clear(); // la API vuelve a mandar al momento
                _tray.ShowBalloonTip(4000, Brand.Name, api ? _s.TapDisabled : _s.TapDisabledNoApi, ToolTipIcon.Info);
            }
            else
            {
                var shell = ClaudeShellDetector.Detect();
                var backup = ClaudeSettingsFile.Rewrite(stamp, cur => StatuslineInstaller.Enable(cur, helper, shell));
                _tray.ShowBalloonTip(5000, Brand.Name, string.Format(_s.TapEnabledFmt, backup), ToolTipIcon.Info);
            }
        }
        catch (TapInstallException ex)
        {
            AppLog.Warn($"statusline no enganchada ({ex.Code}): {ex.Message}");
            try { _tray.ShowBalloonTip(8000, Brand.Name, ex.Code == "compound" ? _s.TapCompoundError : _s.TapTypeError, ToolTipIcon.Warning); } catch { }
        }
        catch (Exception ex) { HookError(ex); }
        _ = RefreshAsync(fetchUsage: false);
    }

    /// <summary>Al arrancar: si el enganche está activo pero con la ruta, el shell o la forma de otra versión (también la de
    /// antes del «tee» resistente a que falte el helper), lo pone al día.</summary>
    private static void UpgradeStatuslineIfNeeded(string helper)
    {
        try
        {
            var shell = ClaudeShellDetector.Detect();
            var path = ClaudeSettingsFile.SettingsPath;
            var current = File.Exists(path) ? File.ReadAllText(path) : "{}";
            if (StatuslineInstaller.Upgrade(current, helper, shell) is null) return;
            ClaudeSettingsFile.Rewrite(DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), cur => StatuslineInstaller.Upgrade(cur, helper, shell) ?? cur);
            AppLog.Info("statusline de Muesca actualizada a la ruta/forma de esta versión");
        }
        catch (Exception ex) { AppLog.Warn($"no se pudo actualizar la statusline: {ex.Message}"); }
    }

    /// <summary>
    /// Cada snapshot nuevo del helper dispara (debounce 750 ms) un refresco SIN API: la cuota se ve segundos
    /// después de cada mensaje de Claude en vez de esperar al siguiente tick.
    /// </summary>
    private void StartSnapshotWatcher()
    {
        try
        {
            Directory.CreateDirectory(_snapshots.Dir);
            _snapDebounce = new System.Windows.Forms.Timer { Interval = 750 };
            _snapDebounce.Tick += async (_, _) => { _snapDebounce!.Stop(); await RefreshAsync(fetchUsage: false); };
            _snapWatcher = new FileSystemWatcher(_snapshots.Dir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            FileSystemEventHandler kick = (_, _) =>
            {
                PostToUi(() => { _snapDebounce!.Stop(); _snapDebounce.Start(); });
            };
            _snapWatcher.Created += kick;
            _snapWatcher.Changed += kick;
            _snapWatcher.Renamed += (s, e) => kick(s, e);
            _snapWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { AppLog.Warn($"sin watcher de snapshots: {ex.Message}"); }
    }

    /// <summary>Sugerencia de UNA vez: si hay Claude Code y el enganche está disponible pero apagado.</summary>
    private void MaybeSuggestTap()
    {
        if (_config.StatuslineTapSuggested || HelperDeployer.Current is null || StatuslineInstaller.IsInstalled()) return;
        if (!File.Exists(ClaudePaths.CredentialsPath)) return; // sin Claude Code no tiene sentido
        MutateConfig(c => c.StatuslineTapSuggested = true);
        try
        {
            _tray.BalloonTipClicked -= OnSuggestClicked;
            _tray.BalloonTipClosed -= OnSuggestClosed;
            _tray.BalloonTipClicked += OnSuggestClicked;
            _tray.BalloonTipClosed += OnSuggestClosed; // que un globo POSTERIOR no abra los ajustes al pulsarlo
            _tray.ShowBalloonTip(8000, Brand.Name, _s.TapSuggest, ToolTipIcon.Info);
        }
        catch { }
    }

    private void OnSuggestClicked(object? sender, EventArgs e)
    {
        OnSuggestClosed(sender, e);
        ShowDashboard();
        _dashboard.ShowSettings();
    }

    private void OnSuggestClosed(object? sender, EventArgs e)
    {
        _tray.BalloonTipClicked -= OnSuggestClicked;
        _tray.BalloonTipClosed -= OnSuggestClosed;
    }

    /// <summary>settings.json ilegible, bloqueado o cambiando sin parar: avisar en vez de reventar la UI.</summary>
    private void HookError(Exception ex)
    {
        AppLog.Error("no se pudieron cambiar los hooks de settings.json", ex);
        try { _tray.ShowBalloonTip(6000, _s.LiveSessionsTitle, ex.Message, ToolTipIcon.Error); } catch { }
    }

    private string CurrentLangCode() =>
        string.IsNullOrEmpty(_config.Language) || _config.Language == "system"
            ? Localization.ResolveSystemCode()
            : _config.Language;

    private void RebuildMenu()
    {
        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = BuildMenu();
        old?.Dispose();
        _menuLangCode = CurrentLangCode();
    }

    private void MutateConfig(Action<AppConfig> change)
    {
        var c = AppConfig.Load();
        change(c);
        c.Save();
        _config = c;
        _s = Localization.ForConfig(c);
        _theme = ThemeResolver.Resolve(c);

        int desired = c.RefreshIntervalMs;
        if (_timer.Interval != desired) _timer.Interval = desired;

        if (CurrentLangCode() != _menuLangCode)
            PostToUi(RebuildMenu);

        if (_config.LiveSessionsEnabled && _pipe is null) StartPipe();
        else if (!_config.LiveSessionsEnabled && _pipe is not null) StopPipe();

        _stats?.SetEnabled(_config.UsageStatsEnabled);   // apagado: se cancela y para al momento
        ApplyNotch();

        // v0.3.10: un cambio de ajustes NO pide cuota a la API (antes cada clic en el panel ⚙ gastaba una
        // petición del límite por cuenta, compartido con ccstatusline). Se re-renderiza con el último dato.
        _ = RefreshAsync(fetchUsage: false);
    }

    // ---------- Data ----------

    private UsageFetchState _lastFetchState = UsageFetchState.Ok;

    private async Task RefreshAsync(bool fetchUsage = true)
    {
        // v0.4: un refresco pedido mientras otro corre (p. ej. llega un snapshot) ya no se pierde: se repite al acabar.
        if (_busy) { _refreshPending = true; return; }
        _busy = true;
        try
        {
            _config = AppConfig.Load();
            _s = Localization.ForConfig(_config);
            _theme = ThemeResolver.Resolve(_config);
            int desiredInterval = _config.RefreshIntervalMs;
            if (_timer.Interval != desiredInterval) _timer.Interval = desiredInterval;
            _stats?.SetEnabled(_config.UsageStatsEnabled);   // también si se edita config.json a mano
            ApplyNotch();   // config.json editado a mano (o idioma «system» que cambió): sin cambios, no hace nada

            // T13b: refresh del catálogo de tarifas en background — fire-and-forget, JAMÁS bloquea
            // este refresh de UI. No-op si la caché tiene <7 días o el toggle de privacidad está OFF.
            PricingCatalog.Default.RefreshInBackground(_config.PricingOnlineUpdate);

            var cfg = _config;
            var now = DateTime.UtcNow;
            // Sesiones muertas sin SessionEnd (crash, ventana cerrada): antes solo se podaban al llegar
            // otro evento, así que en reposo se quedaban en la lista para siempre.
            // v0.4: antes de podar, el registro de sesiones abiertas (las vivas no caducan por TTL) y, si su
            // carpeta apareció después de arrancar, se engancha ahora el watcher.
            if (_config.LiveSessionsEnabled)
            {
                StartRegistryWatcher();
                SyncSessionRegistry();
                _sessions.Prune(now);
            }

            // v0.4: la statusline de las sesiones abiertas trae 5h/7d sin gastar peticiones; mientras esté fresca
            // la API solo se consulta cada 15 min (Opus/Sonnet, extra_usage y uso hecho fuera de Claude Code).
            var snaps = QuotaPolicy.UsableSnapshots(StatuslineInstaller.IsInstalled(), _snapshots.ReadAll(now));
            DateTime? seen = snaps.Count > 0 ? snaps.Max(s => s.SeenUtc) : null;
            QuotaIntegration.LastSeenUtc = seen;
            // Con «Leer la cuota con tu sesión de Claude Code» apagado no hay API: ni se lee el token ni se llama.
            bool callApi = fetchUsage && cfg.QuotaOAuth && QuotaPolicy.ShouldCallApi(seen, _lastApiAttemptUtc, now);
            if (callApi) _lastApiAttemptUtc = now;

            var (result, spend, health) = await Task.Run(async () =>
            {
                var r = callApi
                    ? await _api.FetchAsync(cfg.QuotaCliRefresh)
                    : new UsageResult { State = _lastFetchState }; // sin Usage: no se re-muestrea el histórico
                WindowStats? s = null;
                if (cfg.ShowSpendEstimate)
                {
                    var window = TimeSpan.FromDays(cfg.SpendWindowDays);
                    s = UsageAggregator.Build(_parser.Read(now - window), now, window, window).Week;
                }
                HealthStatus? h = cfg.ShowHealth ? await StatusClient.FetchAsync() : null;
                // Plan real (API de perfil): solo en los refrescos del temporizador; dentro se limita a 1/h.
                if (fetchUsage) await _planResolver.RefreshAsync(cfg.QuotaOAuth);
                return (r, s, h);
            });

            if (callApi) _lastFetchState = result.State;
            if (callApi && result.State == UsageFetchState.Ok && result.Usage is not null)
            {
                _apiUsage = result.Usage;
                _apiAtUtc = now;
            }

            // Fusión statusline + API: ventana más nueva y, dentro, el mayor % (nunca baja un valor más nuevo).
            var merged = QuotaMerger.Merge(snaps, _apiUsage, _apiAtUtc, now);
            if (merged is not null && (_lastUsage is null || merged.ObservedUtc > _lastUsageAtUtc))
            {
                _lastUsage = merged.Usage;
                _lastUsageAtUtc = merged.ObservedUtc;
                UsageCache.Save(merged.Usage, merged.ObservedUtc);
                _history.Append(merged.Usage, now);   // sample real % into SQLite (throttle 60 s dentro)
                if (now - _lastPruneUtc > TimeSpan.FromHours(1)) { _history.Prune(); _lastPruneUtc = now; }
            }
            else if (merged is not null) _lastUsage = merged.Usage; // p. ej. llegó Opus/Sonnet de la API sin cambiar 5h/7d

            // Statusline fresca = dato bueno aunque la API esté en 429 u offline: no se enseña «datos previos».
            bool liveFromStatusline = merged is { FromStatusline: true } && QuotaPolicy.IsFresh(seen, now);
            var effectiveState = QuotaPolicy.EffectiveState(cfg.QuotaOAuth, liveFromStatusline, result.State);
            MaybeSuggestTap();

            PaceResult? paceFive = null, paceSeven = null;
            if (_lastUsage is not null)
            {
                var recent = _history.RecentForRate(now.AddHours(-6));
                (paceFive, paceSeven) = PaceCalculator.Compute(_lastUsage, recent, now);
            }

            var snap = new AppSnapshot
            {
                Usage = _lastUsage,
                LatestState = effectiveState,
                UsageAtUtc = _lastUsageAtUtc,
                LiveFromStatusline = liveFromStatusline,
                Spend = spend,
                SpendDays = cfg.SpendWindowDays,
                Health = health,
                PaceFive = paceFive,
                PaceSeven = paceSeven
            };
            _lastSnapshot = snap;
            UpdateUi(snap);
        }
        catch (Exception ex)
        {
            _tray.Text = Brand.Name + " — error";
            AppLog.Error("refresco fallido", ex);
        }
        finally
        {
            _busy = false;
            if (_refreshPending) { _refreshPending = false; _ = RefreshAsync(fetchUsage: false); }
        }
    }

    private void UpdateUi(AppSnapshot snap, bool updateDashboard = true)
    {
        BadgeSpec spec;
        int iconPx = TrayIconRenderer.SmallIconSizePx();
        if (snap.Usage is { } u)
        {
            var (icoVal, _, status) = IconContent(u, snap);
            // "stale": el dato envejeció (supera 3× el refresco) o la última lectura no fue Ok.
            bool stale = UsageFormat.IsSnapshotStaleAt(snap, DateTime.UtcNow, _config.RefreshSeconds)
                         || snap.LatestState != UsageFetchState.Ok;
            spec = TrayIconRenderer.Spec(icoVal, _theme, _config.WarnThresholdPct, _config.CriticalThresholdPct,
                status, stale, pending: LiveAttentionPending(), taskbarLight: ThemeResolver.TaskbarIsLight(),
                sizePx: iconPx);

            string five = UsageFormat.Countdown(u.FiveHour?.ResetsAt, _s.Resetting);
            string week = UsageFormat.Countdown(u.SevenDay?.ResetsAt, _s.Resetting);
            string tip = $"5h {Fmt(u.FiveHour)}{Suffix(five)}\n7d {Fmt(u.SevenDay)}{Suffix(week)}";
            if (snap.LatestState == UsageFetchState.StatuslineOnly)
                tip += "\n" + UsageFormat.LastReadingAt(snap.UsageAtUtc, DateTime.UtcNow, _s);
            else if (snap.LatestState != UsageFetchState.Ok)
            {
                tip += "\n" + _s.PreviousDataTip;
                if (snap.LatestState == UsageFetchState.RateLimited && _api.CooldownUntilUtc > DateTime.UtcNow)
                    tip += $" · ↻{_api.CooldownUntilUtc.ToLocalTime():HH:mm}";
            }
            _tray.Text = Truncate(tip, 127);

            CheckMilestones(u.MaxUtilization, u);
            CheckPace(snap);
            // notch: % de 5 h y cruces 80/95. La caché vieja que devuelve la fusión con la API en 429/offline se
            // enseña pero no cuenta para los avisos: su primera lectura debe ser real para que la gracia de arranque
            // cubra lo que ya estaba por encima.
            _hub.PublishQuota(u, observe: QuotaPolicy.IsCurrentForRun(snap.UsageAtUtc, _startedUtc));
        }
        else
        {
            spec = TrayIconRenderer.SpecError(_theme.Neutral, pending: LiveAttentionPending(), sizePx: iconPx);
            _tray.Text = Brand.Name + " — " + UsageFormat.StateMessage(snap.LatestState, _s);
        }

        var icon = _iconCache.Get(spec, () => TrayIconRenderer.Render(spec), out var retired);
        if (!ReferenceEquals(_tray.Icon, icon)) _tray.Icon = icon;
        retired?.Dispose(); // ya no lo muestra la bandeja

        if (updateDashboard && _dashboard.Visible)
            _dashboard.UpdateData(snap, _config, _plan);
        _notch?.SetUsage(new UsageInput(snap, _plan));   // página Uso del notch: números al día
    }

    // ---------- Milestone notifications ----------

    private void CheckMilestones(double pct, RealUsage usage)
    {
        var set = (_config.NotifyMilestones ?? Array.Empty<int>())
            .Where(m => m is > 0 and <= 100).Distinct().OrderBy(m => m).ToList();

        foreach (var m in set)
            if (!_fired.ContainsKey(m)) _fired[m] = pct >= m;
        foreach (var stale in _fired.Keys.Where(k => !set.Contains(k)).ToList())
            _fired.Remove(stale);

        if (!_milestonesInitialised)
        {
            foreach (var m in set) _fired[m] = pct >= m;
            _milestonesInitialised = true;
            return;
        }

        int highestCrossed = -1;
        foreach (var m in set)
        {
            if (pct >= m)
            {
                if (!_fired[m]) { _fired[m] = true; if (m > highestCrossed) highestCrossed = m; }
            }
            else
            {
                _fired[m] = false;
            }
        }

        if (highestCrossed >= 0 && _config.NotificationsEnabled)
            NotifyMilestone(highestCrossed, usage);
    }

    private void NotifyMilestone(int milestone, RealUsage u)
    {
        (string dot, ToolTipIcon icon) = milestone switch
        {
            >= 95 => ("🔴", ToolTipIcon.Error),
            >= 75 => ("🟠", ToolTipIcon.Warning),
            >= 50 => ("🟡", ToolTipIcon.Warning),
            _ => ("🟢", ToolTipIcon.Info)
        };

        _tray.BalloonTipIcon = icon;
        _tray.BalloonTipTitle = $"{dot} {string.Format(_s.NotifQuotaFormat, milestone)}";
        _tray.BalloonTipText = $"5h {Fmt(u.FiveHour)} · 7d {Fmt(u.SevenDay)}  ({_plan.Display})";
        _tray.ShowBalloonTip(6000);
    }

    // ---------- Pace ----------

    private (int value, Color color, UsageStatus status) IconContent(RealUsage u, AppSnapshot snap)
    {
        int maxPct = (int)Math.Round(u.MaxUtilization);
        var worst = WorstPace(snap);
        // Estado por color/forma derivado del pace (peor ventana) o, sin pace, del % de uso.
        UsageStatus paceStatus = worst is null
            ? StatusFor(u.MaxUtilization)
            : worst.Status switch
            {
                PaceStatus.Critical => UsageStatus.Critical,
                PaceStatus.Over => UsageStatus.Warn,
                _ => UsageStatus.Ok
            };
        Color paceColor = Theme.StatusColor(_theme, paceStatus);
        UsageStatus usageStatus = StatusFor(u.MaxUtilization);

        return _config.IconDisplayMode switch
        {
            "pace" => (worst is not null ? (int)Math.Round(worst.PaceRatio * 100) : maxPct, paceColor, paceStatus),
            "both" => (maxPct, paceColor, paceStatus),
            _ => (maxPct, Theme.StatusColor(_theme, usageStatus), usageStatus)
        };
    }

    private static PaceResult? WorstPace(AppSnapshot snap)
    {
        var list = new[] { snap.PaceFive, snap.PaceSeven }.Where(p => p is not null).Cast<PaceResult>().ToList();
        return list.Count == 0
            ? null
            : list.OrderByDescending(p => (int)p.Status).ThenByDescending(p => p.PaceRatio).First();
    }

    private void CheckPace(AppSnapshot snap)
    {
        HandlePaceWindow(snap.PaceFive, ref _paceFiredFive, _s.WinSession);
        HandlePaceWindow(snap.PaceSeven, ref _paceFiredSeven, _s.WinWeekly);
    }

    private void HandlePaceWindow(PaceResult? p, ref bool fired, string windowName)
    {
        if (p is null) { fired = false; return; }
        if (p.ExhaustsBeforeReset)
        {
            if (!fired)
            {
                fired = true;
                if (_config.NotificationsEnabled && _config.PaceAlerts) NotifyPace(p, windowName);
            }
        }
        else fired = false;
    }

    private void NotifyPace(PaceResult p, string windowName)
    {
        // ETA con el día abreviado del idioma elegido (T2), no el de la CurrentCulture del SO.
        string eta = UsageFormat.ResetAbsolute(p.EtaUtc, _s.Culture);
        _tray.BalloonTipIcon = ToolTipIcon.Warning;
        _tray.BalloonTipTitle = _s.PaceAlertTitle;
        _tray.BalloonTipText = string.Format(_s.PaceAlertBodyFmt, windowName, eta);
        _tray.ShowBalloonTip(7000);
    }

    // ---------- Helpers ----------

    // % del tooltip/notificaciones con la cultura del idioma elegido (T2): instancia porque lee _s.
    private string Fmt(UsageWindow? w) => w is null ? "—" : UsageFormat.Percent(w.UtilizationPct, _s.Culture);
    private static string Suffix(string countdown) => countdown.Length > 0 ? $" (↻{countdown})" : "";

    private UsageStatus StatusFor(double pct) =>
        pct >= _config.CriticalThresholdPct ? UsageStatus.Critical :
        pct >= _config.WarnThresholdPct ? UsageStatus.Warn :
        UsageStatus.Ok;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private void ToggleDashboard()
    {
        // F3 Task 6: cierre con fade; un clic durante el fade de cierre vuelve a abrir.
        if (_dashboard.IsOpen) _dashboard.HideAnimated();
        else ShowDashboard();
    }

    /// <summary>
    /// Ejecuta en el hilo de UI (asíncrono). Lo que siga en cola cuando se decide salir se descarta: el contexto de
    /// sincronización, a diferencia del BeginInvoke del panel, lo ejecutaría DESPUÉS de ExitApp, con el icono y el
    /// panel ya dispuestos (NullReferenceException en el log al cerrar).
    /// </summary>
    private void PostToUi(Action action)
    {
        if (_exiting) return;
        try { _ui.Post(_ => { if (!_exiting) action(); }, null); } catch { /* cerrando */ }
    }

    private volatile bool _exiting;

    private void ShowDashboard()
    {
        if (_dashboard.IsDisposed) return; // red de seguridad: un panel destruido no se puede volver a enseñar
        if (_lastSnapshot is not null)
            _dashboard.UpdateData(_lastSnapshot, _config, _plan);
        _dashboard.ShowConfigured(_config);
    }

    private Task<List<HistoryBucket>> BuildHistoryAsync(ChartRange range) => Task.Run(() =>
    {
        var records = _parser.Read(DateTime.UtcNow - UsageHistory.Lookback(range));
        return UsageHistory.Build(records, range, DateTime.UtcNow, _s.Culture);
    });

    private Task<List<PctPoint>> BuildPercentAsync(ChartRange range) => Task.Run(() =>
    {
        var now = DateTime.UtcNow;
        return _history.QueryPercent(now - UsageHistory.Lookback(range), now, 120);
    });

    /// <summary>
    /// La página Uso del notch pide los datos de un rango (una vez por rango a la vez, lo controla <see cref="UsageFeed"/>):
    /// gasto por casilla del parser y % de SQLite en segundo plano, como la gráfica del panel; vuelven al hilo de UI con
    /// <see cref="PostToUi"/>. El % va sin promediar y con la hora a la que se leyó (<see cref="UsageSeriesLoader"/>). Si algo
    /// falla se anota y el notch podrá volver a pedirlo (enseña lo que tenía).
    /// </summary>
    private async void OnNotchUsageSeries(ChartRange range)
    {
        try
        {
            var data = await UsageSeriesLoader.LoadAsync(range, BuildHistoryAsync(range), _history, () => DateTime.UtcNow)
                .ConfigureAwait(false);
            PostToUi(() => _notch?.SetUsageSeries(range, data));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"notch: datos de la gráfica de uso ({range}): {ex.Message}");
            PostToUi(() => _notch?.UsageSeriesFailed(range));
        }
    }

    // Pomodoro: lo último que pidió guardar el notch y si ya hay un guardado en cola (varios cambios seguidos, una escritura).
    private FocusState? _focusToSave;
    private bool _focusSaveQueued;
    // El pomodoro del notch que se acaba de cerrar (DestroyNotch), para el siguiente; null tras restaurarlo.
    private FocusState? _focusLive;

    // Página Uso: lo último elegido (para el notch que se rehaga) y quien lo escribe en config.json fuera del hilo de UI.
    private UsageViewState? _usageViewLast;
    private readonly BackgroundConfigWriter<UsageViewState> _usageViewWriter =
        new(AppConfig.ConfigPath, (c, v) => c.NotchUsageView = v);

    /// <summary>El notch pide recordar el modo, la ventana y el rango de la página Uso: se escribe en segundo plano (la UI no
    /// espera al disco) y SIN pasar por <see cref="ApplyNotch"/>: no cambia nada de lo que el notch es.</summary>
    private void OnNotchUsageView(UsageViewState state)
    {
        _usageViewLast = state;
        _config.NotchUsageView = state;
        _usageViewWriter.Save(state);
    }

    /// <summary>
    /// El notch pide guardar el pomodoro (cada acción, el fin de una fase, un cambio de hora): se escribe en <c>config.json</c> en
    /// el siguiente mensaje (<see cref="PostToUi"/>; varios seguidos, una sola escritura) y SIN pasar por <see cref="ApplyNotch"/>:
    /// no cambia nada de lo que el notch es. Se relee con <see cref="AppConfig.LoadOrThrow"/>: si el archivo no se puede leer
    /// ahora (antivirus, OneDrive), no se guarda encima con valores por defecto; el siguiente cambio lo vuelve a intentar.
    /// </summary>
    private void OnNotchFocusState(FocusState state)
    {
        _focusToSave = state;
        if (_focusSaveQueued) return;
        _focusSaveQueued = true;
        PostToUi(SaveFocusState);
    }

    private void SaveFocusState()
    {
        _focusSaveQueued = false;
        if (_focusToSave is not { } state) return;
        try
        {
            var c = AppConfig.LoadOrThrow(AppConfig.ConfigPath);
            c.NotchFocusState = state;
            c.SaveOrThrow(AppConfig.ConfigPath);
            _config.NotchFocusState = state;   // el resto de _config sigue siendo el que manda hasta el próximo refresco
            // Solo ahora sale de la cola (y si no llegó otro mientras tanto): si falla, sigue ahí para el siguiente intento y
            // para el notch que se rehaga.
            if (ReferenceEquals(_focusToSave, state)) _focusToSave = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"notch: no se pudo guardar el pomodoro: {ex.Message}");
        }
    }

    private void OpenBilling()
    {
        try { Process.Start(new ProcessStartInfo("https://console.anthropic.com/settings/billing") { UseShellExecute = true }); }
        catch { }
    }

    private static string AppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
    /// <summary>El repositorio público (política de privacidad, «Acerca de»). GitHub redirige el nombre viejo.</summary>
    private const string RepoUrl = Brand.RepoUrl;

    // ---------- Contador anónimo de uso (PRIVACY.md) ----------

    /// <summary>
    /// Arranca el contador: comprueba en el pool (2-5 min tras arrancar y luego cada hora) y solo guarda en el hilo de UI. Quien
    /// ya usaba la app y actualiza ve una vez un globo con el aviso, y nada sale hasta 10 min después; en una instalación nueva,
    /// nada sale antes de la bienvenida.
    /// </summary>
    private void StartUsageStats(bool packaged)
    {
        try
        {
            bool allowed = UsageStatsEnvironment.Allows(Environment.GetEnvironmentVariable, UsageStatsEnvironment.IsDebugBuild);
            var endpoint = UsageStatsClient.ResolveEndpoint(Environment.GetEnvironmentVariable(UsageStatsEnvironment.UrlVariable));
            if (endpoint is null) AppLog.Warn($"contador de uso: {UsageStatsEnvironment.UrlVariable} no válido; no se manda nada");
            _stats = new UsageStatsService(allowed && endpoint is not null ? new UsageStatsClient(endpoint) : null,
                LoadConfigForStats, () => UsageStatsFacts.Edition(_pro.State, packaged), Localization.ResolveSystemCode(),
                AppVersion, allowed, change => PostToUi(() => SaveUsageStats(change)));
            _stats.Start(_config.UsageStatsEnabled);

            if (UsagePing.NeedsUpgradeNotice(_config.UsageStatsEnabled, _stats.Active, _config.OnboardingPending, _config.UsageStats))
            {
                // Unos segundos después de arrancar: que no lo tape el globo de «actualizado a la versión…».
                _statsNoticeTimer = new System.Windows.Forms.Timer { Interval = 8000 };
                _statsNoticeTimer.Tick += (_, _) =>
                {
                    _statsNoticeTimer?.Stop();
                    _statsNoticeTimer?.Dispose();
                    _statsNoticeTimer = null;
                    ShowUsageStatsNotice();
                };
                _statsNoticeTimer.Start();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("no se pudo iniciar el contador de uso", ex);
        }
    }

    /// <summary>config.json tal cual, sin escribir nada (desde el pool); null si ahora no se puede leer.</summary>
    private static AppConfig? LoadConfigForStats()
    {
        try { return AppConfig.LoadOrThrow(AppConfig.ConfigPath); }
        catch { return null; }
    }

    /// <summary>Guarda lo que el contador ha aceptado (hilo de UI), sin rehacer el notch ni pedir cuota.</summary>
    private void SaveUsageStats(Action<AppConfig> change)
    {
        var c = AppConfig.Load();
        change(c);
        c.Save();
        _config = c;
    }

    /// <summary>El globo, una sola vez, para quien actualiza: queda visto y el primer aviso espera 10 min.</summary>
    private void ShowUsageStatsNotice()
    {
        if (_exiting) return;
        var cfg = AppConfig.Load();
        if (!UsagePing.NeedsUpgradeNotice(cfg.UsageStatsEnabled, _stats?.Active == true, cfg.OnboardingPending, cfg.UsageStats)) return;
        try { _tray.ShowBalloonTip(10000, Brand.Name, _s.UsageStatsNotice, ToolTipIcon.Info); }
        catch { /* cerrando */ }
        SaveUsageStats(c => c.UsageStats = UsagePing.NoticeShown(c.UsageStats, DateTime.UtcNow));
        _stats?.Kick(UsagePing.NoticeDelay + TimeSpan.FromMinutes(1));
    }

    /// <summary>La política de privacidad (en español con la interfaz en español; si no, en inglés).</summary>
    private void OpenPrivacyPolicy()
    {
        string file = CurrentLangCode() == "es" ? "PRIVACY.es.md" : "PRIVACY.md";
        try { Process.Start(new ProcessStartInfo($"{RepoUrl}/blob/main/{file}") { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn($"no se pudo abrir la política de privacidad: {ex.Message}"); }
    }

    private void ShowAbout()
    {
        string msg = $"{Brand.Name}\nv{AppVersion}\n\n{_s.AboutProduct}\n{_s.AboutDisclaimer}\n\n{RepoUrl}";
        MessageBox.Show(msg, $"{_s.About} — {Brand.Name}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenConfig()
    {
        _config.Save();
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppConfig.ConfigPath}\"") { UseShellExecute = true }); }
        catch { }
    }

    private void OpenProjects()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_parser.ProjectsRoot}\"") { UseShellExecute = true }); }
        catch { }
    }

    /// <summary>
    /// Edición de la Store: Windows cierra la sesión, se apaga o la Store actualiza la app con ella abierta (Restart Manager);
    /// llega en el hilo de UI, dentro de <c>WM_ENDSESSION</c> (<see cref="SessionEndWindow"/>). Se cierra con normalidad, como
    /// desde el menú, <b>antes de volver</b>: en cuanto el aviso termina, Windows puede matar el proceso, y un cierre encolado
    /// (<see cref="PostToUi"/>) no llegaría a ejecutarse (ni guardar, ni quitar el icono de la bandeja).
    /// </summary>
    private void OnSessionEnded(string reason)
    {
        if (_exiting) return;
        AppLog.Info($"fin de sesión ({reason}): se cierra la app");
        ExitApp();
    }

    /// <summary>Edición de la Store: la tarea de inicio se ha leído o ha cambiado (desde el pool): Ajustes lo repinta.</summary>
    private void OnStartupTaskChanged() => PostToUi(() => _dashboard.OnStartupChanged());

    // La espera máxima a que acabe un guardado en segundo plano al salir (dentro de WM_ENDSESSION Windows da ~5 s en total).
    private static readonly TimeSpan ExitSaveWait = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Cierre ordenado. Una sola vez: el menú, Velopack y el fin de sesión pueden pedirlo seguidos, y una segunda pasada
    /// dispondría lo ya dispuesto. Antes de nada, lo que estaba en cola para guardar (el pomodoro y la página Uso), porque
    /// <see cref="PostToUi"/> descarta lo encolado en cuanto se sale.
    /// </summary>
    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        FlushPendingSaves();
        _sessionEnd?.Dispose();
        _sessionEnd = null;
        StoreStartupTask.Changed -= OnStartupTaskChanged;
        _timer.Stop();
        if (_updates is not null) _updates.Changed -= OnUpdatesChanged;
        _updates?.Dispose();   // con una versión ya descargada, Update.exe la aplica en cuanto salga la app
        if (_pro is not null) _pro.Changed -= OnProChanged;
        _pro?.Dispose();
        _stats?.Dispose();
        _statsNoticeTimer?.Dispose();
        _snapWatcher?.Dispose();
        _snapDebounce?.Dispose();
        StopRegistryWatcher();
        _regDebounce?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _iconCache.Dispose();
        _dashboard.Dispose();
        _showSignal.Dispose();
        _pipe?.Dispose();
        StopPermissionPipe();
        CloseOnboarding();
        CloseTutorial();
        DestroyNotch();
        ExitThread();
    }

    /// <summary>El pomodoro encolado (<see cref="OnNotchFocusState"/>) se escribe ya y se espera, con plazo, a la página Uso.</summary>
    private void FlushPendingSaves()
    {
        try { if (_focusToSave is not null) SaveFocusState(); }
        catch (Exception ex) { AppLog.Warn($"al salir: no se pudo guardar el pomodoro: {ex.Message}"); }
        try
        {
            if (!_usageViewWriter.WhenIdle().Wait(ExitSaveWait)) AppLog.Warn("al salir: la página Uso no acabó de guardarse a tiempo");
        }
        catch (Exception ex) { AppLog.Warn($"al salir: {ex.GetBaseException().Message}"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exiting = true;
            _sessionEnd?.Dispose();
            _sessionEnd = null;
            StoreStartupTask.Changed -= OnStartupTaskChanged;
            _timer.Dispose();
            if (_updates is not null) _updates.Changed -= OnUpdatesChanged;
            _updates?.Dispose();
            if (_pro is not null) _pro.Changed -= OnProChanged;
            _pro?.Dispose();
            _stats?.Dispose();
            _statsNoticeTimer?.Dispose();
            _snapWatcher?.Dispose();
            _snapDebounce?.Dispose();
            StopRegistryWatcher();
            _regDebounce?.Dispose();
            _tray.Dispose();
            _iconCache.Dispose();
            _dashboard.Dispose();
            _showSignal.Dispose();
            _pipe?.Dispose();
            StopPermissionPipe();
            CloseOnboarding();
            CloseTutorial();
            DestroyNotch();
        }
        base.Dispose(disposing);
    }
}
