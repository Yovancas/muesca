namespace ClaudeBarWin.Services.Licensing;

/// <summary>
/// ¿Está desbloqueada esta función con este estado? PURO y el ÚNICO sitio que lo decide: el reparto de la app (opciones del
/// notch, pipe de permisos, monitores) pregunta aquí, sin <c>if (pro)</c> sueltos. La prueba de 7 días desbloquea todo, como
/// el Pro. Un valor desconocido (de una versión futura, o un cast cualquiera) cuenta como bloqueado: ante la duda, gratis.
/// </summary>
public static class ProGate
{
    /// <summary>Todas las funciones de pago, en el orden del enum.</summary>
    public static IReadOnlyList<ProFeature> All { get; } = Enum.GetValues<ProFeature>();

    public static bool IsUnlocked(ProFeature feature, ProState state) =>
        Enum.IsDefined(feature) && (state is ProState.Trial or ProState.Pro);

    /// <summary>¿Hay alguna función de pago bloqueada? (el candado tiene algo que ofrecer).</summary>
    public static bool AnyLocked(ProState state) => All.Any(f => !IsUnlocked(f, state));
}
