using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeBarWin.Services;

/// <summary>Un ancestro del proceso del hook: pid, ejecutable y hora de inicio (anti reutilización de PID).</summary>
public readonly record struct ProcessInfo(int Pid, string ExeName, DateTime StartUtc);

/// <summary>
/// Cadena de ancestros de un proceso vía una sola instantánea Toolhelp32 (barata: ~1-3 ms, sin WMI).
/// v0.3.10: sirve para dos cosas que antes no funcionaban — (1) "silenciar si la terminal tiene el
/// foco" (antes se comparaba con el PID del powershell efímero del hook, que ya había muerto, y el
/// recorrido de padres devolvía siempre null) y (2) detectar sesiones cuyo claude.exe murió sin
/// mandar SessionEnd, para quitarlas de la lista.
/// </summary>
public static class ProcessTree
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>pid → (ppid, exe) de todo el sistema en una instantánea.</summary>
    internal static Dictionary<int, (int Parent, string Exe)> Snapshot()
    {
        var map = new Dictionary<int, (int, string)>();
        var h = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return map;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(h, ref e)) return map;
            do { map[(int)e.th32ProcessID] = ((int)e.th32ParentProcessID, e.szExeFile ?? ""); }
            while (Process32NextW(h, ref e));
        }
        finally { CloseHandle(h); }
        return map;
    }

    /// <summary>
    /// Ancestros de <paramref name="pid"/> (padre, abuelo, …), sin incluirlo. Se corta si el "padre"
    /// arrancó DESPUÉS que el hijo (PID reutilizado), si hay ciclo o al llegar a <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<ProcessInfo> Ancestors(int pid, int max = 12)
    {
        var result = new List<ProcessInfo>();
        try
        {
            var map = Snapshot();
            var childStart = StartUtc(pid) ?? DateTime.MaxValue;
            var seen = new HashSet<int> { pid };
            int cur = pid;
            while (result.Count < max && map.TryGetValue(cur, out var node))
            {
                int parent = node.Parent;
                if (parent <= 4 || !seen.Add(parent) || !map.TryGetValue(parent, out var pnode)) break;
                var pStart = StartUtc(parent);
                if (pStart is null || pStart > childStart) break; // PID reutilizado: no es el padre real
                result.Add(new ProcessInfo(parent, pnode.Exe, pStart.Value));
                childStart = pStart.Value;
                cur = parent;
            }
        }
        catch { /* best-effort */ }
        return result;
    }

    /// <summary>¿Sigue vivo ese proceso concreto (mismo pid Y misma hora de inicio)?</summary>
    public static bool IsAlive(ProcessInfo p)
    {
        var s = StartUtc(p.Pid);
        return s is { } st && Math.Abs((st - p.StartUtc).TotalSeconds) < 1;
    }

    private static DateTime? StartUtc(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.StartTime.ToUniversalTime();
        }
        catch { return null; }
    }

    /// <summary>El primer ancestro que es el proceso de Claude Code (claude.exe; node/bun en instalaciones npm).</summary>
    public static ProcessInfo? FindClaude(IReadOnlyList<ProcessInfo> ancestors)
    {
        foreach (var a in ancestors)
            if (a.ExeName.StartsWith("claude", StringComparison.OrdinalIgnoreCase)) return a;
        foreach (var a in ancestors)
            if (a.ExeName.Equals("node.exe", StringComparison.OrdinalIgnoreCase)
                || a.ExeName.Equals("bun.exe", StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }
}
