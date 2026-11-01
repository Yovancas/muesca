using System.Drawing;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Config;

/// <summary>
/// Perfiles del asistente de instalación (installer/ClaudeBarWin.iss, página «¿Para qué lo vas a usar?»). Cada perfil es un
/// conjunto de cambios sobre <see cref="AppConfig"/> (<see cref="SetupProfiles.Changes"/>): lo que no nombra no lo toca.
/// La tabla completa está en docs/instalador-perfiles.md.
/// </summary>
public enum SetupProfile
{
    /// <summary>«Solo cuota»: icono de bandeja y panel, sin notch.</summary>
    QuotaOnly,
    /// <summary>«Programo con Claude Code» (el recomendado si Claude Code está instalado): notch con sesiones en vivo y
    /// avisos, música y letras.</summary>
    Coder,
    /// <summary>«Notch completo»: lo anterior más la letra en el notch pequeño, el reloj, el gato y las tareas.</summary>
    FullNotch,
    /// <summary>«Minimalista / juego»: raya en reposo, oculto en pantalla completa, sin sonidos, sin animaciones ni letras.</summary>
    Minimal,
    /// <summary>«Personalizado»: no cambia nada por sí mismo; el asistente manda lo que se marque como <c>overrides</c>.</summary>
    Custom,
}

/// <summary>Los perfiles del asistente: su identificador en el JSON y lo que cambia cada uno. PURO.</summary>
public static class SetupProfiles
{
    public static readonly IReadOnlyList<SetupProfile> All = Enum.GetValues<SetupProfile>();

    /// <summary>Identificador estable en el JSON del asistente (y el que el instalador recuerda para la próxima vez).</summary>
    public static string Id(SetupProfile profile) => profile switch
    {
        SetupProfile.QuotaOnly => "quota",
        SetupProfile.Coder => "coder",
        SetupProfile.FullNotch => "full",
        SetupProfile.Minimal => "minimal",
        _ => "custom",
    };

    /// <summary>Perfil de un identificador (sin distinguir mayúsculas). Desconocido o vacío ⇒ false.</summary>
    public static bool TryParse(string? id, out SetupProfile profile)
    {
        foreach (var p in All)
        {
            if (!string.Equals(Id(p), id?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            profile = p;
            return true;
        }
        profile = SetupProfile.Custom;
        return false;
    }

    /// <summary>
    /// ¿El perfil usa las sesiones en vivo (hooks de Claude Code)? Solo entonces el asistente las ofrece y
    /// <see cref="SetupApply"/> las instala. Personalizado las ofrece (decide el usuario); Solo cuota y Minimalista no.
    /// </summary>
    public static bool WantsLiveSessions(SetupProfile profile) =>
        profile is SetupProfile.Coder or SetupProfile.FullNotch or SetupProfile.Custom;

    /// <summary>Cambios del perfil (clave = propiedad de <see cref="AppConfig"/> del catálogo <see cref="SetupSettings"/>).</summary>
    public static IReadOnlyDictionary<string, object> Changes(SetupProfile profile) => profile switch
    {
        SetupProfile.QuotaOnly => QuotaOnlyChanges,
        SetupProfile.Coder => CoderChanges,
        SetupProfile.FullNotch => FullNotchChanges,
        SetupProfile.Minimal => MinimalChanges,
        _ => NoChanges,
    };

    /// <summary>
    /// El perfil con el que coincide <paramref name="config"/> (todo lo que el perfil fija tiene ese valor), o
    /// <see cref="SetupProfile.Custom"/> si ninguno: es el que el asistente del notch preselecciona al volver a configurar
    /// (el instalador recuerda el último elegido; la app no lo guarda). Notch completo antes que Programo: cumple lo de los
    /// dos y es el más concreto.
    /// </summary>
    public static SetupProfile Matching(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        foreach (var p in new[] { SetupProfile.FullNotch, SetupProfile.Coder, SetupProfile.Minimal, SetupProfile.QuotaOnly })
            if (Changes(p).All(kv => Equals(SetupSettings.Get(config, kv.Key), kv.Value))) return p;
        return SetupProfile.Custom;
    }

    private static readonly IReadOnlyDictionary<string, object> NoChanges = new Dictionary<string, object>();

    // Solo cuota: el notch fuera; el panel y la bandeja como estén.
    private static readonly IReadOnlyDictionary<string, object> QuotaOnlyChanges = new Dictionary<string, object>
    {
        [nameof(AppConfig.NotchEnabled)] = false,
    };

    // Programo con Claude Code: la pastilla (no la raya) con las sesiones y la campanita; música y letras. Sin la letra
    // compacta, el reloj ni el gato: son lo que añade el notch completo. Los permisos desde el notch NO van aquí: son
    // opt-in y se marcan aparte en la página de Claude Code. Reducir movimiento tampoco: es de accesibilidad y no se pisa.
    private static readonly IReadOnlyDictionary<string, object> CoderChanges = new Dictionary<string, object>
    {
        [nameof(AppConfig.NotchEnabled)] = true,
        [nameof(AppConfig.NotchMinimal)] = false,
        [nameof(AppConfig.NotchSound)] = true,
        [nameof(AppConfig.NotchMusic)] = true,
        [nameof(AppConfig.NotchLyrics)] = true,
        [nameof(AppConfig.NotchCompactLyrics)] = false,
        [nameof(AppConfig.NotchClock)] = false,
        [nameof(AppConfig.NotchMascot)] = false,
    };

    // Notch completo: lo de programar más la letra en el notch pequeño (con carátula y anillo), el reloj, el gato y las
    // tareas. La bandeja de archivos va siempre con el notch (no tiene interruptor).
    private static readonly IReadOnlyDictionary<string, object> FullNotchChanges = new Dictionary<string, object>(CoderChanges)
    {
        [nameof(AppConfig.NotchCompactLyrics)] = true,
        [nameof(AppConfig.NotchCompactLyricsArt)] = true,
        [nameof(AppConfig.NotchCompactLyricsQuota)] = true,
        [nameof(AppConfig.NotchClock)] = true,
        [nameof(AppConfig.NotchMascot)] = true,
        [nameof(AppConfig.NotchTasks)] = true,
    };

    // Minimalista / juego: raya en reposo, fuera en pantalla completa, sin campanita, sin animaciones (reducir
    // movimiento), sin letras y sin los extras del reposo (reloj y gato). La música y las tareas se quedan como estén.
    private static readonly IReadOnlyDictionary<string, object> MinimalChanges = new Dictionary<string, object>
    {
        [nameof(AppConfig.NotchEnabled)] = true,
        [nameof(AppConfig.NotchMinimal)] = true,
        [nameof(AppConfig.NotchHideInFullscreen)] = true,
        [nameof(AppConfig.NotchSound)] = false,
        [nameof(AppConfig.ReduceMotion)] = true,
        [nameof(AppConfig.NotchLyrics)] = false,
        [nameof(AppConfig.NotchCompactLyrics)] = false,
        [nameof(AppConfig.NotchClock)] = false,
        [nameof(AppConfig.NotchMascot)] = false,
    };
}

/// <summary>
/// Los ajustes de <see cref="AppConfig"/> que el asistente puede tocar (lista blanca): tipo, lectura, escritura y qué
/// valores valen. La clave es el nombre de la propiedad. Lo demás (umbrales, tema, posición del panel, la clave de
/// Musixmatch…) no se toca nunca desde el instalador, y <see cref="AppConfig.LiveSessionsEnabled"/> tampoco: va con los
/// hooks (<see cref="SetupApply"/>). PURO.
/// </summary>
public static class SetupSettings
{
    private sealed record Setting(Type Type, Func<AppConfig, object> Get, Action<AppConfig, object> Set, Func<object, bool> IsValid);

    private static Setting Bool(Func<AppConfig, bool> get, Action<AppConfig, bool> set) =>
        new(typeof(bool), c => get(c), (c, v) => set(c, (bool)v), _ => true);

    private static Setting Int(Func<AppConfig, int> get, Action<AppConfig, int> set, Func<int, bool> isValid) =>
        new(typeof(int), c => get(c), (c, v) => set(c, (int)v), v => isValid((int)v));

    private static Setting Str(Func<AppConfig, string> get, Action<AppConfig, string> set, Func<string, bool> isValid) =>
        new(typeof(string), c => get(c) ?? "", (c, v) => set(c, (string)v), v => isValid((string)v));

    private static readonly Dictionary<string, Setting> Catalog = new(StringComparer.Ordinal)
    {
        [nameof(AppConfig.Language)] = Str(c => c.Language, (c, v) => c.Language = v, IsLanguage),
        [nameof(AppConfig.ReduceMotion)] = Bool(c => c.ReduceMotion, (c, v) => c.ReduceMotion = v),
        [nameof(AppConfig.NotchEnabled)] = Bool(c => c.NotchEnabled, (c, v) => c.NotchEnabled = v),
        [nameof(AppConfig.NotchOpenOnClick)] = Bool(c => c.NotchOpenOnClick, (c, v) => c.NotchOpenOnClick = v),
        [nameof(AppConfig.NotchMinimal)] = Bool(c => c.NotchMinimal, (c, v) => c.NotchMinimal = v),
        [nameof(AppConfig.NotchHideInFullscreen)] = Bool(c => c.NotchHideInFullscreen, (c, v) => c.NotchHideInFullscreen = v),
        [nameof(AppConfig.NotchSound)] = Bool(c => c.NotchSound, (c, v) => c.NotchSound = v),
        [nameof(AppConfig.NotchPermissions)] = Bool(c => c.NotchPermissions, (c, v) => c.NotchPermissions = v),
        [nameof(AppConfig.NotchSizePercent)] = Int(c => c.NotchSizePercent, (c, v) => c.NotchSizePercent = v,
            v => Array.IndexOf(AppConfig.NotchSizeOptions, v) >= 0),
        [nameof(AppConfig.NotchMonitor)] = Str(c => c.NotchMonitor, (c, v) => c.NotchMonitor = v, IsMonitorName),
        [nameof(AppConfig.NotchClock)] = Bool(c => c.NotchClock, (c, v) => c.NotchClock = v),
        [nameof(AppConfig.NotchMascot)] = Bool(c => c.NotchMascot, (c, v) => c.NotchMascot = v),
        [nameof(AppConfig.NotchMusic)] = Bool(c => c.NotchMusic, (c, v) => c.NotchMusic = v),
        [nameof(AppConfig.NotchLyrics)] = Bool(c => c.NotchLyrics, (c, v) => c.NotchLyrics = v),
        [nameof(AppConfig.NotchCompactLyrics)] = Bool(c => c.NotchCompactLyrics, (c, v) => c.NotchCompactLyrics = v),
        [nameof(AppConfig.NotchCompactLyricsArt)] = Bool(c => c.NotchCompactLyricsArt, (c, v) => c.NotchCompactLyricsArt = v),
        [nameof(AppConfig.NotchCompactLyricsQuota)] = Bool(c => c.NotchCompactLyricsQuota, (c, v) => c.NotchCompactLyricsQuota = v),
        [nameof(AppConfig.NotchTasks)] = Bool(c => c.NotchTasks, (c, v) => c.NotchTasks = v),
    };

    /// <summary>"system" o uno de los idiomas de <see cref="Localization.Languages"/>.</summary>
    private static bool IsLanguage(string v) => v == "system" || Localization.Languages.Any(l => l.Code == v);

    /// <summary>"" (seguir al principal) o un nombre GDI (<c>\\.\DISPLAY2</c>). Si luego no está conectado, el notch va al
    /// principal, como con el menú de la bandeja; aquí solo se descarta basura.</summary>
    private static bool IsMonitorName(string v) => v.Length <= 64 && !v.Any(char.IsControl);

    public static IEnumerable<string> Keys => Catalog.Keys;

    public static bool IsKnown(string key) => Catalog.ContainsKey(key);

    /// <summary>Tipo del valor del ajuste (bool, int o string), o null si no está en el catálogo.</summary>
    public static Type? TypeOf(string key) => Catalog.TryGetValue(key, out var s) ? s.Type : null;

    /// <summary>¿Ese valor vale para ese ajuste (tipo exacto y rango)?</summary>
    public static bool IsValid(string key, object? value) =>
        value is not null && Catalog.TryGetValue(key, out var s) && s.Type == value.GetType() && s.IsValid(value);

    public static object Get(AppConfig config, string key) => Catalog[key].Get(config);

    /// <summary>Escribe el valor (ya validado con <see cref="IsValid"/>). Devuelve si cambió algo.</summary>
    public static bool Set(AppConfig config, string key, object value)
    {
        var s = Catalog[key];
        if (Equals(s.Get(config), value)) return false;
        s.Set(config, value);
        return true;
    }
}

/// <summary>Un monitor tal como lo ve el asistente: nombre GDI, rect en px físicos, si es el principal y su DPI efectivo.</summary>
public readonly record struct SetupMonitor(string DeviceName, Rectangle Bounds, bool IsPrimary, int Dpi)
{
    /// <summary>Ancho en px lógicos (a 96 DPI), el que usa el tamaño Auto del notch.</summary>
    public int LogicalWidth => Dpi > 0 ? (int)Math.Round(Bounds.Width * (double)Services.Dpi.BaseDpi / Dpi) : Bounds.Width;
}

/// <summary>El equipo donde se instala: sus monitores y si Claude Code está instalado (existe su carpeta de configuración).</summary>
public sealed record SetupMachine(IReadOnlyList<SetupMonitor> Monitors, bool ClaudeCodePresent);

/// <summary>
/// Valores recomendados para las preguntas sueltas del asistente según el perfil y el equipo (monitores, DPI y si hay
/// Claude Code). El instalador enseña estos mismos valores (su [Code] es un espejo de estas reglas: si cambias una,
/// cambia la otra) y <see cref="SetupApply"/> los usa para lo que no venga elegido al crear una config nueva. PURO.
/// </summary>
public static class SetupRecommendations
{
    /// <summary>Por debajo de este ancho lógico el notch abierto tapa media pantalla: mejor que se abra con un clic.</summary>
    public const int SmallScreenLogicalWidth = 1440;

    /// <summary>Iniciar con Windows: siempre recomendado (es una app de bandeja).</summary>
    public const bool StartWithWindows = true;

    /// <summary>Con Claude Code, el perfil de programar; sin él (nada que vigilar todavía), solo la cuota.</summary>
    public static SetupProfile Profile(SetupMachine machine) =>
        machine.ClaudeCodePresent ? SetupProfile.Coder : SetupProfile.QuotaOnly;

    /// <summary>Con 2+ monitores se pregunta en cuál va el notch; con uno no hay nada que elegir (igual que la bandeja).</summary>
    public static bool AskMonitor(SetupMachine machine) => NotchMonitorMenu.IsVisible(notchEnabled: true, machine.Monitors.Count);

    /// <summary>El monitor del notch: el elegido si está conectado; si no (o "" = seguir al principal), el principal.</summary>
    public static SetupMonitor? NotchMonitor(SetupMachine machine, string? deviceName)
    {
        var ms = machine.Monitors;
        if (ms.Count == 0) return null;
        if (!string.IsNullOrEmpty(deviceName))
            foreach (var m in ms)
                if (string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) return m;
        foreach (var m in ms) if (m.IsPrimary) return m;
        foreach (var m in ms) if (m.Bounds.Location == Point.Empty) return m;
        return ms[0];
    }

    /// <summary>Qué tamaño será «Auto» en ese monitor (100, 115 o 130 %): depende del ancho lógico, o sea, del DPI.</summary>
    public static int AutoSizePercent(SetupMonitor monitor) =>
        (int)Math.Round(NotchLayout.AutoScale(monitor.LogicalWidth) * 100f, MidpointRounding.AwayFromZero);

    /// <summary>
    /// ¿Hay otro monitor justo encima del notch (su borde inferior toca el superior de este y cubre su centro)? Entonces el
    /// ratón cruza el notch cada vez que sube a ese monitor y el hover lo abriría sin querer.
    /// </summary>
    public static bool HasMonitorAbove(SetupMachine machine, SetupMonitor monitor)
    {
        int cx = monitor.Bounds.Left + monitor.Bounds.Width / 2;
        return machine.Monitors.Any(o =>
            !string.Equals(o.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase)
            && o.Bounds.Bottom == monitor.Bounds.Top && o.Bounds.Left <= cx && cx < o.Bounds.Right);
    }

    /// <summary>Abrir con clic si hay un monitor encima del notch o la pantalla es pequeña; si no, con hover (el de siempre).</summary>
    public static bool OpenOnClick(SetupMachine machine, string? deviceName) =>
        NotchMonitor(machine, deviceName) is { } m
        && (HasMonitorAbove(machine, m) || m.LogicalWidth < SmallScreenLogicalWidth);

    /// <summary>Las sesiones en vivo se ofrecen si el perfil las usa y Claude Code está instalado.</summary>
    public static bool OfferLiveSessions(SetupProfile profile, SetupMachine machine) =>
        SetupProfiles.WantsLiveSessions(profile) && machine.ClaudeCodePresent;

    /// <summary>
    /// Respuestas recomendadas a las preguntas sueltas (idioma, monitor, tamaño, cómo se abre) para ese perfil y
    /// ese equipo. Sin notch (Solo cuota) solo el idioma: el resto no pinta nada.
    /// </summary>
    public static IReadOnlyDictionary<string, object> Values(SetupProfile profile, SetupMachine machine)
    {
        var v = new Dictionary<string, object> { [nameof(AppConfig.Language)] = "system" };
        if (profile == SetupProfile.QuotaOnly) return v;
        v[nameof(AppConfig.NotchMonitor)] = "";
        v[nameof(AppConfig.NotchSizePercent)] = 0;
        v[nameof(AppConfig.NotchOpenOnClick)] = OpenOnClick(machine, "");
        return v;
    }
}
