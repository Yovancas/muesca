using System.Runtime.InteropServices;
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Decide si una sesión está "en primer plano": el dueño de la ventana del primer plano es uno de
/// los ancestros del proceso de la sesión (su claude.exe, la shell o la terminal que la aloja).
///
/// v0.3.10: antes nunca funcionaba. Se comparaba con el PID del powershell efímero del hook (muerto
/// milisegundos después), el recorrido de padres devolvía siempre null y además subía en la dirección
/// equivocada (desde la ventana hacia arriba, buscando la sesión). Ahora los ancestros de la sesión se
/// resuelven al llegar el evento (<see cref="ProcessTree"/>) y aquí solo se busca el PID de la ventana
/// en esa lista. Limitación conocida: una terminal con varias pestañas cuenta como "en foco" aunque la
/// pestaña visible sea otra.
/// </summary>
public sealed class ForegroundDetector
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    /// <summary>PID del dueño de la ventana en primer plano (0 si no hay).</summary>
    internal static int ForegroundPid()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out uint fgPid);
            return (int)fgPid;
        }
        catch { return 0; }
    }

    public bool IsSessionForeground(SessionState s) => IsSessionForeground(s, ForegroundPid());

    /// <summary>Lógica pura (testeable): ¿el PID de la ventana en foco es la sesión o uno de sus ancestros?</summary>
    internal static bool IsSessionForeground(SessionState s, int fgPid)
    {
        if (fgPid <= 0) return false;
        if (s.Pid == fgPid) return true;
        foreach (var a in s.Ancestors)
            if (a.Pid == fgPid) return true;
        return false;
    }

    /// <summary>Compatibilidad: sin ancestros solo puede comparar el PID directo.</summary>
    public bool IsSessionForeground(int? sessionPid)
    {
        if (sessionPid is not { } target) return false;
        int fg = ForegroundPid();
        return fg > 0 && fg == target;
    }
}
