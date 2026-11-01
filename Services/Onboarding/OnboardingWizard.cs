using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services.Onboarding;

/// <summary>Páginas del asistente de bienvenida, en el orden lógico del instalador (docs/instalador-perfiles.md).</summary>
public enum OnboardingPage { Welcome, Profile, Features, Settings, ClaudeCode, Summary }

/// <summary>Lo que tapa la página: la confirmación de «Saltar» (Esc) o el aviso de que no se pudo aplicar.</summary>
public enum OnboardingOverlay { None, ConfirmSkip, Error }

/// <summary>Por qué se recomienda abrir el notch como se recomienda (la línea bajo «Abrir el notch»).</summary>
public enum OpenReason { Hover, MonitorAbove, SmallScreen }

/// <summary>
/// El equipo y la config de partida del asistente. <paramref name="Fresh"/>: primera vez (<see cref="AppConfig.OnboardingPending"/>):
/// los ajustes salen con lo recomendado y Personalizado parte del perfil recomendado; si no (desde la bandeja, «Volver a
/// configurar…»), con los valores actuales y el perfil que coincida con la config. <paramref name="ClaudeDir"/>: la carpeta
/// de Claude Code, solo para enseñarla. <paramref name="Pro"/>: el estado del Pro y la edición, para marcar lo Pro y la nota
/// de la prueba (solo en la Store); null = sin nota.
/// </summary>
public sealed record OnboardingContext(SetupMachine Machine, AppConfig Current, bool HooksInstalled, bool? StartupEnabled,
    bool Fresh, string ClaudeDir = "", Licensing.ProSummary? Pro = null);

/// <summary>
/// Estado del asistente de bienvenida del notch. PURO (sin Win32 ni reloj): páginas visibles, navegación, lo marcado en
/// cada página y las reglas del instalador (installer/ClaudeBarWin.iss, su <c>[Code]</c>), y al final
/// <see cref="Answers"/> → <see cref="SetupAnswers"/>. La ventana lo pinta y le pasa los clics y las teclas.
///
/// <list type="bullet">
/// <item>Al dejar la página de perfiles, «Qué se activa» y las sesiones en vivo se rellenan con el perfil elegido, si cambió
/// desde la última vez (lo tocado a mano con el mismo perfil se respeta). Vale también saltando con los puntos.</item>
/// <item>«Claude Code» solo se ve si el perfil usa las sesiones en vivo y Claude Code está instalado. Las sesiones en vivo
/// vienen marcadas solo con «Programo con Claude Code»; ya instaladas, marcadas y sin poder tocarse. Los permisos desde el
/// notch, opt-in (lo de la config) y solo con el notch y las sesiones en vivo.</item>
/// <item>Primera vez: mientras no se toque, «Abrir el notch» sigue a la recomendación del monitor elegido.</item>
/// <item>Los puntos llevan a cualquier página ya vista; «Siguiente» en el resumen es «Empezar».</item>
/// </list>
/// </summary>
public sealed class OnboardingWizard
{
    /// <summary>Códigos de idioma en el orden del selector: «system» (como Windows) y los de la app.</summary>
    public static IReadOnlyList<string> LanguageCodes => Localization.LanguageCycle;

    /// <summary>Tamaños del selector (0 = Auto).</summary>
    public static IReadOnlyList<int> SizeOptions => AppConfig.NotchSizeOptions;

    private readonly Dictionary<SetupFeature, bool> _features = new();
    private SetupProfile? _prefilled;
    private int _furthest;
    // «Volver a configurar…»: idioma y monitor sin tocar se aplican con el valor de la config, no con el del selector (un
    // monitor desconectado sale como «el principal» y un idioma que no está en la lista, como «system»).
    private bool _languageTouched, _monitorTouched;

    public OnboardingWizard(OnboardingContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Recommended = SetupRecommendations.Profile(context.Machine);
        Profile = context.Fresh ? Recommended : SetupProfiles.Matching(context.Current);
        // Las casillas ya rellenas con el perfil de partida: si se salta la página de perfiles con los puntos, no salen vacías.
        Prefill(Profile);
        PermissionsInit = context.Current.NotchPermissions;
        Permissions = PermissionsInit;
        UsageStats = context.Current.UsageStatsEnabled;
        // La primera vez, el tutorial al terminar viene marcado (se puede desmarcar); al volver a configurar, no.
        ShowTutorial = context.Fresh;
        InitSettings();
    }

    /// <summary>«Ver el tutorial al terminar (2 min)», en el resumen: no aplica nada, solo abre el tutorial tras el relevo.</summary>
    public bool ShowTutorial { get; private set; }

    public void ToggleTutorial() => ShowTutorial = !ShowTutorial;

    /// <summary>El contador anónimo de uso (la línea de la bienvenida): como esté en la config (activado por defecto).</summary>
    public bool UsageStats { get; private set; }

    public void ToggleUsageStats() => UsageStats = !UsageStats;

    public OnboardingContext Context { get; }

    /// <summary>El perfil recomendado para este equipo (con Claude Code, Programo; sin él, Solo cuota).</summary>
    public SetupProfile Recommended { get; }

    public OnboardingPage Page { get; private set; } = OnboardingPage.Welcome;
    public OnboardingOverlay Overlay { get; private set; }

    /// <summary>Texto del aviso de error (lo que no se pudo aplicar); "" sin aviso.</summary>
    public string Error { get; private set; } = "";

    public SetupProfile Profile { get; private set; }

    // ---- ajustes recomendados
    public string Language { get; private set; } = "system";
    /// <summary>0 = el principal; i + 1 = <c>Machine.Monitors[i]</c>.</summary>
    public int MonitorIndex { get; private set; }
    public int SizePercent { get; private set; }
    public bool OpenOnClick { get; private set; }
    public bool OpenTouched { get; private set; }
    public bool StartWithWindows { get; private set; }

    // ---- Claude Code
    public bool LiveSessions { get; private set; }
    public bool Permissions { get; private set; }
    public bool PermissionsInit { get; }

    /// <summary>Páginas que se ven ahora (Claude Code depende del perfil y del equipo).</summary>
    public IReadOnlyList<OnboardingPage> Pages
    {
        get
        {
            var pages = new List<OnboardingPage>(6)
                { OnboardingPage.Welcome, OnboardingPage.Profile, OnboardingPage.Features, OnboardingPage.Settings };
            if (OfferLiveSessions) pages.Add(OnboardingPage.ClaudeCode);
            pages.Add(OnboardingPage.Summary);
            return pages;
        }
    }

    /// <summary>Posición de <see cref="Page"/> en <see cref="Pages"/> (los puntos).</summary>
    public int PageIndex => Math.Max(0, IndexOf(Page));

    /// <summary>La página más avanzada a la que se ha llegado (los puntos solo llevan hasta ella).</summary>
    public int Furthest => Math.Min(_furthest, Pages.Count - 1);

    public bool CanGoBack => Overlay == OnboardingOverlay.None && PageIndex > 0;
    public bool IsLastPage => Page == OnboardingPage.Summary;

    /// <summary>¿Se ofrecen las sesiones en vivo? (perfil que las usa y Claude Code instalado).</summary>
    public bool OfferLiveSessions => SetupRecommendations.OfferLiveSessions(Profile, Context.Machine);

    /// <summary>¿Hay que preguntar el monitor? (2+ monitores, como el menú de la bandeja).</summary>
    public bool AskMonitor => SetupRecommendations.AskMonitor(Context.Machine);

    /// <summary>«Saltar» en la primera vez aplica el perfil recomendado; desde la bandeja, cierra sin cambiar nada.</summary>
    public bool SkipApplies => Context.Fresh;

    // ---------------------------------------------------------------- navegación

    /// <summary>Siguiente página. En el resumen devuelve true: hay que terminar («Empezar»).</summary>
    public bool Next()
    {
        if (Overlay != OnboardingOverlay.None) return false;
        if (IsLastPage) return true;
        GoTo(PageIndex + 1, force: true);
        return false;
    }

    public void Back()
    {
        if (!CanGoBack) return;
        GoTo(PageIndex - 1, force: true);
    }

    /// <summary>Clic en un punto: solo hasta la página más avanzada vista. False si no se movió.</summary>
    public bool GoTo(int index) => GoTo(index, force: false);

    private bool GoTo(int index, bool force)
    {
        if (Overlay != OnboardingOverlay.None) return false;
        var pages = Pages;
        if (index < 0 || index >= pages.Count || index == PageIndex) return false;
        if (!force && index > Furthest) return false;
        if (Page == OnboardingPage.Profile) LeaveProfile();
        // Al dejar la página de perfiles la lista puede cambiar (Claude Code aparece o se va): se vuelve a mirar.
        pages = Pages;
        index = Math.Min(index, pages.Count - 1);
        Page = pages[index];
        _furthest = Math.Max(_furthest, index);
        return true;
    }

    private int IndexOf(OnboardingPage page)
    {
        var pages = Pages;
        for (int i = 0; i < pages.Count; i++) if (pages[i] == page) return i;
        return -1;
    }

    /// <summary>Al dejar la página de perfiles: casillas y sesiones en vivo con el perfil elegido, si cambió.</summary>
    private void LeaveProfile()
    {
        if (_prefilled == Profile) return;
        Prefill(Profile);
        PrefillLiveSessions(Profile);
    }

    // ---------------------------------------------------------------- perfiles

    public void SelectProfile(SetupProfile profile)
    {
        if (!Enum.IsDefined(profile)) return;
        Profile = profile;
    }

    /// <summary>Perfil anterior / siguiente (flechas del teclado en las tarjetas).</summary>
    public void StepProfile(int delta)
    {
        var all = SetupProfiles.All;
        int i = Math.Clamp(all.ToList().IndexOf(Profile) + Math.Sign(delta), 0, all.Count - 1);
        Profile = all[i];
    }

    // ---------------------------------------------------------------- qué se activa

    public bool FeatureOn(SetupFeature f) => _features.TryGetValue(f, out var v) && v;

    public bool FeatureEnabled(SetupFeature f) => SetupFeatures.Enabled(f, FeatureOn);

    public void ToggleFeature(SetupFeature f)
    {
        if (!FeatureEnabled(f)) return;
        _features[f] = !FeatureOn(f);
    }

    /// <summary>«Restablecer recomendados» de «Qué se activa»: otra vez los valores del perfil elegido.</summary>
    public void ResetFeatures() => Prefill(Profile);

    /// <summary>El perfil cuyo nombre sale en «Marcado con los valores de «%1»».</summary>
    public SetupProfile FeaturesProfile => _prefilled ?? Profile;

    private void Prefill(SetupProfile profile)
    {
        foreach (var (f, v) in SetupFeatures.Prefill(profile, Context.Current, Context.Fresh, Recommended)) _features[f] = v;
        _prefilled = profile;
    }

    private void PrefillLiveSessions(SetupProfile profile)
    {
        if (Context.HooksInstalled) return;
        LiveSessions = profile == SetupProfile.Coder;
    }

    // ---------------------------------------------------------------- ajustes recomendados

    private void InitSettings()
    {
        var c = Context.Current;
        if (Context.Fresh)
        {
            Language = "system";
            MonitorIndex = 0;
            SizePercent = 0;
            OpenOnClick = SetupRecommendations.OpenOnClick(Context.Machine, "");
            StartWithWindows = SetupRecommendations.StartWithWindows;
        }
        else
        {
            Language = LanguageCodes.Contains(c.Language) ? c.Language : "system";
            MonitorIndex = MonitorIndexOf(c.NotchMonitor);
            SizePercent = SizeOptions.Contains(c.NotchSizePercent) ? c.NotchSizePercent : 0;
            OpenOnClick = c.NotchOpenOnClick;
            // Desconocido (edición de la Store, la tarea de inicio aún no se ha leído): sale apagado, pero sin tocarlo no se
            // aplica (StartWithWindowsToApply): un «apagado» por no saber apagaría una tarea que estaba encendida.
            StartWithWindows = Context.StartupEnabled ?? false;
        }
        // Las sesiones en vivo de partida: ya instaladas, puestas; si no, las del perfil de partida.
        LiveSessions = Context.HooksInstalled || Profile == SetupProfile.Coder;
    }

    private int MonitorIndexOf(string? device)
    {
        if (string.IsNullOrEmpty(device)) return 0;
        var ms = Context.Machine.Monitors;
        for (int i = 0; i < ms.Count; i++)
            if (string.Equals(ms[i].DeviceName, device, StringComparison.OrdinalIgnoreCase)) return i + 1;
        return 0;
    }

    /// <summary>Nombre GDI del monitor elegido; "" = seguir al principal.</summary>
    public string MonitorDevice => MonitorIndex <= 0 || MonitorIndex > Context.Machine.Monitors.Count ? ""
        : Context.Machine.Monitors[MonitorIndex - 1].DeviceName;

    /// <summary>El monitor donde irá el notch (el elegido o el principal); null sin monitores.</summary>
    public SetupMonitor? NotchMonitor => SetupRecommendations.NotchMonitor(Context.Machine, MonitorDevice);

    /// <summary>Qué será «Auto» en el monitor elegido (100, 115 o 130 %).</summary>
    public int AutoPercent => NotchMonitor is { } m ? SetupRecommendations.AutoSizePercent(m) : 100;

    /// <summary>Ancho lógico del monitor elegido (la explicación de «Auto»).</summary>
    public int LogicalWidth => NotchMonitor?.LogicalWidth ?? 0;

    /// <summary>¿Se recomienda abrir con un clic en el monitor elegido?</summary>
    public bool RecommendOpenOnClick => SetupRecommendations.OpenOnClick(Context.Machine, MonitorDevice);

    public OpenReason OpenReason => NotchMonitor is not { } m ? OpenReason.Hover
        : SetupRecommendations.HasMonitorAbove(Context.Machine, m) ? OpenReason.MonitorAbove
        : m.LogicalWidth < SetupRecommendations.SmallScreenLogicalWidth ? OpenReason.SmallScreen
        : OpenReason.Hover;

    /// <summary>Monitor, tamaño y apertura solo cuentan con el notch encendido.</summary>
    public bool NotchSettingsEnabled => FeatureOn(SetupFeature.Notch);

    public void StepLanguage(int delta)
    {
        int i = Math.Max(0, IndexOfCode(Language));
        Language = LanguageCodes[Mod(i + Math.Sign(delta), LanguageCodes.Count)];
        _languageTouched = true;
    }

    private static int IndexOfCode(string code)
    {
        for (int i = 0; i < LanguageCodes.Count; i++) if (LanguageCodes[i] == code) return i;
        return -1;
    }

    public void StepMonitor(int delta)
    {
        if (!NotchSettingsEnabled || !AskMonitor) return;
        MonitorIndex = Mod(MonitorIndex + Math.Sign(delta), Context.Machine.Monitors.Count + 1);
        _monitorTouched = true;
        FollowOpenRecommendation();
    }

    public void SetSize(int percent)
    {
        if (!NotchSettingsEnabled || !SizeOptions.Contains(percent)) return;
        SizePercent = percent;
    }

    public void StepSize(int delta)
    {
        var list = SizeOptions.ToList();
        int i = Math.Clamp(Math.Max(0, list.IndexOf(SizePercent)) + Math.Sign(delta), 0, list.Count - 1);
        SetSize(list[i]);
    }

    public void SetOpenOnClick(bool click)
    {
        if (!NotchSettingsEnabled) return;
        OpenOnClick = click;
        OpenTouched = true;
    }

    public void ToggleStartup()
    {
        StartWithWindows = !StartWithWindows;
        _startupTouched = true;
    }

    private bool _startupTouched;

    /// <summary>Desde la bandeja, sin saber cómo está «Iniciar con Windows» y sin haber tocado el interruptor: no se aplica.</summary>
    public bool StartupUnknown => !Context.Fresh && Context.StartupEnabled is null && !_startupTouched;

    /// <summary>Lo que se aplica de «Iniciar con Windows»: lo elegido, o null (dejarlo como esté) si el estado es desconocido y
    /// no se tocó.</summary>
    private bool? StartWithWindowsToApply => StartupUnknown ? null : StartWithWindows;

    /// <summary>«Restablecer recomendados» de los ajustes: como Windows, el principal, Auto, la apertura recomendada e
    /// iniciar con Windows.</summary>
    public void ResetSettings()
    {
        OpenTouched = false;
        Language = "system";
        MonitorIndex = 0;
        _languageTouched = _monitorTouched = true;   // elegido a propósito: se aplica tal cual
        SizePercent = 0;
        OpenOnClick = SetupRecommendations.OpenOnClick(Context.Machine, "");
        StartWithWindows = SetupRecommendations.StartWithWindows;
        _startupTouched = true;   // elegido a propósito: se aplica tal cual
    }

    /// <summary>Primera vez: mientras no se toque, la apertura sigue a la recomendación del monitor elegido.</summary>
    private void FollowOpenRecommendation()
    {
        if (Context.Fresh && !OpenTouched) OpenOnClick = RecommendOpenOnClick;
    }

    // ---------------------------------------------------------------- Claude Code

    /// <summary>¿Quedarán puestas las sesiones en vivo? (ya instaladas, o marcadas para instalarlas).</summary>
    public bool LiveSessionsOn => Context.HooksInstalled || LiveSessions;

    /// <summary>La casilla de las sesiones en vivo se puede tocar (si ya están instaladas, no: se quitan desde la bandeja).</summary>
    public bool LiveSessionsEnabled => !Context.HooksInstalled;

    /// <summary>Los permisos desde el notch necesitan el notch y las sesiones en vivo (se mira al momento: desde aquí se
    /// puede volver atrás y apagar el notch).</summary>
    public bool PermissionsAvailable => LiveSessionsOn && FeatureOn(SetupFeature.Notch);

    public bool PermissionsValue => PermissionsAvailable && Permissions;

    public void ToggleLiveSessions()
    {
        if (LiveSessionsEnabled) LiveSessions = !LiveSessions;
    }

    public void TogglePermissions()
    {
        if (PermissionsAvailable) Permissions = !Permissions;
    }

    // ---------------------------------------------------------------- saltar y errores

    /// <summary>Esc: pide confirmar «Saltar» dentro del propio notch (o la cierra si ya estaba a la vista).</summary>
    public void RequestSkip()
    {
        if (Overlay == OnboardingOverlay.ConfirmSkip) Overlay = OnboardingOverlay.None;
        else if (Overlay == OnboardingOverlay.None) Overlay = OnboardingOverlay.ConfirmSkip;
    }

    public void CancelOverlay()
    {
        Overlay = OnboardingOverlay.None;
        Error = "";
    }

    /// <summary>No se pudo aplicar: el aviso tapa la página (Reintentar / Cerrar).</summary>
    public void ShowError(string message)
    {
        Error = message ?? "";
        Overlay = OnboardingOverlay.Error;
    }

    // ---------------------------------------------------------------- respuestas

    /// <summary>Lo elegido, tal cual está ahora. Un perfil elegido sin dejar aún su página cuenta como al dejarla (sus
    /// casillas y sus sesiones en vivo): nunca se aplica un perfil con las casillas de otro. Desde la bandeja, el idioma y el
    /// monitor que no se tocaron van con el valor de la config (así no se pierde un monitor que ahora no está conectado).</summary>
    public SetupAnswers Answers()
    {
        LeaveProfile();
        var c = Context.Current;
        string language = !Context.Fresh && !_languageTouched ? c.Language ?? "system" : Language;
        string monitor = !Context.Fresh && !_monitorTouched ? c.NotchMonitor ?? "" : MonitorDevice;
        return new(Profile, new Dictionary<SetupFeature, bool>(_features), language, monitor, SizePercent, OpenOnClick,
            StartWithWindowsToApply, LiveSessionsOn && OfferLiveSessions, PermissionsValue, UsageStats);
    }

    /// <summary>
    /// «Saltar» en la primera vez: el perfil recomendado con sus casillas y los ajustes recomendados para el equipo. Las
    /// sesiones en vivo NO se instalan (no se ha visto su explicación y tocan el settings.json global de Claude Code): se
    /// activan luego desde la bandeja. Si ya estaban, siguen; los permisos, como estuvieran.
    /// </summary>
    public SetupAnswers SkipAnswers()
    {
        var features = SetupFeatures.Prefill(Recommended, Context.Current, Context.Fresh, Recommended);
        bool notch = features[SetupFeature.Notch];
        return new SetupAnswers(Recommended, features, "system", "", 0, SetupRecommendations.OpenOnClick(Context.Machine, ""),
            SetupRecommendations.StartWithWindows, Context.HooksInstalled,
            Context.HooksInstalled && notch && PermissionsInit, UsageStats);
    }

    private static int Mod(int a, int n) => n <= 0 ? 0 : ((a % n) + n) % n;
}
