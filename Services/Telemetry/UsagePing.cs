using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Licensing;

namespace ClaudeBarWin.Services.Telemetry;

/// <summary>Las casillas de un aviso: <c>d</c> (siempre 1 al mandarlo), <c>m</c> primer aviso del mes y <c>n</c> primer aviso
/// de una instalación nueva.</summary>
public readonly record struct UsagePingFlags(bool Month, bool NewInstall)
{
    public bool Day => true;
}

/// <summary>Qué hacer tras intentar mandar un aviso.</summary>
public enum UsagePingResult
{
    /// <summary>2xx: se guarda el día, el mes y que ya hubo un primer aviso.</summary>
    Sent,
    /// <summary>Sin red, 5xx o 429: no se guarda nada y se vuelve a intentar en la siguiente comprobación (sin colas).</summary>
    Retry,
    /// <summary>El servidor rechaza el aviso (400 u otro 4xx/3xx): no se insiste más ese día.</summary>
    FailedToday,
}

/// <summary>
/// Contador anónimo de uso: cuánta gente usa ClaudeBar sin saber quién. PURO (sin red, reloj ni disco): con lo guardado y la
/// fecha local decide si toca avisar y con qué casillas. Como mucho un aviso por día local; con el reloj atrasado respecto a
/// lo guardado, nada (y lo guardado no se toca). El estado solo cambia con <see cref="Commit"/>, tras un 2xx.
/// </summary>
public static class UsagePing
{
    /// <summary>Quien actualiza ve un globo y el primer aviso espera al menos esto.</summary>
    public static readonly TimeSpan NoticeDelay = TimeSpan.FromMinutes(10);

    public const string DayFormat = "yyyy-MM-dd", MonthFormat = "yyyy-MM";

    public static string DayOf(DateTime local) => local.ToString(DayFormat, CultureInfo.InvariantCulture);
    public static string MonthOf(DateTime local) => local.ToString(MonthFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// ¿Toca avisar hoy (<paramref name="localNow"/>, hora local)? null si ya se avisó hoy, si el reloj va por detrás de lo
    /// guardado o si hoy ya falló sin remedio (<paramref name="failedDay"/>, en memoria). Un valor guardado ilegible cuenta
    /// como que no hay.
    /// </summary>
    public static UsagePingFlags? Decide(UsageStatsData? state, DateTime localNow, string? failedDay = null)
    {
        state ??= new UsageStatsData();
        string today = DayOf(localNow), month = MonthOf(localNow);
        if (failedDay == today) return null;

        string? lastDay = Valid(state.LastDay, DayFormat);
        string? lastMonth = Valid(state.LastMonth, MonthFormat);
        // El mes del último día cuenta también (por si los dos valores no casan: un config.json editado a mano).
        if (lastDay is not null && (lastMonth is null || string.CompareOrdinal(lastDay[..7], lastMonth) > 0)) lastMonth = lastDay[..7];

        if (lastDay is not null && string.CompareOrdinal(today, lastDay) <= 0) return null;     // hoy ya, o reloj atrasado
        if (lastMonth is not null && string.CompareOrdinal(month, lastMonth) < 0) return null;  // reloj atrasado
        return new UsagePingFlags(Month: lastMonth != month, NewInstall: state.NewInstall && !state.FirstSent);
    }

    /// <summary>Tras un 2xx: hoy y este mes ya avisados, y el primer aviso ya hecho.</summary>
    public static UsageStatsData Commit(UsageStatsData? state, DateTime localNow) =>
        (state ?? new UsageStatsData()) with { LastDay = DayOf(localNow), LastMonth = MonthOf(localNow), FirstSent = true };

    /// <summary>Respuesta HTTP (null = sin respuesta: red, DNS, tiempo agotado) → qué hacer.</summary>
    public static UsagePingResult Classify(int? status) => status switch
    {
        null => UsagePingResult.Retry,
        >= 200 and < 300 => UsagePingResult.Sent,
        429 or 408 => UsagePingResult.Retry,
        >= 500 => UsagePingResult.Retry,
        _ => UsagePingResult.FailedToday,
    };

    /// <summary>
    /// ¿Se puede mandar algo ahora? Activado (ajuste y entorno), la bienvenida ya hecha, el aviso del contador ya visto y, si
    /// se vio en el globo de quien actualiza, pasados <see cref="NoticeDelay"/> (con el reloj atrasado, se espera).
    /// </summary>
    public static bool MaySend(bool enabled, bool environmentAllows, bool onboardingPending, UsageStatsData? state, DateTime utcNow)
    {
        if (!enabled || !environmentAllows || onboardingPending || state is not { NoticeSeen: true }) return false;
        if (state.NoticeUtc is not { } shown) return true;
        return utcNow - AsUtc(shown) >= NoticeDelay;
    }

    /// <summary>Quien ya usaba la app y actualiza: la primera vez, un globo discreto con el aviso (una sola vez).</summary>
    public static bool NeedsUpgradeNotice(bool enabled, bool environmentAllows, bool onboardingPending, UsageStatsData? state) =>
        enabled && environmentAllows && !onboardingPending && state is not { NoticeSeen: true };

    /// <summary>El globo ya salió: visto, y el reloj de la espera empieza ahora.</summary>
    public static UsageStatsData NoticeShown(UsageStatsData? state, DateTime utcNow) =>
        (state ?? new UsageStatsData()) with { NoticeSeen = true, NoticeUtc = AsUtc(utcNow) };

    /// <summary>Visto en la bienvenida o en Ajustes (sin espera; si ya estaba visto, como estaba).</summary>
    public static UsageStatsData NoticeSeenInApp(UsageStatsData? state) =>
        state is { NoticeSeen: true } ? state : (state ?? new UsageStatsData()) with { NoticeSeen = true, NoticeUtc = null };

    private static DateTime AsUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),
    };

    private static string? Valid(string? value, string format) =>
        value is not null && DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? value : null;
}

/// <summary>
/// Un aviso tal cual sale: versión, edición, idioma y las tres casillas. NADA más (ni identificadores, ni equipo, ni Windows,
/// ni datos de Claude). El formato es el que valida <c>server/stats-worker/src/index.js</c>.
/// </summary>
public sealed record UsagePingPayload(string Version, string Edition, string Language, UsagePingFlags Flags)
{
    // Los mismos patrones que el servidor (VERSION_RE, LANG_RE, EDITIONS): lo que no encaje no se manda.
    private static readonly Regex VersionRe = new(@"^\d{1,3}\.\d{1,3}\.\d{1,4}(?:-[0-9A-Za-z.]{1,20})?$", RegexOptions.CultureInvariant);
    private static readonly Regex LangRe = new(@"^[a-z]{2,3}(?:-[A-Z]{2})?$", RegexOptions.CultureInvariant);
    public static readonly IReadOnlyList<string> Editions = new[] { "free", "pro", "github" };

    /// <summary>¿Lo aceptaría el servidor? (si no, mandarlo solo daría un 400).</summary>
    public bool IsValid => VersionRe.IsMatch(Version ?? "") && Editions.Contains(Edition) && LangRe.IsMatch(Language ?? "");

    /// <summary><c>{"v":"0.5.0","e":"free","l":"es","d":1,"m":1,"n":0}</c>, siempre en este orden.</summary>
    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("v", Version);
            w.WriteString("e", Edition);
            w.WriteString("l", Language);
            w.WriteNumber("d", Flags.Day ? 1 : 0);
            w.WriteNumber("m", Flags.Month ? 1 : 0);
            w.WriteNumber("n", Flags.NewInstall ? 1 : 0);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

/// <summary>De dónde salen versión, edición e idioma del aviso. PURO.</summary>
public static class UsageStatsFacts
{
    /// <summary><c>pro</c> con el Pro comprado; si no, <c>free</c> en la edición de la Store (identidad de paquete, también
    /// durante la prueba) y <c>github</c> en la de GitHub (Velopack, winget).</summary>
    public static string Edition(ProState state, bool packaged) =>
        state == ProState.Pro ? "pro" : packaged ? "free" : "github";

    /// <summary>
    /// El idioma de la interfaz: el elegido o, con «system», el que resuelve <see cref="Localization.ResolveSystemCode"/>
    /// (<paramref name="systemCode"/>). Uno que la app no tiene cae al inglés, como la interfaz. «zh-Hant» va como «zh» (el
    /// servidor solo admite <c>xx</c> o <c>xx-YY</c>, y la app solo tiene chino tradicional).
    /// </summary>
    public static string Language(string? configured, string systemCode)
    {
        string code = string.IsNullOrEmpty(configured) || configured == "system" ? systemCode : configured;
        if (!Localization.Languages.Any(l => l.Code == code)) code = "en";
        return code == "zh-Hant" ? "zh" : code;
    }

    /// <summary>Versión de la app como <c>Major.Minor.Build</c> (la de «Acerca de»).</summary>
    public static string Version(Version? v) =>
        v is null ? "0.0.0" : $"{Math.Max(0, v.Major)}.{Math.Max(0, v.Minor)}.{Math.Max(0, v.Build)}";
}
