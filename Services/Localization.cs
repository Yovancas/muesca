using System.Globalization;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services;

/// <summary>All user-facing strings for one language.</summary>
public sealed record Strings
{
    /// <summary>
    /// Cultura de FORMATO del idioma (números/moneda/fechas/días abreviados). T2 de la auditoría
    /// 2026-06-10: con Language=en la UI mezclaba locales ("$420,50", "jue 02:12") porque los format
    /// strings caían en la CurrentCulture del SO. Cada idioma fija una cultura representativa
    /// (en→en-US, es→es-ES…); con Language=system <see cref="Localization.ForConfig"/> la sustituye
    /// por la CurrentCulture del SO (ahí sí debe mandar la configuración regional del usuario).
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Los textos del tutorial interactivo (<see cref="TutorialStrings"/>), en su propio archivo.</summary>
    public TutorialStrings Tut { get; init; } = TutorialStrings.English;

    // Menu
    public string Dashboard { get; init; } = "Dashboard";
    public string Refresh { get; init; } = "Refresh now";
    public string PanelWindow { get; init; } = "Panel window";
    public string Position { get; init; } = "Position";
    public string PosBottomRight { get; init; } = "Bottom right";
    public string PosBottomLeft { get; init; } = "Bottom left";
    public string PosTopRight { get; init; } = "Top right";
    public string PosTopLeft { get; init; } = "Top left";
    public string PosCenter { get; init; } = "Center";
    public string PosCustom { get; init; } = "Custom (drag the panel)";
    public string Sticky { get; init; } = "Pinned (don't auto-close)";
    public string AlwaysOnTop { get; init; } = "Always on top";
    /// <summary>Toggle "reducir movimiento" (F3): colapsa toda animación a su estado final.</summary>
    public string ReduceMotion { get; init; } = "Reduce motion";
    /// <summary>Subtítulo de "Reducir movimiento" (T7): aclara que desactiva las animaciones.</summary>
    public string ReduceMotionSubtitle { get; init; } = "Turns off animations";
    public string UpdateFrequency { get; init; } = "Update frequency";
    // Etiquetas COMPACTAS estilo Apple para los segmentos de frecuencia (4 chips en una fila estrecha).
    // El formato largo cortaba el primer chip ("gundos"); ver T3 de la spec v0.3.5.
    public string Sec30 { get; init; } = "30s";
    public string Min1 { get; init; } = "1m";
    public string Min5 { get; init; } = "5m";
    public string Min15 { get; init; } = "15m";
    public string Notifications { get; init; } = "Notifications";
    public string Enabled { get; init; } = "Enabled";
    public string NotifyWhenReaching { get; init; } = "Alert threshold";
    public string ColorThreshold { get; init; } = "Color threshold";
    public string DefaultTag { get; init; } = "(default)";
    public string Settings { get; init; } = "Settings";
    public string ShowSpend { get; init; } = "Show estimated spend";
    /// <summary>Subtítulo de la fila "Mostrar gasto" (T6): aclara qué representa el coste mostrado.</summary>
    public string ShowSpendSubtitle { get; init; } = "API-equivalent cost per model";
    public string StartWithWindows { get; init; } = "Start with Windows";
    /// <summary>Subtítulo de «Iniciar con Windows» (edición de la Store) mientras se lee la tarea de inicio: desconocido, no apagado.</summary>
    public string StartupChecking { get; init; } = "Checking…";
    /// <summary>Subtítulo de «Iniciar con Windows» (edición de la Store) mientras se enciende o se apaga en segundo plano.</summary>
    public string StartupChanging { get; init; } = "Changing…";
    public string EditConfig { get; init; } = "Edit config (advanced)…";
    public string OpenDataFolder { get; init; } = "Open data folder";
    public string Language { get; init; } = "Language";
    public string SystemDefault { get; init; } = "System default";
    public string Exit { get; init; } = "Exit";

    // Dashboard
    public string SessionWord { get; init; } = "Session";
    public string WeekWord { get; init; } = "Week";
    public string ResetsIn { get; init; } = "resets in";
    public string Resetting { get; init; } = "resetting…";
    public string RollingHint { get; init; } = "rolling 5h window from your 1st request";
    public string SpendHeaderFormat { get; init; } = "Estimated spend ({0}d, API-equiv)";
    public string Loading { get; init; } = "Loading…";
    public string UpdatedAt { get; init; } = "Updated";
    /// <summary>{0} = elapsed text (e.g. "5 min"). "ago 5 min" / "hace 5 min".</summary>
    public string AgoFormat { get; init; } = "{0} ago";
    public string StaleLabel { get; init; } = "stale";
    public string LocalSeal { get; init; } = "Your credentials and data never leave this device";
    public string HintClickToHide { get; init; } = "click the icon to hide";
    public string HintPinnedClose { get; init; } = "pinned · ✕ to close";
    public string PreviousDataTip { get; init; } = "⚠ previous data (offline)";
    public string PreviousDataFooter { get; init; } = "previous data";
    /// <summary>Destello in-panel de celebración cuando una ventana de cuota se resetea (F3).</summary>
    public string QuotaRenewed { get; init; } = "quota renewed";

    // States
    public string StateNoCredentials { get; init; } = "Not signed in — log in to Claude Code";
    public string StateAuthExpired { get; init; } = "Session expired — open Claude Code";
    public string StateRateLimited { get; init; } = "Rate limited — retrying";
    public string StateNetworkError { get; init; } = "No connection to Anthropic";
    /// <summary>Solo statusline y aún sin ninguna lectura.</summary>
    public string StateStatuslineOnly { get; init; } = "No quota yet — open a Claude Code session";
    /// <summary>{0} = antigüedad ("12 min", "2 h"). Solo statusline, sin sesión abierta.</summary>
    public string LastReadingFmt { get; init; } = "Last reading {0} ago";

    // Notifications: {0} = milestone percent
    public string NotifQuotaFormat { get; init; } = "Claude {0}%+ quota used";

    // Theme
    public string Theme { get; init; } = "Theme";
    public string ThemeSystem { get; init; } = "System";
    public string ThemeDark { get; init; } = "Dark";
    public string ThemeLight { get; init; } = "Light";
    public string ThemeCli { get; init; } = "CLI";
    public string ThemeImported { get; init; } = "Imported";
    public string ImportTheme { get; init; } = "Import .itermcolors…";

    // Service health
    public string ShowServiceStatus { get; init; } = "Show service status";
    public string HealthOk { get; init; } = "Operational";
    public string HealthDegraded { get; init; } = "Degraded";
    public string HealthOutage { get; init; } = "Outage";

    // Dashboard sections (v0.3)
    public string SectionQuota { get; init; } = "Quota";
    public string SectionSessions { get; init; } = "Sessions";
    public string SectionSpend { get; init; } = "Spend";
    public string SectionChart { get; init; } = "Chart";

    // Settings panel (v0.3)
    public string Back { get; init; } = "Back";
    // (Las strings del tamaño de la mascota — MascotSize*/MascotLabel/Hidden/Compact/Large — se
    //  retiraron en v0.3.7: la mascota es un toggle simple que reutiliza MenuShowMascot.)

    // Chart & misc
    public string UsageChart { get; init; } = "Usage chart";
    public string NoData { get; init; } = "No data in this range";
    public string OpenBilling { get; init; } = "Open billing…";
    public string ChartPeak { get; init; } = "peak";
    public string ChartTotal { get; init; } = "total";
    public string ChartTabSpend { get; init; } = "Spend $";
    public string ChartTabPct { get; init; } = "Quota %";
    /// <summary>Etiqueta UI de la familia canónica <c>ModelFamily.Other</c> (T13a): ids de modelo sin
    /// familia reconocible y el cubo que agrupa a las familias menores cuando hay más de 4. Aparece
    /// en las filas de gasto y en la leyenda de la gráfica.</summary>
    public string ModelFamilyOther { get; init; } = "Other";
    /// <summary>Sufijo discreto de una fila de gasto cuya familia NO tiene tarifa en ninguna fuente
    /// del catálogo (T13b): la fila pinta "— &lt;sufijo&gt;" y NO suma al total (no mentir con $0).</summary>
    public string SpendNoRate { get; init; } = "no rate";
    /// <summary>Toggle de ajustes (Sistema): refrescar el catálogo de tarifas desde models.dev (T13b).</summary>
    public string PricingOnlineUpdate { get; init; } = "Update rates online";
    /// <summary>Subtítulo honesto del toggle: solo precios públicos, no sale nada del usuario.</summary>
    public string PricingOnlineUpdateSubtitle { get; init; } =
        "Downloads public prices from models.dev · sends nothing of yours";
    public string Opacity { get; init; } = "Opacity";

    // Pace
    public string IconMode { get; init; } = "Icon mode";
    public string PaceAlerts { get; init; } = "Pace alerts";
    public string WinSession { get; init; } = "session (5h)";
    public string WinWeekly { get; init; } = "weekly (7d)";
    public string PaceAlertTitle { get; init; } = "⚠ Quota pace";
    /// <summary>{0} = window name, {1} = ETA time.</summary>
    public string PaceAlertBodyFmt { get; init; } = "At this rate you'll run out of {0} quota by {1}, before the reset";
    /// <summary>Micro-rótulo del grupo ETA de la línea de pace (F6): precede a la hora absoluta del
    /// agotamiento ("⚠ empty &lt;hora&gt;") para que la hora no salga suelta sin contexto. Corto y en
    /// minúsculas: comparte fila con los % de ritmo.</summary>
    public string PaceEtaLabel { get; init; } = "empty";

    // Menu groups + updates
    public string MenuAppearance { get; init; } = "Appearance";
    /// <summary>Cabecera de la sección que agrupa qué se muestra en el panel (gasto/estado/gráfica).
    /// Renombrada de la vaga "Sections" a "Panel content" (P2 #3): el título dice de qué va el grupo.</summary>
    public string MenuSections { get; init; } = "Panel content";
    public string MenuIcon { get; init; } = "Icon";
    public string MenuAdvanced { get; init; } = "Advanced";
    /// <summary>Título de la sección SISTEMA del panel (T8): deja de estar hardcodeado.</summary>
    public string MenuSystem { get; init; } = "System";
    /// <summary>Etiqueta de la fila informativa de versión en la sección ACERCA DE (T8).</summary>
    public string VersionLabel { get; init; } = "Version";
    public string About { get; init; } = "About";
    /// <summary>«Acerca de» (Ajustes y bandeja): qué es la app, dicho de forma descriptiva, y quién la publica.</summary>
    public string AboutProduct { get; init; } = "Muesca, the notch for Claude Code · by Bekay";
    /// <summary>«Acerca de»: la app no es de Anthropic y «Claude» es su marca (docs/branding.md).</summary>
    public string AboutDisclaimer { get; init; } = "Not affiliated with Anthropic. Claude is a trademark of Anthropic.";
    public string CheckUpdates { get; init; } = "Check for updates";
    public string Checking { get; init; } = "Checking for updates…";
    /// <summary>{0} = latest version.</summary>
    public string UpdateAvailableFmt { get; init; } = "⬇ Update available {0}";
    /// <summary>{0} = current version.</summary>
    public string UpToDateFmt { get; init; } = "You're up to date (v{0}).";
    /// <summary>{0} = current, {1} = latest.</summary>
    public string UpdatePromptFmt { get; init; } = "Current version: v{0}\nLatest available: {1}\n\nDownload it now?";
    /// <summary>{0} = path.</summary>
    public string UpdateDownloadedFmt { get; init; } = "Downloaded to:\n{0}\n\nClose Muesca and replace the running .exe with this file.";
    public string UpdateFailed { get; init; } = "Couldn't check/download the update.";
    // Actualizaciones con Velopack (v0.5). Solo es/en; el resto de idiomas cae al inglés.
    /// <summary>Menú mientras se descarga; {0} = versión, {1} = porcentaje.</summary>
    public string UpdateDownloadingFmt { get; init; } = "Downloading v{0}… {1} %";
    /// <summary>Menú con la versión ya descargada; {0} = versión.</summary>
    public string UpdateRestartFmt { get; init; } = "Restart to update to v{0}";
    /// <summary>Aviso tras «Buscar actualizaciones» con versión nueva; {0} = versión.</summary>
    public string UpdateFoundFmt { get; init; } = "Version {0} is available. It's downloading in the background.";
    /// <summary>Aviso cuando termina esa descarga; {0} = versión.</summary>
    public string UpdateReadyFmt { get; init; } = "Version {0} is ready. Restart Muesca from the tray menu to update, or it will update the next time it closes.";
    /// <summary>Aviso si la comprobación o la descarga falla; {0} = ruta del log.</summary>
    public string UpdateFailedLogFmt { get; init; } = "Couldn't check for or download the update. Details in {0}";
    /// <summary>Aviso al volver a abrirse tras actualizar; {0} = versión.</summary>
    public string UpdatedFmt { get; init; } = "Muesca is now on version {0}.";

    // Live sessions / Mascot
    public string MenuLiveSessions { get; init; } = "Live sessions";
    /// <summary>Subtítulo de la fila maestra (P1 #1): la fila es ahora un toggle POSITIVO ("Sesiones en
    /// vivo" ON = activadas). El subtítulo gris explica qué hace activar/desactivar — instala/quita los
    /// hooks en la config global de Claude Code — en vez de la antigua doble negación del label. Sin
    /// elipsis: si no cabe en 1 línea, la fila lo ENVUELVE (anti-corte).</summary>
    public string LiveSessionsSubtitle { get; init; } = "Installs/removes hooks in ~/.claude/settings.json";
    /// <summary>Ajustes: el botón que quita de una vez lo de Muesca de settings.json (hooks y statusline).</summary>
    public string RemoveIntegration { get; init; } = "Remove Claude Code integration";
    /// <summary>La nota bajo el botón «Quitar la integración con Claude Code».</summary>
    public string RemoveIntegrationHint { get; init; } = "Takes Muesca's hooks and status line out of Claude Code's settings.json. Do it before uninstalling.";
    /// <summary>Confirmación del botón (qué se quita, que lo ajeno no se toca y que hay copia).</summary>
    public string RemoveIntegrationConfirm { get; init; } = "This removes only Muesca's entries from Claude Code's settings.json (~/.claude/settings.json): the live-session and permission hooks and the status line hook. Your own status line comes back exactly as it was, and nothing else is touched. A backup of settings.json is saved first.\n\nDo this before uninstalling Muesca. Continue?";
    /// <summary>Hecho; {0} = la copia de settings.json.</summary>
    public string RemoveIntegrationDoneFmt { get; init; } = "Claude Code integration removed. Backup of settings.json: {0}";
    /// <summary>No había nada de Muesca en settings.json.</summary>
    public string RemoveIntegrationNothing { get; init; } = "Claude Code's settings.json has no Muesca entries: there is nothing to remove.";
    public string MenuShowMascot { get; init; } = "Show mascot";
    public string MenuSuppressWhenFocused { get; init; } = "Mute when terminal focused";
    public string MenuInstallHooks { get; init; } = "Enable (install hooks)…";
    public string MenuUninstallHooks { get; init; } = "Disable (remove hooks)";
    public string NoActiveSessions { get; init; } = "No active sessions";
    public string SessionPhaseIdle { get; init; } = "idle";
    public string SessionPhaseProcessing { get; init; } = "working";
    public string SessionPhaseWaitingApproval { get; init; } = "waiting for OK";
    public string SessionPhaseWaitingInput { get; init; } = "your turn";
    public string SessionPhaseCompacting { get; init; } = "compacting";
    /// <summary>{0} = nombre del proyecto.</summary>
    public string NotifWaitingApprovalFmt { get; init; } = "Claude is waiting for your OK in {0}";
    /// <summary>{0} = nombre del proyecto.</summary>
    public string NotifWaitingInputFmt { get; init; } = "Claude finished in {0}";
    public string LiveSessionsTitle { get; init; } = "Claude sessions";
    /// <summary>{0} = ruta del backup de settings.json.</summary>
    public string HooksInstalledFmt { get; init; } = "Live sessions on. Backup: {0}";
    public string HooksRemoved { get; init; } = "Live sessions off. Hooks removed.";
    public string Changelog { get; init; } = "What's new";
    // StatusBadge de la fila maestra de hooks (T4): verde si los hooks están instalados, ámbar si no.
    public string BadgeActive { get; init; } = "Active";
    public string BadgeInstall { get; init; } = "Install";

    // v0.4 — cuota leída de la statusline de Claude Code (0 peticiones a la API).
    public string QuotaSourceHeader { get; init; } = "Quota source";
    public string QuotaFromClaudeCode { get; init; } = "Read quota from Claude Code";
    /// <summary>{0} = antigüedad ("12 s", "3 min").</summary>
    public string TapActiveFmt { get; init; } = "On · last data {0} ago";
    public string TapWaiting { get; init; } = "On · waiting for a Claude Code session";
    public string TapOff { get; init; } = "Off · asks the API on every refresh";
    /// <summary>Statusline apagada y lectura con la sesión también: no hay de dónde sacar la cuota.</summary>
    public string TapOffNoApi { get; init; } = "Off · turn it on to see your quota";
    public string TapUnavailable { get; init; } = "Not available in this installation";
    public string TapEnableConfirm { get; init; } =
        "Muesca will read your quota from Claude Code's status line, with no requests to the API.\n\n" +
        "It adds a small step in front of your status line in ~/.claude/settings.json: your status line keeps " +
        "looking the same. If you have none, Muesca adds a compact one (Claude Code then hides some footer hints).\n\n" +
        "A backup of settings.json is made first. Continue?";
    public string TapDisableConfirm { get; init; } =
        "Stop reading the quota from Claude Code?\n\n" +
        "Your original status line is restored exactly and Muesca goes back to asking the API. " +
        "A backup of settings.json is made first.";
    /// <summary>{0} = ruta del backup de settings.json.</summary>
    public string TapEnabledFmt { get; init; } = "Reading quota from Claude Code. Backup: {0}";
    public string TapDisabled { get; init; } = "Status line restored. Back to the API.";
    public string TapDisableConfirmNoApi { get; init; } =
        "Stop reading the quota from Claude Code?\n\n" +
        "Your original status line is restored exactly. Reading with your Claude Code session is off, so Muesca " +
        "will have no quota to show. A backup of settings.json is made first.";
    public string TapDisabledNoApi { get; init; } = "Status line restored. No quota source left.";
    public string TapCompoundError { get; init; } =
        "Your status line is a compound command (&&, ;): Muesca won't hook into it so as not to break it. It keeps using the API.";
    public string TapTypeError { get; init; } = "Your status line isn't a command; Muesca can't read it. It keeps using the API.";
    public string TapSuggest { get; init; } =
        "Muesca can read your quota from Claude Code with zero API requests (no more rate limits). Click to set it up.";

    // Cuota con el token OAuth de Claude Code (no oficial). Solo es/en: el resto de idiomas cae a este inglés.
    public string QuotaOAuthLabel { get; init; } = "Read quota with your Claude Code session (unofficial)";
    public string QuotaOAuthSubtitle { get; init; } =
        "Uses Claude Code's local token only with api.anthropic.com. It isn't stored or sent anywhere else. " +
        "Anthropic doesn't document this, so it may stop working";
    public string QuotaCliRefreshLabel { get; init; } = "Force renewal with the CLI (uses one message)";
    public string QuotaCliRefreshSubtitle { get; init; } =
        "Only with an expired token: runs `claude -p .`, at most every 30 min. Off: open Claude Code to renew it";

    // Notch (F1, solo lectura). Los idiomas sin traducción caen al inglés de estos valores por defecto.
    public string NotchDone { get; init; } = "Done";
    public string NotchWaitingOk { get; init; } = "Waiting for your OK";
    public string NotchAnswerInTerminal { get; init; } = "Answer in the terminal";
    // F2: botones del Permiso en el notch y su confirmación.
    public string NotchAllow { get; init; } = "Allow";
    public string NotchDeny { get; init; } = "Deny";
    public string NotchAllowed { get; init; } = "Allowed";
    public string NotchDenied { get; init; } = "Denied";
    /// <summary>{0} = ventana ("5h"/"7d"), {1} = porcentaje ("96%").</summary>
    public string NotchQuotaFmt { get; init; } = "{0} quota at {1}";
    /// <summary>{0} = sesiones que no caben en la lista del notch expandido.</summary>
    public string NotchMoreSessionsFmt { get; init; } = "+{0} more";
    // Páginas del expandido (F5). Solo es/en; el resto de idiomas cae al inglés.
    public string NotchPageMusic { get; init; } = "Music";
    public string NotchPageTasks { get; init; } = "Tasks";
    public string NotchPageShelf { get; init; } = "Shelf";
    public string NotchNothingPlaying { get; init; } = "Nothing playing";
    public string NotchNoTasks { get; init; } = "No tasks";
    public string NotchShelfEmpty { get; init; } = "The shelf is empty";
    // Página Bandeja (F5 · B4).
    public string NotchShelfClear { get; init; } = "Clear";
    /// <summary>{0} = entradas seleccionadas que se quitarán.</summary>
    public string NotchShelfRemoveFmt { get; init; } = "Remove ({0})";
    public string NotchShelfMissing { get; init; } = "Unavailable";
    public string NotchShelfCopied { get; init; } = "Copied";
    public string NotchShelfDropHere { get; init; } = "Drop here";
    // Detalle gris de cada tarjeta de la bandeja (bajo el nombre; los archivos con tamaño lo enseñan en su lugar).
    public string NotchShelfFolder { get; init; } = "Folder";
    public string NotchShelfFile { get; init; } = "File";
    public string NotchShelfLink { get; init; } = "Link";
    public string NotchShelfText { get; init; } = "Text";
    // Página Tareas y recordatorios (F5 · B5).
    public string NotchTaskAdd { get; init; } = "+ Add";
    public string NotchTaskPlaceholder { get; init; } = "New task…";
    public string NotchTaskSave { get; init; } = "Save";
    public string NotchTaskIn15 { get; init; } = "+15 min";
    public string NotchTaskIn1h { get; init; } = "+1 h";
    public string NotchTaskTomorrow9 { get; init; } = "Tomorrow 9:00";
    public string NotchTaskCustom { get; init; } = "HH:MM";
    /// <summary>Delante de la hora de una tarea de mañana («tomorrow 9:00»).</summary>
    public string NotchTomorrow { get; init; } = "tomorrow";
    public string NotchReminder { get; init; } = "Reminder";
    public string NotchReminderDone { get; init; } = "Done";
    public string NotchReminderSnooze { get; init; } = "+10 min";
    public string NotchLyrics { get; init; } = "Lyrics";
    public string NotchLyricsLoading { get; init; } = "Looking for lyrics…";
    public string NotchNoLyrics { get; init; } = "No lyrics";
    public string NotchLyricsUnavailable { get; init; } = "Lyrics unavailable";
    // Letras v2 (vista de letra): aviso de la letra plana, botones del desfase y «Otra letra».
    public string NotchLyricsUnsynced { get; init; } = "Unsynced lyrics";
    /// <summary>Botones del desfase (\u00ab\u22120,5 s\u00bb / \u00ab+0,5 s\u00bb): se forman con <see cref="Culture"/>, igual que el aviso
    /// (<see cref="NotchLyricsOffsetFmt"/>), para que la fila no mezcle separadores decimales en ning\u00fan idioma.</summary>
    public string NotchLyricsEarlier => Notch.Features.MusicFormat.Offset(-Notch.Features.LyricsCache.OffsetStep, Culture);
    public string NotchLyricsLater => Notch.Features.MusicFormat.Offset(Notch.Features.LyricsCache.OffsetStep, Culture);
    public string NotchLyricsOther { get; init; } = "Other lyrics";
    /// <summary>Aviso del desfase manual; {0} = «+1.5 s».</summary>
    public string NotchLyricsOffsetFmt { get; init; } = "Offset {0}";
    // Ajustes del notch (F1). Solo es/en; el resto de idiomas cae al inglés.
    public string NotchHeader { get; init; } = "Notch";
    public string NotchSubtitle { get; init; } = "Pill at the top of the screen with quota, activity and alerts";
    public string NotchSize { get; init; } = "Size";
    public string NotchSizeAuto { get; init; } = "Auto";
    public string NotchOpenWith { get; init; } = "Open with";
    public string NotchOpenHover { get; init; } = "Hover";
    public string NotchOpenClick { get; init; } = "Click";
    public string NotchMonitor { get; init; } = "Monitor";
    public string NotchMonitorPrimary { get; init; } = "Primary";
    // Nombre legible de cada monitor y submenú de la bandeja para elegir el del notch.
    public string NotchMonitorMenu { get; init; } = "Notch monitor";
    public string NotchMonitorFollowPrimary { get; init; } = "Primary (follow the primary)";
    public string NotchMonitorFallbackFmt { get; init; } = "Monitor {0}";
    public string NotchMonitorIsPrimary { get; init; } = "primary";
    public string NotchMonitorLeft { get; init; } = "left";
    public string NotchMonitorRight { get; init; } = "right";
    public string NotchMonitorAbove { get; init; } = "above";
    public string NotchMonitorBelow { get; init; } = "below";
    // Modo del monitor del notch (29-oct): fijo, siguiendo a la ventana activa o uno en cada monitor.
    public string NotchMonitorModeFixed { get; init; } = "Fixed";
    public string NotchMonitorModeFollow { get; init; } = "Follow the active window";
    public string NotchMonitorModeAll { get; init; } = "All monitors";
    public string NotchMonitorFixed { get; init; } = "Fixed monitor";
    /// <summary>Aviso en solo lectura de un notch que no es el principal efectivo (todos los monitores).</summary>
    public string NotchMirrorClickHint { get; init; } = "Click to answer here";
    public string NotchMinimal { get; init; } = "Minimal mode when idle";
    public string NotchAutoMinimal { get; init; } = "Shrink to a line after 15 s without activity";
    public string NotchHideInFullscreen { get; init; } = "Hide in full screen";
    public string NotchSound { get; init; } = "Sound on Done, permission requests and quota alert";
    // Ajustes del notch F5 (B8).
    public string NotchClock { get; init; } = "Clock when idle";
    public string NotchMascot { get; init; } = "Mascot";
    public string NotchMusic { get; init; } = "Music";
    public string NotchLyricsSetting { get; init; } = "LRCLIB lyrics";
    public string NotchLyricsSubtitle { get; init; } = "Sends title and artist to lrclib.net";
    // Letra en el notch pequeño (depende de «Letras»; sus ranuras, de ella).
    public string NotchCompactLyrics { get; init; } = "Lyrics in the small notch";
    public string NotchCompactLyricsSubtitle { get; init; } = "Fetches each song's lyrics as it plays, even with the lyrics view closed";
    public string NotchCompactLyricsArt { get; init; } = "Album art with the lyrics";
    public string NotchCompactLyricsQuota { get; init; } = "Quota with the lyrics";
    public string NotchTasks { get; init; } = "Tasks and reminders";
    public string NotchShelfClearOnStart { get; init; } = "Shelf: clear on startup";
    public string NotchAdaptiveContrast { get; init; } = "Adaptive contrast";
    // Permisos desde el notch (F2, opt-in). Toca ~/.claude/settings.json: confirmación + backup como la statusline.
    public string NotchPermissions { get; init; } = "Answer permissions from the notch";
    public string NotchPermissionsSubtitle { get; init; } =
        "Allow / Deny buttons; if anything fails, Claude Code shows its usual prompt";
    public string NotchPermissionsEnableConfirm { get; init; } =
        "Muesca will let you answer Claude Code's permission requests with Allow / Deny buttons in the notch.\n\n" +
        "It adds a second entry to PermissionRequest in ~/.claude/settings.json (claudebar-helper permission, " +
        "timeout 45 s). Nothing is decided without your click: if Muesca is closed, busy or you don't answer " +
        "within 25 s, Claude Code shows its usual prompt.\n\n" +
        "A backup of settings.json is made first. Continue?";
    public string NotchPermissionsDisableConfirm { get; init; } =
        "Stop answering permissions from the notch?\n\n" +
        "The permission entry is removed from ~/.claude/settings.json (live sessions keep working) and Claude Code " +
        "goes back to asking only in the terminal. A backup of settings.json is made first.";
    /// <summary>{0} = ruta del backup de settings.json.</summary>
    public string NotchPermissionsEnabledFmt { get; init; } = "Permissions from the notch on. Backup: {0}";
    public string NotchPermissionsDisabled { get; init; } = "Permissions from the notch off. Claude Code asks in the terminal.";
    public string NotchPermissionsNeedHooks { get; init; } =
        "Turn on live sessions first: permissions from the notch use the same hooks.";

    // Aviso de actualización en el notch: punto en el reposo y fila en el expandido.
    /// <summary>{0} = versión («0.6»).</summary>
    public string NotchUpdateFmt { get; init; } = "New version {0}";
    public string NotchUpdateAction { get; init; } = "Update";
    public string NotchUpdateDownloading { get; init; } = "Downloading…";
    /// <summary>En las ventanas que no son la interactiva (todos los monitores): el botón está en la otra.</summary>
    public string NotchUpdateElsewhere { get; init; } = "Ready to install";
    // Muesca Pro (docs/superpowers/specs/2026-10-30-gratis-y-pro-design.md): menú de la bandeja, candado y tarjeta del
    // notch, aviso del fin de la prueba y lo que marca el asistente. En TODOS los idiomas («Pro» y «Muesca Pro» son nombres).
    /// <summary>{0} = estado (<see cref="ProStateFree"/>, <see cref="ProStateTrialFmt"/>…).</summary>
    public string MenuProFmt { get; init; } = "Muesca Pro… ({0})";
    public string ProStateFree { get; init; } = "Free";
    public string ProStatePro { get; init; } = "Pro";
    /// <summary>{0} = días de prueba que quedan (2..7).</summary>
    public string ProStateTrialFmt { get; init; } = "Trial: {0} days";
    public string ProStateTrialOne { get; init; } = "Trial: 1 day";
    /// <summary>Submenú «Monitor del notch» sin Pro: la entrada que abre la tarjeta.</summary>
    public string ProMonitorsLocked { get; init; } = "Several monitors: Muesca Pro…";
    public string ProThanks { get; init; } = "Muesca Pro is active. Thank you!";
    public string NotchProChip { get; init; } = "Pro";
    public string NotchProLockedMusic { get; init; } = "Music and synced lyrics come with Muesca Pro.";
    public string NotchProLockedTasks { get; init; } = "Tasks with reminders come with Muesca Pro. Your tasks are kept.";
    public string NotchProLockedShelf { get; init; } = "The file shelf comes with Muesca Pro. What you dropped is kept.";
    public string NotchProLockedHint { get; init; } = "Click to see what Pro includes";
    // Página Uso del notch: cuota, ritmo, gasto por modelo, plan y salud, con sus gráficas. En TODOS los idiomas.
    public string NotchPageUsage { get; init; } = "Usage";
    /// <summary>Línea del ritmo de una tarjeta. {0} = % del ritmo frente al ideal («118%»).</summary>
    public string NotchUsagePaceFmt { get; init; } = "pace {0}";
    /// <summary>Leyenda de la proyección cuando la cuota se agota antes del reinicio. {0} = hora («17:40»).</summary>
    public string NotchUsageRunsOutFmt { get; init; } = "runs out at {0}";
    /// <summary>Igual que <see cref="NotchUsageRunsOutFmt"/> cuando no es hoy: {0} lleva el día («sáb 10:00»).</summary>
    public string NotchUsageRunsOutDayFmt { get; init; } = "runs out {0}";
    /// <summary>En la gráfica, cuando no se pudieron leer los datos del rango (se reintenta al minuto).</summary>
    public string NotchUsageLoadFailed { get; init; } = "Couldn't load this range · retrying";
    /// <summary>Leyenda de la proyección: dónde llega la cuota al reiniciarse. {0} = % («78%»).</summary>
    public string NotchUsageAtResetFmt { get; init; } = "≈ {0} at reset";
    public string NotchUsageExtraOn { get; init; } = "Extra usage on";
    public string NotchUsageEmptySpend { get; init; } = "No spend in this range yet";
    public string NotchUsageEmptySpendHint { get; init; } = "It fills in as you use Claude Code";
    public string NotchUsageEmptyQuota { get; init; } = "No quota history yet";
    public string NotchUsageEmptyQuotaHint { get; init; } = "Muesca saves your quota % every minute";
    /// <summary>Marcas de la gráfica del %: la raya de ahora y la del reinicio.</summary>
    public string NotchUsageNow { get; init; } = "now";
    public string NotchUsageReset { get; init; } = "reset";
    // Página Foco del notch: el pomodoro (fases, botones, focos de hoy, tarea enlazada y semana), su aviso del fin de fase y sus
    // ajustes. En TODOS los idiomas.
    public string NotchPageFocus { get; init; } = "Focus";
    public string NotchFocusPhaseFocus { get; init; } = "Focus";
    public string NotchFocusPhaseShort { get; init; } = "Short break";
    public string NotchFocusPhaseLong { get; init; } = "Long break";
    public string NotchFocusStart { get; init; } = "Start";
    public string NotchFocusPause { get; init; } = "Pause";
    public string NotchFocusResume { get; init; } = "Resume";
    public string NotchFocusStartBreak { get; init; } = "Start break";
    public string NotchFocusStartFocus { get; init; } = "Start focus";
    public string NotchFocusSkipHint { get; init; } = "Skip to the next phase";
    public string NotchFocusResetPhase { get; init; } = "Restart this phase";
    public string NotchFocusResetCycle { get; init; } = "Start the cycle over";
    /// <summary>{0} = foco del ciclo (1..), {1} = focos hasta el descanso largo.</summary>
    public string NotchFocusCycleFmt { get; init; } = "{0} of {1}";
    public string NotchFocusPaused { get; init; } = "paused";
    public string NotchFocusDoneFocus { get; init; } = "Focus done!";
    public string NotchFocusDoneBreak { get; init; } = "Break's over";
    public string NotchFocusTodayOne { get; init; } = "1 focus session today";
    /// <summary>{0} = focos completos hoy (2 o más; con 1, <see cref="NotchFocusTodayOne"/>).</summary>
    public string NotchFocusTodayFmt { get; init; } = "{0} focus sessions today";
    /// <summary>{0} = minutos.</summary>
    public string NotchFocusMinutesFmt { get; init; } = "{0} min";
    /// <summary>{0} = segundos.</summary>
    public string NotchFocusSecondsFmt { get; init; } = "{0} s";
    /// <summary>{0} = horas.</summary>
    public string NotchFocusHoursFmt { get; init; } = "{0} h";
    public string NotchFocusLinkTask { get; init; } = "Link a task";
    public string NotchFocusNoTasks { get; init; } = "No pending tasks";
    public string NotchFocusStats { get; init; } = "This week's stats";
    /// <summary>{0} = focos de los últimos 7 días; {1} = su tiempo («5 h 25 min»).</summary>
    public string NotchFocusWeekFmt { get; init; } = "This week: {0} sessions · {1}";
    /// <summary>Con un solo foco en los últimos 7 días (como <see cref="NotchFocusTodayOne"/>); {0} = su tiempo («25 min»).</summary>
    public string NotchFocusWeekOneFmt { get; init; } = "This week: 1 session · {0}";
    /// <summary>{0} = la fase que toca («descanso corto»).</summary>
    public string NotchFocusNextFmt { get; init; } = "Up next: {0}";
    public string NotchFocusSoundSetting { get; init; } = "Focus: sound when a phase ends";
    public string NotchFocusLength { get; init; } = "Focus length";
    public string NotchFocusShortLength { get; init; } = "Short break";
    public string NotchFocusLongLength { get; init; } = "Long break";
    public string NotchFocusLongEvery { get; init; } = "Long break every (sessions)";

    public string NotchProTitle { get; init; } = "Muesca Pro";
    /// <summary>{0} = precio de la Store con su moneda («9,99 €»).</summary>
    public string NotchProPriceFmt { get; init; } = "{0} · one-time payment";
    public string NotchProOneTime { get; init; } = "One-time payment";
    /// <summary>{0} = días de prueba que quedan (2..7).</summary>
    public string NotchProTrialFmt { get; init; } = "Trial · {0} days left";
    public string NotchProTrialOne { get; init; } = "Trial · last day";
    public string NotchProActive { get; init; } = "Active. Thank you!";
    /// <summary>Lo que incluye, en el orden del diseño (la tarjeta los pone en dos columnas).</summary>
    public string[] NotchProFeatures { get; init; } =
    {
        "Allow / Deny from the notch", "Music and synced lyrics", "Tasks with reminders", "File shelf",
        "Notch on every monitor", "Usage charts and advanced pomodoro",
    };
    public string NotchProBuy { get; init; } = "Buy";
    public string NotchProRestore { get; init; } = "Restore purchase";
    public string NotchProGetInStore { get; init; } = "Get Pro in Microsoft Store";
    public string NotchProBusy { get; init; } = "Opening Microsoft Store…";
    public string NotchProFailed { get; init; } = "Purchase not completed.";
    public string NotchProOffline { get; init; } = "The Store isn't answering.";
    public string NotchProUnavailable { get; init; } = "Pro isn't in the Store yet.";
    public string NotchProNotOwned { get; init; } = "No Pro on this account.";
    /// <summary>En las ventanas que no son la interactiva (todos los monitores): los botones están en la otra.</summary>
    public string NotchProElsewhere { get; init; } = "Click to buy from this screen";
    public string NotchProTrialEndedTitle { get; init; } = "Your Pro trial has ended";
    public string NotchProTrialEndedDetail { get; init; } = "Your tasks and shelf are kept.";
    public string OnbProTag { get; init; } = "Pro";
    public string OnbProTrialNote { get; init; } = "Pro: free for 7 days, then a one-time payment.";
    public string OnbProStoreNote { get; init; } = "Pro: in the Microsoft Store edition.";
    /// <summary>Edición de la Store sin prueba ni compra.</summary>
    public string OnbProBuyNote { get; init; } = "Pro: a one-time payment, from the Pro card in the notch.";
    public string OnbProActiveNote { get; init; } = "You have Muesca Pro.";

    // Asistente de bienvenida dentro del notch (primer arranque y «Volver a configurar…»). Solo es/en; el resto cae al
    // inglés. Los textos largos son los del instalador (installer/ClaudeBarWin.iss).
    public string MenuReconfigure { get; init; } = "Set up again…";
    public string OnbSkip { get; init; } = "Skip · Esc";
    public string OnbBack { get; init; } = "Back";
    public string OnbNext { get; init; } = "Next";
    public string OnbStart { get; init; } = "Start";
    public string OnbTagline { get; init; } = "Your Claude Code quota, always in sight.";
    public string OnbClaudeDetected { get; init; } = "Claude Code detected";
    public string OnbClaudeNotFound { get; init; } = "Claude Code not found: you can install it later";
    public string OnbKeysHint { get; init; } = "Enter to continue · Tab to move · Esc to skip";
    public string OnbProfileTitle { get; init; } = "What will you use Muesca for?";
    public string OnbProfileSubtitle { get; init; } = "Pick a starting point. You can fine-tune it next or later in the settings.";
    /// <summary>Nombre de cada perfil, en el orden de <c>SetupProfile</c>.</summary>
    public string[] OnbProfileNames { get; init; } = { "Quota only", "I code with Claude Code", "Full notch", "Minimal / gaming", "Custom" };
    /// <summary>La línea de cada tarjeta, en el orden de <c>SetupProfile</c>.</summary>
    public string[] OnbProfileLines { get; init; } =
    {
        "Tray icon and panel. No notch.",
        "Live sessions, alerts, music and lyrics.",
        "All that, plus small lyrics, clock, cat, tasks.",
        "A thin line that hides in games. No noise.",
        "You choose every option.",
    };
    public string OnbRecommended { get; init; } = "Recommended";
    public string OnbPreviewCaption { get; init; } = "Live preview: this is how it will look at the top of your screen.";
    public string OnbPreviewNoNotch { get; init; } = "No notch: your quota lives in the tray icon and its panel.";
    public string OnbClaudeMissing { get; init; } =
        "Claude Code was not found on this computer. Muesca needs it to read your quota: install it and sign in whenever you like.";
    public string OnbFeaturesTitle { get; init; } = "What gets turned on";
    /// <summary>{0} = nombre del perfil.</summary>
    public string OnbFeaturesSubtitleFmt { get; init; } = "Ticked with the values of «{0}». Change whatever you like.";
    /// <summary>Nombre de cada casilla, en el orden de <c>SetupFeature</c>.</summary>
    public string[] OnbFeatureNames { get; init; } =
    {
        "Notch at the top of the screen", "Thin line when idle", "Hide in full screen", "Sounds", "Music", "Synced lyrics",
        "Lyrics in the small notch", "Clock", "The cat", "Tasks and reminders", "Reduce motion",
    };
    /// <summary>La línea gris de cada casilla, en el orden de <c>SetupFeature</c>.</summary>
    public string[] OnbFeatureDetails { get; init; } =
    {
        "quota, activity and alerts", "instead of the pill with the 5-hour %", "games, video, presentations",
        "a chime when Claude finishes and at 95 %", "the song that's playing", "from lrclib.net",
        "the current line instead of the title", "when idle", "when idle", "in the open notch",
        "no animations in the panel or the notch",
    };
    public string OnbFeaturesHint { get; init; } =
        "Music only reads what Windows is playing (nothing leaves your PC). Lyrics send title and artist to lrclib.net.";
    public string OnbReset { get; init; } = "Reset to recommended";
    public string OnbSettingsTitle { get; init; } = "Recommended settings";
    public string OnbSettingsSubtitle { get; init; } = "Already set to what suits this computer.";
    public string OnbSettingsSubtitleKeep { get; init; } = "Your current values; «recommended» marks what suits this computer.";
    /// <summary>{0} = el idioma de Windows («Español»).</summary>
    public string OnbLanguageSystemFmt { get; init; } = "Same as Windows ({0})";
    public string OnbLanguageHint { get; init; } = "Muesca's texts. «Same as Windows» follows the system language.";
    public string OnbMonitorPrimary { get; init; } = "The main one";
    /// <summary>{0} = número de monitores.</summary>
    public string OnbMonitorHintFmt { get; init; } = "You have {0} monitors. «The main one» follows whichever is the main monitor.";
    public string OnbSize { get; init; } = "Notch size";
    /// <summary>{0} = ancho lógico del monitor; {1} = qué será Auto (%).</summary>
    public string OnbSizeHintFmt { get; init; } = "Auto fits the width and scaling (DPI): {0} logical px here (≈ {1} %).";
    public string OnbOpen { get; init; } = "Open the notch";
    public string OnbOpenHover { get; init; } = "By hovering over it";
    public string OnbOpenClick { get; init; } = "With a click";
    public string OnbOpenHintHover { get; init; } = "Resting the pointer on it for a moment is enough.";
    public string OnbOpenHintAbove { get; init; } = "There's a monitor right above: the pointer would cross the notch on its way up.";
    /// <summary>{0} = ancho lógico del monitor.</summary>
    public string OnbOpenHintSmallFmt { get; init; } = "Small screen ({0} logical px wide): opened, the notch would cover a lot.";
    public string OnbStartupHint { get; init; } = "Muesca waits in the tray from the moment you sign in.";
    /// <summary>Desde la bandeja, sin saber aún cómo está «Iniciar con Windows» (edición de la Store): sin tocarlo no se aplica.</summary>
    public string OnbStartupUnknownHint { get; init; } = "Its current state couldn't be read yet: if you don't touch it, it stays as it is.";
    /// <summary>Resumen: «Iniciar con Windows» se queda como esté.</summary>
    public string OnbUnchanged { get; init; } = "unchanged";
    public string OnbNotchOff { get; init; } = "The notch is off: these settings only matter if you turn it on later.";
    public string OnbRecommendedTag { get; init; } = "recommended";
    public string OnbClaudeTitle { get; init; } = "Claude Code";
    public string OnbClaudeSubtitle { get; init; } = "Live sessions and permissions from the notch.";
    /// <summary>{0} = carpeta de Claude Code.</summary>
    public string OnbClaudeFoundFmt { get; init; } = "Claude Code is installed ({0}).";
    public string OnbLiveSessions { get; init; } = "Turn on live sessions";
    public string OnbLiveSessionsActive { get; init; } = "Live sessions are already on";
    public string OnbLiveSessionsHint { get; init; } =
        "Installs hooks in ~/.claude/settings.json, Claude Code's global settings (a backup is made first), so the notch and " +
        "the panel see what each session is doing. You can remove them any time from the tray menu › Disable (remove hooks); " +
        "uninstalling removes them too.";
    public string OnbLiveSessionsActiveHint { get; init; } = "Nothing to install. To turn them off: tray menu › Disable (remove hooks).";
    public string OnbPermissions { get; init; } = "Answer permissions from the notch (optional)";
    public string OnbPermissionsHint { get; init; } =
        "When Claude Code asks for permission (run a command, edit a file…), you allow or deny it with the notch buttons. " +
        "Nothing is decided without your click: if Muesca is closed or you don't answer within 25 s, Claude Code shows its " +
        "usual prompt. Adds one more entry to settings.json.";
    public string OnbPermissionsNeed { get; init; } = "— needs the notch and live sessions";
    public string OnbSummaryTitle { get; init; } = "All set";
    public string OnbSummarySubtitle { get; init; } = "This is what will be applied. Everything can be changed later in the settings.";
    public string OnbSumProfile { get; init; } = "Profile";
    public string OnbSumNotch { get; init; } = "Notch";
    public string OnbSumFeatures { get; init; } = "Turned on";
    public string OnbSumLive { get; init; } = "Live sessions";
    public string OnbSumPermissions { get; init; } = "Permissions from the notch";
    /// <summary>{0} = cómo se abre; {1} = tamaño; {2} = monitor.</summary>
    public string OnbNotchOnFmt { get; init; } = "on · {0} · size {1} · {2}";
    public string OnbNothingElse { get; init; } = "nothing else";
    public string OnbYes { get; init; } = "yes";
    public string OnbNo { get; init; } = "no";
    public string OnbLiveInstall { get; init; } = "turned on (hooks in ~/.claude/settings.json, with a backup)";
    public string OnbLiveActive { get; init; } = "already on";
    public string OnbBackupNote { get; init; } = "A copy of your current config.json is saved first.";
    public string OnbSkipTitle { get; init; } = "Skip the setup?";
    /// <summary>{0} = perfil recomendado.</summary>
    public string OnbSkipTextFmt { get; init; } =
        "Muesca will start with «{0}» and the settings that suit this computer. Live sessions are not turned on: you can do it later from the tray.";
    public string OnbCloseTitle { get; init; } = "Close without changes?";
    public string OnbCloseText { get; init; } = "Your current settings stay as they are. You can set Muesca up again from the tray.";
    public string OnbKeepGoing { get; init; } = "Keep setting up";
    public string OnbSkipConfirm { get; init; } = "Skip";
    public string OnbClose { get; init; } = "Close";
    public string OnbErrorTitle { get; init; } = "Your settings could not be applied";
    /// <summary>{0} = el motivo (de <c>SetupOutcome.Message</c>).</summary>
    public string OnbErrorTextFmt { get; init; } = "Nothing was changed ({0}). Details in claudebar.log.";
    public string OnbRetry { get; init; } = "Retry";
    public string OnbHooksFailed { get; init; } =
        "Your settings were applied, but live sessions could not be turned on: ~/.claude/settings.json could not be changed. " +
        "You can turn them on later from the tray menu › Enable (install hooks).";
    public string OnbInTray { get; init; } = "Muesca is in the tray: click its icon to see your quota.";

    // Mascot verbs (F3): pool de 3-5 verbos JUGUETONES por fase, junto a la mascota con elipsis
    // animada ("thinking…"). Clean-room (inventados, no copiados de Notchi/Buddi = GPL). El
    // MascotAnimator rota por el pool con jitter; el índice se acota al tamaño del pool.
    public string[] MascotVerbsProcessing { get; init; } = { "thinking", "cooking", "tinkering", "noodling", "scheming" };
    public string[] MascotVerbsWaitingApproval { get; init; } = { "your call", "needs a nod", "psst, you" };
    public string[] MascotVerbsWaitingInput { get; init; } = { "your turn", "all ears", "waiting up", "go ahead" };
    public string[] MascotVerbsCompacting { get; init; } = { "tidying up", "folding memory", "squishing", "decluttering" };
    public string[] MascotVerbsIdle { get; init; } = { "napping", "loafing", "daydreaming", "stretching" };
    public string[] MascotVerbsEnded { get; init; } = { "all done", "wrapped up", "out cold" };

    // Contador anónimo de uso (Services/Telemetry, PRIVACY.md): Ajustes › Sistema, la bienvenida y el globo de quien actualiza.
    public string UsageStatsLabel { get; init; } = "Anonymous usage count";
    /// <summary>La explicación en una línea (Ajustes y bienvenida).</summary>
    public string UsageStatsSubtitle { get; init; } =
        "Counts how many people use Muesca, without knowing who. Once a day it sends only version, edition and language.";
    public string UsageStatsPrivacy { get; init; } = "Privacy policy";
    /// <summary>Globo, una sola vez, para quien ya usaba la app y actualiza.</summary>
    public string UsageStatsNotice { get; init; } =
        "New: Muesca counts how many people use it, without knowing who. Once a day it sends only version, edition and language. You can turn it off in Settings › System.";
}

public static class Localization
{
    /// <summary>(code, native name) in menu order. "system" handled separately.</summary>
    public static readonly (string Code, string Native)[] Languages =
    {
        ("en", "English"),
        ("es", "Español"),
        ("nl", "Nederlands"),
        ("fr", "Français"),
        ("de", "Deutsch"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("zh-Hant", "繁體中文")
    };

    public static Strings ForConfig(AppConfig cfg)
    {
        // Idioma "system": textos del idioma del SO pero FORMATOS con la CurrentCulture real del
        // usuario (es-MX ≠ es-ES, etc.). Idioma explícito: textos Y formatos del idioma elegido (T2).
        if (string.IsNullOrEmpty(cfg.Language) || cfg.Language == "system")
            return Get(ResolveSystemCode()) with { Culture = CultureInfo.CurrentCulture };
        return Get(cfg.Language);
    }

    /// <summary>Orden de ciclo del selector de idioma del panel: "system" + los 8 códigos.</summary>
    public static readonly string[] LanguageCycle =
        new[] { "system" }.Concat(Languages.Select(l => l.Code)).ToArray();

    /// <summary>Nombre legible de un código de idioma ("system" → SystemDefault; resto → nativo).</summary>
    public static string LanguageDisplayName(string code, Strings s)
    {
        if (string.IsNullOrEmpty(code) || code == "system") return s.SystemDefault;
        foreach (var (c, native) in Languages)
            if (c == code) return native;
        return code;
    }

    /// <summary>Siguiente idioma en el ciclo (envuelve al principio).</summary>
    public static string NextLanguage(string current)
    {
        var cur = string.IsNullOrEmpty(current) ? "system" : current;
        int i = Array.IndexOf(LanguageCycle, cur);
        return LanguageCycle[(i < 0 ? 0 : (i + 1) % LanguageCycle.Length)];
    }

    public static string ResolveSystemCode()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return two switch
        {
            "es" => "es",
            "nl" => "nl",
            "fr" => "fr",
            "de" => "de",
            "ja" => "ja",
            "ko" => "ko",
            "zh" => "zh-Hant",
            _ => "en"
        };
    }

    public static Strings Get(string code) => code switch
    {
        "es" => Spanish,
        "nl" => Dutch,
        "fr" => French,
        "de" => German,
        "ja" => Japanese,
        "ko" => Korean,
        "zh-Hant" => TradChinese,
        _ => English
    };

    private static readonly Strings English = new();

    private static readonly Strings Spanish = new()
    {
        Culture = CultureInfo.GetCultureInfo("es-ES"),
        Tut = TutorialStrings.Spanish,
        Dashboard = "Panel",
        Refresh = "Actualizar ahora",
        PanelWindow = "Ventana del panel",
        Position = "Posición",
        PosBottomRight = "Abajo derecha",
        PosBottomLeft = "Abajo izquierda",
        PosTopRight = "Arriba derecha",
        PosTopLeft = "Arriba izquierda",
        PosCenter = "Centro",
        PosCustom = "Personalizada (arrastra el panel)",
        Sticky = "Fijado (no se cierra solo)",
        AlwaysOnTop = "Siempre encima",
        ReduceMotion = "Reducir movimiento",
        ReduceMotionSubtitle = "Desactiva las animaciones",
        UpdateFrequency = "Frecuencia de actualización",
        Sec30 = "30s",
        Min1 = "1m",
        Min5 = "5m",
        Min15 = "15m",
        Notifications = "Notificaciones",
        Enabled = "Activadas",
        NotifyWhenReaching = "Umbral de aviso",
        ColorThreshold = "Umbral de color",
        DefaultTag = "(def.)",
        Settings = "Ajustes",
        ShowSpend = "Mostrar gasto estimado",
        ShowSpendSubtitle = "Coste equivalente por modelo",
        StartWithWindows = "Iniciar con Windows",
        StartupChecking = "Comprobando…",
        StartupChanging = "Cambiando…",
        EditConfig = "Editar config (avanzado)…",
        OpenDataFolder = "Abrir carpeta de datos",
        Language = "Idioma",
        SystemDefault = "Sistema (predeterminado)",
        Exit = "Salir",
        SessionWord = "Sesión",
        WeekWord = "Semana",
        ResetsIn = "resetea en",
        Resetting = "reseteando…",
        RollingHint = "ventana móvil de 5h desde tu 1ª petición",
        SpendHeaderFormat = "Gasto estimado ({0}d, equiv. API)",
        Loading = "Cargando…",
        UpdatedAt = "Actualizado",
        AgoFormat = "hace {0}",
        StaleLabel = "desfasado",
        LocalSeal = "Tus credenciales y datos no salen del equipo",
        HintClickToHide = "clic en el icono para ocultar",
        HintPinnedClose = "fijado · ✕ para cerrar",
        PreviousDataTip = "⚠ datos previos (sin conexión)",
        PreviousDataFooter = "datos previos",
        QuotaRenewed = "cuota renovada",
        StateNoCredentials = "No autenticado — inicia sesión en Claude Code",
        StateAuthExpired = "Sesión caducada — abre Claude Code",
        StateRateLimited = "Límite de peticiones — reintentando",
        StateNetworkError = "Sin conexión con Anthropic",
        StateStatuslineOnly = "Sin cuota todavía — abre una sesión de Claude Code",
        LastReadingFmt = "Última lectura hace {0}",
        NotifQuotaFormat = "Claude {0}%+ de cuota usada",
        Theme = "Tema",
        ThemeSystem = "Sistema",
        ThemeDark = "Oscuro",
        ThemeLight = "Claro",
        ThemeCli = "CLI",
        ThemeImported = "Importado",
        ImportTheme = "Importar .itermcolors…",
        ShowServiceStatus = "Mostrar estado del servicio",
        HealthOk = "Operativo",
        HealthDegraded = "Degradado",
        HealthOutage = "Caído",
        SectionQuota = "Cuota",
        SectionSessions = "Sesiones",
        SectionSpend = "Gasto",
        SectionChart = "Gráfica",
        Back = "Volver",
        UsageChart = "Gráfica de uso",
        NoData = "Sin datos en este rango",
        OpenBilling = "Abrir facturación…",
        ChartPeak = "máx",
        ChartTotal = "total",
        ChartTabSpend = "Gasto $",
        ChartTabPct = "Cuota %",
        ModelFamilyOther = "Otros",
        SpendNoRate = "sin tarifa",
        PricingOnlineUpdate = "Actualizar tarifas online",
        PricingOnlineUpdateSubtitle = "Descarga precios públicos de models.dev · no envía nada tuyo",
        Opacity = "Opacidad",
        IconMode = "Modo de icono",
        PaceAlerts = "Avisos de ritmo",
        WinSession = "de sesión (5h)",
        WinWeekly = "semanal (7d)",
        PaceAlertTitle = "⚠ Ritmo de cuota",
        PaceAlertBodyFmt = "A este ritmo te quedas sin cuota {0} el {1}, antes del reset",
        PaceEtaLabel = "se agota",
        MenuAppearance = "Apariencia",
        MenuSections = "Contenido del panel",
        MenuIcon = "Icono",
        MenuAdvanced = "Avanzado",
        MenuSystem = "Sistema",
        VersionLabel = "Versión",
        About = "Acerca de",
        AboutProduct = "Muesca, el notch para Claude Code · de Bekay",
        AboutDisclaimer = "No afiliado a Anthropic. Claude es una marca de Anthropic.",
        CheckUpdates = "Buscar actualizaciones",
        Checking = "Buscando actualizaciones…",
        UpdateAvailableFmt = "⬇ Actualización disponible {0}",
        UpToDateFmt = "Estás al día (v{0}).",
        UpdatePromptFmt = "Versión actual: v{0}\nÚltima disponible: {1}\n\n¿Descargar ahora?",
        UpdateDownloadedFmt = "Descargado en:\n{0}\n\nCierra Muesca y reemplaza el .exe en uso por este archivo.",
        UpdateFailed = "No se pudo comprobar/descargar la actualización.",
        UpdateDownloadingFmt = "Descargando la v{0}… {1} %",
        UpdateRestartFmt = "Reiniciar para actualizar a la v{0}",
        UpdateFoundFmt = "Hay una versión nueva, la {0}. Se está descargando en segundo plano.",
        UpdateReadyFmt = "La versión {0} está lista. Reinicia Muesca desde el menú de la bandeja para actualizar, o se actualizará la próxima vez que se cierre.",
        UpdateFailedLogFmt = "No se ha podido buscar o descargar la actualización. Detalles en {0}",
        UpdatedFmt = "Muesca se ha actualizado a la versión {0}.",
        MenuLiveSessions = "Sesiones en vivo",
        LiveSessionsSubtitle = "Instala/quita hooks en ~/.claude/settings.json",
        RemoveIntegration = "Quitar la integración con Claude Code",
        RemoveIntegrationHint = "Quita los hooks y la statusline de Muesca del settings.json de Claude Code. Hazlo antes de desinstalar.",
        RemoveIntegrationConfirm = "Se quitarán del settings.json de Claude Code (~/.claude/settings.json) solo las entradas de Muesca: los hooks de las sesiones en vivo y de los permisos, y el enganche de la statusline. Tu statusline vuelve a ser exactamente la de antes y no se toca nada más. Antes se guarda una copia de settings.json.\n\nHazlo antes de desinstalar Muesca. ¿Seguir?",
        RemoveIntegrationDoneFmt = "Integración con Claude Code quitada. Copia de settings.json: {0}",
        RemoveIntegrationNothing = "El settings.json de Claude Code no tiene entradas de Muesca: no hay nada que quitar.",
        MenuShowMascot = "Mostrar mascota",
        MenuSuppressWhenFocused = "Silenciar si la terminal tiene foco",
        MenuInstallHooks = "Activar (instalar hooks)…",
        MenuUninstallHooks = "Desactivar (quitar hooks)",
        NoActiveSessions = "Sin sesiones activas",
        SessionPhaseIdle = "en reposo",
        SessionPhaseProcessing = "trabajando",
        SessionPhaseWaitingApproval = "espera tu OK",
        SessionPhaseWaitingInput = "tu turno",
        SessionPhaseCompacting = "compactando",
        NotifWaitingApprovalFmt = "Claude espera tu OK en {0}",
        NotifWaitingInputFmt = "Claude terminó en {0}",
        LiveSessionsTitle = "Sesiones de Claude",
        HooksInstalledFmt = "Sesiones en vivo activadas. Backup: {0}",
        HooksRemoved = "Sesiones en vivo desactivadas. Hooks quitados.",
        Changelog = "Novedades",
        BadgeActive = "Activas",
        BadgeInstall = "Instalar",
        QuotaSourceHeader = "Origen de la cuota",
        QuotaFromClaudeCode = "Leer la cuota desde Claude Code",
        MenuProFmt = "Muesca Pro… ({0})",
        ProStateFree = "Gratis",
        ProStateTrialFmt = "Prueba: {0} días",
        ProStateTrialOne = "Prueba: 1 día",
        ProMonitorsLocked = "Varios monitores: Muesca Pro…",
        ProThanks = "Muesca Pro activado. ¡Gracias!",
        NotchProLockedMusic = "Música y letras sincronizadas, con Muesca Pro.",
        NotchProLockedTasks = "Tareas con avisos, con Muesca Pro. Tus tareas se conservan.",
        NotchPageUsage = "Uso",
        NotchUsagePaceFmt = "ritmo {0}",
        NotchUsageRunsOutFmt = "se agota a las {0}",
        NotchUsageRunsOutDayFmt = "se agota el {0}",
        NotchUsageLoadFailed = "No se pudo cargar este rango · se reintentará",
        NotchUsageAtResetFmt = "≈ {0} al reiniciar",
        NotchUsageExtraOn = "Uso extra activado",
        NotchUsageEmptySpend = "Aún no hay gasto en este rango",
        NotchUsageEmptySpendHint = "Se llena a medida que usas Claude Code",
        NotchUsageEmptyQuota = "Aún no hay historial de cuota",
        NotchUsageEmptyQuotaHint = "Muesca guarda tu % de cuota cada minuto",
        NotchUsageNow = "ahora",
        NotchUsageReset = "reinicio",
        NotchPageFocus = "Foco",
        NotchFocusPhaseFocus = "Foco",
        NotchFocusPhaseShort = "Descanso corto",
        NotchFocusPhaseLong = "Descanso largo",
        NotchFocusStart = "Empezar",
        NotchFocusPause = "Pausa",
        NotchFocusResume = "Reanudar",
        NotchFocusStartBreak = "Empezar descanso",
        NotchFocusStartFocus = "Empezar foco",
        NotchFocusSkipHint = "Saltar a la siguiente fase",
        NotchFocusResetPhase = "Reiniciar esta fase",
        NotchFocusResetCycle = "Empezar el ciclo de nuevo",
        NotchFocusCycleFmt = "{0} de {1}",
        NotchFocusPaused = "en pausa",
        NotchFocusDoneFocus = "¡Foco terminado!",
        NotchFocusDoneBreak = "Fin del descanso",
        NotchFocusTodayOne = "1 foco hoy",
        NotchFocusTodayFmt = "{0} focos hoy",
        NotchFocusMinutesFmt = "{0} min",
        NotchFocusSecondsFmt = "{0} s",
        NotchFocusHoursFmt = "{0} h",
        NotchFocusLinkTask = "Enlazar con una tarea",
        NotchFocusNoTasks = "Sin tareas pendientes",
        NotchFocusStats = "Estadísticas de la semana",
        NotchFocusWeekFmt = "Esta semana: {0} focos · {1}",
        NotchFocusWeekOneFmt = "Esta semana: 1 foco · {0}",
        NotchFocusNextFmt = "Siguiente: {0}",
        NotchFocusSoundSetting = "Foco: sonido al terminar una fase",
        NotchFocusLength = "Duración del foco",
        NotchFocusShortLength = "Descanso corto",
        NotchFocusLongLength = "Descanso largo",
        NotchFocusLongEvery = "Descanso largo cada (focos)",
        NotchProLockedShelf = "La bandeja de archivos, con Muesca Pro. Lo que soltaste se conserva.",
        NotchProLockedHint = "Haz clic para ver qué incluye Pro",
        NotchProPriceFmt = "{0} · pago único",
        NotchProOneTime = "Pago único",
        NotchProTrialFmt = "Prueba · quedan {0} días",
        NotchProTrialOne = "Prueba · último día",
        NotchProActive = "Activado. ¡Gracias!",
        NotchProFeatures = new[]
        {
            "Permitir / Denegar desde el notch", "Música y letras sincronizadas", "Tareas con avisos", "Bandeja de archivos",
            "Notch en todos los monitores", "Gráficas de uso y pomodoro avanzado",
        },
        NotchProBuy = "Comprar",
        NotchProRestore = "Restaurar compra",
        NotchProGetInStore = "Conseguir Pro en Microsoft Store",
        NotchProBusy = "Abriendo Microsoft Store…",
        NotchProFailed = "La compra no se completó.",
        NotchProOffline = "La Store no responde.",
        NotchProUnavailable = "Pro aún no está en la Store.",
        NotchProNotOwned = "Esta cuenta no tiene Pro.",
        NotchProElsewhere = "Haz clic para comprar desde esta pantalla",
        NotchProTrialEndedTitle = "Tu prueba de Pro ha terminado",
        NotchProTrialEndedDetail = "Tus tareas y la bandeja se conservan.",
        OnbProTrialNote = "Pro: gratis 7 días; después, pago único.",
        OnbProStoreNote = "Pro: en la edición de Microsoft Store.",
        OnbProBuyNote = "Pro: pago único, desde la tarjeta Pro del notch.",
        OnbProActiveNote = "Tienes Muesca Pro.",
        TapActiveFmt = "Activo · último dato hace {0}",
        TapWaiting = "Activo · esperando una sesión de Claude Code",
        TapOff = "Sin activar · pregunta a la API en cada refresco",
        TapOffNoApi = "Sin activar · actívalo para ver la cuota",
        TapUnavailable = "No disponible en esta instalación",
        TapEnableConfirm =
            "Muesca leerá la cuota desde la statusline de Claude Code, sin peticiones a la API.\n\n" +
            "Añade un pequeño paso delante de tu statusline en ~/.claude/settings.json: se seguirá viendo igual. " +
            "Si no tienes, Muesca pone una compacta (Claude Code oculta entonces algunas pistas del pie).\n\n" +
            "Antes se hace una copia de seguridad de settings.json. ¿Continuar?",
        TapDisableConfirm =
            "¿Dejar de leer la cuota desde Claude Code?\n\n" +
            "Se restaura tu statusline original tal cual y Muesca vuelve a preguntar a la API. " +
            "Antes se hace una copia de seguridad de settings.json.",
        TapEnabledFmt = "Leyendo la cuota desde Claude Code. Copia: {0}",
        TapDisabled = "Statusline restaurada. De vuelta a la API.",
        TapDisableConfirmNoApi =
            "¿Dejar de leer la cuota desde Claude Code?\n\n" +
            "Se restaura tu statusline original tal cual. La lectura con tu sesión de Claude Code está apagada, así que " +
            "Muesca se quedará sin cuota que enseñar. Antes se hace una copia de seguridad de settings.json.",
        TapDisabledNoApi = "Statusline restaurada. Muesca se queda sin origen de la cuota.",
        TapCompoundError = "Tu statusline es un comando compuesto (&&, ;): Muesca no se engancha para no romperla. Sigue con la API.",
        TapTypeError = "Tu statusline no es un comando; Muesca no puede leerla. Sigue con la API.",
        TapSuggest = "Muesca puede leer tu cuota desde Claude Code sin peticiones a la API (adiós a los límites). Pulsa para activarlo.",
        QuotaOAuthLabel = "Leer la cuota con tu sesión de Claude Code (no oficial)",
        QuotaOAuthSubtitle = "Usa el token local de Claude Code solo con api.anthropic.com. No lo guarda ni lo envía a " +
            "ningún otro sitio. Anthropic no lo documenta, así que puede dejar de funcionar",
        QuotaCliRefreshLabel = "Forzar renovación con la CLI (gasta un mensaje)",
        QuotaCliRefreshSubtitle = "Solo con el token caducado: ejecuta `claude -p .`, como mucho cada 30 min. Apagado: abre Claude Code para renovarlo",
        NotchDone = "Terminado",
        NotchWaitingOk = "Espera tu OK",
        NotchAnswerInTerminal = "Responde en la terminal",
        NotchAllow = "Permitir",
        NotchDeny = "Denegar",
        NotchAllowed = "Permitido",
        NotchDenied = "Denegado",
        NotchQuotaFmt = "Cuota {0} al {1}",
        NotchMoreSessionsFmt = "+{0} más",
        NotchPageMusic = "Música",
        NotchPageTasks = "Tareas",
        NotchPageShelf = "Bandeja",
        NotchNothingPlaying = "No suena nada",
        NotchNoTasks = "Sin tareas",
        NotchShelfEmpty = "La bandeja está vacía",
        NotchShelfClear = "Vaciar",
        NotchShelfRemoveFmt = "Quitar ({0})",
        NotchShelfMissing = "No disponible",
        NotchShelfCopied = "Copiado",
        NotchShelfDropHere = "Suelta aquí",
        NotchShelfFolder = "Carpeta",
        NotchShelfFile = "Archivo",
        NotchShelfLink = "Enlace",
        NotchShelfText = "Texto",
        NotchTaskAdd = "+ Añadir",
        NotchTaskPlaceholder = "Nueva tarea…",
        NotchTaskSave = "Guardar",
        NotchTaskTomorrow9 = "Mañana 9:00",
        NotchTomorrow = "mañana",
        NotchReminder = "Recordatorio",
        NotchReminderDone = "Hecha",
        NotchLyrics = "Letra",
        NotchLyricsLoading = "Buscando la letra…",
        NotchNoLyrics = "Sin letra",
        NotchLyricsUnavailable = "Letra no disponible",
        NotchLyricsUnsynced = "Letra sin sincronizar",
        NotchLyricsOther = "Otra letra",
        NotchLyricsOffsetFmt = "Desfase {0}",
        NotchHeader = "Notch",
        NotchSubtitle = "Pastilla arriba de la pantalla con cuota, actividad y avisos",
        NotchSize = "Tamaño",
        NotchSizeAuto = "Auto",
        NotchOpenWith = "Abrir con",
        NotchOpenHover = "Pasar el ratón",
        NotchOpenClick = "Clic",
        NotchMonitor = "Monitor",
        NotchMonitorPrimary = "Principal",
        NotchMonitorMenu = "Monitor del notch",
        NotchMonitorFollowPrimary = "Principal (seguir al principal)",
        NotchMonitorFallbackFmt = "Monitor {0}",
        NotchMonitorIsPrimary = "principal",
        NotchMonitorLeft = "izquierda",
        NotchMonitorRight = "derecha",
        NotchMonitorAbove = "arriba",
        NotchMonitorBelow = "abajo",
        NotchMonitorModeFixed = "Fijo",
        NotchMonitorModeFollow = "Seguir a la ventana activa",
        NotchMonitorModeAll = "Todos los monitores",
        NotchMonitorFixed = "Monitor fijo",
        NotchMirrorClickHint = "Haz clic para responder aquí",
        NotchMinimal = "Modo mínimo en reposo",
        NotchAutoMinimal = "Encoger a una raya tras 15 s sin actividad",
        NotchHideInFullscreen = "Ocultar en pantalla completa",
        NotchSound = "Sonido al terminar, al pedir permiso y en el aviso de cuota",
        NotchClock = "Reloj en reposo",
        NotchMascot = "Mascota",
        NotchMusic = "Música",
        NotchLyricsSetting = "Letras de LRCLIB",
        NotchLyricsSubtitle = "Envía título y artista a lrclib.net",
        NotchCompactLyrics = "Letra en el notch pequeño",
        NotchCompactLyricsSubtitle = "Pide la letra de cada canción al sonar, aunque la vista de letra esté cerrada",
        NotchCompactLyricsArt = "Carátula en la letra",
        NotchCompactLyricsQuota = "Cuota en la letra",
        NotchTasks = "Tareas y recordatorios",
        NotchShelfClearOnStart = "Bandeja: vaciar al arrancar",
        NotchAdaptiveContrast = "Contraste adaptativo",
        NotchPermissions = "Responder permisos desde el notch",
        NotchPermissionsSubtitle = "Botones Permitir / Denegar; si algo falla, Claude Code muestra su diálogo de siempre",
        NotchPermissionsEnableConfirm =
            "Muesca te dejará contestar los permisos de Claude Code con los botones Permitir / Denegar del notch.\n\n" +
            "Añade una segunda entrada en PermissionRequest de ~/.claude/settings.json (claudebar-helper permission, " +
            "timeout 45 s). Nada se decide sin tu clic: si Muesca está cerrada, ocupada o no contestas en 25 s, " +
            "Claude Code muestra su diálogo de siempre.\n\n" +
            "Antes se hace una copia de seguridad de settings.json. ¿Continuar?",
        NotchPermissionsDisableConfirm =
            "¿Dejar de contestar permisos desde el notch?\n\n" +
            "Se quita la entrada de permisos de ~/.claude/settings.json (las sesiones en vivo siguen igual) y Claude Code " +
            "vuelve a preguntar solo en la terminal. Antes se hace una copia de seguridad de settings.json.",
        NotchPermissionsEnabledFmt = "Permisos desde el notch activados. Copia: {0}",
        NotchPermissionsDisabled = "Permisos desde el notch desactivados. Claude Code pregunta en la terminal.",
        NotchPermissionsNeedHooks = "Activa antes las sesiones en vivo: los permisos desde el notch usan los mismos hooks.",
        NotchUpdateFmt = "Nueva versión {0}",
        NotchUpdateAction = "Actualizar",
        NotchUpdateDownloading = "Descargando…",
        NotchUpdateElsewhere = "Lista para instalar",
        MenuReconfigure = "Volver a configurar…",
        OnbSkip = "Saltar · Esc",
        OnbBack = "Atrás",
        OnbNext = "Siguiente",
        OnbStart = "Empezar",
        OnbTagline = "Tu cuota de Claude Code, siempre a la vista.",
        OnbClaudeDetected = "Claude Code detectado",
        OnbClaudeNotFound = "No encontramos Claude Code: puedes instalarlo luego",
        OnbKeysHint = "Enter para seguir · Tab para moverte · Esc para saltar",
        OnbProfileTitle = "¿Para qué vas a usar Muesca?",
        OnbProfileSubtitle = "Elige un punto de partida. Puedes afinarlo en las páginas siguientes o luego en los ajustes.",
        OnbProfileNames = new[] { "Solo cuota", "Programo con Claude Code", "Notch completo", "Minimalista / juego", "Personalizado" },
        OnbProfileLines = new[]
        {
            "Icono de bandeja y panel. Sin notch.",
            "Sesiones en vivo, avisos, música y letras.",
            "Todo eso y letra pequeña, reloj, gato y tareas.",
            "Una raya que se esconde al jugar. Sin ruido.",
            "Tú eliges cada opción.",
        },
        OnbRecommended = "Recomendado",
        OnbPreviewCaption = "Vista previa en vivo: así se verá arriba de tu pantalla.",
        OnbPreviewNoNotch = "Sin notch: tu cuota vive en el icono de la bandeja y su panel.",
        OnbClaudeMissing =
            "No hemos encontrado Claude Code en este equipo. Muesca lo necesita para leer tu cuota: instálalo e inicia sesión cuando quieras.",
        OnbFeaturesTitle = "Qué se activa",
        OnbFeaturesSubtitleFmt = "Marcado con los valores de «{0}». Cambia lo que quieras.",
        OnbFeatureNames = new[]
        {
            "Notch arriba de la pantalla", "Raya en reposo", "Ocultar en pantalla completa", "Sonidos", "Música",
            "Letras sincronizadas", "Letra en el notch pequeño", "Reloj", "El gato", "Tareas y recordatorios", "Reducir movimiento",
        },
        OnbFeatureDetails = new[]
        {
            "cuota, actividad y avisos", "en lugar de la pastilla con el % de 5 h", "juegos, vídeo, presentaciones",
            "campanita al terminar y al 95 %", "la canción que suena", "de lrclib.net", "la línea que suena en vez del título",
            "en reposo", "en reposo", "en el notch abierto", "sin animaciones en el panel ni en el notch",
        },
        OnbFeaturesHint =
            "La música solo lee lo que suena en Windows (no sale nada del PC). Las letras envían título y artista a lrclib.net.",
        OnbReset = "Restablecer recomendados",
        OnbSettingsTitle = "Ajustes recomendados",
        OnbSettingsSubtitle = "Ya puestos con lo que mejor le va a este equipo.",
        OnbSettingsSubtitleKeep = "Tus valores actuales; «recomendado» marca lo que mejor le va a este equipo.",
        OnbLanguageSystemFmt = "Como Windows ({0})",
        OnbLanguageHint = "Los textos de Muesca. «Como Windows» sigue el idioma del sistema.",
        OnbMonitorPrimary = "El principal",
        OnbMonitorHintFmt = "Tienes {0} monitores. «El principal» sigue al que sea el principal.",
        OnbSize = "Tamaño del notch",
        OnbSizeHintFmt = "Auto se adapta al ancho y la escala (DPI): aquí, {0} px lógicos (≈ {1} %).",
        OnbOpen = "Abrir el notch",
        OnbOpenHover = "Al pasar el ratón",
        OnbOpenClick = "Con un clic",
        OnbOpenHintHover = "Basta con dejar el ratón un momento encima.",
        OnbOpenHintAbove = "Hay un monitor justo encima: al subir el ratón cruzarías el notch.",
        OnbOpenHintSmallFmt = "Pantalla pequeña ({0} px lógicos de ancho): abierto, el notch taparía mucho.",
        OnbStartupHint = "Muesca te espera en la bandeja desde que entras en Windows.",
        OnbStartupUnknownHint = "Aún no se ha podido leer cómo está: si no lo tocas, se queda como esté.",
        OnbUnchanged = "sin cambios",
        OnbNotchOff = "El notch está apagado: esto solo cuenta si lo enciendes luego.",
        OnbRecommendedTag = "recomendado",
        OnbClaudeTitle = "Claude Code",
        OnbClaudeSubtitle = "Sesiones en vivo y permisos desde el notch.",
        OnbClaudeFoundFmt = "Claude Code está instalado ({0}).",
        OnbLiveSessions = "Activar las sesiones en vivo",
        OnbLiveSessionsActive = "Las sesiones en vivo ya están activadas",
        OnbLiveSessionsHint =
            "Instala unos hooks en ~/.claude/settings.json, la configuración global de Claude Code (antes hace una copia), para " +
            "que el notch y el panel vean qué hace cada sesión. Se pueden quitar cuando quieras desde el menú de la bandeja › " +
            "Desactivar (quitar hooks); al desinstalar también se quitan.",
        OnbLiveSessionsActiveHint = "No hay nada que instalar. Para quitarlas: menú de la bandeja › Desactivar (quitar hooks).",
        OnbPermissions = "Contestar los permisos desde el notch (opcional)",
        OnbPermissionsHint =
            "Cuando Claude Code pida permiso (ejecutar un comando, editar un archivo…), lo permites o deniegas con los botones " +
            "del notch. Nada se decide sin tu clic: si Muesca está cerrada o no contestas en 25 s, sale el diálogo de siempre " +
            "de Claude Code. Añade una entrada más a settings.json.",
        OnbPermissionsNeed = "— necesita el notch y las sesiones en vivo",
        OnbSummaryTitle = "Todo listo",
        OnbSummarySubtitle = "Esto es lo que se va a aplicar. Todo se puede cambiar luego en los ajustes.",
        OnbSumProfile = "Perfil",
        OnbSumNotch = "Notch",
        OnbSumFeatures = "Activado",
        OnbSumLive = "Sesiones en vivo",
        OnbSumPermissions = "Permisos desde el notch",
        OnbNotchOnFmt = "sí · {0} · tamaño {1} · {2}",
        OnbNothingElse = "nada más",
        OnbYes = "sí",
        OnbNo = "no",
        OnbLiveInstall = "se activan (hooks en ~/.claude/settings.json, con copia)",
        OnbLiveActive = "ya activadas",
        OnbBackupNote = "Antes se guarda una copia de tu config.json actual.",
        OnbSkipTitle = "¿Saltar la configuración?",
        OnbSkipTextFmt =
            "Muesca empezará con «{0}» y los ajustes que mejor le van a este equipo. Las sesiones en vivo no se activan: puedes hacerlo luego desde la bandeja.",
        OnbCloseTitle = "¿Cerrar sin cambiar nada?",
        OnbCloseText = "Tu configuración actual se queda como está. Puedes volver a configurar Muesca desde la bandeja.",
        OnbKeepGoing = "Seguir configurando",
        OnbSkipConfirm = "Saltar",
        OnbClose = "Cerrar",
        OnbErrorTitle = "No se ha podido aplicar tu configuración",
        OnbErrorTextFmt = "No se ha cambiado nada ({0}). Detalles en claudebar.log.",
        OnbRetry = "Reintentar",
        OnbHooksFailed =
            "Se ha aplicado tu configuración, pero no se han podido activar las sesiones en vivo: no se pudo cambiar " +
            "~/.claude/settings.json. Puedes activarlas luego desde el menú de la bandeja › Activar (instalar hooks).",
        OnbInTray = "Muesca está en la bandeja: haz clic en su icono para ver tu cuota.",
        MascotVerbsProcessing = new[] { "pensando", "cocinando", "tramando", "dándole al coco", "maquinando" },
        MascotVerbsWaitingApproval = new[] { "tú decides", "un visto bueno", "psst, oye" },
        MascotVerbsWaitingInput = new[] { "te toca", "soy todo oídos", "aquí esperando", "cuando quieras" },
        MascotVerbsCompacting = new[] { "ordenando", "plegando memoria", "comprimiendo", "haciendo hueco" },
        MascotVerbsIdle = new[] { "echando la siesta", "haciendo el vago", "soñando despierto", "estirándome" },
        MascotVerbsEnded = new[] { "listo", "asunto cerrado", "fuera de combate" },
        // Contador anónimo de uso (PRIVACY.md)
        UsageStatsLabel = "Contador anónimo de uso",
        UsageStatsSubtitle = "Cuenta cuánta gente usa Muesca, sin saber quién. Una vez al día manda solo versión, edición e idioma.",
        UsageStatsPrivacy = "Política de privacidad",
        UsageStatsNotice = "Novedad: Muesca cuenta cuánta gente la usa, sin saber quién. Una vez al día manda solo versión, edición e idioma. Puedes apagarlo en Ajustes › Sistema."
    };

    private static readonly Strings Dutch = new()
    {
        Culture = CultureInfo.GetCultureInfo("nl-NL"),
        Tut = TutorialStrings.Dutch,
        Dashboard = "Dashboard",
        Refresh = "Nu verversen",
        PanelWindow = "Paneelvenster",
        Position = "Positie",
        PosBottomRight = "Rechtsonder",
        PosBottomLeft = "Linksonder",
        PosTopRight = "Rechtsboven",
        PosTopLeft = "Linksboven",
        PosCenter = "Midden",
        PosCustom = "Aangepast (sleep het paneel)",
        Sticky = "Vastgezet (niet automatisch sluiten)",
        AlwaysOnTop = "Altijd op voorgrond",
        ReduceMotion = "Beweging beperken",
        ReduceMotionSubtitle = "Schakelt animaties uit",
        UpdateFrequency = "Verversingsfrequentie",
        Sec30 = "30s",
        Min1 = "1m",
        Min5 = "5m",
        Min15 = "15m",
        Notifications = "Meldingen",
        Enabled = "Ingeschakeld",
        NotifyWhenReaching = "Meldingsdrempel",
        ColorThreshold = "Kleurdrempel",
        DefaultTag = "(standaard)",
        Settings = "Instellingen",
        ShowSpend = "Geschatte uitgaven tonen",
        ShowSpendSubtitle = "API-equivalente kosten per model",
        StartWithWindows = "Starten met Windows",
        StartupChecking = "Controleren…",
        StartupChanging = "Wijzigen…",
        EditConfig = "Config bewerken (geavanceerd)…",
        OpenDataFolder = "Datamap openen",
        Language = "Taal",
        SystemDefault = "Systeemstandaard",
        Exit = "Afsluiten",
        SessionWord = "Sessie",
        WeekWord = "Week",
        ResetsIn = "reset over",
        Resetting = "resetten…",
        RollingHint = "voortschrijdend venster van 5u vanaf je 1e verzoek",
        SpendHeaderFormat = "Geschatte uitgaven ({0}d, API-equiv.)",
        Loading = "Laden…",
        UpdatedAt = "Bijgewerkt",
        AgoFormat = "{0} geleden",
        StaleLabel = "verouderd",
        LocalSeal = "Je inloggegevens en data verlaten dit apparaat nooit",
        HintClickToHide = "klik op het pictogram om te verbergen",
        HintPinnedClose = "vastgezet · ✕ om te sluiten",
        PreviousDataTip = "⚠ vorige gegevens (offline)",
        PreviousDataFooter = "vorige gegevens",
        QuotaRenewed = "quota vernieuwd",
        StateNoCredentials = "Niet aangemeld — log in bij Claude Code",
        StateAuthExpired = "Sessie verlopen — open Claude Code",
        StateRateLimited = "Verzoeklimiet — opnieuw proberen",
        StateNetworkError = "Geen verbinding met Anthropic",
        NotifQuotaFormat = "Claude {0}%+ quota gebruikt",
        Theme = "Thema",
        ThemeSystem = "Systeem",
        ThemeDark = "Donker",
        ThemeLight = "Licht",
        ThemeCli = "CLI",
        ThemeImported = "Geïmporteerd",
        ImportTheme = "Importeer .itermcolors…",
        ShowServiceStatus = "Servicestatus tonen",
        HealthOk = "Operationeel",
        HealthDegraded = "Verminderd",
        HealthOutage = "Storing",
        Back = "Terug",
        UsageChart = "Gebruiksgrafiek",
        NoData = "Geen gegevens in dit bereik",
        OpenBilling = "Facturering openen…",
        ChartPeak = "max",
        ChartTotal = "totaal",
        ChartTabSpend = "Uitgaven $",
        ChartTabPct = "Quota %",
        ModelFamilyOther = "Overig",
        SpendNoRate = "geen tarief",
        PricingOnlineUpdate = "Tarieven online bijwerken",
        PricingOnlineUpdateSubtitle = "Downloadt publieke prijzen van models.dev · verstuurt niets van jou",
        Opacity = "Dekking",
        IconMode = "Pictogrammodus",
        PaceAlerts = "Tempo-meldingen",
        WinSession = "sessie (5h)",
        WinWeekly = "wekelijks (7d)",
        PaceAlertTitle = "⚠ Quotatempo",
        PaceAlertBodyFmt = "Met dit tempo is je {0}-quota op rond {1}, vóór de reset",
        PaceEtaLabel = "op",
        MenuAppearance = "Weergave",
        MenuSections = "Paneelinhoud",
        MenuIcon = "Pictogram",
        MenuAdvanced = "Geavanceerd",
        MenuSystem = "Systeem",
        VersionLabel = "Versie",
        About = "Over",
        AboutProduct = "Muesca, de notch voor Claude Code · door Bekay",
        AboutDisclaimer = "Niet gelieerd aan Anthropic. Claude is een handelsmerk van Anthropic.",
        CheckUpdates = "Controleren op updates",
        Checking = "Controleren op updates…",
        UpdateAvailableFmt = "⬇ Update beschikbaar {0}",
        UpToDateFmt = "Je bent up-to-date (v{0}).",
        UpdatePromptFmt = "Huidige versie: v{0}\nNieuwste: {1}\n\nNu downloaden?",
        UpdateDownloadedFmt = "Gedownload naar:\n{0}\n\nSluit Muesca en vervang de actieve .exe door dit bestand.",
        UpdateFailed = "Kan de update niet controleren/downloaden.",
        MenuLiveSessions = "Live sessies",
        LiveSessionsSubtitle = "Installeert/verwijdert hooks in ~/.claude/settings.json",
        RemoveIntegration = "Integratie met Claude Code verwijderen",
        RemoveIntegrationHint = "Haalt de hooks en de statusregel van Muesca uit settings.json van Claude Code. Doe dit vóór het verwijderen.",
        RemoveIntegrationConfirm = "Hiermee worden alleen de regels van Muesca uit settings.json van Claude Code (~/.claude/settings.json) verwijderd: de hooks van live sessies en toestemmingen en de koppeling van de statusregel. Je eigen statusregel komt precies terug zoals hij was en verder wordt niets aangeraakt. Eerst wordt een back-up van settings.json gemaakt.\n\nDoe dit vóór het verwijderen van Muesca. Doorgaan?",
        RemoveIntegrationDoneFmt = "Integratie met Claude Code verwijderd. Back-up van settings.json: {0}",
        RemoveIntegrationNothing = "settings.json van Claude Code bevat geen regels van Muesca: er is niets te verwijderen.",
        MenuShowMascot = "Mascotte tonen",
        MenuSuppressWhenFocused = "Dempen als terminal focus heeft",
        MenuInstallHooks = "Inschakelen (hooks installeren)…",
        MenuUninstallHooks = "Uitschakelen (hooks verwijderen)",
        NoActiveSessions = "Geen actieve sessies",
        SessionPhaseIdle = "inactief",
        SessionPhaseProcessing = "bezig",
        SessionPhaseWaitingApproval = "wacht op jouw OK",
        SessionPhaseWaitingInput = "jouw beurt",
        SessionPhaseCompacting = "comprimeren",
        NotifWaitingApprovalFmt = "Claude wacht op jouw OK in {0}",
        NotifWaitingInputFmt = "Claude is klaar in {0}",
        LiveSessionsTitle = "Claude-sessies",
        HooksInstalledFmt = "Live sessies aan. Back-up: {0}",
        HooksRemoved = "Live sessies uit. Hooks verwijderd.",
        Changelog = "Wat is er nieuw",
        BadgeActive = "Actief",
        BadgeInstall = "Installeren",
        QuotaSourceHeader = "Bron van het quotum",
        QuotaFromClaudeCode = "Quotum uit Claude Code lezen",
        MenuProFmt = "Muesca Pro… ({0})",
        ProStateFree = "Gratis",
        ProStateTrialFmt = "Proef: {0} dagen",
        ProStateTrialOne = "Proef: 1 dag",
        ProMonitorsLocked = "Meerdere monitoren: Muesca Pro…",
        ProThanks = "Muesca Pro is actief. Bedankt!",
        NotchProLockedMusic = "Muziek en gesynchroniseerde songteksten met Muesca Pro.",
        NotchProLockedTasks = "Taken met herinneringen met Muesca Pro. Je taken blijven bewaard.",
        NotchPageUsage = "Gebruik",
        NotchUsagePaceFmt = "tempo {0}",
        NotchUsageRunsOutFmt = "op om {0}",
        NotchUsageRunsOutDayFmt = "op vanaf {0}",
        NotchUsageLoadFailed = "Kon dit bereik niet laden · opnieuw proberen",
        NotchUsageAtResetFmt = "≈ {0} bij reset",
        NotchUsageExtraOn = "Extra gebruik aan",
        NotchUsageEmptySpend = "Nog geen uitgaven in dit bereik",
        NotchUsageEmptySpendHint = "Vult zich terwijl je Claude Code gebruikt",
        NotchUsageEmptyQuota = "Nog geen quotageschiedenis",
        NotchUsageEmptyQuotaHint = "Muesca bewaart je quota-% elke minuut",
        NotchUsageNow = "nu",
        NotchUsageReset = "vernieuwd",
        NotchPageFocus = "Focus",
        NotchFocusPhaseFocus = "Focus",
        NotchFocusPhaseShort = "Korte pauze",
        NotchFocusPhaseLong = "Lange pauze",
        NotchFocusStart = "Starten",
        NotchFocusPause = "Pauzeren",
        NotchFocusResume = "Hervatten",
        NotchFocusStartBreak = "Pauze starten",
        NotchFocusStartFocus = "Focus starten",
        NotchFocusSkipHint = "Naar de volgende fase",
        NotchFocusResetPhase = "Deze fase opnieuw",
        NotchFocusResetCycle = "Cyclus opnieuw beginnen",
        NotchFocusCycleFmt = "{0} van {1}",
        NotchFocusPaused = "gepauzeerd",
        NotchFocusDoneFocus = "Focus klaar!",
        NotchFocusDoneBreak = "Pauze voorbij",
        NotchFocusTodayOne = "1 focussessie vandaag",
        NotchFocusTodayFmt = "{0} focussessies vandaag",
        NotchFocusMinutesFmt = "{0} min",
        NotchFocusSecondsFmt = "{0} s",
        NotchFocusHoursFmt = "{0} u",
        NotchFocusLinkTask = "Koppel een taak",
        NotchFocusNoTasks = "Geen openstaande taken",
        NotchFocusStats = "Statistieken van deze week",
        NotchFocusWeekFmt = "Deze week: {0} sessies · {1}",
        NotchFocusWeekOneFmt = "Deze week: 1 sessie · {0}",
        NotchFocusNextFmt = "Volgende: {0}",
        NotchFocusSoundSetting = "Focus: geluid aan het eind van een fase",
        NotchFocusLength = "Focusduur",
        NotchFocusShortLength = "Korte pauze",
        NotchFocusLongLength = "Lange pauze",
        NotchFocusLongEvery = "Lange pauze na (sessies)",
        NotchProLockedShelf = "Het bestandsvak met Muesca Pro. Wat je erin liet vallen, blijft bewaard.",
        NotchProLockedHint = "Klik om te zien wat Pro biedt",
        NotchProPriceFmt = "{0} · eenmalige betaling",
        NotchProOneTime = "Eenmalige betaling",
        NotchProTrialFmt = "Proef · nog {0} dagen",
        NotchProTrialOne = "Proef · laatste dag",
        NotchProActive = "Actief. Bedankt!",
        NotchProFeatures = new[]
        {
            "Toestaan / weigeren vanuit de notch", "Muziek en gesynchroniseerde songteksten", "Taken met herinneringen",
            "Bestandsvak", "Notch op elke monitor", "Gebruiksgrafieken en geavanceerde pomodoro",
        },
        NotchProBuy = "Kopen",
        NotchProRestore = "Aankoop herstellen",
        NotchProGetInStore = "Pro halen in Microsoft Store",
        NotchProBusy = "Microsoft Store wordt geopend…",
        NotchProFailed = "Aankoop niet voltooid.",
        NotchProOffline = "De Store reageert niet.",
        NotchProUnavailable = "Pro staat nog niet in de Store.",
        NotchProNotOwned = "Geen Pro op dit account.",
        NotchProElsewhere = "Klik om vanaf dit scherm te kopen",
        NotchProTrialEndedTitle = "Je Pro-proefperiode is afgelopen",
        NotchProTrialEndedDetail = "Je taken en het vak blijven bewaard.",
        OnbProTrialNote = "Pro: 7 dagen gratis, daarna eenmalig betalen.",
        OnbProStoreNote = "Pro: in de Microsoft Store-editie.",
        OnbProBuyNote = "Pro: eenmalige betaling, via de Pro-kaart in de notch.",
        OnbProActiveNote = "Je hebt Muesca Pro.",
        TapActiveFmt = "Aan · laatste gegevens {0} geleden",
        TapWaiting = "Aan · wacht op een Claude Code-sessie",
        TapOff = "Uit · vraagt de API bij elke verversing",
        TapUnavailable = "Niet beschikbaar in deze installatie",
        TapEnableConfirm =
            "Muesca leest je quotum uit de statusregel van Claude Code, zonder verzoeken aan de API.\n\n" +
            "Het voegt een kleine stap toe vóór je statusregel in ~/.claude/settings.json: je statusregel blijft er hetzelfde uitzien. " +
            "Heb je er geen, dan voegt Muesca een compacte toe (Claude Code verbergt dan enkele hints in de voettekst).\n\n" +
            "Eerst wordt een back-up van settings.json gemaakt. Doorgaan?",
        TapDisableConfirm =
            "Stoppen met het quotum uit Claude Code te lezen?\n\n" +
            "Je oorspronkelijke statusregel wordt exact hersteld en Muesca vraagt weer de API. " +
            "Eerst wordt een back-up van settings.json gemaakt.",
        TapEnabledFmt = "Quotum wordt uit Claude Code gelezen. Back-up: {0}",
        TapDisabled = "Statusregel hersteld. Terug naar de API.",
        TapCompoundError = "Je statusregel is een samengestelde opdracht (&&, ;): Muesca haakt er niet in om hem niet te breken. De API blijft in gebruik.",
        TapTypeError = "Je statusregel is geen opdracht; Muesca kan hem niet lezen. De API blijft in gebruik.",
        TapSuggest = "Muesca kan je quotum uit Claude Code lezen zonder API-verzoeken (geen rate limits meer). Klik om het in te stellen.",
        MascotVerbsProcessing = new[] { "aan het denken", "aan het koken", "aan het knutselen", "aan het broeden", "aan het smeden" },
        MascotVerbsWaitingApproval = new[] { "jouw keuze", "een knikje graag", "psst, jij daar" },
        MascotVerbsWaitingInput = new[] { "jouw beurt", "een en al oor", "wacht op je", "ga je gang" },
        MascotVerbsCompacting = new[] { "opruimen", "geheugen vouwen", "comprimeren", "ruimte maken" },
        MascotVerbsIdle = new[] { "dutje doen", "lekker luieren", "dagdromen", "uitrekken" },
        MascotVerbsEnded = new[] { "klaar", "afgerond", "knock-out" },
        // Contador anónimo de uso (PRIVACY.md)
        UsageStatsLabel = "Anonieme gebruiksteller",
        UsageStatsSubtitle = "Telt hoeveel mensen Muesca gebruiken, zonder te weten wie. Eén keer per dag stuurt het alleen versie, editie en taal.",
        UsageStatsPrivacy = "Privacybeleid",
        UsageStatsNotice = "Nieuw: Muesca telt hoeveel mensen het gebruiken, zonder te weten wie. Eén keer per dag stuurt het alleen versie, editie en taal. Uitzetten kan in Instellingen › Systeem."
    };

    private static readonly Strings French = new()
    {
        Culture = CultureInfo.GetCultureInfo("fr-FR"),
        Tut = TutorialStrings.French,
        Dashboard = "Tableau de bord",
        Refresh = "Actualiser maintenant",
        PanelWindow = "Fenêtre du panneau",
        Position = "Position",
        PosBottomRight = "En bas à droite",
        PosBottomLeft = "En bas à gauche",
        PosTopRight = "En haut à droite",
        PosTopLeft = "En haut à gauche",
        PosCenter = "Centre",
        PosCustom = "Personnalisée (glissez le panneau)",
        Sticky = "Épinglé (ne pas fermer auto)",
        AlwaysOnTop = "Toujours au premier plan",
        ReduceMotion = "Réduire les animations",
        ReduceMotionSubtitle = "Désactive les animations",
        UpdateFrequency = "Fréquence d'actualisation",
        Sec30 = "30s",
        Min1 = "1m",
        Min5 = "5m",
        Min15 = "15m",
        Notifications = "Notifications",
        Enabled = "Activées",
        NotifyWhenReaching = "Seuil d'alerte",
        ColorThreshold = "Seuil de couleur",
        DefaultTag = "(déf.)",
        Settings = "Paramètres",
        ShowSpend = "Afficher le coût estimé",
        ShowSpendSubtitle = "Coût équivalent API par modèle",
        StartWithWindows = "Démarrer avec Windows",
        StartupChecking = "Vérification…",
        StartupChanging = "Modification…",
        EditConfig = "Éditer la config (avancé)…",
        OpenDataFolder = "Ouvrir le dossier de données",
        Language = "Langue",
        SystemDefault = "Système (par défaut)",
        Exit = "Quitter",
        SessionWord = "Session",
        WeekWord = "Semaine",
        ResetsIn = "réinit. dans",
        Resetting = "réinitialisation…",
        RollingHint = "fenêtre glissante de 5h depuis ta 1re requête",
        SpendHeaderFormat = "Coût estimé ({0}j, équiv. API)",
        Loading = "Chargement…",
        UpdatedAt = "Mis à jour",
        AgoFormat = "il y a {0}",
        StaleLabel = "obsolète",
        LocalSeal = "Tes identifiants et tes données ne quittent jamais cet appareil",
        HintClickToHide = "cliquez sur l'icône pour masquer",
        HintPinnedClose = "épinglé · ✕ pour fermer",
        PreviousDataTip = "⚠ données précédentes (hors ligne)",
        PreviousDataFooter = "données précédentes",
        QuotaRenewed = "quota renouvelé",
        StateNoCredentials = "Non connecté — connectez-vous à Claude Code",
        StateAuthExpired = "Session expirée — ouvrez Claude Code",
        StateRateLimited = "Limite de requêtes — nouvelle tentative",
        StateNetworkError = "Pas de connexion à Anthropic",
        NotifQuotaFormat = "Claude {0}%+ de quota utilisé",
        Theme = "Thème",
        ThemeSystem = "Système",
        ThemeDark = "Sombre",
        ThemeLight = "Clair",
        ThemeCli = "CLI",
        ThemeImported = "Importé",
        ImportTheme = "Importer .itermcolors…",
        ShowServiceStatus = "Afficher l'état du service",
        HealthOk = "Opérationnel",
        HealthDegraded = "Dégradé",
        HealthOutage = "Panne",
        Back = "Retour",
        UsageChart = "Graphique d'utilisation",
        NoData = "Aucune donnée sur cette période",
        OpenBilling = "Ouvrir la facturation…",
        ChartPeak = "max",
        ChartTotal = "total",
        ChartTabSpend = "Coût $",
        ChartTabPct = "Quota %",
        ModelFamilyOther = "Autres",
        SpendNoRate = "sans tarif",
        PricingOnlineUpdate = "Mettre à jour les tarifs en ligne",
        PricingOnlineUpdateSubtitle = "Télécharge les prix publics de models.dev · n'envoie rien de toi",
        Opacity = "Opacité",
        IconMode = "Mode d'icône",
        PaceAlerts = "Alertes de rythme",
        WinSession = "de session (5h)",
        WinWeekly = "hebdomadaire (7d)",
        PaceAlertTitle = "⚠ Rythme de quota",
        PaceAlertBodyFmt = "À ce rythme, tu épuises le quota {0} vers {1}, avant la réinitialisation",
        PaceEtaLabel = "épuisé",
        MenuAppearance = "Apparence",
        MenuSections = "Contenu du panneau",
        MenuIcon = "Icône",
        MenuAdvanced = "Avancé",
        MenuSystem = "Système",
        VersionLabel = "Version",
        About = "À propos",
        AboutProduct = "Muesca, le notch pour Claude Code · par Bekay",
        AboutDisclaimer = "Non affilié à Anthropic. Claude est une marque d'Anthropic.",
        CheckUpdates = "Rechercher des mises à jour",
        Checking = "Recherche de mises à jour…",
        UpdateAvailableFmt = "⬇ Mise à jour disponible {0}",
        UpToDateFmt = "Tu es à jour (v{0}).",
        UpdatePromptFmt = "Version actuelle : v{0}\nDernière : {1}\n\nTélécharger maintenant ?",
        UpdateDownloadedFmt = "Téléchargé dans :\n{0}\n\nFerme Muesca et remplace le .exe en cours par ce fichier.",
        UpdateFailed = "Impossible de vérifier/télécharger la mise à jour.",
        MenuLiveSessions = "Sessions en direct",
        LiveSessionsSubtitle = "Installe/retire les hooks dans ~/.claude/settings.json",
        RemoveIntegration = "Retirer l'intégration avec Claude Code",
        RemoveIntegrationHint = "Retire les hooks et la ligne d'état de Muesca du fichier settings.json de Claude Code. À faire avant de désinstaller.",
        RemoveIntegrationConfirm = "Seules les entrées de Muesca seront retirées du fichier settings.json de Claude Code (~/.claude/settings.json) : les hooks des sessions en direct et des autorisations, et le raccordement de la ligne d'état. Votre propre ligne d'état revient exactement comme avant et rien d'autre n'est modifié. Une sauvegarde de settings.json est faite d'abord.\n\nÀ faire avant de désinstaller Muesca. Continuer ?",
        RemoveIntegrationDoneFmt = "Intégration avec Claude Code retirée. Sauvegarde de settings.json : {0}",
        RemoveIntegrationNothing = "Le fichier settings.json de Claude Code ne contient aucune entrée de Muesca : il n'y a rien à retirer.",
        MenuShowMascot = "Afficher la mascotte",
        MenuSuppressWhenFocused = "Muet si le terminal a le focus",
        MenuInstallHooks = "Activer (installer les hooks)…",
        MenuUninstallHooks = "Désactiver (retirer les hooks)",
        NoActiveSessions = "Aucune session active",
        SessionPhaseIdle = "au repos",
        SessionPhaseProcessing = "en cours",
        SessionPhaseWaitingApproval = "attend votre OK",
        SessionPhaseWaitingInput = "à vous",
        SessionPhaseCompacting = "compactage",
        NotifWaitingApprovalFmt = "Claude attend votre OK dans {0}",
        NotifWaitingInputFmt = "Claude a terminé dans {0}",
        LiveSessionsTitle = "Sessions Claude",
        HooksInstalledFmt = "Sessions en direct activées. Sauvegarde : {0}",
        HooksRemoved = "Sessions en direct désactivées. Hooks retirés.",
        Changelog = "Nouveautés",
        BadgeActive = "Actives",
        BadgeInstall = "Installer",
        QuotaSourceHeader = "Source du quota",
        QuotaFromClaudeCode = "Lire le quota depuis Claude Code",
        MenuProFmt = "Muesca Pro… ({0})",
        ProStateFree = "Gratuit",
        ProStateTrialFmt = "Essai : {0} jours",
        ProStateTrialOne = "Essai : 1 jour",
        ProMonitorsLocked = "Plusieurs écrans : Muesca Pro…",
        ProThanks = "Muesca Pro est activé. Merci !",
        NotchProLockedMusic = "Musique et paroles synchronisées avec Muesca Pro.",
        NotchProLockedTasks = "Tâches avec rappels avec Muesca Pro. Vos tâches sont conservées.",
        NotchPageUsage = "Utilisation",
        NotchUsagePaceFmt = "rythme {0}",
        NotchUsageRunsOutFmt = "épuisé à {0}",
        NotchUsageRunsOutDayFmt = "épuisé le {0}",
        NotchUsageLoadFailed = "Impossible de charger cette période · nouvel essai",
        NotchUsageAtResetFmt = "≈ {0} à la réinit.",
        NotchUsageExtraOn = "Usage supplémentaire activé",
        NotchUsageEmptySpend = "Pas encore de coût sur cette période",
        NotchUsageEmptySpendHint = "Se remplit au fil de ton usage de Claude Code",
        NotchUsageEmptyQuota = "Pas encore d'historique de quota",
        NotchUsageEmptyQuotaHint = "Muesca enregistre ton % de quota chaque minute",
        NotchUsageNow = "maintenant",
        NotchUsageReset = "réinit.",
        NotchPageFocus = "Focus",
        NotchFocusPhaseFocus = "Focus",
        NotchFocusPhaseShort = "Pause courte",
        NotchFocusPhaseLong = "Pause longue",
        NotchFocusStart = "Démarrer",
        NotchFocusPause = "Pause",
        NotchFocusResume = "Reprendre",
        NotchFocusStartBreak = "Lancer la pause",
        NotchFocusStartFocus = "Lancer le focus",
        NotchFocusSkipHint = "Passer à la phase suivante",
        NotchFocusResetPhase = "Recommencer cette phase",
        NotchFocusResetCycle = "Recommencer le cycle",
        NotchFocusCycleFmt = "{0} sur {1}",
        NotchFocusPaused = "en pause",
        NotchFocusDoneFocus = "Focus terminé !",
        NotchFocusDoneBreak = "Fin de la pause",
        NotchFocusTodayOne = "1 session de focus aujourd'hui",
        NotchFocusTodayFmt = "{0} sessions de focus aujourd'hui",
        NotchFocusMinutesFmt = "{0} min",
        NotchFocusSecondsFmt = "{0} s",
        NotchFocusHoursFmt = "{0} h",
        NotchFocusLinkTask = "Lier une tâche",
        NotchFocusNoTasks = "Aucune tâche en attente",
        NotchFocusStats = "Statistiques de la semaine",
        NotchFocusWeekFmt = "Cette semaine : {0} sessions · {1}",
        NotchFocusWeekOneFmt = "Cette semaine : 1 session · {0}",
        NotchFocusNextFmt = "Ensuite : {0}",
        NotchFocusSoundSetting = "Focus : son à la fin d'une phase",
        NotchFocusLength = "Durée du focus",
        NotchFocusShortLength = "Pause courte",
        NotchFocusLongLength = "Pause longue",
        NotchFocusLongEvery = "Pause longue toutes les (sessions)",
        NotchProLockedShelf = "Le bac à fichiers avec Muesca Pro. Ce que vous y avez déposé est conservé.",
        NotchProLockedHint = "Cliquez pour voir ce qu'inclut Pro",
        NotchProPriceFmt = "{0} · paiement unique",
        NotchProOneTime = "Paiement unique",
        NotchProTrialFmt = "Essai · encore {0} jours",
        NotchProTrialOne = "Essai · dernier jour",
        NotchProActive = "Activé. Merci !",
        NotchProFeatures = new[]
        {
            "Autoriser / refuser depuis le notch", "Musique et paroles synchronisées", "Tâches avec rappels", "Bac à fichiers",
            "Notch sur chaque écran", "Graphiques d'usage et pomodoro avancé",
        },
        NotchProBuy = "Acheter",
        NotchProRestore = "Restaurer l'achat",
        NotchProGetInStore = "Obtenir Pro sur Microsoft Store",
        NotchProBusy = "Ouverture de Microsoft Store…",
        NotchProFailed = "Achat non finalisé.",
        NotchProOffline = "Le Store ne répond pas.",
        NotchProUnavailable = "Pro pas encore sur le Store.",
        NotchProNotOwned = "Pas de Pro sur ce compte.",
        NotchProElsewhere = "Cliquez pour acheter depuis cet écran",
        NotchProTrialEndedTitle = "Votre essai de Pro est terminé",
        NotchProTrialEndedDetail = "Vos tâches et le bac sont conservés.",
        OnbProTrialNote = "Pro : gratuit 7 jours, puis paiement unique.",
        OnbProStoreNote = "Pro : dans l'édition Microsoft Store.",
        OnbProBuyNote = "Pro : paiement unique, depuis la carte Pro du notch.",
        OnbProActiveNote = "Vous avez Muesca Pro.",
        TapActiveFmt = "Activé · dernière donnée il y a {0}",
        TapWaiting = "Activé · en attente d'une session Claude Code",
        TapOff = "Désactivé · interroge l'API à chaque actualisation",
        TapUnavailable = "Indisponible dans cette installation",
        TapEnableConfirm =
            "Muesca lira votre quota depuis la ligne d'état de Claude Code, sans aucune requête à l'API.\n\n" +
            "Il ajoute une petite étape devant votre ligne d'état dans ~/.claude/settings.json : elle garde le même aspect. " +
            "Si vous n'en avez pas, Muesca en ajoute une compacte (Claude Code masque alors certaines indications du pied).\n\n" +
            "Une copie de sauvegarde de settings.json est faite avant. Continuer ?",
        TapDisableConfirm =
            "Ne plus lire le quota depuis Claude Code ?\n\n" +
            "Votre ligne d'état d'origine est restaurée à l'identique et Muesca recommence à interroger l'API. " +
            "Une copie de sauvegarde de settings.json est faite avant.",
        TapEnabledFmt = "Quota lu depuis Claude Code. Sauvegarde : {0}",
        TapDisabled = "Ligne d'état restaurée. Retour à l'API.",
        TapCompoundError = "Votre ligne d'état est une commande composée (&&, ;) : Muesca ne s'y branche pas pour ne pas la casser. Il continue avec l'API.",
        TapTypeError = "Votre ligne d'état n'est pas une commande ; Muesca ne peut pas la lire. Il continue avec l'API.",
        TapSuggest = "Muesca peut lire votre quota depuis Claude Code sans requête à l'API (fini les limites de débit). Cliquez pour l'activer.",
        MascotVerbsProcessing = new[] { "je réfléchis", "je mijote", "je bricole", "je cogite", "je manigance" },
        MascotVerbsWaitingApproval = new[] { "à toi de voir", "un petit oui ?", "psst, toi" },
        MascotVerbsWaitingInput = new[] { "à toi", "tout ouïe", "je t'attends", "vas-y" },
        MascotVerbsCompacting = new[] { "je range", "je plie la mémoire", "je compresse", "je fais de la place" },
        MascotVerbsIdle = new[] { "je fais la sieste", "je flâne", "je rêvasse", "je m'étire" },
        MascotVerbsEnded = new[] { "terminé", "emballé", "K.-O." },
        // Contador anónimo de uso (PRIVACY.md)
        UsageStatsLabel = "Compteur d'utilisation anonyme",
        UsageStatsSubtitle = "Compte combien de personnes utilisent Muesca, sans savoir qui. Une fois par jour, il n'envoie que la version, l'édition et la langue.",
        UsageStatsPrivacy = "Politique de confidentialité",
        UsageStatsNotice = "Nouveau : Muesca compte combien de personnes l'utilisent, sans savoir qui. Une fois par jour, il n'envoie que la version, l'édition et la langue. Tu peux le désactiver dans Réglages › Système."
    };

    private static readonly Strings German = new()
    {
        Culture = CultureInfo.GetCultureInfo("de-DE"),
        Tut = TutorialStrings.German,
        Dashboard = "Dashboard",
        Refresh = "Jetzt aktualisieren",
        PanelWindow = "Panel-Fenster",
        Position = "Position",
        PosBottomRight = "Unten rechts",
        PosBottomLeft = "Unten links",
        PosTopRight = "Oben rechts",
        PosTopLeft = "Oben links",
        PosCenter = "Mitte",
        PosCustom = "Benutzerdefiniert (Panel ziehen)",
        Sticky = "Angeheftet (nicht autom. schließen)",
        AlwaysOnTop = "Immer im Vordergrund",
        ReduceMotion = "Bewegung reduzieren",
        ReduceMotionSubtitle = "Schaltet Animationen aus",
        UpdateFrequency = "Aktualisierungsintervall",
        Sec30 = "30s",
        Min1 = "1m",
        Min5 = "5m",
        Min15 = "15m",
        Notifications = "Benachrichtigungen",
        Enabled = "Aktiviert",
        NotifyWhenReaching = "Hinweis-Schwelle",
        ColorThreshold = "Farbschwelle",
        DefaultTag = "(Std.)",
        Settings = "Einstellungen",
        ShowSpend = "Geschätzte Kosten anzeigen",
        ShowSpendSubtitle = "API-äquivalente Kosten pro Modell",
        StartWithWindows = "Mit Windows starten",
        StartupChecking = "Wird geprüft…",
        StartupChanging = "Wird geändert…",
        EditConfig = "Konfig bearbeiten (erweitert)…",
        OpenDataFolder = "Datenordner öffnen",
        Language = "Sprache",
        SystemDefault = "Systemstandard",
        Exit = "Beenden",
        SessionWord = "Sitzung",
        WeekWord = "Woche",
        ResetsIn = "Reset in",
        Resetting = "wird zurückgesetzt…",
        RollingHint = "gleitendes 5-Std-Fenster ab deiner 1. Anfrage",
        SpendHeaderFormat = "Geschätzte Kosten ({0}T, API-Äquiv.)",
        Loading = "Laden…",
        UpdatedAt = "Aktualisiert",
        AgoFormat = "vor {0}",
        StaleLabel = "veraltet",
        LocalSeal = "Deine Anmeldedaten und Daten verlassen nie dieses Gerät",
        HintClickToHide = "Symbol anklicken zum Ausblenden",
        HintPinnedClose = "angeheftet · ✕ zum Schließen",
        PreviousDataTip = "⚠ vorherige Daten (offline)",
        PreviousDataFooter = "vorherige Daten",
        QuotaRenewed = "Kontingent erneuert",
        StateNoCredentials = "Nicht angemeldet — bei Claude Code anmelden",
        StateAuthExpired = "Sitzung abgelaufen — Claude Code öffnen",
        StateRateLimited = "Anfragelimit — erneuter Versuch",
        StateNetworkError = "Keine Verbindung zu Anthropic",
        NotifQuotaFormat = "Claude {0}%+ Kontingent genutzt",
        Theme = "Design",
        ThemeSystem = "System",
        ThemeDark = "Dunkel",
        ThemeLight = "Hell",
        ThemeCli = "CLI",
        ThemeImported = "Importiert",
        ImportTheme = ".itermcolors importieren…",
        ShowServiceStatus = "Dienststatus anzeigen",
        HealthOk = "Betriebsbereit",
        HealthDegraded = "Beeinträchtigt",
        HealthOutage = "Störung",
        Back = "Zurück",
        UsageChart = "Nutzungsdiagramm",
        NoData = "Keine Daten in diesem Bereich",
        OpenBilling = "Abrechnung öffnen…",
        ChartPeak = "Max",
        ChartTotal = "Gesamt",
        ChartTabSpend = "Kosten $",
        ChartTabPct = "Kontingent %",
        ModelFamilyOther = "Sonstige",
        SpendNoRate = "kein Tarif",
        PricingOnlineUpdate = "Tarife online aktualisieren",
        PricingOnlineUpdateSubtitle = "Lädt öffentliche Preise von models.dev · sendet nichts von dir",
        Opacity = "Deckkraft",
        IconMode = "Symbolmodus",
        PaceAlerts = "Tempo-Warnungen",
        WinSession = "Sitzung (5h)",
        WinWeekly = "wöchentlich (7d)",
        PaceAlertTitle = "⚠ Kontingent-Tempo",
        PaceAlertBodyFmt = "In diesem Tempo ist dein {0}-Kontingent um {1} aufgebraucht, vor dem Reset",
        PaceEtaLabel = "leer",
        MenuAppearance = "Darstellung",
        MenuSections = "Panel-Inhalt",
        MenuIcon = "Symbol",
        MenuAdvanced = "Erweitert",
        MenuSystem = "System",
        VersionLabel = "Version",
        About = "Über",
        AboutProduct = "Muesca, die Notch für Claude Code · von Bekay",
        AboutDisclaimer = "Nicht mit Anthropic verbunden. Claude ist eine Marke von Anthropic.",
        CheckUpdates = "Nach Updates suchen",
        Checking = "Suche nach Updates…",
        UpdateAvailableFmt = "⬇ Update verfügbar {0}",
        UpToDateFmt = "Du bist auf dem neuesten Stand (v{0}).",
        UpdatePromptFmt = "Aktuelle Version: v{0}\nNeueste: {1}\n\nJetzt herunterladen?",
        UpdateDownloadedFmt = "Heruntergeladen nach:\n{0}\n\nSchließe Muesca und ersetze die laufende .exe durch diese Datei.",
        UpdateFailed = "Update konnte nicht geprüft/geladen werden.",
        MenuLiveSessions = "Live-Sitzungen",
        LiveSessionsSubtitle = "Installiert/entfernt Hooks in ~/.claude/settings.json",
        RemoveIntegration = "Integration mit Claude Code entfernen",
        RemoveIntegrationHint = "Entfernt die Hooks und die Statuszeile von Muesca aus der settings.json von Claude Code. Vor dem Deinstallieren ausführen.",
        RemoveIntegrationConfirm = "Damit werden nur die Einträge von Muesca aus der settings.json von Claude Code (~/.claude/settings.json) entfernt: die Hooks für Live-Sitzungen und Berechtigungen und die Anbindung der Statuszeile. Deine eigene Statuszeile ist danach genau wie vorher, und sonst wird nichts verändert. Vorher wird eine Sicherung der settings.json angelegt.\n\nMach das vor dem Deinstallieren von Muesca. Fortfahren?",
        RemoveIntegrationDoneFmt = "Integration mit Claude Code entfernt. Sicherung der settings.json: {0}",
        RemoveIntegrationNothing = "Die settings.json von Claude Code enthält keine Einträge von Muesca: Es gibt nichts zu entfernen.",
        MenuShowMascot = "Maskottchen anzeigen",
        MenuSuppressWhenFocused = "Stumm, wenn Terminal im Fokus",
        MenuInstallHooks = "Aktivieren (Hooks installieren)…",
        MenuUninstallHooks = "Deaktivieren (Hooks entfernen)",
        NoActiveSessions = "Keine aktiven Sitzungen",
        SessionPhaseIdle = "im Leerlauf",
        SessionPhaseProcessing = "arbeitet",
        SessionPhaseWaitingApproval = "wartet auf dein OK",
        SessionPhaseWaitingInput = "du bist dran",
        SessionPhaseCompacting = "komprimiert",
        NotifWaitingApprovalFmt = "Claude wartet auf dein OK in {0}",
        NotifWaitingInputFmt = "Claude ist fertig in {0}",
        LiveSessionsTitle = "Claude-Sitzungen",
        HooksInstalledFmt = "Live-Sitzungen an. Backup: {0}",
        HooksRemoved = "Live-Sitzungen aus. Hooks entfernt.",
        Changelog = "Neuerungen",
        BadgeActive = "Aktiv",
        BadgeInstall = "Installieren",
        QuotaSourceHeader = "Quelle des Kontingents",
        QuotaFromClaudeCode = "Kontingent aus Claude Code lesen",
        MenuProFmt = "Muesca Pro… ({0})",
        ProStateFree = "Kostenlos",
        ProStateTrialFmt = "Test: {0} Tage",
        ProStateTrialOne = "Test: 1 Tag",
        ProMonitorsLocked = "Mehrere Monitore: Muesca Pro…",
        ProThanks = "Muesca Pro ist aktiv. Danke!",
        NotchProLockedMusic = "Musik und synchronisierte Songtexte mit Muesca Pro.",
        NotchProLockedTasks = "Aufgaben mit Erinnerungen mit Muesca Pro. Deine Aufgaben bleiben erhalten.",
        NotchPageUsage = "Nutzung",
        NotchUsagePaceFmt = "Tempo {0}",
        NotchUsageRunsOutFmt = "leer um {0}",
        NotchUsageRunsOutDayFmt = "leer am {0}",
        NotchUsageLoadFailed = "Zeitraum konnte nicht geladen werden · neuer Versuch",
        NotchUsageAtResetFmt = "≈ {0} beim Reset",
        NotchUsageExtraOn = "Zusätzliche Nutzung an",
        NotchUsageEmptySpend = "Noch keine Kosten in diesem Zeitraum",
        NotchUsageEmptySpendHint = "Füllt sich, während du Claude Code nutzt",
        NotchUsageEmptyQuota = "Noch kein Kontingentverlauf",
        NotchUsageEmptyQuotaHint = "Muesca speichert dein Kontingent-% jede Minute",
        NotchUsageNow = "jetzt",
        NotchUsageReset = "Reset",
        NotchPageFocus = "Fokus",
        NotchFocusPhaseFocus = "Fokus",
        NotchFocusPhaseShort = "Kurze Pause",
        NotchFocusPhaseLong = "Lange Pause",
        NotchFocusStart = "Starten",
        NotchFocusPause = "Pause",
        NotchFocusResume = "Fortsetzen",
        NotchFocusStartBreak = "Pause starten",
        NotchFocusStartFocus = "Fokus starten",
        NotchFocusSkipHint = "Zur nächsten Phase springen",
        NotchFocusResetPhase = "Diese Phase neu starten",
        NotchFocusResetCycle = "Zyklus neu beginnen",
        NotchFocusCycleFmt = "{0} von {1}",
        NotchFocusPaused = "pausiert",
        NotchFocusDoneFocus = "Fokus geschafft!",
        NotchFocusDoneBreak = "Pause vorbei",
        NotchFocusTodayOne = "1 Fokusphase heute",
        NotchFocusTodayFmt = "{0} Fokusphasen heute",
        NotchFocusMinutesFmt = "{0} Min.",
        NotchFocusSecondsFmt = "{0} s",
        NotchFocusHoursFmt = "{0} Std.",
        NotchFocusLinkTask = "Aufgabe verknüpfen",
        NotchFocusNoTasks = "Keine offenen Aufgaben",
        NotchFocusStats = "Statistik dieser Woche",
        NotchFocusWeekFmt = "Diese Woche: {0} Phasen · {1}",
        NotchFocusWeekOneFmt = "Diese Woche: 1 Phase · {0}",
        NotchFocusNextFmt = "Als Nächstes: {0}",
        NotchFocusSoundSetting = "Fokus: Ton am Ende einer Phase",
        NotchFocusLength = "Fokusdauer",
        NotchFocusShortLength = "Kurze Pause",
        NotchFocusLongLength = "Lange Pause",
        NotchFocusLongEvery = "Lange Pause alle (Fokusphasen)",
        NotchProLockedShelf = "Die Dateiablage mit Muesca Pro. Was du abgelegt hast, bleibt erhalten.",
        NotchProLockedHint = "Klicken, um zu sehen, was Pro bietet",
        NotchProPriceFmt = "{0} · Einmalzahlung",
        NotchProOneTime = "Einmalzahlung",
        NotchProTrialFmt = "Test · noch {0} Tage",
        NotchProTrialOne = "Test · letzter Tag",
        NotchProActive = "Aktiv. Danke!",
        NotchProFeatures = new[]
        {
            "Erlauben / Ablehnen in der Notch", "Musik und synchronisierte Songtexte", "Aufgaben mit Erinnerungen",
            "Dateiablage", "Notch auf jedem Monitor", "Nutzungsgrafiken & erweiterter Pomodoro",
        },
        NotchProBuy = "Kaufen",
        NotchProRestore = "Kauf wiederherstellen",
        NotchProGetInStore = "Pro im Microsoft Store holen",
        NotchProBusy = "Microsoft Store wird geöffnet…",
        NotchProFailed = "Kauf nicht abgeschlossen.",
        NotchProOffline = "Der Store antwortet nicht.",
        NotchProUnavailable = "Pro ist noch nicht im Store.",
        NotchProNotOwned = "Kein Pro auf diesem Konto.",
        NotchProElsewhere = "Klicken, um auf diesem Bildschirm zu kaufen",
        NotchProTrialEndedTitle = "Dein Pro-Test ist abgelaufen",
        NotchProTrialEndedDetail = "Aufgaben und Ablage bleiben erhalten.",
        OnbProTrialNote = "Pro: 7 Tage kostenlos, danach eine Einmalzahlung.",
        OnbProStoreNote = "Pro: in der Microsoft-Store-Edition.",
        OnbProBuyNote = "Pro: Einmalzahlung, über die Pro-Karte in der Notch.",
        OnbProActiveNote = "Du hast Muesca Pro.",
        TapActiveFmt = "An · letzte Daten vor {0}",
        TapWaiting = "An · wartet auf eine Claude-Code-Sitzung",
        TapOff = "Aus · fragt bei jeder Aktualisierung die API",
        TapUnavailable = "In dieser Installation nicht verfügbar",
        TapEnableConfirm =
            "Muesca liest dein Kontingent aus der Statuszeile von Claude Code, ganz ohne Anfragen an die API.\n\n" +
            "Dazu wird in ~/.claude/settings.json ein kleiner Schritt vor deine Statuszeile gesetzt: Sie sieht weiter gleich aus. " +
            "Hast du keine, fügt Muesca eine kompakte hinzu (Claude Code blendet dann einige Hinweise in der Fußzeile aus).\n\n" +
            "Vorher wird eine Sicherung von settings.json angelegt. Fortfahren?",
        TapDisableConfirm =
            "Das Kontingent nicht mehr aus Claude Code lesen?\n\n" +
            "Deine ursprüngliche Statuszeile wird exakt wiederhergestellt und Muesca fragt wieder die API. " +
            "Vorher wird eine Sicherung von settings.json angelegt.",
        TapEnabledFmt = "Kontingent wird aus Claude Code gelesen. Sicherung: {0}",
        TapDisabled = "Statuszeile wiederhergestellt. Zurück zur API.",
        TapCompoundError = "Deine Statuszeile ist ein zusammengesetzter Befehl (&&, ;): Muesca hängt sich nicht ein, um sie nicht zu beschädigen. Es bleibt bei der API.",
        TapTypeError = "Deine Statuszeile ist kein Befehl; Muesca kann sie nicht lesen. Es bleibt bei der API.",
        TapSuggest = "Muesca kann dein Kontingent ohne API-Anfragen aus Claude Code lesen (keine Rate Limits mehr). Zum Einrichten klicken.",
        MascotVerbsProcessing = new[] { "am Grübeln", "am Brüten", "am Basteln", "am Tüfteln", "am Schmieden" },
        MascotVerbsWaitingApproval = new[] { "deine Wahl", "ein Nicken bitte", "pst, du da" },
        MascotVerbsWaitingInput = new[] { "du bist dran", "ganz Ohr", "warte auf dich", "leg los" },
        MascotVerbsCompacting = new[] { "räume auf", "falte Speicher", "komprimiere", "schaffe Platz" },
        MascotVerbsIdle = new[] { "halte ein Nickerchen", "faulenze", "träume vor mich hin", "strecke mich" },
        MascotVerbsEnded = new[] { "fertig", "abgehakt", "k. o." },
        // Contador anónimo de uso (PRIVACY.md)
        UsageStatsLabel = "Anonymer Nutzungszähler",
        UsageStatsSubtitle = "Zählt, wie viele Leute Muesca nutzen, ohne zu wissen, wer. Einmal am Tag sendet er nur Version, Edition und Sprache.",
        UsageStatsPrivacy = "Datenschutzerklärung",
        UsageStatsNotice = "Neu: Muesca zählt, wie viele Leute es nutzen, ohne zu wissen, wer. Einmal am Tag sendet es nur Version, Edition und Sprache. Abschalten unter Einstellungen › System."
    };

    private static readonly Strings Japanese = new()
    {
        Culture = CultureInfo.GetCultureInfo("ja-JP"),
        Tut = TutorialStrings.Japanese,
        Dashboard = "ダッシュボード",
        Refresh = "今すぐ更新",
        PanelWindow = "パネルウィンドウ",
        Position = "位置",
        PosBottomRight = "右下",
        PosBottomLeft = "左下",
        PosTopRight = "右上",
        PosTopLeft = "左上",
        PosCenter = "中央",
        PosCustom = "カスタム（ドラッグで移動）",
        Sticky = "固定（自動で閉じない）",
        AlwaysOnTop = "常に最前面",
        ReduceMotion = "アニメーションを減らす",
        ReduceMotionSubtitle = "アニメーションを無効化",
        UpdateFrequency = "更新頻度",
        Sec30 = "30秒",
        Min1 = "1分",
        Min5 = "5分",
        Min15 = "15分",
        Notifications = "通知",
        Enabled = "有効",
        NotifyWhenReaching = "通知のしきい値",
        ColorThreshold = "色のしきい値",
        DefaultTag = "(既定)",
        Settings = "設定",
        ShowSpend = "推定コストを表示",
        ShowSpendSubtitle = "モデルごとのAPI換算コスト",
        StartWithWindows = "Windows 起動時に開始",
        StartupChecking = "確認中…",
        StartupChanging = "変更中…",
        EditConfig = "設定を編集（詳細）…",
        OpenDataFolder = "データフォルダーを開く",
        Language = "言語",
        SystemDefault = "システム既定",
        Exit = "終了",
        SessionWord = "セッション",
        WeekWord = "週間",
        ResetsIn = "リセットまで",
        Resetting = "リセット中…",
        RollingHint = "最初のリクエストからの5時間移動ウィンドウ",
        SpendHeaderFormat = "推定コスト（{0}日, API換算）",
        Loading = "読み込み中…",
        UpdatedAt = "更新",
        AgoFormat = "{0}前",
        StaleLabel = "古い",
        LocalSeal = "認証情報とデータはこの端末から外に出ません",
        HintClickToHide = "アイコンをクリックで非表示",
        HintPinnedClose = "固定中 · ✕ で閉じる",
        PreviousDataTip = "⚠ 以前のデータ（オフライン）",
        PreviousDataFooter = "以前のデータ",
        QuotaRenewed = "クォータ更新",
        StateNoCredentials = "未ログイン — Claude Code にログイン",
        StateAuthExpired = "セッション期限切れ — Claude Code を開く",
        StateRateLimited = "レート制限 — 再試行中",
        StateNetworkError = "Anthropic に接続できません",
        NotifQuotaFormat = "Claude クォータを{0}%以上使用",
        Theme = "テーマ",
        ThemeSystem = "システム",
        ThemeDark = "ダーク",
        ThemeLight = "ライト",
        ThemeCli = "CLI",
        ThemeImported = "インポート済み",
        ImportTheme = ".itermcolors をインポート…",
        ShowServiceStatus = "サービス状態を表示",
        HealthOk = "正常",
        HealthDegraded = "一部障害",
        HealthOutage = "障害",
        Back = "戻る",
        UsageChart = "使用状況グラフ",
        NoData = "この範囲のデータなし",
        OpenBilling = "請求を開く…",
        ChartPeak = "最大",
        ChartTotal = "合計",
        ChartTabSpend = "コスト $",
        ChartTabPct = "クォータ %",
        ModelFamilyOther = "その他",
        SpendNoRate = "料金不明",
        PricingOnlineUpdate = "料金をオンラインで更新",
        PricingOnlineUpdateSubtitle = "models.dev から公開価格を取得 · あなたのデータは送信しません",
        Opacity = "不透明度",
        IconMode = "アイコン表示",
        PaceAlerts = "ペース通知",
        WinSession = "セッション(5h)",
        WinWeekly = "週間(7d)",
        PaceAlertTitle = "⚠ クォータのペース",
        PaceAlertBodyFmt = "このペースだと{0}のクォータは{1}頃に尽きます（リセット前）",
        PaceEtaLabel = "枯渇",
        MenuAppearance = "外観",
        MenuSections = "パネルの内容",
        MenuIcon = "アイコン",
        MenuAdvanced = "詳細",
        MenuSystem = "システム",
        VersionLabel = "バージョン",
        About = "情報",
        AboutProduct = "Muesca — Claude Code のためのノッチ · Bekay",
        AboutDisclaimer = "Anthropic とは提携していません。Claude は Anthropic の商標です。",
        CheckUpdates = "更新を確認",
        Checking = "更新を確認中…",
        UpdateAvailableFmt = "⬇ 更新あり {0}",
        UpToDateFmt = "最新です (v{0})。",
        UpdatePromptFmt = "現在のバージョン: v{0}\n最新: {1}\n\n今すぐダウンロードしますか？",
        UpdateDownloadedFmt = "ダウンロード先:\n{0}\n\nMuesca を閉じて、実行中の .exe をこのファイルに置き換えてください。",
        UpdateFailed = "更新の確認/ダウンロードに失敗しました。",
        MenuLiveSessions = "ライブセッション",
        LiveSessionsSubtitle = "~/.claude/settings.json にフックを追加/削除",
        RemoveIntegration = "Claude Code との連携を解除",
        RemoveIntegrationHint = "Claude Code の settings.json から Muesca のフックとステータスラインを削除します。アンインストールの前に実行してください。",
        RemoveIntegrationConfirm = "Claude Code の settings.json（~/.claude/settings.json）から、Muesca のエントリだけを削除します：ライブセッションと権限のフック、ステータスラインの連携です。お使いのステータスラインは元のとおりに戻り、それ以外は変更しません。先に settings.json のバックアップを作成します。\n\nMuesca をアンインストールする前に実行してください。続行しますか？",
        RemoveIntegrationDoneFmt = "Claude Code との連携を解除しました。settings.json のバックアップ: {0}",
        RemoveIntegrationNothing = "Claude Code の settings.json に Muesca のエントリはありません。削除するものはありません。",
        MenuShowMascot = "マスコットを表示",
        MenuSuppressWhenFocused = "ターミナルがフォーカス中はミュート",
        MenuInstallHooks = "有効化（フックをインストール）…",
        MenuUninstallHooks = "無効化（フックを削除）",
        NoActiveSessions = "アクティブなセッションなし",
        SessionPhaseIdle = "待機中",
        SessionPhaseProcessing = "処理中",
        SessionPhaseWaitingApproval = "承認待ち",
        SessionPhaseWaitingInput = "あなたの番",
        SessionPhaseCompacting = "圧縮中",
        NotifWaitingApprovalFmt = "Claude が {0} で承認を待っています",
        NotifWaitingInputFmt = "Claude が {0} で完了しました",
        LiveSessionsTitle = "Claude セッション",
        HooksInstalledFmt = "ライブセッション オン。バックアップ: {0}",
        HooksRemoved = "ライブセッション オフ。フックを削除しました。",
        Changelog = "変更点",
        BadgeActive = "有効",
        BadgeInstall = "インストール",
        QuotaSourceHeader = "クォータの取得元",
        QuotaFromClaudeCode = "Claude Code からクォータを読み取る",
        MenuProFmt = "Muesca Pro… ({0})",
        ProStateFree = "無料",
        ProStateTrialFmt = "体験版: 残り{0}日",
        ProStateTrialOne = "体験版: 残り1日",
        ProMonitorsLocked = "複数のモニター: Muesca Pro…",
        ProThanks = "Muesca Pro が有効になりました。ありがとうございます！",
        NotchProLockedMusic = "音楽と同期歌詞は Muesca Pro で使えます。",
        NotchProLockedTasks = "リマインダー付きタスクは Muesca Pro で使えます。タスクは保持されます。",
        NotchPageUsage = "使用量",
        NotchUsagePaceFmt = "ペース {0}",
        NotchUsageRunsOutFmt = "{0} に枯渇",
        NotchUsageRunsOutDayFmt = "{0} に枯渇",
        NotchUsageLoadFailed = "この期間を読み込めませんでした · 再試行します",
        NotchUsageAtResetFmt = "リセット時 ≈ {0}",
        NotchUsageExtraOn = "追加使用量: オン",
        NotchUsageEmptySpend = "この期間のコストはまだありません",
        NotchUsageEmptySpendHint = "Claude Code を使うと記録されます",
        NotchUsageEmptyQuota = "クォータ履歴はまだありません",
        NotchUsageEmptyQuotaHint = "Muesca はクォータ % を毎分保存します",
        NotchUsageNow = "現在",
        NotchUsageReset = "リセット",
        NotchPageFocus = "集中",
        NotchFocusPhaseFocus = "集中",
        NotchFocusPhaseShort = "短い休憩",
        NotchFocusPhaseLong = "長い休憩",
        NotchFocusStart = "開始",
        NotchFocusPause = "一時停止",
        NotchFocusResume = "再開",
        NotchFocusStartBreak = "休憩を開始",
        NotchFocusStartFocus = "集中を開始",
        NotchFocusSkipHint = "次のフェーズへスキップ",
        NotchFocusResetPhase = "このフェーズをやり直す",
        NotchFocusResetCycle = "サイクルを最初から",
        NotchFocusCycleFmt = "{0}/{1}",
        NotchFocusPaused = "一時停止中",
        NotchFocusDoneFocus = "集中完了！",
        NotchFocusDoneBreak = "休憩終了",
        NotchFocusTodayOne = "今日の集中 1 回",
        NotchFocusTodayFmt = "今日の集中 {0} 回",
        NotchFocusMinutesFmt = "{0} 分",
        NotchFocusSecondsFmt = "{0} 秒",
        NotchFocusHoursFmt = "{0} 時間",
        NotchFocusLinkTask = "タスクを関連付け",
        NotchFocusNoTasks = "未完了のタスクはありません",
        NotchFocusStats = "今週の統計",
        NotchFocusWeekFmt = "今週: 集中 {0} 回 · {1}",
        NotchFocusWeekOneFmt = "今週: 集中 1 回 · {0}",
        NotchFocusNextFmt = "次: {0}",
        NotchFocusSoundSetting = "集中: フェーズ終了時に音を鳴らす",
        NotchFocusLength = "集中の長さ",
        NotchFocusShortLength = "短い休憩",
        NotchFocusLongLength = "長い休憩",
        NotchFocusLongEvery = "長い休憩の間隔（集中回数）",
        NotchProLockedShelf = "ファイルシェルフは Muesca Pro で使えます。置いたものは保持されます。",
        NotchProLockedHint = "クリックして Pro の内容を見る",
        NotchProPriceFmt = "{0} · 買い切り",
        NotchProOneTime = "買い切り",
        NotchProTrialFmt = "体験版 · 残り{0}日",
        NotchProTrialOne = "体験版 · 最終日",
        NotchProActive = "有効です。ありがとうございます！",
        NotchProFeatures = new[]
        {
            "ノッチから許可 / 拒否", "音楽と同期歌詞", "リマインダー付きタスク", "ファイルシェルフ",
            "すべてのモニターにノッチ", "使用量グラフと高度なポモドーロ",
        },
        NotchProBuy = "購入",
        NotchProRestore = "購入を復元",
        NotchProGetInStore = "Microsoft Store で Pro を入手",
        NotchProBusy = "Microsoft Store を開いています…",
        NotchProFailed = "購入が完了しませんでした。",
        NotchProOffline = "Store が応答しません。",
        NotchProUnavailable = "Pro はまだ Store にありません。",
        NotchProNotOwned = "このアカウントに Pro はありません。",
        NotchProElsewhere = "クリックしてこの画面で購入",
        NotchProTrialEndedTitle = "Pro の体験期間が終了しました",
        NotchProTrialEndedDetail = "タスクとシェルフは保持されます。",
        OnbProTrialNote = "Pro: 7日間無料、その後は買い切り。",
        OnbProStoreNote = "Pro: Microsoft Store 版で利用できます。",
        OnbProBuyNote = "Pro: 買い切り。ノッチの Pro カードから購入できます。",
        OnbProActiveNote = "Muesca Pro をお持ちです。",
        TapActiveFmt = "オン · 最終データ {0} 前",
        TapWaiting = "オン · Claude Code のセッション待ち",
        TapOff = "オフ · 更新のたびに API に問い合わせ",
        TapUnavailable = "このインストールでは利用できません",
        TapEnableConfirm =
            "Muesca は Claude Code のステータスラインからクォータを読み取ります。API へのリクエストは不要です。\n\n" +
            "~/.claude/settings.json のステータスラインの前に小さな処理を追加します。表示はこれまでと同じです。" +
            "ステータスラインがない場合は Muesca がコンパクトなものを追加します（その場合 Claude Code はフッターのヒントの一部を隠します）。\n\n" +
            "先に settings.json のバックアップを作成します。続行しますか？",
        TapDisableConfirm =
            "Claude Code からのクォータ読み取りをやめますか？\n\n" +
            "元のステータスラインがそのまま復元され、Muesca は再び API に問い合わせます。" +
            "先に settings.json のバックアップを作成します。",
        TapEnabledFmt = "Claude Code からクォータを読み取り中。バックアップ: {0}",
        TapDisabled = "ステータスラインを復元しました。API に戻ります。",
        TapCompoundError = "ステータスラインが複合コマンド（&&, ;）のため、壊さないよう Muesca は接続しません。API を使い続けます。",
        TapTypeError = "ステータスラインがコマンドではないため、Muesca は読み取れません。API を使い続けます。",
        TapSuggest = "Muesca は API リクエストなしで Claude Code からクォータを読み取れます（レート制限とはお別れ）。クリックして設定。",
        MascotVerbsProcessing = new[] { "考え中", "調理中", "いじり中", "ひらめき中", "たくらみ中" },
        MascotVerbsWaitingApproval = new[] { "あなた次第", "うなずき待ち", "ねえ、きみ" },
        MascotVerbsWaitingInput = new[] { "あなたの番", "聞いてるよ", "待ってるよ", "どうぞ" },
        MascotVerbsCompacting = new[] { "片づけ中", "記憶をたたみ中", "圧縮中", "すき間づくり中" },
        MascotVerbsIdle = new[] { "お昼寝中", "のんびり中", "空想中", "伸び中" },
        MascotVerbsEnded = new[] { "完了", "ひと段落", "ノックアウト" },
        // Contador anónimo de uso (PRIVACY.md)
        UsageStatsLabel = "匿名の利用カウンター",
        UsageStatsSubtitle = "誰かは知らずに、Muesca の利用人数だけを数えます。1 日 1 回、バージョン・エディション・言語だけを送信します。",
        UsageStatsPrivacy = "プライバシーポリシー",
        UsageStatsNotice = "新機能：Muesca は誰かを知らずに利用人数を数えます。1 日 1 回、バージョン・エディション・言語だけを送信します。設定 › システムでオフにできます。"
    };

    private static readonly Strings Korean = new()
    {
        Culture = CultureInfo.GetCultureInfo("ko-KR"),
        Tut = TutorialStrings.Korean,
        Dashboard = "대시보드",
        Refresh = "지금 새로고침",
        PanelWindow = "패널 창",
        Position = "위치",
        PosBottomRight = "오른쪽 아래",
        PosBottomLeft = "왼쪽 아래",
        PosTopRight = "오른쪽 위",
        PosTopLeft = "왼쪽 위",
        PosCenter = "가운데",
        PosCustom = "사용자 지정 (드래그)",
        Sticky = "고정 (자동으로 닫지 않음)",
        AlwaysOnTop = "항상 위에",
        ReduceMotion = "동작 줄이기",
        ReduceMotionSubtitle = "애니메이션 끄기",
        UpdateFrequency = "새로고침 주기",
        Sec30 = "30초",
        Min1 = "1분",
        Min5 = "5분",
        Min15 = "15분",
        Notifications = "알림",
        Enabled = "사용",
        NotifyWhenReaching = "알림 기준값",
        ColorThreshold = "색상 임계값",
        DefaultTag = "(기본)",
        Settings = "설정",
        ShowSpend = "예상 비용 표시",
        ShowSpendSubtitle = "모델별 API 환산 비용",
        StartWithWindows = "Windows 시작 시 실행",
        StartupChecking = "확인 중…",
        StartupChanging = "변경 중…",
        EditConfig = "구성 편집 (고급)…",
        OpenDataFolder = "데이터 폴더 열기",
        Language = "언어",
        SystemDefault = "시스템 기본값",
        Exit = "종료",
        SessionWord = "세션",
        WeekWord = "주간",
        ResetsIn = "재설정까지",
        Resetting = "재설정 중…",
        RollingHint = "첫 요청부터 5시간 이동 윈도우",
        SpendHeaderFormat = "예상 비용 ({0}일, API 환산)",
        Loading = "로딩 중…",
        UpdatedAt = "업데이트됨",
        AgoFormat = "{0} 전",
        StaleLabel = "오래됨",
        LocalSeal = "자격 증명과 데이터는 이 기기를 벗어나지 않습니다",
        HintClickToHide = "아이콘을 클릭하여 숨기기",
        HintPinnedClose = "고정됨 · ✕ 닫기",
        PreviousDataTip = "⚠ 이전 데이터 (오프라인)",
        PreviousDataFooter = "이전 데이터",
        QuotaRenewed = "쿼터 갱신됨",
        StateNoCredentials = "로그인 안 됨 — Claude Code에 로그인",
        StateAuthExpired = "세션 만료 — Claude Code 열기",
        StateRateLimited = "요청 제한 — 재시도 중",
        StateNetworkError = "Anthropic에 연결할 수 없음",
        NotifQuotaFormat = "Claude 할당량 {0}%+ 사용",
        Theme = "테마",
        ThemeSystem = "시스템",
        ThemeDark = "어두움",
        ThemeLight = "밝음",
        ThemeCli = "CLI",
        ThemeImported = "가져옴",
        ImportTheme = ".itermcolors 가져오기…",
        ShowServiceStatus = "서비스 상태 표시",
        HealthOk = "정상",
        HealthDegraded = "일부 장애",
        HealthOutage = "장애",
        Back = "뒤로",
        UsageChart = "사용량 그래프",
        NoData = "이 범위에 데이터 없음",
        OpenBilling = "청구 열기…",
        ChartPeak = "최대",
        ChartTotal = "합계",
        ChartTabSpend = "비용 $",
        ChartTabPct = "할당량 %",
        ModelFamilyOther = "기타",
        SpendNoRate = "요금 정보 없음",
        PricingOnlineUpdate = "요금 온라인 업데이트",
        PricingOnlineUpdateSubtitle = "models.dev에서 공개 가격을 받아옵니다 · 사용자 데이터는 보내지 않습니다",
        Opacity = "불투명도",
        IconMode = "아이콘 모드",
        PaceAlerts = "페이스 알림",
        WinSession = "세션(5h)",
        WinWeekly = "주간(7d)",
        PaceAlertTitle = "⚠ 할당량 페이스",
        PaceAlertBodyFmt = "이 페이스면 {0} 할당량이 {1}경 소진됩니다 (리셋 전)",
        PaceEtaLabel = "소진",
        MenuAppearance = "모양",
        MenuSections = "패널 내용",
        MenuIcon = "아이콘",
        MenuAdvanced = "고급",
        MenuSystem = "시스템",
        VersionLabel = "버전",
        About = "정보",
        AboutProduct = "Muesca, Claude Code를 위한 노치 · Bekay",
        AboutDisclaimer = "Anthropic과 제휴 관계가 없습니다. Claude는 Anthropic의 상표입니다.",
        CheckUpdates = "업데이트 확인",
        Checking = "업데이트 확인 중…",
        UpdateAvailableFmt = "⬇ 업데이트 있음 {0}",
        UpToDateFmt = "최신 버전입니다 (v{0}).",
        UpdatePromptFmt = "현재 버전: v{0}\n최신: {1}\n\n지금 다운로드할까요?",
        UpdateDownloadedFmt = "다운로드 위치:\n{0}\n\nMuesca를 닫고 실행 중인 .exe를 이 파일로 교체하세요.",
        UpdateFailed = "업데이트 확인/다운로드에 실패했습니다.",
        MenuLiveSessions = "실시간 세션",
        LiveSessionsSubtitle = "~/.claude/settings.json에 훅 설치/제거",
        RemoveIntegration = "Claude Code 연동 제거",
        RemoveIntegrationHint = "Claude Code의 settings.json에서 Muesca의 훅과 상태 표시줄을 제거합니다. 제거하기 전에 실행하세요.",
        RemoveIntegrationConfirm = "Claude Code의 settings.json(~/.claude/settings.json)에서 Muesca 항목만 제거합니다: 실시간 세션과 권한 훅, 상태 표시줄 연결입니다. 사용자의 상태 표시줄은 원래대로 돌아오고 그 밖의 것은 건드리지 않습니다. 먼저 settings.json을 백업합니다.\n\nMuesca를 제거하기 전에 실행하세요. 계속할까요?",
        RemoveIntegrationDoneFmt = "Claude Code 연동을 제거했습니다. settings.json 백업: {0}",
        RemoveIntegrationNothing = "Claude Code의 settings.json에 Muesca 항목이 없습니다. 제거할 것이 없습니다.",
        MenuShowMascot = "마스코트 표시",
        MenuSuppressWhenFocused = "터미널이 포커스일 때 음소거",
        MenuInstallHooks = "활성화 (훅 설치)…",
        MenuUninstallHooks = "비활성화 (훅 제거)",
        NoActiveSessions = "활성 세션 없음",
        SessionPhaseIdle = "대기 중",
        SessionPhaseProcessing = "작업 중",
        SessionPhaseWaitingApproval = "승인 대기",
        SessionPhaseWaitingInput = "당신 차례",
        SessionPhaseCompacting = "압축 중",
        NotifWaitingApprovalFmt = "Claude가 {0}에서 승인을 기다립니다",
        NotifWaitingInputFmt = "Claude가 {0}에서 완료했습니다",
        LiveSessionsTitle = "Claude 세션",
        HooksInstalledFmt = "실시간 세션 켜짐. 백업: {0}",
        HooksRemoved = "실시간 세션 꺼짐. 훅 제거됨.",
        Changelog = "변경 사항",
        BadgeActive = "활성",
        BadgeInstall = "설치",
        QuotaSourceHeader = "사용량 출처",
        QuotaFromClaudeCode = "Claude Code에서 사용량 읽기",
        MenuProFmt = "Muesca Pro… ({0})",
        ProStateFree = "무료",
        ProStateTrialFmt = "체험판: {0}일 남음",
        ProStateTrialOne = "체험판: 1일 남음",
        ProMonitorsLocked = "여러 모니터: Muesca Pro…",
        ProThanks = "Muesca Pro가 활성화되었습니다. 감사합니다!",
        NotchProLockedMusic = "음악과 동기화 가사는 Muesca Pro에 포함됩니다.",
        NotchProLockedTasks = "알림이 있는 작업은 Muesca Pro에 포함됩니다. 작업은 보존됩니다.",
        NotchPageUsage = "사용량",
        NotchUsagePaceFmt = "페이스 {0}",
        NotchUsageRunsOutFmt = "{0}에 소진",
        NotchUsageRunsOutDayFmt = "{0}에 소진",
        NotchUsageLoadFailed = "이 기간을 불러오지 못했습니다 · 다시 시도합니다",
        NotchUsageAtResetFmt = "초기화 시 ≈ {0}",
        NotchUsageExtraOn = "추가 사용량 켜짐",
        NotchUsageEmptySpend = "이 기간에 아직 비용이 없습니다",
        NotchUsageEmptySpendHint = "Claude Code를 사용하면 채워집니다",
        NotchUsageEmptyQuota = "아직 할당량 기록이 없습니다",
        NotchUsageEmptyQuotaHint = "Muesca가 할당량 %를 매분 저장합니다",
        NotchUsageNow = "지금",
        NotchUsageReset = "초기화",
        NotchPageFocus = "집중",
        NotchFocusPhaseFocus = "집중",
        NotchFocusPhaseShort = "짧은 휴식",
        NotchFocusPhaseLong = "긴 휴식",
        NotchFocusStart = "시작",
        NotchFocusPause = "일시정지",
        NotchFocusResume = "재개",
        NotchFocusStartBreak = "휴식 시작",
        NotchFocusStartFocus = "집중 시작",
        NotchFocusSkipHint = "다음 단계로 건너뛰기",
        NotchFocusResetPhase = "이 단계 다시 시작",
        NotchFocusResetCycle = "사이클 처음부터",
        NotchFocusCycleFmt = "{0}/{1}",
        NotchFocusPaused = "일시정지됨",
        NotchFocusDoneFocus = "집중 완료!",
        NotchFocusDoneBreak = "휴식 끝",
        NotchFocusTodayOne = "오늘 집중 1회",
        NotchFocusTodayFmt = "오늘 집중 {0}회",
        NotchFocusMinutesFmt = "{0}분",
        NotchFocusSecondsFmt = "{0}초",
        NotchFocusHoursFmt = "{0}시간",
        NotchFocusLinkTask = "작업 연결",
        NotchFocusNoTasks = "남은 작업 없음",
        NotchFocusStats = "이번 주 통계",
        NotchFocusWeekFmt = "이번 주: 집중 {0}회 · {1}",
        NotchFocusWeekOneFmt = "이번 주: 집중 1회 · {0}",
        NotchFocusNextFmt = "다음: {0}",
        NotchFocusSoundSetting = "집중: 단계가 끝나면 소리",
        NotchFocusLength = "집중 시간",
        NotchFocusShortLength = "짧은 휴식",
        NotchFocusLongLength = "긴 휴식",
        NotchFocusLongEvery = "긴 휴식 간격(집중 횟수)",
        NotchProLockedShelf = "파일 선반은 Muesca Pro에 포함됩니다. 넣어 둔 항목은 보존됩니다.",
        NotchProLockedHint = "클릭해서 Pro에 포함된 기능 보기",
        NotchProPriceFmt = "{0} · 일회성 결제",
        NotchProOneTime = "일회성 결제",
        NotchProTrialFmt = "체험판 · {0}일 남음",
        NotchProTrialOne = "체험판 · 마지막 날",
        NotchProActive = "활성화됨. 감사합니다!",
        NotchProFeatures = new[]
        {
            "노치에서 허용 / 거부", "음악과 동기화 가사", "알림이 있는 작업", "파일 선반",
            "모든 모니터에 노치", "사용량 그래프와 고급 뽀모도로",
        },
        NotchProBuy = "구매",
        NotchProRestore = "구매 복원",
        NotchProGetInStore = "Microsoft Store에서 Pro 받기",
        NotchProBusy = "Microsoft Store를 여는 중…",
        NotchProFailed = "구매가 완료되지 않았습니다.",
        NotchProOffline = "Store가 응답하지 않습니다.",
        NotchProUnavailable = "Pro가 아직 Store에 없습니다.",
        NotchProNotOwned = "이 계정에 Pro가 없습니다.",
        NotchProElsewhere = "클릭해서 이 화면에서 구매",
        NotchProTrialEndedTitle = "Pro 체험 기간이 끝났습니다",
        NotchProTrialEndedDetail = "작업과 선반은 보존됩니다.",
        OnbProTrialNote = "Pro: 7일 무료, 이후 일회성 결제.",
        OnbProStoreNote = "Pro: Microsoft Store 버전에서 제공.",
        OnbProBuyNote = "Pro: 일회성 결제, 노치의 Pro 카드에서 구매.",
        OnbProActiveNote = "Muesca Pro를 보유하고 있습니다.",
        TapActiveFmt = "켜짐 · 마지막 데이터 {0} 전",
        TapWaiting = "켜짐 · Claude Code 세션 대기 중",
        TapOff = "꺼짐 · 새로 고칠 때마다 API에 요청",
        TapUnavailable = "이 설치에서는 사용할 수 없음",
        TapEnableConfirm =
            "Muesca가 API 요청 없이 Claude Code 상태 표시줄에서 사용량을 읽습니다.\n\n" +
            "~/.claude/settings.json의 상태 표시줄 앞에 작은 단계를 추가합니다. 상태 표시줄은 그대로 보입니다. " +
            "상태 표시줄이 없으면 Muesca가 간단한 것을 추가합니다(이 경우 Claude Code가 하단 힌트 일부를 숨깁니다).\n\n" +
            "먼저 settings.json을 백업합니다. 계속할까요?",
        TapDisableConfirm =
            "Claude Code에서 사용량 읽기를 중지할까요?\n\n" +
            "원래 상태 표시줄이 그대로 복원되고 Muesca는 다시 API에 요청합니다. " +
            "먼저 settings.json을 백업합니다.",
        TapEnabledFmt = "Claude Code에서 사용량을 읽는 중. 백업: {0}",
        TapDisabled = "상태 표시줄을 복원했습니다. API로 돌아갑니다.",
        TapCompoundError = "상태 표시줄이 복합 명령(&&, ;)이라 망가뜨리지 않도록 Muesca가 연결하지 않습니다. 계속 API를 사용합니다.",
        TapTypeError = "상태 표시줄이 명령이 아니어서 Muesca가 읽을 수 없습니다. 계속 API를 사용합니다.",
        TapSuggest = "Muesca는 API 요청 없이 Claude Code에서 사용량을 읽을 수 있습니다(속도 제한 걱정 끝). 클릭해서 설정하세요.",
        MascotVerbsProcessing = new[] { "생각 중", "요리 중", "만지작 중", "골똘히", "꿍꿍이 중" },
        MascotVerbsWaitingApproval = new[] { "당신 결정", "끄덕임 부탁", "저기요" },
        MascotVerbsWaitingInput = new[] { "당신 차례", "다 듣고 있어요", "기다리는 중", "어서요" },
        MascotVerbsCompacting = new[] { "정리 중", "기억 접는 중", "압축 중", "자리 만드는 중" },
        MascotVerbsIdle = new[] { "낮잠 중", "빈둥대는 중", "공상 중", "기지개 중" },
        MascotVerbsEnded = new[] { "완료", "마무리됨", "녹다운" },
        // Contador anónimo de uso (PRIVACY.md)
        UsageStatsLabel = "익명 사용자 수 집계",
        UsageStatsSubtitle = "누구인지 모른 채 Muesca 사용자 수만 셉니다. 하루 한 번 버전, 에디션, 언어만 보냅니다.",
        UsageStatsPrivacy = "개인정보 처리방침",
        UsageStatsNotice = "새 기능: Muesca가 누구인지 모른 채 사용자 수를 셉니다. 하루 한 번 버전, 에디션, 언어만 보냅니다. 설정 › 시스템에서 끌 수 있습니다."
    };

    private static readonly Strings TradChinese = new()
    {
        Culture = CultureInfo.GetCultureInfo("zh-TW"),
        Tut = TutorialStrings.TradChinese,
        Dashboard = "儀表板",
        Refresh = "立即重新整理",
        PanelWindow = "面板視窗",
        Position = "位置",
        PosBottomRight = "右下",
        PosBottomLeft = "左下",
        PosTopRight = "右上",
        PosTopLeft = "左上",
        PosCenter = "置中",
        PosCustom = "自訂（拖曳面板）",
        Sticky = "釘選（不自動關閉）",
        AlwaysOnTop = "永遠在最上層",
        ReduceMotion = "減少動態效果",
        ReduceMotionSubtitle = "關閉動畫",
        UpdateFrequency = "更新頻率",
        Sec30 = "30秒",
        Min1 = "1分鐘",
        Min5 = "5分鐘",
        Min15 = "15分鐘",
        Notifications = "通知",
        Enabled = "已啟用",
        NotifyWhenReaching = "提醒門檻",
        ColorThreshold = "顏色門檻",
        DefaultTag = "(預設)",
        Settings = "設定",
        ShowSpend = "顯示估計花費",
        ShowSpendSubtitle = "各模型的 API 約當成本",
        StartWithWindows = "隨 Windows 啟動",
        StartupChecking = "檢查中…",
        StartupChanging = "變更中…",
        EditConfig = "編輯設定（進階）…",
        OpenDataFolder = "開啟資料夾",
        Language = "語言",
        SystemDefault = "系統預設",
        Exit = "結束",
        SessionWord = "工作階段",
        WeekWord = "每週",
        ResetsIn = "重設於",
        Resetting = "重設中…",
        RollingHint = "從你第一次請求起算的 5 小時滾動視窗",
        SpendHeaderFormat = "估計花費（{0}天，API 約當）",
        Loading = "載入中…",
        UpdatedAt = "已更新",
        AgoFormat = "{0}前",
        StaleLabel = "過期",
        LocalSeal = "你的憑證與資料絕不離開本機",
        HintClickToHide = "點擊圖示以隱藏",
        HintPinnedClose = "已釘選 · ✕ 關閉",
        PreviousDataTip = "⚠ 先前資料（離線）",
        PreviousDataFooter = "先前資料",
        QuotaRenewed = "額度已更新",
        StateNoCredentials = "未登入 — 請登入 Claude Code",
        StateAuthExpired = "工作階段過期 — 開啟 Claude Code",
        StateRateLimited = "請求受限 — 重試中",
        StateNetworkError = "無法連線到 Anthropic",
        NotifQuotaFormat = "Claude 已使用 {0}%+ 配額",
        Theme = "主題",
        ThemeSystem = "系統",
        ThemeDark = "深色",
        ThemeLight = "淺色",
        ThemeCli = "CLI",
        ThemeImported = "已匯入",
        ImportTheme = "匯入 .itermcolors…",
        ShowServiceStatus = "顯示服務狀態",
        HealthOk = "正常運作",
        HealthDegraded = "部分異常",
        HealthOutage = "中斷",
        Back = "返回",
        UsageChart = "使用量圖表",
        NoData = "此範圍沒有資料",
        OpenBilling = "開啟帳單…",
        ChartPeak = "最高",
        ChartTotal = "總計",
        ChartTabSpend = "花費 $",
        ChartTabPct = "配額 %",
        ModelFamilyOther = "其他",
        SpendNoRate = "無費率",
        PricingOnlineUpdate = "線上更新費率",
        PricingOnlineUpdateSubtitle = "從 models.dev 下載公開價格 · 不會傳送你的任何資料",
        Opacity = "不透明度",
        IconMode = "圖示模式",
        PaceAlerts = "用量速度提醒",
        WinSession = "工作階段(5h)",
        WinWeekly = "每週(7d)",
        PaceAlertTitle = "⚠ 配額速度",
        PaceAlertBodyFmt = "照這個速度，你的{0}配額會在 {1} 用完（重設前）",
        PaceEtaLabel = "用完",
        MenuAppearance = "外觀",
        MenuSections = "面板內容",
        MenuIcon = "圖示",
        MenuAdvanced = "進階",
        MenuSystem = "系統",
        VersionLabel = "版本",
        About = "關於",
        AboutProduct = "Muesca，Claude Code 的瀏海 · Bekay",
        AboutDisclaimer = "與 Anthropic 無隸屬關係。Claude 是 Anthropic 的商標。",
        CheckUpdates = "檢查更新",
        Checking = "正在檢查更新…",
        UpdateAvailableFmt = "⬇ 有更新 {0}",
        UpToDateFmt = "已是最新版本 (v{0})。",
        UpdatePromptFmt = "目前版本：v{0}\n最新：{1}\n\n要立即下載嗎？",
        UpdateDownloadedFmt = "已下載到:\n{0}\n\n關閉 Muesca 並用此檔案取代執行中的 .exe。",
        UpdateFailed = "無法檢查／下載更新。",
        MenuLiveSessions = "即時工作階段",
        LiveSessionsSubtitle = "在 ~/.claude/settings.json 安裝／移除 hooks",
        RemoveIntegration = "移除與 Claude Code 的整合",
        RemoveIntegrationHint = "從 Claude Code 的 settings.json 移除 Muesca 的 hooks 與狀態列。請在解除安裝前執行。",
        RemoveIntegrationConfirm = "只會從 Claude Code 的 settings.json（~/.claude/settings.json）移除 Muesca 的項目：即時工作階段與權限的 hooks，以及狀態列的連結。你自己的狀態列會完全恢復原狀，其他內容都不會更動。會先備份 settings.json。\n\n請在解除安裝 Muesca 之前執行。要繼續嗎？",
        RemoveIntegrationDoneFmt = "已移除與 Claude Code 的整合。settings.json 的備份：{0}",
        RemoveIntegrationNothing = "Claude Code 的 settings.json 中沒有 Muesca 的項目，沒有需要移除的內容。",
        MenuShowMascot = "顯示吉祥物",
        MenuSuppressWhenFocused = "終端機聚焦時靜音",
        MenuInstallHooks = "啟用（安裝 hooks）…",
        MenuUninstallHooks = "停用（移除 hooks）",
        NoActiveSessions = "沒有作用中的工作階段",
        SessionPhaseIdle = "閒置",
        SessionPhaseProcessing = "處理中",
        SessionPhaseWaitingApproval = "等待你的同意",
        SessionPhaseWaitingInput = "輪到你了",
        SessionPhaseCompacting = "壓縮中",
        NotifWaitingApprovalFmt = "Claude 正在 {0} 等待你的同意",
        NotifWaitingInputFmt = "Claude 已在 {0} 完成",
        LiveSessionsTitle = "Claude 工作階段",
        HooksInstalledFmt = "即時工作階段已開啟。備份：{0}",
        HooksRemoved = "即時工作階段已關閉。已移除 hooks。",
        Changelog = "更新內容",
        BadgeActive = "啟用中",
        BadgeInstall = "安裝",
        QuotaSourceHeader = "用量來源",
        QuotaFromClaudeCode = "從 Claude Code 讀取用量",
        MenuProFmt = "Muesca Pro… ({0})",
        ProStateFree = "免費",
        ProStateTrialFmt = "試用：剩 {0} 天",
        ProStateTrialOne = "試用：剩 1 天",
        ProMonitorsLocked = "多部螢幕：Muesca Pro…",
        ProThanks = "Muesca Pro 已啟用，謝謝！",
        NotchProLockedMusic = "音樂與同步歌詞包含在 Muesca Pro 中。",
        NotchProLockedTasks = "附提醒的工作包含在 Muesca Pro 中。你的工作會保留。",
        NotchPageUsage = "用量",
        NotchUsagePaceFmt = "速度 {0}",
        NotchUsageRunsOutFmt = "{0} 用完",
        NotchUsageRunsOutDayFmt = "{0} 用完",
        NotchUsageLoadFailed = "無法載入此範圍 · 將重試",
        NotchUsageAtResetFmt = "重設時 ≈ {0}",
        NotchUsageExtraOn = "額外用量已開啟",
        NotchUsageEmptySpend = "此範圍尚無花費",
        NotchUsageEmptySpendHint = "使用 Claude Code 後就會出現",
        NotchUsageEmptyQuota = "尚無配額紀錄",
        NotchUsageEmptyQuotaHint = "Muesca 每分鐘儲存你的配額 %",
        NotchUsageNow = "現在",
        NotchUsageReset = "重設",
        NotchPageFocus = "專注",
        NotchFocusPhaseFocus = "專注",
        NotchFocusPhaseShort = "短休息",
        NotchFocusPhaseLong = "長休息",
        NotchFocusStart = "開始",
        NotchFocusPause = "暫停",
        NotchFocusResume = "繼續",
        NotchFocusStartBreak = "開始休息",
        NotchFocusStartFocus = "開始專注",
        NotchFocusSkipHint = "跳到下一階段",
        NotchFocusResetPhase = "重新開始此階段",
        NotchFocusResetCycle = "重新開始循環",
        NotchFocusCycleFmt = "{0}/{1}",
        NotchFocusPaused = "已暫停",
        NotchFocusDoneFocus = "專注完成！",
        NotchFocusDoneBreak = "休息結束",
        NotchFocusTodayOne = "今天專注 1 次",
        NotchFocusTodayFmt = "今天專注 {0} 次",
        NotchFocusMinutesFmt = "{0} 分鐘",
        NotchFocusSecondsFmt = "{0} 秒",
        NotchFocusHoursFmt = "{0} 小時",
        NotchFocusLinkTask = "連結工作",
        NotchFocusNoTasks = "沒有待辦工作",
        NotchFocusStats = "本週統計",
        NotchFocusWeekFmt = "本週：專注 {0} 次 · {1}",
        NotchFocusWeekOneFmt = "本週：專注 1 次 · {0}",
        NotchFocusNextFmt = "接下來：{0}",
        NotchFocusSoundSetting = "專注：階段結束時播放音效",
        NotchFocusLength = "專注時長",
        NotchFocusShortLength = "短休息",
        NotchFocusLongLength = "長休息",
        NotchFocusLongEvery = "長休息間隔（專注次數）",
        NotchProLockedShelf = "檔案架包含在 Muesca Pro 中。放入的項目會保留。",
        NotchProLockedHint = "按一下查看 Pro 包含的功能",
        NotchProPriceFmt = "{0} · 一次性付款",
        NotchProOneTime = "一次性付款",
        NotchProTrialFmt = "試用 · 剩 {0} 天",
        NotchProTrialOne = "試用 · 最後一天",
        NotchProActive = "已啟用，謝謝！",
        NotchProFeatures = new[]
        {
            "在瀏海中允許 / 拒絕", "音樂與同步歌詞", "附提醒的工作", "檔案架",
            "每部螢幕都有瀏海", "用量圖表與進階番茄鐘",
        },
        NotchProBuy = "購買",
        NotchProRestore = "還原購買",
        NotchProGetInStore = "在 Microsoft Store 取得 Pro",
        NotchProBusy = "正在開啟 Microsoft Store…",
        NotchProFailed = "購買未完成。",
        NotchProOffline = "Store 沒有回應。",
        NotchProUnavailable = "Pro 尚未在 Store 上架。",
        NotchProNotOwned = "此帳戶沒有 Pro。",
        NotchProElsewhere = "按一下從這個螢幕購買",
        NotchProTrialEndedTitle = "你的 Pro 試用期已結束",
        NotchProTrialEndedDetail = "你的工作與檔案架會保留。",
        OnbProTrialNote = "Pro：免費 7 天，之後一次性付款。",
        OnbProStoreNote = "Pro：Microsoft Store 版提供。",
        OnbProBuyNote = "Pro：一次性付款，可從瀏海的 Pro 卡片購買。",
        OnbProActiveNote = "你已擁有 Muesca Pro。",
        TapActiveFmt = "開啟 · 最後資料於 {0} 前",
        TapWaiting = "開啟 · 等待 Claude Code 工作階段",
        TapOff = "關閉 · 每次重新整理都詢問 API",
        TapUnavailable = "此安裝中無法使用",
        TapEnableConfirm =
            "Muesca 會從 Claude Code 的狀態列讀取用量，完全不需向 API 發出請求。\n\n" +
            "它會在 ~/.claude/settings.json 中你的狀態列前加入一個小步驟：狀態列看起來不變。" +
            "若你沒有狀態列，Muesca 會加入一個精簡的（此時 Claude Code 會隱藏頁尾的部分提示）。\n\n" +
            "會先備份 settings.json。要繼續嗎？",
        TapDisableConfirm =
            "要停止從 Claude Code 讀取用量嗎？\n\n" +
            "會原樣還原你原本的狀態列，Muesca 將改回詢問 API。" +
            "會先備份 settings.json。",
        TapEnabledFmt = "正在從 Claude Code 讀取用量。備份：{0}",
        TapDisabled = "已還原狀態列。改回使用 API。",
        TapCompoundError = "你的狀態列是複合命令（&&、;）：為避免弄壞它，Muesca 不會掛接。將繼續使用 API。",
        TapTypeError = "你的狀態列不是命令，Muesca 無法讀取。將繼續使用 API。",
        TapSuggest = "Muesca 可以不經 API 請求、直接從 Claude Code 讀取用量（告別速率限制）。按一下即可設定。",
        MascotVerbsProcessing = new[] { "思考中", "烹煮中", "擺弄中", "靈光乍現", "謀劃中" },
        MascotVerbsWaitingApproval = new[] { "你說了算", "點個頭吧", "欸，是你" },
        MascotVerbsWaitingInput = new[] { "輪到你了", "洗耳恭聽", "等你呢", "請吧" },
        MascotVerbsCompacting = new[] { "整理中", "摺疊記憶", "壓縮中", "騰出空間" },
        MascotVerbsIdle = new[] { "打盹中", "閒晃中", "做白日夢", "伸懶腰" },
        MascotVerbsEnded = new[] { "完成", "收工", "倒下了" },
        // Contador anónimo de uso (PRIVACY.md)
        UsageStatsLabel = "匿名使用人數統計",
        UsageStatsSubtitle = "只統計有多少人使用 Muesca，不知道是誰。每天一次，只傳送版本、版本類型與語言。",
        UsageStatsPrivacy = "隱私權政策",
        UsageStatsNotice = "新功能：Muesca 會統計使用人數，但不知道是誰。每天一次，只傳送版本、版本類型與語言。可在「設定 › 系統」關閉。"
    };
}
