namespace ClaudeBarWin.Tests;

/// <summary>
/// Reconocer en un píxel pintado el acento de Muesca (el violeta #7B4DFF y su variante de texto #B49BFF), también tenue o
/// fundido sobre el negro del notch. Azul dominante y rojo por encima del verde: así no se confunde con el azul cielo del
/// descanso largo (R &lt; G), el verde del corto, el ámbar o el rojo de los avisos (B ≈ G o menos), ni con grises y blancos.
/// </summary>
internal static class BrandPixels
{
    public static bool IsAccent(Color c) => c.A > 0 && c.B > c.G + 40 && c.R > c.G + 8;
}
