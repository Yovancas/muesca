using System.Globalization;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Una marca de eje: dónde cae (fracción 0..1: desde abajo en Y, desde la izquierda en X) y su texto ya formateado.</summary>
public readonly record struct UsageTick(float Fraction, string Label);

/// <summary>Una línea del aviso de cuota en la gráfica del % (los umbrales de <c>NotchQuotaThresholds</c>).</summary>
public readonly record struct UsageThreshold(float Fraction, string Label, bool Critical);

/// <summary>Una fila del tooltip: la serie (su color), su nombre y su valor ya formateados.</summary>
public readonly record struct UsageTipRow(int Color, string Label, string Value);

/// <summary>Tooltip de una casilla: la hora (o el día), el total (solo en el gasto) y el desglose por serie.</summary>
public sealed record UsageTip(string Title, string? Total, IReadOnlyList<UsageTipRow> Rows);

/// <summary>
/// Una serie de la gráfica: clave estable (familia de modelo, «5h» / «7d»), nombre y valor de la leyenda ya formateados
/// (gasto del rango o % actual), color (slot de <c>Theme.ChartSeries</c>) y si se ve. <see cref="Values"/>: el dato de
/// cada casilla (dólares o %; NaN = hueco).
/// </summary>
public sealed record UsageSeries(string Key, string Label, string Value, int Color, double[] Values, bool Visible);

/// <summary>La proyección del ritmo ya lista para pintar: la recta (x 0..1, y 0..1), las marcas y el texto de la leyenda.</summary>
public sealed record UsageProjectionView(float FromX, float FromY, float ToX, float ToY, float? EtaX, float? ResetX,
    int Color, string Caption, bool Exhausts);

/// <summary>
/// Forma de la gráfica en coordenadas normalizadas, remuestreada en <see cref="Samples"/> puntos con la curva monótona: la
/// MISMA para pintarla quieta y para el morphing entre dos rangos. <see cref="Stacked"/> (gasto): cada serie es una franja
/// entre <see cref="Lower"/> y <see cref="Upper"/>; si no (cuota), una línea en <see cref="Upper"/> con su área hasta abajo
/// (NaN = hueco). Las muestras van de la primera casilla (<see cref="FirstX"/> × <see cref="PastFraction"/>) a la última
/// (<see cref="PastFraction"/>), equiespaciadas (<see cref="X"/>). <see cref="Alpha"/>: opacidad de cada serie (las líneas
/// que aparecen o se van en un morphing se funden).
/// </summary>
public sealed class UsageShape
{
    /// <summary>Puntos por curva: ~4 px entre puntos en la gráfica de 334 px lógicos a 1x; con antialias se ve curva.</summary>
    public const int Samples = 80;

    /// <param name="firstX">Dónde cae la primera casilla, en fracción del pasado: 0 si las casillas se marcan por su inicio
    /// (el gasto); 1/n si cada una vale lo de su FINAL (la cuota: la última muestra de la casilla).</param>
    public UsageShape(bool stacked, float pastFraction, IReadOnlyList<string> keys, IReadOnlyList<int> colors, float firstX = 0f)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(colors);
        Stacked = stacked;
        PastFraction = pastFraction;
        FirstX = firstX;
        Keys = keys.ToArray();
        Colors = colors.ToArray();
        Lower = new float[Keys.Length][];
        Upper = new float[Keys.Length][];
        Alpha = new float[Keys.Length];
        for (int k = 0; k < Keys.Length; k++)
        {
            Lower[k] = new float[Samples];
            Upper[k] = new float[Samples];
            Alpha[k] = 1f;
        }
    }

    public bool Stacked { get; }
    public float PastFraction { get; set; }

    /// <summary>x de la primera casilla en fracción del pasado (0..1); ver el constructor.</summary>
    public float FirstX { get; set; }

    public string[] Keys { get; }
    public int[] Colors { get; }
    public float[][] Lower { get; }
    public float[][] Upper { get; }
    public float[] Alpha { get; }

    /// <summary>x (0..1) de la muestra <paramref name="m"/>.</summary>
    public float X(int m) => (FirstX + (float)m / (Samples - 1) * (1f - FirstX)) * PastFraction;

    public int IndexOf(string key) => Array.IndexOf(Keys, key);
}

/// <summary>
/// Todo lo que pinta la gráfica de la página Uso para un modo y un rango, YA calculado y formateado (la ventana lo rehace al
/// llegar datos, al cambiar de rango, de idioma o al ocultar una serie; nunca por frame): series, ejes «bonitos», tooltips de
/// cada casilla, umbrales, proyección y la forma suave. <see cref="Tops"/>[serie][casilla]: dónde cae (0..1) el punto
/// resaltado del hover de cada serie (NaN si no se ve). <see cref="Empty"/>: sin datos en todo el rango (estado vacío, sin
/// ejes); <see cref="Loading"/>: aún no han llegado.
/// </summary>
public sealed record UsageChartModel(UsageChartMode Mode, ChartRange Range, int Count, float PastFraction,
    IReadOnlyList<UsageSeries> Series, IReadOnlyList<UsageTick> YTicks, IReadOnlyList<UsageTick> XTicks,
    IReadOnlyList<UsageTip> Tips, float[][] Tops, string? Corner, IReadOnlyList<UsageThreshold> Thresholds,
    UsageProjectionView? Projection, UsageShape Shape, bool Empty, bool Loading)
{
    /// <summary>¿Se ve la serie <paramref name="k"/>?</summary>
    public bool Visible(int k) => k >= 0 && k < Series.Count && Series[k].Visible;
}

/// <summary>Lo que necesita la gráfica del % además de las muestras: la cuota de ahora y el ritmo de cada ventana.</summary>
public sealed record UsageQuotaInputs(UsageWindow? Five, UsageWindow? Seven, PaceResult? PaceFive, PaceResult? PaceSeven,
    IReadOnlyList<int> Thresholds);

/// <summary>
/// Construye <see cref="UsageChartModel"/>. PURO: datos, idioma, instante y series ocultas por parámetro; ni E/S ni reloj.
/// </summary>
public static class UsageChartBuilder
{
    /// <summary>Claves de las dos series de la gráfica del %.</summary>
    public const string FiveKey = "5h", SevenKey = "7d";

    /// <summary>Slots de color de las dos líneas de la cuota (cielo para 5 h, violeta para 7 d).</summary>
    public const int FiveColor = 1, SevenColor = 0;

    /// <summary>Marcas del eje X (las de los extremos se anclan al borde al pintarlas).</summary>
    public const int XTickCount = 4;

    /// <summary>Filas del tooltip como mucho (el resto no cabría sobre la gráfica).</summary>
    public const int MaxTipRows = 4;

    /// <summary>Gráfica sin datos todavía (pedidos a la bandeja, sin respuesta).</summary>
    public static UsageChartModel Loading(UsageChartMode mode, ChartRange range) => EmptyModel(mode, range, loading: true);

    /// <summary>
    /// Gasto equivalente por modelo: áreas apiladas, la familia de más gasto abajo (el orden del panel), cada una con su slot
    /// estable de color (<paramref name="slots"/>). Las ocultas (<paramref name="hidden"/>) no suman al eje ni a los totales,
    /// pero siguen en la leyenda, atenuadas.
    /// </summary>
    public static UsageChartModel Spend(IReadOnlyList<HistoryBucket> buckets, ChartRange range,
        IReadOnlyDictionary<string, int> slots, IReadOnlySet<string> hidden, Strings s)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(s);
        int n = buckets.Count;
        var families = Families(buckets);
        if (n == 0 || families.Count == 0) return EmptyModel(UsageChartMode.Spend, range, loading: false);
        // Lo oculto vale para las familias de ESTE rango (se ocultó mirando otro); si las ocultaría todas, ninguna (como la
        // leyenda, que nunca deja la gráfica en blanco).
        var hide = families.Where(hidden.Contains).ToHashSet(StringComparer.Ordinal);
        if (hide.Count == families.Count) hide.Clear();

        var ci = s.Culture;
        var series = new List<UsageSeries>(families.Count);
        foreach (var f in families)
        {
            var values = new double[n];
            double total = 0;
            for (int i = 0; i < n; i++) total += values[i] = buckets[i].Cost(f);
            series.Add(new UsageSeries(f, FamilyLabel(f, s), UsageFormat.Money(total, ci), slots.GetValueOrDefault(f),
                values, !hide.Contains(f)));
        }

        double max = 0, sum = 0;
        var stack = new double[n];
        for (int i = 0; i < n; i++)
        {
            foreach (var se in series) if (se.Visible) stack[i] += se.Values[i];
            max = Math.Max(max, stack[i]);
            sum += stack[i];
        }
        var axis = NiceScale.For(max);

        var tops = new float[series.Count][];
        var cum = new double[n];
        for (int k = 0; k < series.Count; k++)
        {
            tops[k] = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (series[k].Visible) cum[i] += series[k].Values[i];
                tops[k][i] = series[k].Visible ? (float)(cum[i] / axis.Max) : float.NaN;
            }
        }

        var shape = new UsageShape(true, 1f, series.Select(x => x.Key).ToList(), series.Select(x => x.Color).ToList());
        FillStacked(shape, series, axis.Max);

        var tips = new UsageTip[n];
        for (int i = 0; i < n; i++)
        {
            var rows = series.Where(x => x.Visible && x.Values[i] > 0).OrderByDescending(x => x.Values[i]).Take(MaxTipRows)
                .Select(x => new UsageTipRow(x.Color, x.Label, UsageFormat.Money(x.Values[i], ci))).ToArray();
            tips[i] = new UsageTip(SpendTipTitle(buckets[i].StartLocal, range, ci), UsageFormat.Money(stack[i], ci), rows);
        }

        // Las casillas van de la primera a la última por su inicio (x = i / (n − 1)): las horas «redondas» entre ellas.
        var xticks = n == 1 ? new[] { new UsageTick(0.5f, TimeTick(buckets[0].StartLocal, range, ci)) }
            : TimeTicks(buckets[0].StartLocal, buckets[n - 1].StartLocal, range, ci);

        return new UsageChartModel(UsageChartMode.Spend, range, n, 1f, series, MoneyTicks(axis, ci), xticks, tips, tops,
            $"{s.ChartTotal} {UsageFormat.Money(sum, ci)}", Array.Empty<UsageThreshold>(), null, shape,
            Empty: families.All(f => buckets.All(b => b.Cost(f) <= 0)), Loading: false);
    }

    /// <summary>
    /// % real de la cuota (5 h y 7 d) desde SQLite, en casillas (<see cref="UsageBuckets.Percent"/>) hasta ahora y, si hay
    /// proyección (<paramref name="projection"/>), con sitio delante hasta el reinicio de la ventana destacada
    /// (<paramref name="focus"/>; sin destacar, la de 5 h). Eje fijo de 0 a 100 % con los umbrales de los avisos.
    /// </summary>
    public static UsageChartModel Quota(IReadOnlyList<PctPoint> points, ChartRange range, DateTime nowUtc, QuotaFocus focus,
        UsageQuotaInputs q, IReadOnlySet<string> hidden, bool projection, Strings s)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(s);
        var ci = s.Culture;
        var now = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        var span = UsageRanges.Span(range);
        bool seven = focus == QuotaFocus.Seven;
        var focusWindow = seven ? q.Seven : q.Five;
        var (start, end, past) = projection
            ? UsageProjection.Domain(span, now, focusWindow?.ResetsAt)
            : (now - span, now, 1.0);

        int n = UsageRanges.PercentBuckets(range);
        var maxGap = UsageBuckets.MaxGap(span, n);
        var five = UsageBuckets.Percent(points, p => p.FivePct, nowUtc - span, nowUtc, n, maxGap);
        var sev = UsageBuckets.Percent(points, p => p.SevenPct, nowUtc - span, nowUtc, n, maxGap);
        bool any = UsageBuckets.Any(five) || UsageBuckets.Any(sev);
        if (!any) return EmptyModel(UsageChartMode.Quota, range, loading: false);

        var series = new[]
        {
            new UsageSeries(FiveKey, "5h", Last(five, q.Five, ci), FiveColor, five, !hidden.Contains(FiveKey)),
            new UsageSeries(SevenKey, "7d", Last(sev, q.Seven, ci), SevenColor, sev, !hidden.Contains(SevenKey)),
        };

        var tops = new float[series.Length][];
        for (int k = 0; k < series.Length; k++)
        {
            tops[k] = new float[n];
            for (int i = 0; i < n; i++)
                tops[k][i] = series[k].Visible && !double.IsNaN(series[k].Values[i])
                    ? (float)(Math.Clamp(series[k].Values[i], 0, 100) / 100) : float.NaN;
        }

        var visible = series.Where(x => x.Visible).ToList();
        // Cada casilla vale el % de su FINAL (la última muestra): ahí va, la i en (i + 1) / n del pasado, como dice su tooltip.
        var shape = new UsageShape(false, (float)past, visible.Select(x => x.Key).ToList(), visible.Select(x => x.Color).ToList(),
            firstX: 1f / n);
        FillLines(shape, visible);

        var tips = new UsageTip[n];
        var bucket = span / n;
        for (int i = 0; i < n; i++)
        {
            var rows = series.Where(x => x.Visible && !double.IsNaN(x.Values[i]))
                .Select(x => new UsageTipRow(x.Color, x.Label, UsageFormat.Percent(Math.Round(x.Values[i]), ci))).ToArray();
            var at = (nowUtc - span + bucket * (i + 1)).ToLocalTime();
            tips[i] = new UsageTip(QuotaTipTitle(at, range, ci), null, rows);
        }

        var xticks = TimeTicks(start.LocalDateTime, end.LocalDateTime, range, ci);

        var thresholds = new List<UsageThreshold>();
        var th = q.Thresholds.Where(t => t is > 0 and < 100).Distinct().Order().ToList();
        for (int i = 0; i < th.Count; i++)
            thresholds.Add(new UsageThreshold(th[i] / 100f, UsageFormat.Percent(th[i], ci), i == th.Count - 1 && th.Count > 1));

        UsageProjectionView? proj = null;
        int fk = seven ? 1 : 0;
        if (projection && series[fk].Visible && focusWindow is { } w)
        {
            var pace = seven ? q.PaceSeven : q.PaceFive;
            if (UsageProjection.Compute(w.UtilizationPct, pace?.EtaUtc, w.ResetsAt, now, start, end) is { } p)
            {
                string caption = p.Exhausts && pace?.EtaUtc is { } eta
                    ? string.Format(ci, UsageText.IsToday(eta, now) ? s.NotchUsageRunsOutFmt : s.NotchUsageRunsOutDayFmt,
                        UsageText.EtaClock(eta, now, ci))
                    : p.PctAtReset is { } atReset
                        ? string.Format(ci, s.NotchUsageAtResetFmt, UsageFormat.Percent(Math.Round(atReset), ci))
                        : "";
                proj = new UsageProjectionView((float)p.FromX, (float)(p.FromPct / 100), (float)p.ToX, (float)(p.ToPct / 100),
                    (float?)p.EtaX, (float?)p.ResetX, series[fk].Color, caption, p.Exhausts);
            }
        }

        // Eje fijo de 0 a 100 %: la marca del 50 % (el techo es el 100 % y los umbrales llevan la suya, cerca de él).
        var yticks = new[] { new UsageTick(0.5f, UsageFormat.Percent(50, ci)) };
        return new UsageChartModel(UsageChartMode.Quota, range, n, (float)past, series, yticks, xticks, tips, tops, null,
            thresholds, proj, shape, Empty: false, Loading: false);
    }

    /// <summary>Familias con gasto (&gt; 0) en el rango, por gasto descendente y, a igualdad, por nombre (determinista).</summary>
    public static List<string> Families(IReadOnlyList<HistoryBucket> data)
    {
        var totals = new Dictionary<string, double>();
        foreach (var b in data)
            foreach (var kv in b.CostByFamily)
                totals[kv.Key] = totals.GetValueOrDefault(kv.Key) + kv.Value;
        return totals.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key).ToList();
    }

    /// <summary>Nombre de la familia en la UI: la canónica «Otros» se traduce; el resto va tal cual.</summary>
    public static string FamilyLabel(string family, Strings s) => family == ModelFamily.Other ? s.ModelFamilyOther : family;

    /// <summary>Marcas del eje Y en dólares, con los decimales que pide el paso («$0,25», «$2»).</summary>
    public static IReadOnlyList<UsageTick> MoneyTicks(NiceAxis axis, CultureInfo ci)
    {
        int d = NiceScale.Decimals(axis.Step);
        var list = new List<UsageTick>(axis.Ticks);
        for (int t = 1; t <= axis.Ticks; t++)
        {
            double v = axis.Step * t;
            list.Add(new UsageTick((float)(v / axis.Max), "$" + v.ToString("F" + d, ci)));
        }
        return list;
    }

    /// <summary>Pasos «redondos» del eje de tiempo, de 5 min a 2 semanas.</summary>
    private static readonly TimeSpan[] TimeSteps =
    {
        TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromDays(1), TimeSpan.FromDays(2), TimeSpan.FromDays(3), TimeSpan.FromDays(7), TimeSpan.FromDays(14),
    };

    /// <summary>
    /// Marcas del eje de tiempo «bonitas» entre <paramref name="startLocal"/> (x = 0) y <paramref name="endLocal"/> (x = 1): el
    /// paso redondo más fino con el que caben como mucho <paramref name="max"/> (15 min, 2 h, 6 h, 1 día, 1 semana…), en sus
    /// múltiplos de la hora local (los de días, a medianoche). La etiqueta: la hora con pasos de menos de un día; si no, el día
    /// de la semana en 7D y día y mes en el resto. Las fracciones van en tiempo REAL (en UTC, como las casillas): la noche
    /// del cambio de hora el reloj local se salta o repite una hora. <paramref name="tz"/>: la zona de las horas locales
    /// (la del sistema por defecto).
    /// </summary>
    public static IReadOnlyList<UsageTick> TimeTicks(DateTime startLocal, DateTime endLocal, ChartRange range, CultureInfo ci,
        int max = XTickCount, TimeZoneInfo? tz = null)
    {
        tz ??= TimeZoneInfo.Local;
        DateTime? Utc(DateTime local)
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            return tz.IsInvalidTime(local) ? null : TimeZoneInfo.ConvertTimeToUtc(local, tz);
        }
        if (Utc(startLocal) is not { } startUtc || Utc(endLocal) is not { } endUtc) return Array.Empty<UsageTick>();
        var span = endUtc - startUtc;
        if (span <= TimeSpan.Zero || max <= 0) return Array.Empty<UsageTick>();
        DateTime First(TimeSpan st)
        {
            if (st >= TimeSpan.FromDays(1))
                return startLocal.TimeOfDay == TimeSpan.Zero ? startLocal.Date : startLocal.Date.AddDays(1);
            long into = (startLocal - startLocal.Date).Ticks;
            return startLocal.Date + TimeSpan.FromTicks((into + st.Ticks - 1) / st.Ticks * st.Ticks);
        }
        // El paso más fino con el que las marcas alineadas que caen en el tramo son como mucho max (un tramo de 24 h justas
        // desde las 17:20: 18:00, 00:00, 06:00 y 12:00, cada 6 h; contar «tramo / paso» lo dejaba en cada 12 h).
        var step = TimeSteps.FirstOrDefault(st => (span - (Utc(First(st)) - startUtc ?? TimeSpan.Zero)).Ticks / st.Ticks + 1 <= max,
            TimeSteps[^1]);
        var first = First(step);
        string fmt = step < TimeSpan.FromDays(1) ? "HH:mm" : range == ChartRange.Week1 ? "ddd" : "d MMM";
        var list = new List<UsageTick>(max);
        for (var t = first; t <= endLocal && list.Count < max; t += step)
        {
            if (Utc(t) is not { } at) continue;   // la hora que el cambio de hora se salta
            float f = (float)((at - startUtc).TotalMilliseconds / span.TotalMilliseconds);
            if (f is >= 0f and <= 1f) list.Add(new UsageTick(f, t.ToString(fmt, ci)));
        }
        return list;
    }

    /// <summary>Etiqueta del eje X: la hora en los rangos cortos, el día de la semana en 7D y día y mes en 30D.</summary>
    public static string TimeTick(DateTime local, ChartRange range, CultureInfo ci) => range switch
    {
        ChartRange.Week1 => local.ToString("ddd", ci),
        ChartRange.Month1 => local.ToString("d MMM", ci),
        _ => local.ToString("HH:mm", ci),
    };

    /// <summary>Título del tooltip del gasto: el tramo de la casilla («14:00–15:00») o el día («jue 29 oct»).</summary>
    public static string SpendTipTitle(DateTime startLocal, ChartRange range, CultureInfo ci)
    {
        if (range is ChartRange.Week1 or ChartRange.Month1) return startLocal.ToString("ddd d MMM", ci);
        var step = range switch
        {
            ChartRange.Hour1 => TimeSpan.FromMinutes(5),
            ChartRange.Hours5 => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromHours(1),
        };
        return $"{startLocal.ToString("HH:mm", ci)}–{(startLocal + step).ToString("HH:mm", ci)}";
    }

    /// <summary>Título del tooltip del %: la hora de la muestra (con el día en 7D y 30D).</summary>
    public static string QuotaTipTitle(DateTime local, ChartRange range, CultureInfo ci) =>
        range is ChartRange.Week1 or ChartRange.Month1 ? local.ToString("ddd d, HH:mm", ci) : local.ToString("HH:mm", ci);

    // ---------------------------------------------------------------- forma

    private static void FillStacked(UsageShape shape, IReadOnlyList<UsageSeries> series, double max)
    {
        int n = series.Count == 0 ? 0 : series[0].Values.Length;
        var scratch = new double[Math.Max(1, n)];
        var sample = new float[UsageShape.Samples];
        var baseLine = new float[UsageShape.Samples];
        for (int k = 0; k < series.Count; k++)
        {
            var lower = shape.Lower[k];
            var upper = shape.Upper[k];
            baseLine.CopyTo(lower, 0);
            if (!series[k].Visible || n == 0)
            {
                baseLine.CopyTo(upper, 0);
                continue;
            }
            UsageCurves.Resample(series[k].Values, scratch, sample);
            for (int m = 0; m < sample.Length; m++)
            {
                upper[m] = lower[m] + (float)(Math.Max(0, sample[m]) / max);
                baseLine[m] = upper[m];
            }
        }
    }

    private static void FillLines(UsageShape shape, IReadOnlyList<UsageSeries> visible)
    {
        int n = visible.Count == 0 ? 0 : visible[0].Values.Length;
        var scratch = new double[Math.Max(1, n)];
        var sample = new float[UsageShape.Samples];
        for (int k = 0; k < visible.Count; k++)
        {
            UsageCurves.Resample(visible[k].Values, scratch, sample);
            for (int m = 0; m < sample.Length; m++)
                shape.Upper[k][m] = float.IsNaN(sample[m]) ? float.NaN : Math.Clamp(sample[m], 0f, 100f) / 100f;
            Array.Clear(shape.Lower[k]);
        }
    }

    private static string Last(double[] values, UsageWindow? live, CultureInfo ci)
    {
        if (live is { } w) return UsageFormat.Percent(Math.Round(w.UtilizationPct), ci);
        for (int i = values.Length - 1; i >= 0; i--)
            if (!double.IsNaN(values[i])) return UsageFormat.Percent(Math.Round(values[i]), ci);
        return "—";
    }

    private static UsageChartModel EmptyModel(UsageChartMode mode, ChartRange range, bool loading) =>
        new(mode, range, 0, 1f, Array.Empty<UsageSeries>(), Array.Empty<UsageTick>(), Array.Empty<UsageTick>(),
            Array.Empty<UsageTip>(), Array.Empty<float[]>(), null, Array.Empty<UsageThreshold>(), null,
            new UsageShape(mode == UsageChartMode.Spend, 1f, Array.Empty<string>(), Array.Empty<int>()), Empty: !loading,
            Loading: loading);
}
