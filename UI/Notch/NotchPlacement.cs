using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Dónde y a qué escala va el notch. PURO (sin Win32): <c>NotchForm</c> le pasa el DPI y los límites del
/// monitor elegido. El notch no usa <c>Dpi.Apply</c>: su escala es <c>DPI/96 × factor de tamaño</c>.
/// </summary>
public static class NotchPlacement
{
    /// <summary>Superficie (px lógicos) donde caben el modo más grande, sus orejas, el squash y la sombra; el resto es alfa 0 y
    /// deja pasar el ratón. <see cref="SurfaceHeight"/>, la alta, solo con la página Uso (<see cref="NotchLayout.UsagePageMaxHeight"/>
    /// + la sombra) y durante su transición; todo lo demás (<see cref="NotchLayout.ExpandedMaxHeight"/> + la sombra) cabe en
    /// <see cref="CompactSurfaceHeight"/>.</summary>
    public const float SurfaceWidth = 700f, SurfaceHeight = 272f, CompactSurfaceHeight = 200f;

    /// <summary>
    /// Escala del notch: <c>DPI/96 × factor</c>; el factor es <paramref name="sizePercent"/>/100 o, con 0
    /// (Auto), <see cref="NotchLayout.AutoScale"/> del ancho LÓGICO del monitor. Porcentajes fuera de
    /// 50..200 se tratan como Auto.
    /// </summary>
    public static float Scale(int dpi, int sizePercent, int monitorPhysicalWidth)
    {
        float dpiFactor = Dpi.FactorFor(dpi);
        float size = sizePercent is >= 50 and <= 200
            ? sizePercent / 100f
            : NotchLayout.AutoScale((int)Math.Round(monitorPhysicalWidth / dpiFactor));
        return dpiFactor * size;
    }

    /// <summary>Superficie <c>ceil(700·s) × ceil(272·s)</c>, con tolerancia para que 700 × 1,15f no suba a 806.</summary>
    public static Size Surface(float scale) => SurfaceFor(scale, tall: true);

    /// <summary>La superficie alta (<see cref="SurfaceHeight"/>) o la baja (<see cref="CompactSurfaceHeight"/>); el ancho no
    /// cambia, así que la esquina superior izquierda tampoco.</summary>
    public static Size SurfaceFor(float scale, bool tall) =>
        new(Ceil(SurfaceWidth * scale), Ceil((tall ? SurfaceHeight : CompactSurfaceHeight) * scale));

    /// <summary>
    /// ¿Hace falta la superficie alta? Con la página Uso activa (<paramref name="usagePage"/>: objetivo del carrusel o en su
    /// cruce) y mientras la forma pintada (<paramref name="drawnHeight"/>, px físicos, la de los muelles) o la que retiene la
    /// zona de hover (<paramref name="hold"/>: vieja ∪ nueva al cambiar de página) sea más alta que el tope de las demás
    /// páginas: así la forma que encoge al salir de Uso nunca se recorta. Asentada en otra cosa, la baja.
    /// </summary>
    public static bool NeedsTallSurface(bool usagePage, float drawnHeight, HoverHold hold, float scale)
    {
        if (usagePage) return true;
        float held = hold.Active ? Math.Max(hold.From.Height, hold.To.Height) : 0f;
        return Math.Max(drawnHeight, held) > NotchLayout.ExpandedMaxHeight * scale + 0.5f;
    }

    /// <summary>Esquina superior izquierda de la superficie: centrada y pegada al borde superior del monitor.</summary>
    public static Point TopLeft(Rectangle monitorBounds, Size surface) =>
        new(monitorBounds.Left + (monitorBounds.Width - surface.Width) / 2, monitorBounds.Top);

    private static int Ceil(float v) => (int)Math.Ceiling(v - 1e-3f);
}
