using System.Drawing;

namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Geometría y curvas PURAS de la apertura y el cierre del panel (lenguaje de movimiento §4 #1–#2): la
/// opacidad va con Rápido y se aplica como alfa de una ventana layered; al cerrar, el contenido se
/// desplaza 0 → <see cref="Motion.CloseShiftPx"/> hacia la bandeja y el panel se oculta cuando el alfa
/// baja de <see cref="Motion.CloseHideAlpha"/>.
/// </summary>
public static class PanelChoreography
{
    /// <summary>
    /// Hacia dónde queda la bandeja: +1 abajo (barra de tareas abajo o a un lado), −1 arriba (barra de
    /// tareas arriba: el área útil empieza por debajo del borde superior de la pantalla).
    /// </summary>
    public static int TrayDirection(Rectangle screenBounds, Rectangle workingArea)
        => workingArea.Top > screenBounds.Top ? -1 : 1;

    /// <summary>
    /// Desplazamiento (px enteros) del contenido durante el cierre: proporcional a lo que ha bajado la
    /// opacidad respecto a su objetivo, así comparte el muelle Rápido y vuelve con él si se reabre.
    /// </summary>
    public static int CloseShiftPx(double opacity, double targetOpacity, double scale, int direction)
    {
        double gone = targetOpacity > 0 ? 1 - Math.Clamp(opacity / targetOpacity, 0, 1) : 1;
        return direction * (int)Math.Round(Motion.CloseShiftPx * scale * gone, MidpointRounding.AwayFromZero);
    }

    /// <summary>¿Puede ocultarse ya la ventana que se está cerrando?</summary>
    public static bool ShouldHide(double opacity) => opacity < Motion.CloseHideAlpha;

    /// <summary>Alfa de <c>SetLayeredWindowAttributes</c> (0..255) para una opacidad 0..1.</summary>
    public static byte AlphaByte(double opacity)
        => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255, MidpointRounding.AwayFromZero);
}
