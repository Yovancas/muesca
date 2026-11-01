namespace ClaudeBarWin.Services.Licensing;

/// <summary>
/// Edición gratis (GitHub / Velopack / winget): siempre <see cref="ProState.Free"/>, sin prueba (no hay forma de comprar
/// desde aquí) y con el enlace a la ficha de la Store para el botón «Conseguir Pro en Microsoft Store».
/// </summary>
public sealed class FreeEditionLicense : IProLicense
{
    public FreeEditionLicense(string? storeProductId = null) => StoreUrl = ProCatalog.StoreUrl(storeProductId);

    public ProState State => ProState.Free;
    public int? TrialDaysLeft => null;
    public bool TrialEnded => false;
    public bool CanPurchaseHere => false;
    public string? StoreUrl { get; }
    public string? Price => null;

    /// <summary>Nunca cambia.</summary>
    public event Action? Changed { add { } remove { } }

    public Task<PurchaseResult> PurchaseAsync() => Task.FromResult(PurchaseResult.NotAvailable);
    public Task<PurchaseResult> RestoreAsync() => Task.FromResult(PurchaseResult.NotAvailable);
    public Task RefreshAsync() => Task.CompletedTask;
    public void Dispose() { }
}
