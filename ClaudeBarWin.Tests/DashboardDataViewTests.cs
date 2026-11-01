using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T9 (auditoría visual): la primera etiqueta del eje X de la gráfica se cortaba por la izquierda
/// ("Jun 00h" → "un 00h") porque se centraba bajo su tick y el tick más a la izquierda coincide con
/// el borde del plot. <see cref="DashboardDataView.AxisLabelX"/> es un helper PURO que recoloca cada
/// etiqueta dentro de [plotLeft, plotRight] sin solapar ni salirse, así que se testea sin GDI+.
/// </summary>
public class DashboardDataViewTests
{
    [Fact]
    public void First_label_is_not_clipped_on_the_left()
    {
        // Etiqueta de 40 px centrada en plotLeft (x=100) querría empezar en 80 (< plotLeft) → se ancla.
        float lx = DashboardDataView.AxisLabelX(centerX: 100, labelW: 40, plotLeft: 100, plotRight: 400);
        Assert.True(lx >= 100, $"la primera etiqueta empezó en {lx} (< plotLeft 100): se cortaría por la izquierda");
        Assert.Equal(100, lx);
    }

    [Fact]
    public void Last_label_is_not_clipped_on_the_right()
    {
        // Etiqueta de 40 px centrada en plotRight (x=400) querría terminar en 420 (> plotRight) → se ancla.
        float lx = DashboardDataView.AxisLabelX(centerX: 400, labelW: 40, plotLeft: 100, plotRight: 400);
        Assert.True(lx + 40 <= 400, $"la última etiqueta terminó en {lx + 40} (> plotRight 400): se cortaría por la derecha");
        Assert.Equal(360, lx);
    }

    [Fact]
    public void Middle_label_stays_centered()
    {
        // Bien dentro del plot: se centra bajo su tick (250 - 20 = 230).
        float lx = DashboardDataView.AxisLabelX(centerX: 250, labelW: 40, plotLeft: 100, plotRight: 400);
        Assert.Equal(230, lx);
    }

    [Fact]
    public void Label_never_starts_before_plot_left_for_any_center()
    {
        for (float cx = 100; cx <= 400; cx += 13)
        {
            float lx = DashboardDataView.AxisLabelX(cx, labelW: 50, plotLeft: 100, plotRight: 400);
            Assert.True(lx >= 100, $"centerX {cx}: arranca en {lx} (< 100)");
        }
    }

    [Fact]
    public void Wide_label_is_clamped_to_plot_left_when_it_cannot_fit()
    {
        // Etiqueta más ancha que el plot: prioriza no cortar por la izquierda (Near en plotLeft).
        float lx = DashboardDataView.AxisLabelX(centerX: 250, labelW: 500, plotLeft: 100, plotRight: 400);
        Assert.Equal(100, lx);
    }

    // ================= v0.3.5 P2 #4: mini-fila de modelo (Opus/Sonnet 7d) con barra de referencia =================

    [Fact]
    public void ModelLine_measure_equals_paint()
    {
        // Invariante de 2 pasadas: medir (draw=false) y pintar (draw=true) avanzan el MISMO y.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(42, DateTimeOffset.UtcNow);
        var cfg = new AppConfig();

        int measured = DashboardDataView.DrawModelLine(g, draw: false, "Sonnet 7d", win, 16, 30, 300,
            Typography.Caption, fg, dim, Theme.Dark, cfg, Localization.Get("en").Culture);
        int painted = DashboardDataView.DrawModelLine(g, draw: true, "Sonnet 7d", win, 16, 30, 300,
            Typography.Caption, fg, dim, Theme.Dark, cfg, Localization.Get("en").Culture);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void ModelLine_reserves_room_for_the_reference_bar()
    {
        // P2 #4: la mini-fila ya no es solo el % suelto; reserva alto para una barrita de referencia
        // debajo (texto + barra + gap), así el % se lee como una mini-cuota anclada, no flotando.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(42, DateTimeOffset.UtcNow);
        var cfg = new AppConfig();

        const int y0 = 30;
        int after = DashboardDataView.DrawModelLine(g, draw: false, "Opus 7d", win, 16, y0, 300,
            Typography.Caption, fg, dim, Theme.Dark, cfg, Localization.Get("en").Culture);

        // Crece más que una simple línea de 16px (texto + barra + gap).
        Assert.True(after - y0 > 16, $"la mini-fila debe reservar alto para la barra de referencia (avance={after - y0})");
    }

    [Fact]
    public void ModelLine_draws_a_reference_bar_under_the_text()
    {
        // La barrita de referencia se pinta DEBAJO del texto: hay píxeles del relleno en la banda de la
        // barra (no solo el track), anclando el % a una mini-cuota. F5 (v0.3.9 g3): el relleno es NEUTRO
        // (ModelBarFill = TextMuted), no por riesgo — el color se busca contra ese token, no contra RiskColor.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(60, DateTimeOffset.UtcNow); // 60% → relleno visible
        var cfg = new AppConfig();
        var c = DashboardDataView.ModelBarFill(Theme.Dark);

        const int x = 16, y0 = 30, w = 300;
        DashboardDataView.DrawModelLine(g, draw: true, "Sonnet 7d", win, x, y0, w,
            Typography.Caption, fg, dim, Theme.Dark, cfg, Localization.Get("en").Culture);

        // Banda de la barra: justo bajo la línea de texto (16px). Busca el color de riesgo cerca del inicio.
        bool foundFill = false;
        for (int py = y0 + 16; py <= y0 + 24 && !foundFill; py++)
            for (int px = x + 1; px < x + 20 && !foundFill; px++)
            {
                var p = bmp.GetPixel(px, py);
                if (Math.Abs(p.R - c.R) <= 14 && Math.Abs(p.G - c.G) <= 14 && Math.Abs(p.B - c.B) <= 14)
                    foundFill = true;
            }
        Assert.True(foundFill, "la mini-fila de modelo debe pintar una barra de referencia rellena bajo el %");
    }

    // ================= T4a: colisión de etiquetas del eje X (filtro de espaciado mínimo) =================

    [Fact]
    public void Axis_labels_without_collision_are_all_kept()
    {
        var labels = new (float x, float w)[] { (0, 30), (40, 30), (80, 30) };
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        Assert.Equal(new[] { 0, 1, 2 }, keep);
    }

    [Fact]
    public void Axis_label_overlapping_the_previous_kept_is_skipped()
    {
        // Caso real del render auditado (§3 #1): la 1ª etiqueta anclada a plotLeft [0,46] y la 2ª
        // centrada bajo su tick arrancando en 33 → se superponían ("lun 1{2/2}h"). La 3ª (89) sí cabe.
        var labels = new (float x, float w)[] { (0, 46), (33, 46), (89, 46) };
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        Assert.Equal(new[] { 0, 2 }, keep);
    }

    [Fact]
    public void Skipped_axis_label_does_not_block_the_next_one()
    {
        // El hueco se mide contra la última MANTENIDA, no contra la saltada: 1 colisiona con 0 (28 <
        // 30+4) y se salta; 2 (36 ≥ 34) cabe respecto a 0 aunque pisara a la saltada.
        var labels = new (float x, float w)[] { (0, 30), (28, 30), (36, 30) };
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        Assert.Equal(new[] { 0, 2 }, keep);
    }

    [Fact]
    public void First_axis_label_is_always_kept()
    {
        var labels = new (float x, float w)[] { (10, 200) };
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        Assert.Equal(new[] { 0 }, keep);
    }

    [Fact]
    public void Kept_axis_labels_never_overlap_for_dense_input()
    {
        // Propiedad: tras el filtro, cada etiqueta mantenida respeta el gap con la anterior mantenida.
        var labels = new List<(float x, float w)>();
        for (int i = 0; i < 20; i++) labels.Add((i * 17f, 40f)); // densas: cada una pisa a la siguiente
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        for (int k = 1; k < keep.Count; k++)
        {
            var prev = labels[keep[k - 1]];
            var cur = labels[keep[k]];
            Assert.True(cur.x >= prev.x + prev.w + 4,
                $"etiquetas {keep[k - 1]} y {keep[k]} mantenidas pero solapadas ({prev.x + prev.w} → {cur.x})");
        }
    }

    // ================= T4b: la etiqueta "peak …" no debe pisar el punto del pico =================

    // Geometría compartida con producción: punto de r=2.5 px centrado en (peakX, peakY).
    private static RectangleF MarkerRect(float peakX, float peakY) => new(peakX - 2.5f, peakY - 2.5f, 5, 5);

    [Fact]
    public void Peak_label_clears_the_marker_when_peak_is_in_the_last_bucket()
    {
        // §3 #9: pico en el último bucket ⇒ etiqueta clavada al borde derecho (clamp) con el punto
        // ENCIMA del texto ("peak $41,81" pisado). En modo $ el pico siempre cae en la fila del clamp
        // superior (peakY = top+14), así que el rect centrado intersecta el punto → debe apartarse.
        var label = new SizeF(70, 15);
        var pos = DashboardDataView.PeakLabelPos(peakX: 310, peakY: 34, label, x: 10, w: 300, top: 20,
            avoid: new RectangleF(10, 19, 60, 15));
        var rect = new RectangleF(pos.X, pos.Y, label.Width, label.Height);
        Assert.False(rect.IntersectsWith(MarkerRect(310, 34)),
            $"la etiqueta {rect} sigue pisando el punto del pico {MarkerRect(310, 34)}");
        Assert.True(pos.X >= 10 && pos.X + label.Width <= 310, $"se sale del plot: x={pos.X}");
    }

    [Fact]
    public void Peak_label_stays_centered_above_the_marker_when_there_is_room()
    {
        // Pico a media altura y centrado: la etiqueta queda centrada sobre el punto, sin tocarlo.
        var label = new SizeF(70, 15);
        var pos = DashboardDataView.PeakLabelPos(peakX: 160, peakY: 60, label, x: 10, w: 300, top: 20,
            avoid: new RectangleF(10, 19, 60, 15));
        Assert.Equal(160 - 35, pos.X);
        var rect = new RectangleF(pos.X, pos.Y, label.Width, label.Height);
        Assert.False(rect.IntersectsWith(MarkerRect(160, 60)), "centrada pero pisando el punto");
        Assert.True(pos.Y + label.Height <= 60 - 2.5f, $"el borde inferior ({pos.Y + label.Height}) entra en el punto");
    }

    [Fact]
    public void Peak_label_goes_right_of_the_marker_when_there_is_no_room_left()
    {
        // Pico clavado al borde izquierdo en la fila del clamp: a la izquierda no cabe → a la derecha.
        var label = new SizeF(70, 15);
        var pos = DashboardDataView.PeakLabelPos(peakX: 12, peakY: 34, label, x: 10, w: 300, top: 20,
            avoid: RectangleF.Empty);
        var rect = new RectangleF(pos.X, pos.Y, label.Width, label.Height);
        Assert.False(rect.IntersectsWith(MarkerRect(12, 34)), $"etiqueta {rect} sobre el punto");
        Assert.True(pos.X >= 12 + 2.5f, $"debía colocarse a la derecha del punto (x={pos.X})");
    }

    [Fact]
    public void Peak_label_still_drops_below_when_colliding_with_topleft_label()
    {
        // Comportamiento previo conservado: si la etiqueta del pico cae sobre el total/% actual
        // (esquina superior izquierda), baja bajo su marcador.
        var label = new SizeF(70, 15);
        var avoid = new RectangleF(10, 19, 80, 15);
        var pos = DashboardDataView.PeakLabelPos(peakX: 40, peakY: 34, label, x: 10, w: 300, top: 20, avoid);
        var rect = new RectangleF(pos.X, pos.Y, label.Width, label.Height);
        Assert.False(rect.IntersectsWith(avoid), $"etiqueta {rect} sigue sobre la fija {avoid}");
        Assert.True(pos.Y >= 34 + 2.5f, $"debía caer bajo el marcador (y={pos.Y})");
    }

    [Fact]
    public void Peak_label_never_leaves_the_plot()
    {
        var label = new SizeF(70, 15);
        for (float px = 10; px <= 310; px += 7)
        {
            var pos = DashboardDataView.PeakLabelPos(px, peakY: 34, label, x: 10, w: 300, top: 20,
                avoid: new RectangleF(10, 19, 60, 15));
            Assert.True(pos.X >= 10 && pos.X + label.Width <= 310,
                $"peakX {px}: etiqueta fuera del plot (x={pos.X})");
        }
    }

    // ================= T4d: color del pico unificado ($ y % usan Foreground) =================

    [Fact]
    public void Spend_peak_annotation_uses_foreground_like_percent_mode()
    {
        // §T4d: en modo $ el marcador/etiqueta del pico caían al pincel dim (theme era null en el
        // body de gasto) mientras el modo % usaba Foreground: mismo dato, dos jerarquías. Unificado:
        // el band del pico debe contener píxeles ~Foreground (≥200 en los 3 canales; dim ≈ 161-170).
        using var bmp = new Bitmap(360, 160);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var s = Localization.Get("en");
        var t0 = new DateTime(2026, 6, 1);
        var data = new List<HistoryBucket>();
        double[] costs = { 1, 2, 10, 2, 1 };               // pico en el centro
        for (int i = 0; i < costs.Length; i++)
            data.Add(new HistoryBucket(t0.AddHours(i), $"{i:00}h",
                new Dictionary<string, double> { ["Opus"] = costs[i] }));

        const int x = 10, top = 20, w = 300;
        DashboardDataView.DrawSpendBody(g, draw: true, x, top, w, s, Theme.Dark, Typography.Caption,
            data, chartLoading: false, dim);

        // Banda del pico (etiqueta arriba, hacia el centro-derecha; el total dim queda a la izquierda).
        bool found = false;
        for (int py = top - 2; py <= top + 16 && !found; py++)
            for (int px = x + w / 3; px <= x + w && !found; px++)
            {
                var p = bmp.GetPixel(px, py);
                if (p.R >= 200 && p.G >= 200 && p.B >= 200) found = true;
            }
        Assert.True(found, "el pico en modo $ debe pintarse con Foreground (como el modo %), no con dim");
    }

    [Fact]
    public void Spend_body_measure_equals_paint_with_chart_data()
    {
        // Invariante medir==pintar: el filtro de etiquetas y el recolocado del pico solo actúan en la
        // pasada de pintado; ambas pasadas devuelven el MISMO y final.
        using var bmp = new Bitmap(360, 160);
        using var g = Graphics.FromImage(bmp);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var s = Localization.Get("en");
        var t0 = new DateTime(2026, 6, 1);
        var data = new List<HistoryBucket>();
        for (int i = 0; i < 12; i++)
            data.Add(new HistoryBucket(t0.AddHours(i), $"lun {i:00}h",
                new Dictionary<string, double> { ["Opus"] = 5 + i, ["Sonnet"] = 1, ["Haiku"] = 0.5 }));

        int measured = DashboardDataView.DrawSpendBody(g, draw: false, 10, 20, 300, s, Theme.Dark,
            Typography.Caption, data, chartLoading: false, dim);
        int painted = DashboardDataView.DrawSpendBody(g, draw: true, 10, 20, 300, s, Theme.Dark,
            Typography.Caption, data, chartLoading: false, dim);
        Assert.Equal(measured, painted);
    }

    // ================= T7a (§3 #5): tabs de rango 1H/5H/24H/7D/30D = chips DrawSegments =================

    // Llama a DrawChart con datos vacíos (las tabs se registran igual) y devuelve los 3 dicts de rects.
    private static (Dictionary<ChartRange, Rectangle> tabs, Dictionary<string, Rectangle> modes)
        DrawChartTabs(Graphics g, bool draw, Theme theme)
    {
        var s = Localization.Get("en");
        var cfg = new AppConfig();
        using var tabFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        var tabRects = new Dictionary<ChartRange, Rectangle>();
        var modeRects = new Dictionary<string, Rectangle>();
        var pctWinRects = new Dictionary<string, Rectangle>();
        using var dim = new SolidBrush(theme.TextSecondary);
        DashboardDataView.DrawChart(g, draw, 10, 20, 320, s, theme, cfg, Typography.Caption, tabFont,
            "spend", ChartRange.Hours5, "7d", new List<HistoryBucket>(), new List<PctPoint>(),
            chartLoading: false, tabRects, modeRects, pctWinRects, dim);
        return (tabRects, modeRects);
    }

    [Fact]
    public void Chart_range_tabs_share_chip_geometry_with_mode_segments()
    {
        // §3 #5: las tabs de rango tenían geometría propia (alto 20, radio 5, padX 6, gap 4) distinta
        // de los chips vecinos (Spend $/Quota % vía DrawSegments: alto 18, padX 7, gap 3). Unificadas:
        // mismo alto que los segmentos del modo y mismo gap entre chips.
        using var bmp = new Bitmap(360, 240);
        using var g = Graphics.FromImage(bmp);
        var (tabs, modes) = DrawChartTabs(g, draw: false, Theme.Dark);

        Assert.Equal(5, tabs.Count);
        Assert.All(tabs.Values, r => Assert.Equal(18, r.Height));
        Assert.All(modes.Values, r => Assert.Equal(18, r.Height));

        var ordered = tabs.Values.OrderBy(r => r.X).ToList();
        for (int i = 1; i < ordered.Count; i++)
            Assert.Equal(3, ordered[i].X - ordered[i - 1].Right);
    }

    [Fact]
    public void Chart_inactive_range_tab_shows_border_in_light_theme()
    {
        // §3 #5: el chip inactivo era BgElevated puro (#FFF sobre #FAFAFA → invisible en claro). Debe
        // llevar el borde Separator de DrawSegments: en el contorno superior del chip inactivo hay
        // píxeles ≈ Separator (≠ relleno y ≠ fondo).
        using var bmp = new Bitmap(360, 240);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Light.Background);
        var (tabs, _) = DrawChartTabs(g, draw: true, Theme.Light);

        var r = tabs[ChartRange.Hour1]; // 1H inactiva (la activa es 5H)
        var sep = Theme.Light.Separator;
        bool border = false;
        for (int px = r.X + 4; px <= r.Right - 4 && !border; px++)
        {
            var p = bmp.GetPixel(px, r.Y);
            if (Math.Abs(p.R - sep.R) <= 8 && Math.Abs(p.G - sep.G) <= 8 && Math.Abs(p.B - sep.B) <= 8)
                border = true;
        }
        Assert.True(border, "la tab de rango inactiva debe llevar el borde Separator (era invisible en claro)");
    }

    [Fact]
    public void Chart_tabs_measure_equals_paint()
    {
        // Invariante de 2 pasadas para DrawChart: medir y pintar registran los MISMOS rects de tabs.
        using var bmp = new Bitmap(360, 240);
        using var g = Graphics.FromImage(bmp);
        var (measured, _) = DrawChartTabs(g, draw: false, Theme.Dark);
        var (painted, _) = DrawChartTabs(g, draw: true, Theme.Dark);

        Assert.Equal(measured.Count, painted.Count);
        foreach (var (k, r) in measured) Assert.Equal(r, painted[k]);
    }

    // ================= T8b/T8c: nada atraviesa el valor right-aligned ni rebasa el ancho =================

    [Fact]
    public void RowWithRightValue_elides_the_left_text_before_the_value()
    {
        // §3 #14: el nombre de proyecto de una sesión en vivo atravesaba la etiqueta de fase. El layout
        // compartido elide el texto izquierdo con gutter Spacing.Md antes del valor right-aligned.
        using var bmp = new Bitmap(360, 60);
        using var g = Graphics.FromImage(bmp);
        const int x = 16, w = 200;
        const string longName = "proyecto-con-nombre-larguisimo-que-no-cabe-en-la-fila";

        var (shown, rightX) = DashboardDataView.RowWithRightValue(g, longName, "esperando aprobación",
            x, w, Typography.Caption, Typography.Caption);

        Assert.EndsWith(TextWrap.Ellipsis, shown);
        Assert.True(g.MeasureString(shown, Typography.Caption).Width <= rightX - Spacing.Md - x + 1,
            "el nombre elidido debe respetar el gutter Md antes de la fase");
        Assert.True(rightX <= x + w, $"el valor right-aligned arranca en {rightX}, fuera de x+w={x + w}");
    }

    [Fact]
    public void RowWithRightValue_keeps_short_text_intact()
    {
        using var bmp = new Bitmap(360, 60);
        using var g = Graphics.FromImage(bmp);

        var (shown, _) = DashboardDataView.RowWithRightValue(g, "tkb", "inactiva",
            16, 300, Typography.Caption, Typography.Caption);

        Assert.Equal("tkb", shown);
    }

    [Fact]
    public void LiveSessions_long_project_name_stays_within_the_row()
    {
        // Pixel-check del defecto original: con un nombre más ancho que la fila, nada se pinta a la
        // derecha de x+w (antes el DrawString sin acotar seguía de largo).
        using var bmp = new Bitmap(420, 80);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var s = Localization.Get("es");
        var view = new LiveSessionsView
        {
            Instances = new[]
            {
                new SessionState
                {
                    SessionId = "s1", Cwd = @"C:\x",
                    ProjectName = "proyecto-con-nombre-larguisimo-que-no-cabe-en-la-fila",
                    Phase = SessionPhase.Processing,
                }
            }
        };

        const int x = 16, y0 = 10, w = 200;
        DashboardDataView.DrawLiveSessionsBody(g, draw: true, view, s, x, y0, w,
            Typography.Caption, fg, dim, new Dictionary<string, Rectangle>());

        var bg = Theme.Dark.Background;
        for (int px = x + w + 2; px < bmp.Width; px++)
            for (int py = y0; py < y0 + 16; py++)
            {
                var p = bmp.GetPixel(px, py);
                Assert.True(p.R == bg.R && p.G == bg.G && p.B == bg.B,
                    $"píxel pintado en ({px},{py}), fuera del ancho w={w}: el nombre no se elide");
            }
    }

    [Fact]
    public void DrawPace_line_never_exceeds_the_content_width()
    {
        // T8c: la línea "↗ 5h 130% · 7d 95%   ⚠ jue 02:12" desbordaba el panel con locales/ETAs largos.
        using var bmp = new Bitmap(420, 60);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var s = Localization.Get("es");
        var eta = DateTimeOffset.UtcNow.AddHours(3);
        var snap = new AppSnapshot
        {
            PaceFive = new PaceResult("5h", 62, 1.30, 47.7, eta, null, true, PaceStatus.Critical),
            PaceSeven = new PaceResult("7d", 84, 0.95, 88.4, null, null, false, PaceStatus.Ok),
        };

        const int x = 16, y0 = 10, w = 100;
        DashboardDataView.DrawPace(g, draw: true, snap, s, Theme.Dark, x, y0, w, Typography.Caption);

        var bg = Theme.Dark.Background;
        for (int px = x + w + 2; px < bmp.Width; px++)
            for (int py = y0; py < y0 + 18; py++)
            {
                var p = bmp.GetPixel(px, py);
                Assert.True(p.R == bg.R && p.G == bg.G && p.B == bg.B,
                    $"píxel pintado en ({px},{py}), fuera del ancho w={w}: la línea de pace no se elide");
            }
    }

    [Fact]
    public void ModelLine_track_stops_before_the_percent_text()
    {
        // T3a (auditoría §3 #2): el track de la fila compacta cruzaba a todo el ancho por debajo del %
        // right-aligned y lo tachaba (el Mono de 12pt baja hasta la banda de la barrita). Ahora el track
        // se corta un gap ANTES del texto: la banda del gap debe ser fondo puro, sin píxeles de Track.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(12, DateTimeOffset.UtcNow);     // 12% como en el render auditado
        var cfg = new AppConfig();
        var culture = Localization.Get("en").Culture;

        const int x = 16, y0 = 30, w = 300;
        DashboardDataView.DrawModelLine(g, draw: true, "Sonnet 7d", win, x, y0, w,
            Typography.Caption, fg, dim, Theme.Dark, cfg, culture);

        // Mide el % igual que producción (T10: TextMetrics, tipográfico + Ceiling) para localizar
        // dónde arranca el texto right-aligned.
        string val = UsageFormat.Percent(win.UtilizationPct, culture);
        int textLeft = x + w - TextMetrics.MeasureWidth(g, val, Typography.Mono);

        var track = Theme.Dark.Track;
        for (int py = y0 + 16; py <= y0 + 19; py++)               // banda de la barrita (ModelBarH=4)
            for (int px = textLeft - 7; px <= textLeft - 1; px++) // el gap previo al texto
            {
                var p = bmp.GetPixel(px, py);
                bool isTrack = Math.Abs(p.R - track.R) <= 6 && Math.Abs(p.G - track.G) <= 6 && Math.Abs(p.B - track.B) <= 6;
                Assert.False(isTrack, $"track en ({px},{py}): la barrita sigue cruzando bajo el % (texto en x={textLeft})");
            }
    }

    // ================= T13a: series del gráfico = familias dinámicas de los DATOS =================

    [Fact]
    public void ChartFamilies_ordena_por_gasto_descendente()
    {
        var t0 = new DateTime(2026, 6, 1);
        var data = new List<HistoryBucket>
        {
            new(t0, "00h", new Dictionary<string, double> { ["Fable"] = 5, ["Opus"] = 1 }),
            new(t0.AddHours(1), "01h", new Dictionary<string, double> { ["Fable"] = 5, ["Sonnet"] = 3 }),
        };

        Assert.Equal(new[] { "Fable", "Sonnet", "Opus" }, DashboardDataView.ChartFamilies(data));
    }

    [Fact]
    public void ChartFamilies_familia_sin_registros_en_la_ventana_desaparece()
    {
        // Desaparición natural: nadie usó Haiku en la ventana → no hay serie ni leyenda fantasma.
        var t0 = new DateTime(2026, 6, 1);
        var data = new List<HistoryBucket>
        {
            new(t0, "00h", new Dictionary<string, double> { ["Opus"] = 2 }),
        };

        Assert.Equal(new[] { "Opus" }, DashboardDataView.ChartFamilies(data));
    }

    [Fact]
    public void ChartFamilies_familia_con_gasto_cero_no_genera_serie()
    {
        var t0 = new DateTime(2026, 6, 1);
        var data = new List<HistoryBucket>
        {
            new(t0, "00h", new Dictionary<string, double> { ["Opus"] = 2, ["Fable"] = 0 }),
        };

        Assert.DoesNotContain("Fable", DashboardDataView.ChartFamilies(data));
    }

    [Fact]
    public void Spend_body_pinta_una_familia_nueva_con_el_primer_slot_de_la_gama()
    {
        // Una familia desconocida ayer (Fable) y única en la ventana = 1.ª por gasto → slot 0 de
        // theme.ChartSeries. Antes ni se pintaba: la vista solo conocía Opus/Sonnet/Haiku/other.
        using var bmp = new Bitmap(360, 160);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var s = Localization.Get("en");
        var t0 = new DateTime(2026, 6, 1);
        var data = new List<HistoryBucket>();
        for (int i = 0; i < 5; i++)
            data.Add(new HistoryBucket(t0.AddHours(i), $"{i:00}h",
                new Dictionary<string, double> { ["Fable"] = 4 + i }));

        const int x = 10, top = 20, w = 300;
        DashboardDataView.DrawSpendBody(g, draw: true, x, top, w, s, Theme.Dark, Typography.Caption,
            data, chartLoading: false, dim);

        var c0 = Theme.Dark.ChartSeries[0];
        bool found = false;
        for (int py = top + 30; py < top + 90 && !found; py++)
            for (int px = x + 5; px < x + w - 5 && !found; px++)
            {
                var p = bmp.GetPixel(px, py);
                if (Math.Abs(p.R - c0.R) <= 8 && Math.Abs(p.G - c0.G) <= 8 && Math.Abs(p.B - c0.B) <= 8)
                    found = true;
            }
        Assert.True(found, "la serie de una familia nueva debe pintarse con ChartSeries[0] (rango 1.º)");
    }

    [Fact]
    public void FamilyLabel_localiza_la_familia_Otros_y_deja_el_resto_tal_cual()
    {
        Assert.Equal("Fable", DashboardDataView.FamilyLabel("Fable", Localization.Get("es")));
        Assert.Equal("Otros", DashboardDataView.FamilyLabel(ModelFamily.Other, Localization.Get("es")));
        Assert.Equal("Other", DashboardDataView.FamilyLabel(ModelFamily.Other, Localization.Get("en")));
    }

    // ================= T10: la columna derecha (QuotaBar % + % de modelo) queda alineada (§3 #16) =================

    [Fact]
    public void ModelLine_percent_aligns_flush_with_the_QuotaBar_percent_column()
    {
        // El defecto auditado: cada fila medía su valor right-aligned con un criterio distinto
        // ((int)/Ceiling/float, con o sin padding del formato default) y la columna quedaba "dentada"
        // (3-6px). Con la medida central, el % de la barra grande y el % de la mini-fila de modelo
        // terminan en la MISMA columna de píxeles (±1) y pegados a x+w.
        using var bmp = new Bitmap(420, 160);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var cfg = new AppConfig();
        var s = Localization.Get("en");
        var winBar = new UsageWindow(87, DateTimeOffset.UtcNow.AddHours(2));
        var winModel = new UsageWindow(12, DateTimeOffset.UtcNow);

        const int x = 16, yBar = 10, yModel = 90, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", winBar, pace: null, x, yBar, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);
        DashboardDataView.DrawModelLine(g, draw: true, "Sonnet 7d", winModel, x, yModel, w,
            Typography.Caption, fg, dim, Theme.Dark, cfg, s.Culture);

        int inkBar = TextMetricsTests.RightmostInk(bmp, Theme.Dark.Background, yBar, yBar + 18);
        int inkModel = TextMetricsTests.RightmostInk(bmp, Theme.Dark.Background, yModel, yModel + 16);
        Assert.True(inkBar >= 0 && inkModel >= 0, "no se pintó alguna de las dos filas");
        Assert.True(Math.Abs(inkBar - inkModel) <= 1,
            $"columna dentada: el % de la barra termina en {inkBar} y el de la mini-fila en {inkModel}");
        Assert.InRange(inkBar, x + w - 3, x + w);
        Assert.InRange(inkModel, x + w - 3, x + w);
    }

    // ================= T13b: fila de gasto de una familia SIN tarifa =================

    [Fact]
    public void Fila_sin_tarifa_muestra_em_dash_con_sufijo_localizado_no_cero()
    {
        // "No mentir con 0": una familia sin tarifa en ninguna fuente pinta "— <sufijo>" (string
        // localizada), nunca "$0.00"; las tarifadas siguen pintando su moneda con la cultura del idioma.
        var spend = new WindowStats();
        spend.CostByModel["Mythos"] = 0;
        spend.UnpricedModels.Add("Mythos");
        spend.CostByModel["Opus"] = 12.5;

        var es = Localization.Get("es");
        Assert.Equal("— " + es.SpendNoRate, DashboardDataView.SpendValueText(spend, "Mythos", es));
        Assert.Equal(UsageFormat.Money(12.5, es.Culture), DashboardDataView.SpendValueText(spend, "Opus", es));

        var en = Localization.Get("en");
        Assert.Equal("— " + en.SpendNoRate, DashboardDataView.SpendValueText(spend, "Mythos", en));
    }

    [Fact]
    public void Familia_con_gasto_tarifado_y_marcador_residual_muestra_su_dinero()
    {
        // El marcador unpriced solo manda si la familia NO tiene gasto tarifado (p.ej. "Otros" tras
        // fundir una familia sin tarifa junto a otra con gasto real): se enseña el $ real, no "—".
        var spend = new WindowStats();
        spend.CostByModel[ModelFamily.Other] = 3.2;
        spend.UnpricedModels.Add(ModelFamily.Other);

        var s = Localization.Get("en");
        Assert.Equal(UsageFormat.Money(3.2, s.Culture), DashboardDataView.SpendValueText(spend, ModelFamily.Other, s));
    }

    // ================= F5 (v0.3.9 g3): mini-filas de modelo con relleno NEUTRO (no por riesgo) =================

    [Fact]
    public void ModelBarFill_is_neutral_text_muted_not_a_risk_color()
    {
        // La mini-fila no tiene pace por modelo: su relleno deja de ir por RiskColor (verde/rojo) y pasa a
        // TextMuted, para no competir con el verde/rojo semántico de las barras grandes (tres semánticas).
        foreach (var theme in new[] { Theme.Dark, Theme.Light, Theme.Cli })
            Assert.Equal(theme.TextMuted, DashboardDataView.ModelBarFill(theme));
    }

    [Fact]
    public void ModelBarFill_does_not_match_RiskColor_at_critical_percent()
    {
        // Garantía explícita del fix: al 95% (crítico) el relleno NO es el rojo de RiskColor — es neutro.
        var cfg = new AppConfig();
        var risk = ColorMath.RiskColor(95, Theme.Dark, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
        Assert.NotEqual(risk, DashboardDataView.ModelBarFill(Theme.Dark));
    }

    [Fact]
    public void ModelLine_reference_bar_uses_neutral_fill_not_risk_color()
    {
        // Pixel-check: a un % crítico (90), la barrita NO contiene el rojo de RiskColor; sí el neutro.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(90, DateTimeOffset.UtcNow);   // 90% → relleno largo y visible
        var cfg = new AppConfig();
        var risk = ColorMath.RiskColor(90, Theme.Dark, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
        var neutral = DashboardDataView.ModelBarFill(Theme.Dark);

        const int x = 16, y0 = 30, w = 300;
        DashboardDataView.DrawModelLine(g, draw: true, "Opus 7d", win, x, y0, w,
            Typography.Caption, fg, dim, Theme.Dark, cfg, Localization.Get("en").Culture);

        // Banda de la barra (justo bajo la línea de texto, 16px): el track empieza en x.
        bool foundRisk = false, foundNeutral = false;
        for (int py = y0 + 16; py <= y0 + 19; py++)
            for (int px = x + 2; px < x + 30; px++)
            {
                var p = bmp.GetPixel(px, py);
                if (Math.Abs(p.R - risk.R) <= 10 && Math.Abs(p.G - risk.G) <= 10 && Math.Abs(p.B - risk.B) <= 10)
                    foundRisk = true;
                if (Math.Abs(p.R - neutral.R) <= 10 && Math.Abs(p.G - neutral.G) <= 10 && Math.Abs(p.B - neutral.B) <= 10)
                    foundNeutral = true;
            }
        Assert.False(foundRisk, "la mini-fila sigue pintando el relleno por RiskColor (rojo crítico)");
        Assert.True(foundNeutral, "la mini-fila debe pintar el relleno NEUTRO (TextMuted)");
    }

    // ================= F7 (v0.3.9 g3): fila de modelo bajo epsilon ⇒ % atenuado (TextMuted) =================

    [Fact]
    public void IsModelPctEmpty_only_below_the_epsilon()
    {
        Assert.True(DashboardDataView.IsModelPctEmpty(0));
        Assert.True(DashboardDataView.IsModelPctEmpty(0.4));
        Assert.False(DashboardDataView.IsModelPctEmpty(DashboardDataView.ModelEmptyEpsilonPct)); // 0.5 no es vacío
        Assert.False(DashboardDataView.IsModelPctEmpty(0.5));
        Assert.False(DashboardDataView.IsModelPctEmpty(12));
    }

    // Brillo MÁXIMO de la tinta del % (mitad derecha de la fila de texto). Discriminador robusto entre
    // foreground (TextPrimary ≈ 244) y dim (TextSecondary ≈ 161): un % atenuado NUNCA supera el brillo de
    // su token (~161), mientras que el de foreground alcanza ~244. Evita confundir los bordes AA (que
    // interpolan bg→color y pasan por el tono intermedio) con el color base, que sí confundía al contar.
    private static int ModelPctPeakBrightness(double util)
    {
        using var bmp = new Bitmap(420, 80);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(util, DateTimeOffset.UtcNow);
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        DashboardDataView.DrawModelLine(g, draw: true, "Sonnet 7d", win, x, y0, w,
            Typography.Caption, fg, dim, Theme.Dark, cfg, s.Culture);

        int peak = 0;
        for (int py = y0; py < y0 + 16; py++)
            for (int px = x + w / 2; px <= x + w; px++)
            {
                var p = bmp.GetPixel(px, py);
                peak = Math.Max(peak, Math.Max(p.R, Math.Max(p.G, p.B)));
            }
        return peak;
    }

    [Fact]
    public void Empty_model_row_paints_the_percent_muted_not_foreground()
    {
        // "Sonnet 7d 0%": el % va en TextMuted/dim, no en foreground — deja de resaltar el dato más vacío.
        // El brillo pico del % vacío queda bajo el del foreground (TextSecondary ≈ 161 < TextPrimary ≈ 244).
        int emptyPeak = ModelPctPeakBrightness(0);
        int usedPeak = ModelPctPeakBrightness(42);
        Assert.True(emptyPeak >= Theme.Dark.TextSecondary.R - 20,
            $"no se pintó el % de la fila vacía (pico={emptyPeak})");
        Assert.True(emptyPeak < Theme.Dark.TextPrimary.R - 30,
            $"el % de una fila vacía (0%) no debe llegar al brillo del foreground (pico={emptyPeak})");
        Assert.True(usedPeak - emptyPeak >= 40,
            $"el % vacío (pico={emptyPeak}) debe ser claramente más tenue que el de uso real (pico={usedPeak})");
    }

    [Fact]
    public void Used_model_row_paints_the_percent_in_foreground()
    {
        // Con uso real (42%) el % sí va en foreground (no se atenúa): la jerarquía se respeta — su brillo
        // pico alcanza el del TextPrimary (~244), no el dim (~161).
        int usedPeak = ModelPctPeakBrightness(42);
        Assert.True(usedPeak >= Theme.Dark.TextPrimary.R - 20,
            $"el % de una fila con uso real (42%) debe pintarse en foreground (pico={usedPeak})");
    }

    // ================= F6 (v0.3.9 g3): línea de pace — flecha + color POR SEGMENTO + grupo ETA =================

    [Fact]
    public void PaceArrow_is_steady_when_on_pace_and_rising_when_over()
    {
        Assert.Equal("→", DashboardDataView.PaceArrow(PaceStatus.Ok));      // por debajo/en ritmo: no acelera
        Assert.Equal("↗", DashboardDataView.PaceArrow(PaceStatus.Over));    // acelerado
        Assert.Equal("↗", DashboardDataView.PaceArrow(PaceStatus.Critical));
    }

    [Fact]
    public void PaceSegments_color_each_window_by_its_own_status()
    {
        // El caso minor de la auditoría: 5h crítica (212%) y 7d SANA (77%). Antes TODA la línea iba del
        // color de la peor ventana ⇒ la 7d salía roja. Ahora cada segmento lleva SU color: el 7d en Ok.
        var pf = new PaceResult("5h", 90, 2.12, 42, null, null, false, PaceStatus.Critical);
        var ps = new PaceResult("7d", 77, 0.77, 100, null, null, false, PaceStatus.Ok);
        var segs = DashboardDataView.PaceSegments(pf, ps, Localization.Get("en"), Theme.Dark, Theme.Dark.TextSecondary);

        var seg5 = segs.First(t => t.text.Contains("5h"));
        var seg7 = segs.First(t => t.text.Contains("7d"));
        Assert.Equal(Theme.Dark.CriticalText, seg5.color);     // 5h crítica → CriticalText
        Assert.Equal(Theme.Dark.Ok, seg7.color);               // 7d sana → Ok (no rojo)
    }

    [Fact]
    public void PaceSegments_arrow_follows_each_window_status()
    {
        // La flecha por segmento: la ventana acelerada lleva ↗, la que va en ritmo →.
        var pf = new PaceResult("5h", 90, 1.5, 42, null, null, false, PaceStatus.Over);
        var ps = new PaceResult("7d", 50, 0.6, 80, null, null, false, PaceStatus.Ok);
        var segs = DashboardDataView.PaceSegments(pf, ps, Localization.Get("en"), Theme.Dark, Theme.Dark.TextSecondary);

        Assert.StartsWith("↗", segs.First(t => t.text.Contains("5h")).text);
        Assert.StartsWith("→", segs.First(t => t.text.Contains("7d")).text);
    }

    [Fact]
    public void PaceSegments_eta_group_uses_localized_label_and_warn_separator()
    {
        // El grupo ETA: separador " · " + "⚠ <rótulo localizado> <hora>", coloreado por la ventana que se
        // agota (no por la peor de la línea). El rótulo viene de Strings.PaceEtaLabel (9 idiomas).
        var eta = DateTimeOffset.UtcNow.AddHours(3);
        var pf = new PaceResult("5h", 90, 1.5, 42, eta, null, true, PaceStatus.Over);
        var ps = new PaceResult("7d", 50, 0.6, 80, null, null, false, PaceStatus.Ok);
        var es = Localization.Get("es");
        var segs = DashboardDataView.PaceSegments(pf, ps, es, Theme.Dark, Theme.Dark.TextSecondary);

        var etaSeg = segs.First(t => t.text.Contains("⚠"));
        Assert.Contains(es.PaceEtaLabel, etaSeg.text);                 // micro-rótulo localizado ("se agota")
        Assert.Contains(UsageFormat.ResetAbsolute(eta, es.Culture), etaSeg.text);
        Assert.Equal(Theme.Dark.WarnText, etaSeg.color);              // color de la ventana que se agota (Over)
        Assert.Contains(segs, t => t.text == " · " && t.color == Theme.Dark.TextSecondary); // separador neutro
    }

    [Fact]
    public void DrawPace_measure_equals_paint_with_two_windows_and_eta()
    {
        // Invariante de 2 pasadas tras la reescritura por segmentos: medir y pintar avanzan el mismo y.
        using var bmp = new Bitmap(420, 60);
        using var g = Graphics.FromImage(bmp);
        var s = Localization.Get("es");
        var eta = DateTimeOffset.UtcNow.AddHours(3);
        var snap = new AppSnapshot
        {
            PaceFive = new PaceResult("5h", 90, 2.12, 42, eta, null, true, PaceStatus.Critical),
            PaceSeven = new PaceResult("7d", 77, 0.77, 100, null, null, false, PaceStatus.Ok),
        };

        int measured = DashboardDataView.DrawPace(g, draw: false, snap, s, Theme.Dark, 16, 10, 380, Typography.Caption);
        int painted = DashboardDataView.DrawPace(g, draw: true, snap, s, Theme.Dark, 16, 10, 380, Typography.Caption);
        Assert.Equal(measured, painted);
    }

    [Fact]
    public void DrawPace_segments_never_exceed_the_content_width()
    {
        // Invariante T8c conservado tras pasar a segmentos: nada se pinta más allá de x+w en una fila estrecha.
        using var bmp = new Bitmap(420, 60);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var s = Localization.Get("es");
        var eta = DateTimeOffset.UtcNow.AddHours(3);
        var snap = new AppSnapshot
        {
            PaceFive = new PaceResult("5h", 62, 1.30, 47.7, eta, null, true, PaceStatus.Critical),
            PaceSeven = new PaceResult("7d", 84, 0.95, 88.4, null, null, false, PaceStatus.Ok),
        };

        const int x = 16, y0 = 10, w = 90;
        DashboardDataView.DrawPace(g, draw: true, snap, s, Theme.Dark, x, y0, w, Typography.Caption);

        var bg = Theme.Dark.Background;
        for (int px = x + w + 2; px < bmp.Width; px++)
            for (int py = y0; py < y0 + 18; py++)
            {
                var p = bmp.GetPixel(px, py);
                Assert.True(p.R == bg.R && p.G == bg.G && p.B == bg.B,
                    $"píxel pintado en ({px},{py}), fuera del ancho w={w}: los segmentos de pace rebasan el panel");
            }
    }

    [Fact]
    public void PaceEtaLabel_exists_in_all_nine_languages()
    {
        // Invariante i18n: el micro-rótulo del ETA está poblado en los 9 idiomas (no cae a vacío en ninguno).
        foreach (var code in Localization.LanguageCycle)
        {
            var s = code == "system" ? Localization.Get("en") : Localization.Get(code);
            Assert.False(string.IsNullOrWhiteSpace(s.PaceEtaLabel), $"PaceEtaLabel vacío en idioma '{code}'");
        }
    }

    // ================= F13 (v0.3.9 g3): gridlines de umbral en modo Cuota% =================

    [Fact]
    public void ThresholdY_projects_percent_like_the_data_line()
    {
        // 0% ⇒ bottom; 100% ⇒ bottom-(chartH-headroom); 50% ⇒ punto medio. Misma fórmula que Y(v).
        const int bottom = 200, chartH = 92, headroom = 14;
        Assert.Equal(bottom, DashboardDataView.ThresholdY(bottom, chartH, headroom, 0));
        Assert.Equal(bottom - (chartH - headroom), DashboardDataView.ThresholdY(bottom, chartH, headroom, 100));
        Assert.Equal(bottom - (chartH - headroom) / 2f, DashboardDataView.ThresholdY(bottom, chartH, headroom, 50));
    }

    [Fact]
    public void ThresholdY_warn_is_below_critical_on_screen()
    {
        // Warn (70%) queda MÁS ABAJO en pantalla (mayor Y) que Critical (90%): el plot crece hacia arriba.
        const int bottom = 200, chartH = 92, headroom = 14;
        float warn = DashboardDataView.ThresholdY(bottom, chartH, headroom, 70);
        float crit = DashboardDataView.ThresholdY(bottom, chartH, headroom, 90);
        Assert.True(warn > crit, "la gridline de warn debe quedar bajo la de critical en pantalla");
    }

    [Fact]
    public void Percent_mode_draws_threshold_gridlines()
    {
        // F13: en modo % hay gridlines sutiles (theme.Separator) a la altura de Warn y Crit. Antes no había
        // ninguna línea de umbral: no se veía el cruce warn/crit.
        using var bmp = new Bitmap(360, 200);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var s = Localization.Get("en");
        var cfg = new AppConfig();
        var t0 = DateTime.UtcNow.AddHours(-4);
        var pctData = new List<PctPoint>();
        for (int i = 0; i < 6; i++) pctData.Add(new PctPoint(t0.AddHours(i), 30 + i, 20 + i)); // valores bajos

        const int x = 10, top = 20, w = 320;
        DashboardDataView.DrawPercentBody(g, draw: true, x, top, w, s, Theme.Dark, cfg,
            Typography.Caption, ChartRange.Hours5, "5h", pctData, chartLoading: false, dim);

        int bottom = top + DashboardDataView.ChartH;
        int headroom = Dpi.Scale(14);
        var sep = Theme.Dark.Separator;
        foreach (double th in new[] { cfg.WarnThresholdPct, cfg.CriticalThresholdPct })
        {
            int gy = (int)Math.Round(DashboardDataView.ThresholdY(bottom, DashboardDataView.ChartH, headroom, th));
            bool found = false;
            // La gridline es punteada: busca un pixel Separator en su banda [gy-1, gy+1] por todo el ancho.
            for (int py = gy - 1; py <= gy + 1 && !found; py++)
                for (int px = x + 5; px <= x + w - 5 && !found; px++)
                {
                    var p = bmp.GetPixel(px, py);
                    if (Math.Abs(p.R - sep.R) <= 10 && Math.Abs(p.G - sep.G) <= 10 && Math.Abs(p.B - sep.B) <= 10)
                        found = true;
                }
            Assert.True(found, $"falta la gridline de umbral en {th}% (modo Cuota%)");
        }
    }

    [Fact]
    public void Spend_mode_has_no_threshold_gridlines()
    {
        // Acotación del fix: el modo $ NO recibe gridlines de umbral (solo el modo %). Comprobamos que el
        // body de gasto no las pinta (no hay Separator horizontal continuo en la banda de un umbral).
        using var bmp = new Bitmap(360, 200);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var s = Localization.Get("en");
        var t0 = new DateTime(2026, 6, 1);
        var data = new List<HistoryBucket>();
        for (int i = 0; i < 5; i++)
            data.Add(new HistoryBucket(t0.AddHours(i), $"{i:00}h",
                new Dictionary<string, double> { ["Opus"] = 4 + i }));

        const int x = 10, top = 20, w = 320;
        DashboardDataView.DrawSpendBody(g, draw: true, x, top, w, s, Theme.Dark, Typography.Caption,
            data, chartLoading: false, dim);

        int bottom = top + DashboardDataView.ChartH;
        int headroom = Dpi.Scale(14);
        var sep = Theme.Dark.Separator;
        int gy = (int)Math.Round(DashboardDataView.ThresholdY(bottom, DashboardDataView.ChartH, headroom, 70));
        int sepCount = 0;
        for (int px = x + 5; px <= x + w - 5; px++)
        {
            var p = bmp.GetPixel(px, gy);
            if (Math.Abs(p.R - sep.R) <= 10 && Math.Abs(p.G - sep.G) <= 10 && Math.Abs(p.B - sep.B) <= 10)
                sepCount++;
        }
        Assert.True(sepCount < (w - 10) / 3, "el modo $ no debe pintar una gridline de umbral horizontal");
    }
}
