using System.Drawing.Drawing2D;

namespace ClaudeBarWin.Services;

/// <summary>
/// Geometría de dibujo centralizada (GDI+). Versión canónica de las rutinas que antes estaban
/// duplicadas en <c>DashboardDataView</c>, <c>DashboardHeader</c>, <c>DashboardSettingsView</c> y
/// <c>TrayIconRenderer</c>. Lleva las guardas de la copia de <c>DashboardDataView</c> (ancho/alto
/// no positivo no pinta; radio &lt;= 1 cae a rectángulo plano).
/// </summary>
public static class Shapes
{
    /// <summary>Rellena un rectángulo de esquinas redondeadas. Si el área no es positiva, no pinta.</summary>
    public static void FillRounded(Graphics g, Brush b, Rectangle r, int radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius <= 1) { g.FillRectangle(b, r); return; }
        int d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(b, path);
    }

    /// <summary>
    /// Variante subpíxel de <see cref="FillRounded(Graphics, Brush, Rectangle, int)"/> para formas que se
    /// mueven (realce del hover, F3): coordenadas en float para que el deslizamiento no vaya a escalones.
    /// Mismas guardas: área no positiva no pinta; radio ≤ 1 cae a rectángulo plano.
    /// </summary>
    public static void FillRounded(Graphics g, Brush b, RectangleF r, float radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius <= 1) { g.FillRectangle(b, r); return; }
        float d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(b, path);
    }

    /// <summary>
    /// Dibuja el CONTORNO (1px) de un rectángulo de esquinas redondeadas con el pincel dado. Mismas
    /// guardas que <see cref="FillRounded"/> (área no positiva no pinta; radio ≤ 1 cae a rectángulo
    /// plano). Se resta 1px al ancho/alto para que el trazo quede dentro del rect (no se recorte por la
    /// derecha/abajo). Lo usa el borde sutil ("track") de los chips de segmento inactivos.
    /// </summary>
    public static void DrawRounded(Graphics g, Pen p, Rectangle r, int radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        var inner = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
        radius = Math.Min(radius, Math.Min(inner.Width, inner.Height) / 2);
        if (radius <= 1) { g.DrawRectangle(p, inner); return; }
        using var path = RoundedRectPath(inner, radius);
        g.DrawPath(p, path);
    }

    /// <summary>Construye el contorno de un rectángulo de esquinas redondeadas (el llamador lo libera).</summary>
    public static GraphicsPath RoundedRectPath(Rectangle r, int radius)
    {
        int d = Math.Max(2, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
