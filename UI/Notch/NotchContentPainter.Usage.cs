using System.Drawing.Drawing2D;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Página Uso: a la izquierda los números (gratis siempre): tarjetas de 5 h y 7 d con su anillo, cuenta atrás y ritmo, cuota
/// por modelo y uso extra; arriba el plan y la salud del servicio. A la derecha la gráfica: gasto equivalente por modelo
/// (áreas apiladas) o % real de la cuota (líneas con los umbrales de los avisos y la proyección del ritmo), con su barra
/// (modo y rangos) y su leyenda. Hover: raya, puntos y tooltip. Todo llega calculado y formateado en <see cref="UsageView"/>
/// (geometría de <see cref="UsageLayout"/>, la misma del clic): aquí solo se pinta. Pinceles, lápices, trazos y fuentes
/// propios se reutilizan (sin reservas por frame salvo los rectángulos redondeados, como el resto del painter).
/// </summary>
public sealed partial class NotchContentPainter
{
    // Superficies translúcidas sobre el negro de la pastilla.
    private static readonly Color UsageTileFill = Color.FromArgb(14, 255, 255, 255);
    private static readonly Color UsageTileFocus = Color.FromArgb(26, 255, 255, 255);
    private static readonly Color UsageTrackFill = Color.FromArgb(18, 255, 255, 255);
    private static readonly Color UsageSegmentOn = Color.FromArgb(44, 255, 255, 255);
    private static readonly Color UsageGridLine = Color.FromArgb(22, 255, 255, 255);
    private static readonly Color UsageNowLine = Color.FromArgb(46, 255, 255, 255);
    private static readonly Color UsageHoverLine = Color.FromArgb(110, 255, 255, 255);
    /// <summary>Opacidad del conector discontinuo que salva un hueco de la curva del %.</summary>
    private const int UsageGapAlpha = 110;
    private static readonly Color UsageTipFill = Color.FromArgb(244, 28, 28, 30);
    private static readonly Color UsageTipBorder = Color.FromArgb(34, 255, 255, 255);
    private static readonly Color UsagePlanBorder = Color.FromArgb(60, 255, 255, 255);

    /// <summary>Alfa de las franjas apiladas y del área bajo las líneas (arriba → abajo).</summary>
    private const int UsageBandAlpha = 200, UsageAreaTop = 90, UsageAreaBottom = 0;
    private const float UsageRingStroke = 3.5f, UsageHiddenDim = 0.4f, UsageStaleDim = 0.3f, UsageTipPad = 7f, UsageTipRow = 14f, UsageDot = 3.5f,
        UsageAxisCell = 64f;

    private Font? _usageNum;                       // % dentro de los anillos de las tarjetas
    private float _usageNumScale = -1f;
    private readonly PointF[] _usageBand = new PointF[2 * UsageShape.Samples];
    private readonly PointF[] _usageLine = new PointF[UsageShape.Samples];
    private readonly PointF[]?[] _usageRuns = new PointF[]?[UsageShape.Samples + 3];   // tramos entre huecos, por largo
    private readonly Pen _usageDash = new(Color.White) { DashStyle = DashStyle.Dash };
    private readonly float[] _usageDashPattern = new float[2];
    // Alturas del glifo del estado vacío (barras discretas; la de la cuota, creciente).
    private static readonly float[] EmptyQuotaBars = { 0.35f, 0.55f, 0.8f, 1f }, EmptySpendBars = { 0.6f, 1f, 0.45f, 0.8f };
    private LinearGradientBrush? _usageArea;
    private Color _usageAreaTop, _usageAreaBottom;
    private RectangleF _usageAreaRect;
    // Medida del tooltip: se mide una vez por tooltip y escala (los textos ya vienen hechos).
    private UsageTip? _tipMeasured;
    private float _tipScale = -1f;
    private SizeF _tipSize;
    private float _tipValueW;

    /// <summary>Colores de las series (los de la gráfica del panel en el tema oscuro: la pastilla siempre es negra).</summary>
    private Color UsageColor(int slot) => _t.ChartSeries[((slot % _t.ChartSeries.Count) + _t.ChartSeries.Count) % _t.ChartSeries.Count];

    private void PaintUsagePage(Graphics g, RectangleF inner, float s, UsageView? v)
    {
        var r = UsageLayout.Compute(inner, s);
        if (v is null)
        {
            DrawText(g, Strings.NotchPageUsage, _title!, _t.Foreground, r.Header, _left);
            DrawText(g, Strings.Loading, _body!, _t.TextMuted, RectangleF.FromLTRB(r.Header.Left, r.Header.Bottom, r.Header.Right,
                r.Legend.Bottom), _center);
            return;
        }
        EnsureUsageFonts(s);
        UsageHeader(g, r, s, v.Numbers);
        for (int i = 0; i < v.Numbers.Tiles.Count && i < 2; i++) UsageTileCard(g, r, i, s, v.Numbers.Tiles[i], v);
        for (int i = 0; i < v.Numbers.Rows.Count && i < UsageLayout.Rows; i++) UsageRowLine(g, r.Row(i), s, v.Numbers.Rows[i]);
        UsageToolbar(g, r, s, v);
        UsageChartArea(g, r, s, v);
    }

    // ---------------------------------------------------------------- números

    /// <summary>«Uso», el plan en una cápsula con borde y, a la derecha, la salud del servicio con su punto de color.</summary>
    private void UsageHeader(Graphics g, UsageRects r, float s, UsageNumbers n)
    {
        var h = r.Header;
        float tw = g.MeasureString(Strings.NotchPageUsage, _title!, int.MaxValue, _left).Width;
        float right = h.Right;
        if (n.Health is { } health)
        {
            float hw = Math.Min(h.Width * 0.4f, TextW(g, health) + 1f);
            DrawText(g, health, _caption!, _t.TextMuted, RectangleF.FromLTRB(right - hw, h.Top, right, h.Bottom), _right);
            float d = 6f * s, cx = right - hw - 4f * s - d;
            _brush.Color = Fade(n.HealthLevel switch
            {
                HealthLevel.Operational => _t.Ok,
                HealthLevel.Degraded => _t.Warn,
                _ => _t.Critical,
            });
            g.FillEllipse(_brush, cx, h.Top + (h.Height - d) / 2f, d, d);
            right = cx - Gap * s;
        }
        DrawText(g, Strings.NotchPageUsage, _title!, _t.Foreground, RectangleF.FromLTRB(h.Left, h.Top, Math.Min(right, h.Left + tw + 1f),
            h.Bottom), _left);
        if (n.Plan is not { } plan) return;
        float px = h.Left + tw + Gap * s, ph = 16f * s;
        float pw = Math.Min(Math.Max(0f, right - px), TextW(g, plan) + 14f * s);
        if (pw < 24f * s) return;
        var chip = new RectangleF(px, h.Top + (h.Height - ph) / 2f, pw, ph);
        using (var path = RoundedRect(Rectangle.Round(chip), chip.Height / 2f))
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            SetPen(UsagePlanBorder, Math.Max(1f, s));
            g.DrawPath(_pen, path);
            g.Restore(state);
        }
        DrawText(g, plan, _caption!, _t.TextMuted, RectangleF.Inflate(chip, -6f * s, 0f), _center);
    }

    /// <summary>Tarjeta de una ventana: fondo (más claro la destacada o con el ratón encima), anillo grueso con el % dentro y
    /// tres líneas: nombre, cuenta atrás del reinicio y ritmo (en su color).</summary>
    private void UsageTileCard(Graphics g, UsageRects r, int i, float s, UsageTile t, UsageView v)
    {
        var box = r.Tile(i);
        bool hover = v.Hover.Control == UsageControl.Tile && v.Hover.Index == i && v.HoverIntensity > 0f;
        bool pressed = v.Pressed.Control == UsageControl.Tile && v.Pressed.Index == i;
        var fill = t.Focused ? UsageTileFocus : UsageTileFill;
        if (hover) fill = Color.FromArgb(Math.Min(255, fill.A + (int)(MusicHoverFill.A * 0.6f * Math.Clamp(v.HoverIntensity, 0f, 1f))), fill);
        if (pressed)
        {
            box = RectangleF.Inflate(box, -Math.Max(1f, MathF.Round(s)), -Math.Max(1f, MathF.Round(s)));
            fill = Darken(fill, MusicPressDarken);
        }
        using (var path = RoundedRect(Rectangle.Round(box), 9f * s))
        {
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }

        var ring = r.Ring(i);
        float w = UsageRingStroke * s;
        var arc = RectangleF.Inflate(ring, -w / 2f, -w / 2f);
        SetPen(Color.FromArgb((int)(255 * NotchMotion.RingTrackAlpha), _t.Foreground), w);
        g.DrawEllipse(_pen, arc);
        if (t.Value is { } pct && pct > 0)
        {
            SetPen(QuotaColor(pct, text: false), w);
            g.DrawArc(_pen, arc, -90f, (float)(Math.Clamp(pct, 0, 100) * 3.6));
        }
        DrawText(g, t.Pct, _usageNum!, t.Value is { } p2 ? QuotaColor(p2, text: true) : _t.TextMuted, RectangleF.Inflate(ring, 2f * s, 0f),
            _center);

        var text = r.TileText(i);
        float line = text.Height / 3f;
        DrawText(g, t.Title, _body!, _t.Foreground, new RectangleF(text.Left, text.Top, text.Width, line), _left);
        if (t.Reset is { } reset)
            DrawText(g, reset, _caption!, _t.TextMuted, new RectangleF(text.Left, text.Top + line, text.Width, line), _left);
        if (t.PaceText is { } pace)
            DrawText(g, pace, _caption!, t.Pace is { } st ? Theme.PaceTextColor(_t, st) : _t.TextMuted,
                new RectangleF(text.Left, text.Top + 2 * line, text.Width, line), _left);
    }

    /// <summary>Fila bajo las tarjetas: nombre y % (con una barrita de 2 px abajo) o el uso extra, con un punto del acento.</summary>
    private void UsageRowLine(Graphics g, RectangleF row, float s, UsageRow x)
    {
        if (x.Accent)
        {
            float d = 6f * s;
            _brush.Color = Fade(_t.Accent);
            g.FillEllipse(_brush, row.Left, row.Top + (row.Height - d) / 2f, d, d);
            DrawText(g, x.Label, _caption!, _t.Foreground, RectangleF.FromLTRB(row.Left + d + 6f * s, row.Top, row.Right, row.Bottom), _left);
            return;
        }
        float barH = Math.Max(1f, 2f * s);
        var text = RectangleF.FromLTRB(row.Left, row.Top, row.Right, row.Bottom - barH);
        DrawText(g, x.Label, _caption!, _t.TextMuted, text, _left);
        if (x.Pct is { } pct)
            DrawText(g, pct, _caption!, x.Value is { } v && v >= 1 ? QuotaColor(v, text: true) : _t.TextMuted, text, _right);
        var bar = new RectangleF(row.Left, row.Bottom - barH, row.Width, barH);
        _brush.Color = Fade(_t.Track);
        g.FillRectangle(_brush, bar);
        if (x.Value is { } val && val > 0)
        {
            _brush.Color = Fade(QuotaColor(val, text: false));
            g.FillRectangle(_brush, bar.Left, bar.Top, bar.Width * (float)(Math.Clamp(val, 0, 100) / 100), bar.Height);
        }
    }

    // ---------------------------------------------------------------- barra de la gráfica

    /// <summary>Selector Gasto | Cuota (cápsula con el elegido más claro) y los rangos (o, sin el Pro, el rango en texto y el
    /// chip «Pro»).</summary>
    private void UsageToolbar(Graphics g, UsageRects r, float s, UsageView v)
    {
        if (v.Modes) UsageModes(g, r, s, v);
        UsageRangesBar(g, r, s, v);
    }

    /// <summary>Selector Gasto | Cuota: cápsula con el elegido más claro.</summary>
    private void UsageModes(Graphics g, UsageRects r, float s, UsageView v)
    {
        var track = RectangleF.FromLTRB(r.Segment(0).Left, r.Toolbar.Top, r.Segment(1).Right, r.Toolbar.Bottom);
        using (var path = RoundedRect(Rectangle.Round(track), track.Height / 2f))
        {
            _brush.Color = Fade(UsageTrackFill);
            g.FillPath(_brush, path);
        }
        for (int i = 0; i < 2; i++)
        {
            bool on = (int)v.Mode == i;
            var seg = RectangleF.Inflate(r.Segment(i), -2f * s, -2f * s);
            bool hover = !on && v.Hover.Control == UsageControl.Segment && v.Hover.Index == i && v.HoverIntensity > 0f;
            if (on || hover)
            {
                var fill = on ? UsageSegmentOn : Color.FromArgb((int)(MusicHoverFill.A * Math.Clamp(v.HoverIntensity, 0f, 1f)), 255, 255, 255);
                if (v.Pressed.Control == UsageControl.Segment && v.Pressed.Index == i) fill = Darken(fill, MusicPressDarken);
                using var path = RoundedRect(Rectangle.Round(seg), seg.Height / 2f);
                _brush.Color = Fade(fill);
                g.FillPath(_brush, path);
            }
            DrawText(g, i == 0 ? Strings.ChartTabSpend : Strings.ChartTabPct, _caption!, on ? _t.Foreground : _t.TextMuted, seg, _center);
        }
    }

    /// <summary>Los rangos o, sin el Pro, el rango en texto y el chip «Pro».</summary>
    private void UsageRangesBar(Graphics g, UsageRects r, float s, UsageView v)
    {
        if (v.Locked)
        {
            DrawText(g, UsageRanges.Label(v.Range), _caption!, _t.TextMuted, r.RangeLabel, _right);
            ProChip(g, r.ProChip, s);
            return;
        }
        for (int i = 0; i < UsageRanges.All.Count; i++)
        {
            var range = UsageRanges.All[i];
            bool on = range == v.Range;
            var chip = r.Chip(i);
            bool hover = v.Interactive && v.Hover.Control == UsageControl.Range && v.Hover.Index == i && v.HoverIntensity > 0f;
            bool pressed = v.Interactive && v.Pressed.Control == UsageControl.Range && v.Pressed.Index == i;
            if (pressed) chip = RectangleF.Inflate(chip, -Math.Max(1f, MathF.Round(s)), -Math.Max(1f, MathF.Round(s)));
            int a = (on ? LyricsButtonOn.A : 0) + (hover ? (int)(MusicHoverFill.A * Math.Clamp(v.HoverIntensity, 0f, 1f)) : 0);
            if (a > 0)
            {
                var fill = Color.FromArgb(Math.Min(255, a), 255, 255, 255);
                if (pressed) fill = Darken(fill, MusicPressDarken);
                using var path = RoundedRect(Rectangle.Round(chip), chip.Height / 2f);
                _brush.Color = Fade(fill);
                g.FillPath(_brush, path);
            }
            DrawText(g, UsageRanges.Label(range), _small!, on ? _t.Foreground : _t.TextMuted, chip, _center);
        }
    }

    // ---------------------------------------------------------------- gráfica

    /// <summary>
    /// La gráfica con su transición: quieta; transformándose (la forma intermedia con la leyenda y los umbrales nuevos; las
    /// etiquetas de los ejes se cruzan EN SERIE, la vieja en la primera mitad y la nueva en la segunda, para que dos textos
    /// distintos no se pisen en el mismo sitio; la proyección entra fundiéndose); o fundiéndose una en otra (cambio de modo, o
    /// con «reducir movimiento»).
    /// </summary>
    private void UsageChartArea(Graphics g, UsageRects r, float s, UsageView v)
    {
        if (v.Status != UsageChartStatus.Ready && !v.Chart.Loading)
        {
            // La del rango anterior mientras llega la del elegido: atenuada, sin hover y con lo que pasa encima.
            float saved = _alpha;
            _alpha = Quantize(saved * UsageStaleDim);
            try { UsageChartLayer(g, r, s, v, v.Chart, v.Chart.Shape, 1f, 1f, 1f, 1f, hover: false); }
            finally { _alpha = saved; }
            DrawText(g, UsageStatusText(v.Status), _caption!, v.Status == UsageChartStatus.Failed ? _t.WarnText : _t.Foreground,
                r.Plot, _center);
            return;
        }
        float p = Math.Clamp(v.Progress, 0f, 1f);
        switch (v.Morph)
        {
            case UsageMorphKind.Fade:
                if (v.Previous is { } prev) UsageChartLayer(g, r, s, v, prev, prev.Shape, 1f - p, 1f - p, 1f - p, 1f - p, hover: false);
                UsageChartLayer(g, r, s, v, v.Chart, v.Chart.Shape, p, p, p, p, hover: false);
                break;
            case UsageMorphKind.Morph:
                if (v.Previous is { Loading: false, Empty: false } from)
                {
                    float saved = _alpha;
                    _alpha = Quantize(saved * Math.Max(0f, 1f - 2f * p));
                    if (_alpha > 0f) UsageAxisLabels(g, r.Plot, s, from);
                    _alpha = saved;
                }
                UsageChartLayer(g, r, s, v, v.Chart, v.Shape ?? v.Chart.Shape, 1f, 1f, Math.Max(0f, 2f * p - 1f), p, hover: false);
                break;
            default:
                UsageChartLayer(g, r, s, v, v.Chart, v.Chart.Shape, 1f, 1f, 1f, 1f, hover: true);
                break;
        }
    }

    /// <summary>Una gráfica con sus alfas: la forma (<paramref name="alpha"/>), guías, umbrales y leyenda
    /// (<paramref name="marksAlpha"/>), etiquetas de los ejes (<paramref name="labelsAlpha"/>) y proyección
    /// (<paramref name="projectionAlpha"/>).</summary>
    private void UsageChartLayer(Graphics g, UsageRects r, float s, UsageView v, UsageChartModel m, UsageShape shape, float alpha,
        float marksAlpha, float labelsAlpha, float projectionAlpha, bool hover)
    {
        float saved = _alpha;
        try
        {
            _alpha = Quantize(saved * alpha);
            if (_alpha <= 0f && Quantize(saved * marksAlpha) <= 0f) return;
            if (m.Loading || m.Empty)
            {
                UsageEmpty(g, r, s, m, ReferenceEquals(m, v.Chart) ? v.Status : UsageChartStatus.Ready);
                return;
            }
            var plot = r.Plot;
            _alpha = Quantize(saved * marksAlpha);
            UsageGridLines(g, plot, s, m);
            _alpha = Quantize(saved * alpha);
            var state = g.Save();
            try
            {
                g.SetClip(RectangleF.Inflate(plot, 0f, Math.Max(1f, s)), CombineMode.Intersect);
                if (shape.Stacked) UsageBands(g, plot, s, shape);
                else UsageLines(g, plot, s, shape, m);
            }
            finally { g.Restore(state); }
            _alpha = Quantize(saved * marksAlpha);
            UsageThresholds(g, plot, s, m);
            _alpha = Quantize(saved * projectionAlpha);
            if (_alpha > 0f) UsageProjectionMarks(g, plot, s, m);
            _alpha = Quantize(saved * labelsAlpha);
            if (_alpha > 0f) UsageAxisLabels(g, plot, s, m);
            _alpha = Quantize(saved * marksAlpha);
            UsageLegend(g, r, s, m, v);
            _alpha = saved;
            if (hover && v.Interactive && v.Bucket >= 0f && v.TipAlpha > 0f && m.Count > 0) UsageHover(g, r, s, m, v);
        }
        finally { _alpha = saved; }
    }

    /// <summary>Líneas de guía muy tenues (la base y una por marca), debajo de las series.</summary>
    private void UsageGridLines(Graphics g, RectangleF plot, float s, UsageChartModel m)
    {
        float lw = Math.Max(1f, s);
        _brush.Color = Fade(UsageGridLine);
        g.FillRectangle(_brush, plot.Left, plot.Bottom - lw, plot.Width, lw);   // base
        foreach (var t in m.YTicks)
            g.FillRectangle(_brush, plot.Left, MathF.Round(plot.Bottom - t.Fraction * plot.Height), plot.Width, lw);
    }

    /// <summary>Valores de las guías (colgando bajo su línea, a la derecha: el de arriba no choca con la barra), el eje de
    /// tiempo (cada hora centrada en su marca sin salirse de la gráfica) y la esquina (el total del gasto), encima de las series.</summary>
    private void UsageAxisLabels(Graphics g, RectangleF plot, float s, UsageChartModel m)
    {
        foreach (var t in m.YTicks)
        {
            float y = MathF.Round(plot.Bottom - t.Fraction * plot.Height);
            DrawText(g, t.Label, _small!, _t.TextMuted, new RectangleF(plot.Left, y + 1f * s, plot.Width - 2f * s, 12f * s), _right);
        }
        if (m.Corner is { } corner)
            DrawText(g, corner, _small!, _t.TextMuted, new RectangleF(plot.Left + 2f * s, plot.Top + 1f * s, plot.Width * 0.6f, 12f * s), _left);
        float cw = Math.Min(UsageAxisCell * s, plot.Width / 2f);
        foreach (var t in m.XTicks)
        {
            float x = Math.Clamp(plot.Left + t.Fraction * plot.Width - cw / 2f, plot.Left, plot.Right - cw);
            DrawText(g, t.Label, _small!, _t.TextMuted, new RectangleF(x, plot.Bottom, cw, UsageLayout.Axis * s), _center);
        }
    }

    /// <summary>Gasto apilado: cada franja entre su borde de abajo y el de arriba, con un filo más claro arriba.</summary>
    private void UsageBands(Graphics g, RectangleF plot, float s, UsageShape shape)
    {
        int n = UsageShape.Samples;
        for (int k = 0; k < shape.Keys.Length; k++)
        {
            var lower = shape.Lower[k];
            var upper = shape.Upper[k];
            bool any = false;
            for (int m = 0; m < n && !any; m++) any = upper[m] - lower[m] > 0.0005f;
            if (!any) continue;
            for (int m = 0; m < n; m++)
            {
                float x = plot.Left + shape.X(m) * plot.Width;
                _usageBand[m] = new PointF(x, plot.Bottom - upper[m] * plot.Height);
                _usageBand[2 * n - 1 - m] = new PointF(x, plot.Bottom - lower[m] * plot.Height);
                _usageLine[m] = _usageBand[m];
            }
            var c = UsageColor(shape.Colors[k]);
            _brush.Color = Fade(Color.FromArgb(UsageBandAlpha, c));
            g.FillPolygon(_brush, _usageBand);
            SetPen(Lighten(c, 0.25f), Math.Max(1f, 1.25f * s));
            g.DrawLines(_pen, _usageLine);
        }
    }

    /// <summary>% de la cuota: una línea por serie (cortada en los huecos) con su área en degradado hasta abajo; la casilla con
    /// dato entre dos huecos (sin vecinos no hay línea, y el remuestreo la pierde) va como un punto.</summary>
    private void UsageLines(Graphics g, RectangleF plot, float s, UsageShape shape, UsageChartModel model)
    {
        int n = UsageShape.Samples;
        float saved = _alpha;
        for (int k = 0; k < shape.Keys.Length; k++)
        {
            _alpha = Quantize(saved * shape.Alpha[k]);
            if (_alpha <= 0f) continue;
            var c = UsageColor(shape.Colors[k]);
            var upper = shape.Upper[k];
            int m = 0;
            while (m < n)
            {
                while (m < n && float.IsNaN(upper[m])) m++;
                int start = m;
                while (m < n && !float.IsNaN(upper[m])) m++;
                int len = m - start;
                if (len < 2) continue;
                var line = Run(len);
                for (int i = 0; i < len; i++)
                    line[i] = new PointF(plot.Left + shape.X(start + i) * plot.Width, plot.Bottom - upper[start + i] * plot.Height);
                var area = Run(len + 2);
                Array.Copy(line, area, len);
                area[len] = new PointF(line[len - 1].X, plot.Bottom);
                area[len + 1] = new PointF(line[0].X, plot.Bottom);
                g.FillPolygon(AreaBrush(plot, c), area);
                SetPen(c, Math.Max(1.25f, 2f * s));
                g.DrawLines(_pen, line);
            }
            UsageGapLinks(g, plot, s, model, shape.Keys[k], c);
            UsageLoneDots(g, plot, s, model, shape.Keys[k], c);
        }
        _alpha = saved;
    }

    /// <summary>Cada hueco que queda en la serie <paramref name="key"/> (la app estuvo cerrada) se salva con una línea fina,
    /// discontinua y tenue entre la última casilla con dato y la siguiente, sin área debajo: la curva no se corta en negro.</summary>
    private void UsageGapLinks(Graphics g, RectangleF plot, float s, UsageChartModel model, string key, Color c)
    {
        int j = -1;
        for (int i = 0; i < model.Series.Count && j < 0; i++) if (model.Series[i].Key == key) j = i;
        if (j < 0 || j >= model.Tops.Length) return;
        var tops = model.Tops[j];
        DashPen(Color.FromArgb(UsageGapAlpha, c), Math.Max(1f, s), 3f * s, 3f * s);
        int prev = -1;
        for (int i = 0; i < tops.Length; i++)
        {
            if (float.IsNaN(tops[i])) continue;
            if (prev >= 0 && i - prev > 1)
            {
                float x0 = plot.Left + UsageChartHit.X(prev, model.Count, model.PastFraction, model.Shape.FirstX) * plot.Width;
                float x1 = plot.Left + UsageChartHit.X(i, model.Count, model.PastFraction, model.Shape.FirstX) * plot.Width;
                g.DrawLine(_usageDash, x0, plot.Bottom - tops[prev] * plot.Height, x1, plot.Bottom - tops[i] * plot.Height);
            }
            prev = i;
        }
    }

    /// <summary>Un punto en cada casilla de la serie <paramref name="key"/> con dato y huecos a los dos lados.</summary>
    private void UsageLoneDots(Graphics g, RectangleF plot, float s, UsageChartModel model, string key, Color c)
    {
        int j = -1;
        for (int i = 0; i < model.Series.Count && j < 0; i++) if (model.Series[i].Key == key) j = i;
        if (j < 0 || j >= model.Tops.Length) return;
        var values = model.Series[j].Values;
        float d = 2 * UsageDot * s;
        _brush.Color = Fade(c);
        for (int i = 0; i < values.Length; i++)
        {
            if (double.IsNaN(values[i]) || (i > 0 && !double.IsNaN(values[i - 1]))
                || (i + 1 < values.Length && !double.IsNaN(values[i + 1]))) continue;
            float x = plot.Left + UsageChartHit.X(i, model.Count, model.PastFraction, model.Shape.FirstX) * plot.Width;
            float y = plot.Bottom - model.Tops[j][i] * plot.Height;
            g.FillEllipse(_brush, x - d / 2f, y - d / 2f, d, d);
        }
    }

    /// <summary>Umbrales de los avisos (discontinuos, en ámbar y rojo, con su % colgando bajo la línea) y la raya de ahora.</summary>
    private void UsageThresholds(Graphics g, RectangleF plot, float s, UsageChartModel m)
    {
        float lw = Math.Max(1f, s);
        foreach (var t in m.Thresholds)
        {
            float y = MathF.Round(plot.Bottom - t.Fraction * plot.Height);
            var c = t.Critical ? _t.Critical : _t.Warn;
            DashPen(Color.FromArgb(150, c), lw, 2f * s, 3f * s);
            g.DrawLine(_usageDash, plot.Left, y, plot.Right, y);
            DrawText(g, t.Label, _small!, t.Critical ? _t.CriticalText : _t.WarnText,
                new RectangleF(plot.Left, y + 1f * s, plot.Width - 2f * s, 12f * s), _right);
        }
        if (m.PastFraction < 0.999f)
        {
            float x = MathF.Round(plot.Left + m.PastFraction * plot.Width);
            _brush.Color = Fade(UsageNowLine);
            g.FillRectangle(_brush, x, plot.Top, lw, plot.Height);
        }
    }

    /// <summary>La proyección del ritmo: recta discontinua desde el punto de ahora, la raya del reinicio y, si se agota antes,
    /// el punto rojo en el 100 %.</summary>
    private void UsageProjectionMarks(Graphics g, RectangleF plot, float s, UsageChartModel m)
    {
        if (m.Projection is not { } p) return;
        float lw = Math.Max(1f, s);
        var c2 = UsageColor(p.Color);
        float x0 = plot.Left + p.FromX * plot.Width, y0 = plot.Bottom - p.FromY * plot.Height;
        float x1 = plot.Left + p.ToX * plot.Width, y1 = plot.Bottom - p.ToY * plot.Height;
        DashPen(c2, Math.Max(1.25f, 1.75f * s), 4f * s, 3f * s);
        if (x1 - x0 > 0.5f) g.DrawLine(_usageDash, x0, y0, x1, y1);
        float d = 2 * UsageDot * s;
        _brush.Color = Fade(c2);
        g.FillEllipse(_brush, x0 - d / 2f, y0 - d / 2f, d, d);
        if (p.ResetX is { } rx)
        {
            float x = MathF.Round(plot.Left + rx * plot.Width);
            DashPen(Color.FromArgb(120, _t.Foreground), lw, 1f * s, 3f * s);
            g.DrawLine(_usageDash, x, plot.Top, x, plot.Bottom);
            DrawText(g, Strings.NotchUsageReset, _small!, _t.TextMuted, new RectangleF(x - 61f * s, plot.Bottom - 14f * s, 58f * s, 12f * s),
                _right);
        }
        if (p.Exhausts && p.EtaX is { } ex)
        {
            float x = plot.Left + ex * plot.Width, y = plot.Top;
            _brush.Color = Fade(_t.Critical);
            g.FillEllipse(_brush, x - d / 2f, y - d / 2f, d, d);
        }
    }

    /// <summary>Leyenda: una entrada por serie (punto, nombre y valor; las ocultas con el punto hueco y atenuadas) y, en la
    /// cuota, la proyección a la derecha.</summary>
    private void UsageLegend(Graphics g, UsageRects r, float s, UsageChartModel m, UsageView v)
    {
        int slots = UsageLayout.LegendSlotsFor(m.Mode, m.Series.Count), n = Math.Min(m.Series.Count, slots);
        for (int i = 0; i < n; i++)
        {
            var se = m.Series[i];
            var slot = r.LegendSlot(i, 1, slots);
            bool hover = v.Interactive && v.Hover.Control == UsageControl.Legend && v.Hover.Index == i && v.HoverIntensity > 0f;
            if (hover)
            {
                using var path = RoundedRect(Rectangle.Round(RectangleF.Inflate(slot, -1f * s, 0f)), slot.Height / 2f);
                _brush.Color = Fade(Color.FromArgb((int)(MusicHoverFill.A * Math.Clamp(v.HoverIntensity, 0f, 1f)), 255, 255, 255));
                g.FillPath(_brush, path);
            }
            float saved = _alpha;
            if (!se.Visible) _alpha = Quantize(_alpha * UsageHiddenDim);
            try
            {
                float d = 7f * s, x = slot.Left + 4f * s, cy = slot.Top + slot.Height / 2f;
                var c = UsageColor(se.Color);
                if (se.Visible)
                {
                    _brush.Color = Fade(c);
                    g.FillEllipse(_brush, x, cy - d / 2f, d, d);
                }
                else
                {
                    SetPen(c, Math.Max(1f, 1.25f * s));
                    g.DrawEllipse(_pen, x + 0.5f, cy - d / 2f + 0.5f, d - 1f, d - 1f);
                }
                var text = RectangleF.FromLTRB(x + d + 4f * s, slot.Top, slot.Right - 2f * s, slot.Bottom);
                float lw = Math.Min(text.Width, g.MeasureString(se.Label, _small!, int.MaxValue, _left).Width + 1f);
                DrawText(g, se.Label, _small!, _t.Foreground, new RectangleF(text.Left, text.Top, lw, text.Height), _left);
                DrawText(g, se.Value, _small!, _t.TextMuted, RectangleF.FromLTRB(text.Left + lw + 3f * s, text.Top, text.Right, text.Bottom), _left);
            }
            finally { _alpha = saved; }
        }
        if (m.Projection is { Caption.Length: > 0 } p)
        {
            var slot = r.LegendSlot(2, 2);
            DrawText(g, p.Caption, _small!, p.Exhausts ? _t.CriticalText : _t.TextMuted, slot, _right);
        }
    }

    /// <summary>Sin datos todavía: nada de ejes vacíos. Un glifo de barras discreto, el título y la pista, centrados en la
    /// gráfica; cargando, solo «Cargando…» (o, si falló, el aviso).</summary>
    private void UsageEmpty(Graphics g, UsageRects r, float s, UsageChartModel m, UsageChartStatus status)
    {
        var area = RectangleF.FromLTRB(r.Plot.Left, r.Plot.Top, r.Plot.Right, r.Legend.Bottom);
        if (m.Loading)
        {
            DrawText(g, UsageStatusText(status), _caption!, status == UsageChartStatus.Failed ? _t.WarnText : _t.TextMuted, area,
                _center);
            return;
        }
        bool quota = m.Mode == UsageChartMode.Quota;
        float cy = area.Top + area.Height * 0.42f, gw = 26f * s, gh = 16f * s, bw = Math.Max(2f, 4f * s);
        float gx = area.Left + (area.Width - gw) / 2f, gy = cy - gh - 10f * s;
        for (int i = 0; i < 4; i++)
        {
            float h = gh * (quota ? EmptyQuotaBars[i] : EmptySpendBars[i]);
            var bar = new RectangleF(gx + i * (bw + 3.33f * s), gy + gh - h, bw, h);
            using var path = RoundedRect(Rectangle.Round(bar), bw / 2f);
            _brush.Color = Fade(Color.FromArgb(70, _t.Foreground));
            g.FillPath(_brush, path);
        }
        DrawText(g, quota ? Strings.NotchUsageEmptyQuota : Strings.NotchUsageEmptySpend, _body!, _t.Foreground,
            new RectangleF(area.Left, cy - 2f * s, area.Width, 16f * s), _center);
        DrawText(g, quota ? Strings.NotchUsageEmptyQuotaHint : Strings.NotchUsageEmptySpendHint, _caption!, _t.TextMuted,
            new RectangleF(area.Left, cy + 15f * s, area.Width, 14f * s), _center);
    }

    /// <summary>«Cargando…» o, si falló la lectura, el aviso (ya traducidos).</summary>
    private string UsageStatusText(UsageChartStatus status) =>
        status == UsageChartStatus.Failed ? Strings.NotchUsageLoadFailed : Strings.Loading;

    // ---------------------------------------------------------------- hover

    /// <summary>Raya vertical que se desliza entre casillas, un punto en cada serie a la vista y el tooltip de la casilla.</summary>
    private void UsageHover(Graphics g, UsageRects r, float s, UsageChartModel m, UsageView v)
    {
        var plot = r.Plot;
        float saved = _alpha;
        _alpha = Quantize(saved * Math.Clamp(v.TipAlpha, 0f, 1f));
        try
        {
            if (_alpha <= 0f) return;
            float x = plot.Left + UsageChartHit.X(v.Bucket, m.Count, m.PastFraction, m.Shape.FirstX) * plot.Width;
            _brush.Color = Fade(UsageHoverLine);
            g.FillRectangle(_brush, MathF.Round(x), plot.Top, Math.Max(1f, s), plot.Height);

            int i0 = Math.Clamp((int)MathF.Floor(v.Bucket), 0, m.Count - 1), i1 = Math.Min(m.Count - 1, i0 + 1);
            float f = Math.Clamp(v.Bucket - i0, 0f, 1f), d = 2 * UsageDot * s, ring = Math.Max(1f, 1.5f * s);
            for (int k = 0; k < m.Series.Count && k < m.Tops.Length; k++)
            {
                if (!m.Series[k].Visible) continue;
                float a = m.Tops[k][i0], b = m.Tops[k][i1];
                // Junto a un hueco no se inventa nada: solo el punto de la casilla con dato más cercana, si es la de al lado.
                if (float.IsNaN(a) || float.IsNaN(b))
                {
                    float near = f < 0.5f ? a : b;
                    if (float.IsNaN(near)) continue;
                    a = b = near;
                }
                float y = plot.Bottom - (a + (b - a) * f) * plot.Height;
                _brush.Color = Fade(Color.Black);
                g.FillEllipse(_brush, x - d / 2f - ring, y - d / 2f - ring, d + 2 * ring, d + 2 * ring);
                _brush.Color = Fade(UsageColor(m.Series[k].Color));
                g.FillEllipse(_brush, x - d / 2f, y - d / 2f, d, d);
            }
            if (v.TipIndex >= 0 && v.TipIndex < m.Tips.Count) UsageTooltip(g, r, s, m.Tips[v.TipIndex], x);
        }
        finally { _alpha = saved; }
    }

    /// <summary>Tooltip: caja oscura redondeada junto a la raya (a la derecha si cabe; si no, a la izquierda) con la hora, el
    /// total y una fila por serie (punto de color, nombre y valor a la derecha).</summary>
    private void UsageTooltip(Graphics g, UsageRects r, float s, UsageTip tip, float x)
    {
        var size = MeasureTip(g, tip, s);
        float gap = 8f * s;
        var bounds = RectangleF.FromLTRB(r.Right.Left, r.Toolbar.Top, r.Right.Right, r.Legend.Bottom);
        float left = x + gap + size.Width <= bounds.Right ? x + gap : x - gap - size.Width;
        left = Math.Clamp(left, bounds.Left, Math.Max(bounds.Left, bounds.Right - size.Width));
        var box = new RectangleF(left, r.Plot.Top + 2f * s, size.Width, size.Height);
        using (var path = RoundedRect(Rectangle.Round(box), 7f * s))
        {
            _brush.Color = Fade(UsageTipFill);
            g.FillPath(_brush, path);
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            SetPen(UsageTipBorder, Math.Max(1f, s));
            g.DrawPath(_pen, path);
            g.Restore(state);
        }
        float pad = UsageTipPad * s, row = UsageTipRow * s, y = box.Top + pad;
        var inner = RectangleF.FromLTRB(box.Left + pad, box.Top, box.Right - pad, box.Bottom);
        DrawText(g, tip.Title, _small!, _t.TextMuted, new RectangleF(inner.Left, y, inner.Width, row), _left);
        if (tip.Total is { } total)
            DrawText(g, total, _caption!, _t.Foreground, new RectangleF(inner.Left, y, inner.Width, row), _right);
        y += row;
        foreach (var rw in tip.Rows)
        {
            float d = 6f * s;
            _brush.Color = Fade(UsageColor(rw.Color));
            g.FillEllipse(_brush, inner.Left, y + (row - d) / 2f, d, d);
            var line = new RectangleF(inner.Left + d + 5f * s, y, inner.Width - d - 5f * s, row);
            DrawText(g, rw.Label, _small!, _t.Foreground, RectangleF.FromLTRB(line.Left, line.Top, line.Right - _tipValueW - 6f * s, line.Bottom), _left);
            DrawText(g, rw.Value, _small!, _t.Foreground, line, _right);
            y += row;
        }
    }

    /// <summary>Tamaño del tooltip (medido una vez por tooltip y escala).</summary>
    private SizeF MeasureTip(Graphics g, UsageTip tip, float s)
    {
        if (ReferenceEquals(tip, _tipMeasured) && Math.Abs(s - _tipScale) < 0.001f) return _tipSize;
        float W(string t, Font f) => g.MeasureString(t, f, int.MaxValue, _left).Width;
        float head = W(tip.Title, _small!) + (tip.Total is { } total ? 10f * s + W(total, _caption!) : 0f);
        float label = 0f, value = 0f;
        foreach (var rw in tip.Rows)
        {
            label = Math.Max(label, W(rw.Label, _small!));
            value = Math.Max(value, W(rw.Value, _small!));
        }
        float rows = tip.Rows.Count == 0 ? 0f : 6f * s + 5f * s + label + 10f * s + value;
        float pad = UsageTipPad * s;
        _tipSize = new SizeF(MathF.Ceiling(Math.Max(Math.Max(head, rows), 60f * s) + 2 * pad),
            MathF.Ceiling((1 + tip.Rows.Count) * UsageTipRow * s + 2 * pad));
        _tipValueW = value;
        _tipMeasured = tip;
        _tipScale = s;
        return _tipSize;
    }

    // ---------------------------------------------------------------- anillos que abren la página Uso

    /// <summary>
    /// Ventana de cuota bajo <paramref name="p"/> en la vista previa del reposo (<paramref name="pill"/>, como la pinta
    /// <see cref="PaintWorking"/> con <c>peek</c>): con los dos anillos juntos («◯ 5h %  ◯ 7d %»), cada grupo hasta la mitad
    /// del hueco que los separa, y nada fuera del par (ese clic abre el notch como siempre); con Claude trabajando o la vista
    /// previa ancha, el anillo de la izquierda (sin sesión activa) y las ranuras «7d %» y «5h %» de la derecha.
    /// <see cref="QuotaFocus.None"/> fuera de ellos. Mide con las MISMAS fuentes.
    /// </summary>
    public QuotaFocus PeekQuotaAt(PointF p, RectangleF pill, float s, bool mascot, IReadOnlyList<SessionState> sessions,
        RealUsage? quota)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (s <= 0 || !pill.Contains(p)) return QuotaFocus.None;
        var area = IdleScene.ContentArea(pill, mascot, s);
        if (p.X < area.Left) return QuotaFocus.None;
        bool active = WorkingText(sessions, Strings).Length > 0;
        if (!active && !SevenDayFits(area.Width, s, NotchMotion.RingDiameterPeek))
        {
            float pair = MeasureQuotaPair(s, withPct: true, Pct(quota?.FiveHour), Pct(quota?.SevenDay));
            return QuotaPairAt(p.X, area, s, pair, MeasureQuotaGroup(s, "5h", Pct(quota?.FiveHour)));
        }
        return PeekSlotAt(p.X, area, s, active);
    }

    /// <summary>Holgura (px lógicos) a cada lado del par de anillos que aún cuenta como encima.</summary>
    private const float PairHitSlop = 4f;

    /// <summary>Grupo del par de anillos centrado en <paramref name="area"/> bajo <paramref name="x"/>: el de 5 h hasta la mitad
    /// del hueco y el de 7 d desde ahí, solo dentro del par (con <see cref="PairHitSlop"/>); fuera, <see cref="QuotaFocus.None"/>.
    /// PURO.</summary>
    internal static QuotaFocus QuotaPairAt(float x, RectangleF area, float s, float pairWidth, float fiveWidth)
    {
        float left = area.Left + (area.Width - pairWidth) / 2f, slop = PairHitSlop * s;
        if (x < left - slop || x > left + pairWidth + slop) return QuotaFocus.None;
        return x < QuotaPairSplit(area, s, pairWidth, fiveWidth) ? QuotaFocus.Five : QuotaFocus.Seven;
    }

    /// <summary>x que separa los dos grupos del par de anillos centrado en <paramref name="area"/> (ancho total
    /// <paramref name="pairWidth"/>, el del primer grupo <paramref name="fiveWidth"/>): la mitad del hueco entre ellos. PURO.</summary>
    internal static float QuotaPairSplit(RectangleF area, float s, float pairWidth, float fiveWidth) =>
        area.Left + (area.Width - pairWidth) / 2f + fiveWidth + PairGroupGap * s / 2f;

    /// <summary>Ranura de la vista previa ancha bajo <paramref name="x"/>: a la derecha «5h %» (y su etiqueta) y, a su
    /// izquierda, «7d %»; sin sesión activa, el anillo de la izquierda es el de 5 h. PURO (la geometría de <see cref="PaintWorking"/>).</summary>
    internal static QuotaFocus PeekSlotAt(float x, RectangleF area, float s, bool active)
    {
        float fiveLeft = area.Right - (PadX + RightSlot + 18f) * s;
        if (x >= fiveLeft) return QuotaFocus.Five;
        if (SevenDayFits(area.Width, s, active ? NotchMotion.SpinnerDiameter : NotchMotion.RingDiameterPeek)
            && x >= fiveLeft - (Gap + RightSlot + 18f) * s) return QuotaFocus.Seven;
        if (!active && x < area.Left + (PadX + NotchMotion.RingDiameterPeek + Gap) * s) return QuotaFocus.Five;
        return QuotaFocus.None;
    }

    /// <summary>Ancho del grupo «◯ etiqueta %» con las fuentes del par de anillos.</summary>
    private float MeasureQuotaGroup(float s, string label, string pct)
    {
        EnsureFonts(s);
        _measureBmp ??= new Bitmap(1, 1);
        _measureG ??= Graphics.FromImage(_measureBmp);
        _measureG.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        return (RingFor(true) + PairIconGap) * s + TextW(_measureG, label) + PairPctGap * s + TextW(_measureG, pct);
    }

    /// <summary>Ventana bajo <paramref name="p"/> en la columna de cuota de la página Claude (5 h arriba, 7 d debajo), con la
    /// MISMA geometría que <see cref="PaintClaudePage"/>; <see cref="QuotaFocus.None"/> fuera. PURO.</summary>
    public static QuotaFocus ClaudeQuotaAt(PointF p, RectangleF pill, float s, bool withAlert, bool withDots)
    {
        var inner = ExpandedInner(pill, s, withAlert, withDots);
        float line = NotchLayout.ExpandedLine * s, block = 2 * line + ExpandedWindowGap * s;
        if (p.X < inner.Left || p.X > inner.Left + ExpandedQuotaCol * s || p.Y < inner.Top) return QuotaFocus.None;
        if (p.Y < inner.Top + block) return QuotaFocus.Five;
        return p.Y < inner.Top + 2 * block && p.Y <= inner.Bottom + 1f ? QuotaFocus.Seven : QuotaFocus.None;
    }

    // ---------------------------------------------------------------- piezas

    /// <summary>Un tramo de <paramref name="len"/> puntos (reutilizado por largo: sin arrays por fotograma).</summary>
    private PointF[] Run(int len) => _usageRuns[len] ??= new PointF[len];

    /// <summary>Degradado vertical del área bajo una línea: el color de la serie arriba y transparente abajo, de lo alto a lo bajo
    /// de la gráfica. Un solo pincel, creado para la caja de la gráfica (fija con la página a la vista: va anclada arriba y al
    /// centro) y rehecho solo si cambia; los colores se le cambian sin crear nada.</summary>
    private Brush AreaBrush(RectangleF plot, Color c)
    {
        var top = Fade(Color.FromArgb(UsageAreaTop, c));
        var bottom = Fade(Color.FromArgb(UsageAreaBottom, c));
        if (_usageArea is null || plot != _usageAreaRect)
        {
            _usageArea?.Dispose();
            // Un píxel de más arriba y abajo: el antialias del borde no cae fuera del degradado (sin repetirlo).
            _usageArea = new LinearGradientBrush(RectangleF.Inflate(plot, 0f, 1f), top, bottom, LinearGradientMode.Vertical)
            {
                WrapMode = WrapMode.TileFlipXY,
            };
            _usageAreaRect = plot;
            _usageAreaTop = top;
            _usageAreaBottom = bottom;
        }
        else if (top != _usageAreaTop || bottom != _usageAreaBottom)
        {
            _usageArea.LinearColors = new[] { top, bottom };
            _usageAreaTop = top;
            _usageAreaBottom = bottom;
        }
        return _usageArea;
    }

    /// <summary>Lápiz discontinuo: trazos de <paramref name="dash"/> y huecos de <paramref name="gap"/> px (GDI+ los mide en
    /// anchos de lápiz).</summary>
    private void DashPen(Color c, float width, float dash, float gap)
    {
        _usageDash.Color = Fade(c);
        _usageDash.Width = width;
        _usageDashPattern[0] = Math.Max(0.1f, dash / width);
        _usageDashPattern[1] = Math.Max(0.1f, gap / width);
        _usageDash.DashPattern = _usageDashPattern;
    }

    private static Color Lighten(Color c, float amount) => Color.FromArgb(c.A,
        (int)(c.R + (255 - c.R) * amount), (int)(c.G + (255 - c.G) * amount), (int)(c.B + (255 - c.B) * amount));

    private void EnsureUsageFonts(float scale)
    {
        if (Math.Abs(scale - _usageNumScale) < 0.001f && _usageNum is not null) return;
        _usageNum?.Dispose();
        _usageNum = new Font(Typography.Body.FontFamily.Name, 10.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _usageNumScale = scale;
    }

    private void DisposeUsage()
    {
        _usageNum?.Dispose();
        _usageNum = null;
        _usageArea?.Dispose();
        _usageArea = null;
        _usageDash.Dispose();
    }
}
