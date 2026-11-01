using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.UI;

/// <summary>
/// Secciones plegables del dashboard, reordenadas por prioridad: Cuota → Sesiones → Gasto → Gráfica.
/// Sin estado: recibe Theme/Strings/Config/fuentes/datos y los diccionarios de rects por parámetro.
/// Cada cabecera de sección es clicable ("▸ Título" plegada / "▾ Título" expandida) y registra su
/// rect en <c>sectionRects</c> con clave "quota"/"sessions"/"spend"/"chart". Cada cuerpo conserva la
/// simetría medir(draw=false)/pintar(draw=true): avanza y devuelve <c>y</c> idéntico en ambas ramas.
/// </summary>
public static class DashboardDataView
{
    // --- Datos estáticos del gráfico (movidos de DashboardForm, intactos) ---
    internal static readonly (ChartRange range, string label)[] Tabs =
    {
        (ChartRange.Hour1, "1H"), (ChartRange.Hours5, "5H"), (ChartRange.Day1, "24H"),
        (ChartRange.Week1, "7D"), (ChartRange.Month1, "30D")
    };

    // T13a: las series del área apilada son DINÁMICAS — las familias reales de los datos
    // (ModelFamily.FromId), no la lista fija Opus/Sonnet/Haiku/other con colores hardcodeados.
    // El color sale de theme.ChartSeries por RANGO de gasto, con asignación estable por sesión:
    // una familia conserva su slot entre refrescos mientras siga presente.
    internal static readonly ChartSeriesAssigner SeriesAssigner = new();

    /// <summary>
    /// Familias con gasto (&gt; 0) en la ventana del gráfico, por gasto descendente — el rango define
    /// el slot de color y el orden de apilado/leyenda. Una familia sin registros en la ventana no
    /// aparece (desaparición natural); desempate por nombre para que el orden sea determinista.
    /// </summary>
    internal static List<string> ChartFamilies(IReadOnlyList<HistoryBucket> data)
    {
        var totals = new Dictionary<string, double>();
        foreach (var b in data)
            foreach (var kv in b.CostByFamily)
                totals[kv.Key] = totals.GetValueOrDefault(kv.Key) + kv.Value;
        return totals.Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>Etiqueta de familia para la UI: la canónica "Otros" se localiza; el resto va tal cual.</summary>
    internal static string FamilyLabel(string family, Strings s)
        => family == ModelFamily.Other ? s.ModelFamilyOther : family;

    /// <summary>
    /// Valor de la fila de gasto de una familia (T13b), PURO y testeable: con tarifa, la moneda con la
    /// cultura del idioma (<see cref="UsageFormat.Money"/>); SIN tarifa en ninguna fuente del catálogo
    /// (<see cref="WindowStats.IsUnpriced"/>), "— &lt;sufijo&gt;" localizado — nunca "$0.00", que
    /// mentiría (esa familia tampoco suma al total).
    /// </summary>
    internal static string SpendValueText(WindowStats spend, string family, Strings s)
        => spend.IsUnpriced(family)
            ? "— " + s.SpendNoRate
            : UsageFormat.Money(spend.CostByModel.GetValueOrDefault(family), s.Culture);

    /// <summary>Color del slot asignado a una familia (módulo: nunca se sale de la gama del tema).</summary>
    private static Color SeriesColor(Theme theme, IReadOnlyDictionary<string, int> slots, string family)
        => theme.ChartSeries[slots[family] % theme.ChartSeries.Count];

    // T11: alto del plot y del pie de la gráfica en px de diseño (96 DPI) proyectados al DPI vigente
    // (las etiquetas del eje/leyenda crecen con la fuente; el plot debe crecer con ellas).
    internal static int ChartH => Dpi.Scale(92);
    internal static int ChartFooter => Dpi.Scale(32);

    /// <summary>
    /// Número de secciones de datos realmente visibles (cuota siempre; sesiones/gasto/gráfica según
    /// config y datos). Lo usa la cabecera/footer para calcular el índice de stagger del footer
    /// (cabecera=0, datos=1..n ⇒ footer=n+1) sin duplicar las condiciones de visibilidad.
    /// </summary>
    public static int VisibleSectionCount(AppConfig cfg, AppSnapshot? snap)
    {
        int n = 1; // cuota siempre presente
        if (cfg.LiveSessionsEnabled) n++;
        if (cfg.ShowSpendEstimate && snap?.Spend is { } spend && spend.CostByModel.Count > 0) n++;
        if (cfg.ShowChart) n++;
        return n;
    }

    /// <summary>
    /// Dibuja las cuatro secciones plegables en orden de prioridad y devuelve el nuevo y.
    /// Limpia y rellena <c>sectionRects</c>; los rects internos de cada sección (tabs/modos/ventana
    /// %/filas live) se limpian/rellenan según corresponda manteniendo el comportamiento previo.
    /// </summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w,
        AppSnapshot? snap, LiveSessionsView live, AppConfig cfg, Strings s, Theme theme,
        Font labelFont, Font smallFont, Font tabFont,
        string chartMode, ChartRange chartRange, string chartPctWindow,
        List<HistoryBucket> chartData, List<PctPoint> pctData, bool chartLoading,
        Dictionary<string, Rectangle> sectionRects,
        Dictionary<ChartRange, Rectangle> tabRects,
        Dictionary<string, Rectangle> modeRects,
        Dictionary<string, Rectangle> pctWinRects,
        Dictionary<string, Rectangle> liveRowRects,
        MotionState? motion = null, bool reduceMotion = false,
        double tSinceOpenMs = double.PositiveInfinity, int firstSectionIndex = 1, int trayDirection = 1)
    {
        sectionRects.Clear();

        using var fg = GdiCache.BrushFor(theme.TextPrimary);
        using var dim = GdiCache.BrushFor(theme.TextSecondary);

        // Entrada escalonada (Tarea 4): cada sección de datos se traslada OffsetY px → 0 con desfase
        // por índice (cabecera=0, datos=1..n, footer=n+1). El índice corre solo por las secciones
        // realmente dibujadas, arrancando en firstSectionIndex (1, tras la cabecera). El y de layout NO
        // cambia: el transform se aplica/deshace alrededor del draw de cada sección. tSinceOpen=+∞ ⇒
        // todas asentadas (render-test/estado final), igual con reduceMotion.
        int idx = firstSectionIndex;

        // 1) Cuota (barras 5h/7d + pace + modelos)
        y = Section(g, draw, "quota", s.SectionQuota, !cfg.CollapsedQuota, x, y, w, theme, labelFont, sectionRects,
            yy => DrawQuotaBody(g, draw, snap, cfg, s, theme, x, yy, w, labelFont, smallFont, fg, dim, motion, reduceMotion),
            idx++, tSinceOpenMs, reduceMotion, trayDirection);

        // 2) Sesiones en vivo (solo lista de instancias; la mascota la pinta la cabecera, no se duplica aquí)
        if (cfg.LiveSessionsEnabled)
        {
            y = Section(g, draw, "sessions", s.SectionSessions, !cfg.CollapsedSessions, x, y, w, theme, labelFont, sectionRects,
                yy => DrawLiveSessionsBody(g, draw, live, s, x, yy, w, smallFont, fg, dim, liveRowRects),
                idx++, tSinceOpenMs, reduceMotion, trayDirection);
        }
        else { liveRowRects.Clear(); }

        // 3) Gasto estimado por modelo
        if (cfg.ShowSpendEstimate && snap?.Spend is { } spend && spend.CostByModel.Count > 0)
        {
            y = Section(g, draw, "spend", s.SectionSpend, !cfg.CollapsedSpend, x, y, w, theme, labelFont, sectionRects,
                yy => DrawSpendSection(g, draw, snap, s, x, yy, w, labelFont, smallFont, fg, dim),
                idx++, tSinceOpenMs, reduceMotion, trayDirection);
        }

        // 4) Gráfica de uso
        if (cfg.ShowChart)
        {
            y = Section(g, draw, "chart", s.SectionChart, !cfg.CollapsedChart, x, y, w, theme, labelFont, sectionRects,
                yy => DrawChart(g, draw, x, yy, w, s, theme, cfg, smallFont, tabFont,
                    chartMode, chartRange, chartPctWindow, chartData, pctData, chartLoading,
                    tabRects, modeRects, pctWinRects, dim),
                idx++, tSinceOpenMs, reduceMotion, trayDirection);
            // Cuando la sección gráfica está plegada, no deben quedar rects fantasma.
            if (cfg.CollapsedChart) { tabRects.Clear(); modeRects.Clear(); pctWinRects.Clear(); }
        }
        else
        {
            tabRects.Clear(); modeRects.Clear(); pctWinRects.Clear();
        }

        return y;
    }

    /// <summary>
    /// Cabecera plegable: dibuja "▸/▾ Título", registra su rect y, si está expandida, llama al cuerpo.
    /// La entrada escalonada (§4 #3) envuelve TODO el draw de la sección en una traslación vertical en
    /// px enteros y, tras pintarla, la funde cubriéndola con el color de su superficie; el <c>y</c> que
    /// avanza es idéntico con o sin entrada, y ambas cosas solo ocurren en la pasada de pintado (medir
    /// no se desplaza). reduce-motion ⇒ sin desplazamiento ni fundido.
    /// </summary>
    private static int Section(Graphics g, bool draw, string key, string title, bool expanded,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects, Func<int, int> body,
        int sectionIndex, double tSinceOpenMs, bool reduceMotion, int trayDirection)
    {
        int offsetY = EntranceOffset(draw, reduceMotion, tSinceOpenMs, sectionIndex, trayDirection);

        // Solo desplazamos el DIBUJO (draw=true); la pasada de medir devuelve el mismo y sin transform.
        bool shift = offsetY != 0;
        if (shift) g.TranslateTransform(0, offsetY);
        try
        {
            int top = y;
            // T11: la fila de cabecera plegable (hit-rect + avance) escala con el DPI.
            var r = new Rectangle(x, y, w, Dpi.Scale(18));
            rects[key] = r;
            if (draw)
            {
                using var b = GdiCache.BrushFor(theme.TextPrimary);
                g.DrawString((expanded ? "▾ " : "▸ ") + title, f, b, x, y);
            }
            y += Dpi.Scale(22);
            if (expanded) y = body(y);
            CoverEntrance(g, draw, reduceMotion, tSinceOpenMs, sectionIndex, theme.Background, x, top, w, y - top);
        }
        finally
        {
            if (shift) g.TranslateTransform(0, -offsetY);
        }
        return y + Dpi.Scale(6);
    }

    /// <summary>
    /// Desplazamiento (px enteros) de la entrada escalonada de la sección <paramref name="index"/>, ya
    /// proyectado al DPI y orientado hacia la bandeja. 0 al medir y con reduce-motion.
    /// </summary>
    internal static int EntranceOffset(bool draw, bool reduceMotion, double tSinceOpenMs, int index, int trayDirection)
        => draw && !reduceMotion ? Stagger.OffsetPx(tSinceOpenMs, index, Dpi.Factor, trayDirection) : 0;

    /// <summary>
    /// Fundido de entrada de una sección (§4 #3): tras pintarla, cubre su banda con el color de la
    /// superficie que tiene debajo (<paramref name="surface"/>, el fondo plano del panel) y alfa
    /// (1 − a), con a según Rápido. Se ensancha un poco a los lados para cubrir también el realce de
    /// hover. Solo en la pasada de pintado; nada con reduce-motion o con la sección ya opaca.
    /// </summary>
    internal static void CoverEntrance(Graphics g, bool draw, bool reduceMotion, double tSinceOpenMs, int index,
        Color surface, int x, int top, int w, int height)
    {
        if (!draw || reduceMotion || height <= 0) return;
        double a = Stagger.FadeAlpha(tSinceOpenMs, index);
        int alpha = (int)Math.Round((1 - a) * surface.A);
        if (alpha <= 0) return;
        int pad = Spacing.Scaled.Sm;
        using var b = GdiCache.BrushFor(Color.FromArgb(alpha, surface));
        g.FillRectangle(b, x - pad, top - 1, w + 2 * pad, height + 2);
    }

    // ---------------- Cuerpos de sección (movidos de DashboardForm.cs, lógica intacta) ----------------

    /// <summary>Cuota: barra 5h + barra 7d + línea de pace + modelos 7d (Opus/Sonnet).</summary>
    private static int DrawQuotaBody(Graphics g, bool draw, AppSnapshot? snap, AppConfig cfg, Strings s, Theme theme,
        int x, int y, int w, Font labelFont, Font smallFont, Brush fg, Brush dim,
        MotionState? motion = null, bool reduceMotion = false)
    {
        var usage = snap?.Usage;
        if (usage is null)
        {
            if (draw)
            {
                string msg = snap is null ? s.Loading : UsageFormat.StateMessage(snap.LatestState, s);
                g.DrawString(msg, labelFont, dim, x, y);
            }
            return y + Dpi.Scale(24);
        }

        // Muestreo animado de cada barra (ancho, cifra, color y rastro) por clave del MotionState.
        var b5 = SampledBar(motion, "bar:5h", usage.FiveHour, reduceMotion);
        var b7 = SampledBar(motion, "bar:7d", usage.SevenDay, reduceMotion);
        // T11: el aire entre barras escala con el DPI (los px del plan de auditoría: 22/16/14/BarH).
        y = QuotaBar.Draw(g, draw, $"{s.SessionWord} (5h)", usage.FiveHour, snap?.PaceFive, x, y, w, cfg, s, theme, labelFont, smallFont, fg, dim,
            b5.Util, b5.Number, b5.Color, b5.Trail, LineBlend(motion, "reset:5h", reduceMotion));
        y += Dpi.Scale(16);
        y = QuotaBar.Draw(g, draw, $"{s.WeekWord} (7d)", usage.SevenDay, snap?.PaceSeven, x, y, w, cfg, s, theme, labelFont, smallFont, fg, dim,
            b7.Util, b7.Number, b7.Color, b7.Trail, LineBlend(motion, "reset:7d", reduceMotion));
        y += Dpi.Scale(14);

        y = DrawPace(g, draw, snap, s, theme, x, y, w, smallFont);

        // Aire entre la línea de pace y el desglose por modelo (Opus/Sonnet): antes iban pegados y se
        // leían como una sola fila apretada (auditoría visual, T9). Constante en ambas pasadas (medir==pintar).
        y += Spacing.Sm;
        y = DrawModelLine(g, draw, "Opus 7d", usage.SevenDayOpus, x, y, w, smallFont, fg, dim, theme, cfg, s.Culture);
        y = DrawModelLine(g, draw, "Sonnet 7d", usage.SevenDaySonnet, x, y, w, smallFont, fg, dim, theme, cfg, s.Culture);

        // Explicación honesta del rolling: la ventana de 5h corre desde tu 1ª petición, no a hora fija.
        // theme.TextMuted / Typography.Caption; suma su alto en ambas ramas (medir/pintar).
        if (draw)
        {
            using var muted = GdiCache.BrushFor(theme.TextMuted);
            g.DrawString(s.RollingHint, Typography.Caption, muted, x, y);
        }
        y += Dpi.Scale(16);
        // Aire extra al cierre de la sección Cuota: despega el bloque de modelos/hint del ACORDEÓN de abajo
        // (la mini-fila Sonnet 7d ya no queda pegada a la siguiente cabecera plegable). P2 #4.
        y += Spacing.Sm;
        return y;
    }

    /// <summary>Cruce de la línea de reset (micro-interacción 4); null sin motion o con reduce-motion.</summary>
    private static Func<string, (string? Prev, double Blend)>? LineBlend(MotionState? motion, string key, bool reduceMotion)
        => motion is null || reduceMotion ? null : t => motion.TextBlend(key, t);

    /// <summary>
    /// Muestreo completo de una barra (§4 #5): ancho y cifra del mismo muelle, color mezclado y rastro
    /// de renovación. Sin <paramref name="motion"/> todo es null ⇒ la barra pinta el valor crudo.
    /// </summary>
    private static (double? Util, double? Number, Color? Color, (double Pct, double Alpha)? Trail) SampledBar(
        MotionState? motion, string key, UsageWindow? win, bool reduceMotion)
    {
        if (motion is null || win is null) return (null, null, null, null);
        return (motion.Display(key, win.UtilizationPct, reduceMotion),
                motion.DisplayNumber(key, win.UtilizationPct, reduceMotion),
                reduceMotion ? null : motion.DisplayColor(key),
                reduceMotion ? null : motion.TrailOf(key));
    }

    /// <summary>
    /// Layout de una fila "texto izquierda + valor right-aligned" (T8b): el valor se ancla a
    /// <c>x+w</c> y el texto izquierdo se ELIDE con elipsis medida (gutter <c>Spacing.Md</c>) para no
    /// atravesarlo ni rebasar el panel — el patrón de <c>InfoRowLayout</c> de ajustes, compartido aquí
    /// por las sesiones en vivo y las claves de gasto. Determinista (misma medición en draw=false/true).
    /// T10: el valor se mide con <see cref="TextMetrics.MeasureWidth"/> (tipográfico + Ceiling) y el
    /// llamador debe pintarlo en <c>rightX</c> con <see cref="TextMetrics.Typographic"/> para que la
    /// tinta termine exacta en <c>x+w</c> (columna derecha alineada, §3 #16).
    /// </summary>
    internal static (string shownLeft, int rightX) RowWithRightValue(Graphics g, string left, string right,
        int x, int w, Font leftFont, Font rightFont)
    {
        int rightW = TextMetrics.MeasureWidth(g, right, rightFont);
        int rightX = Math.Max(x, x + w - rightW);
        string shown = TextWrap.FitLine(left, x, rightX, Spacing.Md,
            t => g.MeasureString(t, leftFont).Width);
        return (shown, rightX);
    }

    /// <summary>Gasto estimado por modelo (cabecera con días + filas modelo/$$).</summary>
    private static int DrawSpendSection(Graphics g, bool draw, AppSnapshot? snap, Strings s,
        int x, int y, int w, Font labelFont, Font smallFont, Brush fg, Brush dim)
    {
        var spend = snap?.Spend;
        if (spend is null || spend.CostByModel.Count == 0) return y;

        if (draw) g.DrawString(string.Format(s.SpendHeaderFormat, snap!.SpendDays), smallFont, dim, x, y);
        y += Dpi.Scale(18); // T11: filas de texto del gasto escalan con el DPI
        foreach (var kv in spend.CostByModel.OrderByDescending(k => k.Value))
        {
            if (draw)
            {
                // Moneda con la cultura del idioma elegido (T2): "$420.50" en inglés, "$420,50" en español.
                // T8b: la clave (nombre de modelo) se elide contra el $ right-aligned (gutter Md), no lo cruza.
                // T13a: las claves son familias dinámicas; la canónica "Otros" se localiza al pintar.
                // T13b: familia sin tarifa en el catálogo → "— <sufijo>" localizado, nunca "$0.00".
                string val = SpendValueText(spend, kv.Key, s);
                var (shownKey, valX) = RowWithRightValue(g, FamilyLabel(kv.Key, s), val, x, w, labelFont, Typography.Mono);
                g.DrawString(shownKey, labelFont, fg, x, y);
                g.DrawString(val, Typography.Mono, dim, valX, y, TextMetrics.Typographic);
            }
            y += Dpi.Scale(20);
        }
        return y;
    }

    /// <summary>
    /// Flecha de la línea de pace por estado de ritmo (F6, v0.3.9 g3), PURA y testeable. Antes la flecha
    /// ↗ estaba FIJA aunque una ventana fuera POR DEBAJO del ritmo ideal (pace Ok); la flecha mentía. Ahora
    /// ↗ solo cuando se va acelerado (Over/Critical) y → cuando se va en ritmo o por debajo (Ok).
    /// </summary>
    internal static string PaceArrow(PaceStatus status) => status == PaceStatus.Ok ? "→" : "↗";

    /// <summary>
    /// Segmentos coloreados de la línea de pace (F6, v0.3.9 g3), PUROS y testeables. Cada ventana (5h, 7d)
    /// aporta su flecha por <see cref="PaceArrow"/> y su % de ritmo, coloreados por SU PaceStatus con la
    /// variante AA de texto (<see cref="Theme.PaceTextColor"/>): la 7d sana ya no se tiñe del rojo de la 5h
    /// (antes TODA la línea iba del color de la PEOR ventana, §minor "tiñe todo del peor estado"). El grupo
    /// ETA se separa con " · ⚠" + el micro-rótulo localizado <paramref name="s"/>.PaceEtaLabel (en vez de
    /// una hora suelta sin contexto) y se colorea con el estado de la ventana que se agota. Los separadores
    /// (" · ") son neutros (<paramref name="dim"/>). Devuelve la lista en orden de pintado izq→der.
    /// </summary>
    internal static List<(string text, Color color)> PaceSegments(PaceResult? pf, PaceResult? ps, Strings s, Theme theme, Color dim)
    {
        var segs = new List<(string, Color)>();
        bool first = true;
        void AddWindow(PaceResult p)
        {
            if (!first) segs.Add((" · ", dim));
            first = false;
            string win = p.Window;   // "5h" | "7d"
            segs.Add(($"{PaceArrow(p.Status)} {win} {p.PaceRatio * 100:0}%", Theme.PaceTextColor(theme, p.Status)));
        }
        if (pf is not null) AddWindow(pf);
        if (ps is not null) AddWindow(ps);

        var exa = new[] { pf, ps }
            .Where(p => p is { ExhaustsBeforeReset: true, EtaUtc: not null })
            .OrderBy(p => p!.EtaUtc).FirstOrDefault();
        if (exa is not null)
        {
            // Grupo ETA: separador neutro + ⚠ rótulo localizado + hora absoluta, coloreado por la ventana
            // que se agota (no por la peor de toda la línea).
            segs.Add((" · ", dim));
            segs.Add(($"⚠ {s.PaceEtaLabel} {UsageFormat.ResetAbsolute(exa.EtaUtc, s.Culture)}",
                Theme.PaceTextColor(theme, exa.Status)));
        }
        return segs;
    }

    // Cuerpo de DrawPace de DashboardForm.cs, adaptado a recibir snap/theme/font por parámetro.
    // Recibe Strings por la cultura de formato del ETA "ddd HH:mm" (T2).
    internal static int DrawPace(Graphics g, bool draw, AppSnapshot? snap, Strings s, Theme theme, int x, int y, int w, Font smallFont)
    {
        var pf = snap?.PaceFive;
        var ps = snap?.PaceSeven;
        if (pf is null && ps is null) return y;
        if (!draw) return y + Dpi.Scale(18); // T11: misma fila escalada que la rama de pintado

        // F6 (v0.3.9 g3): la línea se pinta por SEGMENTOS — flecha por ventana + color por ventana (la 7d
        // sana ya no sale roja) + grupo ETA " · ⚠ <rótulo> <hora>". Antes era un único DrawString del color
        // de la PEOR ventana con la flecha ↗ fija.
        var segs = PaceSegments(pf, ps, s, theme, theme.TextSecondary);

        // T8c (invariante): nada se pinta más allá de x+w. Recorremos los segmentos izq→der y dejamos de
        // pintar en cuanto uno no cabe entero — el alto reservado no cambia → medir==pintar.
        float cx = x;
        float right = x + w;
        foreach (var (text, color) in segs)
        {
            float segW = g.MeasureString(text, smallFont).Width;
            if (cx + segW > right) break;   // no cabe entero: corta aquí (no rebasa el panel)
            using var br = GdiCache.BrushFor(color);
            g.DrawString(text, smallFont, br, cx, y);
            cx += segW;
        }
        return y + Dpi.Scale(18);
    }

    // Mini-fila de modelo (Opus/Sonnet 7d): alto de la línea de texto + barrita de referencia + su gap.
    // T11: en px de diseño (96 DPI), proyectados al DPI vigente (a factor 1.0, idénticos a los const).
    private static int ModelLineTextH => Dpi.Scale(16);   // alto de la línea etiqueta/%
    private static int ModelBarH => Dpi.Scale(4);         // barrita de referencia fina (mini-cuota)
    private static int ModelBarRadius => Dpi.Scale(2);

    // F7 (v0.3.9 g3): por debajo de este % la fila de modelo está prácticamente VACÍA (p.ej. "Sonnet 7d
    // 0%"); el % se pinta en TextMuted (no en foreground) para no resaltar el dato más vacío del panel.
    internal const double ModelEmptyEpsilonPct = 0.5;

    /// <summary>
    /// ¿El % de una mini-fila de modelo está bajo el epsilon de "vacío" (F7)? PURO y testeable: decide si
    /// el % se pinta atenuado (TextMuted) en vez de en foreground, para que "Sonnet 7d 0%" no resalte más
    /// que un modelo con uso real (jerarquía invertida del render auditado).
    /// </summary>
    internal static bool IsModelPctEmpty(double utilizationPct) => utilizationPct < ModelEmptyEpsilonPct;

    /// <summary>
    /// Color de RELLENO de la barrita de referencia de una mini-fila de modelo (F5, v0.3.9 g3): NEUTRO
    /// (<see cref="Theme.TextMuted"/>), no por riesgo. La mini-fila NO tiene pace por modelo, así que el
    /// verde/rojo semántico de <see cref="ColorMath.RiskColor"/> aquí era ruido — competía con el
    /// verde/rojo de las barras grandes (tres semánticas de color en el mismo panel, auditoría §3 #2).
    /// El relleno neutro la mantiene como referencia de longitud sin reclamar estado de color.
    /// </summary>
    internal static Color ModelBarFill(Theme theme) => theme.TextMuted;

    /// <summary>
    /// Mini-fila de cuota por modelo (Opus/Sonnet 7d): etiqueta a la izquierda + % a la derecha y, debajo,
    /// una <b>barrita de referencia</b> fina (track + relleno por <see cref="ColorMath.RiskColor"/>). Antes
    /// el % iba SUELTO sin barra (se leía como un número huérfano flotando entre "Semana (7d)" y el
    /// acordeón, P2 #4); la barrita lo ancla como una mini-cuota coherente con las barras de arriba.
    /// Mide==pinta: el alto reservado (texto + barra + gap) NO depende de <paramref name="draw"/>.
    /// </summary>
    internal static int DrawModelLine(Graphics g, bool draw, string label, UsageWindow? win, int x, int y, int w,
        Font smallFont, Brush fg, Brush dim, Theme theme, AppConfig cfg, System.Globalization.CultureInfo culture)
    {
        if (win is null) return y;
        if (draw)
        {
            g.DrawString(label, smallFont, dim, x, y);
            // % con la cultura del idioma elegido (T2), no la del SO.
            // T10 (§3 #16): medida central tipográfica + Ceiling, igual que QuotaBar → la columna
            // derecha del % de modelo queda en la MISMA vertical que el % de las barras grandes.
            // F7 (v0.3.9 g3): bajo el epsilon de "vacío" (p.ej. "Sonnet 7d 0%") el % va en TextMuted (dim)
            // y no en foreground — el dato más vacío del panel dejaba de ser el que más resaltaba.
            string val = UsageFormat.Percent(win.UtilizationPct, culture);
            int valW = TextMetrics.MeasureWidth(g, val, Typography.Mono);
            Brush pctBrush = IsModelPctEmpty(win.UtilizationPct) ? dim : fg;
            g.DrawString(val, Typography.Mono, pctBrush, x + w - valW, y, TextMetrics.Typographic);

            // Barrita de referencia (mini-cuota): track + relleno proporcional al %, color NEUTRO —
            // ancla el % como longitud sin reclamar estado (la mini-fila no tiene pace por modelo).
            // T3a (auditoría §3 #2): el track se corta un gap ANTES del % right-aligned — a todo el
            // ancho tachaba el número (el Mono de 12pt baja hasta la banda de la barrita).
            // F5 (v0.3.9 g3): el relleno deja de ir por RiskColor (verde/rojo) y pasa a ModelBarFill
            // (TextMuted) — quitaba una tercera semántica de color que competía con las barras grandes.
            int trackW = QuotaBarGeometry.CompactTrackWidth(w, valW, Spacing.Sm);
            int by = y + ModelLineTextH;
            if (trackW > 0)
            {
                using (var track = GdiCache.BrushFor(theme.Track))
                    Shapes.FillRounded(g, track, new Rectangle(x, by, trackW, ModelBarH), ModelBarRadius);
                double clamped = Math.Min(Math.Max(win.UtilizationPct, 0) / 100.0, 1.0);
                int fw = (int)Math.Round(trackW * clamped);
                if (fw > 1)
                {
                    using var fill = GdiCache.BrushFor(ModelBarFill(theme));
                    Shapes.FillRounded(g, fill, new Rectangle(x, by, fw, ModelBarH), ModelBarRadius);
                }
            }
        }
        return y + ModelLineTextH + ModelBarH + Spacing.Sm;   // texto + barra + gap entre mini-filas
    }

    // Cuerpo de DrawLiveSessions SIN su propia cabecera (la pone Section). Solo lista de instancias:
    // la mascota la pinta DashboardHeader, así que NO se vuelve a dibujar aquí (evita duplicado).
    internal static int DrawLiveSessionsBody(Graphics g, bool draw, LiveSessionsView view, Strings s,
        int x, int y, int w, Font smallFont, Brush fg, Brush dim,
        Dictionary<string, Rectangle> liveRowRects)
    {
        liveRowRects.Clear();

        // Lista de instancias
        if (view.Instances.Count == 0)
        {
            if (draw) g.DrawString(s.NoActiveSessions, smallFont, dim, x, y);
            y += Dpi.Scale(18);
        }
        else
        {
            foreach (var inst in view.Instances)
            {
                var rect = new Rectangle(x, y, w, Dpi.Scale(16)); // T11: fila clicable escalada
                if (draw)
                {
                    // T8b (§3 #14): el nombre de proyecto se elide contra la fase right-aligned
                    // (gutter Md) — antes un nombre largo la atravesaba y rebasaba el panel.
                    var st = PhaseLabel(s, inst.Phase);
                    var (shownName, phaseX) = RowWithRightValue(g, inst.ProjectName, st, x, w, smallFont, smallFont);
                    g.DrawString(shownName, smallFont, fg, x, y);
                    g.DrawString(st, smallFont, dim, phaseX, y, TextMetrics.Typographic);
                }
                liveRowRects[inst.SessionId] = rect;
                y += Dpi.Scale(18);
            }
        }
        return y;
    }

    internal static string PhaseLabel(Strings s, SessionPhase p) => p switch
    {
        SessionPhase.Idle => s.SessionPhaseIdle,
        SessionPhase.Processing => s.SessionPhaseProcessing,
        SessionPhase.WaitingForApproval => s.SessionPhaseWaitingApproval,
        SessionPhase.WaitingForInput => s.SessionPhaseWaitingInput,
        SessionPhase.Compacting => s.SessionPhaseCompacting,
        _ => s.SessionPhaseIdle,
    };

    // Cuerpo de DrawChart de DashboardForm.cs: toggle $/% , tabs de rango, selector 5h/7d y cuerpo.
    internal static int DrawChart(Graphics g, bool draw, int x, int y, int w, Strings s, Theme theme, AppConfig cfg,
        Font smallFont, Font tabFont,
        string chartMode, ChartRange chartRange, string chartPctWindow,
        List<HistoryBucket> chartData, List<PctPoint> pctData, bool chartLoading,
        Dictionary<ChartRange, Rectangle> tabRects, Dictionary<string, Rectangle> modeRects,
        Dictionary<string, Rectangle> pctWinRects, Brush dim)
    {
        bool pct = chartMode == "percent";

        // Toggle de modo (Spend $ | Quota %), right-aligned. T5c: AQUÍ ya no se pinta el título
        // "Usage chart" — la cabecera plegable de la sección ("▾ Chart") ya nombra el bloque y el
        // doble título era ruido (§3 jerarquía). La fila se conserva (y += 18) porque la ocupa el
        // toggle; el layout no cambia → medir==pintar intacto.
        modeRects.Clear();
        DrawSegments(g, draw, tabFont, theme,
            new[] { (s.ChartTabSpend, "spend"), (s.ChartTabPct, "percent") }, chartMode,
            x + w, y - 1, rightAlign: true, modeRects);
        y += Dpi.Scale(18); // T11: fila del toggle de modo escalada

        // Range tabs (left) — T7a (§3 #5): mismos chips que el resto del panel vía DrawSegments (alto
        // 18, radio 4, padX 7, gap 3 y borde Separator en el inactivo — antes BgElevated puro con
        // geometría propia 20/5/6/4, invisible en tema claro). El avance de la fila (y += 26) no
        // cambia → layout intacto y medir==pintar (los rects se registran en ambas pasadas).
        tabRects.Clear();
        DrawSegments(g, draw, tabFont, theme,
            Tabs.Select(t => (t.label, t.range)).ToArray(), chartRange,
            x, y, rightAlign: false, tabRects);
        // Window selector [5h|7d] (right, only in percent mode)
        pctWinRects.Clear();
        if (pct)
            DrawSegments(g, draw, tabFont, theme,
                new[] { ("5h", "5h"), ("7d", "7d") }, chartPctWindow,
                x + w, y, rightAlign: true, pctWinRects);
        y += Dpi.Scale(26); // T11: fila de tabs de rango escalada

        return pct
            ? DrawPercentBody(g, draw, x, y, w, s, theme, cfg, smallFont, chartRange, chartPctWindow, pctData, chartLoading, dim)
            : DrawSpendBody(g, draw, x, y, w, s, theme, smallFont, chartData, chartLoading, dim);
    }

    /// <summary>
    /// X de dibujo de una etiqueta del eje horizontal, PURA y testeable. Por defecto la centra bajo su
    /// tick (<paramref name="centerX"/>), pero la ancla al borde del plot cuando se saldría: la primera
    /// etiqueta (tick = plotLeft) dejaba de cortarse por la izquierda ("Jun 00h" → "un 00h") y la última
    /// por la derecha. Si la etiqueta es más ancha que el plot, prioriza no cortar por la izquierda.
    /// </summary>
    internal static float AxisLabelX(float centerX, float labelW, float plotLeft, float plotRight)
    {
        float lx = centerX - labelW / 2f;          // centrada bajo el tick
        if (lx + labelW > plotRight) lx = plotRight - labelW;  // última: no cortar por la derecha
        if (lx < plotLeft) lx = plotLeft;          // primera: no cortar por la izquierda (gana)
        return lx;
    }

    /// <summary>
    /// Filtro de colisión de etiquetas del eje X (T4a, §3 #1), PURO y testeable. Recorre las etiquetas
    /// de izquierda a derecha (con sus x ya resueltos por <see cref="AxisLabelX"/>) y devuelve los
    /// índices de las que se pintan: una etiqueta solo se mantiene si arranca al menos
    /// <paramref name="minGap"/> px después del borde derecho de la última MANTENIDA (la primera
    /// siempre se mantiene). Antes el muestreo era solo por índice (cada n/8) sin medir: la 1ª anclada
    /// a plotLeft y la 2ª centrada bajo su tick se superponían ("lun 1{2/2}h") y el resto iban pegadas.
    /// </summary>
    internal static List<int> VisibleAxisLabels(IReadOnlyList<(float x, float w)> labels, float minGap)
    {
        var keep = new List<int>(labels.Count);
        float lastRight = float.NegativeInfinity;
        for (int i = 0; i < labels.Count; i++)
        {
            if (keep.Count > 0 && labels[i].x < lastRight + minGap) continue;
            keep.Add(i);
            lastRight = labels[i].x + labels[i].w;
        }
        return keep;
    }

    // Marcador del pico: radio del punto (el FillEllipse de 5 px) y holgura mínima etiqueta↔punto (T4b).
    private const float PeakMarkerR = 2.5f;
    private const float PeakLabelGap = 2f;

    /// <summary>
    /// Posición de la etiqueta "peak …" (T4b, §3 #9), PURA y testeable. Parte de centrada sobre el
    /// marcador con holgura real (el offset histórico <c>alto+1</c> dejaba el borde inferior DENTRO del
    /// punto) y resuelve dos colisiones en orden: (1) la etiqueta fija superior izquierda (total $/%
    /// actual): baja bajo el marcador y, si sigue chocando, se corre a su derecha (comportamiento
    /// previo conservado); (2) el propio punto del pico: cuando el clamp superior deja el punto dentro
    /// del rect (pico en el último bucket ⇒ etiqueta clavada al borde derecho con el punto ENCIMA del
    /// texto), se desplaza a un lado del punto — izquierda si cabe, derecha si no. Nunca sale del plot.
    /// </summary>
    internal static PointF PeakLabelPos(float peakX, float peakY, SizeF label, int x, int w, int top, RectangleF avoid)
    {
        float pxp = Math.Clamp(peakX - label.Width / 2f, x, x + w - label.Width);
        float pyp = Math.Max(top - 2, peakY - label.Height - PeakMarkerR - PeakLabelGap);
        var rect = new RectangleF(pxp, pyp, label.Width, label.Height);

        // (1) Etiqueta fija superior izquierda (total / % actual): bajo el marcador; si sigue, a su derecha.
        if (rect.IntersectsWith(avoid))
        {
            pyp = peakY + 4;
            rect = new RectangleF(pxp, pyp, label.Width, label.Height);
            if (rect.IntersectsWith(avoid))
                pxp = Math.Clamp(avoid.Right + 6, x, x + w - label.Width);
            rect = new RectangleF(pxp, pyp, label.Width, label.Height);
        }

        // (2) El punto del pico: solo intersecta cuando manda el clamp superior (en modo $ el pico
        // siempre cae en esa fila). A un lado del punto, sin pisar la etiqueta fija.
        var marker = new RectangleF(peakX - PeakMarkerR, peakY - PeakMarkerR, PeakMarkerR * 2, PeakMarkerR * 2);
        if (rect.IntersectsWith(marker))
        {
            float leftX = peakX - PeakMarkerR - PeakLabelGap - label.Width;
            pxp = leftX >= x ? leftX : Math.Min(peakX + PeakMarkerR + PeakLabelGap, x + w - label.Width);
            rect = new RectangleF(pxp, pyp, label.Width, label.Height);
            if (rect.IntersectsWith(avoid)) pyp = peakY + 4;   // red de seguridad: nunca sobre la fija
        }
        return new PointF(pxp, pyp);
    }

    /// <summary>
    /// Hilera de chips de selección única (geometría canónica: alto 18, radio 4, padX 7, gap 3; activo
    /// Accent + Contrast, inactivo BgElevated + borde Separator). Genérico en la CLAVE (T7a): el modo
    /// $/% y la ventana 5h/7d usan string; las tabs de rango usan <see cref="ChartRange"/> directamente
    /// — un único lenguaje de pill sin diccionarios puente.
    /// </summary>
    internal static void DrawSegments<TKey>(Graphics g, bool draw, Font font, Theme theme,
        (string label, TKey key)[] segs, TKey activeKey, int anchorX, int y,
        bool rightAlign, Dictionary<TKey, Rectangle> rects) where TKey : notnull
    {
        const int gap = 3, padX = 7;
        int h = Dpi.Scale(18); // T11: el alto del chip escala con el DPI (el texto centrado ya crecía)
        var widths = segs.Select(seg => (int)g.MeasureString(seg.label, font).Width + padX * 2).ToArray();
        int total = widths.Sum() + gap * (segs.Length - 1);
        int sx = rightAlign ? anchorX - total : anchorX;
        for (int i = 0; i < segs.Length; i++)
        {
            var (label, key) = segs[i];
            var rect = new Rectangle(sx, y, widths[i], h);
            if (draw)
            {
                bool active = EqualityComparer<TKey>.Default.Equals(key, activeKey);
                using var bg = GdiCache.BrushFor(active ? theme.Accent : theme.BgElevated);
                Shapes.FillRounded(g, bg, rect, 4);
                // Borde sutil del chip inactivo (track) para que se lea como BOTÓN incluso cuando el
                // BgElevated casi iguala al fondo (tema claro: #FFF sobre #FAFAFA, antes invisible). El
                // chip activo (Accent) no lo necesita: ya contrasta. (P1 #3, lenguaje de pill único.)
                if (!active)
                    using (var bd = GdiCache.PenFor(theme.Separator))
                        Shapes.DrawRounded(g, bd, rect, 4);
                using var tb = GdiCache.BrushFor(active ? ColorMath.Contrast(theme.Accent) : theme.TextPrimary);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, tb, rect, sf);
            }
            rects[key] = rect;
            sx += widths[i] + gap;
        }
    }

    internal static int DrawSpendBody(Graphics g, bool draw, int x, int top, int w, Strings s, Theme theme,
        Font smallFont, List<HistoryBucket> chartData, bool chartLoading, Brush dim)
    {
        int bottom = top + ChartH;
        if (chartLoading)
        {
            if (draw) g.DrawString("…", smallFont, dim, x, top + ChartH / 2);
            return bottom + ChartFooter;
        }

        int n = chartData.Count;
        double max = n > 0 ? chartData.Max(b => b.CostUsd) : 0;
        if (n == 0 || max <= 0)
        {
            if (draw) g.DrawString(s.NoData, smallFont, dim, x, top + ChartH / 2 - 6);
            return bottom + ChartFooter;
        }
        if (!draw) return bottom + ChartFooter;

        double total = chartData.Sum(b => b.CostUsd);
        // Moneda con la cultura del idioma elegido (T2).
        var totalText = $"{s.ChartTotal} {UsageFormat.Money(total, s.Culture)}";
        var totalSz = g.MeasureString(totalText, smallFont);
        g.DrawString(totalText, smallFont, dim, x, top - 1);
        var totalRect = new RectangleF(x, top - 1, totalSz.Width, totalSz.Height);

        float X(int i) => n == 1 ? x + w / 2f : x + (float)i * w / (n - 1);
        float Y(double v) => bottom - (float)(v / max) * (ChartH - Dpi.Scale(14)); // T11: headroom escalado

        // T13a: series dinámicas — las familias reales de la ventana, apiladas por rango de gasto
        // (la 1.ª abajo) y con color por slot estable de theme.ChartSeries (no por nombre).
        var families = ChartFamilies(chartData);
        var slots = SeriesAssigner.Assign(families);

        var baseline = new double[n];
        foreach (var family in families)
        {
            var topArr = new double[n];
            for (int i = 0; i < n; i++) topArr[i] = baseline[i] + chartData[i].Cost(family);

            var pts = new List<PointF>(2 * n);
            for (int i = 0; i < n; i++) pts.Add(new PointF(X(i), Y(topArr[i])));
            for (int i = n - 1; i >= 0; i--) pts.Add(new PointF(X(i), Y(baseline[i])));
            using var br = GdiCache.BrushFor(SeriesColor(theme, slots, family));
            if (pts.Count >= 3) g.FillPolygon(br, pts.ToArray());

            baseline = topArr;
        }

        int peakIdx = 0;
        for (int i = 1; i < n; i++)
            if (chartData[i].CostUsd > chartData[peakIdx].CostUsd) peakIdx = i;
        AnnotatePeak(g, smallFont, theme, $"{s.ChartPeak} {UsageFormat.Money(max, s.Culture)}", X(peakIdx), Y(max), x, w, top, totalRect);

        // Etiquetas del eje X: muestreo por índice + filtro de colisión midiendo cada etiqueta (T4a) —
        // las que pisarían a la última pintada se saltan (antes "lun 1{2/2}h" y vecinas pegadas, §3 #1).
        int labelEvery = Math.Max(1, (int)Math.Ceiling(n / 8.0));
        var axisLabels = new List<(string text, float lx, float lw)>();
        for (int i = 0; i < n; i += labelEvery)
        {
            var lbl = chartData[i].Label;
            var lsz = g.MeasureString(lbl, smallFont);
            // Ancla la 1ª/última etiqueta al borde del plot para que no se corten (auditoría visual, T9).
            axisLabels.Add((lbl, AxisLabelX(X(i), lsz.Width, x, x + w), lsz.Width));
        }
        foreach (int k in VisibleAxisLabels(axisLabels.Select(l => (l.lx, l.lw)).ToArray(), Spacing.Xs))
            g.DrawString(axisLabels[k].text, smallFont, dim, axisLabels[k].lx, bottom + 2);

        // T11: la leyenda escala con el DPI (la fila del eje X crece con la fuente; sin escalar, la
        // leyenda a bottom+16 fijos se montaría sobre las etiquetas a 125/150%).
        // T13a: la leyenda lista las familias REALES de los datos (rango = orden), con su slot de color.
        int lx = x, ly = bottom + Dpi.Scale(16);
        foreach (var family in families)
        {
            using var sw = GdiCache.BrushFor(SeriesColor(theme, slots, family));
            // T4c: swatch en ly+4 (antes ly+2) — quedaba ~2px alto respecto al centro óptico del texto
            // de la leyenda (caja de mayúsculas del Caption ≈ [ly+4, ly+13]; el cuadrado centraba en
            // ly+6.5 en vez de ≈ly+8.5). Mismo tamaño 9×9; solo baja el anclaje vertical. (T11: todo
            // en px de diseño escalados — a factor 1.0, idéntico.)
            g.FillRectangle(sw, lx, ly + Dpi.Scale(4), Dpi.Scale(9), Dpi.Scale(9));
            string name = FamilyLabel(family, s);
            g.DrawString(name, smallFont, dim, lx + Dpi.Scale(12), ly);
            lx += Dpi.Scale(12) + (int)g.MeasureString(name, smallFont).Width + Dpi.Scale(10);
        }
        return bottom + ChartFooter;
    }

    /// <summary>
    /// Y (px) de la gridline de umbral del modo Cuota% (F13, v0.3.9 g3), PURA y testeable. Es el análogo
    /// vertical de <see cref="QuotaBarGeometry.TickX"/>: proyecta un % [0,100] sobre el eje Y del plot con
    /// la MISMA fórmula que la línea de datos (<c>Y(v)</c> de <see cref="DrawPercentBody"/>), de modo que la
    /// gridline de 90% cae exactamente a la altura donde la curva marca 90%. 0% ⇒ <paramref name="bottom"/>,
    /// 100% ⇒ <c>bottom - (chartH - headroom)</c>. Recorta el % a [0,100].
    /// </summary>
    internal static float ThresholdY(int bottom, int chartH, int headroom, double pct)
        => bottom - (float)(Math.Clamp(pct, 0, 100) / 100.0) * (chartH - headroom);

    internal static int DrawPercentBody(Graphics g, bool draw, int x, int top, int w, Strings s, Theme theme, AppConfig cfg,
        Font smallFont, ChartRange chartRange, string chartPctWindow, List<PctPoint> pctData, bool chartLoading, Brush dim)
    {
        int bottom = top + ChartH;
        if (chartLoading)
        {
            if (draw) g.DrawString("…", smallFont, dim, x, top + ChartH / 2);
            return bottom + ChartFooter;
        }

        Func<PctPoint, double?> sel = chartPctWindow == "5h" ? p => p.FivePct : p => p.SevenPct;
        var pts = pctData.Where(p => sel(p) is not null)
            .Select(p => (p.TsUtc, v: sel(p)!.Value)).ToList();

        if (pts.Count == 0)
        {
            if (draw) g.DrawString(s.NoData, smallFont, dim, x, top + ChartH / 2 - 6);
            return bottom + ChartFooter;
        }
        if (!draw) return bottom + ChartFooter;

        int n = pts.Count;
        double peak = pts.Max(p => p.v);
        double current = pts[^1].v;

        float X(int i) => n == 1 ? x + w / 2f : x + (float)i * w / (n - 1);
        float Y(double v) => bottom - (float)(Math.Clamp(v, 0, 100) / 100.0) * (ChartH - Dpi.Scale(14)); // T11

        Color status = ColorMath.RiskColor(peak, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);

        // current value (top-left) — % con la cultura del idioma elegido (T2).
        var curText = UsageFormat.Percent(current, s.Culture);
        var curSz = g.MeasureString(curText, smallFont);
        g.DrawString(curText, smallFont, dim, x, top - 1);
        var curRect = new RectangleF(x, top - 1, curSz.Width, curSz.Height);

        // F13 (v0.3.9 g3): gridlines de UMBRAL en Warn/Crit (el modo % ya recibe cfg). Antes del fill para
        // que el área no las tape (igual que los ticks de la QuotaBar se pintan tras el relleno por las
        // esquinas redondeadas, aquí el fill es semitransparente y deja ver la línea por debajo). Finas,
        // punteadas y NEUTRAS (theme.Separator) — son escala, no estado: dan el cruce warn/crit que el modo
        // % no tenía, sin meter una 4.ª semántica de color sobre el plot. Y de cada umbral por ThresholdY
        // (misma proyección que la curva). Headroom = Dpi.Scale(14), igual que Y(v).
        using (var grid = new Pen(theme.Separator, 1f) { DashStyle = DashStyle.Dash })
        {
            int headroom = Dpi.Scale(14);
            foreach (double th in new[] { cfg.WarnThresholdPct, cfg.CriticalThresholdPct })
            {
                float gy = ThresholdY(bottom, ChartH, headroom, th);
                g.DrawLine(grid, x, gy, x + w, gy);
            }
        }

        // filled area under the line
        var poly = new List<PointF>(n + 2);
        for (int i = 0; i < n; i++) poly.Add(new PointF(X(i), Y(pts[i].v)));
        poly.Add(new PointF(X(n - 1), bottom));
        poly.Add(new PointF(X(0), bottom));
        using (var fill = GdiCache.BrushFor(Color.FromArgb(70, status)))
            if (poly.Count >= 3) g.FillPolygon(fill, poly.ToArray());
        // line on top
        if (n >= 2)
        {
            using var pen = GdiCache.PenFor(status, 1.8f);
            var line = new PointF[n];
            for (int i = 0; i < n; i++) line[i] = new PointF(X(i), Y(pts[i].v));
            g.DrawLines(pen, line);
        }

        // peak annotation
        int peakIdx = 0;
        for (int i = 1; i < n; i++) if (pts[i].v > pts[peakIdx].v) peakIdx = i;
        AnnotatePeak(g, smallFont, theme, $"{s.ChartPeak} {UsageFormat.Percent(peak, s.Culture)}", X(peakIdx), Y(peak), x, w, top, curRect);

        // x-axis time labels — muestreo por índice + filtro de colisión midiendo cada etiqueta (T4a).
        int labelEvery = Math.Max(1, (int)Math.Ceiling(n / 6.0));
        bool longRange = chartRange is ChartRange.Week1 or ChartRange.Month1;
        var axisLabels = new List<(string text, float lx, float lw)>();
        for (int i = 0; i < n; i += labelEvery)
        {
            string lbl = pts[i].TsUtc.ToLocalTime().ToString(longRange ? "dd/MM" : "HH:mm", s.Culture);
            var lsz = g.MeasureString(lbl, smallFont);
            // Ancla la 1ª/última etiqueta al borde del plot para que no se corten (auditoría visual, T9).
            axisLabels.Add((lbl, AxisLabelX(X(i), lsz.Width, x, x + w), lsz.Width));
        }
        foreach (int k in VisibleAxisLabels(axisLabels.Select(l => (l.lx, l.lw)).ToArray(), Spacing.Xs))
            g.DrawString(axisLabels[k].text, smallFont, dim, axisLabels[k].lx, bottom + 2);
        return bottom + ChartFooter;
    }

    // Cuerpo de AnnotatePeak de DashboardForm.cs. T4d: marcador y etiqueta usan SIEMPRE theme.Foreground
    // — antes el body de gasto pasaba theme:null y caía al pincel dim mientras el modo % usaba
    // Foreground: mismo dato, dos jerarquías visuales. La posición de la etiqueta la resuelve
    // <see cref="PeakLabelPos"/> (pura): esquiva la etiqueta fija superior izquierda Y el propio punto.
    private static void AnnotatePeak(Graphics g, Font font, Theme theme, string text, float peakX, float peakY,
        int x, int w, int top, RectangleF avoid)
    {
        using var mb = GdiCache.BrushFor(theme.Foreground);
        g.FillEllipse(mb, peakX - PeakMarkerR, peakY - PeakMarkerR, PeakMarkerR * 2, PeakMarkerR * 2);
        var psz = g.MeasureString(text, font);
        var pos = PeakLabelPos(peakX, peakY, psz, x, w, top, avoid);
        g.DrawString(text, font, mb, pos.X, pos.Y);
    }

}
