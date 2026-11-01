using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Config;

/// <summary>
/// User-editable settings, persisted to %APPDATA%\ClaudeBarWin\config.json.
/// Thresholds are in PERCENT (0-100) to match Anthropic's utilization values.
/// </summary>
public sealed class AppConfig
{
    /// <summary>"system" = follow Windows UI language; otherwise a code: en, es, nl, fr, de, ja, ko, zh-Hant.</summary>
    public string Language { get; set; } = "system";

    /// <summary>"system" = follow Windows light/dark; otherwise: dark, light, cli, imported.</summary>
    public string Theme { get; set; } = "system";
    public ImportedThemeColors? ImportedTheme { get; set; }

    /// <summary>Show the Anthropic service-health indicator in the dashboard.</summary>
    public bool ShowHealth { get; set; } = true;

    /// <summary>Show the usage chart inside the dashboard.</summary>
    public bool ShowChart { get; set; } = true;
    /// <summary>Chart metric: "spend" ($ equiv, by model) or "percent" (real utilization).</summary>
    public string ChartMode { get; set; } = "spend";
    /// <summary>In percent mode, which window to plot: "5h" or "7d".</summary>
    public string ChartPctWindow { get; set; } = "7d";

    /// <summary>Tray icon content: "percent" (utilization), "pace" (ritmo), or "both".</summary>
    public string IconDisplayMode { get; set; } = "percent";
    /// <summary>Proactive notification when a window is projected to exhaust before its reset.</summary>
    public bool PaceAlerts { get; set; } = true;

    public int RefreshSeconds { get; set; } = 60;
    public double WarnThresholdPct { get; set; } = 70;
    public double CriticalThresholdPct { get; set; } = 90;
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Utilization milestones (%) that trigger a notification when first crossed.</summary>
    public int[] NotifyMilestones { get; set; } = { 25, 50, 75, 95 };

    /// <summary>Show the local "estimated spend by model" section (ccusage-style).</summary>
    public bool ShowSpendEstimate { get; set; } = true;
    public int SpendWindowDays { get; set; } = 7;

    /// <summary>
    /// T13b: refrescar el catálogo de tarifas desde models.dev (GET anónimo de precios públicos; no
    /// sale NINGÚN dato del usuario — el sello de privacidad del footer sigue siendo verdad). OFF ⇒
    /// solo caché local + snapshot embebido. Default ON.
    /// </summary>
    public bool PricingOnlineUpdate { get; set; } = true;

    /// <summary>Contador anónimo de uso (PRIVACY.md): como mucho una vez al día manda versión, edición e idioma, sin ningún
    /// identificador. Activado por defecto, apagable en Ajustes → Sistema (y con <c>CLAUDEBAR_NO_STATS=1</c>).</summary>
    public bool UsageStatsEnabled { get; set; } = true;
    /// <summary>Lo que recuerda el contador (último día y mes avisados, si ya vio el aviso…). null en un config.json de antes
    /// de esta versión: <see cref="Normalize"/> lo crea.</summary>
    public UsageStatsData? UsageStats { get; set; }

    // Dashboard window placement & behaviour
    /// <summary>BottomRight | BottomLeft | TopRight | TopLeft | Center | Custom</summary>
    public string DashboardPosition { get; set; } = "BottomRight";
    public int DashboardX { get; set; } = -1;
    public int DashboardY { get; set; } = -1;
    /// <summary>Sticky: keep the dashboard open when it loses focus.</summary>
    public bool DashboardSticky { get; set; } = false;
    /// <summary>Keep the dashboard above other windows.</summary>
    public bool DashboardAlwaysOnTop { get; set; } = true;
    /// <summary>Dashboard window opacity (0.3–1.0).</summary>
    public double DashboardOpacity { get; set; } = 1.0;

    // Live sessions (hook de Claude Code -> Named Pipe)
    /// <summary>Interruptor maestro de la feature de sesiones en vivo (listener del pipe + mascota + lista).</summary>
    public bool LiveSessionsEnabled { get; set; } = false;
    /// <summary>Mostrar la mascota ASCII que reacciona a la fase global de las sesiones.</summary>
    public bool ShowMascot { get; set; } = true;
    /// <summary>No avisar mientras una ventana de Claude Code/terminal sea la del primer plano.</summary>
    public bool SuppressWhenFocused { get; set; } = true;
    /// <summary>Bestiario de la mascota a renderizar (de momento solo "cat").</summary>
    public string MascotKind { get; set; } = "cat";

    /// <summary>v0.4: la sugerencia «lee la cuota desde Claude Code» ya se mostró (solo una vez).</summary>
    public bool StatuslineTapSuggested { get; set; } = false;

    /// <summary>
    /// v0.5 (Velopack): el asistente de perfiles (que ya no va en el instalador, sino en la app) está pendiente. Lo pone a
    /// true la primera instalación (<see cref="OnboardingState"/>) y, en general, una config nueva: sin config.json (o con
    /// uno ilegible, apartado a <c>.bad</c>) <see cref="Load(string)"/> crea uno con el asistente pendiente. Un config.json
    /// que ya existía y no trae la clave se lee como false: ese usuario ya está configurado (el asistente de Inno, o los
    /// ajustes del panel). El asistente de bienvenida del notch lo vuelve a poner a false al terminar (o al saltarlo).
    /// </summary>
    public bool OnboardingPending { get; set; } = false;

    /// <summary>Capítulos del tutorial ya hechos (por nombre, <see cref="Services.Tutorial.TutorialChapter"/>): el tutorial
    /// se retoma en el primero que falta. Nombres desconocidos (de otra versión) se descartan al leer.</summary>
    public string[] TutorialChaptersDone { get; set; } = Array.Empty<string>();

    // Cuota con el token OAuth de Claude Code contra endpoints que Anthropic no documenta. Decisión del autor (29-10-2026):
    // se sigue usando, ACTIVADO por defecto, pero visible y apagable en Ajustes → Origen de la cuota.
    /// <summary>Leer la cuota (y el plan) de <c>api.anthropic.com/api/oauth/*</c> con el token local de Claude Code (no
    /// oficial). Apagado ⇒ ni se lee el token ni se llama a esos endpoints: la cuota sale solo de la statusline
    /// (<c>rate_limits</c>) y, sin sesión abierta, se enseña la última lectura con su edad.</summary>
    public bool QuotaOAuth { get; set; } = true;
    /// <summary>Con el token OAuth caducado, forzar su renovación lanzando <c>claude -p .</c> (como mucho cada 30 min). Ese
    /// prompt GASTA un mensaje de la cuota del usuario, así que va apagado por defecto: sin él, «sesión caducada, abre
    /// Claude Code» y la cuota sigue llegando por la statusline. Antes (≤ 0.4) era automático. Solo cuenta con
    /// <see cref="QuotaOAuth"/>.</summary>
    public bool QuotaCliRefresh { get; set; } = false;

    // Dashboard layout (v0.3)
    // (MascotSize se retiró en v0.3.7: solo queda la mascota compacta. Valores viejos en
    //  config.json se ignoran al deserializar.)
    /// <summary>Sección Cuota plegada en el dashboard.</summary>
    public bool CollapsedQuota { get; set; } = false;
    /// <summary>Sección Sesiones plegada.</summary>
    public bool CollapsedSessions { get; set; } = false;
    /// <summary>Sección Gasto plegada (por defecto sí, para un panel compacto).</summary>
    public bool CollapsedSpend { get; set; } = true;
    /// <summary>Sección Gráfica plegada (por defecto sí).</summary>
    public bool CollapsedChart { get; set; } = true;

    // Accesibilidad / microinteracciones (v0.3 F3)
    /// <summary>
    /// Reducir movimiento: colapsa TODA animación a su estado final (sin fade/tween/stagger/bounce,
    /// mascota sin spinner/jitter). Default <c>false</c> = animaciones ON (decisión de Yovan). El gate
    /// es ÚNICO; el default NO depende del SO (existe <see cref="ClaudeBarWin.Services.Motion.MotionPrefs"/>
    /// para una futura opción "seguir Windows").
    /// </summary>
    public bool ReduceMotion { get; set; } = false;

    // Notch (F1, solo lectura): pastilla anclada arriba del monitor con cuota, actividad y avisos.
    /// <summary>Interruptor maestro del notch. Apagado ⇒ no se crea ninguna ventana ni hook (opt-in).</summary>
    public bool NotchEnabled { get; set; } = false;
    /// <summary>Expandir solo con clic (por defecto basta con 250 ms de hover).</summary>
    public bool NotchOpenOnClick { get; set; } = false;
    /// <summary>En reposo, una raya de 44 × 4 en lugar de la pastilla con el % de 5 h.</summary>
    public bool NotchMinimal { get; set; } = false;
    /// <summary>Encoger a la raya tras estos segundos en reposo sin actividad (0 = nunca). Vuelve a crecer con
    /// cualquier actividad de Claude, un aviso o el ratón encima.</summary>
    public int NotchAutoMinimalSeconds { get; set; } = 15;
    /// <summary>Ocultar el notch mientras haya una app a pantalla completa (juegos, vídeo, presentaciones).</summary>
    public bool NotchHideInFullscreen { get; set; } = true;
    /// <summary>Campanita con «Terminado» y el aviso del 95 % (nunca en pantalla completa).</summary>
    public bool NotchSound { get; set; } = true;
    /// <summary>F2 (opt-in): contestar los permisos de Claude Code con los botones del notch. Apagado ⇒ ni entrada
    /// <c>permission</c> en settings.json ni pipe <c>claudebar-perm</c>. Se cambia desde el host (confirmación + backup).</summary>
    public bool NotchPermissions { get; set; } = false;
    /// <summary>Tamaño: 0 = Auto (según el ancho del monitor), o 100/115/130/150 %.</summary>
    public int NotchSizePercent { get; set; } = 0;
    /// <summary><c>Screen.DeviceName</c> del monitor del notch; "" = el principal. Solo cuenta en
    /// <see cref="Config.NotchMonitorMode.Fixed"/> (y es el punto de partida de los otros dos modos).</summary>
    public string NotchMonitor { get; set; } = "";
    /// <summary>Dónde va el notch: en <see cref="NotchMonitor"/> (como siempre), siguiendo a la ventana activa o uno en
    /// cada monitor. En <c>config.json</c> va por nombre; un valor desconocido vuelve a <see cref="Config.NotchMonitorMode.Fixed"/>.</summary>
    [JsonConverter(typeof(NotchMonitorModeJsonConverter))]
    public NotchMonitorMode NotchMonitorMode { get; set; } = NotchMonitorMode.Fixed;
    /// <summary>Umbrales de cuota (%) del notch: el más alto (≥ 95) es aviso rojo con sonido; el resto, HUD.</summary>
    public int[] NotchQuotaThresholds { get; set; } = { 80, 95 };

    // Notch F5 (B8): lo que se ve y se hace en reposo y en las páginas del expandido.
    /// <summary>Reloj (hora y fecha) intercalado con los anillos en reposo.</summary>
    public bool NotchClock { get; set; } = true;
    /// <summary>El gato del reposo.</summary>
    public bool NotchMascot { get; set; } = true;
    /// <summary>Música (página Música y la canción en reposo). Solo eventos del sistema: nada sale del PC.</summary>
    public bool NotchMusic { get; set; } = true;
    /// <summary>Letras sincronizadas de LRCLIB: envía título y artista a lrclib.net (el único destino de red del notch).
    /// Solo cuenta con la música activa.</summary>
    public bool NotchLyrics { get; set; } = true;
    /// <summary>Letra en el notch PEQUEÑO: con música sonando y letra sincronizada, el reposo enseña la línea actual en vez
    /// del título. Con ella activa la letra se pide al sonar cada pista (una vez por pista, caché primero) aunque la vista de
    /// letra esté cerrada. Solo cuenta con las letras activas.</summary>
    public bool NotchCompactLyrics { get; set; } = true;
    /// <summary>Carátula mini a la izquierda de la letra compacta (si no, una nota «♪»).</summary>
    public bool NotchCompactLyricsArt { get; set; } = true;
    /// <summary>Anillo de la cuota de 5 h a la derecha de la letra compacta.</summary>
    public bool NotchCompactLyricsQuota { get; set; } = true;
    // (NotchLyricsNetEase y NotchMusixmatchApiKey se retiraron el 29-oct junto con esas fuentes: solo queda LRCLIB. En un
    //  config.json viejo se ignoran al deserializar y desaparecen al guardar.)
    /// <summary>Tareas y recordatorios (los avisos suenan aunque la página no esté a la vista).</summary>
    public bool NotchTasks { get; set; } = true;
    /// <summary>Vaciar la bandeja al arrancar.</summary>
    public bool NotchShelfClearOnStart { get; set; } = false;
    /// <summary>Contraste adaptativo: contorno tenue sobre fondos oscuros (lee 1 px de pantalla bajo la pastilla).</summary>
    public bool NotchAdaptiveContrast { get; set; } = true;
    // Pomodoro de la página Foco: duraciones (cambiarlas es Pro), el aviso discreto del fin de fase y su estado, que sobrevive a
    // un reinicio de la app (corriendo, la hora de FIN; ver PomodoroTimer).
    /// <summary>Minutos de un foco (1..180; Ajustes ofrece <see cref="FocusDurations.FocusOptions"/>).</summary>
    public int NotchFocusMinutes { get; set; } = 25;
    /// <summary>Minutos del descanso corto.</summary>
    public int NotchShortBreakMinutes { get; set; } = 5;
    /// <summary>Minutos del descanso largo.</summary>
    public int NotchLongBreakMinutes { get; set; } = 15;
    /// <summary>Cada cuántos focos toca el descanso largo (2..8).</summary>
    public int NotchLongBreakEvery { get; set; } = 4;
    /// <summary>Sonido discreto al terminar una fase (además del sonido del notch).</summary>
    public bool NotchFocusSound { get; set; } = true;
    /// <summary>Estado del pomodoro (lo escribe el notch; null = parado al principio).</summary>
    public FocusState? NotchFocusState { get; set; }
    /// <summary>Modo, ventana y rango de la página Uso del notch al cerrarla (null = lo de serie). Lo escribe el notch en segundo
    /// plano (<see cref="BackgroundConfigWriter{T}"/>); las series ocultas no se guardan.</summary>
    public UsageViewState? NotchUsageView { get; set; }

    /// <summary>Las duraciones del pomodoro, en rango.</summary>
    [JsonIgnore]
    public FocusDurations NotchFocusDurations =>
        new FocusDurations(NotchFocusMinutes, NotchShortBreakMinutes, NotchLongBreakMinutes, NotchLongBreakEvery).Normalize();

    // ClaudeBar Pro (docs/superpowers/specs/2026-10-30-gratis-y-pro-design.md). Lo decide la licencia (Services/Licensing);
    // aquí solo queda lo que tiene que sobrevivir a un reinicio.
    /// <summary>Edición de la Store: cuándo empezó la prueba de 7 días (el primer arranque). null = aún no ha empezado (o la
    /// edición GitHub, que no tiene prueba).</summary>
    public DateTime? ProTrialStartUtc { get; set; }
    /// <summary>Edición de la Store: si la última respuesta válida de la Store decía que el Pro está comprado. Es el estado con
    /// el que se arranca (también sin red): nunca se quita el Pro por un fallo de red.</summary>
    public bool ProLastKnownPro { get; set; } = false;
    /// <summary>Edición de la Store: alguna vez hubo compra (aunque luego se reembolsara). Con ella, el aviso «tu prueba ha
    /// terminado» ya no corresponde.</summary>
    public bool ProEverOwned { get; set; } = false;
    /// <summary>El único aviso de «la prueba ha terminado» ya salió en el notch.</summary>
    public bool ProTrialEndNoticeShown { get; set; } = false;
    /// <summary>Id de la ficha de ClaudeBar en Microsoft Store para el botón de la edición GitHub; null = el de
    /// <c>ProCatalog.StoreProductId</c> (de momento un marcador).</summary>
    public string? ProStoreProductId { get; set; }

    /// <summary>Tamaños del notch que ofrecen los ajustes (0 = Auto).</summary>
    public static readonly int[] NotchSizeOptions = { 0, 100, 115, 130, 150 };

    // Rangos válidos (v0.3.10, auditoría cruzada): config.json es editable a mano desde el menú, así
    // que cualquier entero puede llegar aquí. Antes RefreshSeconds=2147484 desbordaba `* 1000` a un
    // int negativo → Timer.Interval lanzaba en el constructor → la app no arrancaba NUNCA.
    public const int MinRefreshSeconds = 15;
    public const int MaxRefreshSeconds = 86_400;
    public const int MinSpendWindowDays = 1;
    public const int MaxSpendWindowDays = 90;

    /// <summary>Intervalo del timer de refresco en ms, siempre dentro de rango (sin desbordamiento).</summary>
    [JsonIgnore]
    public int RefreshIntervalMs =>
        (int)(Math.Clamp((long)RefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds) * 1000L);

    /// <summary>
    /// Lleva a rango todos los valores numéricos/colecciones. Pura y en memoria: NO guarda (Load se
    /// llama en cada tick; guardar aquí reescribiría el archivo cada minuto).
    /// </summary>
    public AppConfig Normalize()
    {
        RefreshSeconds = Math.Clamp(RefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds);
        SpendWindowDays = Math.Clamp(SpendWindowDays, MinSpendWindowDays, MaxSpendWindowDays);

        WarnThresholdPct = ClampPct(WarnThresholdPct, 70);
        CriticalThresholdPct = ClampPct(CriticalThresholdPct, 90);
        if (WarnThresholdPct > CriticalThresholdPct)
            (WarnThresholdPct, CriticalThresholdPct) = (CriticalThresholdPct, WarnThresholdPct);

        DashboardOpacity = double.IsFinite(DashboardOpacity) ? Math.Clamp(DashboardOpacity, 0.3, 1.0) : 1.0;

        NotifyMilestones = (NotifyMilestones ?? new[] { 25, 50, 75, 95 })
            .Where(m => m is > 0 and <= 100).Distinct().OrderBy(m => m).ToArray();

        Language ??= "system";
        Theme ??= "system";
        ChartMode ??= "spend";
        NotchUsageView = NotchUsageView?.Normalize();
        ChartPctWindow ??= "7d";
        IconDisplayMode ??= "percent";
        DashboardPosition ??= "BottomRight";
        MascotKind ??= "cat";
        // Contador de uso: sin datos (config de antes de esta versión, o nueva), solo es instalación nueva si la bienvenida
        // sigue pendiente; quien ya usaba la app y actualiza no cuenta como nuevo.
        UsageStats ??= new UsageStatsData { NewInstall = OnboardingPending };
        TutorialChaptersDone = Services.Tutorial.TutorialMachine.Format(Services.Tutorial.TutorialMachine.Parse(TutorialChaptersDone));

        if (Array.IndexOf(NotchSizeOptions, NotchSizePercent) < 0) NotchSizePercent = 0;
        NotchMonitor ??= "";
        if (!Enum.IsDefined(NotchMonitorMode)) NotchMonitorMode = NotchMonitorMode.Fixed;
        NotchQuotaThresholds = (NotchQuotaThresholds ?? new[] { 80, 95 })
            .Where(t => t is > 0 and <= 100).Distinct().OrderBy(t => t).ToArray();
        // Si el filtro no deja ninguno (p. ej. [0, 120] a mano), el notch se quedaría sin avisos en silencio: 80/95.
        if (NotchQuotaThresholds.Length == 0) NotchQuotaThresholds = new[] { 80, 95 };
        var focus = NotchFocusDurations;
        (NotchFocusMinutes, NotchShortBreakMinutes, NotchLongBreakMinutes, NotchLongBreakEvery) =
            (focus.FocusMinutes, focus.ShortMinutes, focus.LongMinutes, focus.LongEvery);
        return this;
    }

    private static double ClampPct(double v, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, 0, 100) : fallback;

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeBarWin", "config.json");

    public static AppConfig Load() => Load(ConfigPath);

    /// <summary>
    /// Carga + normaliza. Si el archivo existe pero NO parsea, se aparta a <c>config.json.bad</c>
    /// (antes se sobrescribía con los defaults y el usuario perdía su configuración entera por un
    /// error de sintaxis al editarlo a mano) y se sigue con defaults.
    /// </summary>
    internal static AppConfig Load(string path)
    {
        AppConfig? cfg;
        try { cfg = Read(path); }
        catch (Exception ex)
        {
            // E/S transitoria (archivo bloqueado a mitad de un guardado): defaults SIN tocar el disco.
            AppLog.Warn($"no se pudo leer config.json: {ex.Message}");
            return new AppConfig().Normalize();
        }
        if (cfg is not null) return cfg;

        // Sin config (primera vez, o la que había no se podía leer): la de por defecto, con el asistente pendiente.
        var def = new AppConfig { OnboardingPending = true }.Normalize();
        def.Save(path);
        return def;
    }

    /// <summary>
    /// Como <see cref="Load(string)"/>, pero si el archivo existe y no se puede leer ahora mismo (bloqueado por el antivirus,
    /// OneDrive sincronizando el perfil…) LANZA en vez de seguir con los defaults, y sin archivo no escribe nada. Es para
    /// quien va a guardar encima (<see cref="SetupApply"/>): con unos defaults callados sobrescribiría toda la configuración.
    /// </summary>
    internal static AppConfig LoadOrThrow(string path) => Read(path) ?? new AppConfig().Normalize();

    /// <summary>La config del archivo, normalizada; null si no hay archivo o no parsea (apartado a <c>.bad</c>). Lanza si
    /// existe y no se puede leer.</summary>
    private static AppConfig? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path))?.Normalize();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            try { File.Copy(path, path + ".bad", overwrite: true); } catch { /* best-effort */ }
            AppLog.Warn($"config.json inválido, apartado a config.json.bad y usando valores por defecto: {ex.Message}");
            return null;
        }
    }

    public void Save() => Save(ConfigPath);

    internal void Save(string path)
    {
        try
        {
            SaveOrThrow(path);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudo guardar config.json: {ex.Message}");
        }
    }

    /// <summary>Como <see cref="Save(string)"/>, pero lanza si no se pudo guardar (<see cref="SetupApply"/> tiene que poder
    /// decirlo). Atómico: si falla, el archivo sigue como estaba.</summary>
    internal void SaveOrThrow(string path)
    {
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        lock (FileGate) AtomicFile.WriteAllText(path, json);
    }

    /// <summary>Turno para escribir <c>config.json</c>: lo toma cada guardado y, de la lectura a la escritura, quien guarda en
    /// segundo plano (<see cref="BackgroundConfigWriter{T}"/>), para que ninguno se cuele en medio y se pierdan ajustes.</summary>
    internal static readonly object FileGate = new();
}

/// <summary>Dónde va el notch con varios monitores (decisión del 29-oct: «haz que se pueda decidir»).</summary>
public enum NotchMonitorMode
{
    /// <summary>En el monitor elegido (<see cref="AppConfig.NotchMonitor"/>; "" = el principal). Como siempre.</summary>
    Fixed,
    /// <summary>Un solo notch que se va al monitor de la ventana en primer plano.</summary>
    FollowActive,
    /// <summary>Un notch por monitor; los botones (Permiso, Recordatorio) solo en el de la ventana activa.</summary>
    AllMonitors,
}

/// <summary>
/// <see cref="NotchMonitorMode"/> en <c>config.json</c> por nombre (<c>"FollowActive"</c>), y tolerante al leer: acepta el
/// nombre sin distinguir mayúsculas o el número, y cualquier otra cosa (un error al editarlo a mano) es
/// <see cref="NotchMonitorMode.Fixed"/>. Con el conversor estándar un valor desconocido tumbaría TODO el archivo
/// (<c>config.json.bad</c> y ajustes por defecto) por un solo campo.
/// </summary>
public sealed class NotchMonitorModeJsonConverter : JsonConverter<NotchMonitorMode>
{
    public override NotchMonitorMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return Enum.TryParse<NotchMonitorMode>(reader.GetString(), ignoreCase: true, out var m) && Enum.IsDefined(m)
                    ? m : NotchMonitorMode.Fixed;
            case JsonTokenType.Number:
                return reader.TryGetInt32(out int n) && Enum.IsDefined((NotchMonitorMode)n) ? (NotchMonitorMode)n : NotchMonitorMode.Fixed;
            default:
                reader.Skip();   // objeto o lista: se salta entero y el resto del archivo se lee bien
                return NotchMonitorMode.Fixed;
        }
    }

    public override void Write(Utf8JsonWriter writer, NotchMonitorMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Enum.IsDefined(value) ? value.ToString() : nameof(NotchMonitorMode.Fixed));
}

/// <summary>Theme colours imported from an .itermcolors file, stored as #RRGGBB hex.</summary>
public sealed class ImportedThemeColors
{
    public string? Bg { get; set; }
    public string? Fg { get; set; }
    public string? Dim { get; set; }
    public string? Track { get; set; }
    public string? Ok { get; set; }
    public string? Warn { get; set; }
    public string? Critical { get; set; }
}
