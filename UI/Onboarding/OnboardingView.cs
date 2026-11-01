using System.Globalization;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Onboarding;

namespace ClaudeBarWin.UI.Onboarding;

/// <summary>Una tarjeta de perfil: título, la línea de debajo y si es la recomendada.</summary>
public sealed record OnboardingCard(SetupProfile Profile, string Title, string Line, bool Recommended);

/// <summary>Una fila con interruptor: nombre, línea gris, si está encendido, si se puede tocar y si es de ClaudeBar Pro (lleva
/// la etiqueta «Pro» junto al nombre).</summary>
public sealed record OnboardingToggle(string Label, string Detail, bool On, bool Enabled, bool Pro = false);

/// <summary>
/// Una fila de «Ajustes recomendados»: su etiqueta, el valor que se ve (selectores que ciclan), los segmentos (controles
/// segmentados) con el elegido y el recomendado, la explicación de debajo, si se puede tocar y, en el interruptor, si está
/// encendido.
/// </summary>
public sealed record OnboardingSetting(OnboardingControl Row, string Label, string Value, IReadOnlyList<string> Segments,
    int Selected, int Recommended, string Hint, bool Enabled, bool On = false);

/// <summary>Una fila del resumen: etiqueta, valor y cuántas líneas ocupa (lo activado puede ocupar varias).</summary>
public sealed record OnboardingSummaryRow(string Label, string Value, int Lines = 1);

/// <summary>El aviso que tapa la página: título, texto y sus dos botones.</summary>
public sealed record OnboardingOverlayView(string Title, string Text, string Secondary, string Primary);

/// <summary>
/// Todo lo que pinta el asistente en su estado actual, con los textos YA formateados: la ventana lo rehace cuando cambia algo
/// (un clic, una tecla, otro idioma) y el painter lo pinta tal cual en cada frame, sin formatear nada.
/// </summary>
public sealed record OnboardingView(
    OnboardingPage Page, IReadOnlyList<OnboardingPage> Pages, int PageIndex,
    string Title, string Subtitle, string Skip, string Back, string Next, bool CanGoBack,
    string Wordmark, string Tagline, string Detected, bool DetectedOk, string KeysHint,
    IReadOnlyList<OnboardingCard> Cards, int SelectedCard, string RecommendedBadge, string PreviewCaption,
    IReadOnlyList<OnboardingToggle> Toggles, string FeaturesHint, string Reset, string? ProNote,
    IReadOnlyList<OnboardingSetting> Settings, string? NotchOffNote,
    string ClaudeInfo, OnboardingToggle Live, string LiveHint, OnboardingToggle Permissions, string PermissionsHint,
    IReadOnlyList<OnboardingSummaryRow> Summary, string SummaryNote,
    OnboardingOverlayView? Overlay,
    OnboardingToggle? UsageStats = null,
    OnboardingToggle? Tutorial = null);

/// <summary>
/// Construye <see cref="OnboardingView"/> desde el estado del asistente y los textos del idioma. PURO: los nombres de los
/// monitores (<paramref name="monitorLabels"/>, por nombre GDI, como los de la bandeja) y el idioma de Windows llegan por
/// parámetro.
/// </summary>
public static class OnboardingText
{
    /// <summary>Lo que ocupa a lo sumo la fila «Activado» del resumen (el resto, una línea).</summary>
    public const int FeaturesSummaryLines = 3;

    public static OnboardingView Build(OnboardingWizard w, Strings s, IReadOnlyDictionary<string, string>? monitorLabels = null,
        string? systemLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(s);
        monitorLabels ??= new Dictionary<string, string>();
        systemLanguage ??= Localization.ResolveSystemCode();
        var ctx = w.Context;

        var (title, subtitle) = w.Page switch
        {
            OnboardingPage.Profile => (s.OnbProfileTitle, s.OnbProfileSubtitle),
            OnboardingPage.Features => (s.OnbFeaturesTitle, Fmt(s, s.OnbFeaturesSubtitleFmt, ProfileName(s, w.FeaturesProfile))),
            OnboardingPage.Settings => (s.OnbSettingsTitle, ctx.Fresh ? s.OnbSettingsSubtitle : s.OnbSettingsSubtitleKeep),
            OnboardingPage.ClaudeCode => (s.OnbClaudeTitle, s.OnbClaudeSubtitle),
            OnboardingPage.Summary => (s.OnbSummaryTitle, s.OnbSummarySubtitle),
            _ => ("", ""),
        };

        var cards = SetupProfiles.All.Select(p => new OnboardingCard(p, ProfileName(s, p), Pick(s.OnbProfileLines, (int)p),
            p == w.Recommended)).ToList();
        string caption = ProfilePreview.Scenes(ProfilePreview.FeaturesFor(w, w.Profile))[0].Face == PreviewFace.None
            ? s.OnbPreviewNoNotch : s.OnbPreviewCaption;
        if (!ctx.Machine.ClaudeCodePresent) caption = s.OnbClaudeMissing;

        var toggles = SetupFeatures.All.Select(f => new OnboardingToggle(Pick(s.OnbFeatureNames, (int)f),
            Pick(s.OnbFeatureDetails, (int)f), w.FeatureOn(f), w.FeatureEnabled(f), IsPro(f))).ToList();

        var live = ctx.HooksInstalled
            ? new OnboardingToggle(s.OnbLiveSessionsActive, "", true, false)
            : new OnboardingToggle(Recommend(s, s.OnbLiveSessions, w.Profile == SetupProfile.Coder), "", w.LiveSessions, true);
        var permissions = new OnboardingToggle(w.PermissionsAvailable ? s.OnbPermissions : $"{s.OnbPermissions} {s.OnbPermissionsNeed}",
            "", w.PermissionsValue, w.PermissionsAvailable, Pro: !ProGate.IsUnlocked(ProFeature.Permissions, ProState.Free));

        return new OnboardingView(w.Page, w.Pages, w.PageIndex, title, subtitle, s.OnbSkip, s.OnbBack,
            w.IsLastPage ? s.OnbStart : s.OnbNext, w.CanGoBack,
            Brand.Name, s.OnbTagline, ctx.Machine.ClaudeCodePresent ? s.OnbClaudeDetected : s.OnbClaudeNotFound,
            ctx.Machine.ClaudeCodePresent, s.OnbKeysHint,
            cards, (int)w.Profile, s.OnbRecommended, caption,
            toggles, s.OnbFeaturesHint, s.OnbReset, ProNote(ctx.Pro, s),
            Settings(w, s, monitorLabels, systemLanguage), w.NotchSettingsEnabled ? null : s.OnbNotchOff,
            Fmt(s, s.OnbClaudeFoundFmt, string.IsNullOrEmpty(ctx.ClaudeDir) ? "~/.claude" : ctx.ClaudeDir),
            live, ctx.HooksInstalled ? s.OnbLiveSessionsActiveHint : s.OnbLiveSessionsHint, permissions, s.OnbPermissionsHint,
            Summary(w, s, monitorLabels, systemLanguage), s.OnbBackupNote,
            Overlay(w, s),
            // La bienvenida: la línea del contador anónimo de uso, con su interruptor.
            new OnboardingToggle(s.UsageStatsSubtitle, "", w.UsageStats, true),
            // El resumen: el tutorial interactivo al terminar (no obligatorio).
            new OnboardingToggle(s.Tut.Onboarding, "", w.ShowTutorial, true));
    }

    public static string ProfileName(Strings s, SetupProfile p) => Pick(s.OnbProfileNames, (int)p);

    /// <summary>La función de pago de cada casilla (música, letras y letra pequeña son la música; las tareas, las tareas); null
    /// si la casilla es gratis.</summary>
    public static ProFeature? ProFeatureOf(SetupFeature f) => f switch
    {
        SetupFeature.Music or SetupFeature.Lyrics or SetupFeature.CompactLyrics => ProFeature.Music,
        SetupFeature.Tasks => ProFeature.Tasks,
        _ => null,
    };

    /// <summary>¿Va la casilla con la etiqueta «Pro»? (lo que la edición gratis no tiene, según <see cref="ProGate"/>).</summary>
    public static bool IsPro(SetupFeature f) => ProFeatureOf(f) is { } pf && !ProGate.IsUnlocked(pf, ProState.Free);

    /// <summary>
    /// La nota del Pro en «Qué se activa»: con el Pro, que se tiene; en la Store, la prueba de 7 días mientras dura (el aviso
    /// de la prueba, SOLO ahí) o el pago único después; en la edición GitHub, que el Pro está en la Store. Sin estado, nada.
    /// </summary>
    public static string? ProNote(ProSummary? pro, Strings s) => pro switch
    {
        null => null,
        { State: ProState.Pro } => s.OnbProActiveNote,
        { Store: true, State: ProState.Trial } => s.OnbProTrialNote,
        { Store: true } => s.OnbProBuyNote,
        _ => s.OnbProStoreNote,
    };

    /// <summary>«Como Windows (Español)» o el nombre nativo del idioma.</summary>
    public static string LanguageName(Strings s, string code, string systemLanguage)
    {
        if (string.IsNullOrEmpty(code) || code == "system")
            return Fmt(s, s.OnbLanguageSystemFmt, Native(systemLanguage));
        return Native(code);
    }

    private static string Native(string code)
    {
        foreach (var (c, native) in Localization.Languages) if (c == code) return native;
        return code;
    }

    /// <summary>Nombre del monitor elegido: «El principal» o su nombre de la bandeja («Monitor 2 · 2560×1440 · arriba»).</summary>
    public static string MonitorName(OnboardingWizard w, Strings s, IReadOnlyDictionary<string, string> labels)
    {
        var device = w.MonitorDevice;
        if (string.IsNullOrEmpty(device)) return s.OnbMonitorPrimary;
        return labels.TryGetValue(device, out var label) && !string.IsNullOrEmpty(label) ? label
            : Fmt(s, s.NotchMonitorFallbackFmt, w.MonitorIndex);
    }

    private static IReadOnlyList<OnboardingSetting> Settings(OnboardingWizard w, Strings s, IReadOnlyDictionary<string, string> labels,
        string systemLanguage)
    {
        bool notch = w.NotchSettingsEnabled;
        var rows = new List<OnboardingSetting>(5)
        {
            new(OnboardingControl.Language, s.Language,
                Recommend(s, LanguageName(s, w.Language, systemLanguage), w.Language == "system"),
                Array.Empty<string>(), 0, 0, s.OnbLanguageHint, true),
        };
        if (w.AskMonitor)
            rows.Add(new(OnboardingControl.Monitor, s.NotchMonitorMenu, Recommend(s, MonitorName(w, s, labels), w.MonitorIndex == 0),
                Array.Empty<string>(), 0, 0, Fmt(s, s.OnbMonitorHintFmt, w.Context.Machine.Monitors.Count), notch));

        var sizes = OnboardingWizard.SizeOptions;
        var sizeLabels = sizes.Select(v => v == 0 ? s.NotchSizeAuto : Fmt(s, "{0} %", v)).ToList();
        rows.Add(new(OnboardingControl.Size, s.OnbSize, "", sizeLabels, IndexOf(sizes, w.SizePercent), 0,
            Fmt(s, s.OnbSizeHintFmt, w.LogicalWidth, w.AutoPercent), notch));

        string openHint = w.OpenReason switch
        {
            OpenReason.MonitorAbove => s.OnbOpenHintAbove,
            OpenReason.SmallScreen => Fmt(s, s.OnbOpenHintSmallFmt, w.LogicalWidth),
            _ => s.OnbOpenHintHover,
        };
        rows.Add(new(OnboardingControl.Open, s.OnbOpen, "", new[] { s.OnbOpenHover, s.OnbOpenClick }, w.OpenOnClick ? 1 : 0,
            w.RecommendOpenOnClick ? 1 : 0, openHint, notch));
        rows.Add(new(OnboardingControl.Startup, s.StartWithWindows, "", Array.Empty<string>(), 0, 0,
            w.StartupUnknown ? s.OnbStartupUnknownHint : s.OnbStartupHint, true, w.StartWithWindows));
        return rows;
    }

    private static IReadOnlyList<OnboardingSummaryRow> Summary(OnboardingWizard w, Strings s, IReadOnlyDictionary<string, string> labels,
        string systemLanguage)
    {
        var rows = new List<OnboardingSummaryRow>(7) { new(s.OnbSumProfile, ProfileName(s, w.Profile)) };
        bool notch = w.FeatureOn(SetupFeature.Notch);
        if (notch)
        {
            string open = LowerFirst(w.OpenOnClick ? s.OnbOpenClick : s.OnbOpenHover);
            string size = w.SizePercent == 0 ? s.NotchSizeAuto : Fmt(s, "{0} %", w.SizePercent);
            rows.Add(new(s.OnbSumNotch, Fmt(s, s.OnbNotchOnFmt, open, size, LowerFirst(MonitorName(w, s, labels)))));
        }
        else rows.Add(new(s.OnbSumNotch, s.OnbNo));

        // Lo activado: lo que está encendido y se puede usar (sin notch, solo reducir movimiento), en minúsculas.
        var on = SetupFeatures.All.Where(f => f != SetupFeature.Notch && w.FeatureOn(f) && w.FeatureEnabled(f)
                && (notch || f == SetupFeature.ReduceMotion))
            .Select(f => Pick(s.OnbFeatureNames, (int)f).ToLower(s.Culture)).ToList();
        rows.Add(new(s.OnbSumFeatures, on.Count == 0 ? s.OnbNothingElse : string.Join(", ", on),
            on.Count <= 3 ? 1 : on.Count <= 6 ? 2 : FeaturesSummaryLines));
        rows.Add(new(s.Language, LanguageName(s, w.Language, systemLanguage)));
        rows.Add(new(s.StartWithWindows, w.StartupUnknown ? s.OnbUnchanged : w.StartWithWindows ? s.OnbYes : s.OnbNo));
        if (w.OfferLiveSessions)
        {
            string live = w.Context.HooksInstalled ? s.OnbLiveActive : w.LiveSessions ? s.OnbLiveInstall : s.OnbNo;
            rows.Add(new(s.OnbSumLive, live));
            rows.Add(new(s.OnbSumPermissions, w.PermissionsValue ? s.OnbYes : s.OnbNo));
        }
        return rows;
    }

    private static OnboardingOverlayView? Overlay(OnboardingWizard w, Strings s) => w.Overlay switch
    {
        OnboardingOverlay.ConfirmSkip when w.SkipApplies => new(s.OnbSkipTitle, Fmt(s, s.OnbSkipTextFmt, ProfileName(s, w.Recommended)),
            s.OnbKeepGoing, s.OnbSkipConfirm),
        OnboardingOverlay.ConfirmSkip => new(s.OnbCloseTitle, s.OnbCloseText, s.OnbKeepGoing, s.OnbClose),
        OnboardingOverlay.Error => new(s.OnbErrorTitle, Fmt(s, s.OnbErrorTextFmt, w.Error), s.OnbClose, s.OnbRetry),
        _ => null,
    };

    /// <summary>Pone « (recomendado)» al final, como el instalador.</summary>
    private static string Recommend(Strings s, string text, bool recommended) =>
        recommended ? $"{text} ({s.OnbRecommendedTag})" : text;

    private static string LowerFirst(string v) =>
        string.IsNullOrEmpty(v) ? v : char.ToLower(v[0], CultureInfo.InvariantCulture) + v[1..];

    private static string Pick(string[] values, int i) => values is { Length: > 0 } && i >= 0 && i < values.Length ? values[i] : "";

    private static string Fmt(Strings s, string format, params object[] args) => string.Format(s.Culture, format, args);

    private static int IndexOf(IReadOnlyList<int> list, int v)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == v) return i;
        return 0;
    }
}
