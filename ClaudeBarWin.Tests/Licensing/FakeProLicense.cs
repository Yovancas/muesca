using ClaudeBarWin.Services.Licensing;

namespace ClaudeBarWin.Tests.Licensing;

/// <summary>Licencia de pruebas: el estado se cambia a mano (<see cref="Set"/>) y <see cref="Changed"/> salta al momento;
/// cuenta las compras y restauraciones y devuelve lo que se le diga.</summary>
internal sealed class FakeProLicense : IProLicense
{
    public FakeProLicense(ProState state = ProState.Free, int? days = null, bool store = true)
    {
        State = state;
        TrialDaysLeft = days;
        CanPurchaseHere = store;
    }

    public ProState State { get; private set; }
    public int? TrialDaysLeft { get; private set; }
    public bool TrialEnded { get; set; }
    public bool CanPurchaseHere { get; }
    public string? StoreUrl => ProCatalog.StoreUrl(null);
    public string? Price { get; set; } = "9,99 €";
    public event Action? Changed;

    public PurchaseResult NextPurchase { get; set; } = PurchaseResult.Purchased;
    public PurchaseResult NextRestore { get; set; } = PurchaseResult.NotOwned;
    public int Purchases { get; private set; }
    public int Restores { get; private set; }
    public bool Disposed { get; private set; }

    public void Set(ProState state, int? days = null)
    {
        State = state;
        TrialDaysLeft = days;
        Changed?.Invoke();
    }

    public Task<PurchaseResult> PurchaseAsync()
    {
        Purchases++;
        if (NextPurchase is PurchaseResult.Purchased or PurchaseResult.AlreadyOwned) Set(ProState.Pro);
        return Task.FromResult(NextPurchase);
    }

    public Task<PurchaseResult> RestoreAsync()
    {
        Restores++;
        if (NextRestore == PurchaseResult.AlreadyOwned) Set(ProState.Pro);
        return Task.FromResult(NextRestore);
    }

    public Task RefreshAsync() => Task.CompletedTask;
    public void Dispose() => Disposed = true;
}
