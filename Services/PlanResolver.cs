using System.Text.Json;

namespace ClaudeBarWin.Services;

/// <summary>
/// Decide qué plan se enseña (cabecera del panel, globos de aviso, <c>--report</c>).
/// <para>
/// Antes salía SOLO de <c>~/.claude/.credentials.json</c>, leído una vez al arrancar. Dos fallos: Claude Code escribe
/// ahí el plan al hacer login y no lo toca al renovar el token (tras pasar de Pro a Max seguía diciendo «pro»), y
/// aunque cambiase, ClaudeBar no lo volvía a leer hasta reiniciarse.
/// </para>
/// <para>
/// Ahora la fuente buena es la API de perfil (<see cref="UsageApiClient.FetchProfileAsync"/>): se consulta al
/// arrancar y cada <see cref="RefreshInterval"/> (cada <see cref="RetryAfterFailure"/> si falla) y su último
/// resultado se guarda en disco para enseñarlo al instante en el próximo arranque, también sin red. El
/// <c>.credentials.json</c> queda de respaldo y de testigo: cada vez que Claude Code lo reescribe (un /login, aunque
/// sea a otra cuenta con el mismo plan obsoleto, o un token renovado) se vuelve a preguntar enseguida, como mucho
/// una vez cada <see cref="MinGapAfterChange"/>; si además su plan cambió, lo que se sabía se descarta ya; y con la
/// sesión cerrada (sin token) no se enseña el plan de la cuenta anterior.
/// </para>
/// <para>
/// Con «Leer la cuota con tu sesión de Claude Code» apagado (<c>QuotaOAuth</c>) no se pregunta a la API ni se abre
/// <c>.credentials.json</c> (ni para mirar su plan ni para saber si hay sesión): queda lo último que dijo la API de perfil
/// (en memoria o en <c>plan.json</c>) o «Plan desconocido».
/// </para>
/// No es seguro para llamadas concurrentes a <see cref="RefreshAsync"/> (la app las serializa con su refresco);
/// <see cref="Current"/> sí se puede leer desde cualquier hilo.
/// </summary>
public sealed class PlanResolver
{
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan MinGapAfterChange = TimeSpan.FromMinutes(2);

    private static readonly CredentialsState NoCredentials = new(new PlanInfo("", ""), HasToken: false, DateTime.MinValue);

    public static string DefaultCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeBarWin", "plan.json");

    private readonly Func<Task<PlanInfo?>> _fetchProfile;
    private readonly Func<CredentialsState?> _readCredentials;
    private readonly Func<DateTime> _utcNow;
    private readonly string _cachePath;

    private PlanInfo? _profile;          // último plan bueno de la API (o de la caché, mientras siga valiendo)
    private PlanInfo? _credsAtProfile;   // lo que decía .credentials.json cuando se obtuvo _profile
    private CredentialsState _lastCreds; // última lectura LEGIBLE de .credentials.json
    private DateTime _nextFetchUtc = DateTime.MinValue;
    private DateTime _lastAttemptUtc = DateTime.MinValue;

    private bool _credentialsOff;        // la última vuelta fue con el ajuste apagado (ver UseProfileOnly)
    private bool _credentialsSeen;       // en este arranque se llegó a leer .credentials.json

    private volatile PlanInfo _current;
    private volatile string _source = "credentials";

    /// <param name="useCredentials">Ajuste <c>QuotaOAuth</c> al arrancar. Apagado, ni se abre <c>.credentials.json</c>.</param>
    public PlanResolver(UsageApiClient api, bool useCredentials)
        : this(api.FetchProfileAsync, new CredentialsStateCache(() => ClaudePaths.CredentialsPath).Read,
            () => DateTime.UtcNow, DefaultCachePath, useCredentials) { }

    internal PlanResolver(Func<Task<PlanInfo?>> fetchProfile, Func<CredentialsState?> readCredentials,
        Func<DateTime> utcNow, string cachePath, bool useCredentials = true)
    {
        _fetchProfile = fetchProfile;
        _readCredentials = readCredentials;
        _utcNow = utcNow;
        _cachePath = cachePath;
        _lastCreds = NoCredentials;
        _current = NoCredentials.Plan;

        if (!useCredentials)
        {
            UseProfileOnly();
            return;
        }

        _lastCreds = SafeReadCredentials() ?? NoCredentials;
        _current = _lastCreds.Plan;

        // Lo cacheado solo vale con sesión iniciada y si se obtuvo con el mismo plan en .credentials.json. No prueba que
        // sea la misma cuenta (eso lo confirma la consulta al perfil que se hace nada más arrancar): sirve para enseñar
        // algo bueno ya, también sin red.
        if (_lastCreds.HasToken && LoadCache() is { } cached && cached.Credentials == _lastCreds.Plan && !IsEmpty(cached.Plan))
        {
            _profile = cached.Plan;
            _credsAtProfile = cached.Credentials;
            _current = cached.Plan;
            _source = "cache";
        }
    }

    /// <summary>Plan a enseñar ahora mismo.</summary>
    public PlanInfo Current => _current;

    /// <summary>De dónde salió <see cref="Current"/>: <c>profile</c> (API), <c>cache</c>, <c>credentials</c> o <c>none</c>
    /// (ajuste apagado y nada de la API: «Plan desconocido» sin haber mirado el archivo).</summary>
    public string Source => _source;

    /// <summary>
    /// Pone el plan al día si toca. Barato si no toca (mira la fecha de <c>.credentials.json</c> y solo lo relee si
    /// Claude Code lo reescribió; ver <see cref="CredentialsStateCache"/>). Nunca lanza.
    /// Devuelve true si <see cref="Current"/> cambió.
    /// </summary>
    /// <param name="useProfileApi">Ajuste <c>QuotaOAuth</c>. Apagado no se pregunta a la API de perfil ni se abre
    /// <c>.credentials.json</c> (<see cref="UseProfileOnly"/>).</param>
    public async Task<bool> RefreshAsync(bool useProfileApi = true)
    {
        var before = _current;
        if (useProfileApi)
        {
            _credentialsOff = false;
            await RefreshWithCredentialsAsync().ConfigureAwait(false);
        }
        else if (!_credentialsOff) UseProfileOnly();

        bool changed = _current != before;
        if (changed) AppLog.Info($"plan: {before.Display} → {_current.Display} ({_source})");
        return changed;
    }

    private async Task RefreshWithCredentialsAsync()
    {
        var now = _utcNow();

        // Ilegible justo ahora (Claude Code reescribiéndolo, bloqueado…): no es un cambio de cuenta → lo último.
        var read = SafeReadCredentials();
        var creds = read ?? _lastCreds;
        bool fileChanged = read is not null && read.WrittenUtc != _lastCreds.WrittenUtc;
        _lastCreds = creds;

        if (!creds.HasToken)
        {
            // Sesión cerrada: el plan de la cuenta anterior ya no es de nadie, y sin token no hay a quién preguntar.
            // Al volver a iniciar sesión el archivo cambia y se pregunta en esa misma vuelta.
            _profile = null;
            _credsAtProfile = null;
        }
        else
        {
            if (_profile is not null && _credsAtProfile != creds.Plan)
            {
                // Su plan cambió (nuevo /login u otra cuenta): lo que se sabía era de antes → respaldo ya.
                _profile = null;
                _credsAtProfile = null;
                AskSoon(now);
            }
            else if (fileChanged)
            {
                // Reescrito con el mismo plan: un /login a otra cuenta con el mismo plan obsoleto, o un token renovado.
                // Se sigue enseñando lo que había mientras se confirma (sin parpadeos si la red falla).
                AskSoon(now);
            }

            if (now >= _nextFetchUtc)
            {
                _lastAttemptUtc = now;
                PlanInfo? fetched = null;
                try { fetched = await _fetchProfile().ConfigureAwait(false); }
                catch { /* best-effort: el llamador sigue con lo que había */ }

                if (fetched is not null && !IsEmpty(fetched))
                {
                    _profile = fetched;
                    _credsAtProfile = creds.Plan;
                    _nextFetchUtc = now + RefreshInterval;
                    _source = "profile";
                    SaveCache(fetched, creds.Plan, now);
                }
                else
                {
                    _nextFetchUtc = now + RetryAfterFailure;
                }
            }
        }

        if (_profile is null) _source = "credentials";
        _current = _profile ?? creds.Plan;
    }

    /// <summary>
    /// Ajuste apagado: <c>.credentials.json</c> no se abre. Queda lo último que dijo la API de perfil (lo de esta sesión o,
    /// si en este arranque nunca se leyó el archivo, <c>plan.json</c>, sin poder comprobar que siga siendo la misma cuenta)
    /// o «Plan desconocido». Si el archivo se leyó y el perfil se descartó (sesión cerrada, otra cuenta), <c>plan.json</c>
    /// es de antes y no vuelve. Una vez por apagado: mientras siga así no hay nada nuevo que mirar. Al volver a encenderlo,
    /// el archivo se lee como siempre y, si cambió mientras tanto, se pregunta enseguida.
    /// </summary>
    private void UseProfileOnly()
    {
        _credentialsOff = true;
        if (_profile is null && !_credentialsSeen && LoadCache() is { } cached && !IsEmpty(cached.Plan))
        {
            _profile = cached.Plan;
            _credsAtProfile = cached.Credentials;
            _source = "cache";
        }
        if (_profile is null) _source = "none";
        _current = _profile ?? NoCredentials.Plan;
    }

    /// <summary>
    /// Adelanta la próxima consulta a «ya», pero nunca a menos de <see cref="MinGapAfterChange"/> de la anterior: si el
    /// archivo se reescribe sin parar (varias sesiones de Claude Code renovando), la API de perfil no se martillea.
    /// </summary>
    private void AskSoon(DateTime now)
    {
        var earliest = _lastAttemptUtc == DateTime.MinValue ? now : _lastAttemptUtc + MinGapAfterChange;
        var target = earliest > now ? earliest : now;
        if (target < _nextFetchUtc) _nextFetchUtc = target;
    }

    private CredentialsState? SafeReadCredentials()
    {
        CredentialsState? state;
        try { state = _readCredentials(); }
        catch { return null; }
        if (state is not null) _credentialsSeen = true;
        return state;
    }

    private static bool IsEmpty(PlanInfo p) =>
        string.IsNullOrEmpty(p.SubscriptionType) && string.IsNullOrEmpty(p.RateLimitTier);

    // ---------- caché en disco (%APPDATA%\ClaudeBarWin\plan.json) ----------

    private sealed class CacheDto
    {
        public string? SubscriptionType { get; set; }
        public string? RateLimitTier { get; set; }
        public string? CredentialsSubscriptionType { get; set; }
        public string? CredentialsRateLimitTier { get; set; }
        public DateTime FetchedAtUtc { get; set; }
    }

    private (PlanInfo Plan, PlanInfo Credentials)? LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return null;
            var dto = JsonSerializer.Deserialize<CacheDto>(File.ReadAllText(_cachePath));
            if (dto is null) return null;
            return (new PlanInfo(dto.SubscriptionType ?? "", dto.RateLimitTier ?? ""),
                    new PlanInfo(dto.CredentialsSubscriptionType ?? "", dto.CredentialsRateLimitTier ?? ""));
        }
        catch
        {
            return null;
        }
    }

    private void SaveCache(PlanInfo plan, PlanInfo creds, DateTime nowUtc)
    {
        try
        {
            var dto = new CacheDto
            {
                SubscriptionType = plan.SubscriptionType,
                RateLimitTier = plan.RateLimitTier,
                CredentialsSubscriptionType = creds.SubscriptionType,
                CredentialsRateLimitTier = creds.RateLimitTier,
                FetchedAtUtc = nowUtc,
            };
            AtomicFile.WriteAllText(_cachePath, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // no fatal: sin caché, el próximo arranque enseña el respaldo hasta la primera consulta
        }
    }
}
