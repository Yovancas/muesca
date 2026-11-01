namespace ClaudeBarWin.Services;

/// <summary>
/// v0.4 — ¿toca pedir la cuota a la API en este tick? Con statusline fresca (alguna sesión la escribió hace
/// &lt; 10 min) solo cada 15 min: Opus/Sonnet por separado, extra_usage y uso hecho fuera de Claude Code. Sin ella,
/// en cada tick como en la v0.3.10, pero nunca dos veces en menos de <see cref="MinApiGap"/>. El backoff de 429 de
/// UsageApiClient sigue mandando por encima.
/// </summary>
public static class QuotaPolicy
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan SlowApiInterval = TimeSpan.FromMinutes(15);
    /// <summary>Hueco mínimo entre dos peticiones de cuota sea cual sea el refresco (30 s, 15 s a mano en config.json o
    /// «Actualizar ahora» repetido): el límite de /api/oauth/usage es por cuenta y lo comparte la statusline. 55 s y no
    /// 60 para que el tick de 1 min, que puede llegar unos ms antes, no se salte una vuelta.</summary>
    public static readonly TimeSpan MinApiGap = TimeSpan.FromSeconds(55);

    public static bool IsFresh(DateTime? lastSeenUtc, DateTime nowUtc) =>
        lastSeenUtc is { } t && nowUtc - t < FreshFor;

    /// <summary>
    /// Notch: ¿la lectura vale para el hub de esta ejecución? Solo si se observó después de arrancar la app (o
    /// fresca al arrancar, &lt; <see cref="FreshFor"/> antes, como una statusline recién escrita). La caché de
    /// UsageCache puede tener horas o días: si fuera la primera lectura del tracker de umbrales gastaría su gracia de
    /// arranque y el primer refresco real avisaría de un cruce que ya estaba hecho antes de abrir la app.
    /// </summary>
    public static bool IsCurrentForRun(DateTime observedUtc, DateTime startedUtc) =>
        observedUtc > startedUtc - FreshFor;

    /// <summary>
    /// Solo cuentan los snapshots si el enganche sigue en settings.json. Si el usuario lo quitó (p. ej. con
    /// /statusline), un snapshot huérfano aún «fresco» no puede frenar la API ni enseñarse como dato vivo (V04-05).
    /// </summary>
    public static IReadOnlyList<RateLimitSnapshot> UsableSnapshots(bool tapInstalled, IReadOnlyList<RateLimitSnapshot> snaps) =>
        tapInstalled ? snaps : Array.Empty<RateLimitSnapshot>();

    public static bool ShouldCallApi(DateTime? lastSeenUtc, DateTime lastApiAttemptUtc, DateTime nowUtc) =>
        nowUtc - lastApiAttemptUtc >= MinApiGap
        && (!IsFresh(lastSeenUtc, nowUtc) || nowUtc - lastApiAttemptUtc >= SlowApiInterval);

    /// <summary>
    /// Estado que ve el usuario. Statusline fresca = dato bueno aunque la API esté en 429 u offline. Con la lectura por
    /// la sesión de Claude Code apagada (<c>QuotaOAuth</c>) no hay API: <see cref="UsageFetchState.StatuslineOnly"/>,
    /// que la interfaz enseña como «última lectura hace X» y no como error.
    /// </summary>
    public static UsageFetchState EffectiveState(bool oauthEnabled, bool liveFromStatusline, UsageFetchState apiState) =>
        liveFromStatusline ? UsageFetchState.Ok
        : !oauthEnabled ? UsageFetchState.StatuslineOnly
        : apiState;
}
