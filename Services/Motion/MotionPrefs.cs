using System.Runtime.InteropServices;

namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Lee la preferencia del SISTEMA "reducir animaciones" de Windows
/// (<c>SPI_GETCLIENTAREAANIMATION</c>). PURO desde el punto de vista de los tests: no lanza nunca —
/// si la llamada P/Invoke falla, hace fallback a <c>false</c> (animaciones permitidas).
///
/// <para>Disponible para una FUTURA opción "seguir Windows", pero el default del toggle
/// <see cref="ClaudeBarWin.Config.AppConfig.ReduceMotion"/> NO depende del SO (es <c>false</c> por
/// decisión de Yovan): el gate único de reduce-motion se alimenta de la config, no de aquí.</para>
/// </summary>
public static class MotionPrefs
{
    // SystemParametersInfo(SPI_GETCLIENTAREAANIMATION) -> BOOL: true = animaciones permitidas.
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);

    /// <summary>
    /// ¿Pide Windows reducir el movimiento? <c>true</c> si la animación de área cliente está
    /// desactivada en el SO. Ante cualquier fallo (no-Windows, P/Invoke fallida) devuelve <c>false</c>.
    /// </summary>
    public static bool OsReducedMotion()
    {
        try
        {
            bool animationsEnabled = true; // por defecto el SO permite animaciones
            if (SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref animationsEnabled, 0))
                return !animationsEnabled; // reduce-motion = animaciones del SO desactivadas
        }
        catch
        {
            // fallback abajo
        }
        return false;
    }
}
