using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Lo que el notch puede usar con el estado del Pro: de lo que pide la config a lo que se le da a <see cref="NotchHost"/>.
/// PURO y todo con <see cref="ProGate"/> (nada de <c>if (pro)</c> sueltos en la ventana). Sin la función:
/// <list type="bullet">
/// <item><b>Música:</b> música, letras y letra compacta apagadas (ni sesión de medios ni lrclib.net); la página Música, con
/// candado si estaba encendida.</item>
/// <item><b>Tareas:</b> apagadas (ni avisos ni temporizadores; <c>tasks.json</c> no se toca); la página, con candado.</item>
/// <item><b>Bandeja:</b> la página con candado, sin cargarla ni aceptar arrastres; «vaciar al arrancar» no se aplica (los datos
/// nunca se borran por no tener el Pro).</item>
/// <item><b>Varios monitores:</b> un solo notch en el monitor principal (Fijo y ""), sea cual sea el modo guardado.</item>
/// <item><b>Gráficas de uso:</b> la página Uso sigue con sus números; su gráfica se queda quieta con el chip «Pro»
/// (<see cref="NotchProLocks.UsageCharts"/>).</item>
/// <item><b>Foco:</b> el pomodoro básico sigue entero; las duraciones vuelven a las de serie (25 / 5 / 15 cada 4) sin tocar lo
/// guardado, y enlazar con tareas y las estadísticas llevan el chip «Pro» (<see cref="NotchProLocks.FocusPro"/>).</item>
/// </list>
/// Los permisos no pasan por aquí: sin el Pro el pipe ni se abre (<c>TrayAppContext.PermissionPipeWanted</c>). La config no
/// se toca: al comprar, todo vuelve en caliente tal como estaba elegido.
/// </summary>
public static class NotchProGating
{
    public static (NotchOptions Options, NotchMonitorMode Mode) Apply(NotchOptions requested, NotchMonitorMode mode, ProState state)
    {
        ArgumentNullException.ThrowIfNull(requested);
        var o = requested;
        var locks = NotchProLocks.None;
        if (!ProGate.IsUnlocked(ProFeature.Music, state))
        {
            if (o.Music) locks |= NotchProLocks.Music;
            o = o with { Music = false, Lyrics = false, CompactLyrics = false };
        }
        if (!ProGate.IsUnlocked(ProFeature.Tasks, state))
        {
            if (o.Tasks) locks |= NotchProLocks.Tasks;
            o = o with { Tasks = false };
        }
        if (!ProGate.IsUnlocked(ProFeature.Shelf, state))
        {
            if (NotchPages.Normalize(o.Pages).Contains(NotchPage.Shelf)) locks |= NotchProLocks.Shelf;
            o = o with { ShelfClearOnStart = false };
        }
        if (!ProGate.IsUnlocked(ProFeature.NotchUsage, state)) locks |= NotchProLocks.UsageCharts;
        if (!ProGate.IsUnlocked(ProFeature.NotchFocus, state))
        {
            locks |= NotchProLocks.FocusPro;
            o = o with { Focus = o.FocusOrDefault with { Durations = FocusDurations.Default } };
        }
        (mode, var device) = Placement(mode, o.MonitorDeviceName, state);
        return (o with { Locked = locks, MonitorDeviceName = device }, mode);
    }

    /// <summary>Modo y monitor que de verdad tendrá el notch: sin <see cref="ProFeature.MultiMonitor"/>, uno solo en el
    /// principal (Fijo y ""), sea cual sea lo guardado. Lo usan también el asistente (dónde aterriza) y la bandeja.</summary>
    public static (NotchMonitorMode Mode, string Monitor) Placement(NotchMonitorMode mode, string? monitor, ProState state) =>
        ProGate.IsUnlocked(ProFeature.MultiMonitor, state) ? (mode, monitor ?? "") : (NotchMonitorMode.Fixed, "");
}
