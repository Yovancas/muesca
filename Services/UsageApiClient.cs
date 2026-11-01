using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ClaudeBarWin.Services;

public sealed record UsageWindow(double UtilizationPct, DateTimeOffset? ResetsAt);

public sealed class RealUsage
{
    public UsageWindow? FiveHour { get; init; }
    public UsageWindow? SevenDay { get; init; }
    public UsageWindow? SevenDayOpus { get; init; }
    public UsageWindow? SevenDaySonnet { get; init; }
    public bool ExtraUsageEnabled { get; init; }

    public double MaxUtilization =>
        Math.Max(FiveHour?.UtilizationPct ?? 0, SevenDay?.UtilizationPct ?? 0);
}

public enum UsageFetchState
{
    Ok,
    NoCredentials,
    AuthExpired,
    RateLimited,
    NetworkError,
    /// <summary>Lectura con el token OAuth apagada en Ajustes: la cuota sale solo de la statusline. No es un error; sin
    /// sesión de Claude Code abierta se enseña la última lectura con su edad.</summary>
    StatuslineOnly
}

public sealed class UsageResult
{
    public UsageFetchState State { get; init; }
    public RealUsage? Usage { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Reads real Claude Code quota from Anthropic's OAuth usage endpoint using the
/// locally stored OAuth token. Mirrors the approach of CodeZeno/Claude-Code-Usage-Monitor.
/// The token is used only to read the user's own usage; it is never stored or transmitted
/// anywhere else: se lee justo antes de cada petición (<see cref="CredentialsFile"/>) y solo viaja por
/// <see cref="OAuthTransport"/> (HTTPS a api.anthropic.com, sin redirecciones).
///
/// v0.3.10 (auditoría cruzada Claude + Codex):
///  • ClaudeBar ya NO refresca el token OAuth por su cuenta ni escribe ~/.claude/.credentials.json.
///    Ese POST rotaba el refresh token en el servidor; si Claude Code refrescaba a la vez (sus sesiones
///    lo hacen bajo su propio lock), una de las dos escrituras perdía y Claude Code podía quedarse con
///    un refresh token ya consumido → login forzado. Ahora se relee el archivo en cada poll (Claude Code
///    lo renueva solo) y, como último recurso, `claude -p .` (mecanismo y lock oficiales) como mucho
///    una vez cada <see cref="CliRefreshCooldown"/>. Desde la 0.4 ese último recurso solo corre con el ajuste
///    <c>QuotaCliRefresh</c> (apagado por defecto): el prompt gasta un mensaje de la cuota del usuario.
///  • 429 con backoff exponencial + jitter (300 → 600 → 1200 → … ≤ 3600 s) que se relaja con cada
///    OK, y Retry-After en segundos O en fecha HTTP. Antes era fijo (300 s) y la app chocaba con el
///    límite por cuenta (compartido con ccstatusline) ~100 veces al día. Durante la espera ni se abre
///    .credentials.json.
///  • Token inservible (caducado, o rechazado con 401/403): mientras .credentials.json no cambie (fecha y
///    tamaño, sin abrirlo) seguirá igual, así que ni se relee ni se gasta una petición hasta que Claude Code lo
///    reescriba, o como mucho cada <see cref="DeadTokenRecheck"/>. No se guarda nada derivado del token.
/// </summary>
public sealed class UsageApiClient
{
    private static readonly Uri UsageUri = new("https://api.anthropic.com/api/oauth/usage");
    private static readonly Uri ProfileUri = new("https://api.anthropic.com/api/oauth/profile");
    private const string Beta = "oauth-2025-04-20";

    internal const int BaseBackoffSeconds = 300;
    internal const int MaxBackoffSeconds = 3600;
    internal static readonly TimeSpan CliRefreshCooldown = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan DeadTokenRecheck = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly Func<string> _credPath;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<bool> _cliRefresh;
    private readonly Func<double> _jitter;

    private DateTime _cooldownUntilUtc = DateTime.MinValue;
    private int _backoffLevel;
    private DateTime _lastCliRefreshUtc = DateTime.MinValue;
    private UsageFetchState? _lastState;
    // Huella de .credentials.json (fecha de escritura y tamaño) cuando su token resultó inservible.
    private (DateTime WrittenUtc, long Length)? _deadTokenStamp;
    private DateTime _deadTokenUntilUtc;

    public UsageApiClient() : this(null, null, null, null, null) { }

    /// <summary>Constructor inyectable para tests (HTTP falso, ruta de credenciales, reloj, refresh CLI, jitter).</summary>
    internal UsageApiClient(HttpMessageHandler? handler, Func<string>? credPath, Func<DateTime>? utcNow,
        Func<bool>? cliRefresh, Func<double>? jitter)
    {
        _http = OAuthTransport.CreateClient(handler);
        _credPath = credPath ?? (() => ClaudePaths.CredentialsPath);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _cliRefresh = cliRefresh ?? TryCliRefresh;
        _jitter = jitter ?? (() => 0.9 + Random.Shared.NextDouble() * 0.2); // ±10 %
    }

    /// <summary>Hasta cuándo no se volverá a llamar a la API por un 429 (MinValue = sin espera).</summary>
    public DateTime CooldownUntilUtc => _cooldownUntilUtc;

    /// <param name="allowCliRefresh">Ajuste <c>QuotaCliRefresh</c>: con el token caducado, lanzar <c>claude -p .</c>
    /// (gasta un mensaje). Sin él se devuelve <see cref="UsageFetchState.AuthExpired"/> sin más.</param>
    public async Task<UsageResult> FetchAsync(bool allowCliRefresh = false)
    {
        var result = await FetchCoreAsync(allowCliRefresh).ConfigureAwait(false);
        if (result.State != _lastState)
        {
            string extra = result.State == UsageFetchState.RateLimited && _cooldownUntilUtc > _utcNow()
                ? $" (reintento a las {_cooldownUntilUtc:HH:mm:ss}Z, nivel {_backoffLevel})"
                : result.Error is { Length: > 0 } e ? $" ({e})" : "";
            AppLog.Info($"cuota: {_lastState?.ToString() ?? "inicio"} → {result.State}{extra}");
            _lastState = result.State;
        }
        return result;
    }

    private async Task<UsageResult> FetchCoreAsync(bool allowCliRefresh)
    {
        var now = _utcNow();
        // Back off while rate-limited: serve last-good data without hitting the API.
        if (now < _cooldownUntilUtc)
            return new UsageResult { State = UsageFetchState.RateLimited };

        // .credentials.json solo se abre aquí, cuando de verdad toca pedir la cuota. Si su token ya resultó inservible
        // y el archivo no ha cambiado desde entonces, ni se abre (salvo que toque forzar con la CLI).
        var path = _credPath();
        var stamp = CredentialsFile.Stamp(path);
        bool cliDue = allowCliRefresh && now - _lastCliRefreshUtc >= CliRefreshCooldown;
        if (!cliDue && stamp is not null && stamp == _deadTokenStamp && now < _deadTokenUntilUtc)
            return new UsageResult { State = UsageFetchState.AuthExpired };

        var creds = ReadCreds(path);
        if (creds is null)
            return new UsageResult { State = UsageFetchState.NoCredentials };

        if (IsExpired(creds.ExpiresAtMs, now, skewMs: 60_000))
        {
            // Claude Code renueva el token por su cuenta; solo si sigue caducado, y si el usuario lo permitió (gasta un
            // mensaje), se fuerza con la CLI, con enfriamiento (antes podía lanzar `claude -p .` en CADA tick).
            if (cliDue)
            {
                _lastCliRefreshUtc = now;
                AppLog.Info("token caducado: forzando refresh con la CLI de Claude Code");
                try { _cliRefresh(); } catch { /* best-effort */ }
                stamp = CredentialsFile.Stamp(path);
                creds = ReadCreds(path) ?? creds;
            }
            // Caducado de verdad (sin margen): no gastar una petición del límite compartido en un 401.
            if (IsExpired(creds.ExpiresAtMs, _utcNow(), skewMs: 0))
                return DeadToken(stamp, now);
        }

        try
        {
            using var resp = await OAuthTransport.GetAsync(_http, UsageUri, creds, Beta).ConfigureAwait(false);
            creds = null; // ya enviado: la referencia no sigue viva el resto del método
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return DeadToken(stamp, now);

            if ((int)resp.StatusCode == 429)
            {
                _backoffLevel = Math.Min(_backoffLevel + 1, 8);
                double backoff = Math.Min(MaxBackoffSeconds, BaseBackoffSeconds * Math.Pow(2, _backoffLevel - 1));
                double wait = backoff * _jitter();
                if (RetryAfterSeconds(resp, _utcNow()) is { } ra)
                    wait = Math.Max(wait, Math.Min(ra, 6 * 3600)); // respetar al servidor (cordura: ≤ 6 h)
                _cooldownUntilUtc = _utcNow().AddSeconds(wait);
                return new UsageResult { State = UsageFetchState.RateLimited };
            }

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var usage = Parse(json);
            if (_backoffLevel > 0) _backoffLevel--; // se relaja poco a poco, no de golpe
            _deadTokenStamp = null;
            return new UsageResult { State = UsageFetchState.Ok, Usage = usage };
        }
        catch (Exception ex)
        {
            return new UsageResult { State = UsageFetchState.NetworkError, Error = ex.Message };
        }
    }

    /// <summary>
    /// Plan real de la cuenta desde <c>/api/oauth/profile</c> (el <c>.credentials.json</c> se queda con el plan del
    /// último login). Endpoint distinto del de cuota: no gasta su límite. Sin efectos secundarios: con el token
    /// caducado NO fuerza el refresh por la CLI (eso es cosa de <see cref="FetchAsync"/>) y cualquier fallo
    /// (red, 401, 429, JSON raro) devuelve null para que el llamador siga con lo que tenía.
    /// </summary>
    public async Task<PlanInfo?> FetchProfileAsync()
    {
        var creds = ReadCreds(_credPath());
        if (creds is null || IsExpired(creds.ExpiresAtMs, _utcNow(), skewMs: 0)) return null;
        try
        {
            using var resp = await OAuthTransport.GetAsync(_http, ProfileUri, creds, Beta).ConfigureAwait(false);
            creds = null;
            if (!resp.IsSuccessStatusCode)
            {
                AppLog.Info($"plan: el perfil respondió {(int)resp.StatusCode}");
                return null;
            }
            return ParseProfile(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            AppLog.Info($"plan: perfil no disponible ({ex.GetType().Name})");
            return null;
        }
    }

    /// <summary>
    /// <c>organization.organization_type</c> ("claude_max") + <c>organization.rate_limit_tier</c>
    /// ("default_claude_max_5x"); si falta el tipo, los flags <c>account.has_claude_max/pro</c>. Null si nada sirve.
    /// </summary>
    internal static PlanInfo? ParseProfile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? tier = null;
            if (root.TryGetProperty("organization", out var org) && org.ValueKind == JsonValueKind.Object)
            {
                tier = Str(org, "rate_limit_tier");
                if (PlanInfo.FromOrganizationType(Str(org, "organization_type"), tier) is { } plan) return plan;
            }

            if (root.TryGetProperty("account", out var acc) && acc.ValueKind == JsonValueKind.Object)
            {
                if (IsTrue(acc, "has_claude_max")) return new PlanInfo("max", tier ?? "");
                if (IsTrue(acc, "has_claude_pro")) return new PlanInfo("pro", tier ?? "");
            }
            return null;
        }
        catch
        {
            return null;
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static bool IsTrue(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    }

    /// <summary>Retry-After en segundos ("120") o en fecha HTTP ("Fri, 25 Sep 2026 11:23:00 GMT").</summary>
    internal static double? RetryAfterSeconds(HttpResponseMessage resp, DateTime nowUtc)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra?.Delta is { } d) return Math.Max(0, d.TotalSeconds);
        if (ra?.Date is { } date) return Math.Max(0, (date.UtcDateTime - nowUtc).TotalSeconds);
        if (resp.Headers.TryGetValues("retry-after", out var vals)
            && double.TryParse(vals.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            return Math.Max(0, s);
        return null;
    }

    /// <summary>
    /// Token recién leído de <c>.credentials.json</c> (solo <c>accessToken</c> y <c>expiresAt</c>; ver
    /// <see cref="CredentialsFile"/>). Se pide justo antes de cada petición y no se guarda en ningún campo.
    /// </summary>
    private static Creds? ReadCreds(string path) => CredentialsFile.ReadToken(path);

    /// <summary>Token caducado o rechazado: se apunta la huella del archivo (no el token) para no insistir con él.</summary>
    private UsageResult DeadToken((DateTime WrittenUtc, long Length)? stamp, DateTime now)
    {
        _deadTokenStamp = stamp;
        _deadTokenUntilUtc = now + DeadTokenRecheck;
        return new UsageResult { State = UsageFetchState.AuthExpired };
    }

    private static bool IsExpired(long? expiresAtMs, DateTime nowUtc, long skewMs)
    {
        if (expiresAtMs is null) return false;
        var nowMs = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        return nowMs >= expiresAtMs.Value - skewMs;
    }

    /// <summary>
    /// Runs `claude -p .` headless to force Claude Code's internal OAuth refresh (bajo SU lock),
    /// which rewrites ~/.claude/.credentials.json. Best-effort, capped at 30s. OJO: es un prompt de verdad y gasta
    /// un mensaje de la cuota; solo se llama con el ajuste <c>QuotaCliRefresh</c>.
    /// </summary>
    private static bool TryCliRefresh()
    {
        try
        {
            var claude = ResolveClaudePath();
            if (claude is null) return false;

            bool isCmd = claude.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            var psi = new ProcessStartInfo
            {
                FileName = isCmd ? "cmd.exe" : claude,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            if (isCmd)
            {
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(claude);
            }
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(".");
            psi.Environment.Remove("CLAUDECODE");
            psi.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            // Drenar stdout/stderr: con la salida redirigida y sin leerla, un buffer lleno bloquearía al hijo.
            _ = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();
            proc.StandardInput.Close();
            if (!proc.WaitForExit(30_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveClaudePath()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "bin", "claude.exe");
        if (File.Exists(local)) return local;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                foreach (var name in new[] { "claude.exe", "claude.cmd" })
                {
                    var p = Path.Combine(dir.Trim(), name);
                    if (File.Exists(p)) return p;
                }
            }
            catch
            {
                // skip malformed PATH entries
            }
        }
        return null;
    }

    internal static RealUsage Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new RealUsage
        {
            FiveHour = ParseWindow(root, "five_hour"),
            SevenDay = ParseWindow(root, "seven_day"),
            SevenDayOpus = ParseWindow(root, "seven_day_opus"),
            SevenDaySonnet = ParseWindow(root, "seven_day_sonnet"),
            ExtraUsageEnabled = root.TryGetProperty("extra_usage", out var eu)
                && eu.ValueKind == JsonValueKind.Object
                && eu.TryGetProperty("is_enabled", out var en)
                && en.ValueKind == JsonValueKind.True
        };
    }

    private static UsageWindow? ParseWindow(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;

        double util = el.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number
            ? u.GetDouble()
            : 0;

        DateTimeOffset? reset = null;
        if (el.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dto))
            reset = dto;

        return new UsageWindow(util, reset);
    }
}

/// <summary>Combined snapshot consumed by the UI: live quota + optional local spend estimate.</summary>
public sealed class AppSnapshot
{
    public RealUsage? Usage { get; init; }          // last good live data
    public UsageFetchState LatestState { get; init; }
    public DateTime UsageAtUtc { get; init; }
    public WindowStats? Spend { get; init; }        // local ccusage-style estimate (optional)
    public int SpendDays { get; init; }
    public HealthStatus? Health { get; init; }      // Anthropic service status (optional)
    public PaceResult? PaceFive { get; init; }      // pace for the 5h window
    public PaceResult? PaceSeven { get; init; }     // pace for the 7d window
    /// <summary>v0.4: 5h/7d vienen de una statusline fresca (&lt; 10 min): no se sondea la API, así que la edad
    /// del dato no significa «viejo».</summary>
    public bool LiveFromStatusline { get; init; }
}
