using System.Drawing.Drawing2D;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// El candado del Pro (edición gratis): la página con candado (<see cref="NotchFrame.Locked"/>) enseña su nombre con el chip
/// «Pro» discreto, con el acento, qué trae y la pista para abrir la tarjeta. La geometría es la de
/// <see cref="NotchProLayout"/>, la MISMA con que <c>NotchForm</c> acierta el clic. Los textos son los del idioma, sin
/// formatear nada por frame.
/// </summary>
public sealed partial class NotchContentPainter
{
    /// <summary>Fondo y borde del chip: el acento muy tenue sobre el negro (discreto), con el texto en el acento legible.</summary>
    private const int ChipFillAlpha = 40, ChipBorderAlpha = 150;

    private void PaintLockedPage(Graphics g, RectangleF inner, float s, NotchPage page)
    {
        var (header, pitch) = page switch
        {
            NotchPage.Music => (Strings.NotchPageMusic, Strings.NotchProLockedMusic),
            NotchPage.Tasks => (Strings.NotchPageTasks, Strings.NotchProLockedTasks),
            _ => (Strings.NotchPageShelf, Strings.NotchProLockedShelf),
        };
        var head = NotchProLayout.LockedHeader(inner, s);
        var chip = NotchProLayout.Chip(inner, s);
        DrawText(g, header, _title!, _t.Foreground,
            RectangleF.FromLTRB(head.Left, head.Top, Math.Max(head.Left, chip.Left - Gap * s), head.Bottom), _left);
        ProChip(g, chip, s);

        var (body, hint) = NotchProLayout.LockedBody(inner, s);
        if (body.Width > 1f && body.Height > 1f)
        {
            _brush.Color = Fade(_t.TextMuted);
            g.DrawString(pitch, _body!, _brush, body, _wrap);
        }
        DrawText(g, Strings.NotchProLockedHint, _caption!, _t.AccentText, hint, _left);
    }

    /// <summary>Fondo del botón secundario de la tarjeta (blanco translúcido, como «Denegar») y alfa de los botones ocupados.</summary>
    private static readonly Color ProSecondaryFill = Color.FromArgb(34, 255, 255, 255);
    private const float ProBusyAlpha = 0.45f;

    /// <summary>
    /// Tarjeta del Pro (<see cref="NotchFrame.ProCard"/>): ocupa el expandido entero. Arriba «Muesca Pro» y, a la derecha,
    /// el precio, los días de prueba o «Activado»; debajo lo que incluye en dos columnas con un visto del acento; abajo la
    /// línea de estado y los botones («Comprar» con el acento y «Restaurar compra», o el ancho de la Store). Geometría de
    /// <see cref="NotchProLayout"/>.
    /// </summary>
    private void PaintProCard(Graphics g, RectangleF pill, float s, NotchProCardView v)
    {
        var inner = NotchProLayout.CardInner(pill, s);
        var title = NotchProLayout.CardTitle(inner, s);
        float subW = v.Subtitle.Length == 0 ? 0f : Math.Min(title.Width * 0.6f, g.MeasureString(v.Subtitle, _caption!, int.MaxValue, _left).Width + 1f);
        DrawText(g, v.Title, _title!, _t.Foreground, RectangleF.FromLTRB(title.Left, title.Top, title.Right - subW - Gap * s, title.Bottom), _left);
        if (subW > 0f)
            DrawText(g, v.Subtitle, _caption!, v.Primary is null && v.Secondary is null && v.Status is null ? _t.AccentText : _t.TextMuted,
                RectangleF.FromLTRB(title.Right - subW, title.Top, title.Right, title.Bottom), _right);

        for (int i = 0; i < v.Features.Count && i < 2 * NotchProLayout.CardFeatureRows; i++)
        {
            var r = NotchProLayout.CardFeature(inner, s, i);
            float cy = r.Top + r.Height / 2f, d = 8f * s;
            SetPen(_t.Accent, Math.Max(1.25f, 1.5f * s));
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            _tri[0] = new PointF(r.Left + 1f * s, cy);
            _tri[1] = new PointF(r.Left + 1f * s + d * 0.35f, cy + d * 0.35f);
            _tri[2] = new PointF(r.Left + 1f * s + d, cy - d * 0.4f);
            g.DrawLines(_pen, _tri);
            g.Restore(state);
            DrawText(g, v.Features[i], _caption!, _t.Foreground,
                RectangleF.FromLTRB(r.Left + 16f * s, r.Top, r.Right - Gap * s, r.Bottom), _left);
        }

        if (v.Status is { } status)
            DrawText(g, status, _caption!, v.StatusIsError ? _t.WarnText : _t.TextMuted, NotchProLayout.CardStatus(inner, s, v), _left);
        if (v.Secondary is { } secondary)
            ProButton(g, NotchProLayout.CardSecondary(inner, s), secondary, primary: false, v, ProCardButton.Secondary, s);
        if (v.Primary is { } primaryText)
            ProButton(g, NotchProLayout.CardPrimary(inner, s, v.Wide), primaryText, primary: true, v, ProCardButton.Primary, s);
    }

    /// <summary>Botón de la tarjeta: el principal con el acento (con <see cref="OnAccent"/>, como «Actualizar»), el otro
    /// translúcido. Con el ratón encima cambia de tono sin perder contraste; pulsado encoge 1 px y se oscurece; ocupado (la
    /// ventana de compra abierta), atenuado.</summary>
    private void ProButton(Graphics g, Rectangle b, string text, bool primary, NotchProCardView v, ProCardButton which, float s)
    {
        if (b.IsEmpty) return;
        bool hover = !v.Busy && v.Hover == which, pressed = !v.Busy && v.Pressed == which && hover;
        var r = b;
        if (pressed) r = Rectangle.Inflate(r, -Math.Max(1, (int)Math.Round(s)), -Math.Max(1, (int)Math.Round(s)));
        var fill = primary ? AccentButtonFill(hover, pressed)
            : pressed ? Darken(ProSecondaryFill, ButtonPressDarken)
            : hover ? Color.FromArgb(ProSecondaryFill.A, Math.Min(255, ProSecondaryFill.R + 18), Math.Min(255, ProSecondaryFill.G + 18),
                Math.Min(255, ProSecondaryFill.B + 18))
            : ProSecondaryFill;
        float saved = _alpha;
        if (v.Busy) _alpha = Quantize(_alpha * ProBusyAlpha);
        try
        {
            using (var path = RoundedRect(r, ButtonRadius * s))
            {
                _brush.Color = Fade(fill);
                g.FillPath(_brush, path);
            }
            DrawText(g, text, primary ? _title! : _body!, primary ? OnAccent : _t.Foreground, r, _center);
        }
        finally { _alpha = saved; }
    }

    /// <summary>
    /// El chip «Pro» suelto, a escala <paramref name="scale"/> y con <paramref name="alpha"/>: el MISMO que el de las páginas
    /// con candado. Lo usan las piezas que enseñan funciones del Pro fuera del notch (los pasos Pro del tutorial).
    /// </summary>
    public void PaintProChip(Graphics g, RectangleF r, float scale, float alpha = 1f)
    {
        ArgumentNullException.ThrowIfNull(g);
        EnsureFonts(scale);
        float saved = _alpha;
        _alpha = Quantize(alpha);
        try
        {
            if (_alpha > 0f) ProChip(g, r, scale);
        }
        finally { _alpha = saved; }
    }

    /// <summary>Chip «Pro»: cápsula con el acento tenue, borde de 1 px y el texto en el acento.</summary>
    private void ProChip(Graphics g, RectangleF r, float s)
    {
        var box = Rectangle.Round(r);
        if (box.Width < 2 || box.Height < 2) return;
        using (var path = RoundedRect(box, box.Height / 2f))
        {
            _brush.Color = Fade(Color.FromArgb(ChipFillAlpha, _t.Accent));
            g.FillPath(_brush, path);
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            SetPen(Color.FromArgb(ChipBorderAlpha, _t.Accent), Math.Max(1f, s));
            g.DrawPath(_pen, path);
            g.Restore(state);
        }
        DrawText(g, Strings.NotchProChip, _small ?? _caption!, _t.AccentText, r, _center);
    }
}
