namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Lo que la página Uso recuerda entre aperturas (en <c>config.json</c>, <c>NotchUsageView</c>): el modo (Gasto | Cuota), la
/// ventana destacada (5 h / 7 d) y el rango elegido con el Pro. Las series ocultas NO: cada apertura empieza con todas
/// visibles. Los enums van como número; cualquier valor raro (a mano, de otra versión) vuelve a lo de serie al normalizar.
/// </summary>
public sealed record UsageViewState(UsageChartMode Mode, QuotaFocus Focus, ChartRange Range)
{
    /// <summary>Lo de serie: Gasto, sin ventana destacada y el rango del gasto (24H).</summary>
    public static UsageViewState Default { get; } = new(UsageChartMode.Spend, QuotaFocus.None, UsageRanges.DefaultSpend);

    /// <summary>El mismo estado con cada valor desconocido cambiado por el de serie.</summary>
    public UsageViewState Normalize() => new(
        Enum.IsDefined(Mode) ? Mode : Default.Mode,
        Enum.IsDefined(Focus) ? Focus : Default.Focus,
        UsageRanges.All.Contains(Range) ? Range : Default.Range);

    /// <summary>El modo que se enseña: el elegido salvo con «Mostrar gasto estimado» apagado, que es siempre Cuota (sin ni una
    /// cifra de gasto). Lo elegido no se pierde: al encender el ajuste vuelve.</summary>
    public static UsageChartMode ShownMode(UsageChartMode chosen, bool showSpend) => showSpend ? chosen : UsageChartMode.Quota;
}