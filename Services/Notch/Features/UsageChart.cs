namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Gráfica de la página Uso: gasto equivalente por modelo (áreas apiladas) o % real de la cuota (líneas).</summary>
public enum UsageChartMode { Spend, Quota }

/// <summary>Ventana de cuota destacada en la página Uso (clic en un anillo del reposo o en una tarjeta de la página).</summary>
public enum QuotaFocus { None, Five, Seven }

/// <summary>Un eje «bonito»: de 0 a <see cref="Max"/> en pasos de <see cref="Step"/> (1, 2 o 5 × 10ⁿ).</summary>
public readonly record struct NiceAxis(double Max, double Step)
{
    /// <summary>Marcas por encima de 0 (<see cref="Max"/> / <see cref="Step"/>).</summary>
    public int Ticks => Step > 0 ? (int)Math.Round(Max / Step) : 0;
}

/// <summary>
/// Escalas de eje «bonitas» (Heckbert, Graphics Gems): el máximo y el paso caen en 1, 2 o 5 × 10ⁿ, así las marcas dicen
/// «$2 · $4», nunca «$1,37». PURO.
/// </summary>
public static class NiceScale
{
    /// <summary>Aire por encima del dato más alto: la curva no toca el techo de la gráfica.</summary>
    public const double Headroom = 1.08;

    /// <summary>
    /// Eje para datos de 0 a <paramref name="max"/> con unas <paramref name="target"/> marcas. Sin datos (≤ 0, NaN o
    /// infinito), el eje de 0 a 1 en pasos de 0,5: nunca un máximo 0 (dividiría por cero al normalizar).
    /// </summary>
    public static NiceAxis For(double max, int target = 3)
    {
        if (!(max > 0) || double.IsInfinity(max)) return new NiceAxis(1, 0.5);
        target = Math.Max(1, target);
        double range = Nice(max * Headroom, round: false);
        double step = Nice(range / target, round: true);
        double top = Math.Ceiling(max * Headroom / step - 1e-9) * step;
        return new NiceAxis(Math.Max(step, top), step);
    }

    /// <summary>El número «bonito» más cercano a <paramref name="x"/> (redondeando) o el primero ≥ x (sin redondear).</summary>
    public static double Nice(double x, bool round)
    {
        if (!(x > 0) || double.IsInfinity(x)) return 1;
        double exp = Math.Floor(Math.Log10(x));
        double pow = Math.Pow(10, exp);
        double f = x / pow;
        double nf = round
            ? f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10
            : f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
        return nf * pow;
    }

    /// <summary>Decimales que necesita una marca de paso <paramref name="step"/> para no repetirse («$0,25», «$2»).</summary>
    public static int Decimals(double step)
    {
        if (!(step > 0) || double.IsInfinity(step)) return 0;
        int d = 0;
        while (d < 6 && Math.Abs(step * Math.Pow(10, d) - Math.Round(step * Math.Pow(10, d))) > 1e-6) d++;
        return d;
    }
}

/// <summary>
/// Curvas suaves que no inventan picos: interpolación cúbica monótona (Fritsch–Carlson) sobre puntos equiespaciados. Pasa
/// por cada dato y, entre dos, nunca sale de su rango: una franja de gasto ≥ 0 no se hunde bajo cero ni un 42 % se pasa a
/// 44 % entre dos muestras. Los huecos (NaN) cortan la curva. PURO y sin reservas: trabaja sobre los spans que se le dan.
/// </summary>
public static class UsageCurves
{
    /// <summary>Tangentes de cada punto (<paramref name="m"/>, mismo largo que <paramref name="y"/>).</summary>
    public static void Tangents(ReadOnlySpan<double> y, Span<double> m)
    {
        int n = y.Length;
        if (m.Length < n) throw new ArgumentException("Faltan tangentes.", nameof(m));
        if (n == 0) return;
        if (n == 1) { m[0] = 0; return; }
        for (int k = 0; k < n; k++)
        {
            double left = k > 0 ? y[k] - y[k - 1] : double.NaN;
            double right = k < n - 1 ? y[k + 1] - y[k] : double.NaN;
            bool l = !double.IsNaN(left), r = !double.IsNaN(right);
            m[k] = l && r ? (left * right <= 0 ? 0 : (left + right) / 2) : l ? left : r ? right : 0;
        }
        // Restricción de Fritsch–Carlson: tangentes acotadas para que cada tramo sea monótono.
        for (int k = 0; k < n - 1; k++)
        {
            double d = y[k + 1] - y[k];
            if (double.IsNaN(d)) continue;
            if (d == 0) { m[k] = 0; m[k + 1] = 0; continue; }
            double a = m[k] / d, b = m[k + 1] / d;
            if (a < 0) { m[k] = 0; a = 0; }
            if (b < 0) { m[k + 1] = 0; b = 0; }
            double s = a * a + b * b;
            if (s > 9)
            {
                double t = 3 / Math.Sqrt(s);
                m[k] = t * a * d;
                m[k + 1] = t * b * d;
            }
        }
    }

    /// <summary>Valor de la curva en <paramref name="u"/> ∈ [0, n−1] (unidades de punto); NaN si cae en un hueco.</summary>
    public static double Eval(ReadOnlySpan<double> y, ReadOnlySpan<double> m, double u)
    {
        int n = y.Length;
        if (n == 0 || double.IsNaN(u)) return double.NaN;
        if (n == 1) return y[0];
        u = Math.Clamp(u, 0, n - 1);
        int k = Math.Min((int)Math.Floor(u), n - 2);
        double t = u - k;
        double y0 = y[k], y1 = y[k + 1];
        if (t <= 1e-9) return y0;
        if (t >= 1 - 1e-9) return y1;
        if (double.IsNaN(y0) || double.IsNaN(y1)) return double.NaN;
        double t2 = t * t, t3 = t2 * t;
        double v = (2 * t3 - 3 * t2 + 1) * y0 + (t3 - 2 * t2 + t) * m[k] + (-2 * t3 + 3 * t2) * y1 + (t3 - t2) * m[k + 1];
        // La restricción ya lo garantiza; esto solo quita el error de coma flotante en los extremos del tramo.
        return Math.Clamp(v, Math.Min(y0, y1), Math.Max(y0, y1));
    }

    /// <summary>
    /// Remuestrea <paramref name="y"/> en <paramref name="output"/>.Length puntos equiespaciados de 0 a n−1 (el primero y
    /// el último caen en el primer y el último dato). <paramref name="scratch"/>: tangentes, al menos del largo de y.
    /// </summary>
    public static void Resample(ReadOnlySpan<double> y, Span<double> scratch, Span<float> output)
    {
        int n = y.Length, count = output.Length;
        if (count == 0) return;
        if (n == 0) { output.Fill(float.NaN); return; }
        Tangents(y, scratch);
        for (int i = 0; i < count; i++)
        {
            double u = count == 1 ? 0 : (double)i * (n - 1) / (count - 1);
            output[i] = (float)Eval(y, scratch[..n], u);
        }
    }
}

/// <summary>Rangos de la página Uso: los cinco del panel (1H … 30D), su tramo visible y sus casillas.</summary>
public static class UsageRanges
{
    /// <summary>Los rangos en el orden de los botones.</summary>
    public static IReadOnlyList<ChartRange> All { get; } =
        new[] { ChartRange.Hour1, ChartRange.Hours5, ChartRange.Day1, ChartRange.Week1, ChartRange.Month1 };

    /// <summary>Etiqueta del botón (igual en todos los idiomas, como en el panel).</summary>
    public static string Label(ChartRange r) => r switch
    {
        ChartRange.Hour1 => "1H",
        ChartRange.Hours5 => "5H",
        ChartRange.Day1 => "24H",
        ChartRange.Week1 => "7D",
        _ => "30D",
    };

    /// <summary>Tramo que enseña la gráfica (hasta ahora).</summary>
    public static TimeSpan Span(ChartRange r) => r switch
    {
        ChartRange.Hour1 => TimeSpan.FromHours(1),
        ChartRange.Hours5 => TimeSpan.FromHours(5),
        ChartRange.Day1 => TimeSpan.FromDays(1),
        ChartRange.Week1 => TimeSpan.FromDays(7),
        _ => TimeSpan.FromDays(30),
    };

    /// <summary>Casillas de la gráfica del % (más finas que las barras de gasto: la línea de la cuota tiene detalle).</summary>
    public static int PercentBuckets(ChartRange r) => r switch
    {
        ChartRange.Hour1 => 60,    // 1 min
        ChartRange.Hours5 => 60,   // 5 min
        ChartRange.Day1 => 96,     // 15 min
        ChartRange.Week1 => 84,    // 2 h
        _ => 90,                   // 8 h
    };

    /// <summary>Rango con el que se abre cada ventana de cuota (su propia duración).</summary>
    public static ChartRange For(QuotaFocus focus) => focus == QuotaFocus.Seven ? ChartRange.Week1 : ChartRange.Hours5;

    /// <summary>Rango de la gráfica de gasto por defecto (el del día, como el panel).</summary>
    public const ChartRange DefaultSpend = ChartRange.Day1;
}

/// <summary>Casillas de la gráfica del % real a partir de las muestras de SQLite. PURO.</summary>
public static class UsageBuckets
{
    /// <summary>Hueco mínimo que se da por «app cerrada» (más corto, un 429 del endpoint de cuota o una suspensión breve).</summary>
    public static readonly TimeSpan MinClosedGap = TimeSpan.FromHours(2);

    /// <summary>
    /// Hueco máximo entre dos muestras CONSECUTIVAS que se une con una recta en <paramref name="count"/> casillas de
    /// <paramref name="span"/>: max(<see cref="MinClosedGap"/>, 2 casillas). Más largo, la app estaba cerrada y se deja el hueco.
    /// </summary>
    public static TimeSpan MaxGap(TimeSpan span, int count) =>
        count <= 0 ? MinClosedGap : TimeSpan.FromTicks(Math.Max(MinClosedGap.Ticks, 2 * (span.Ticks / count)));

    /// <summary>
    /// <paramref name="count"/> casillas iguales de [<paramref name="fromUtc"/>, <paramref name="toUtc"/>]. Cada una vale la
    /// ÚLTIMA muestra que cae dentro (el % que había al acabar la casilla; la media escondería la caída de un reinicio). Las
    /// vacías entre dos casillas con dato se rellenan en línea recta si el hueco REAL (la última muestra de la anterior frente
    /// a la PRIMERA de la siguiente) es ≤ <paramref name="maxGap"/>; las vacías del final siguen con el último valor mientras no
    /// pase <paramref name="maxGap"/> (entre muestra y muestra el % no cambió); el resto, NaN (hueco en la curva). Muestras
    /// fuera del tramo o sin valor no cuentan.
    /// </summary>
    public static double[] Percent(IReadOnlyList<PctPoint> points, Func<PctPoint, double?> select, DateTime fromUtc,
        DateTime toUtc, int count, TimeSpan maxGap)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(select);
        var values = new double[Math.Max(0, count)];
        Array.Fill(values, double.NaN);
        if (count <= 0 || toUtc <= fromUtc) return values;
        var times = new DateTime[count];
        var firsts = new DateTime[count];
        long dt = (toUtc - fromUtc).Ticks;
        foreach (var p in points)
        {
            if (select(p) is not { } v || double.IsNaN(v) || p.TsUtc < fromUtc || p.TsUtc > toUtc) continue;
            int i = Index(p.TsUtc, fromUtc, dt, count);
            bool empty = double.IsNaN(values[i]);
            if (empty || p.TsUtc < firsts[i]) firsts[i] = p.TsUtc;
            if (!empty && p.TsUtc < times[i]) continue;   // vale la última de la casilla
            values[i] = v;
            times[i] = p.TsUtc;
        }

        int prev = -1;
        for (int i = 0; i < count; i++)
        {
            if (double.IsNaN(values[i])) continue;
            if (prev >= 0 && i - prev > 1 && firsts[i] - times[prev] <= maxGap)
                for (int k = prev + 1; k < i; k++)
                    values[k] = values[prev] + (values[i] - values[prev]) * (k - prev) / (i - prev);
            prev = i;
        }
        if (prev >= 0)
        {
            for (int k = prev + 1; k < count; k++)
            {
                var start = fromUtc + TimeSpan.FromTicks(dt * k / count);
                if (start - times[prev] > maxGap) break;
                values[k] = values[prev];
            }
        }
        return values;
    }

    /// <summary>
    /// Lo único que <see cref="Percent"/> usa de <paramref name="points"/> con esas mismas casillas: en cada una, la primera y
    /// la última muestra con 5 h y la primera y la última con 7 d (sin repetir), en orden. <see cref="Percent"/> sobre esto da
    /// lo mismo que sobre todas (30 días de muestras por minuto se quedan en unas 90-180 por serie). PURO.
    /// </summary>
    public static List<PctPoint> EdgesPerBucket(IReadOnlyList<PctPoint> points, DateTime fromUtc, DateTime toUtc, int count)
    {
        ArgumentNullException.ThrowIfNull(points);
        var result = new List<PctPoint>();
        if (count <= 0 || toUtc <= fromUtc) return result;
        var edges = new PctPoint?[count, 4];   // primera 5 h, última 5 h, primera 7 d, última 7 d
        long dt = (toUtc - fromUtc).Ticks;
        foreach (var p in points)
        {
            if (p.TsUtc < fromUtc || p.TsUtc > toUtc) continue;
            int i = Index(p.TsUtc, fromUtc, dt, count);
            if (p.FivePct is { } f && !double.IsNaN(f)) Keep(edges, i, 0, p);
            if (p.SevenPct is { } s && !double.IsNaN(s)) Keep(edges, i, 2, p);
        }
        var bucket = new List<PctPoint>(4);
        for (int i = 0; i < count; i++)
        {
            bucket.Clear();
            for (int j = 0; j < 4; j++)
                if (edges[i, j] is { } e && !bucket.Exists(x => ReferenceEquals(x, e))) bucket.Add(e);
            bucket.Sort((a, b) => a.TsUtc.CompareTo(b.TsUtc));
            result.AddRange(bucket);
        }
        return result;

        static void Keep(PctPoint?[,] edges, int i, int j, PctPoint p)
        {
            if (edges[i, j] is not { } first || p.TsUtc < first.TsUtc) edges[i, j] = p;
            if (edges[i, j + 1] is not { } last || p.TsUtc >= last.TsUtc) edges[i, j + 1] = p;
        }
    }

    /// <summary>Casilla de <paramref name="ts"/> en <paramref name="count"/> casillas iguales de <paramref name="dt"/> ticks
    /// desde <paramref name="fromUtc"/> (lo que cae justo en el final, en la última).</summary>
    private static int Index(DateTime ts, DateTime fromUtc, long dt, int count) =>
        (int)Math.Min(count - 1, (ts - fromUtc).Ticks * count / dt);

    /// <summary>¿Hay algún dato (no NaN) en las casillas?</summary>
    public static bool Any(IReadOnlyList<double> values)
    {
        foreach (var v in values) if (!double.IsNaN(v)) return true;
        return false;
    }
}

/// <summary>La proyección del ritmo en la gráfica del %: desde ahora hasta el reinicio (o el borde), en coordenadas
/// normalizadas (x de 0 a 1 en el tramo de la gráfica; y en %).</summary>
public readonly record struct QuotaProjection(double FromX, double FromPct, double ToX, double ToPct, double? EtaX,
    double? ResetX, double? PctAtReset, bool Exhausts);

/// <summary>
/// Hasta dónde llega la cuota a este ritmo (<see cref="PaceCalculator"/> da la hora a la que se agota). PURO: el instante y el
/// tramo de la gráfica llegan por parámetro.
/// </summary>
public static class UsageProjection
{
    /// <summary>Lo que se reserva a la derecha de «ahora» para la proyección: hasta el reinicio, como mucho la mitad del tramo.</summary>
    public const double MaxFutureShare = 0.5;

    /// <summary>
    /// Tramo de la gráfica del %: <paramref name="span"/> hacia atrás desde <paramref name="now"/> y, delante, hasta el
    /// reinicio de la ventana destacada (como mucho <see cref="MaxFutureShare"/> × span; sin reinicio, nada).
    /// <c>PastFraction</c>: dónde cae «ahora» (0..1).
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End, double PastFraction) Domain(TimeSpan span, DateTimeOffset now,
        DateTimeOffset? reset)
    {
        if (span <= TimeSpan.Zero) span = TimeSpan.FromHours(1);
        var future = reset is { } r && r > now
            ? TimeSpan.FromTicks(Math.Min((r - now).Ticks, (long)(span.Ticks * MaxFutureShare)))
            : TimeSpan.Zero;
        return (now - span, now + future, span.TotalMilliseconds / (span + future).TotalMilliseconds);
    }

    /// <summary>
    /// La recta del ritmo desde (ahora, <paramref name="utilPct"/>) hasta el reinicio o el borde del tramo, lo que llegue
    /// antes, y como mucho hasta 100 % (ahí se para, en la hora en que se agota). Sin hora de agotarse
    /// (<paramref name="eta"/> null: no se está gastando) va plana. null si no hay sitio a la derecha de «ahora».
    /// </summary>
    public static QuotaProjection? Compute(double utilPct, DateTimeOffset? eta, DateTimeOffset? reset, DateTimeOffset now,
        DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= now || end <= start || double.IsNaN(utilPct)) return null;
        double total = (end - start).TotalMilliseconds;
        double X(DateTimeOffset t) => Math.Clamp((t - start).TotalMilliseconds / total, 0, 1);
        double u = Math.Clamp(utilPct, 0, 100);
        double slope = eta is { } e && e > now && u < 100 ? (100 - u) / (e - now).TotalMilliseconds : 0;   // %/ms
        double Pct(DateTimeOffset t) => Math.Min(100, u + slope * Math.Max(0, (t - now).TotalMilliseconds));

        var stop = reset is { } r && r > now && r < end ? r : end;
        if (slope > 0 && eta is { } at && at < stop) stop = at;
        double? etaX = slope > 0 && eta is { } et && et <= end ? X(et) : null;
        double? resetX = reset is { } rs && rs > now && rs <= end ? X(rs) : null;
        double? atReset = reset is { } rr && rr > now ? Pct(rr) : null;
        bool exhausts = slope > 0 && eta is { } ex && reset is { } re && ex < re;
        return new QuotaProjection(X(now), u, X(stop), Pct(stop), etaX, resetX, atReset, exhausts);
    }
}

/// <summary>Qué casilla de la gráfica hay bajo el ratón. PURO: la MISMA cuenta para pintar la línea del hover y acertarla.</summary>
public static class UsageChartHit
{
    /// <summary>Holgura (fracción del ancho) por fuera de la gráfica que aún cuenta como encima.</summary>
    public const float Slack = 0.02f;

    /// <summary>
    /// Casilla (0..<paramref name="count"/>−1) más cercana a <paramref name="px"/> en una gráfica de
    /// <paramref name="left"/> y <paramref name="width"/> px cuyas casillas van de <paramref name="firstX"/> ×
    /// <paramref name="pastFraction"/> a <paramref name="pastFraction"/> del ancho (<see cref="UsageShape.FirstX"/>; el resto
    /// es la proyección: allí manda la última). −1 si no hay casillas o el punto cae fuera.
    /// </summary>
    public static int Bucket(float px, float left, float width, int count, float pastFraction, float firstX = 0f)
    {
        if (count <= 0 || !(width > 0)) return -1;
        float f = (px - left) / width;
        if (f < -Slack || f > 1 + Slack) return -1;
        if (count == 1) return 0;
        float past = Past(pastFraction), first = Math.Clamp(firstX, 0f, 0.99f);
        float u = (Math.Clamp(f, 0f, past) / past - first) / (1f - first) * (count - 1);
        return Math.Clamp((int)MathF.Round(u), 0, count - 1);
    }

    /// <summary>x (fracción del ancho) de la casilla <paramref name="index"/> (fraccionaria: la línea se desliza entre dos), la
    /// MISMA que la de la curva (<see cref="UsageShape.X"/>).</summary>
    public static float X(float index, int count, float pastFraction, float firstX = 0f)
    {
        float past = Past(pastFraction), first = Math.Clamp(firstX, 0f, 0.99f);
        if (count <= 1) return first > 0f ? first * past : past / 2f;
        return (first + Math.Clamp(index, 0f, count - 1) / (count - 1) * (1f - first)) * past;
    }

    private static float Past(float pastFraction) => pastFraction is > 0 and <= 1 ? pastFraction : 1f;
}
