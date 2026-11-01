using System.Runtime.InteropServices;

namespace ClaudeBarWin.UI.Notch;

/// <summary>P/Invoke de la ventana del notch (estilos, ratón, topmost, DPI del monitor y bucle por DwmFlush).
/// Solo declaraciones y constantes: la lógica vive en <see cref="NotchForm"/>.</summary>
internal static class NotchNative
{
    // ---- estilos extendidos
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // ---- mensajes
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_MOUSEHWHEEL = 0x020E;
    public const int WM_MOUSELEAVE = 0x02A3;
    public const int WM_DPICHANGED = 0x02E0;

    public const int MA_NOACTIVATE = 3;
    public const int HTCLIENT = 1;
    public const int HTTRANSPARENT = -1;
    public const int SPI_SETWORKAREA = 0x002F;
    public const int WHEEL_DELTA = 120;

    // ---- TrackMouseEvent
    public const uint TME_LEAVE = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint cbSize, dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);

    // ---- topmost
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string name);

    // ---- ventana de sombra
    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hwnd, int cmd);

    // ---- monitor y DPI
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    // ---- bucle de animación
    public const uint PM_NOREMOVE = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    /// <summary>Bloquea hasta la siguiente composición de DWM (HRESULT; &lt; 0 = DWM no compone).</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    // ---- clic real en los botones del Permiso (ver PermissionInputGuard)
    public const int VK_LBUTTON = 0x01;

    /// <summary>Posición del cursor (pantalla) cuando se generó el mensaje que se está atendiendo.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetMessagePos();

    /// <summary>Estado físico de una tecla o botón del ratón (bit alto = pulsado ahora; bit bajo = pulsado desde la
    /// consulta anterior).</summary>
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    // ---- lParam de los mensajes de ratón (coordenadas con signo: monitores a la izquierda/arriba son negativos)
    public static int LoWordSigned(IntPtr p) => unchecked((short)(long)p);
    public static int HiWordSigned(IntPtr p) => unchecked((short)((long)p >> 16));
}
