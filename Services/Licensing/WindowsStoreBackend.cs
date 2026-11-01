using Windows.Services.Store;

namespace ClaudeBarWin.Services.Licensing;

/// <summary>
/// <see cref="IStoreBackend"/> con <c>Windows.Services.Store</c> (proyección WinRT que ya trae el TFM
/// <c>net9.0-windows10.0.19041.0</c>; sin paquetes NuGet). Solo funciona con identidad de paquete (MSIX de la Store): sin
/// ella <see cref="ProLicenseFactory"/> ni lo crea. En una app de escritorio <see cref="StoreContext"/> necesita una ventana
/// dueña para la ventana de compra (<c>InitializeWithWindow</c>, WinRT.Interop): cada compra usa un contexto NUEVO con la
/// ventana que haya viva y a la vista en ese momento (una fijada al primer uso puede morir: Windows destruye el HWND del panel
/// sin cerrar el form, y desde entonces todas las compras fallarían). Las consultas usan otro, sin dueña (no la necesitan).
/// </summary>
public sealed class WindowsStoreBackend : IStoreBackend
{
    private static readonly string[] DurableKinds = { "Durable" };

    private readonly Func<IntPtr> _owner;
    private readonly object _gate = new();
    private StoreContext? _context;
    private bool _disposed;

    /// <param name="owner">Ventana dueña de la ventana de compra: se pide en cada compra, al empezar (en el hilo de UI).</param>
    public WindowsStoreBackend(Func<IntPtr> owner) => _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public event Action? LicensesChanged;

    private StoreContext Context()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_context is not null) return _context;
            var ctx = StoreContext.GetDefault();
            ctx.OfflineLicensesChanged += OnOfflineLicensesChanged;
            return _context = ctx;
        }
    }

    private void OnOfflineLicensesChanged(StoreContext sender, object args) => LicensesChanged?.Invoke();

    public async Task<StoreLicenseInfo> GetLicenseAsync()
    {
        var license = await Context().GetAppLicenseAsync();
        bool owns = false;
        foreach (var addOn in license.AddOnLicenses.Values)
            if (addOn.IsActive && ProCatalog.IsProAddOn(addOn.InAppOfferToken)) owns = true;
        return new StoreLicenseInfo(license.IsActive, owns);
    }

    public async Task<PurchaseResult> PurchaseAsync()
    {
        // La dueña se pide ya (aún en el hilo de UI) y se comprueba que siga viva; el contexto de la compra es nuevo.
        var hwnd = _owner();
        if (hwnd != IntPtr.Zero && !IsWindow(hwnd)) hwnd = IntPtr.Zero;
        if (hwnd == IntPtr.Zero) AppLog.Warn("pro: compra sin ventana dueña");
        var product = await FindProAsync().WaitAsync(StoreProLicense.QueryTimeout);
        if (product is null)
        {
            AppLog.Warn("pro: el complemento «Pro» no está entre los productos de la app en la Store");
            return PurchaseResult.NotAvailable;
        }
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
        var purchase = StoreContext.GetDefault();
        try { if (hwnd != IntPtr.Zero) WinRT.Interop.InitializeWithWindow.Initialize(purchase, hwnd); }
        catch (Exception ex) { AppLog.Warn($"pro: no se pudo dar la ventana dueña a la compra: {ex.Message}"); }
        var result = await purchase.RequestPurchaseAsync(product.StoreId);
        if (result.ExtendedError is { } error) AppLog.Warn($"pro: la compra devolvió 0x{error.HResult:X8}: {error.Message}");
        return Map(result.Status);
    }

    public async Task<string?> GetPriceAsync() => (await FindProAsync())?.Price?.FormattedPrice;

    /// <summary>El complemento «Pro» entre los duraderos de la app; null si no está. Lanza si la Store no responde.</summary>
    private async Task<StoreProduct?> FindProAsync()
    {
        var query = await Context().GetAssociatedStoreProductsAsync(DurableKinds);
        if (query.ExtendedError is { } error) throw new IOException($"la Store no respondió (0x{error.HResult:X8})", error);
        foreach (var product in query.Products.Values)
            if (ProCatalog.IsProAddOn(product.InAppOfferToken)) return product;
        return null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    /// <summary>De la respuesta de la Store a <see cref="PurchaseResult"/>. PURO.</summary>
    internal static PurchaseResult Map(StorePurchaseStatus status) => status switch
    {
        StorePurchaseStatus.Succeeded => PurchaseResult.Purchased,
        StorePurchaseStatus.AlreadyPurchased => PurchaseResult.AlreadyOwned,
        StorePurchaseStatus.NotPurchased => PurchaseResult.Cancelled,
        StorePurchaseStatus.NetworkError => PurchaseResult.NetworkError,
        _ => PurchaseResult.Failed,
    };

    public void Dispose()
    {
        StoreContext? ctx;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ctx = _context;
            _context = null;
        }
        if (ctx is not null) ctx.OfflineLicensesChanged -= OnOfflineLicensesChanged;
    }
}
