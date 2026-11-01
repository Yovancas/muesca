namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Pinta un <see cref="NotchFrame"/> en una ventana layered. Detrás de una interfaz para poder cambiar
/// GDI+ por Visual Layer/DirectComposition sin tocar la lógica ni la ventana (spec §4.1).
/// </summary>
public interface INotchRenderer : IDisposable
{
    /// <summary>
    /// Pinta <paramref name="frame"/> en una superficie de <paramref name="surface"/> píxeles colocada en
    /// <paramref name="topLeft"/> (coordenadas de pantalla): la pastilla a <paramref name="hwnd"/> y la sombra
    /// y el halo a <paramref name="shadowHwnd"/> (ventana que no recibe clics, misma superficie y posición;
    /// <c>IntPtr.Zero</c> = sin sombra). Llamar siempre desde el hilo de UI. No lanza: si Windows rechaza la
    /// actualización, el frame se pierde.
    /// </summary>
    void Render(IntPtr hwnd, IntPtr shadowHwnd, Point topLeft, Size surface, NotchFrame frame);
}
