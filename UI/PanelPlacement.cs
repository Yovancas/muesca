namespace ClaudeBarWin.UI;

/// <summary>
/// Geometría PURA de la colocación del panel (F3 Task 6). Devuelve los bounds completos para un alto
/// dado, de modo que el relayout aplique posición y tamaño en UNA llamada (<c>SetBounds</c>) y el alto
/// animado quede anclado a su borde: abajo en las posiciones inferiores (por defecto), arriba en las
/// superiores y en "Custom" (esquina superior izquierda guardada), centrado en "Center".
/// </summary>
internal static class PanelPlacement
{
    /// <param name="custom">Esquina ya acotada a su pantalla para "Custom"; null si no aplica.</param>
    public static Rectangle Bounds(string position, Rectangle workArea, Size size, int margin, Point? custom)
    {
        var wa = workArea;
        int w = size.Width, h = size.Height, m = margin;
        Point p = position switch
        {
            "BottomLeft" => new Point(wa.Left + m, wa.Bottom - h - m),
            "TopRight" => new Point(wa.Right - w - m, wa.Top + m),
            "TopLeft" => new Point(wa.Left + m, wa.Top + m),
            "Center" => new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2),
            "Custom" when custom is { } c => c,
            _ => new Point(wa.Right - w - m, wa.Bottom - h - m),
        };
        return new Rectangle(p, size);
    }
}
