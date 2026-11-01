using System.Runtime.InteropServices;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>
/// Los monitores conectados con su nombre del EDID, para <see cref="MonitorNames"/>. Capa fina sobre
/// <c>Screen.AllScreens</c> + <c>QueryDisplayConfig</c>: cada ruta activa da su nombre GDI (<c>\\.\DISPLAY2</c>, por
/// <c>DISPLAYCONFIG_SOURCE_DEVICE_NAME</c>) y el nombre amigable del monitor (<c>DISPLAYCONFIG_TARGET_DEVICE_NAME</c>).
/// Nada lanza: si la API falla, los monitores salen sin nombre («Monitor 2 · …»). Los nombres se cachean hasta que
/// Windows avisa de un cambio de pantallas; el menú de la bandeja fuerza una lectura nueva al abrirse.
/// </summary>
public static class MonitorCatalog
{
    private static readonly object Gate = new();
    private static IReadOnlyDictionary<string, string>? _names;
    private static bool _hooked;

    /// <summary>Monitores en el orden de <c>Screen.AllScreens</c>. <paramref name="refresh"/>: relee los nombres del EDID.</summary>
    public static IReadOnlyList<MonitorInfo> Current(bool refresh = false)
    {
        var names = FriendlyNames(refresh);
        return Screen.AllScreens
            .Select(sc => new MonitorInfo(sc.DeviceName, sc.Bounds, sc.Primary,
                names.TryGetValue(sc.DeviceName, out var n) ? n : null))
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> FriendlyNames(bool refresh)
    {
        lock (Gate)
        {
            if (!_hooked)
            {
                _hooked = true;
                try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => { lock (Gate) _names = null; }; }
                catch (Exception ex) { AppLog.Warn($"sin aviso de cambio de pantallas: {ex.Message}"); }
            }
            if (refresh || _names is null) _names = Query();
            return _names;
        }
    }

    /// <summary>Nombre GDI → nombre amigable del monitor. Vacío si la API falla. En un clon (varios monitores por
    /// salida) se queda con el primero que tenga nombre.</summary>
    internal static IReadOnlyDictionary<string, string> Query()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            int err = 0;
            DISPLAYCONFIG_PATH_INFO[] paths = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
            DISPLAYCONFIG_MODE_INFO[] modes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();
            // La configuración puede cambiar entre las dos llamadas: ERROR_INSUFFICIENT_BUFFER → se repite.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint nPaths, out uint nModes);
                if (err != 0) break;
                paths = new DISPLAYCONFIG_PATH_INFO[nPaths];
                modes = new DISPLAYCONFIG_MODE_INFO[nModes];
                err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref nPaths, paths, ref nModes, modes, IntPtr.Zero);
                if (err != ERROR_INSUFFICIENT_BUFFER)
                {
                    if (err == 0) Array.Resize(ref paths, (int)nPaths);
                    break;
                }
            }
            if (err != 0)
            {
                AppLog.Warn($"QueryDisplayConfig falló ({err}): monitores sin nombre");
                return map;
            }

            foreach (var p in paths)
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = p.sourceInfo.adapterId,
                        id = p.sourceInfo.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0 || string.IsNullOrEmpty(source.viewGdiDeviceName)) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = p.targetInfo.adapterId,
                        id = p.targetInfo.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;
                var name = target.monitorFriendlyDeviceName?.Trim();
                if (!string.IsNullOrEmpty(name) && !map.ContainsKey(source.viewGdiDeviceName))
                    map[source.viewGdiDeviceName] = name;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            AppLog.Warn($"sin API de pantallas: {ex.Message}");
        }
        return map;
    }

    // ---- P/Invoke (wingdi.h / winuser.h)

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshRateNumerator;
        public uint refreshRateDenominator;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>Solo se pasa a QueryDisplayConfig (no se lee): 4 + 4 + LUID 8 + unión de 48 bytes.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
}
