namespace ClaudeBarWin.Services;

/// <summary>
/// Familia de modelo DERIVADA del id de los .jsonl, sin listas de nombres conocidos (T13a): se quita
/// el prefijo "claude-" y se toma el primer token alfabético, capitalizado — "claude-opus-4-8" →
/// "Opus", "claude-fable-5" → "Fable", "claude-3-5-sonnet-20240620" → "Sonnet" (esquema antiguo con
/// la versión delante). Así una familia nueva (Fable, Mythos, lo que venga) aparece sola en
/// agregado/filas/gráfica en vez de computar como "other". Ids raros ("&lt;synthetic&gt;", vacío,
/// sin prefijo claude-) caen a <see cref="Other"/>.
/// </summary>
public static class ModelFamily
{
    /// <summary>Clave CANÓNICA del cubo residual (la UI la localiza vía <c>Strings.ModelFamilyOther</c>).</summary>
    public const string Other = "Otros";

    /// <summary>Máximo de familias en panel/gráfica (3 con nombre + <see cref="Other"/>): cap del crecimiento.</summary>
    public const int MaxFamilies = 4;

    public static string FromId(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return Other;
        var m = model.Trim();
        const string prefix = "claude-";
        if (!m.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Other;

        foreach (var token in m[prefix.Length..].Split('-'))
        {
            if (token.Length == 0 || !IsAlphabetic(token)) continue;
            return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
        }
        return Other;
    }

    private static bool IsAlphabetic(string token)
    {
        foreach (var ch in token)
            if (!char.IsAsciiLetter(ch)) return false;
        return true;
    }

    /// <summary>
    /// Familias que se muestran con nombre propio; el resto se funde en <see cref="Other"/>. Con
    /// ≤ <paramref name="max"/> familias se conservan todas; con más, las (max−1) de mayor gasto —
    /// excluida <see cref="Other"/>, que siempre es el cubo de DESTINO de la fusión — de modo que el
    /// resultado final tiene como mucho <paramref name="max"/> entradas incluida Other. Desempate
    /// por nombre (orden ordinal) para que el cap sea determinista.
    /// </summary>
    public static HashSet<string> Keep(IReadOnlyDictionary<string, double> totals, int max = MaxFamilies)
    {
        if (totals.Count <= max) return totals.Keys.ToHashSet();
        return totals.Where(kv => kv.Key != Other)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(max - 1)
            .Select(kv => kv.Key)
            .ToHashSet();
    }
}
