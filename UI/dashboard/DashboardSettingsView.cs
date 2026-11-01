using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI;

/// <summary>
/// Panel de ajustes dentro del dashboard (modo "settings"). Sin estado: dibuja las filas de
/// configuración agrupadas y registra cada control clicable en <c>rects</c> con una clave de acción
/// (p.ej. "toggle:ShowSpend", "theme:dark", "freq:60", "cycle:position", "special:importtheme").
/// <see cref="ActionFor"/> traduce las claves que son MUTACIÓN SIMPLE de <see cref="AppConfig"/> a la
/// mutación que <c>DashboardForm</c> emite por su evento <c>SettingsChanged</c>. Las claves "special:*"
/// NO tienen <see cref="ActionFor"/>: el host las enruta por <c>SpecialActionRequested</c> (acciones que
/// necesitan diálogo/instalador, como importar un .itermcolors o instalar/quitar los hooks).
/// Cada helper avanza y devuelve <c>y</c> idéntico en draw=false/true (simetría medir/pintar).
/// </summary>
public static class DashboardSettingsView
{
    // Posiciones del dashboard en orden de ciclo (sin "Custom": ese estado lo fija arrastrar el panel).
    private static readonly string[] PositionCycle = { "BottomRight", "BottomLeft", "TopRight", "TopLeft", "Center" };
    // Opacidades ofrecidas en el panel (como en el menú original, recortado a 3 para caber).
    private static readonly (string val, string txt)[] OpacitySegs =
        { ("1", "100%"), ("0.85", "85%"), ("0.7", "70%") };
    // Combinaciones de umbral warn/critical del menú original.
    private static readonly (double warn, double crit)[] ThresholdOptions = { (70, 90), (80, 95), (60, 85) };
    // Hitos de notificación individuales (toggles sobre NotifyMilestones).
    private static readonly int[] MilestoneOptions = { 25, 50, 75, 95 };

    /// <summary>Enlace «Política de privacidad» bajo el contador de uso: lo abre el host en el navegador.</summary>
    public const string PrivacyKey = "special:privacy";
    /// <summary>Acerca de › «Tutorial (2 min)»: la bandeja abre el tutorial interactivo.</summary>
    public const string TutorialKey = "special:tutorial";

    /// <summary>«Quitar la integración con Claude Code»: el host pide confirmación y quita hooks y statusline de una vez
    /// (<see cref="ClaudeIntegration"/>).</summary>
    public const string RemoveIntegrationKey = "special:removeintegration";

    // --- Scroll del panel de ajustes (v0.3.7) -----------------------------------------------------
    // El panel de ajustes era más alto que la pantalla ("de arriba a abajo", queja de Yovan): ahora el
    // alto de la ventana se LIMITA a un % del área útil y el contenido rueda con la rueda del ratón.
    // Matemática PURA aquí (testeable); el estado del scroll vive en DashboardForm.

    /// <summary>Tope del alto del panel de ajustes como % del alto útil de la pantalla.</summary>
    public const int MaxPanelHeightPct = 65;
    /// <summary>Píxeles de desplazamiento por "diente" de rueda (delta 120).</summary>
    public const int WheelStepPx = 48;
    /// <summary>Ancho de la barrita de scroll y su margen al borde derecho (T11: px de diseño a
    /// 96 DPI proyectados al DPI vigente; a factor 1.0 valen 4/4 como las const históricas).</summary>
    public static int ScrollBarW => Dpi.Scale(4);
    public static int ScrollBarMargin => Dpi.Scale(4);

    /// <summary>Acota el scroll a [0, contentH - viewportH]; 0 si el contenido cabe entero.</summary>
    internal static int ClampScroll(int scroll, int contentH, int viewportH)
        => Math.Max(0, Math.Min(scroll, Math.Max(0, contentH - viewportH)));

    /// <summary>
    /// Convierte un evento de rueda en PÍXELES a desplazar, ACUMULANDO el delta crudo para soportar
    /// trackpads de precisión. Un ratón clásico manda un "diente" entero de ±120 por muesca; un trackpad
    /// (portátil, Magic Trackpad…) hace smooth-scrolling: muchos eventos pequeños (±1…±40) que con la
    /// división entera <c>delta / 120</c> truncaban a 0 ⇒ el panel NO rodaba con trackpad. Aquí cada
    /// evento SUMA su <paramref name="delta"/> al acumulador y solo se convierte a píxeles la parte que
    /// ya completa "muescas" (<c>acc * WheelStepPx / 120</c>); el delta sobrante se conserva en
    /// <c>rest</c> para el siguiente evento, de modo que los deltas pequeños se van sumando y producen
    /// scroll suave y proporcional sin perder nada.
    /// <para>
    /// Simetría: tanto la división entera como el módulo de C# heredan el signo del numerador, así que
    /// subir (+) y bajar (−) se comportan idénticos en magnitud (p.ej. ±100·48/120 = ±40), exactamente
    /// lo que queremos. Para un diente exacto de ±120 da px=±48 y rest=0 (misma sensación que el ratón de
    /// siempre), y nunca se "traga" delta — el resto se acumula hasta completar el siguiente píxel.
    /// </para>
    /// </summary>
    /// <param name="accumulatedDelta">Resto arrastrado de eventos anteriores (0 al empezar); es delta
    /// crudo YA escalado por WheelStepPx, opaco para quien llama — solo hay que volver a pasarlo.</param>
    /// <param name="delta">Delta de ESTE evento de rueda (<c>MouseEventArgs.Delta</c>; ±120 por muesca
    /// de ratón clásico, valores pequeños en trackpads).</param>
    /// <returns><c>px</c> = píxeles enteros a desplazar (mismo signo que el delta neto); <c>rest</c> =
    /// resto escalado que queda acumulado para el siguiente evento (siempre <c>|rest| &lt; 120</c> ⇒
    /// insuficiente para otro píxel, así que se irá sumando hasta completarlo sin perder NADA).</returns>
    internal static (int px, int rest) WheelToPixels(int accumulatedDelta, int delta)
    {
        // Trabajamos en el dominio ESCALADO (delta·WheelStepPx) para que el reparto píxel/resto sea
        // EXACTO sea cual sea WheelStepPx (120 no es múltiplo de 48): el resto se conserva entero y los
        // deltas diminutos del trackpad se acumulan sin tragarse nada ni perder precisión.
        long scaled = (long)accumulatedDelta + (long)delta * WheelStepPx;
        // Píxeles completos: división entera de C# que TRUNCA HACIA CERO ⇒ simétrica en + y −.
        int px = (int)(scaled / 120);
        // Resto escalado (también con signo del numerador): lo que aún no alcanza el siguiente píxel.
        int rest = (int)(scaled % 120);
        return (px, rest);
    }

    /// <summary>
    /// Rect del pulgar de la barra de scroll: alto proporcional al viewport (mínimo 24px para que
    /// siempre se pueda ver/agarrar) y posición proporcional al scroll. <see cref="Rectangle.Empty"/>
    /// cuando el contenido cabe (sin overflow no hay barra).
    /// </summary>
    internal static Rectangle ThumbRect(int trackX, int trackTop, int viewportH, int contentH, int scroll)
    {
        if (contentH <= viewportH || viewportH <= 0) return Rectangle.Empty;
        int thumbH = Math.Max(Dpi.Scale(24), (int)((long)viewportH * viewportH / contentH)); // T11: mínimo agarrable escalado
        thumbH = Math.Min(thumbH, viewportH);
        int maxScroll = contentH - viewportH;
        int maxThumbTravel = viewportH - thumbH;
        int thumbY = trackTop + (maxScroll <= 0 ? 0
            : (int)((long)ClampScroll(scroll, contentH, viewportH) * maxThumbTravel / maxScroll));
        return new Rectangle(trackX, thumbY, ScrollBarW, thumbH);
    }

    // Zona de clic del "‹ Ajustes" (chrome de la vista de ajustes): mínimo cómodo y alto históricos
    // (eran el 80×20 fijo); el ancho real ahora se MIDE con el texto localizado (T8d).
    private const int BackMinHitW = 80, BackHitH = 20;

    /// <summary>
    /// Rect clicable del "‹ Ajustes" (T8d): ancho = texto localizado MEDIDO (con suelo
    /// <see cref="BackMinHitW"/> para conservar la zona cómoda histórica y techo <paramref name="w"/>).
    /// El 80×20 fijo dejaba media etiqueta DE/FR/NL fuera de la zona de clic. Determinista (misma
    /// medición en ambas pasadas) → medir==pintar.
    /// </summary>
    internal static Rectangle BackHitRect(Graphics g, string label, Font f, int x, int y, int w)
    {
        // T11: la zona cómoda mínima y el alto escalan con el DPI (el texto medido ya crece solo).
        int textW = (int)Math.Ceiling(g.MeasureString(label, f).Width);
        return new Rectangle(x, y,
            Math.Min(Math.Max(Dpi.Scale(BackMinHitW), textW), Math.Max(1, w)), Dpi.Scale(BackHitH));
    }

    /// <summary>Dibuja el panel y registra rects clicables con clave de acción. Devuelve nuevo y.
    /// <paramref name="version"/> es la versión a mostrar en "Acerca de"; si es null se resuelve desde
    /// el ensamblado en ejecución (inyectable para test). <paramref name="pro"/> = estado del Pro: sin él, las filas de las
    /// funciones Pro del notch salen con candado (<see cref="ProDependent"/>).</summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w, AppConfig cfg, Strings s, Theme theme,
                           Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects, string? version = null,
                           ProState pro = ProState.Pro)
    {
        rects.Clear();

        // -------- Mostrar (qué se ve en el dashboard) --------
        y = SectionHeader(g, draw, s.MenuSections, x, y, w, theme, smallFont);
        // Gasto estimado lleva subtítulo aclaratorio ("Coste equivalente por modelo").
        y = ToggleRow(g, draw, "toggle:ShowSpend", s.ShowSpend, s.ShowSpendSubtitle, cfg.ShowSpendEstimate, x, y, w, theme, labelFont, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowHealth", s.ShowServiceStatus, null, cfg.ShowHealth, x, y, w, theme, labelFont, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowChart", s.UsageChart, null, cfg.ShowChart, x, y, w, theme, labelFont, smallFont, rects);

        // -------- Origen de la cuota (v0.4: statusline de Claude Code, 0 peticiones) --------
        var tapNow = DateTime.UtcNow;
        y = QuotaSourceSection(g, draw, QuotaIntegration.Current(tapNow), QuotaIntegration.LastSeenUtc, tapNow,
            x, y, w, cfg, s, theme, labelFont, smallFont, rects);

        // -------- Sesiones en vivo (master hooks + StatusBadge + mascota dependiente) --------
        // Fila MAESTRA = instalar/quitar hooks con StatusBadge; Mostrar mascota / Tamaño / Silenciar son
        // DEPENDIENTES (sangrados Lg, atenuados+inertes mientras no haya hooks). Quita el control huérfano:
        // "Mostrar mascota" ya no es una fila suelta por encima del botón. (T0 hace que el gato Idle se vea
        // con ShowMascot on aunque live esté off; aquí la jerarquía master→dependiente comunica que la
        // mascota solo cobra vida con hooks.)
        y = LiveSessionsSection(g, draw, HookInstaller.IsInstalled(), x, y, w, cfg, s, theme, labelFont, smallFont, rects);

        // -------- Quitar la integración con Claude Code (hooks + statusline de una vez) --------
        // Justo después de las dos integraciones. Siempre a la vista (lo busca la guía «Antes de desinstalar» de la ficha de la
        // Store; sin nada instalado, el host lo dice). AccentText: el acento como texto cumple AA sobre el fondo.
        y = ButtonRow(g, draw, RemoveIntegrationKey, s.RemoveIntegration, theme.AccentText, x, y, w, theme, labelFont, rects);
        y = NoteRow(g, draw, s.RemoveIntegrationHint, x, y, w, theme, smallFont);

        // -------- Notch (F1): maestro + dependientes (atenuados e inertes con el notch apagado) --------
        y = NotchSection(g, draw, x, y, w, cfg, s, theme, labelFont, smallFont, rects, pro);

        // -------- Notificaciones (master + dependientes) --------
        // "Notificaciones" es el MASTER; PaceAlerts y los hitos son DEPENDIENTES: sangrados Spacing.Lg,
        // atenuados e INERTES cuando el master está off (vía DrawDependent). Resuelve "lo activé y no pasa
        // nada": el dependiente solo responde si el master está encendido.
        y = SectionHeader(g, draw, s.Notifications, x, y, w, theme, smallFont);
        y = ToggleRow(g, draw, "toggle:Notifications", s.Enabled, null, cfg.NotificationsEnabled, x, y, w, theme, labelFont, smallFont, rects);
        bool notif = cfg.NotificationsEnabled;
        y = DrawDependent(notif, x, y, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:PaceAlerts", s.PaceAlerts, null, cfg.PaceAlerts,
                ix, y, iw, th, labelFont, smallFont, rects));
        // Hitos individuales 25/50/75/95 como toggles multi-activo que editan el array NotifyMilestones:
        // un único MultiSegmentRow (varios activos, mismo estilo de pill Accent+Contrast), SIN re-pintado manual.
        int milestoneY = y; // captura el y de entrada del dependiente para la lambda
        y = DrawDependent(notif, x, milestoneY, w, theme, rects,
            (ix, iw, th) => MultiSegmentRow(g, draw, "milestone", s.NotifyWhenReaching,
                MilestoneOptions.Select(m => ($"{m}", $"{m}%")).ToArray(),
                cfg.NotifyMilestones ?? Array.Empty<int>(), ix, milestoneY, iw, th, smallFont, rects));

        // -------- Frecuencia de actualización --------
        y = SectionHeader(g, draw, s.UpdateFrequency, x, y, w, theme, smallFont);
        y = SegmentedRow(g, draw, "freq", "",
            new[] { ("30", s.Sec30), ("60", s.Min1), ("300", s.Min5), ("900", s.Min15) },
            cfg.RefreshSeconds.ToString(), x, y, w, theme, smallFont, rects);

        // -------- Icono --------
        y = SectionHeader(g, draw, s.MenuIcon, x, y, w, theme, smallFont);
        y = SegmentedRow(g, draw, "icon", s.IconMode,
            new[] { ("percent", "%"), ("pace", "▲"), ("both", "%▲") },
            string.IsNullOrEmpty(cfg.IconDisplayMode) ? "percent" : cfg.IconDisplayMode, x, y, w, theme, smallFont, rects);
        y = SegmentedRow(g, draw, "threshold", s.ColorThreshold,
            ThresholdOptions.Select(t => ($"{t.warn:0}/{t.crit:0}", $"{t.warn:0}/{t.crit:0}")).ToArray(),
            $"{cfg.WarnThresholdPct:0}/{cfg.CriticalThresholdPct:0}", x, y, w, theme, smallFont, rects);

        // -------- Apariencia --------
        // "Importar .itermcolors" se RETIRA de aquí (lo consolida T8 en "Acerca de"): aquí solo preferencias.
        y = SectionHeader(g, draw, s.MenuAppearance, x, y, w, theme, smallFont);
        y = SegmentedRow(g, draw, "theme", s.Theme,
            new[] { ("system", s.ThemeSystem), ("dark", s.ThemeDark), ("light", s.ThemeLight), ("cli", "CLI") },
            cfg.Theme, x, y, w, theme, smallFont, rects);
        // Posición: fila que cicla (5 opciones no caben en segmentos); muestra la posición actual.
        y = CycleRow(g, draw, "cycle:position", s.Position, PosLabel(cfg.DashboardPosition, s), x, y, w, theme, smallFont, rects);
        y = SegmentedRow(g, draw, "opacity", s.Opacity, OpacitySegs, FmtOpacity(cfg.DashboardOpacity), x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:Sticky", s.Sticky, null, cfg.DashboardSticky, x, y, w, theme, labelFont, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:OnTop", s.AlwaysOnTop, null, cfg.DashboardAlwaysOnTop, x, y, w, theme, labelFont, smallFont, rects);
        // "Reducir movimiento" lleva subtítulo aclaratorio ("Desactiva las animaciones").
        y = ToggleRow(g, draw, "toggle:ReduceMotion", s.ReduceMotion, s.ReduceMotionSubtitle, cfg.ReduceMotion, x, y, w, theme, labelFont, smallFont, rects);

        // -------- Sistema --------
        // i18n: el título sale de Strings (fin del literal hardcodeado). Agrupa Arrancar con Windows + Idioma
        // (el ciclo de idioma se trae aquí desde su antigua sección suelta: una sola pantalla, menos cabeceras).
        y = SectionHeader(g, draw, s.MenuSystem, x, y, w, theme, smallFont);
        // Edición de la Store: la tarea de inicio se lee y se cambia en segundo plano; mientras no se sabe o está cambiando,
        // el subtítulo lo dice y el pill enseña el último estado conocido (desconocido no es «apagado»).
        var startup = StartupManager.State();
        y = ToggleRow(g, draw, "toggle:Startup", s.StartWithWindows, StartupSubtitle(startup, s), startup.On == true,
            x, y, w, theme, labelFont, smallFont, rects);
        // T13b: toggle de privacidad del catálogo de tarifas. Subtítulo honesto: solo se descargan
        // precios públicos de models.dev, no sale nada del usuario (el sello del footer no cambia).
        y = ToggleRow(g, draw, "toggle:PricingOnline", s.PricingOnlineUpdate, s.PricingOnlineUpdateSubtitle,
            cfg.PricingOnlineUpdate, x, y, w, theme, labelFont, smallFont, rects);
        // Contador anónimo de uso (PRIVACY.md): lo que manda, en una línea, y el enlace a la política. Apagarlo para el envío
        // al momento (TrayAppContext.MutateConfig → UsageStatsService.SetEnabled).
        y = ToggleRow(g, draw, "toggle:UsageStats", s.UsageStatsLabel, s.UsageStatsSubtitle, cfg.UsageStatsEnabled,
            x, y, w, theme, labelFont, smallFont, rects);
        y = ActionRow(g, draw, PrivacyKey, s.UsageStatsPrivacy, x, y, w, theme, smallFont, rects);
        y = CycleRow(g, draw, "cycle:lang", s.Language,
            Localization.LanguageDisplayName(cfg.Language, s), x, y, w, theme, smallFont, rects);

        // -------- Acerca de (acciones al final, separadas de las preferencias) --------
        // Versión (texto, no clicable) + Importar .itermcolors (ButtonRow) + qué es y el aviso de marca (NoteRow). "Importar tema" se consolida aquí
        // (sacado de Apariencia en T7) para no mezclar una ACCIÓN con las preferencias. Logs/GitHub solo se
        // pintarían si el host enrutara sus "special:*"; TrayAppContext NO los enruta hoy → no se dibujan (sin
        // botón muerto).
        y = SectionHeader(g, draw, s.About, x, y, w, theme, smallFont);
        y = InfoRow(g, draw, s.VersionLabel, "v" + (version ?? ResolveVersion()), x, y, w, theme, labelFont, rects);
        // El tutorial interactivo, para volver a él cuando se quiera (también en el menú de la bandeja).
        y = ActionRow(g, draw, TutorialKey, s.Tut.SettingsLink, x, y, w, theme, smallFont, rects);
        // AccentText (no Accent): el acento como TEXTO/borde fino debe cumplir AA (el violeta de relleno se
        // queda en ~3.7:1 sobre el fondo oscuro; AccentText lo aclara en oscuro y lo oscurece en claro). (P1 #3)
        y = ButtonRow(g, draw, "special:importtheme", s.ImportTheme, theme.AccentText, x, y, w, theme, labelFont, rects);
        // Qué es (dicho de forma descriptiva) y quién lo publica; y que no es de Anthropic (docs/branding.md).
        y = NoteRow(g, draw, s.AboutProduct, x, y, w, theme, smallFont);
        y = NoteRow(g, draw, s.AboutDisclaimer, x, y, w, theme, smallFont);

        return y;
    }

    /// <summary>Subtítulo de «Iniciar con Windows»: «Cambiando…» con un cambio en marcha, «Comprobando…» si aún no se sabe
    /// cómo está, y nada si se sabe. PURO.</summary>
    internal static string? StartupSubtitle(StartupState state, Strings s) =>
        state.Changing ? s.StartupChanging : !state.Known ? s.StartupChecking : null;

    /// <summary>Versión a mostrar en "Acerca de" cuando no se inyecta una: Major.Minor.Build del ensamblado.</summary>
    private static string ResolveVersion()
    {
        var v = typeof(DashboardSettingsView).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// Traduce la clave de un rect clicado a la mutación de config. Devuelve null para claves "special:*"
    /// (las enruta el host por SpecialActionRequested) o desconocidas.
    /// </summary>
    public static Action<AppConfig>? ActionFor(string key)
    {
        switch (key)
        {
            case "toggle:ShowSpend": return c => c.ShowSpendEstimate = !c.ShowSpendEstimate;
            case "toggle:ShowHealth": return c => c.ShowHealth = !c.ShowHealth;
            case "toggle:ShowChart": return c => c.ShowChart = !c.ShowChart;
            case "toggle:ShowMascot": return c => c.ShowMascot = !c.ShowMascot;
            case "toggle:Suppress": return c => c.SuppressWhenFocused = !c.SuppressWhenFocused;
            case "toggle:Notifications": return c => c.NotificationsEnabled = !c.NotificationsEnabled;
            case "toggle:PaceAlerts": return c => c.PaceAlerts = !c.PaceAlerts;
            case "toggle:Sticky": return c => c.DashboardSticky = !c.DashboardSticky;
            case "toggle:OnTop": return c => c.DashboardAlwaysOnTop = !c.DashboardAlwaysOnTop;
            case "toggle:ReduceMotion": return c => c.ReduceMotion = !c.ReduceMotion;
            case "toggle:PricingOnline": return c => c.PricingOnlineUpdate = !c.PricingOnlineUpdate;
            // Con el interruptor delante, la explicación ya se ha visto (no hace falta el globo de quien actualiza).
            case "toggle:UsageStats":
                return c =>
                {
                    c.UsageStatsEnabled = !c.UsageStatsEnabled;
                    c.UsageStats = Services.Telemetry.UsagePing.NoticeSeenInApp(c.UsageStats);
                };
            case "toggle:QuotaOAuth": return c => c.QuotaOAuth = !c.QuotaOAuth;
            case "toggle:QuotaCliRefresh": return c => c.QuotaCliRefresh = !c.QuotaCliRefresh;
            case "toggle:Startup": return _ => StartupManager.Toggle();

            case "toggle:NotchEnabled": return c => c.NotchEnabled = !c.NotchEnabled;
            case "toggle:NotchMinimal": return c => c.NotchMinimal = !c.NotchMinimal;
            case "toggle:NotchAutoMinimal": return c => c.NotchAutoMinimalSeconds = c.NotchAutoMinimalSeconds > 0 ? 0 : 15;
            case "toggle:NotchHideFullscreen": return c => c.NotchHideInFullscreen = !c.NotchHideInFullscreen;
            case "toggle:NotchSound": return c => c.NotchSound = !c.NotchSound;
            case "toggle:NotchClock": return c => c.NotchClock = !c.NotchClock;
            case "toggle:NotchMascot": return c => c.NotchMascot = !c.NotchMascot;
            case "toggle:NotchMusic": return c => c.NotchMusic = !c.NotchMusic;
            case "toggle:NotchLyrics": return c => c.NotchLyrics = !c.NotchLyrics;
            case "toggle:NotchCompactLyrics": return c => c.NotchCompactLyrics = !c.NotchCompactLyrics;
            case "toggle:NotchCompactLyricsArt": return c => c.NotchCompactLyricsArt = !c.NotchCompactLyricsArt;
            case "toggle:NotchCompactLyricsQuota": return c => c.NotchCompactLyricsQuota = !c.NotchCompactLyricsQuota;
            case "toggle:NotchTasks": return c => c.NotchTasks = !c.NotchTasks;
            case "toggle:NotchShelfClear": return c => c.NotchShelfClearOnStart = !c.NotchShelfClearOnStart;
            case "toggle:NotchContrast": return c => c.NotchAdaptiveContrast = !c.NotchAdaptiveContrast;
            case "toggle:NotchFocusSound": return c => c.NotchFocusSound = !c.NotchFocusSound;
            case "notchopen:hover": return c => c.NotchOpenOnClick = false;
            case "notchopen:click": return c => c.NotchOpenOnClick = true;
            // Modo del monitor (29-oct): fijo, seguir a la ventana activa o uno en cada monitor; en caliente.
            case "notchmonmode:fixed": return c => c.NotchMonitorMode = NotchMonitorMode.Fixed;
            case "notchmonmode:follow": return c => c.NotchMonitorMode = NotchMonitorMode.FollowActive;
            case "notchmonmode:all": return c => c.NotchMonitorMode = NotchMonitorMode.AllMonitors;
            // Los monitores se leen al hacer clic (pueden conectarse/desconectarse con la app abierta).
            case "cycle:notchmonitor":
                return c => c.NotchMonitor = NextMonitor(c.NotchMonitor, Screen.AllScreens.Select(m => m.DeviceName).ToList());

            case "theme:system": return c => c.Theme = "system";
            case "theme:dark": return c => c.Theme = "dark";
            case "theme:light": return c => c.Theme = "light";
            case "theme:cli": return c => c.Theme = "cli";

            case "icon:percent": return c => c.IconDisplayMode = "percent";
            case "icon:pace": return c => c.IconDisplayMode = "pace";
            case "icon:both": return c => c.IconDisplayMode = "both";

            case "freq:30": return c => c.RefreshSeconds = 30;
            case "freq:60": return c => c.RefreshSeconds = 60;
            case "freq:300": return c => c.RefreshSeconds = 300;
            case "freq:900": return c => c.RefreshSeconds = 900;

            case "cycle:lang": return c => c.Language = Localization.NextLanguage(c.Language);
            case "cycle:position": return c => c.DashboardPosition = NextPosition(c.DashboardPosition);
        }

        // Opacidad: "opacity:<double>"
        if (key.StartsWith("opacity:") && double.TryParse(key.AsSpan("opacity:".Length),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var op))
            return c => c.DashboardOpacity = op;

        // Umbral: "threshold:<warn>/<crit>"
        if (key.StartsWith("threshold:"))
        {
            var parts = key.AsSpan("threshold:".Length);
            int slash = parts.IndexOf('/');
            if (slash > 0
                && double.TryParse(parts[..slash], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var warn)
                && double.TryParse(parts[(slash + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var crit))
                return c => { c.WarnThresholdPct = warn; c.CriticalThresholdPct = crit; };
        }

        // Pomodoro: "focusmin:<min>", "focusshort:<min>", "focuslong:<min>", "focusevery:<n>"; solo lo que se ofrece.
        if (FocusOption(key, "focusmin:", FocusDurations.FocusOptions) is { } focusMin) return c => c.NotchFocusMinutes = focusMin;
        if (FocusOption(key, "focusshort:", FocusDurations.ShortOptions) is { } shortMin) return c => c.NotchShortBreakMinutes = shortMin;
        if (FocusOption(key, "focuslong:", FocusDurations.LongOptions) is { } longMin) return c => c.NotchLongBreakMinutes = longMin;
        if (FocusOption(key, "focusevery:", FocusDurations.EveryOptions) is { } every) return c => c.NotchLongBreakEvery = every;

        // Tamaño del notch: "notchsize:<pct>" (0 = Auto); solo los tamaños que se ofrecen.
        if (key.StartsWith("notchsize:") && int.TryParse(key.AsSpan("notchsize:".Length), out var size)
            && Array.IndexOf(AppConfig.NotchSizeOptions, size) >= 0)
            return c => c.NotchSizePercent = size;

        // Hito individual: "milestone:<pct>" (toggle dentro del array).
        if (key.StartsWith("milestone:") && int.TryParse(key.AsSpan("milestone:".Length), out var pct))
            return c =>
            {
                var list = (c.NotifyMilestones ?? Array.Empty<int>()).ToList();
                if (list.Contains(pct)) list.Remove(pct); else list.Add(pct);
                c.NotifyMilestones = list.Distinct().OrderBy(v => v).ToArray();
            };

        return null; // "special:*" o desconocida → no es mutación simple
    }

    /// <summary>El valor de una clave del pomodoro («focusmin:25») si es de <paramref name="prefix"/> y está entre los que se
    /// ofrecen; null si no.</summary>
    private static int? FocusOption(string key, string prefix, IReadOnlyList<int> options) =>
        key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(key.AsSpan(prefix.Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && options.Contains(v) ? v : null;

    // ---------------- ciclos / etiquetas ----------------

    /// <summary>Clave del segmento del modo de monitor («fixed», «follow», «all»); uno desconocido es Fijo. Pura.</summary>
    internal static string MonitorModeKey(NotchMonitorMode mode) => mode switch
    {
        NotchMonitorMode.FollowActive => "follow",
        NotchMonitorMode.AllMonitors => "all",
        _ => "fixed",
    };

    /// <summary>Siguiente monitor del notch: principal ("") → cada pantalla en orden → principal. Un monitor
    /// que ya no está conectado se trata como el principal (la fila ya lo enseña como «Principal»), así que el
    /// clic pasa a la primera pantalla y siempre cambia lo que se ve. Puro (los nombres se pasan por parámetro).</summary>
    internal static string NextMonitor(string? current, IReadOnlyList<string> screens)
    {
        if (screens.Count == 0) return "";
        if (string.IsNullOrEmpty(current)) return screens[0];
        int i = -1;
        for (int k = 0; k < screens.Count; k++)
            if (string.Equals(screens[k], current, StringComparison.OrdinalIgnoreCase)) { i = k; break; }
        if (i < 0) return screens[0];
        return i + 1 >= screens.Count ? "" : screens[i + 1];
    }

    /// <summary>Etiqueta del monitor del notch: «Principal» o el nombre legible de la pantalla («LG ULTRAGEAR · 2560×1440 ·
    /// derecha», <see cref="MonitorNames"/>). Un monitor guardado que ya no está conectado sale como «Principal», que es donde
    /// lo pone el notch de hecho (y el siguiente clic pasa a la primera pantalla). Pura (los monitores se pasan por parámetro).</summary>
    internal static string MonitorLabel(string? device, IReadOnlyList<MonitorInfo> monitors, Strings s) =>
        MonitorNames.LabelFor(device, monitors, s) ?? s.NotchMonitorPrimary;

    /// <summary>Monitores para la etiqueta (nombres del EDID cacheados; nada lanza al pintar).</summary>
    private static IReadOnlyList<MonitorInfo> CurrentMonitors()
    {
        try { return ClaudeBarWin.Services.Notch.Platform.MonitorCatalog.Current(); }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudieron leer los monitores: {ex.Message}");
            return Array.Empty<MonitorInfo>();
        }
    }

    private static string NextPosition(string current)
    {
        int i = Array.IndexOf(PositionCycle, string.IsNullOrEmpty(current) ? "BottomRight" : current);
        return PositionCycle[(i < 0 ? 0 : (i + 1) % PositionCycle.Length)];
    }

    private static string PosLabel(string key, Strings s) => key switch
    {
        "BottomRight" => s.PosBottomRight,
        "BottomLeft" => s.PosBottomLeft,
        "TopRight" => s.PosTopRight,
        "TopLeft" => s.PosTopLeft,
        "Center" => s.PosCenter,
        "Custom" => s.PosCustom,
        _ => key,
    };

    private static string FmtOpacity(double op)
    {
        double v = op <= 0 ? 1.0 : op;
        // Casa con OpacitySegs ("1"/"0.85"/"0.7") usando invariant culture.
        if (Math.Abs(v - 1.0) < 0.001) return "1";
        if (Math.Abs(v - 0.85) < 0.001) return "0.85";
        if (Math.Abs(v - 0.7) < 0.001) return "0.7";
        return v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------------- helpers de dibujo (simetría medir/pintar; registran rects con clave) ----------------

    // -------- ritmo vertical sobre rejilla 8pt: DOS tokens uniformes (P2 #2) --------
    // sectionGap = aire ARRIBA de cada cabecera de sección (separa un grupo del anterior).
    // rowGap     = separación entre filas del MISMO grupo.
    // Antes los avances eran +Md arriba de la cabecera y +Sm entre filas (ritmo plano, poco "Apple").
    // Ahora la cabecera respira Xl arriba y las filas Md entre sí: agrupación perceptible y consistente.
    internal const int SectionGap = Spacing.Xl;   // 24 — aire sobre cada cabecera
    internal const int RowGap = Spacing.Md;       // 12 — separación entre filas
    // Alto de contenido de una fila estándar de 1 línea; el avance suma RowGap entre filas.
    // T11: en px de diseño (96 DPI) proyectados al DPI vigente (a factor 1.0, idénticos a las const).
    private static int RowContentHeight => Dpi.Scale(18);
    private static int RowAdvance => RowContentHeight + RowGap;       // fila estándar = alto + rowGap
    // Alto de un bloque de segmentos (coincide con DashboardDataView.DrawSegments: h=18 escalado).
    private static int SegmentHeight => Dpi.Scale(18);
    private static int SegmentRowAdvance => SegmentHeight + RowGap;   // fila de segmentos = alto + rowGap
    // Separación vertical entre las dos hileras cuando una fila de segmentos ENVUELVE (intra-control,
    // más apretada que rowGap: las 2 hileras son el MISMO control).
    private const int SegmentWrapGap = Spacing.Sm;

    // -------- TogglePill: cápsula+knob dibujada (sustituye ☑/☐) sobre rejilla 8pt --------
    // Track de 36×20 (múltiplos de 4) con knob circular ligeramente menor que el alto del track.
    // T11: px de diseño escalados al DPI vigente (la pill crecía menos que el texto y se descentraba).
    private static int PillTrackW => Dpi.Scale(36);
    private static int PillTrackH => Dpi.Scale(20);
    private static int PillKnobInset => Dpi.Scale(2);          // holgura del knob dentro del track
    private static int PillKnobDiameter => PillTrackH - PillKnobInset * 2;

    /// <summary>
    /// Encabezado de sección estilo Apple: caption en MAYÚSCULAS, tenue (<c>TextMuted</c>, más pequeña
    /// que el body), con un Divider de 1px (<c>Theme.Separator</c>) debajo. Reserva <c>Spacing.Md</c> de
    /// aire arriba y <c>Spacing.Sm</c> abajo. Mide==pinta: el avance es idéntico en ambas pasadas.
    /// </summary>
    internal static int SectionHeader(Graphics g, bool draw, string title, int x, int y, int w, Theme theme, Font f)
    {
        y += SectionGap; // aire arriba (separa del grupo anterior): sectionGap (~Xl), ritmo "Apple"
        int textH = (int)Math.Ceiling(g.MeasureString(title, f).Height);
        if (draw)
        {
            using var b = new SolidBrush(theme.TextMuted);
            g.DrawString(title.ToUpperInvariant(), f, b, x, y);
        }
        y += textH;
        if (draw)
        {
            // Divisor 1px dentro de [x, x+w], centrado en el aire inferior.
            int dy = y + Spacing.Sm / 2;
            using var pen = new Pen(theme.Separator, 1);
            g.DrawLine(pen, x, dy, x + w, dy);
        }
        return y + Spacing.Sm; // aire abajo (separa de la primera fila)
    }

    // -------- DrawDependent: sub-ajuste dependiente de un master (indent + atenuación + inercia) --------

    /// <summary>
    /// Atenúa un tema a ~0.5 hacia su fondo (para dibujar dependientes "apagados" cuando el master está
    /// off). PURO: interpola por canal los tokens de texto/acento/separador/elevado hacia
    /// <c>Background</c>; la geometría de las filas NO depende del color, así que medir==pintar se
    /// mantiene aunque el dependiente cambie de tema según el estado del master.
    /// </summary>
    private static Theme Dimmed(Theme t)
    {
        const double k = 0.5; // ~50% de opacidad sobre el fondo
        Color D(Color c) => ColorMath.Lerp(c, t.Background, k);
        return new Theme
        {
            Id = t.Id,
            Background = t.Background,
            Foreground = D(t.Foreground),
            Dim = D(t.Dim),
            Track = D(t.Track),
            Ok = D(t.Ok),
            Warn = D(t.Warn),
            Critical = D(t.Critical),
            Neutral = D(t.Neutral),
            Accent = D(t.Accent),
            AccentTextOverride = D(t.AccentText),
            BgElevated = D(t.BgElevated),
            TextMuted = D(t.TextMuted),
            Separator = D(t.Separator),
        };
    }

    /// <summary>
    /// Dibuja un sub-ajuste DEPENDIENTE de un control maestro: sangrado <c>Spacing.Lg</c> (indent), y
    /// cuando el master está <b>off</b> (<paramref name="active"/>=false) se pinta <b>atenuado</b>
    /// (~0.5 vía <see cref="Dimmed"/>) e <b>inerte</b> (sus rects NO quedan registrados → un clic no
    /// muta nada). La sangría reduce el ancho útil a <c>w - Spacing.Lg</c>. El <paramref name="drawInner"/>
    /// recibe el x sangrado, el ancho reducido y el tema efectivo, y devuelve el nuevo <c>y</c>; el
    /// avance es IDÉNTICO esté el master on u off (solo cambian color e inercia, no la geometría), de
    /// modo que medir==pintar y nada se mueve al activar/desactivar el master. Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int DrawDependent(bool active, int x, int y, int w, Theme theme,
        Dictionary<string, Rectangle> rects, Func<int, int, Theme, int> drawInner)
    {
        int ix = x + Spacing.Lg;        // sangría del dependiente
        int iw = w - Spacing.Lg;        // ancho útil reducido por la sangría
        Theme eff = active ? theme : Dimmed(theme);
        // Snapshot de claves para poder retirar las nuevas (inercia) si el master está off.
        var before = active ? null : new HashSet<string>(rects.Keys);
        int ny = drawInner(ix, iw, eff);
        if (!active && before is not null)
            foreach (var k in rects.Keys.Where(k => !before.Contains(k)).ToList())
                rects.Remove(k);        // inerte: el dependiente no dispara mutación con el master off
        return ny;
    }

    /// <summary>Prefijo de las claves de las filas Pro bloqueadas («special:getpro:music»…): la bandeja abre la tarjeta del Pro.</summary>
    public const string GetProKeyPrefix = "special:getpro:";

    /// <summary>
    /// Dependiente de una función Pro. Con ella desbloqueada (<paramref name="unlocked"/>, de <see cref="ProGate"/>), igual que
    /// <see cref="DrawDependent"/>. Sin ella: la fila atenuada e inerte (su ajuste no haría nada), un chip «Pro» con el acento a
    /// la derecha y la fila entera como enlace a la tarjeta del Pro (<see cref="GetProKeyPrefix"/> + <paramref name="feature"/>;
    /// con el maestro apagado, también inerte). La fila se dibuja un chip más estrecha: su texto envuelve antes y nunca pasa
    /// bajo el chip. Medir==pintar (el ancho del chip se mide igual en ambas pasadas).
    /// </summary>
    internal static int ProDependent(bool active, bool unlocked, string feature, Graphics g, bool draw, int x, int y, int w,
        Theme theme, Strings s, Font chipFont, Dictionary<string, Rectangle> rects, Func<int, int, Theme, int> drawInner)
    {
        if (unlocked) return DrawDependent(active, x, y, w, theme, rects, drawInner);
        string text = s.NotchProChip;
        int chipW = (int)Math.Ceiling(g.MeasureString(text, chipFont).Width) + SegPadX * 2;
        int chipH = Math.Min(SegmentHeight, PillTrackH);
        int ny = DrawDependent(false, x, y, w - chipW - Spacing.Sm, theme, rects, drawInner);
        int rowH = Math.Max(1, ny - y - RowGap);
        var chip = new Rectangle(x + w - Spacing.Sm - chipW, y + Math.Max(0, (Math.Min(rowH, PillTrackH) - chipH) / 2), chipW, chipH);
        if (draw)
        {
            var t = active ? theme : Dimmed(theme);
            using var bg = new SolidBrush(t.Accent);
            Shapes.FillRounded(g, bg, chip, 4);
            using var tb = new SolidBrush(ColorMath.Contrast(t.Accent));
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, chipFont, tb, chip, sf);
        }
        if (active) rects[GetProKeyPrefix + feature] = new Rectangle(x + Spacing.Lg, y, w - Spacing.Lg, rowH);
        return ny;
    }

    /// <summary>
    /// Centro X del knob del TogglePill dado el rect del track y el estado. Helper PURO (geometría,
    /// sin dibujo) para test e implementación: knob a la izquierda si OFF, a la derecha si ON.
    /// </summary>
    internal static int PillKnobCenterX(Rectangle track, bool on)
    {
        int rad = PillKnobDiameter / 2;
        int left = track.X + PillKnobInset + rad;
        int right = track.Right - PillKnobInset - rad;
        return on ? right : left;
    }

    /// <summary>
    /// Cápsula con knob deslizante (estilo iOS), dibujada a mano (GDI+), anclada por su borde DERECHO a
    /// <paramref name="rightX"/> con margen interno de seguridad <c>Spacing.Sm</c>. Track <c>Theme.Accent</c>
    /// cuando ON y <c>Theme.Separator</c> cuando OFF; knob circular claro a izquierda (OFF) / derecha (ON).
    /// Sustituye los glifos Unicode ☑/☐. Devuelve el rect del track (idéntico en medir y pintar).
    /// </summary>
    internal static Rectangle TogglePill(Graphics g, bool draw, bool on, int rightX, int y, int rowH, Theme theme)
    {
        // Anclado a la derecha con margen interno ≥ Spacing.Sm; centrado verticalmente en la fila.
        int tx = rightX - Spacing.Sm - PillTrackW;
        int ty = y + (rowH - PillTrackH) / 2;
        var track = new Rectangle(tx, ty, PillTrackW, PillTrackH);
        if (draw)
        {
            using (var bg = new SolidBrush(on ? theme.Accent : theme.Separator))
                Shapes.FillRounded(g, bg, track, PillTrackH / 2);
            int d = PillKnobDiameter;
            int cx = PillKnobCenterX(track, on);
            var knob = new Rectangle(cx - d / 2, track.Y + PillKnobInset, d, d);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // T9b (§3 #11): knob BLANCO con borde sutil en ambos estados y los 3 temas (estándar
            // iOS/Fluent). Antes: TextPrimary en OFF (negro sobre el track claro = "agujero
            // perforado") y Contrast(Accent) en ON (negro sobre el verde CLI).
            using (var kb = new SolidBrush(theme.KnobFill))
                g.FillEllipse(kb, knob);
            using (var kp = new Pen(theme.KnobBorder))
                g.DrawEllipse(kp, knob);
            g.SmoothingMode = sm;
        }
        return track;
    }

    // -------- StatusBadge: cápsula semántica de estado (Activas/Instalar) a la derecha de la fila --------
    // Padding interno horizontal del badge (reusa la geometría de chip de DrawSegments → un solo lenguaje).
    private const int BadgePadX = SegPadX;
    // Alto del badge sobre la rejilla 8pt (coincide con el alto de un chip de segmento, ya escalado).
    private static int BadgeHeight => SegmentHeight;
    private const int BadgeRadius = 4;

    /// <summary>
    /// Texto que el <see cref="StatusBadge"/> muestra realmente dentro del rango útil <c>[x, rightX]</c>:
    /// el texto original o, si no cabe, recortado por la cola con <b>elipsis medida</b>
    /// (<see cref="TextWrap.Ellipsize"/>). PURO y determinista (misma medición en draw=false/true → mismo
    /// resultado, clave para medir==pintar). El ancho útil de texto descuenta el padding del badge a ambos
    /// lados y el margen de seguridad derecho <c>Spacing.Sm</c>; nunca invade a la izquierda de <paramref name="x"/>.
    /// </summary>
    internal static string StatusBadgeShownText(Graphics g, string text, int x, int rightX, Font f)
    {
        text ??= string.Empty;
        // Espacio disponible para el badge completo: desde contentLeft (x) hasta rightX con margen Sm.
        int maxBadgeW = (rightX - Spacing.Sm) - x;
        int maxTextW = maxBadgeW - BadgePadX * 2;
        if (maxTextW <= 0) return TextWrap.Ellipsis;
        return TextWrap.Ellipsize(text, maxTextW, t => g.MeasureString(t, f).Width);
    }

    /// <summary>
    /// Badge semántico de estado (mini RoundedRect + texto de 1 línea centrado), anclado por su borde
    /// DERECHO a <paramref name="rightX"/> con margen interno de seguridad <c>Spacing.Sm</c>. Relleno con
    /// el color semántico dado (<c>Theme.Ok</c> "Activas" / <c>Theme.Warn</c> "Instalar"); el texto usa
    /// <see cref="ColorMath.Contrast"/> del relleno y se recorta por la cola (<see cref="StatusBadgeShownText"/>)
    /// para nunca rebasar su ancho. El badge nunca empieza a la izquierda de <paramref name="x"/>
    /// (contentLeft). Centrado verticalmente en la fila de alto <paramref name="rowH"/>. Devuelve el rect del
    /// badge (idéntico en medir y pintar: la decisión de recorte es la misma en ambas pasadas).
    /// </summary>
    internal static Rectangle StatusBadge(Graphics g, bool draw, string text, Color color,
        int x, int rightX, int y, int rowH, Theme theme, Font f)
    {
        string shown = StatusBadgeShownText(g, text, x, rightX, f);
        int textW = (int)Math.Ceiling(g.MeasureString(shown, f).Width);
        int badgeW = textW + BadgePadX * 2;
        int rightEdge = rightX - Spacing.Sm;             // margen derecho de seguridad
        int bx = rightEdge - badgeW;
        if (bx < x) { bx = x; badgeW = Math.Max(0, rightEdge - bx); } // clamp izquierdo a contentLeft
        int by = y + (rowH - BadgeHeight) / 2;            // centrado vertical en la fila
        var badge = new Rectangle(bx, by, badgeW, BadgeHeight);
        if (draw && badgeW > 0)
        {
            using (var bg = new SolidBrush(color))
                Shapes.FillRounded(g, bg, badge, BadgeRadius);
            using var tb = new SolidBrush(ColorMath.Contrast(color));
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString(shown, f, tb, badge, sf);
        }
        return badge;
    }

    /// <summary>
    /// Fila MAESTRA de una integración (p.ej. "Activar sesiones en vivo" = instalar/quitar hooks): título
    /// (<c>labelFont</c>/<c>TextPrimary</c>) + subtítulo opcional debajo (<c>smallFont</c>/<c>TextMuted</c>) a la
    /// izquierda + <see cref="StatusBadge"/> semántico a la DERECHA (verde "Activas" / ámbar "Instalar"). A
    /// diferencia de <see cref="ToggleRow"/>, NO lleva pill: la fila completa es clicable y su clave es
    /// normalmente "special:*" (la enruta el host: instalar/quitar hooks con confirmación). El hit-test es el
    /// rect COMPLETO de la fila. Mide==pinta: la geometría del badge se decide igual en ambas pasadas, así que
    /// el rect y el <c>y</c> de salida son idénticos. Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int MasterRowWithBadge(Graphics g, bool draw, string key, string label, string? subtitle,
        string badgeText, Color badgeColor, int x, int y, int w, Theme theme, Font labelFont, Font smallFont,
        Dictionary<string, Rectangle> rects)
    {
        // El badge se ancla a la derecha; su geometría se decide igual en ambas pasadas (medir==pintar).
        // Lo medimos PRIMERO (sin dibujar todavía) para conocer el borde izquierdo del badge: la columna de
        // texto envuelve dentro de [x, badge.X - Spacing.Md], NUNCA por debajo del badge.
        var badge = StatusBadge(g, draw: false, badgeText, badgeColor, x, x + w, y, BadgeHeight, theme, smallFont);
        // ANTI-CORTE (P0 #2): el texto NO se elide. La columna de texto izquierda mide su ancho útil hasta
        // el borde izquierdo del badge (gutter Spacing.Md) y, si no cabe en 1 línea, ENVUELVE a varias líneas
        // (WordBreak), sumando su alto en AMBAS pasadas → medir==pintar. Yovan quiere el TEXTO COMPLETO.
        int textColW = (badge.Width > 0 ? badge.X : x + w) - Spacing.Md - x;
        if (textColW <= 0) textColW = Math.Max(1, x + w - x); // defensa: nunca ≤ 0

        var labelLines = TextWrap.WordWrap(label, textColW, t => g.MeasureString(t, labelFont).Width);
        int titleLineH = (int)Math.Ceiling(g.MeasureString(label, labelFont).Height);
        int titleH = labelLines.Count * titleLineH;

        bool hasSub = !string.IsNullOrEmpty(subtitle);
        var subLines = hasSub
            ? TextWrap.WordWrap(subtitle!, textColW, t => g.MeasureString(t, smallFont).Width)
            : new List<string>();
        int subLineH = hasSub ? (int)Math.Ceiling(g.MeasureString(subtitle!, smallFont).Height) : 0;
        int subH = subLines.Count * subLineH;

        int contentH = Math.Max(BadgeHeight, titleH + subH);

        var r = new Rectangle(x, y, w, contentH);
        rects[key] = r;
        if (draw)
        {
            // Re-emite el badge en la pasada de pintado (mismo rect que el medido → idéntico).
            StatusBadge(g, draw: true, badgeText, badgeColor, x, x + w, y, contentH, theme, smallFont);
            int ly = y;
            using (var b = new SolidBrush(theme.TextPrimary))
                foreach (var line in labelLines) { g.DrawString(line, labelFont, b, x, ly); ly += titleLineH; }
            if (hasSub)
                using (var sb = new SolidBrush(theme.TextMuted))
                    foreach (var line in subLines) { g.DrawString(line, smallFont, sb, x, ly); ly += subLineH; }
        }
        return y + contentH + RowGap;
    }

    /// <summary>
    /// Sección "SESIONES EN VIVO": cabecera + fila MAESTRA en POSITIVO (P1 #1) = toggle-pill
    /// (<c>special:hooktoggle</c>) cuyo estado ON refleja <paramref name="hooksInstalled"/> (=activadas).
    /// El label es "Activadas" (<c>s.Enabled</c>, mismo patrón que la fila maestra de NOTIFICACIONES; NO
    /// duplica el texto de la cabecera "SESIONES EN VIVO" ni la antigua doble negación "Desactivar (quitar
    /// hooks)") y el subtítulo gris explica que activar/desactivar instala/quita los hooks en
    /// <c>~/.claude/settings.json</c>. <b>Sin StatusBadge</b>: el toggle ya comunica el estado, así que la
    /// pill NO compite con un segundo control "Activas/Instalar" (elimina el "cosas dobles"). El clic enruta
    /// a <c>special:hooktoggle</c> (el host pide confirmación antes de tocar la config global). Debajo, los
    /// DEPENDIENTES (Mostrar mascota, Tamaño, Silenciar) vía <see cref="DrawDependent"/>: sangrados
    /// <c>Spacing.Lg</c> y, mientras los hooks NO están instalados, atenuados (~0.5) e INERTES (sus rects no
    /// se registran → un clic no muta nada). La fila maestra SIEMPRE responde (es la que instala los hooks).
    /// Esto mantiene el huérfano resuelto: "Mostrar mascota" depende visiblemente del master. La geometría no
    /// depende de <paramref name="hooksInstalled"/> (solo color + inercia + estado del pill) → medir==pintar
    /// y el avance es idéntico esté instalado o no. <paramref name="hooksInstalled"/> se inyecta para testear
    /// ambas ramas (el <see cref="Draw"/> pasa <c>HookInstaller.IsInstalled()</c>).
    /// </summary>
    /// <summary>
    /// v0.4 — subtítulo de la fila «Leer la cuota desde Claude Code»: edad del último snapshot si está activa y
    /// fresca, «esperando una sesión» si está activa sin datos, «pregunta a la API» si está apagada (o «actívalo para
    /// ver la cuota» si la lectura con la sesión también lo está: entonces no queda otro origen).
    /// </summary>
    internal static string TapSubtitle(TapStatus status, DateTime? lastSeenUtc, DateTime nowUtc, Strings s,
        bool apiOn = true) => status switch
    {
        TapStatus.Active when lastSeenUtc is { } t => string.Format(s.TapActiveFmt, TapAge(nowUtc - t)),
        TapStatus.Active or TapStatus.Waiting => s.TapWaiting,
        TapStatus.Off => apiOn ? s.TapOff : s.TapOffNoApi,
        _ => s.TapUnavailable,
    };

    // Minutos, no segundos: Draw se llama en la pasada de medir y en la de pintar con relojes distintos, y un
    // «9 s» → «10 s» entre ambas podía cambiar el ajuste de línea (medir≠pintar). «Activa» es < 10 min → ≤ 6 caracteres.
    private static string TapAge(TimeSpan d) =>
        d.TotalMinutes < 1 ? "<1 min" : $"{(int)d.TotalMinutes} min";

    /// <summary>
    /// v0.4 — sección «ORIGEN DE LA CUOTA»: fila MAESTRA (<c>special:statuslinetoggle</c>, el host pide confirmación
    /// porque toca ~/.claude/settings.json) con el estado vivo en el subtítulo. Sin helper en esta instalación la fila
    /// se pinta atenuada e inerte (DrawDependent inactivo). Debajo, «Leer la cuota con tu sesión de Claude Code (no
    /// oficial)» con su explicación (qué hace con el token y que Anthropic no lo documenta) y, dependiente de ella,
    /// «Forzar renovación con la CLI» (gasta un mensaje; atenuada e inerte con la lectura por la sesión apagada).
    /// Medir==pintar: la geometría no depende de draw, y apagar la lectura por la sesión no mueve nada (la fila de la CLI
    /// solo se atenúa).
    /// </summary>
    internal static int QuotaSourceSection(Graphics g, bool draw, TapStatus status, DateTime? lastSeenUtc, DateTime nowUtc,
        int x, int y, int w, AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont,
        Dictionary<string, Rectangle> rects)
    {
        y = SectionHeader(g, draw, s.QuotaSourceHeader, x, y, w, theme, smallFont);
        bool on = status is TapStatus.Active or TapStatus.Waiting;
        string sub = TapSubtitle(status, lastSeenUtc, nowUtc, s, cfg.QuotaOAuth);
        if (status == TapStatus.Unavailable)
        {
            int yy = y;
            y = DrawDependent(false, x, yy, w, theme, rects,
                (ix, iw, th) => MasterToggleRow(g, draw, "special:statuslinetoggle", s.QuotaFromClaudeCode, sub, false,
                    ix, yy, iw, th, labelFont, smallFont, rects));
        }
        else
            y = MasterToggleRow(g, draw, "special:statuslinetoggle", s.QuotaFromClaudeCode, sub, on,
                x, y, w, theme, labelFont, smallFont, rects);

        y = ToggleRow(g, draw, "toggle:QuotaOAuth", s.QuotaOAuthLabel, s.QuotaOAuthSubtitle, cfg.QuotaOAuth,
            x, y, w, theme, labelFont, smallFont, rects);
        int cliY = y;
        return DrawDependent(cfg.QuotaOAuth, x, cliY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:QuotaCliRefresh", s.QuotaCliRefreshLabel, s.QuotaCliRefreshSubtitle,
                cfg.QuotaCliRefresh, ix, cliY, iw, th, labelFont, smallFont, rects));
    }

    internal static int LiveSessionsSection(Graphics g, bool draw, bool hooksInstalled, int x, int y, int w,
        AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects)
    {
        y = SectionHeader(g, draw, s.MenuLiveSessions, x, y, w, theme, smallFont);
        // Fila maestra POSITIVA: toggle-pill (ON = activadas). El label es "Activadas" (mismo patrón que la
        // fila maestra de NOTIFICACIONES: cabecera = nombre de la feature, fila = on/off) para NO duplicar el
        // texto de la cabecera "SESIONES EN VIVO" (invariante: sin cosas dobles). El subtítulo gris explica que
        // activar/desactivar instala/quita hooks (acción delicada → confirmación en el host). Sin StatusBadge:
        // el toggle ya comunica el estado.
        y = MasterToggleRow(g, draw, "special:hooktoggle",
            s.Enabled, s.LiveSessionsSubtitle, hooksInstalled,
            x, y, w, theme, labelFont, smallFont, rects);
        // Dependientes: atenuados + inertes mientras no haya hooks (la mascota Idle solo cobra vida con hooks).
        // MASCOTA = toggle simple "Mostrar mascota" (v0.3.7): la talla grande se retiró, así que la
        // decisión vuelve a ser binaria (oculta / gatito compacto) y el control de 3 estados sobra.
        int mascotY = y;
        y = DrawDependent(hooksInstalled, x, mascotY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:ShowMascot", s.MenuShowMascot, null,
                cfg.ShowMascot, ix, mascotY, iw, th, labelFont, smallFont, rects));
        int supY = y;
        y = DrawDependent(hooksInstalled, x, supY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:Suppress", s.MenuSuppressWhenFocused, null,
                cfg.SuppressWhenFocused, ix, supY, iw, th, labelFont, smallFont, rects));
        return y;
    }

    /// <summary>
    /// Sección «NOTCH»: fila maestra (toggle simple, sin confirmación: no toca nada fuera de la app) y sus
    /// dependientes sangrados — tamaño, abrir con, monitor, modo mínimo, ocultar en pantalla completa, sonido, reloj, mascota,
    /// música, letras (además inertes sin música), tareas, el pomodoro (su sonido y, un nivel más dentro, sus duraciones, Pro),
    /// bandeja, contraste y permisos —, atenuados e inertes mientras el
    /// notch esté apagado. Geometría idéntica en ambos estados (medir==pintar). Sin el Pro (<paramref name="pro"/>, decidido con
    /// <see cref="ProGate"/>), monitores, música, tareas, bandeja y permisos llevan el chip «Pro» y abren su tarjeta, y lo que
    /// cuelga de ellos (monitor fijo, letras) queda inerte: activarlos no haría nada.
    /// </summary>
    internal static int NotchSection(Graphics g, bool draw, int x, int y, int w, AppConfig cfg, Strings s, Theme theme,
        Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects, ProState pro = ProState.Pro)
    {
        bool monitors = ProGate.IsUnlocked(ProFeature.MultiMonitor, pro);
        bool musicPro = ProGate.IsUnlocked(ProFeature.Music, pro);
        y = SectionHeader(g, draw, s.NotchHeader, x, y, w, theme, smallFont);
        y = MasterToggleRow(g, draw, "toggle:NotchEnabled", s.Enabled, s.NotchSubtitle, cfg.NotchEnabled,
            x, y, w, theme, labelFont, smallFont, rects);
        bool on = cfg.NotchEnabled;
        var sizes = AppConfig.NotchSizeOptions
            .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v == 0 ? s.NotchSizeAuto : $"{v}%"))
            .ToArray();
        int sizeY = y;
        y = DrawDependent(on, x, sizeY, w, theme, rects,
            (ix, iw, th) => SegmentedRow(g, draw, "notchsize", s.NotchSize, sizes,
                cfg.NotchSizePercent.ToString(System.Globalization.CultureInfo.InvariantCulture), ix, sizeY, iw, th, smallFont, rects));
        int openY = y;
        y = DrawDependent(on, x, openY, w, theme, rects,
            (ix, iw, th) => SegmentedRow(g, draw, "notchopen", s.NotchOpenWith,
                new[] { ("hover", s.NotchOpenHover), ("click", s.NotchOpenClick) },
                cfg.NotchOpenOnClick ? "click" : "hover", ix, openY, iw, th, smallFont, rects));
        // Monitor: fijo, siguiendo a la ventana activa o uno en cada monitor; el monitor concreto, un nivel más dentro, solo
        // cuenta en Fijo (en los otros, atenuado e inerte sin mover la geometría).
        int monModeY = y;
        y = ProDependent(on, monitors, "monitors", g, draw, x, monModeY, w, theme, s, smallFont, rects,
            (ix, iw, th) => SegmentedRow(g, draw, "notchmonmode", s.NotchMonitor,
                new[] { ("fixed", s.NotchMonitorModeFixed), ("follow", s.NotchMonitorModeFollow), ("all", s.NotchMonitorModeAll) },
                MonitorModeKey(cfg.NotchMonitorMode), ix, monModeY, iw, th, smallFont, rects));
        int monY = y;
        y = DrawDependent(on && monitors && cfg.NotchMonitorMode == NotchMonitorMode.Fixed, x + Spacing.Lg, monY, w - Spacing.Lg, theme, rects,
            (ix, iw, th) => CycleRow(g, draw, "cycle:notchmonitor", s.NotchMonitorFixed, MonitorLabel(cfg.NotchMonitor, CurrentMonitors(), s),
                ix, monY, iw, th, smallFont, rects));
        int minY = y;
        y = DrawDependent(on, x, minY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchMinimal", s.NotchMinimal, null, cfg.NotchMinimal,
                ix, minY, iw, th, labelFont, smallFont, rects));
        int autoY = y;
        y = DrawDependent(on && !cfg.NotchMinimal, x, autoY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchAutoMinimal", s.NotchAutoMinimal, null,
                cfg.NotchAutoMinimalSeconds > 0, ix, autoY, iw, th, labelFont, smallFont, rects));
        int fsY = y;
        y = DrawDependent(on, x, fsY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchHideFullscreen", s.NotchHideInFullscreen, null,
                cfg.NotchHideInFullscreen, ix, fsY, iw, th, labelFont, smallFont, rects));
        int sndY = y;
        y = DrawDependent(on, x, sndY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchSound", s.NotchSound, null, cfg.NotchSound,
                ix, sndY, iw, th, labelFont, smallFont, rects));
        // F5 (B8): reposo y páginas del expandido. Las letras dependen además de la música (sin ella no hay qué pedir).
        int clockY = y;
        y = DrawDependent(on, x, clockY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchClock", s.NotchClock, null, cfg.NotchClock,
                ix, clockY, iw, th, labelFont, smallFont, rects));
        int catY = y;
        y = DrawDependent(on, x, catY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchMascot", s.NotchMascot, null, cfg.NotchMascot,
                ix, catY, iw, th, labelFont, smallFont, rects));
        int musicY = y;
        y = ProDependent(on, musicPro, "music", g, draw, x, musicY, w, theme, s, smallFont, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchMusic", s.NotchMusic, null, cfg.NotchMusic,
                ix, musicY, iw, th, labelFont, smallFont, rects));
        int lyricsY = y;
        y = DrawDependent(on && musicPro && cfg.NotchMusic, x, lyricsY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchLyrics", s.NotchLyricsSetting, s.NotchLyricsSubtitle, cfg.NotchLyrics,
                ix, lyricsY, iw, th, labelFont, smallFont, rects));
        bool lyricsOn = on && musicPro && cfg.NotchMusic && cfg.NotchLyrics;
        // Letra en el notch pequeño, un nivel más dentro (depende de «Letras»), y sus dos ranuras, otro más (dependen de
        // ella). Como el resto de dependientes: atenuadas e inertes cuando no aplican, sin mover la geometría.
        int compactY = y;
        y = DrawDependent(lyricsOn, x + Spacing.Lg, compactY, w - Spacing.Lg, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchCompactLyrics", s.NotchCompactLyrics, s.NotchCompactLyricsSubtitle,
                cfg.NotchCompactLyrics, ix, compactY, iw, th, labelFont, smallFont, rects));
        bool compact = lyricsOn && cfg.NotchCompactLyrics;
        int compactArtY = y;
        y = DrawDependent(compact, x + 2 * Spacing.Lg, compactArtY, w - 2 * Spacing.Lg, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchCompactLyricsArt", s.NotchCompactLyricsArt, null,
                cfg.NotchCompactLyricsArt, ix, compactArtY, iw, th, labelFont, smallFont, rects));
        int compactQuotaY = y;
        y = DrawDependent(compact, x + 2 * Spacing.Lg, compactQuotaY, w - 2 * Spacing.Lg, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchCompactLyricsQuota", s.NotchCompactLyricsQuota, null,
                cfg.NotchCompactLyricsQuota, ix, compactQuotaY, iw, th, labelFont, smallFont, rects));
        int tasksY = y;
        y = ProDependent(on, ProGate.IsUnlocked(ProFeature.Tasks, pro), "tasks", g, draw, x, tasksY, w, theme, s, smallFont, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchTasks", s.NotchTasks, null, cfg.NotchTasks,
                ix, tasksY, iw, th, labelFont, smallFont, rects));
        // Pomodoro (página Foco): el aviso discreto del fin de fase es gratis; las duraciones, Pro (sin él, las de serie y el chip).
        int focusSndY = y;
        y = DrawDependent(on, x, focusSndY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchFocusSound", s.NotchFocusSoundSetting, null, cfg.NotchFocusSound,
                ix, focusSndY, iw, th, labelFont, smallFont, rects));
        bool focusPro = ProGate.IsUnlocked(ProFeature.NotchFocus, pro);
        var focus = focusPro ? cfg.NotchFocusDurations : FocusDurations.Default;
        foreach (var (key, label, options, value, minutes) in new (string, string, IReadOnlyList<int>, int, bool)[]
                 {
                     ("focusmin", s.NotchFocusLength, FocusDurations.FocusOptions, focus.FocusMinutes, true),
                     ("focusshort", s.NotchFocusShortLength, FocusDurations.ShortOptions, focus.ShortMinutes, true),
                     ("focuslong", s.NotchFocusLongLength, FocusDurations.LongOptions, focus.LongMinutes, true),
                     ("focusevery", s.NotchFocusLongEvery, FocusDurations.EveryOptions, focus.LongEvery, false),
                 })
        {
            int rowY = y;
            var segs = options.Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                minutes ? string.Format(s.Culture, s.NotchFocusMinutesFmt, v) : v.ToString(s.Culture))).ToArray();
            y = ProDependent(on, focusPro, "focus:" + key, g, draw, x + Spacing.Lg, rowY, w - Spacing.Lg, theme, s, smallFont, rects,
                (ix, iw, th) => SegmentedRow(g, draw, key, label, segs, value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ix, rowY, iw, th, smallFont, rects));
        }
        int shelfY = y;
        y = ProDependent(on, ProGate.IsUnlocked(ProFeature.Shelf, pro), "shelf", g, draw, x, shelfY, w, theme, s, smallFont, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchShelfClear", s.NotchShelfClearOnStart, null,
                cfg.NotchShelfClearOnStart, ix, shelfY, iw, th, labelFont, smallFont, rects));
        int contrastY = y;
        y = DrawDependent(on, x, contrastY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:NotchContrast", s.NotchAdaptiveContrast, null,
                cfg.NotchAdaptiveContrast, ix, contrastY, iw, th, labelFont, smallFont, rects));
        // F2: permisos desde el notch (opt-in). Toca ~/.claude/settings.json ⇒ clave «special:*»: el host pide
        // confirmación, hace backup y añade/quita la entrada «permission» del hook.
        int permY = y;
        y = ProDependent(on, ProGate.IsUnlocked(ProFeature.Permissions, pro), "permissions", g, draw, x, permY, w, theme, s,
            smallFont, rects,
            (ix, iw, th) => ToggleRow(g, draw, "special:notchpermtoggle", s.NotchPermissions, s.NotchPermissionsSubtitle,
                cfg.NotchPermissions, ix, permY, iw, th, labelFont, smallFont, rects));
        return y;
    }

    /// <summary>
    /// Fila MAESTRA con toggle-pill (P1 #1): título (<c>labelFont</c>/<c>TextPrimary</c>) + subtítulo gris
    /// debajo (<c>smallFont</c>/<c>TextMuted</c>) a la IZQUIERDA + <see cref="TogglePill"/> a la DERECHA cuyo
    /// estado refleja <paramref name="on"/>. A diferencia de <see cref="ToggleRow"/>, la columna de texto
    /// ENVUELVE (WordWrap) dentro de <c>[x, pill.X - Spacing.Md]</c> en lugar de pintar en una sola línea que
    /// se solaparía con el pill: así un subtítulo largo (p.ej. "Instala/quita hooks en ~/.claude/settings.json")
    /// se lee ENTERO en 2 líneas, NUNCA cortado ni elidido (invariante anti-corte de Yovan). Se usa para la
    /// fila maestra de "Sesiones en vivo" en POSITIVO (ON = activadas); su clave es normalmente "special:*"
    /// (la enruta el host con confirmación). El hit-test es el rect COMPLETO de la fila. Mide==pinta: el
    /// recuento de líneas (y por tanto el alto) es idéntico en ambas pasadas. Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int MasterToggleRow(Graphics g, bool draw, string key, string label, string? subtitle, bool on,
        int x, int y, int w, Theme theme, Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects)
    {
        // El pill se ancla a la derecha (mismo cálculo que TogglePill, sin dibujar todavía): su borde
        // izquierdo fija el ancho de la columna de texto (gutter Spacing.Md), así el texto NUNCA pasa por
        // debajo del pill. Medir==pintar: la geometría del pill no depende de draw.
        int pillLeft = (x + w) - Spacing.Sm - PillTrackW;
        int textColW = pillLeft - Spacing.Md - x;
        if (textColW <= 0) textColW = Math.Max(1, w); // defensa: nunca ≤ 0

        var labelLines = TextWrap.WordWrap(label, textColW, t => g.MeasureString(t, labelFont).Width);
        int titleLineH = (int)Math.Ceiling(g.MeasureString(label, labelFont).Height);
        int titleH = labelLines.Count * titleLineH;

        bool hasSub = !string.IsNullOrEmpty(subtitle);
        var subLines = hasSub
            ? TextWrap.WordWrap(subtitle!, textColW, t => g.MeasureString(t, smallFont).Width)
            : new List<string>();
        int subLineH = hasSub ? (int)Math.Ceiling(g.MeasureString(subtitle!, smallFont).Height) : 0;
        int subH = subLines.Count * subLineH;

        int contentH = Math.Max(PillTrackH, titleH + subH);

        var r = new Rectangle(x, y, w, contentH);
        rects[key] = r;
        if (draw)
        {
            int ly = y;
            using (var b = new SolidBrush(theme.TextPrimary))
                foreach (var line in labelLines) { g.DrawString(line, labelFont, b, x, ly); ly += titleLineH; }
            if (hasSub)
                using (var sb = new SolidBrush(theme.TextMuted))
                    foreach (var line in subLines) { g.DrawString(line, smallFont, sb, x, ly); ly += subLineH; }
            TogglePill(g, draw: true, on, x + w, y, contentH, theme);
        }
        return y + contentH + RowGap;
    }

    /// <summary>
    /// Fila de toggle estilo Apple: título (<c>labelFont</c>/<c>TextPrimary</c>) a la izquierda + subtítulo
    /// opcional debajo (<c>smallFont</c>/<c>TextMuted</c>) + <see cref="TogglePill"/> a la derecha (sin
    /// glifos Unicode). El hit-test es el rect COMPLETO de la fila (clic en cualquier punto).
    /// T8a (§3 #13): delega en <see cref="MasterToggleRow"/> — título y subtítulo ENVUELVEN dentro de
    /// <c>[x, pillLeft - Spacing.Md]</c> en vez de pintarse en una sola línea sin acotar que en DE/FR/NL
    /// pasaba por debajo del pill (invariante anti-corte: envolver, JAMÁS cortar). Con textos de 1 línea
    /// la geometría es idéntica a la histórica. Mide==pinta: el avance es igual en ambas pasadas.
    /// </summary>
    internal static int ToggleRow(Graphics g, bool draw, string key, string label, string? subtitle, bool on,
        int x, int y, int w, Theme theme, Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects)
        => MasterToggleRow(g, draw, key, label, subtitle, on, x, y, w, theme, labelFont, smallFont, rects);

    /// <summary>Fila de acción simple (etiqueta clicable, sin estado on/off). Registra rects[key].</summary>
    private static int ActionRow(Graphics g, bool draw, string key, string label,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        var r = new Rectangle(x, y, w, RowContentHeight);
        rects[key] = r;
        if (draw)
        {
            using var b = new SolidBrush(theme.TextPrimary);
            g.DrawString("› " + label, f, b, x, y);
        }
        return y + RowAdvance;
    }

    /// <summary>
    /// Geometría medida de un <see cref="InfoRow"/>: etiqueta izquierda (en <c>x</c>) y valor derecho
    /// (anclado a <c>x+w-Spacing.Sm</c>, YA elidido si no cabía). PURO (sin dibujo) para test e
    /// implementación: el valor nunca invade la etiqueta (gutter ≥ <c>Spacing.Md</c>) ni rebasa el margen
    /// derecho de seguridad (<c>Spacing.Sm</c>). Devuelve (labelX, labelW, valueX, valueW).
    /// </summary>
    internal static (int lx, int lw, int rx, int rw) InfoRowLayout(Graphics g, string label, string value,
        int x, int w, Font f)
    {
        int labelW = (int)Math.Ceiling(g.MeasureString(label, f).Width);
        int rightEdge = x + w - Spacing.Sm;            // margen derecho de seguridad
        int valueLeftBound = x + labelW + Spacing.Md;  // gutter mínimo etiqueta↔valor
        int maxValueW = Math.Max(0, rightEdge - valueLeftBound);
        string shown = ShownInfoValue(g, value, maxValueW, f);
        int valueW = (int)Math.Ceiling(g.MeasureString(shown, f).Width);
        if (valueW > maxValueW) valueW = maxValueW;    // defensa: nunca exceder
        int valueX = rightEdge - valueW;               // anclado a la derecha con margen
        if (valueX < valueLeftBound) valueX = valueLeftBound;
        return (x, labelW, valueX, valueW);
    }

    /// <summary>Valor que un <see cref="InfoRow"/> muestra realmente: el valor o, si no cabe en
    /// <paramref name="maxValueW"/>, recortado por la cola con elipsis MEDIDA. Determinista (misma medición
    /// en draw=false/true → mismo resultado, clave para medir==pintar).</summary>
    private static string ShownInfoValue(Graphics g, string value, int maxValueW, Font f)
    {
        value ??= string.Empty;
        if (maxValueW <= 0) return string.Empty;
        if ((int)Math.Ceiling(g.MeasureString(value, f).Width) <= maxValueW) return value;
        return TextWrap.Ellipsize(value, maxValueW, t => g.MeasureString(t, f).Width);
    }

    /// <summary>
    /// Fila INFORMATIVA (no clicable): etiqueta a la izquierda (<c>TextMuted</c>) + valor a la derecha
    /// (<c>TextPrimary</c>), p.ej. "Versión" / "v0.3.5". NO registra rect (no es accionable). El valor se
    /// elide con elipsis medida si no cabe (nunca solapa la etiqueta ni rebasa el margen derecho). Mide==pinta:
    /// la decisión de elidir es la misma en ambas pasadas → mismo <c>y</c> de salida. Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int InfoRow(Graphics g, bool draw, string label, string value,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        if (draw)
        {
            var (_, _, rx, _) = InfoRowLayout(g, label, value, x, w, f);
            using (var lb = new SolidBrush(theme.TextMuted))
                g.DrawString(label, f, lb, x, y);
            int rightEdge = x + w - Spacing.Sm;
            int labelW = (int)Math.Ceiling(g.MeasureString(label, f).Width);
            int maxValueW = Math.Max(0, rightEdge - (x + labelW + Spacing.Md));
            string shown = ShownInfoValue(g, value, maxValueW, f);
            using var vb = new SolidBrush(theme.TextPrimary);
            g.DrawString(shown, f, vb, rx, y);
        }
        return y + RowAdvance;
    }

    /// <summary>
    /// Nota INFORMATIVA (no clicable, no registra rect): texto pequeño en <c>TextMuted</c> partido en líneas dentro del ancho
    /// útil <c>[x, x+w-Spacing.Sm]</c>, sin elipsis. Mide==pinta: las líneas salen de la misma medición en las dos pasadas.
    /// Para el pie de «Acerca de» (qué es la app y el aviso de marca). Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int NoteRow(Graphics g, bool draw, string text, int x, int y, int w, Theme theme, Font f)
    {
        if (string.IsNullOrWhiteSpace(text)) return y;
        var lines = TextWrap.WordWrap(text, Math.Max(1, w - Spacing.Sm), t => g.MeasureString(t, f).Width);
        int lineH = (int)Math.Ceiling(f.GetHeight(g));
        if (draw)
        {
            using var b = new SolidBrush(theme.TextMuted);
            for (int i = 0; i < lines.Count; i++) g.DrawString(lines[i], f, b, x, y + i * lineH);
        }
        return y + Math.Max(1, lines.Count) * lineH + Spacing.Xs;
    }

    /// <summary>
    /// Botón destacado de ancho completo (relleno tenue + borde + texto del color de acento) para acciones
    /// que deben "verse como botón" y no como una fila más. Registra rects[key]. Avanza h+8.
    /// </summary>
    private static int ButtonRow(Graphics g, bool draw, string key, string label, Color accent,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        int h = Dpi.Scale(28); // T11: alto del botón escalado con el DPI
        var r = new Rectangle(x, y, w, h);
        rects[key] = r;
        if (draw)
        {
            using var path = Shapes.RoundedRectPath(new Rectangle(x, y, w - 1, h - 1), 7);
            using (var fill = new SolidBrush(theme.BgElevated))
                g.FillPath(fill, path);
            using (var pen = new Pen(accent, 1.5f))
                g.DrawPath(pen, path);
            using var tb = new SolidBrush(accent);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(label, f, tb, r, sf);
        }
        return y + h + RowGap;
    }

    /// <summary>
    /// Número de líneas (≥1) que el VALOR de un <see cref="CycleRow"/> ocupa al envolverse (WordBreak)
    /// dentro del ancho útil <c>[x, x+w-Spacing.Sm]</c>. PURO y determinista (misma medición en
    /// draw=false/true → mismo recuento, clave para medir==pintar). Sin elipsis: el valor SIEMPRE se
    /// muestra completo, partido en tantas líneas como haga falta.
    /// </summary>
    internal static int CycleRowValueLineCount(Graphics g, string current, int x, int w, Font f)
    {
        int valueW = Math.Max(1, w - Spacing.Sm); // ancho útil del valor (deja margen derecho ≥ Sm)
        return TextWrap.WordWrap(current ?? string.Empty, valueW, t => g.MeasureString(t, f).Width).Count;
    }

    /// <summary>
    /// Fila apilada: "Etiqueta" arriba (<c>TextPrimary</c>) + VALOR completo debajo en gris
    /// (<c>TextSecondary</c>), envuelto a 2+ líneas con WordBreak si no cabe — <b>NUNCA elidido ni
    /// recortado</b> (P0 #3: "Personalizada (arrastra el panel)" sale entero). SIN chevron ‹›: la
    /// afordancia de ciclar es el clic en toda la fila (rect completo). Un clic cicla al siguiente valor
    /// (la mutación la pone <see cref="ActionFor"/> con la misma clave <c>cycle:*</c>). Mide==pinta: el
    /// recuento de líneas del valor es idéntico en ambas pasadas, así que el rect y el <c>y</c> de salida
    /// coinciden. Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int CycleRow(Graphics g, bool draw, string key, string label, string current,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        int lineH = (int)Math.Ceiling(g.MeasureString(label, f).Height);
        int valueW = Math.Max(1, w - Spacing.Sm);
        var valueLines = TextWrap.WordWrap(current ?? string.Empty, valueW, t => g.MeasureString(t, f).Width);
        int contentH = lineH + valueLines.Count * lineH; // etiqueta + N líneas de valor

        var r = new Rectangle(x, y, w, contentH);
        rects[key] = r;
        if (draw)
        {
            using var fgb = new SolidBrush(theme.TextPrimary);
            using var dimb = new SolidBrush(theme.TextSecondary);
            g.DrawString(label, f, fgb, x, y);                 // etiqueta arriba
            int vy = y + lineH;
            foreach (var line in valueLines) { g.DrawString(line, f, dimb, x, vy); vy += lineH; } // valor completo
        }
        return y + contentH + RowGap;
    }

    // Geometría de los chips de segmento. El RELLENO/borde es el MISMO lenguaje que
    // DashboardDataView.DrawSegments (Accent activo + Contrast / BgElevated + borde Separator), pero la
    // SEPARACIÓN entre opciones sube a Spacing.Sm (más aire que el 3px de las tabs del chart) — P2 #2: las
    // afordancias del panel (segmented de Tema/Frecuencia/Icono/Mascota) comparten geometría y respiran.
    private const int SegGap = Spacing.Sm, SegPadX = 7;

    /// <summary>
    /// Etiqueta de una fila de segmentos ENVUELTA dentro de <c>[x, x+w-Spacing.Sm]</c> (T8a, §3 #13:
    /// antes una etiqueta DE/FR más ancha que la fila se pintaba en una sola línea sin acotar y rebasaba
    /// el borde del panel). Compartida por <see cref="SegmentedRow"/> y <see cref="MultiSegmentRow"/>.
    /// Devuelve <c>(labelRight, labelBlockH)</c>: con 1 línea, <c>labelRight = x + ancho + Md</c>
    /// (geometría histórica: los chips pueden ir a su derecha); con 2+ líneas, <c>labelRight = x + w</c>
    /// (nada cabe al lado → los chips caen SIEMPRE debajo del bloque) y <c>labelBlockH</c> es el alto
    /// del bloque envuelto. Determinista (misma decisión en draw=false/true) → medir==pintar.
    /// </summary>
    private static (int labelRight, int labelBlockH) SegmentRowLabel(Graphics g, bool draw, string? label,
        int x, int y, int w, Theme theme, Font f)
    {
        if (string.IsNullOrEmpty(label)) return (x, 0);
        var lines = TextWrap.WordWrap(label, w - Spacing.Sm, t => g.MeasureString(t, f).Width);
        int lineH = (int)Math.Ceiling(g.MeasureString(label, f).Height);
        if (draw)
        {
            using var b = new SolidBrush(theme.TextPrimary);
            int ly = y;
            foreach (var line in lines) { g.DrawString(line, f, b, x, ly); ly += lineH; }
        }
        return lines.Count == 1
            ? (x + (int)Math.Ceiling(g.MeasureString(label, f).Width) + Spacing.Md, lineH)
            : (x + w, lines.Count * lineH);
    }

    /// <summary>Ancho total (incl. gaps) de una fila de segmentos con las etiquetas dadas.</summary>
    private static int SegmentsTotalWidth(Graphics g, Font f, IReadOnlyList<string> labels)
    {
        if (labels.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < labels.Count; i++)
            total += (int)g.MeasureString(labels[i], f).Width + SegPadX * 2;
        return total + SegGap * (labels.Count - 1);
    }

    /// <summary>Avance de una fila de segmentos de UN renglón (para test del wrap a 2 filas).</summary>
    internal static int SegmentRowAdvanceForTest => SegmentRowAdvance;

    /// <summary>
    /// Fila con etiqueta opcional + segmentos a la derecha (mismo lenguaje de pill que el resto del panel:
    /// activo en Accent + texto Contrast, inactivo BgElevated + borde Separator). A diferencia del antiguo
    /// delegado a <see cref="DashboardDataView.DrawSegments"/> (gap 3px de las tabs del chart), aquí los
    /// chips se miden y pintan con el MISMO <see cref="SegGap"/> (=Spacing.Sm) → más aire entre opciones
    /// (P2 #2) y, sobre todo, la medición de "cabe/no cabe" usa exactamente la misma separación que el
    /// pintado (sin desajuste medir/pintar). ANTI-TRUNCAMIENTO: mide el ancho total en draw=false; si los
    /// segmentos NO caben en el espacio útil (<c>[labelRight+gutter, x+w-Spacing.Sm]</c>) <b>envuelve a 2
    /// filas</b> alineadas a <c>contentLeft</c>; si caben, se anclan a la derecha dejando margen ≥
    /// <c>Spacing.Sm</c>. Ningún chip se pinta con <c>x &lt; contentLeft</c>. La decisión (1 fila vs 2) es
    /// idéntica en medir/pintar → mismo <c>y</c> de salida. Cada segmento registra rects[$"{key}:{val}"].
    /// </summary>
    internal static int SegmentedRow(Graphics g, bool draw, string key, string label,
        (string val, string txt)[] segs, string active, int x, int y, int w, Theme theme, Font f,
        Dictionary<string, Rectangle> rects)
    {
        // T8a: la etiqueta envuelve dentro del ancho útil (con 2+ líneas los chips caen debajo del bloque).
        var (labelRight, labelBlockH) = SegmentRowLabel(g, draw, label, x, y, w, theme, f);
        int rightEdge = x + w - Spacing.Sm;            // margen derecho de seguridad

        int total = SegmentsTotalWidth(g, f, segs.Select(s => s.txt).ToList());
        int avail = rightEdge - Math.Max(labelRight, x);

        if (total <= avail)
        {
            // Cabe en un renglón: anclar a la derecha (deja margen ≥ Sm; primer chip ≥ labelRight ≥ x).
            DrawSelectChips(g, draw, key, segs, active, rightEdge - total, y, theme, f, rects);
            return y + SegmentRowAdvance;
        }

        // No cabe junto a la etiqueta. Si HAY etiqueta, los chips bajan a su PROPIO renglón (bajo el
        // bloque COMPLETO de la etiqueta) → NUNCA se solapan con su texto (regresión que aparecía al dar
        // más aire entre opciones: "Umbral de color" + 3 chips dejaban de caber a la derecha y se
        // pisaban). Mismo patrón que MultiSegmentRow. medir==pintar (misma decisión determinista en
        // ambas pasadas). Con etiqueta de 1 línea el avance histórico (SegmentHeight) se conserva.
        bool hasLabel = !string.IsNullOrEmpty(label);
        int chipsY = hasLabel ? y + Math.Max(SegmentHeight, labelBlockH) + SegmentWrapGap : y;
        int rowWidth = w; // ancho útil del renglón empezando en contentLeft
        if (total <= rowWidth - Spacing.Sm)
        {
            // Caben todos en un renglón propio anclados a la izquierda.
            DrawSelectChips(g, draw, key, segs, active, x, chipsY, theme, f, rects);
            return chipsY + SegmentRowAdvance;
        }
        // Ni en un renglón completo: repartir en las filas que hagan falta (normalmente 2), alineadas a contentLeft. Cada fila
        // se llena con lo que cabe en el ancho útil: con muchas opciones (las 7 duraciones del pomodoro) la segunda tampoco se
        // sale por la derecha.
        var rest = segs;
        int rows = 0;
        while (rest.Length > 0)
        {
            int split = SplitIndexForWrap(g, f, rest.Select(s => s.txt).ToList(), rowWidth - Spacing.Sm);
            DrawSelectChips(g, draw, key, rest.Take(split).ToArray(), active, x,
                chipsY + rows * (SegmentHeight + SegmentWrapGap), theme, f, rects);
            rest = rest.Skip(split).ToArray();
            rows++;
        }
        return chipsY + SegmentHeight * rows + SegmentWrapGap * (rows - 1) + RowGap;
    }

    /// <summary>
    /// Pinta y registra una hilera de chips de selección ÚNICA (single-select) empezando en
    /// <paramref name="startX"/>: el chip cuyo <c>val == active</c> va en Accent + <see cref="ColorMath.Contrast"/>,
    /// el resto en BgElevated + borde <see cref="Theme.Separator"/>. Geometría idéntica a
    /// <see cref="DrawMultiChips"/>/<see cref="SegmentsTotalWidth"/> (<see cref="SegGap"/>/<see cref="SegPadX"/>/
    /// <see cref="SegmentHeight"/>), de modo que medir==pintar y todo el panel comparte UN solo estilo de
    /// pill con la MISMA separación entre opciones. Cada chip registra <c>rects[$"{key}:{val}"]</c>.
    /// </summary>
    private static void DrawSelectChips(Graphics g, bool draw, string key, (string val, string txt)[] segs,
        string active, int startX, int y, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        int sx = startX;
        foreach (var (val, txt) in segs)
        {
            int chipW = (int)g.MeasureString(txt, f).Width + SegPadX * 2;
            var rect = new Rectangle(sx, y, chipW, SegmentHeight);
            bool on = val == active;
            if (draw)
            {
                using var bg = new SolidBrush(on ? theme.Accent : theme.BgElevated);
                Shapes.FillRounded(g, bg, rect, 4);
                if (!on)
                    using (var bd = new Pen(theme.Separator))
                        Shapes.DrawRounded(g, bd, rect, 4);
                using var tb = new SolidBrush(on ? ColorMath.Contrast(theme.Accent) : theme.TextPrimary);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(txt, f, tb, rect, sf);
            }
            rects[$"{key}:{val}"] = rect;
            sx += chipW + SegGap;
        }
    }

    /// <summary>
    /// Índice de corte para envolver a 2 filas: cuántos segmentos (≥1) caben en <paramref name="rowWidth"/>
    /// empezando en contentLeft. Determinista (misma medición en ambas pasadas).
    /// </summary>
    private static int SplitIndexForWrap(Graphics g, Font f, IReadOnlyList<string> labels, int rowWidth)
    {
        int count = 0, acc = 0;
        for (int i = 0; i < labels.Count; i++)
        {
            int wseg = (int)g.MeasureString(labels[i], f).Width + SegPadX * 2;
            int next = acc + (count > 0 ? SegGap : 0) + wseg;
            if (count > 0 && next > rowWidth) break;
            acc = next; count++;
        }
        return Math.Max(1, count); // al menos 1 por fila para no perder segmentos
    }

    /// <summary>
    /// Fila de segmentos con <b>MÚLTIPLES activos</b> (p.ej. hitos 25/50/75/95): a diferencia de
    /// <see cref="DashboardDataView.DrawSegments"/> (un solo activo), aquí <paramref name="active"/> es un
    /// conjunto y CADA valor presente se pinta en estado "on" con el MISMO lenguaje visual (Accent +
    /// <see cref="ColorMath.Contrast"/>), un único estilo de pill. Esto sustituye al re-pintado manual por
    /// encima de <c>DrawSegments</c>. Geometría de chip idéntica a <c>DrawSegments</c>
    /// (<c>SegGap</c>/<c>SegPadX</c>/<c>SegmentHeight</c>). Etiqueta opcional a la izquierda; segmentos
    /// anclados a la derecha dejando margen ≥ <c>Spacing.Sm</c> y nunca con <c>x &lt; contentLeft</c>. Cada
    /// segmento registra <c>rects[$"{key}:{val}"]</c>. Mide==pinta: el avance es idéntico en ambas pasadas
    /// (la geometría no depende de <paramref name="draw"/>). Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int MultiSegmentRow(Graphics g, bool draw, string key, string label,
        (string val, string txt)[] segs, IEnumerable<int> active, int x, int y, int w, Theme theme, Font f,
        Dictionary<string, Rectangle> rects)
    {
        // T8a: la etiqueta envuelve dentro del ancho útil (con 2+ líneas los chips caen debajo del bloque).
        var (labelRight, labelBlockH) = SegmentRowLabel(g, draw, label, x, y, w, theme, f);

        var activeSet = new HashSet<string>((active ?? Enumerable.Empty<int>())
            .Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        int rightEdge = x + w - Spacing.Sm;            // margen derecho de seguridad

        // ANTI-TRUNCAMIENTO (igual que SegmentedRow): mide el ancho total; si los chips NO caben en el
        // espacio útil a la derecha de la etiqueta ([labelRight, rightEdge]) → ENVUELVE a 2 filas
        // alineadas a contentLeft (ningún chip a la izquierda de x ni más allá de rightEdge). La decisión
        // (1 fila vs 2) es idéntica en medir/pintar → mismo y de salida.
        int total = SegmentsTotalWidth(g, f, segs.Select(s => s.txt).ToList());
        int avail = rightEdge - Math.Max(labelRight, x);

        if (total <= avail)
        {
            // Cabe en un renglón junto a la etiqueta: anclar a la derecha (deja margen ≥ Sm; primer chip
            // ≥ max(labelRight, x) ≥ contentLeft).
            DrawMultiChips(g, draw, key, segs, activeSet, Math.Max(rightEdge - total, Math.Max(labelRight, x)),
                y, theme, f, rects);
            return y + SegmentRowAdvance;
        }

        // No cabe junto a la etiqueta. Los chips bajan a su PROPIO renglón (bajo el bloque COMPLETO de
        // la etiqueta, si la hay), alineados a contentLeft → nunca se solapan con la etiqueta ni con el
        // borde. Desde ahí, si TODO el ancho de contenido tampoco basta, se reparten en 2 filas (split).
        // Ningún chip a la izquierda de x ni más allá de rightEdge. medir==pintar (misma decisión
        // determinista en ambas pasadas). Con etiqueta de 1 línea el avance histórico se conserva.
        bool hasLabel = !string.IsNullOrEmpty(label);
        int chipsY = hasLabel ? y + Math.Max(SegmentHeight, labelBlockH) + SegmentWrapGap : y; // chips bajo la etiqueta
        if (total <= w - Spacing.Sm)
        {
            // Caben todos en un renglón propio anclados a la izquierda.
            DrawMultiChips(g, draw, key, segs, activeSet, x, chipsY, theme, f, rects);
            return chipsY + SegmentRowAdvance;
        }
        // Ni en un renglón completo: repartir en 2 filas alineadas a contentLeft.
        int split = SplitIndexForWrap(g, f, segs.Select(s => s.txt).ToList(), w);
        DrawMultiChips(g, draw, key, segs.Take(split).ToArray(), activeSet, x, chipsY, theme, f, rects);
        DrawMultiChips(g, draw, key, segs.Skip(split).ToArray(), activeSet, x,
            chipsY + SegmentHeight + SegmentWrapGap, theme, f, rects);
        return chipsY + SegmentHeight * 2 + SegmentWrapGap + RowGap;
    }

    /// <summary>
    /// Pinta y registra una hilera de chips multi-activo (Accent si está en <paramref name="activeSet"/>,
    /// BgElevated si no) empezando en <paramref name="startX"/>. Geometría idéntica a
    /// <c>DrawSegments</c>/<see cref="SegmentsTotalWidth"/> (<c>SegGap</c>/<c>SegPadX</c>/<c>SegmentHeight</c>).
    /// Reutilizado por la rama de 1 fila y por cada renglón del wrap, de modo que medir==pintar y el estilo
    /// de pill es único. Cada chip registra <c>rects[$"{key}:{val}"]</c>.
    /// </summary>
    private static void DrawMultiChips(Graphics g, bool draw, string key, (string val, string txt)[] segs,
        HashSet<string> activeSet, int startX, int y, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        int sx = startX;
        foreach (var (val, txt) in segs)
        {
            int chipW = (int)g.MeasureString(txt, f).Width + SegPadX * 2;
            var rect = new Rectangle(sx, y, chipW, SegmentHeight);
            bool on = activeSet.Contains(val);
            if (draw)
            {
                using var bg = new SolidBrush(on ? theme.Accent : theme.BgElevated);
                Shapes.FillRounded(g, bg, rect, 4);
                // Mismo borde "track" del chip inactivo que DrawSegments → un único lenguaje de pill que
                // se lee como botón en todos los temas (P1 #3).
                if (!on)
                    using (var bd = new Pen(theme.Separator))
                        Shapes.DrawRounded(g, bd, rect, 4);
                using var tb = new SolidBrush(on ? ColorMath.Contrast(theme.Accent) : theme.TextPrimary);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(txt, f, tb, rect, sf);
            }
            rects[$"{key}:{val}"] = rect;
            sx += chipW + SegGap;
        }
    }
}
