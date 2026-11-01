namespace ClaudeBarWin.Services.Notch;

/// <summary>Ventana de cuota vigilada por <see cref="QuotaThresholdTracker"/>.</summary>
public enum QuotaWindowKind { FiveHour, SevenDay }

/// <summary>Una ventana acaba de cruzar <paramref name="Threshold"/> (%) con utilización <paramref name="Pct"/>.</summary>
public sealed record QuotaCrossing(QuotaWindowKind Window, int Threshold, double Pct, DateTimeOffset? ResetsAt);

/// <summary>
/// Decide cuándo avisar de que la cuota cruza un umbral (80 / 95 %) en cada ventana (5 h y 7 d).
/// PURO: recibe cada lectura de <see cref="RealUsage"/> por parámetro y no lee el reloj (regla del repo).
///
/// <list type="bullet">
/// <item><b>Gracia de arranque</b>: la primera lectura de una ventana nunca avisa; los umbrales que
/// ya están superados nacen desarmados (no sabemos cuándo se cruzaron).</item>
/// <item><b>Una vez por cruce</b>: al avisar, el umbral se desarma. Si un salto cruza varios a la vez
/// (70 → 96) solo se avisa del más alto, y los inferiores también quedan desarmados.</item>
/// <item><b>Histéresis</b>: un umbral desarmado se rearma cuando el pct baja de
/// <c>umbral − hysteresisPct</c>; el ruido de la API (81 → 79 → 81) no repite el aviso.</item>
/// <item><b>Reset</b>: si el <c>ResetsAt</c> salta hacia adelante más de <see cref="ResetJumpMinutes"/>
/// minutos, la ventana es nueva y se rearma todo. Saltos menores son jitter de la API.</item>
/// </list>
/// Una ventana <c>null</c> en la lectura se ignora y conserva su estado.
/// </summary>
public sealed class QuotaThresholdTracker
{
    /// <summary>Salto mínimo del <c>ResetsAt</c> (minutos) para tratarlo como ventana nueva. Igual que
    /// <see cref="Motion.ResetDetector.JumpThresholdMinutes"/>: la API mueve el valor unos segundos entre lecturas.</summary>
    public const double ResetJumpMinutes = 10.0;

    private readonly int[] _thresholds;          // ascendentes, sin duplicados
    private readonly double _hysteresis;
    private readonly WindowState _fiveHour;
    private readonly WindowState _sevenDay;

    public QuotaThresholdTracker(IReadOnlyList<int> thresholds, double hysteresisPct = 3)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        if (hysteresisPct < 0) throw new ArgumentOutOfRangeException(nameof(hysteresisPct));
        _thresholds = thresholds.Distinct().Order().ToArray();
        _hysteresis = hysteresisPct;
        _fiveHour = new WindowState(_thresholds.Length);
        _sevenDay = new WindowState(_thresholds.Length);
    }

    /// <summary>Los umbrales (%), ascendentes y sin duplicados.</summary>
    public IReadOnlyList<int> Thresholds => _thresholds;

    /// <summary>
    /// Registra una lectura y devuelve los cruces nuevos (como mucho uno por ventana, 5 h primero).
    /// Lista vacía si no hay nada que avisar.
    /// </summary>
    public IReadOnlyList<QuotaCrossing> Observe(RealUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        List<QuotaCrossing>? result = null;
        if (Step(_fiveHour, QuotaWindowKind.FiveHour, usage.FiveHour) is { } a) (result ??= new()).Add(a);
        if (Step(_sevenDay, QuotaWindowKind.SevenDay, usage.SevenDay) is { } b) (result ??= new()).Add(b);
        return result ?? (IReadOnlyList<QuotaCrossing>)Array.Empty<QuotaCrossing>();
    }

    private QuotaCrossing? Step(WindowState st, QuotaWindowKind kind, UsageWindow? w)
    {
        if (w is null) return null;
        double pct = w.UtilizationPct;

        if (!st.Initialized)
        {
            // Gracia de arranque: armados solo los umbrales que aún no se han alcanzado.
            for (int i = 0; i < _thresholds.Length; i++) st.Armed[i] = pct < _thresholds[i];
            st.Initialized = true;
            st.LastResetsAt = w.ResetsAt;
            return null;
        }

        // Ventana nueva: el ResetsAt avanza de verdad (no jitter ni retroceso) → se rearma todo.
        if (st.LastResetsAt is { } prev && w.ResetsAt is { } next
            && (next - prev).TotalMinutes > ResetJumpMinutes)
            Array.Fill(st.Armed, true);
        if (w.ResetsAt is not null) st.LastResetsAt = w.ResetsAt;

        // Histéresis: rearma los que han bajado lo bastante.
        for (int i = 0; i < _thresholds.Length; i++)
            if (!st.Armed[i] && pct < _thresholds[i] - _hysteresis) st.Armed[i] = true;

        // Cruces: de los armados ya alcanzados se avisa solo del más alto y se desarman todos.
        int highest = -1;
        for (int i = 0; i < _thresholds.Length; i++)
        {
            if (st.Armed[i] && pct >= _thresholds[i])
            {
                st.Armed[i] = false;
                highest = i;
            }
        }
        return highest < 0 ? null : new QuotaCrossing(kind, _thresholds[highest], pct, w.ResetsAt);
    }

    private sealed class WindowState(int count)
    {
        public bool Initialized;
        public DateTimeOffset? LastResetsAt;
        public readonly bool[] Armed = new bool[count];
    }
}
