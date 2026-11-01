namespace ClaudeBarWin.Services.Licensing;

/// <summary>Los datos del Pro en Microsoft Store, en un único sitio.</summary>
public static class ProCatalog
{
    /// <summary>
    /// Id de la ficha de ClaudeBar en Microsoft Store (los 12 caracteres de Partner Center). MARCADOR hasta el alta: se
    /// puede cambiar sin compilar con <c>ProStoreProductId</c> en <c>config.json</c>.
    /// </summary>
    public const string StoreProductId = "CLAUDEBARPRO";

    /// <summary>«Product ID» del complemento duradero en Partner Center (llega como <c>InAppOfferToken</c>).</summary>
    public const string AddOnToken = "Pro";

    /// <summary>La ficha de la Store para <paramref name="productId"/> (vacío o null = <see cref="StoreProductId"/>).</summary>
    public static string StoreUrl(string? productId)
    {
        var id = string.IsNullOrWhiteSpace(productId) ? StoreProductId : productId.Trim();
        return "ms-windows-store://pdp/?productid=" + Uri.EscapeDataString(id);
    }

    /// <summary>¿Es este complemento el Pro? (por su <c>InAppOfferToken</c>, sin distinguir mayúsculas).</summary>
    public static bool IsProAddOn(string? inAppOfferToken) =>
        string.Equals(inAppOfferToken?.Trim(), AddOnToken, StringComparison.OrdinalIgnoreCase);
}
