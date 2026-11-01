using System.Drawing.Drawing2D;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Geometría de la pastilla dentro de la superficie layered. PURA (sin Win32 ni estado): la usan el
/// renderer para pintar y <c>NotchForm</c> para <c>WM_NCHITTEST</c>, así pintar y acertar con el ratón
/// salen de la misma cuenta.
///
/// <para>La pastilla va pegada al borde superior (sin esquinas arriba: «sale» del borde de la pantalla)
/// y centrada en horizontal. Todo en píxeles físicos (ya multiplicados por la escala).</para>
/// </summary>
public static class NotchGeometry
{
    /// <summary>Margen (px lógicos) que la zona de clic añade a la pastilla por los lados y por abajo.</summary>
    public const float HitMargin = 4f;

    /// <summary>Zona de clic mínima (px lógicos): la raya de Minimal (44×4) sería imposible de encontrar.</summary>
    public const float HitMinWidth = 100f, HitMinHeight = 10f;

    /// <summary>Rectángulo de la pastilla de <paramref name="width"/>×<paramref name="height"/> centrado
    /// arriba en la superficie.</summary>
    public static RectangleF PillRect(float width, float height, Size surface) =>
        new((surface.Width - width) / 2f, 0f, Math.Max(0f, width), Math.Max(0f, height));

    /// <summary>
    /// Contorno de la pastilla: arriba recto, esquinas inferiores redondeadas y, si <paramref name="ears"/> &gt; 0,
    /// «orejas» cóncavas arriba que la unen con el borde de la pantalla (sobresalen <paramref name="ears"/> px
    /// por cada lado, solo en la parte alta). El radio se limita a la mitad del alto y del ancho (en los
    /// modos bajos, p. ej. Minimal o Idle, <c>NotchLayout.CornerRadius</c> supera el alto) y la oreja al
    /// lateral recto que queda. Rect vacío → trayecto vacío. Quien lo pide lo libera.
    /// </summary>
    public static GraphicsPath PillPath(RectangleF r, float radius, float ears = 0f)
    {
        var path = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0) return path;

        float rad = ClampRadius(r, radius);
        float e = Math.Clamp(ears, 0f, Math.Max(0f, r.Height - rad));
        if (rad < 0.5f && e < 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        float d = rad * 2f;
        path.StartFigure();
        if (e >= 0.5f)
        {
            // Borde superior de oreja a oreja; cada oreja es un cuarto de círculo cóncavo de radio e.
            path.AddLine(r.Left - e, r.Top, r.Right + e, r.Top);
            path.AddArc(r.Right, r.Top, 2f * e, 2f * e, 270f, -90f);
        }
        else path.AddLine(r.Left, r.Top, r.Right, r.Top);

        path.AddLine(r.Right, r.Top + e, r.Right, r.Bottom - rad);
        if (rad >= 0.5f) path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        path.AddLine(r.Right - rad, r.Bottom, r.Left + rad, r.Bottom);
        if (rad >= 0.5f) path.AddArc(r.Left, r.Bottom - d, d, d, 90f, 90f);
        path.AddLine(r.Left, r.Bottom - rad, r.Left, r.Top + e);

        if (e >= 0.5f) path.AddArc(r.Left - 2f * e, r.Top, 2f * e, 2f * e, 0f, -90f);
        path.CloseFigure();
        return path;
    }

    /// <summary>Radio efectivo de las esquinas inferiores: ≤ mitad del alto y del ancho.</summary>
    public static float ClampRadius(RectangleF r, float radius) =>
        Math.Clamp(radius, 0f, Math.Max(0f, Math.Min(r.Height, r.Width) / 2f));

    /// <summary>
    /// ¿El punto <paramref name="p"/> (coordenadas de la superficie) cae en la pastilla? Contra el MISMO
    /// contorno que se pinta (esquinas inferiores redondeadas), ensanchado <see cref="HitMargin"/>·s, nunca
    /// contra un rectángulo: las esquinas transparentes dejan pasar el clic. Además vale la zona mínima
    /// centrada (<see cref="HitMinWidth"/>·s × <see cref="HitMinHeight"/>·s) para poder encontrar la raya de
    /// Minimal. Siempre dentro de <see cref="HitRect"/>, que sirve de filtro grueso.
    /// </summary>
    public static bool HitTest(Point p, float width, float height, float scale, Size surface)
    {
        var box = HitRect(width, height, scale, surface);
        if (!box.Contains(p)) return false;

        float m = HitMargin * scale;
        var pill = PillRect(width, height, surface);
        float cx = surface.Width / 2f;
        if (Math.Abs(p.X + 0.5f - cx) <= HitMinWidth * scale / 2f && p.Y < HitMinHeight * scale) return true;

        float x = p.X + 0.5f, y = p.Y + 0.5f;   // centro del píxel
        if (x < pill.Left - m || x > pill.Right + m || y > pill.Bottom + m) return false;
        float rad = ClampRadius(pill, NotchLayout.CornerRadius(width, scale));
        if (y <= pill.Bottom - rad) return true;

        // Franja de las esquinas inferiores: distancia al centro del arco ≤ radio + margen.
        float ccx = x < pill.Left + rad ? pill.Left + rad : x > pill.Right - rad ? pill.Right - rad : x;
        float dx = x - ccx, dy = y - (pill.Bottom - rad);
        return dx * dx + dy * dy <= (rad + m) * (rad + m);
    }

    /// <summary>
    /// <see cref="HitTest"/> con la zona de hover retenida de un cambio de página (<paramref name="hold"/>): mientras la forma
    /// cambia de tamaño, cuenta la unión de la forma animada, la vieja y la nueva (el cursor no «sale» porque la pastilla
    /// encoja por debajo de él). Sin retención, igual que <see cref="HitTest"/>.
    /// </summary>
    public static bool HitTest(Point p, float width, float height, float scale, Size surface, HoverHold hold) =>
        HitTest(p, width, height, scale, surface)
        || (hold.Active && (HitTest(p, hold.From.Width, hold.From.Height, scale, surface)
                            || HitTest(p, hold.To.Width, hold.To.Height, scale, surface)));

    /// <summary>
    /// Contorno de la zona de clic de <see cref="HitTest"/> como trayecto: la pastilla ensanchada
    /// <see cref="HitMargin"/>·s (rectángulo redondeado con radio + margen, que es exactamente el contorno
    /// desplazado) más la zona mínima centrada. El renderer lo pinta con alfa 1/255: en una ventana layered
    /// solo los píxeles con alfa &gt; 0 reciben el ratón, así el margen y la raya de Mínimo se pueden
    /// encontrar sin verse (lenguaje de movimiento §6.3). Hidden → vacío. Quien lo pide lo libera.
    /// </summary>
    public static GraphicsPath HitPadPath(float width, float height, float scale, Size surface)
    {
        var path = new GraphicsPath();
        if (width <= 0 || height <= 0 || scale <= 0) return path;

        float m = HitMargin * scale;
        var pill = PillRect(width, height, surface);
        float rad = ClampRadius(pill, NotchLayout.CornerRadius(width, scale));
        // Contorno desplazado: lados rectos hasta el centro de los arcos y arcos de radio rad + m con el
        // MISMO centro que los de la pastilla (no vale PillPath del rect ensanchado: limitaría el radio).
        float cy = pill.Bottom - rad, r = rad + m, d = 2f * r;
        path.StartFigure();
        path.AddLine(pill.Left - m, 0f, pill.Right + m, 0f);
        path.AddLine(pill.Right + m, 0f, pill.Right + m, cy);
        path.AddArc(pill.Right - rad - r, cy - r, d, d, 0f, 90f);
        path.AddLine(pill.Right - rad, cy + r, pill.Left + rad, cy + r);
        path.AddArc(pill.Left + rad - r, cy - r, d, d, 90f, 90f);
        path.CloseFigure();

        float mw = HitMinWidth * scale, mh = HitMinHeight * scale;
        path.AddRectangle(new RectangleF((surface.Width - mw) / 2f, 0f, mw, mh));
        path.FillMode = FillMode.Winding;   // la unión, no el XOR de las dos figuras
        return path;
    }

    /// <summary>Botones del Permiso (px lógicos): tamaño, separación y márgenes respecto a la pastilla.</summary>
    public const float PermissionButtonWidth = 92f, PermissionButtonHeight = 26f, PermissionButtonGap = 8f,
        PermissionButtonPadX = 14f, PermissionButtonPadBottom = 14f;

    /// <summary>
    /// Botones Denegar (izquierda) y Permitir (derecha), abajo a la derecha de la caja del contenido
    /// <paramref name="pill"/>. La MISMA cuenta para pintar y para acertar el clic. Redondeados a píxel entero
    /// (hacia dentro: nunca se salen de la pastilla). Pastilla demasiado pequeña (abriéndose) → vacíos.
    /// </summary>
    public static (Rectangle Deny, Rectangle Allow) PermissionButtons(RectangleF pill, float scale)
    {
        float w = PermissionButtonWidth * scale, h = PermissionButtonHeight * scale, gap = PermissionButtonGap * scale;
        float right = pill.Right - PermissionButtonPadX * scale, bottom = pill.Bottom - PermissionButtonPadBottom * scale;
        float left = right - 2f * w - gap, top = bottom - h;
        if (scale <= 0 || left < pill.Left || top < pill.Top) return (Rectangle.Empty, Rectangle.Empty);

        static Rectangle Inside(float l, float t, float r, float b) => Rectangle.FromLTRB(
            (int)Math.Ceiling(l - 1e-3f), (int)Math.Ceiling(t - 1e-3f), (int)Math.Floor(r + 1e-3f), (int)Math.Floor(b + 1e-3f));

        return (Inside(left, top, left + w, bottom), Inside(right - w, top, right, bottom));
    }

    /// <summary>El botón del aviso del fin de fase del pomodoro («Empezar descanso» / «Empezar foco»): uno ancho donde el Permiso
    /// lleva Denegar y Permitir (su texto, más largo, cabe en todos los idiomas). Vacío mientras la forma se abre.</summary>
    public static Rectangle FocusAlertButton(RectangleF pill, float scale)
    {
        var (deny, allow) = PermissionButtons(pill, scale);
        return deny.IsEmpty || allow.IsEmpty ? Rectangle.Empty : Rectangle.FromLTRB(deny.Left, deny.Top, allow.Right, allow.Bottom);
    }

    /// <summary>Puntos de página del expandido (px lógicos, lenguaje de movimiento §3.9 #5): puntos de 6 px con un paso
    /// fijo y una píldora de 16 × 3 que se desliza entre ellos.</summary>
    public const float PageDotDiameter = 6f, PageDotPitch = 22f, PageIndicatorWidth = 16f, PageIndicatorHeight = 3f;

    /// <summary>
    /// Centro del punto <paramref name="index"/> de <paramref name="count"/>: el grupo va centrado en la fila de puntos
    /// (<see cref="NotchLayout.PageDotsRow"/>), justo encima del margen inferior de la caja <paramref name="pill"/>.
    /// La MISMA cuenta para pintar y para acertar el clic (<see cref="PageDotAt"/>). Sin reservas en el montón.
    /// </summary>
    public static PointF PageDotCenter(RectangleF pill, float scale, int count, float index)
    {
        float pitch = PageDotPitch * scale;
        float x = pill.Left + pill.Width / 2f + (index - (count - 1) / 2f) * pitch;
        float y = pill.Bottom - (NotchLayout.ExpandedPadBottom + NotchLayout.PageDotsRow / 2f) * scale;
        return new PointF(x, y);
    }

    /// <summary>
    /// Punto de página bajo <paramref name="p"/>, o −1. Cada punto acierta en su celda (paso × fila de puntos, más el
    /// margen inferior hasta el borde de la pastilla): un punto de 6 px solo sería imposible de acertar. Con menos de dos
    /// páginas no hay puntos.
    /// </summary>
    public static int PageDotAt(Point p, RectangleF pill, float scale, int count)
    {
        if (count < 2 || scale <= 0 || pill.Width <= 0 || pill.Height <= 0) return -1;
        float top = pill.Bottom - (NotchLayout.ExpandedPadBottom + NotchLayout.PageDotsRow) * scale;
        if (top < pill.Top) return -1;
        float x = p.X + 0.5f, y = p.Y + 0.5f;
        if (y < top || y > pill.Bottom) return -1;
        float pitch = PageDotPitch * scale;
        float left = PageDotCenter(pill, scale, count, 0).X - pitch / 2f;
        int i = (int)MathF.Floor((x - left) / pitch);
        return i >= 0 && i < count ? i : -1;
    }

    /// <summary>Flechas ‹ › de página (px lógicos): distancia de su centro al centro del punto de su extremo, zona de clic
    /// cuadrada (≥ 24 px a 100 %, escala con el DPI), medio ancho y medio alto del chevron y su trazo.</summary>
    public const float PageArrowDistance = 28f, PageArrowHit = 24f, PageArrowHalfWidth = 2.5f, PageArrowHalfHeight = 4.5f,
        PageArrowStroke = 1.5f;

    /// <summary>Centro de la flecha <paramref name="side"/> en la fila de los puntos: a <see cref="PageArrowDistance"/>·s del
    /// primer punto (anterior) o del último (siguiente). La MISMA cuenta para pintar y para acertar el clic.</summary>
    public static PointF PageArrowCenter(RectangleF pill, float scale, int count, PageArrow side)
    {
        var edge = PageDotCenter(pill, scale, count, side == PageArrow.Previous ? 0 : count - 1);
        return new PointF(edge.X + (int)side * PageArrowDistance * scale, edge.Y);
    }

    /// <summary>Zona de clic de la flecha <paramref name="side"/>: <see cref="PageArrowHit"/>·s × <see cref="PageArrowHit"/>·s
    /// centrada en el chevron (no pisa las celdas de los puntos).</summary>
    public static RectangleF PageArrowHitRect(RectangleF pill, float scale, int count, PageArrow side)
    {
        var c = PageArrowCenter(pill, scale, count, side);
        float h = PageArrowHit * scale;
        return new RectangleF(c.X - h / 2f, c.Y - h / 2f, h, h);
    }

    /// <summary>
    /// Flecha bajo <paramref name="p"/> o <see cref="PageArrow.None"/>. Con menos de dos páginas, pastilla vacía o aún tan
    /// baja que la fila de puntos no cabe, no hay flechas. No mira si la flecha lleva a algún sitio
    /// (<see cref="PageArrows.Target"/>): eso lo decide quien la usa.
    /// </summary>
    public static PageArrow PageArrowAt(Point p, RectangleF pill, float scale, int count)
    {
        if (count < 2 || scale <= 0 || pill.Width <= 0 || pill.Height <= 0) return PageArrow.None;
        if (pill.Bottom - (NotchLayout.ExpandedPadBottom + NotchLayout.PageDotsRow) * scale < pill.Top) return PageArrow.None;
        var pt = new PointF(p.X + 0.5f, p.Y + 0.5f);
        if (PageArrowHitRect(pill, scale, count, PageArrow.Previous).Contains(pt)) return PageArrow.Previous;
        if (PageArrowHitRect(pill, scale, count, PageArrow.Next).Contains(pt)) return PageArrow.Next;
        return PageArrow.None;
    }

    /// <summary>
    /// Zona que captura el ratón: la pastilla + <see cref="HitMargin"/>·s a los lados y abajo (arriba no:
    /// es el borde de la pantalla), con un mínimo de <see cref="HitMinWidth"/>·s × <see cref="HitMinHeight"/>·s,
    /// centrada y recortada a la superficie. Se redondea hacia fuera para no perder ningún píxel pintado.
    /// Pastilla 0×0 (Hidden) → vacía: nada debe capturar clics si no se ve nada.
    /// </summary>
    public static Rectangle HitRect(float width, float height, float scale, Size surface)
    {
        if (width <= 0 || height <= 0 || scale <= 0) return Rectangle.Empty;

        float w = Math.Max(width + 2f * HitMargin * scale, HitMinWidth * scale);
        float h = Math.Max(height + HitMargin * scale, HitMinHeight * scale);
        float left = (surface.Width - w) / 2f;

        // Redondeo hacia fuera con tolerancia: 271,0000001 por coma flotante no debe comerse un píxel.
        int l = (int)Math.Floor(left + 1e-3f);
        int r = (int)Math.Ceiling(left + w - 1e-3f);
        int b = (int)Math.Ceiling(h - 1e-3f);
        return Rectangle.FromLTRB(Math.Max(0, l), 0, Math.Min(surface.Width, r), Math.Min(surface.Height, b));
    }
}

/// <summary>
/// Zona de hover retenida durante la animación de tamaño de un cambio de página del usuario: la forma de la que se sale
/// (<see cref="From"/>) y la de la página nueva (<see cref="To"/>), en px físicos. <c>default</c> = sin retención.
/// </summary>
public readonly record struct HoverHold(SizeF From, SizeF To)
{
    public bool Active => From.Width > 0 && From.Height > 0;

    /// <summary>Nueva retención al cambiar de página con la forma en <paramref name="current"/>: si ya había una (cambios
    /// encadenados), la forma de partida es la mayor de las dos, para no perder la zona de la primera.</summary>
    public static HoverHold Start(HoverHold previous, SizeF current) => new(
        previous.Active ? new SizeF(Math.Max(previous.From.Width, current.Width), Math.Max(previous.From.Height, current.Height))
                        : current,
        current);
}
