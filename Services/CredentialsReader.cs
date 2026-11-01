namespace ClaudeBarWin.Services;

public sealed record PlanInfo(string SubscriptionType, string RateLimitTier)
{
    public string Display
    {
        get
        {
            if (string.IsNullOrEmpty(SubscriptionType)) return "Plan desconocido";

            string sub = char.ToUpperInvariant(SubscriptionType[0]) + SubscriptionType[1..];
            string tier = RateLimitTier switch
            {
                var t when t.Contains("max_20x") => "Max 20x",
                var t when t.Contains("max_5x") => "Max 5x",
                var t when t.Contains("pro") => "Pro",
                _ => string.Empty
            };

            // El tier ya repite el nombre del plan ("Max 5x"), que también es `sub` → sin deduplicar salía
            // "Max · Max 5x" (auditoría visual, T9). Quita el `sub ` redundante del inicio del tier y
            // antepone "Plan" una sola vez → "Plan Max · 5x" / "Plan Pro" / "Plan Max".
            if (tier.StartsWith(sub + " ", StringComparison.OrdinalIgnoreCase))
                tier = tier[(sub.Length + 1)..];
            else if (string.Equals(tier, sub, StringComparison.OrdinalIgnoreCase))
                tier = string.Empty;

            return string.IsNullOrEmpty(tier) ? $"Plan {sub}" : $"Plan {sub} · {tier}";
        }
    }

    /// <summary>
    /// Plan a partir del <c>organization_type</c> de la API de perfil ("claude_max", "claude_pro", "claude_team"…).
    /// Null si no es una suscripción de claude.ai (p. ej. una organización de API): entonces manda el respaldo.
    /// </summary>
    public static PlanInfo? FromOrganizationType(string? organizationType, string? rateLimitTier)
    {
        const string prefix = "claude_";
        if (string.IsNullOrWhiteSpace(organizationType)
            || !organizationType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || organizationType.Length == prefix.Length)
            return null;
        return new PlanInfo(organizationType[prefix.Length..].ToLowerInvariant(), rateLimitTier ?? "");
    }
}

/// <summary>
/// Reads ONLY the non-secret plan fields from ~/.claude/.credentials.json, plus whether an access token is present
/// (to tell "signed out" apart) without decoding it. Never keeps, stores, logs, or transmits access/refresh tokens.
/// El archivo lo abre <see cref="CredentialsFile"/> (lista blanca de campos, bytes a cero al terminar).
/// <para>
/// OJO: Claude Code escribe <c>subscriptionType</c>/<c>rateLimitTier</c> al hacer login y NO los actualiza al
/// renovar el token: tras pasar de Pro a Max el archivo sigue diciendo «pro» hasta el siguiente /login. Por eso
/// el plan que se enseña lo decide <see cref="PlanResolver"/> con la API de perfil; esto es solo el respaldo.
/// </para>
/// </summary>
public static class CredentialsReader
{
    public static PlanInfo Read() => Read(ClaudePaths.CredentialsPath);

    internal static PlanInfo Read(string path) => ReadState(path)?.Plan ?? new PlanInfo("", "");

    public static CredentialsState? ReadState() => ReadState(ClaudePaths.CredentialsPath);

    /// <summary>
    /// Plan + si hay sesión iniciada (hay token, sin leerlo más allá de comprobar que existe) + cuándo se escribió.
    /// Sin archivo = sesión cerrada. Null = ilegible ahora mismo (p. ej. Claude Code reescribiéndolo): no es un
    /// cambio de cuenta, el llamador debe seguir con lo último que sabía.
    /// </summary>
    internal static CredentialsState? ReadState(string path) => CredentialsFile.ReadState(path);
}

/// <summary>
/// <see cref="CredentialsReader.ReadState(string)"/> con memoria: el archivo solo se vuelve a abrir si cambió su fecha
/// de escritura o su tamaño (se mira sin abrirlo). <see cref="PlanResolver"/> pregunta en cada refresco del temporizador
/// y casi nunca hay nada nuevo; así <c>.credentials.json</c> se lee cuando Claude Code lo reescribe, no cada minuto.
/// </summary>
internal sealed class CredentialsStateCache
{
    private readonly Func<string> _path;
    private (DateTime WrittenUtc, long Length)? _stamp;
    private CredentialsState? _last;

    public CredentialsStateCache(Func<string> path) => _path = path;

    /// <summary>Cuántas veces se abrió el archivo de verdad (para tests).</summary>
    internal int Reads { get; private set; }

    public CredentialsState? Read()
    {
        var path = _path();
        var stamp = CredentialsFile.Stamp(path);
        if (stamp is null)
        {
            _stamp = null;
            _last = null;
            return CredentialsReader.ReadState(path); // sin archivo: sesión cerrada, sin abrir nada
        }
        if (_last is not null && stamp == _stamp) return _last;

        Reads++;
        var state = CredentialsReader.ReadState(path);
        if (state is not null)
        {
            _stamp = stamp;
            _last = state;
        }
        return state; // ilegible: no se recuerda, se reintenta en la próxima
    }
}

/// <summary>Lo no secreto de <c>.credentials.json</c> que necesita <see cref="PlanResolver"/>.</summary>
public sealed record CredentialsState(PlanInfo Plan, bool HasToken, DateTime WrittenUtc);
