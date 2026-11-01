namespace ClaudeBarWin.Services.Licensing;

/// <summary>
/// Elige la licencia al arrancar: con identidad de paquete, <see cref="StoreProLicense"/> (la edición de la Store); sin ella,
/// <see cref="FreeEditionLicense"/> (GitHub / Velopack / winget).
///
/// <para>Override de desarrollo: <c>CLAUDEBAR_PRO=pro|trial|free</c> SOLO en Debug. Todo el camino (la lectura de la variable
/// y la licencia de pruebas) está dentro de <c>#if DEBUG</c>: en Release no existe, ni siquiera como código muerto (lo vigila
/// un test que solo corre en Release).</para>
/// </summary>
public static class ProLicenseFactory
{
    public const string OverrideVariable = "CLAUDEBAR_PRO";

    /// <param name="packaged">¿Identidad de paquete? (<see cref="PackageIdentity.HasIdentity"/>).</param>
    /// <param name="backend">La Store de verdad; solo se crea con <paramref name="packaged"/>.</param>
    /// <param name="store">Dónde se guardan la prueba y el último estado conocido.</param>
    /// <param name="post">Lleva trabajo al hilo de UI.</param>
    /// <param name="storeProductId">Ficha de la Store (config); null = <see cref="ProCatalog.StoreProductId"/>.</param>
    /// <param name="env">Lee variables de entorno (solo cuenta en Debug).</param>
    public static IProLicense Create(bool packaged, Func<IStoreBackend> backend, IProStateStore store, Action<Action> post,
        string? storeProductId, Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(env);
#if DEBUG
        if (DebugOverride(env(OverrideVariable), post) is { } dev)
        {
            AppLog.Warn($"pro: {OverrideVariable}={dev.State} (solo en Debug)");
            return dev;
        }
#endif
        if (!packaged) return new FreeEditionLicense(storeProductId);
        return new StoreProLicense(backend(), store, post, storeProductId: storeProductId);
    }

#if DEBUG
    /// <summary>La licencia de <c>CLAUDEBAR_PRO</c> («pro», «trial» o «free», sin distinguir mayúsculas); null con cualquier
    /// otra cosa o sin la variable. Solo existe en Debug.</summary>
    internal static IProLicense? DebugOverride(string? value, Action<Action> post) => value?.Trim().ToLowerInvariant() switch
    {
        "pro" => new DebugProLicense(ProState.Pro, post),
        "trial" => new DebugProLicense(ProState.Trial, post),
        "free" => new DebugProLicense(ProState.Free, post),
        _ => null,
    };
#endif
}

#if DEBUG
/// <summary>
/// Licencia de desarrollo (<c>CLAUDEBAR_PRO</c>, solo Debug): hace como la edición de la Store para poder ver el candado, la
/// tarjeta y el desbloqueo en caliente sin paquete. «Comprar» pasa a Pro; «Restaurar compra» dice lo que haya.
/// </summary>
internal sealed class DebugProLicense : IProLicense
{
    private readonly Action<Action> _post;

    public DebugProLicense(ProState state, Action<Action> post)
    {
        State = state;
        _post = post;
    }

    public ProState State { get; private set; }
    public int? TrialDaysLeft => State == ProState.Trial ? 5 : null;
    public bool TrialEnded => State == ProState.Free;
    public bool CanPurchaseHere => true;
    public string? StoreUrl => ProCatalog.StoreUrl(null);
    public string? Price => "9,99 €";
    public event Action? Changed;

    public Task<PurchaseResult> PurchaseAsync()
    {
        if (State == ProState.Pro) return Task.FromResult(PurchaseResult.AlreadyOwned);
        State = ProState.Pro;
        _post(() => Changed?.Invoke());
        return Task.FromResult(PurchaseResult.Purchased);
    }

    public Task<PurchaseResult> RestoreAsync() =>
        Task.FromResult(State == ProState.Pro ? PurchaseResult.AlreadyOwned : PurchaseResult.NotOwned);

    public Task RefreshAsync() => Task.CompletedTask;
    public void Dispose() { }
}
#endif
