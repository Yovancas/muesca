namespace ClaudeBarWin.Services.Licensing;

/// <summary>
/// La licencia del Pro, sin interfaz propia: el notch, el menú de la bandeja y el asistente leen el estado y piden las
/// acciones. Implementaciones: <see cref="StoreProLicense"/> (edición de la Store: complemento duradero y prueba),
/// <see cref="FreeEditionLicense"/> (GitHub / Velopack / winget: siempre gratis, con el enlace a la Store) y, solo en Debug,
/// la de <c>CLAUDEBAR_PRO</c>. La elige <see cref="ProLicenseFactory"/> al arrancar. Una venta directa con claves sería
/// otra implementación más, sin tocar a quien la usa.
/// </summary>
public interface IProLicense : IDisposable
{
    ProState State { get; }

    /// <summary>Días de prueba que quedan (solo en <see cref="ProState.Trial"/>; null en lo demás).</summary>
    int? TrialDaysLeft { get; }

    /// <summary>Hubo prueba y se acabó sin compra (para el único aviso del notch).</summary>
    bool TrialEnded { get; }

    /// <summary>¿Se puede comprar desde la app? (edición de la Store). Si no, se ofrece <see cref="StoreUrl"/>.</summary>
    bool CanPurchaseHere { get; }

    /// <summary>La ficha de ClaudeBar en Microsoft Store (<c>ms-windows-store://pdp/?productid=…</c>); null si no aplica.</summary>
    string? StoreUrl { get; }

    /// <summary>Precio del Pro tal como lo da la Store, ya con su moneda («9,99 €»); null si no se sabe.</summary>
    string? Price { get; }

    /// <summary>Cambió algo de lo anterior. Siempre en el hilo de UI.</summary>
    event Action? Changed;

    /// <summary>Compra el Pro (ventana de la Store). Si sale bien, <see cref="State"/> pasa a Pro al momento.</summary>
    Task<PurchaseResult> PurchaseAsync();

    /// <summary>«Restaurar compra»: vuelve a preguntar por la licencia y dice si esta cuenta tiene el Pro.</summary>
    Task<PurchaseResult> RestoreAsync();

    /// <summary>Vuelve a preguntar por la licencia en segundo plano. Un fallo (sin red, la Store no responde) no cambia nada:
    /// nunca se quita el Pro por eso.</summary>
    Task RefreshAsync();
}
