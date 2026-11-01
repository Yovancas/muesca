using System.Drawing;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Foto de la ventana en primer plano que necesita <see cref="FullscreenPolicy"/>. La rellena
/// <see cref="FullscreenWatcher"/> con Win32; los tests la construyen a mano.
/// </summary>
/// <param name="Bounds">Rect real en píxeles físicos (DWM extended frame, sin la sombra invisible).</param>
/// <param name="HasCaption">La ventana tiene barra de título (<c>WS_CAPTION</c>).</param>
/// <param name="IsShellWindow">Es el escritorio o la barra de tareas (Progman/WorkerW/Shell_TrayWnd…).</param>
/// <param name="IsCloaked">DWM la tiene oculta (otro escritorio virtual, app UWP suspendida…).</param>
/// <param name="IsOwnProcess">Es una ventana de ClaudeBar.</param>
/// <param name="Monitor">HMONITOR donde está la mayor parte de la ventana.</param>
public readonly record struct ForegroundInfo(
    Rectangle Bounds, bool HasCaption, bool IsShellWindow, bool IsCloaked, bool IsOwnProcess, IntPtr Monitor);

/// <summary>
/// Decide si el primer plano es "pantalla completa" a efectos del notch (que se esconde y para su
/// bucle). PURA: todo llega por parámetro.
///
/// <list type="bullet">
/// <item>Nunca cuentan el escritorio/la barra, las ventanas cloaked ni las propias.</item>
/// <item>Solo importa el monitor del notch: un juego en el otro monitor no lo esconde. Con un notch por monitor, cada
/// uno se decide con el suyo (<see cref="FullscreenMonitor"/>): solo se esconde el del juego.</item>
/// <item>QUNS 2 (BUSY), 3 (D3D exclusivo) o 4 (presentación) en ese monitor ⇒ sí, aunque el rect sea
/// menor (D3D a otra resolución) o tenga caption (PowerPoint en presentación).</item>
/// <item>Si no, rect que cubre el monitor entero y SIN caption (borderless: Valorant/Fortnite). Una
/// maximizada normal tiene caption y ocupa solo el área de trabajo ⇒ no.</item>
/// </list>
/// </summary>
public static class FullscreenPolicy
{
    // Valores de QUERY_USER_NOTIFICATION_STATE (shellapi.h).
    public const int QunsBusy = 2;
    public const int QunsRunningD3DFullScreen = 3;
    public const int QunsPresentationMode = 4;

    public static bool IsFullscreen(ForegroundInfo fg, Rectangle monitorBounds, IntPtr notchMonitor, int qunsState)
    {
        if (fg.IsShellWindow || fg.IsCloaked || fg.IsOwnProcess) return false;
        if (fg.Monitor != notchMonitor) return false;

        if (qunsState is QunsBusy or QunsRunningD3DFullScreen or QunsPresentationMode) return true;

        return !fg.HasCaption && !monitorBounds.IsEmpty && fg.Bounds.Contains(monitorBounds);
    }

    /// <summary>
    /// Monitor en pantalla completa (con un notch por monitor, solo se esconde el de ese monitor): el de la ventana en primer
    /// plano si lo es, o <see cref="IntPtr.Zero"/> si no hay ninguno. Solo puede serlo el monitor del primer plano: los demás
    /// siguen viéndose aunque juegues en uno. <paramref name="foregroundMonitorBounds"/> = rect del monitor de
    /// <see cref="ForegroundInfo.Monitor"/>. Cada ventana del notch lo compara con el suyo (<see cref="IsFullscreen"/>).
    /// </summary>
    public static IntPtr FullscreenMonitor(ForegroundInfo fg, Rectangle foregroundMonitorBounds, int qunsState) =>
        fg.Monitor != IntPtr.Zero && IsFullscreen(fg, foregroundMonitorBounds, fg.Monitor, qunsState) ? fg.Monitor : IntPtr.Zero;
}
