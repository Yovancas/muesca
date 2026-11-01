namespace ClaudeBarWin.Config;

/// <summary>
/// Casillas de «Qué se activa», en el orden en que se ven (el mismo que el instalador: <c>FeatureKey</c> de
/// installer/ClaudeBarWin.iss). Cada una es un bool de <see cref="AppConfig"/> del catálogo <see cref="SetupSettings"/>.
/// </summary>
public enum SetupFeature
{
    Notch, Minimal, HideFullscreen, Sound, Music, Lyrics, CompactLyrics, Clock, Mascot, Tasks, ReduceMotion,
}

/// <summary>Las casillas del asistente: su clave en <see cref="AppConfig"/>, de qué dependen y cómo se rellenan. PURO.</summary>
public static class SetupFeatures
{
    public static readonly IReadOnlyList<SetupFeature> All = Enum.GetValues<SetupFeature>();

    /// <summary>Propiedad de <see cref="AppConfig"/> (clave de <see cref="SetupSettings"/> y de los <c>overrides</c>).</summary>
    public static string Key(SetupFeature f) => f switch
    {
        SetupFeature.Notch => nameof(AppConfig.NotchEnabled),
        SetupFeature.Minimal => nameof(AppConfig.NotchMinimal),
        SetupFeature.HideFullscreen => nameof(AppConfig.NotchHideInFullscreen),
        SetupFeature.Sound => nameof(AppConfig.NotchSound),
        SetupFeature.Music => nameof(AppConfig.NotchMusic),
        SetupFeature.Lyrics => nameof(AppConfig.NotchLyrics),
        SetupFeature.CompactLyrics => nameof(AppConfig.NotchCompactLyrics),
        SetupFeature.Clock => nameof(AppConfig.NotchClock),
        SetupFeature.Mascot => nameof(AppConfig.NotchMascot),
        SetupFeature.Tasks => nameof(AppConfig.NotchTasks),
        _ => nameof(AppConfig.ReduceMotion),
    };

    /// <summary>
    /// ¿Se puede tocar la casilla con lo que hay marcado? Sin notch, solo el notch y reducir movimiento; las letras solo con
    /// la música, y la letra pequeña con las letras (como Ajustes → Notch y <c>UpdateFeatureStates</c> del instalador).
    /// </summary>
    public static bool Enabled(SetupFeature f, Func<SetupFeature, bool> on)
    {
        ArgumentNullException.ThrowIfNull(on);
        if (f is SetupFeature.Notch or SetupFeature.ReduceMotion) return true;
        if (!on(SetupFeature.Notch)) return false;
        return f switch
        {
            SetupFeature.Lyrics => on(SetupFeature.Music),
            SetupFeature.CompactLyrics => on(SetupFeature.Music) && on(SetupFeature.Lyrics),
            _ => true,
        };
    }

    /// <summary>
    /// Casillas con los valores del perfil; lo que el perfil no fija, como está en <paramref name="current"/> (la config de
    /// ahora, que en una instalación nueva son los valores por defecto). Personalizado en una instalación nueva
    /// (<paramref name="fresh"/>) parte de <paramref name="recommended"/>. Espejo de <c>PrefillFeatures</c> del instalador.
    /// </summary>
    public static Dictionary<SetupFeature, bool> Prefill(SetupProfile profile, AppConfig current, bool fresh, SetupProfile recommended)
    {
        ArgumentNullException.ThrowIfNull(current);
        var source = profile == SetupProfile.Custom && fresh ? recommended : profile;
        var changes = SetupProfiles.Changes(source);
        var values = new Dictionary<SetupFeature, bool>();
        foreach (var f in All)
            values[f] = changes.TryGetValue(Key(f), out var v) ? (bool)v : (bool)SetupSettings.Get(current, Key(f));
        return values;
    }
}

/// <summary>
/// Lo elegido en el asistente de bienvenida del notch, en memoria (el instalador lo manda como JSON a
/// <c>--apply-setup</c>; aquí no hace falta pasar por disco). <see cref="ToRequest"/> lo convierte en el mismo
/// <see cref="SetupRequest"/> que lee <see cref="SetupApply"/>: viaja todo lo que se ha visto, como en una instalación nueva
/// del instalador (con una config previa, lo que no cambia no cuenta como cambio). PURO.
/// </summary>
/// <param name="Language">"system" o un código de <c>Localization.Languages</c>.</param>
/// <param name="NotchMonitor">Nombre GDI del monitor del notch; "" = seguir al principal.</param>
/// <param name="NotchSizePercent">0 = Auto, o uno de <see cref="AppConfig.NotchSizeOptions"/>.</param>
/// <param name="StartWithWindows">Encender o apagar «Iniciar con Windows»; null = dejarlo como esté (desde la bandeja, sin
/// saber cómo estaba y sin tocar el interruptor).</param>
/// <param name="LiveSessions">Instalar los hooks (solo cuenta si el perfil los usa y hay Claude Code).</param>
/// <param name="NotchPermissions">Contestar los permisos desde el notch (solo cuenta con el notch y las sesiones en vivo).</param>
/// <param name="UsageStats">El interruptor del contador anónimo de uso de la bienvenida; null = no se tocó (el instalador). No
/// viaja en <see cref="ToRequest"/>: lo guarda <c>SetupEnvironment.ApplyOnboarding</c> junto con que el aviso ya se vio.</param>
public sealed record SetupAnswers(
    SetupProfile Profile,
    IReadOnlyDictionary<SetupFeature, bool> Features,
    string Language,
    string NotchMonitor,
    int NotchSizePercent,
    bool NotchOpenOnClick,
    bool? StartWithWindows,
    bool LiveSessions,
    bool NotchPermissions,
    bool? UsageStats = null)
{
    /// <summary>¿Está marcada la casilla? (una que falte cuenta como apagada).</summary>
    public bool On(SetupFeature f) => Features.TryGetValue(f, out var v) && v;

    /// <summary>
    /// La petición para <see cref="SetupApply"/>: perfil, casillas, idioma, monitor, tamaño y apertura como <c>overrides</c>;
    /// los permisos solo si se ofrecen las sesiones en vivo (como el instalador); y las sesiones en vivo solo si se ofrecen.
    /// Lo que no valga para <see cref="SetupSettings"/> se aparta en <see cref="SetupRequest.Ignored"/>, igual que con el JSON.
    /// </summary>
    public SetupRequest ToRequest(SetupMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        bool offer = SetupRecommendations.OfferLiveSessions(Profile, machine);
        var overrides = new Dictionary<string, object>(StringComparer.Ordinal);
        var ignored = new List<string>();
        void Add(string key, object value)
        {
            if (SetupSettings.IsValid(key, value)) overrides[key] = value;
            else ignored.Add($"{key}: valor no válido ({value})");
        }

        foreach (var f in SetupFeatures.All) Add(SetupFeatures.Key(f), On(f));
        Add(nameof(AppConfig.Language), Language ?? "system");
        Add(nameof(AppConfig.NotchMonitor), NotchMonitor ?? "");
        Add(nameof(AppConfig.NotchSizePercent), NotchSizePercent);
        Add(nameof(AppConfig.NotchOpenOnClick), NotchOpenOnClick);
        if (offer) Add(nameof(AppConfig.NotchPermissions), NotchPermissions && LiveSessions && On(SetupFeature.Notch));
        return new SetupRequest(Profile, overrides, offer && LiveSessions, StartWithWindows, ignored);
    }
}
