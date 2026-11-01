using System.Drawing.Drawing2D;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// El aviso de actualización (<see cref="NotchFrame.Update"/>): en el reposo y la vista previa, un punto discreto de acento
/// pegado al borde derecho; en el expandido, la línea de arriba (si no la ocupa un aviso) con «Nueva versión 0.6» y, a la
/// derecha, el anillo de la descarga o el botón «Actualizar» con el acento. La geometría es la de
/// <see cref="NotchUpdateLayout"/>, la MISMA con que <c>NotchForm</c> acierta el clic.
/// </summary>
public sealed partial class NotchContentPainter
{
    private static readonly Color UpdateTrack = Color.FromArgb(48, 255, 255, 255);

    /// <summary>Texto sobre el acento: el lado que más contrasta (<see cref="Theme.OnAccent"/>; blanco con el violeta).</summary>
    private Color OnAccent => _t.OnAccent;

    /// <summary>Botón con el acento: con el ratón encima, el realce que no le quita contraste al texto
    /// (<see cref="ColorMath.HoverFill"/>); pulsado, además, un 6 % más oscuro.</summary>
    private Color AccentButtonFill(bool hover, bool pressed)
    {
        var fill = hover || pressed ? ColorMath.HoverFill(_t.Accent) : _t.Accent;
        return pressed ? Darken(fill, ButtonPressDarken) : fill;
    }

    private void PaintUpdateDot(Graphics g, RectangleF pill, float s)
    {
        var d = NotchUpdateLayout.Dot(pill, s);
        _brush.Color = Fade(_t.Accent);
        g.FillEllipse(_brush, d);
    }

    private void PaintUpdateRow(Graphics g, RectangleF row, float s, NotchUpdateView u)
    {
        float cy = row.Top + row.Height / 2f, dot = 6f * s;
        _brush.Color = Fade(_t.Accent);
        g.FillEllipse(_brush, row.Left + 1f * s, cy - dot / 2f, dot, dot);
        float x = row.Left + 14f * s;

        float right = row.Right;
        if (u.Button)
        {
            var b = NotchUpdateLayout.Button(row, s);
            if (!b.IsEmpty)
            {
                var r = b;
                if (u.Pressed) r = Rectangle.Inflate(r, -Math.Max(1, (int)Math.Round(s)), -Math.Max(1, (int)Math.Round(s)));
                var fill = AccentButtonFill(u.Hover, u.Pressed);
                using (var path = RoundedRect(r, ButtonRadius * s))
                {
                    _brush.Color = Fade(fill);
                    g.FillPath(_brush, path);
                }
                DrawText(g, u.Action!, _title!, OnAccent, r, _center);
                right = b.Left - Gap * s;
            }
        }
        else if (!u.Ready)
        {
            // Descargando: anillo con el progreso (la pista al 19 % y el arco con el acento) y el % a su izquierda.
            var ring = NotchUpdateLayout.Ring(row, s);
            float w = Math.Max(1.5f, 2f * s);
            var rr = RectangleF.Inflate(ring, -w / 2f, -w / 2f);
            SetPen(UpdateTrack, w);
            g.DrawEllipse(_pen, rr);
            if (u.Progress > 0f)
            {
                SetPen(_t.Accent, w);
                g.DrawArc(_pen, rr, -90f, 360f * Math.Clamp(u.Progress, 0f, 1f));
            }
            right = ring.Left - Gap * s;
        }

        float statusW = u.Status.Length == 0 ? 0f : g.MeasureString(u.Status, _caption!, int.MaxValue, _left).Width + 1f;
        float statusX = Math.Max(x, right - statusW);
        if (statusW > 0f)
            DrawText(g, u.Status, _caption!, _t.TextMuted, new RectangleF(statusX, row.Top, right - statusX, row.Height), _right);
        DrawText(g, u.Label, _body!, _t.Foreground,
            new RectangleF(x, row.Top, Math.Max(0f, statusX - Gap * s - x), row.Height), _left);
    }

    /// <summary>¿Lleva el frame la línea de arriba del expandido? (el aviso integrado o, si no hay, la actualización).</summary>
    private static bool HasTopLine(NotchFrame f) => f.Alert is not null || f.Update is not null;
}
