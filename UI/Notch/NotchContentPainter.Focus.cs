using System.Drawing.Drawing2D;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// El pomodoro: la página Foco (anillo grande con la cuenta atrás y los puntos del ciclo; cabecera con los focos de hoy; la línea
/// de la fase, que con el ratón sobre saltar o reiniciar dice qué hacen; Empezar / Pausa / Reanudar y los botones redondos de
/// saltar y reiniciar; la tarea enlazada y la semana, Pro), el mini anillo del notch cerrado (reposo, vista previa, la ranura de
/// Trabajando y la raya de Mínimo) y el aviso del fin de fase con su botón ancho. Todo llega en <see cref="FocusView"/> con los
/// textos ya formateados; la geometría es la de <see cref="FocusLayout"/> / <see cref="FocusFace"/>, la misma del clic. Colores
/// de fase: el acento (el violeta de Muesca) para el foco, verde para el descanso corto y azul cielo para el largo (ninguno es
/// el rojo de los avisos; el violeta y el azul cielo se separan ~58° de tono, ver <see cref="FocusPalette"/>).
/// </summary>
public sealed partial class NotchContentPainter
{
    private static readonly Color FocusTrack = Color.FromArgb(38, 255, 255, 255);
    private static readonly Color FocusButtonFill = Color.FromArgb(24, 255, 255, 255);
    private static readonly Color FocusBarFill = Color.FromArgb(120, 255, 255, 255);

    /// <summary>Alfa del arco parado (aún sin empezar) y en pausa.</summary>
    private const float FocusReadyDim = 0.55f, FocusPausedDim = 0.45f;

    private Font? _focusClock;
    private float _focusClockScale = -1f;

    // Ancho de la cabecera («Foco»), medido una vez por texto y fuente (no en cada frame).
    private string? _focusHeaderText;
    private Font? _focusHeaderFont;
    private float _focusHeaderW;

    // El visto bueno del anillo del fin de fase: los tres puntos, sin un array nuevo por frame.
    private readonly PointF[] _focusCheck = new PointF[3];

    // Textos del aviso del fin de fase por fases, del idioma vigente: se forman una vez, no por frame.
    private Strings? _focusAlertStrings;
    private readonly Dictionary<(FocusPhase Ended, FocusPhase Next), (string Title, string Detail, string Button)> _focusAlertTexts = new();

    /// <summary>Color de la fase: el acento (foco), verde (descanso corto) o azul cielo (descanso largo), de
    /// <see cref="FocusPalette"/>.</summary>
    internal Color FocusColor(FocusPhase phase) => FocusPalette.Fill(phase, _t);

    /// <summary>El color del arco según el estado: entero corriendo; atenuado parado o en pausa.</summary>
    private Color FocusArcColor(FocusView v) => Faded(FocusColor(v.Phase),
        v.Run == FocusRun.Running ? 1f : v.Run == FocusRun.Paused ? FocusPausedDim : FocusReadyDim);

    private static Color Faded(Color c, float k) => k >= 1f ? c : Color.FromArgb((int)(c.A * Math.Clamp(k, 0f, 1f)), c);

    // ---------------------------------------------------------------- página Foco

    private void PaintFocusPage(Graphics g, RectangleF inner, float s, FocusView? v)
    {
        var r = FocusLayout.Compute(inner, s);
        if (v is null)
        {
            DrawText(g, Strings.NotchPageFocus, _title!, _t.Foreground, r.Header, _left);
            return;
        }
        EnsureFocusFonts(s);
        var t = v.Texts;
        FocusBigRing(g, r, s, v);

        // Cabecera: «Foco» y, a la derecha, los focos de hoy.
        var h = r.Header;
        float tw = Math.Min(h.Width * 0.45f, FocusHeaderWidth(g, t.Header) + 1f);
        DrawText(g, t.Header, _title!, _t.Foreground, new RectangleF(h.Left, h.Top, tw, h.Height), _left);
        DrawText(g, t.Today, _caption!, _t.TextMuted, RectangleF.FromLTRB(h.Left + tw + Gap * s, h.Top, h.Right, h.Bottom), _right);

        // La fase (el nombre de saltar y reiniciar va en su tooltip, FocusIconTip).
        var lineColor = v.Finished is { } done ? FocusPalette.Text(done, _t) : _t.TextMuted;
        DrawText(g, t.PhaseLine, _caption!, lineColor, r.Phase, _left);

        FocusPrimaryButton(g, r.Primary, s, v);
        FocusRoundButton(g, r.Skip, s, v, FocusControl.Skip);
        FocusRoundButton(g, r.Reset, s, v, FocusControl.Reset);
        FocusTaskRow(g, r, s, v);
        FocusStatsRow(g, r, s, v);
        FocusIconTip(g, inner, r, s, v);
    }

    /// <summary>
    /// Tooltip del notch con el nombre de saltar o reiniciar, encima del icono bajo el ratón: la caja oscura del tooltip de Uso,
    /// con el fundido del realce (<see cref="FocusView.HoverIntensity"/>; con «reducir movimiento» el realce no se anima y el
    /// tooltip aparece y se va de golpe).
    /// </summary>
    private void FocusIconTip(Graphics g, RectangleF inner, FocusRects r, float s, FocusView v)
    {
        float k = Math.Clamp(v.HoverIntensity, 0f, 1f);
        if (k <= 0.002f || v.Texts.TipFor(v.Hover.Control) is not { } text) return;
        var button = v.Hover.Control == FocusControl.Skip ? r.Skip : r.Reset;
        float tw = g.MeasureString(text, _small!, int.MaxValue, _left).Width;
        var box = FocusLayout.TipBox(button, tw, inner, s);
        float saved = _alpha;
        _alpha *= k;
        try
        {
            using (var path = RoundedRect(Rectangle.Round(box), box.Height / 2f))
            {
                _brush.Color = Fade(UsageTipFill);
                g.FillPath(_brush, path);
                var state = g.Save();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                SetPen(UsageTipBorder, Math.Max(1f, s));
                g.DrawPath(_pen, path);
                g.Restore(state);
            }
            DrawText(g, text, _small!, _t.Foreground, box, _center);
        }
        finally { _alpha = saved; }
    }

    /// <summary>Anillo grande: pista, arco de lo que QUEDA (de las 12 en el sentido del reloj) en el color de la fase, el reloj
    /// dentro y los puntos del ciclo debajo. <see cref="FocusView.Pop"/> lo escala desde su centro (bote del fin de fase).</summary>
    private void FocusBigRing(Graphics g, FocusRects r, float s, FocusView v)
    {
        var ring = r.Ring;
        if (Math.Abs(v.Pop - 1f) > 0.001f)
        {
            float k = Math.Clamp(v.Pop, 0.2f, 1.3f);
            ring = new RectangleF(ring.Left + ring.Width * (1f - k) / 2f, ring.Top + ring.Height * (1f - k) / 2f, ring.Width * k,
                ring.Height * k);
        }
        float w = FocusLayout.RingStroke * s;
        var arc = RectangleF.Inflate(ring, -w / 2f, -w / 2f);
        if (arc.Width > 1f)
        {
            SetPen(FocusTrack, w);
            g.DrawEllipse(_pen, arc);
            float left = 1f - Math.Clamp(v.Progress, 0f, 1f);
            if (left > 0.0005f)
            {
                SetPen(FocusArcColor(v), w);
                g.DrawArc(_pen, arc, -90f, 360f * left);
            }
        }
        DrawText(g, v.Texts.Clock, _focusClock!, v.Run == FocusRun.Paused ? _t.TextMuted : _t.Foreground, r.Clock, _center);

        int n = Math.Clamp(v.LongEvery, 1, FocusDurations.MaxEvery);
        for (int i = 0; i < n; i++)
        {
            _brush.Color = Fade(i < v.Cycle ? FocusColor(FocusPhase.Focus) : FocusTrack);
            g.FillEllipse(_brush, r.CycleDot(i, n));
        }
    }

    /// <summary>Realce del control bajo el ratón (con su intensidad) y pulsación (encoge 1 px y se oscurece):
    /// <see cref="FocusPressFill"/>.</summary>
    private RectangleF FocusPressable(RectangleF box, ref Color fill, float s, FocusView v, FocusControl c)
    {
        bool pressed = v.Pressed.Control == c;
        fill = FocusPressFill(fill, v.Hover.Control == c ? v.HoverIntensity : 0f, pressed);
        if (pressed)
        {
            float inset = Math.Max(1f, MathF.Round(s));
            box = RectangleF.Inflate(box, -inset, -inset);
        }
        return box;
    }

    /// <summary>
    /// El relleno que se pinta de verdad con el ratón encima (<paramref name="hover"/>, 0..1) y pulsado. Translúcido (Pausa,
    /// Saltar, Reiniciar): gana alfa. Opaco (el color de la fase, con texto encima): <see cref="ColorMath.HoverFill"/>, que aclara
    /// los rellenos claros (texto negro) y OSCURECE los oscuros (texto blanco); aclarar el violeta bajaba el blanco de AA. PURO.
    /// </summary>
    internal static Color FocusPressFill(Color fill, float hover, bool pressed)
    {
        if (hover > 0f)
        {
            float k = Math.Clamp(hover, 0f, 1f);
            fill = fill.A < 255
                ? Color.FromArgb(Math.Min(255, fill.A + (int)(MusicHoverFill.A * k)), fill)
                : ColorMath.HoverFill(fill, k);
        }
        return pressed ? Darken(fill, MusicPressDarken) : fill;
    }

    /// <summary>La tinta del botón principal, calculada sobre el relleno que se pinta (<paramref name="paintedFill"/>, ya con el
    /// realce y la pulsación): el lado que más contrasta. En pausa, el color de texto del notch (el botón es translúcido). PURO.</summary>
    internal static Color FocusPrimaryInk(Color paintedFill, bool pause, Color foreground) =>
        pause ? foreground : ColorMath.Contrast(paintedFill);

    /// <summary>Empezar / Reanudar (y la oferta) en el color de la fase con el texto que más contrasta; Pausa, translúcido. Con su
    /// icono.</summary>
    private void FocusPrimaryButton(Graphics g, RectangleF box, float s, FocusView v)
    {
        bool pause = v.Run == FocusRun.Running && v.Finished is null;
        var fill = pause ? FocusButtonFill : FocusColor(v.Phase);
        var r = FocusPressable(box, ref fill, s, v, FocusControl.Primary);
        using (var path = RoundedRect(Rectangle.Round(r), r.Height / 2f))
        {
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }
        // Sobre el relleno que se pinta (con el realce y la pulsación), el lado que más contrasta: blanco con el violeta del
        // foco, oscuro con el verde y el azul claros de los descansos.
        var ink = FocusPrimaryInk(fill, pause, _t.Foreground);
        float icon = 9f * s, ix = r.Left + 13f * s, cy = r.Top + r.Height / 2f;
        _brush.Color = Fade(ink);
        if (pause)
        {
            float bw = 2.6f * s;
            g.FillRectangle(_brush, ix + 0.5f * s, cy - icon / 2f, bw, icon);
            g.FillRectangle(_brush, ix + icon - bw - 0.5f * s, cy - icon / 2f, bw, icon);
        }
        else
        {
            _tri[0] = new PointF(ix + 1f * s, cy - icon / 2f);
            _tri[1] = new PointF(ix + 1f * s, cy + icon / 2f);
            _tri[2] = new PointF(ix + icon, cy);
            g.FillPolygon(_brush, _tri);
        }
        DrawText(g, v.Texts.Primary, _title!, ink, RectangleF.FromLTRB(ix + icon + 7f * s, r.Top, r.Right - 10f * s, r.Bottom), _left);
    }

    /// <summary>Saltar (triángulo con su barra) y reiniciar (flecha en círculo): redondos, translúcidos, sin texto.</summary>
    private void FocusRoundButton(Graphics g, RectangleF box, float s, FocusView v, FocusControl which)
    {
        var fill = FocusButtonFill;
        var r = FocusPressable(box, ref fill, s, v, which);
        _brush.Color = Fade(fill);
        g.FillEllipse(_brush, r);
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, u = 4.2f * s;
        var ink = _t.Foreground;
        if (which == FocusControl.Skip)
        {
            _brush.Color = Fade(ink);
            _tri[0] = new PointF(cx - u * 0.9f, cy - u);
            _tri[1] = new PointF(cx - u * 0.9f, cy + u);
            _tri[2] = new PointF(cx + u * 0.6f, cy);
            g.FillPolygon(_brush, _tri);
            g.FillRectangle(_brush, cx + u * 0.65f, cy - u, Math.Max(1.5f, 1.8f * s), 2f * u);
            return;
        }
        // Reiniciar: arco de 280° y la punta de la flecha al final, en el sentido del reloj.
        float rad = u * 1.15f, w = Math.Max(1.4f, 1.7f * s);
        SetPen(ink, w);
        var circle = new RectangleF(cx - rad, cy - rad, 2f * rad, 2f * rad);
        const float start = 250f, sweep = 280f;
        g.DrawArc(_pen, circle, start, sweep);
        double end = (start + sweep) * Math.PI / 180.0;
        var tip = new PointF(cx + rad * (float)Math.Cos(end), cy + rad * (float)Math.Sin(end));
        float dx = -(float)Math.Sin(end), dy = (float)Math.Cos(end), nx = (float)Math.Cos(end), ny = (float)Math.Sin(end);
        float a = 2.6f * s;
        _brush.Color = Fade(ink);
        _tri[0] = new PointF(tip.X + dx * a, tip.Y + dy * a);
        _tri[1] = new PointF(tip.X - dx * a * 0.3f + nx * a, tip.Y - dy * a * 0.3f + ny * a);
        _tri[2] = new PointF(tip.X - dx * a * 0.3f - nx * a, tip.Y - dy * a * 0.3f - ny * a);
        g.FillPolygon(_brush, _tri);
    }

    /// <summary>La tarea enlazada (Pro): su color, el título, los focos ya apuntados y «›» (un clic pasa a la siguiente). Sin
    /// tarea, «Enlazar con una tarea» atenuado. Sin el Pro, ese texto con el chip «Pro».</summary>
    private void FocusTaskRow(Graphics g, FocusRects r, float s, FocusView v)
    {
        var row = r.Task;
        if (v.Locked)
        {
            FocusLockedRow(g, r, row, s, v.Texts.LockedTask);
            return;
        }
        var t = v.Texts;
        bool hover = v.Hover.Control == FocusControl.Task && v.HoverIntensity > 0f, pressed = v.Pressed.Control == FocusControl.Task;
        if (hover || pressed)
        {
            int a = pressed ? MusicHoverFill.A : (int)(MusicHoverFill.A * Math.Clamp(v.HoverIntensity, 0f, 1f));
            var fill = Color.FromArgb(Math.Min(255, a), 255, 255, 255);
            if (pressed) fill = Darken(fill, MusicPressDarken);
            using var path = RoundedRect(Rectangle.Round(RectangleF.Inflate(row, 4f * s, 0f)), ShelfRowRadius * s);
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }
        float d = 7f * s, cy = row.Top + row.Height / 2f;
        if (v.TaskColor >= 0)
        {
            _brush.Color = Fade(TaskColor(v.TaskColor));
            g.FillEllipse(_brush, row.Left, cy - d / 2f, d, d);
        }
        else
        {
            SetPen(_t.TextMuted, Math.Max(1f, 1.2f * s));
            g.DrawEllipse(_pen, row.Left + 0.5f, cy - d / 2f + 0.5f, d - 1f, d - 1f);
        }
        float right = row.Right;
        DrawText(g, "›", _body!, _t.TextMuted, RectangleF.FromLTRB(right - 10f * s, row.Top, right, row.Bottom), _right);
        right -= 14f * s;
        if (t.TaskCount is { } count)
        {
            float cw = TextW(g, count) + 1f;
            DrawText(g, count, _caption!, _t.TextMuted, RectangleF.FromLTRB(right - cw, row.Top, right, row.Bottom), _right);
            right -= cw + 3f * s;
            float dot = 5f * s;
            _brush.Color = Fade(FocusColor(FocusPhase.Focus));
            g.FillEllipse(_brush, right - dot, cy - dot / 2f, dot, dot);
            right -= dot + Gap * s;
        }
        DrawText(g, t.TaskLine, _caption!, t.TaskMuted ? _t.TextMuted : _t.Foreground,
            RectangleF.FromLTRB(row.Left + d + 7f * s, row.Top, Math.Max(row.Left + d + 7f * s, right), row.Bottom), _left);
    }

    /// <summary>La semana (Pro): siete barras (hoy, la última, en el color del foco) y «Esta semana: 12 focos · 5 h».</summary>
    private void FocusStatsRow(Graphics g, FocusRects r, float s, FocusView v)
    {
        var row = r.Stats;
        if (v.Locked)
        {
            FocusLockedRow(g, r, row, s, v.Texts.LockedStats);
            return;
        }
        int max = 1;
        for (int i = 0; i < v.Week.Count; i++) max = Math.Max(max, v.Week[i]);
        for (int i = 0; i < PomodoroTimer.WeekDays && i < v.Week.Count; i++)
        {
            var b = r.StatsBar(i);
            int n = v.Week[i];
            float bh = n <= 0 ? Math.Max(1.5f, 2f * s) : Math.Max(2f * s, b.Height * n / max);
            _brush.Color = Fade(n <= 0 ? FocusTrack : i == PomodoroTimer.WeekDays - 1 ? FocusColor(FocusPhase.Focus) : FocusBarFill);
            g.FillRectangle(_brush, b.Left, b.Bottom - bh, b.Width, bh);
        }
        DrawText(g, v.Texts.WeekLine, _caption!, _t.TextMuted, r.StatsLabel, _left);
    }

    /// <summary>Fila sin el Pro: qué haría, atenuado, y el chip «Pro» al final (la fila entera abre la tarjeta).</summary>
    private void FocusLockedRow(Graphics g, FocusRects r, RectangleF row, float s, string text)
    {
        var chip = r.Chip(row);
        DrawText(g, text, _caption!, _t.TextMuted, RectangleF.FromLTRB(row.Left, row.Top, Math.Max(row.Left, chip.Left - Gap * s),
            row.Bottom), _left);
        ProChip(g, chip, s);
    }

    // ---------------------------------------------------------------- notch cerrado

    /// <summary>Mini anillo: pista, arco de lo que queda (con lo que se enseña cerrado) y, en pausa, dos barritas dentro.</summary>
    private void FocusMiniRing(Graphics g, float x, float cy, float d, FocusView v)
    {
        float w = Math.Max(1.5f, d * FocusFace.MiniStroke / FocusFace.MiniRing);
        var r = new RectangleF(x + w / 2f, cy - d / 2f + w / 2f, d - w, d - w);
        SetPen(FocusTrack, w);
        g.DrawEllipse(_pen, r);
        float left = 1f - Math.Clamp(v.CompactProgress, 0f, 1f);
        if (left > 0.0005f)
        {
            SetPen(FocusArcColor(v), w);
            g.DrawArc(_pen, r, -90f, 360f * left);
        }
        if (v.Run != FocusRun.Paused || d < 12f) return;
        float bh = d * 0.32f, bw = Math.Max(1f, d * 0.09f), cx = x + d / 2f;
        _brush.Color = Fade(_t.TextMuted);
        g.FillRectangle(_brush, cx - bw * 1.6f, cy - bh / 2f, bw, bh);
        g.FillRectangle(_brush, cx + bw * 0.6f, cy - bh / 2f, bw, bh);
    }

    /// <summary>Reposo con el foco en marcha: el mini anillo y «25 min» juntos y centrados. Con la cuota de 5 h en aviso
    /// (<see cref="QuotaWarns"/>), también su anillo a la derecha: el mini anillo no tapa la cuota alta.</summary>
    private void PaintFocusIdle(Graphics g, RectangleF pill, float s, NotchFrame f, FocusView v)
    {
        float d = FocusFace.MiniRing * s, gap = FocusFace.MiniGap * s;
        float tw = TextW(g, v.Texts.Compact);
        bool quota = QuotaWarns(f);
        float qgap = FocusFace.IdleQuotaGap * s, qd = FocusFace.IdleQuotaRing * s;
        float total = d + gap + tw + (quota ? qgap + qd : 0f);
        float x = pill.Left + Math.Max(0f, (pill.Width - total) / 2f), cy = pill.Top + pill.Height / 2f;
        FocusMiniRing(g, x, cy, d, v);
        DrawText(g, v.Texts.Compact, _caption!, v.Run == FocusRun.Paused ? _t.TextMuted : _t.Foreground,
            new RectangleF(x + d + gap, pill.Top, tw + 1f, pill.Height), _left);
        float qx = x + d + gap + tw + qgap;
        if (quota && qx + qd <= pill.Right + 0.5f) Ring(g, qx, cy, qd, f.Quota!.FiveHour!.UtilizationPct);
    }

    /// <summary>Vista previa sobre el reposo con el foco: el mini anillo y su tiempo a la izquierda; el anillo de 5 h con su %
    /// a la derecha (la cuota no desaparece al acercar el ratón).</summary>
    private void PaintFocusPeek(Graphics g, RectangleF pill, float s, NotchFrame f, FocusView v)
    {
        float cy = pill.Top + pill.Height / 2f, x = pill.Left + PadX * s;
        float d = FocusFace.MiniRing * s;
        FocusMiniRing(g, x, cy, d, v);
        x += d + FocusFace.MiniGap * s;
        float tw = TextW(g, v.Texts.Compact);
        DrawText(g, v.Texts.Compact, _caption!, v.Run == FocusRun.Paused ? _t.TextMuted : _t.Foreground,
            new RectangleF(x, pill.Top, tw + 1f, pill.Height), _left);

        var five = f.Quota?.FiveHour;
        string p5 = Pct(five);
        float right = pill.Right - PadX * s;
        float pw = TextW(g, p5);
        DrawText(g, p5, _caption!, PctColor(five, text: true), new RectangleF(right - pw - 1f, pill.Top, pw + 1f, pill.Height), _left);
        right -= pw + PairPctGap * s;
        float lw = TextW(g, "5h");
        DrawText(g, "5h", _caption!, _t.TextMuted, new RectangleF(right - lw - 1f, pill.Top, lw + 1f, pill.Height), _left);
        right -= lw + PairIconGap * s;
        float ring = NotchMotion.RingDiameterPeek * s;
        if (right - ring > x + tw + Gap * s) Ring(g, right - ring, cy, ring, five?.UtilizationPct);
    }

    /// <summary>Mínimo con el foco en marcha (y la cuota sin aviso: si no, su color manda): la raya se llena con lo que queda,
    /// en el color de la fase.</summary>
    private void FocusMinimalBar(Graphics g, GraphicsPath path, RectangleF shape, FocusView v)
    {
        float left = 1f - Math.Clamp(v.CompactProgress, 0f, 1f);
        if (left <= 0f) return;
        var state = g.Save();
        try
        {
            g.SetClip(path, CombineMode.Intersect);
            _brush.Color = FocusArcColor(v);
            g.FillRectangle(_brush, shape.Left, shape.Top, shape.Width * left, shape.Height);
        }
        finally { g.Restore(state); }
    }

    // ---------------------------------------------------------------- aviso del fin de fase

    /// <summary>Título, detalle y botón del aviso (idioma vigente), formados una vez por fases.</summary>
    private (string Title, string Detail, string Button) FocusAlertTexts(FocusPhase ended, FocusPhase next)
    {
        if (!ReferenceEquals(_focusAlertStrings, Strings))
        {
            _focusAlertTexts.Clear();
            _focusAlertStrings = Strings;
        }
        if (!_focusAlertTexts.TryGetValue((ended, next), out var texts))
        {
            texts = (FocusText.AlertTitle(ended, Strings), FocusText.AlertDetail(next, Strings), FocusText.AlertButton(next, Strings));
            _focusAlertTexts[(ended, next)] = texts;
        }
        return texts;
    }

    /// <summary>
    /// «¡Foco terminado!» / «Fin del descanso» con «Siguiente: …» y, abajo a la derecha, un botón ancho («Empezar descanso» /
    /// «Empezar foco») que se arma como los del recordatorio (<see cref="NotchFrame.PermissionArm"/>). El anillo de la izquierda,
    /// completo en el color de la fase que acabó, entra con un bote (<see cref="FocusView.Pop"/>).
    /// </summary>
    private void PaintFocusAlert(Graphics g, RectangleF pill, float s, NotchFrame f, NotchAlert a)
    {
        var (ended, next) = FocusText.AlertPhases(a.Title, a.Detail);
        var (title, detail, button) = FocusAlertTexts(ended, next);
        var wide = NotchGeometry.FocusAlertButton(pill, s);
        float x = pill.Left + PadX * s, right = pill.Right - PadX * s, icon = AlertIconSize * s, lineH = 18f * s;
        float areaBottom = wide.IsEmpty ? pill.Bottom : wide.Top - 2f * s;
        float blockTop = pill.Top + Math.Max(0f, (areaBottom - pill.Top - 2f * lineH) / 2f);
        FocusDoneRing(g, x, blockTop + lineH / 2f, icon, FocusColor(ended), f.Focus?.Pop ?? 1f, s);
        x += icon + Gap * s;
        var col = new RectangleF(x, blockTop, Math.Max(0f, right - x), lineH);
        DrawText(g, title, _title!, FocusPalette.Text(ended, _t), col, _left);
        col.Y += lineH;
        DrawText(g, detail, _body!, _t.Foreground, col, _left);
        if (wide.IsEmpty) return;   // la forma aún se está abriendo
        PermissionButton(g, wide, allow: true, s, f, button);
    }

    /// <summary>Anillo completo del fin de fase con el visto bueno dentro, escalado desde su centro por el bote.</summary>
    private void FocusDoneRing(Graphics g, float x, float cy, float d, Color color, float pop, float s)
    {
        float k = Math.Clamp(pop, 0.2f, 1.4f), dd = d * k, cx = x + d / 2f;
        float w = Math.Max(1.5f, 2.4f * s * k);
        var r = new RectangleF(cx - dd / 2f + w / 2f, cy - dd / 2f + w / 2f, dd - w, dd - w);
        if (r.Width <= 1f) return;
        SetPen(color, w);
        g.DrawEllipse(_pen, r);
        SetPen(color, Math.Max(1.4f, 1.8f * s * k));
        _focusCheck[0] = new PointF(r.Left + r.Width * 0.28f, r.Top + r.Height * 0.53f);
        _focusCheck[1] = new PointF(r.Left + r.Width * 0.44f, r.Top + r.Height * 0.68f);
        _focusCheck[2] = new PointF(r.Left + r.Width * 0.72f, r.Top + r.Height * 0.36f);
        g.DrawLines(_pen, _focusCheck);
    }

    /// <summary>Glifo del pomodoro dentro del círculo de un aviso (solo lectura e integrado): un aro.</summary>
    private void FocusGlyph(Graphics g, RectangleF r, float d)
    {
        float inset = d * 0.27f;
        g.DrawEllipse(_pen, RectangleF.Inflate(r, -inset, -inset));
    }

    // ---------------------------------------------------------------- fuentes

    private float FocusHeaderWidth(Graphics g, string text)
    {
        if (!ReferenceEquals(_focusHeaderFont, _title) || !string.Equals(_focusHeaderText, text, StringComparison.Ordinal))
        {
            _focusHeaderW = g.MeasureString(text, _title!, int.MaxValue, _left).Width;
            _focusHeaderText = text;
            _focusHeaderFont = _title;
        }
        return _focusHeaderW;
    }

    private void EnsureFocusFonts(float scale)
    {
        if (Math.Abs(scale - _focusClockScale) < 0.001f && _focusClock is not null) return;
        _focusClock?.Dispose();
        _focusClock = new Font(Typography.Body.FontFamily.Name, 22f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _focusClockScale = scale;
    }

    private void DisposeFocus()
    {
        _focusClock?.Dispose();
        _focusClock = null;
    }
}
