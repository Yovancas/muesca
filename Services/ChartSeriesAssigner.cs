namespace ClaudeBarWin.Services;

/// <summary>
/// Asignador ESTABLE de slot de color para las series dinámicas de la gráfica (T13a). El primer
/// reparto va por rango de gasto (1.º → slot 0, 2.º → slot 1…); en refrescos posteriores cada
/// familia CONSERVA su slot mientras siga presente, aunque el orden de gasto baile — que Opus no
/// cambie de color entre refrescos. Las familias ausentes liberan su slot, que hereda la siguiente
/// nueva (en orden de rango). El slot indexa <c>Theme.ChartSeries</c> (módulo su tamaño).
/// </summary>
public sealed class ChartSeriesAssigner
{
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _slots = new();

    /// <param name="familiesByRank">Familias presentes, ya ordenadas por gasto descendente.</param>
    /// <returns>Mapa familia → slot (copia: estable frente a llamadas posteriores).</returns>
    public IReadOnlyDictionary<string, int> Assign(IReadOnlyList<string> familiesByRank)
    {
        lock (_lock)
        {
            var present = new HashSet<string>(familiesByRank);
            foreach (var gone in _slots.Keys.Where(f => !present.Contains(f)).ToList())
                _slots.Remove(gone);

            foreach (var family in familiesByRank)
            {
                if (_slots.ContainsKey(family)) continue;
                int slot = 0;
                while (_slots.ContainsValue(slot)) slot++;
                _slots[family] = slot;
            }
            return new Dictionary<string, int>(_slots);
        }
    }
}
