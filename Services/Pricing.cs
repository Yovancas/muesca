using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Fachada FINA sobre <see cref="PricingCatalog"/> (T13b): las tarifas ya NO viven hardcodeadas aquí
/// — salen de la cadena caché en disco → models.dev (background, opt-out) → snapshot embebido.
/// Sigue siendo una *estimación equivalente* a la lista pública de la API, no lo que cobra una
/// suscripción. <see cref="CostUsd"/> conserva la firma histórica para los call sites que solo
/// quieren el número (gráfica); <see cref="Cost"/> añade <see cref="PricedCost.IsPriced"/> para no
/// mentir con $0 cuando un modelo no tiene tarifa en NINGUNA fuente.
/// </summary>
public static class Pricing
{
    /// <summary>Coste estimado + bandera de tarifa real (false ⇒ la UI pinta "—", el total no suma).</summary>
    public static PricedCost Cost(UsageRecord r) => PricingCatalog.Default.CostFor(r);

    /// <summary>Solo el número (0.0 si no hay tarifa) — firma histórica.</summary>
    public static double CostUsd(UsageRecord r) => Cost(r).Usd;
}
