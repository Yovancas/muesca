using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Vigila el primer plano por EVENTOS (sin sondeo) y avisa cuando entra o sale una pantalla completa
/// en el monitor del notch. La decisión es de <see cref="FullscreenPolicy"/>; aquí solo hay Win32.
///
/// <list type="bullet">
/// <item>Los hooks son de <see cref="WinEventHooks"/>, UNOS para todas las ventanas del notch (con un notch por monitor
/// no se instala un juego por ventana); cada vigilante evalúa con SU monitor: solo se esconde el del juego.</item>
/// <item>FOREGROUND ⇒ reevalúa al momento y lanza <see cref="ForegroundChanged"/> (reafirmar topmost).</item>
/// <item>MOVESIZESTART/END ⇒ <see cref="MoveSizeChanged"/> (atenuar con Snap Layouts); al soltar, reevalúa.</item>
/// <item>LOCATIONCHANGE de la ventana en primer plano, con debounce de 150 ms (un juego que pasa a borderless cambia de
/// tamaño sin cambiar el foco) ⇒ reevalúa.</item>
/// </list>
/// Nada aquí lanza: ante cualquier fallo de Win32 la ventana se trata como "no pantalla completa" (el notch se queda
/// visible, que es el fallo menos grave).
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    public const int DebounceMs = WinEventHooks.DebounceMs;

    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;          // WS_BORDER | WS_DLGFRAME
    private const uint MONITOR_DEFAULTTONULL = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;

    private static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private readonly Func<IntPtr> _notchMonitor;
    private bool _started;
    private bool _disposed;

    /// <summary>Cambia el estado de pantalla completa (true = el notch debe esconderse).</summary>
    public event Action<bool>? Changed;
    /// <summary>Empieza (true) o termina (false) un arrastre/redimensionado de ventana.</summary>
    public event Action<bool>? MoveSizeChanged;
    /// <summary>Cambió la ventana en primer plano.</summary>
    public event Action? ForegroundChanged;

    public bool IsFullscreen { get; private set; }

    /// <param name="notchMonitor">HMONITOR actual del notch (se consulta en cada evaluación: puede moverse).</param>
    public FullscreenWatcher(Func<IntPtr> notchMonitor)
    {
        _notchMonitor = notchMonitor ?? throw new ArgumentNullException(nameof(notchMonitor));
    }

    /// <summary>Se engancha a los hooks compartidos y hace una primera evaluación. Llamar desde el hilo de UI.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;
        var hooks = WinEventHooks.Shared;
        hooks.Foreground += OnForeground;
        hooks.MoveSize += OnMoveSize;
        hooks.LocationSettled += Reevaluate;
        hooks.Acquire();
        Reevaluate();
    }

    /// <summary>Recalcula ya (cambio de monitor del notch, ajustes, TaskbarCreated…) y avisa si cambia.</summary>
    public void Reevaluate()
    {
        if (_disposed) return;
        bool now = Evaluate();
        if (now == IsFullscreen) return;
        IsFullscreen = now;
        Changed?.Invoke(now);
    }

    private void OnForeground()
    {
        if (_disposed) return;
        ForegroundChanged?.Invoke();
        Reevaluate();
    }

    private void OnMoveSize(bool moving)
    {
        if (_disposed) return;
        MoveSizeChanged?.Invoke(moving);
        if (!moving) Reevaluate();
    }

    private bool Evaluate()
    {
        try
        {
            IntPtr fgHwnd = GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero) return false;

            IntPtr notchMon = _notchMonitor();
            if (notchMon == IntPtr.Zero) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(notchMon, ref mi)) return false;

            int quns = SHQueryUserNotificationState(out int state) == 0 ? state : 0;
            return FullscreenPolicy.IsFullscreen(Describe(fgHwnd), mi.rcMonitor.ToRectangle(), notchMon, quns);
        }
        catch { return false; }
    }

    private static ForegroundInfo Describe(IntPtr hwnd)
    {
        bool caption = (GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & WS_CAPTION) == WS_CAPTION;

        bool shell = ClassOf(hwnd) is { } cls && ShellClasses.Contains(cls);

        bool cloaked = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloak, sizeof(int)) == 0 && cloak != 0;

        GetWindowThreadProcessId(hwnd, out uint pid);
        bool own = pid == (uint)Environment.ProcessId;

        // El extended frame excluye los bordes invisibles de redimensionado de Win10/11; si DWM falla,
        // GetWindowRect (algo más grande) es un respaldo aceptable.
        Rectangle bounds = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0
            ? r.ToRectangle()
            : GetWindowRect(hwnd, out RECT wr) ? wr.ToRectangle() : Rectangle.Empty;

        IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return new ForegroundInfo(bounds, caption, shell, cloaked, own, mon);
    }

    /// <summary>
    /// La ventana en primer plano de ahora, para seguirla (<see cref="ForegroundFilter"/> decide si cuenta). Su monitor es
    /// el que tiene la mayor parte de la ventana, por nombre GDI (el de <c>Screen.DeviceName</c>); null si no está en
    /// ninguno. Ante cualquier fallo, una foto vacía (no cuenta: el notch se queda donde estaba).
    /// </summary>
    public static ForegroundWindow DescribeForeground()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return default;
            bool cloaked = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloak, sizeof(int)) == 0 && cloak != 0;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return new ForegroundWindow(hwnd, MonitorDevice(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONULL)), ClassOf(hwnd),
                IsWindowVisible(hwnd), IsIconic(hwnd), cloaked, pid == (uint)Environment.ProcessId);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return default;
        }
    }

    /// <summary>Nombre GDI del monitor que está ahora a pantalla completa (<see cref="FullscreenPolicy.FullscreenMonitor"/>),
    /// o null si ninguno o si algo falla.</summary>
    public static string? FullscreenMonitorDevice()
    {
        try
        {
            IntPtr fgHwnd = GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero) return null;
            var fg = Describe(fgHwnd);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (fg.Monitor == IntPtr.Zero || !GetMonitorInfo(fg.Monitor, ref mi)) return null;
            int quns = SHQueryUserNotificationState(out int state) == 0 ? state : 0;
            return MonitorDevice(FullscreenPolicy.FullscreenMonitor(fg, mi.rcMonitor.ToRectangle(), quns));
        }
        catch { return null; }
    }

    /// <summary>Nombre GDI (<c>\\.\DISPLAY2</c>) de un HMONITOR; null si no hay o la API falla.</summary>
    public static string? MonitorDevice(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero) return null;
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfoEx(monitor, ref mi) && !string.IsNullOrEmpty(mi.szDevice) ? mi.szDevice : null;
    }

    private static string? ClassOf(IntPtr hwnd)
    {
        var cls = new StringBuilder(256);
        return GetClassName(hwnd, cls, cls.Capacity) > 0 ? cls.ToString() : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_started) return;
        var hooks = WinEventHooks.Shared;
        hooks.Foreground -= OnForeground;
        hooks.MoveSize -= OnMoveSize;
        hooks.LocationSettled -= Reevaluate;
        hooks.Release();
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoEx(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int pquns);
}

/// <summary>
/// Los hooks de WinEvent del notch, COMPARTIDOS por todas sus ventanas (uno o uno por monitor) y por el host que sigue a la
/// ventana activa: una sola señal de primer plano en todo el proceso, sin hooks globales de teclado ni de ratón.
///
/// <list type="bullet">
/// <item><c>SetWinEventHook</c> fuera de proceso (<c>WINEVENT_OUTOFCONTEXT|WINEVENT_SKIPOWNPROCESS</c>): los eventos llegan
/// al hilo que llamó a <see cref="Acquire"/>, que debe tener bucle de mensajes (el de UI).</item>
/// <item>Se instalan con el primer <see cref="Acquire"/> y se quitan con el último <see cref="Release"/>.</item>
/// <item>LOCATIONCHANGE se escucha SOLO en el proceso de la ventana en primer plano y se reinstala en cada cambio de primer
/// plano. Un hook de todo el sistema recibe también el movimiento del cursor y del caret de cualquier proceso: despertaba
/// el hilo de UI con cada movimiento del ratón (adiós al 0 % en reposo). Solo <c>OBJID_WINDOW</c> de la ventana en primer
/// plano, con debounce de <see cref="DebounceMs"/>.</item>
/// </list>
/// El delegado del hook vive en un campo <c>static</c>: si el GC lo recolectara, Windows llamaría a memoria liberada. Un
/// suscriptor que lanza no deja sin aviso a los demás.
/// </summary>
internal sealed class WinEventHooks
{
    public const int DebounceMs = 150;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;

    private static readonly WinEventDelegate s_proc = OnWinEvent;

    public static WinEventHooks Shared { get; } = new();

    private readonly List<IntPtr> _hooks = new();
    private System.Windows.Forms.Timer? _debounce;
    private int _users;
    private IntPtr _locHook;
    private uint _locPid;

    /// <summary>Cambió la ventana en primer plano.</summary>
    public event Action? Foreground;
    /// <summary>Empieza (true) o termina (false) un arrastre/redimensionado de ventana.</summary>
    public event Action<bool>? MoveSize;
    /// <summary>La ventana en primer plano dejó de moverse o cambiar de tamaño (tras el debounce).</summary>
    public event Action? LocationSettled;

    private WinEventHooks() { }

    /// <summary>Un usuario más; el primero instala los hooks. Hilo de UI.</summary>
    public void Acquire()
    {
        if (_users++ > 0) return;
        if (_debounce is null)
        {
            _debounce = new System.Windows.Forms.Timer { Interval = DebounceMs };
            _debounce.Tick += (_, _) => { _debounce.Stop(); Raise(LocationSettled); };
        }
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND);
        RehookLocation();
    }

    /// <summary>Un usuario menos; el último quita los hooks.</summary>
    public void Release()
    {
        if (_users == 0 || --_users > 0) return;
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
        _locHook = IntPtr.Zero;
        _locPid = 0;
        _debounce?.Stop();
    }

    private IntPtr Hook(uint min, uint max, uint idProcess = 0)
    {
        IntPtr h = SetWinEventHook(min, max, IntPtr.Zero, s_proc, idProcess, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (h != IntPtr.Zero) _hooks.Add(h);
        else AppLog.Warn($"FullscreenWatcher: SetWinEventHook(0x{min:X}) falló ({Marshal.GetLastWin32Error()})");
        return h;
    }

    private void RehookLocation()
    {
        uint pid = 0;
        IntPtr fg = FullscreenWatcher.GetForegroundWindow();
        if (fg != IntPtr.Zero) FullscreenWatcher.GetWindowThreadProcessId(fg, out pid);
        if (pid == _locPid && _locHook != IntPtr.Zero) return;
        if (_locHook != IntPtr.Zero) { UnhookWinEvent(_locHook); _hooks.Remove(_locHook); _locHook = IntPtr.Zero; }
        _locPid = pid;
        if (pid == 0 || pid == (uint)Environment.ProcessId) return;
        _locHook = Hook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, pid);
    }

    private static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        var s = Shared;
        if (s._users == 0 || !s._hooks.Contains(hook)) return;
        try { s.Handle(ev, hwnd, idObject); }
        catch (Exception ex) { AppLog.Warn($"FullscreenWatcher: {ex.Message}"); }
    }

    private void Handle(uint ev, IntPtr hwnd, int idObject)
    {
        switch (ev)
        {
            case EVENT_SYSTEM_FOREGROUND:
                _debounce?.Stop();
                RehookLocation();
                Raise(Foreground);
                break;
            case EVENT_SYSTEM_MOVESIZESTART:
                Raise(MoveSize, true);
                break;
            case EVENT_SYSTEM_MOVESIZEEND:
                Raise(MoveSize, false);
                break;
            case EVENT_OBJECT_LOCATIONCHANGE:
                if (idObject != OBJID_WINDOW || hwnd != FullscreenWatcher.GetForegroundWindow()) return;
                _debounce?.Stop();   // reinicia la cuenta: solo evalúa cuando deja de moverse
                _debounce?.Start();
                break;
        }
    }

    private static void Raise(Action? e)
    {
        if (e is null) return;
        foreach (Action d in e.GetInvocationList())
        {
            try { d(); }
            catch (Exception ex) { AppLog.Warn($"FullscreenWatcher: {ex.Message}"); }
        }
    }

    private static void Raise(Action<bool>? e, bool arg)
    {
        if (e is null) return;
        foreach (Action<bool> d in e.GetInvocationList())
        {
            try { d(arg); }
            catch (Exception ex) { AppLog.Warn($"FullscreenWatcher: {ex.Message}"); }
        }
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────────────────────────

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
