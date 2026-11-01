using System.Drawing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI.Notch;

/// <summary>Página Tareas y aviso de recordatorio del notch (F5 · B5). Archivo aparte para no mezclarse con el resto.</summary>
public sealed partial class NotchContentPainter
{
    /// <summary>Paleta de las tareas (índice 0..5 de <see cref="NotchTask.Color"/>): azul eléctrico, morado, verde, ámbar,
    /// rojo y rosa; todos legibles sobre el negro de la pastilla.</summary>
    internal static readonly Color[] TaskPalette =
    {
        Color.FromArgb(0x4D, 0x8D, 0xFF), Color.FromArgb(0xA6, 0x6B, 0xFF), Color.FromArgb(0x3D, 0xD6, 0x8C),
        Color.FromArgb(0xFF, 0xB5, 0x47), Color.FromArgb(0xFF, 0x5C, 0x6C), Color.FromArgb(0xFF, 0x7A, 0xC6),
    };

    internal static Color TaskColor(int i) => TaskPalette[Math.Clamp(i, 0, TaskPalette.Length - 1)];

    // Campo de texto de la edición: un poco más claro con el foco del teclado.
    private static readonly Color TaskFieldFill = Color.FromArgb(16, 255, 255, 255);
    private static readonly Color TaskFieldFocus = Color.FromArgb(30, 255, 255, 255);
    private const float TaskDoneDim = 0.5f;

    // ---------------------------------------------------------------- página Tareas

    /// <summary>
    /// Página Tareas: cabecera con «+ Añadir» (o, editando, el campo de texto con «Guardar» y debajo las horas rápidas) y
    /// una fila por tarea: círculo para marcar hecha (con el color de la tarea), título, hora (vencida en ámbar) y, con el
    /// ratón encima, el aspa para quitarla. Lo que no cabe, «+N más». Geometría de <see cref="TasksLayout"/>.
    /// </summary>
    private void PaintTasksPage(Graphics g, RectangleF inner, float s, TasksView? v)
    {
        if (v is null)
        {
            PaintPlaceholderPage(g, inner, s, Strings.NotchPageTasks, Strings.NotchNoTasks);
            return;
        }
        bool editing = v.Draft is not null;
        var r = TasksLayout.Compute(inner, s, editing);
        if (v.Draft is { } d) TaskEditor(g, r, d, v, s);
        else
        {
            DrawText(g, Strings.NotchPageTasks, _title!, _t.Foreground, r.Title, _left);
            TaskCapsule(g, r.AddButton, Strings.NotchTaskAdd, v, new TaskHit(TaskControl.Add), s, on: false, enabled: true);
        }

        int count = v.Items.Count;
        if (count == 0)
        {
            if (!editing) DrawText(g, Strings.NotchNoTasks, _body!, _t.TextMuted, r.Rows > 0 ? r.Row(0) : r.List, _center);
            return;
        }
        var w = ShelfLayout.Window(count, r.Rows, v.Offset);
        for (int k = 0; k < w.Shown; k++) TaskRow(g, r, k, v, w.First + k, s);
        if (w.More > 0)
        {
            var row = r.Row(w.Shown);
            float x = r.Check(w.Shown).Right + ShelfLayout.IconGap * s;
            DrawText(g, string.Format(Strings.Culture, Strings.NotchMoreSessionsFmt, w.More), _caption!, _t.TextMuted,
                RectangleF.FromLTRB(x, row.Top, row.Right, row.Bottom), _left);
        }
    }

    /// <summary>Una tarea: realce (hover/pulsación), círculo con el color, título, hora y aspa con el ratón encima.</summary>
    private void TaskRow(Graphics g, TasksRects r, int visible, TasksView v, int i, float s)
    {
        var t = v.Items[i];
        var row = r.Row(visible);
        bool hoverRow = v.Hover.Index == i && v.HoverIntensity > 0f
            && v.Hover.Control is TaskControl.Row or TaskControl.Check or TaskControl.Delete;
        bool pressedRow = v.Pressed.Index == i && v.Pressed.Control is TaskControl.Row or TaskControl.Check or TaskControl.Delete;

        int a = hoverRow ? (int)(MusicHoverFill.A * Math.Clamp(v.HoverIntensity, 0f, 1f)) : 0;
        if (pressedRow) a = Math.Max(a, MusicHoverFill.A);
        if (a > 0)
        {
            var fill = Color.FromArgb(Math.Min(255, a), 255, 255, 255);
            if (pressedRow) fill = Darken(fill, MusicPressDarken);
            using var path = RoundedRect(Rectangle.Round(row), ShelfRowRadius * s);
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }

        float saved = _alpha;
        if (t.Done) _alpha = Quantize(_alpha * TaskDoneDim);
        try
        {
            var check = r.Check(visible);
            if (v.Pressed.Control == TaskControl.Check && v.Pressed.Index == i)
                check = RectangleF.Inflate(check, -Math.Max(1f, MathF.Round(s)), -Math.Max(1f, MathF.Round(s)));
            TaskCheck(g, check, TaskColor(t.Color), t.Done, s);

            var time = r.TimeLabel(visible);
            string? label = TaskList.When(t) is { } when
                ? TaskTimes.Label(when, v.NowLocal, Strings.Culture, v.Use24h, Strings.NotchTomorrow) : null;
            float tx = check.Right + ShelfLayout.IconGap * s;
            float titleRight = label is null ? r.Delete(visible).Left - 6f * s : time.Left - 4f * s;
            // Focos del pomodoro apuntados en ella (página Foco, Pro): un punto del color del foco y cuántos, antes de la hora.
            if (t.Focuses > 0)
            {
                string n = FocusCountText(t.Focuses);
                float nw = TextW(g, n) + 1f, dot = 5f * s, cy = row.Top + row.Height / 2f;
                DrawText(g, n, _caption!, _t.TextMuted, RectangleF.FromLTRB(titleRight - nw, row.Top, titleRight, row.Bottom), _right);
                titleRight -= nw + 3f * s;
                _brush.Color = Fade(FocusColor(FocusPhase.Focus));
                g.FillEllipse(_brush, titleRight - dot, cy - dot / 2f, dot, dot);
                titleRight -= dot + 6f * s;
            }
            DrawText(g, t.Title, _body!, _t.Foreground, RectangleF.FromLTRB(tx, row.Top, Math.Max(tx, titleRight), row.Bottom), _left);
            if (label is not null)
            {
                bool overdue = !t.Done && TaskList.When(t) <= v.NowLocal;
                DrawText(g, label, _caption!, overdue ? _t.WarnText : _t.TextMuted, time, _right);
            }
            if (hoverRow || pressedRow) TaskDelete(g, r.Delete(visible), v, i, s);
        }
        finally { _alpha = saved; }
    }

    // Cifras de los focos de una tarea, hechas una vez (el painter no formatea texto por frame).
    private static readonly string[] FocusCounts = Enumerable.Range(0, 100).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    /// <summary>«3» (hasta 99; luego «99+»).</summary>
    internal static string FocusCountText(int n) => n < FocusCounts.Length ? FocusCounts[Math.Max(0, n)] : "99+";

    /// <summary>Círculo de «hecha»: aro del color de la tarea; hecha = relleno con el visto bueno en negro.</summary>
    private void TaskCheck(Graphics g, RectangleF r, Color color, bool done, float s)
    {
        float w = Math.Max(1.25f, 1.5f * s);
        if (done)
        {
            _brush.Color = Fade(color);
            g.FillEllipse(_brush, r);
            SetPen(Color.Black, w);
            g.DrawLines(_pen, new[]
            {
                new PointF(r.Left + r.Width * 0.28f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.44f, r.Top + r.Height * 0.68f),
                new PointF(r.Left + r.Width * 0.72f, r.Top + r.Height * 0.36f),
            });
            return;
        }
        SetPen(color, w);
        g.DrawEllipse(_pen, RectangleF.Inflate(r, -w / 2f, -w / 2f));
    }

    /// <summary>Aspa de quitar: más clara con el ratón justo encima.</summary>
    private void TaskDelete(Graphics g, RectangleF r, TasksView v, int i, float s)
    {
        bool over = v.Hover.Control == TaskControl.Delete && v.Hover.Index == i;
        bool pressed = v.Pressed.Control == TaskControl.Delete && v.Pressed.Index == i;
        if (pressed) r = RectangleF.Inflate(r, -Math.Max(1f, MathF.Round(s)), -Math.Max(1f, MathF.Round(s)));
        float k = r.Width * 0.3f;
        SetPen(over || pressed ? _t.Foreground : _t.TextMuted, Math.Max(1.25f, 1.5f * s));
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
        g.DrawLine(_pen, cx - k, cy - k, cx + k, cy + k);
        g.DrawLine(_pen, cx - k, cy + k, cx + k, cy - k);
    }

    /// <summary>Edición en línea: punto de color, campo con el título (o el texto de ayuda) y el cursor, «Guardar» y las
    /// horas rápidas. El cursor es fijo (sin parpadeo: ningún temporizador mientras se escribe).</summary>
    private void TaskEditor(Graphics g, TasksRects r, TaskDraftView d, TasksView v, float s)
    {
        // Punto de color (clic = siguiente color).
        bool dotHover = v.Hover.Control == TaskControl.Color && v.HoverIntensity > 0f;
        var dot = dotHover ? RectangleF.Inflate(r.ColorDot, s, s) : r.ColorDot;
        _brush.Color = Fade(TaskColor(d.Color));
        g.FillEllipse(_brush, dot);

        // Campo de texto.
        bool focus = d.Field == TaskField.Title;
        using (var path = RoundedRect(Rectangle.Round(r.Field), 6f * s))
        {
            _brush.Color = Fade(focus ? TaskFieldFocus : TaskFieldFill);
            g.FillPath(_brush, path);
        }
        var text = RectangleF.Inflate(r.Field, -6f * s, 0f);
        bool empty = d.Title.Length == 0;
        if (empty) DrawText(g, Strings.NotchTaskPlaceholder, _body!, _t.TextMuted, text, _left);
        float caretX = text.Left;
        if (!empty)
        {
            // Si no cabe, se ve el final (donde se escribe).
            float tw = MeasureBody(g, d.Title);
            var state = g.Save();
            g.SetClip(text, System.Drawing.Drawing2D.CombineMode.Intersect);
            var fmt = tw > text.Width ? RightNoTrim : LeftNoTrim;
            _brush.Color = Fade(_t.Foreground);
            if (text.Width > 1f) g.DrawString(d.Title, _body!, _brush, text, fmt);
            g.Restore(state);
            caretX = Math.Min(text.Right, text.Left + tw);
        }
        if (focus) Caret(g, caretX, r.Field, s);

        TaskCapsule(g, r.SaveButton, Strings.NotchTaskSave, v, new TaskHit(TaskControl.Save), s, on: d.CanSave, enabled: d.CanSave);

        for (int i = 0; i < TasksLayout.Chips; i++)
        {
            var q = TasksLayout.ChipQuick(i);
            string label = q switch
            {
                TaskQuick.In15Min => Strings.NotchTaskIn15,
                TaskQuick.In1Hour => Strings.NotchTaskIn1h,
                TaskQuick.Tomorrow9 => Strings.NotchTaskTomorrow9,
                _ => d.TimeDigits.Length > 0 ? TaskTimes.FormatDigits(d.TimeDigits) : Strings.NotchTaskCustom,
            };
            var chip = r.Chip(i);
            TaskCapsule(g, chip, label, v, new TaskHit(TaskControl.Quick, (int)q), s, on: d.Quick == q, enabled: true,
                warn: q == TaskQuick.Custom && !d.TimeValid);
            if (q == TaskQuick.Custom && d.Field == TaskField.Time && d.TimeDigits.Length > 0)
                Caret(g, chip.Left + chip.Width / 2f + MeasureCaption(g, label) / 2f + s, chip, s);
            else if (q == TaskQuick.Custom && d.Field == TaskField.Time)
                Caret(g, chip.Right - 8f * s, chip, s);
        }
    }

    /// <summary>Cursor de texto: raya vertical de 1 px, fija.</summary>
    private void Caret(Graphics g, float x, RectangleF box, float s)
    {
        SetPen(_t.Foreground, Math.Max(1f, MathF.Round(s)));
        float pad = 4f * s;
        g.DrawLine(_pen, x, box.Top + pad, x, box.Bottom - pad);
    }

    /// <summary>Cápsula translúcida con hover y pulsación (como «Vaciar» y los botones de la música). <paramref name="on"/>:
    /// elegida (más clara); <paramref name="enabled"/> = false: texto atenuado; <paramref name="warn"/>: texto en ámbar.</summary>
    private void TaskCapsule(Graphics g, RectangleF r, string text, TasksView v, TaskHit me, float s, bool on, bool enabled,
        bool warn = false)
    {
        if (r.IsEmpty) return;
        bool pressed = enabled && v.Pressed == me;
        if (pressed) r = RectangleF.Inflate(r, -Math.Max(1f, MathF.Round(s)), -Math.Max(1f, MathF.Round(s)));
        var fill = on ? LyricsButtonOn : ShelfButtonFill;
        if (enabled && v.Hover == me && v.HoverIntensity > 0f)
            fill = Color.FromArgb(Math.Min(255, fill.A + (int)(MusicHoverFill.A * Math.Clamp(v.HoverIntensity, 0f, 1f))), fill);
        if (pressed) fill = Darken(fill, MusicPressDarken);
        using (var path = RoundedRect(Rectangle.Round(r), r.Height / 2f))
        {
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }
        DrawText(g, text, _caption!, warn ? _t.WarnText : enabled ? _t.Foreground : _t.TextMuted, r, _center);
    }

    private float MeasureBody(Graphics g, string t) => g.MeasureString(t, _body!, int.MaxValue, LeftNoTrim).Width;
    private float MeasureCaption(Graphics g, string t) => g.MeasureString(t, _caption!, int.MaxValue, LeftNoTrim).Width;

    // Sin puntos suspensivos: en el campo se ve el texto tal cual (recortado por el clip), con el final a la vista.
    private StringFormat? _leftNoTrimFmt, _rightNoTrimFmt;
    private StringFormat LeftNoTrim => _leftNoTrimFmt ??= NoTrim(_left);
    private StringFormat RightNoTrim => _rightNoTrimFmt ??= NoTrim(_right);

    private static StringFormat NoTrim(StringFormat f)
    {
        var c = (StringFormat)f.Clone();
        c.Trimming = StringTrimming.None;
        return c;
    }

    private void DisposeTaskFormats()
    {
        _leftNoTrimFmt?.Dispose();
        _rightNoTrimFmt?.Dispose();
        _leftNoTrimFmt = _rightNoTrimFmt = null;
    }

    // ---------------------------------------------------------------- aviso de recordatorio

    /// <summary>Recordatorio: reloj, «Recordatorio» y el título de la tarea arriba; «+10 min» y «Hecha» abajo a la derecha
    /// (la MISMA geometría que el Permiso con botones, la que usa <c>NotchForm</c> para el clic).</summary>
    private void PaintReminderActions(Graphics g, RectangleF pill, float s, NotchFrame f, NotchAlert a)
    {
        var (title, detail, color) = AlertLines(a, f);
        var (snooze, done) = NotchGeometry.PermissionButtons(pill, s);
        float x = pill.Left + PadX * s, right = pill.Right - PadX * s, icon = 18f * s, lineH = 18f * s;
        int lines = detail is null ? 1 : 2;
        float areaBottom = snooze.IsEmpty ? pill.Bottom : snooze.Top - 2f * s;
        float blockTop = pill.Top + Math.Max(0f, (areaBottom - pill.Top - lines * lineH) / 2f);
        AlertIcon(g, a.Kind, x, blockTop + lineH / 2f, icon, color);
        x += icon + Gap * s;
        var col = new RectangleF(x, blockTop, Math.Max(0f, right - x), lineH);
        DrawText(g, title, _title!, color, col, _left);
        if (detail is not null)
        {
            col.Y += lineH;
            DrawText(g, detail, _body!, _t.Foreground, col, _left);
        }
        if (snooze.IsEmpty) return;   // la forma aún se está abriendo
        PermissionButton(g, snooze, allow: false, s, f, Strings.NotchReminderSnooze);
        PermissionButton(g, done, allow: true, s, f, Strings.NotchReminderDone);
    }

    /// <summary>Glifo del recordatorio dentro del círculo del aviso: esfera de reloj con las agujas en negro.</summary>
    private void ReminderGlyph(Graphics g, RectangleF r, float d)
    {
        float cx = r.Left + d / 2f, cy = r.Top + d / 2f;
        g.DrawLine(_pen, cx, cy, cx, r.Top + d * 0.24f);
        g.DrawLine(_pen, cx, cy, r.Left + d * 0.70f, cy);
    }
}
