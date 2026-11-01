using System.Globalization;
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

public enum ChartRange
{
    Hour1,   // last 1 hour  → 12 × 5 min
    Hours5,  // last 5 hours → 10 × 30 min
    Day1,    // last 24 hours → 24 × 1 h
    Week1,   // last 7 days  → 7 × 1 day
    Month1   // last 30 days → 30 × 1 day
}

/// <summary>
/// Un sub-tramo de la ventana de la gráfica con su gasto por familia DINÁMICA (T13a): las claves
/// son las familias reales presentes en los datos (<see cref="ModelFamily.FromId"/>), no los campos
/// fijos Opus/Sonnet/Haiku/Other de antes. Solo guarda familias con gasto ≠ 0 en el tramo.
/// </summary>
public sealed record HistoryBucket(
    DateTime StartLocal, string Label,
    IReadOnlyDictionary<string, double> CostByFamily)
{
    public double CostUsd => CostByFamily.Values.Sum();
    public double Cost(string family) => CostByFamily.GetValueOrDefault(family);
}

/// <summary>
/// Buckets local transcript usage (cost-equivalent $) into a rolling window ending NOW,
/// subdivided into bars. Each range shows the *last* hour / 5h / 24h / week / month.
/// Source is the JSONL parser — the only thing with history (the API is a snapshot).
/// </summary>
public static class UsageHistory
{
    private static (TimeSpan sub, int count) Spec(ChartRange r) => r switch
    {
        ChartRange.Hour1 => (TimeSpan.FromMinutes(5), 12),
        ChartRange.Hours5 => (TimeSpan.FromMinutes(30), 10),
        ChartRange.Day1 => (TimeSpan.FromHours(1), 24),
        ChartRange.Week1 => (TimeSpan.FromDays(1), 7),
        ChartRange.Month1 => (TimeSpan.FromDays(1), 30),
        _ => (TimeSpan.FromMinutes(30), 10)
    };

    /// <summary>How far back the parser must read to fill the window (+ a margin).</summary>
    public static TimeSpan Lookback(ChartRange r)
    {
        var (sub, count) = Spec(r);
        return sub * count + sub;
    }

    /// <summary>
    /// <paramref name="culture"/>: cultura de FORMATO de las etiquetas del eje X ("ddd"/"dd/MM"), la del
    /// idioma elegido en la UI (T2: con Language=en salían "lun"/"mié" de la CurrentCulture del SO).
    /// </summary>
    public static List<HistoryBucket> Build(IEnumerable<UsageRecord> records, ChartRange range, DateTime nowUtc,
        CultureInfo culture)
    {
        var (sub, count) = Spec(range);
        var perFamily = new Dictionary<string, double[]>();

        foreach (var rec in records)
        {
            var age = nowUtc - rec.TimestampUtc;
            if (age < TimeSpan.Zero) continue;
            int stepsAgo = (int)(age.Ticks / sub.Ticks); // 0 = current (newest) bucket
            int idx = count - 1 - stepsAgo;
            if (idx < 0 || idx >= count) continue;

            // T13a: familia dinámica (claude-fable-5 → "Fable"), la misma que usa el agregado.
            var family = ModelFamily.FromId(rec.Model);
            if (!perFamily.TryGetValue(family, out var arr))
                perFamily[family] = arr = new double[count];
            arr[idx] += Pricing.CostUsd(rec);
        }

        // Cap de familias (mismo criterio que el agregado de gasto): con más de MaxFamilies en la
        // ventana, las menores se funden en "Otros" — series y leyenda no crecen sin límite.
        var totals = perFamily.ToDictionary(kv => kv.Key, kv => kv.Value.Sum());
        var keep = ModelFamily.Keep(totals);
        foreach (var family in perFamily.Keys.Where(f => f != ModelFamily.Other && !keep.Contains(f)).ToList())
        {
            var src = perFamily[family];
            perFamily.Remove(family);
            if (!perFamily.TryGetValue(ModelFamily.Other, out var dst))
                perFamily[ModelFamily.Other] = dst = new double[count];
            for (int i = 0; i < count; i++) dst[i] += src[i];
        }

        var list = new List<HistoryBucket>(count);
        for (int i = 0; i < count; i++)
        {
            var byFamily = new Dictionary<string, double>();
            foreach (var (family, arr) in perFamily)
                if (arr[i] != 0) byFamily[family] = arr[i];
            var startLocal = (nowUtc - sub * (count - i)).ToLocalTime();
            list.Add(new HistoryBucket(startLocal, Label(range, startLocal, culture), byFamily));
        }
        return list;
    }

    private static string Label(ChartRange r, DateTime start, CultureInfo ci) => r switch
    {
        ChartRange.Hour1 => start.ToString("HH:mm", ci),
        ChartRange.Hours5 => start.ToString("HH:mm", ci),
        ChartRange.Day1 => start.ToString("HH'h'", ci),
        ChartRange.Week1 => start.ToString("ddd", ci),
        ChartRange.Month1 => start.ToString("dd/MM", ci),
        _ => start.ToString("dd/MM", ci)
    };
}
