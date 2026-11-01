using System.Diagnostics;
using System.Runtime.InteropServices;
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Trae al frente la terminal de una sesión (clic en el texto del Permiso del notch, F2). Usa los ancestros que
/// el servidor del hook resolvió (<see cref="SessionState.Ancestors"/>: claude.exe, la shell, la terminal…): la
/// primera de ellos, del más cercano al más lejano, que siga viva (mismo PID y hora de inicio) y tenga ventana
/// principal. Best effort: con una consola clásica (conhost) la ventana no es de ningún ancestro y no se encuentra.
/// Windows deja traer otra ventana al frente porque el clic acaba de llegar a este proceso.
/// </summary>
public static class TerminalFocus
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

    /// <summary>Procesos candidatos a tener la ventana de la terminal, del más cercano al más lejano (puro).</summary>
    internal static IReadOnlyList<ProcessInfo> Candidates(SessionState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var list = new List<ProcessInfo>(s.Ancestors);
        if (s.ClaudeProcess is { } c && !list.Any(a => a.Pid == c.Pid)) list.Insert(0, c);
        return list;
    }

    public static bool TryFocus(SessionState s)
    {
        try
        {
            foreach (var a in Candidates(s))
            {
                if (!ProcessTree.IsAlive(a)) continue;
                using var p = Process.GetProcessById(a.Pid);
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
                return SetForegroundWindow(h);
            }
        }
        catch { /* el proceso se fue entre medias: nada que enfocar */ }
        return false;
    }
}
