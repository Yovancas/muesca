using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>
/// Disco y shell para las miniaturas de la bandeja (F5 · B4), lado Windows. SOLO desde el hilo de fondo de
/// <see cref="ShelfThumbnails"/> (un <see cref="StaWorker"/>: los extractores de miniaturas suelen ser de apartamento STA).
/// <list type="bullet">
/// <item><see cref="Probe"/>: existencia, tamaño y fecha con <see cref="FileInfo"/> / <see cref="DirectoryInfo"/>; las letras
/// de red o sin montar no se tocan (<see cref="ShelfThumbPolicy.Touches"/>), ni una ruta que pasa por un enlace simbólico o de
/// unión que lleva a otra máquina (<see cref="RemoteLink"/>).</item>
/// <item><see cref="Extract"/>: <c>SHCreateItemFromParsingName</c> + <c>IShellItemImageFactory::GetImage</c> con
/// <c>SIIGBF_RESIZETOFIT</c> (miniatura, o el icono si el tipo no tiene) y, si falla, <c>SIIGBF_ICONONLY</c>. El HBITMAP se
/// copia a un <see cref="Bitmap"/> propio (PArgb si viene premultiplicado, ARGB si no; sin alfa, opaco) y se libera. Antes se
/// repite la comprobación con lo que hay en el disco: ni accesos directos, ni carpetas cuyo <c>desktop.ini</c> no es seguro,
/// ni enlaces a otra máquina (NTLM).</item>
/// </list>
/// Nada lanza: un fallo es <see cref="ShelfFileProbe.Missing"/> o <c>null</c>.
/// </summary>
public sealed class ShellThumbnailSource : IShelfThumbnailSource
{
    /// <summary>Tope del lado de la imagen que se acepta del shell (la pedida ronda 48–72 px).</summary>
    private const int MaxSide = 1024;

    /// <summary>Tope de un <c>desktop.ini</c> que se lee (los normales ocupan unos cientos de bytes).</summary>
    private const int MaxDesktopIni = 64 * 1024;

    public ShelfFileProbe Probe(string path) => Probe(path, DriveTypeOf, OnDisk, LinkTargetOf);

    /// <summary>Como <see cref="Probe(string)"/> con el tipo de unidad, la consulta al disco y el destino de los enlaces
    /// inyectados (tests; sin <paramref name="linkTarget"/>, no hay enlaces).</summary>
    internal static ShelfFileProbe Probe(string path, Func<string, DriveType> driveType, Func<string, ShelfFileProbe> onDisk,
        Func<string, string?>? linkTarget = null)
    {
        if (string.IsNullOrWhiteSpace(path) || ShelfStore.IsUncOrDevicePath(path)) return ShelfFileProbe.Missing;
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0])
            && !ShelfThumbPolicy.Touches(SafeDriveType(driveType, path[..1] + @":\"))) return ShelfFileProbe.Opaque;
        if (linkTarget is not null && RemoteLink(path, linkTarget, driveType)) return ShelfFileProbe.Opaque;
        return onDisk(path);
    }

    /// <summary>
    /// ¿<paramref name="path"/> pasa por un enlace simbólico o de unión cuyo destino está en otra máquina (<c>\\host\…</c>,
    /// <c>\\?\UNC\…</c>, una letra de red o <c>file://</c>)? Seguirlo, para mirarlo o pedir su imagen, haría que Windows se
    /// autenticara por NTLM contra ese host. Se mira de la raíz hacia abajo y se para en el primero remoto: cada consulta lee el
    /// propio enlace (sus atributos y su destino, <see cref="FileSystemInfo.LinkTarget"/>) sin seguirlo, así que no sale a la red.
    /// Un enlace que no se puede leer no cuenta.
    /// </summary>
    internal static bool RemoteLink(string path, Func<string, string?> linkTarget, Func<string, DriveType> driveType)
    {
        var chain = new List<string>();
        for (string? p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p)) chain.Add(p);
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            string? target;
            try { target = linkTarget(chain[i]); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException or System.Security.SecurityException) { continue; }
            if (target is not null && RemoteTarget(target, driveType)) return true;
        }
        return false;
    }

    /// <summary>¿El destino de un enlace está en otra máquina? Las formas de dispositivo de una letra local
    /// (<c>\\?\C:\…</c>, <c>\??\C:\…</c>) son locales; cualquier otra ruta de red o de dispositivo, no. Una letra de red o sin
    /// montar, tampoco. Un destino relativo se queda en la misma máquina.</summary>
    internal static bool RemoteTarget(string target, Func<string, DriveType> driveType)
    {
        var t = target.Trim();
        foreach (var prefix in DevicePrefixes)
            if (t.Length >= prefix.Length + 2 && t.StartsWith(prefix, StringComparison.Ordinal)
                && char.IsAsciiLetter(t[prefix.Length]) && t[prefix.Length + 1] == ':')
            {
                t = t[prefix.Length..];
                break;
            }
        if (ShelfStore.IsUncOrDevicePath(t) || t.Contains("://", StringComparison.Ordinal)
            || t.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        return t.Length >= 2 && t[1] == ':' && char.IsAsciiLetter(t[0])
            && !ShelfThumbPolicy.Touches(SafeDriveType(driveType, t[..1] + @":\"));
    }

    private static readonly string[] DevicePrefixes = { @"\\?\", @"\??\", @"\\.\" };

    private static DriveType SafeDriveType(Func<string, DriveType> driveType, string root)
    {
        try { return driveType(root); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return DriveType.Unknown; }
    }

    public Bitmap? Extract(string path, int sizePx)
    {
        if (sizePx <= 0 || sizePx > MaxSide || string.IsNullOrWhiteSpace(path) || ShelfStore.IsUncOrDevicePath(path)) return null;
        if (!ShellImageAllowed(path)) return null;
        IShellItemImageFactory? factory = null;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory) < 0 || factory is null) return null;
            var size = new SIZE { cx = sizePx, cy = sizePx };
            if (factory.GetImage(size, SIIGBF_RESIZETOFIT, out hbm) < 0 || hbm == IntPtr.Zero)
            {
                hbm = IntPtr.Zero;
                if (factory.GetImage(size, SIIGBF_ICONONLY, out hbm) < 0 || hbm == IntPtr.Zero)
                {
                    hbm = IntPtr.Zero;
                    return null;
                }
            }
            return ToBitmap(hbm);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidCastException or ArgumentException or OutOfMemoryException)
        {
            AppLog.Warn($"notch: bandeja: el shell no dio miniatura: {ex.Message}");
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    private static DriveType DriveTypeOf(string root) => new DriveInfo(root).DriveType;

    /// <summary>Justo antes de pedir la imagen, otra vez con lo que HAY en el disco (pudo cambiar tras mirarlo): nada que pase
    /// por un enlace a otra máquina; una carpeta, salvo si su <c>desktop.ini</c> no es seguro; un archivo, salvo los tipos con
    /// glifo propio (<see cref="ShelfThumbPolicy.WantsShellImage"/>).</summary>
    private static bool ShellImageAllowed(string path)
    {
        if (RemoteLink(path, LinkTargetOf, DriveTypeOf)) return false;
        if (Directory.Exists(path)) return !UnsafeFolderIcon(path);
        return File.Exists(path) && ShelfThumbPolicy.WantsShellImage(path, ShelfProbeState.File);
    }

    /// <summary>Destino de <paramref name="path"/> si es un enlace (simbólico o de unión), sin seguirlo; <c>null</c> si no lo
    /// es. Los atributos son los del propio enlace: el último tramo no se sigue.</summary>
    private static string? LinkTargetOf(string path)
    {
        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReparsePoint) == 0) return null;
        FileSystemInfo info = (attrs & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
        return info.LinkTarget;
    }

    /// <summary>¿Carpeta cuyo <c>desktop.ini</c> no es seguro pedírselo al shell (<see cref="ShelfThumbPolicy.UnsafeFolderIcon"/>)?
    /// Uno enorme también cuenta: no es un desktop.ini normal y no merece la pena leerlo. Si no se puede leer, no.</summary>
    private static bool UnsafeFolderIcon(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            var ini = new FileInfo(Path.Combine(path, "desktop.ini"));
            if (!ini.Exists) return false;
            return ini.Length > MaxDesktopIni || ShelfThumbPolicy.UnsafeFolderIcon(File.ReadAllText(ini.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static ShelfFileProbe OnDisk(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Exists) return new ShelfFileProbe(ShelfProbeState.File, file.Length, file.LastWriteTimeUtc);
            var dir = new DirectoryInfo(path);
            if (dir.Exists) return new ShelfFileProbe(ShelfProbeState.Folder, 0, dir.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException) { }
        return ShelfFileProbe.Missing;
    }

    /// <summary>
    /// Copia el HBITMAP a un <see cref="Bitmap"/> de arriba abajo con <c>GetDIBits</c> (sea DIB de abajo arriba o no). El shell
    /// da 32 bpp con alfa; uno sin alfa (todo a 0) o de menos bits se toma como opaco. Con alfa, lo normal es que venga
    /// PREMULTIPLICADO (el formato de <c>AlphaBlend</c>) y va tal cual a un PArgb (sin oscurecer los bordes por multiplicar
    /// dos veces); si algún canal pasa de su alfa no lo está, y va a un ARGB (sin halos claros).
    /// </summary>
    private static Bitmap? ToBitmap(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var info) == 0) return null;
        int w = info.bmWidth, h = Math.Abs(info.bmHeight);
        if (w <= 0 || h <= 0 || w > MaxSide || h > MaxSide) return null;

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
                biCompression = BI_RGB,
            },
        };
        var px = new byte[w * h * 4];
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        try
        {
            if (GetDIBits(dc, hbm, 0, (uint)h, px, ref bmi, DIB_RGB_COLORS) != h) return null;
        }
        finally { DeleteDC(dc); }

        bool alpha = info.bmBitsPixel == 32;
        if (alpha)
        {
            alpha = false;
            for (int i = 3; i < px.Length; i += 4)
                if (px[i] != 0) { alpha = true; break; }
        }
        if (!alpha)
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        var format = alpha && Premultiplied(px) ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppArgb;

        return FromBgra(px, w, h, format);
    }

    /// <summary>
    /// <see cref="Bitmap"/> de <paramref name="w"/> × <paramref name="h"/> con los píxeles BGRA de arriba abajo de
    /// <paramref name="bgra"/>. Si bloquear o copiar falla, el bitmap se libera antes de que la excepción siga su camino (no se
    /// queda el GDI+ esperando al finalizador). <paramref name="created"/>: lo ve recién creado (tests).
    /// </summary>
    internal static Bitmap FromBgra(byte[] bgra, int w, int h, PixelFormat format, Action<Bitmap>? created = null)
    {
        var bmp = new Bitmap(w, h, format);
        try
        {
            created?.Invoke(bmp);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, format);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }

    /// <summary>¿BGRA premultiplicado? Ningún canal de color pasa de su alfa (en uno sin premultiplicar, los bordes
    /// semitransparentes de color claro sí pasan).</summary>
    internal static bool Premultiplied(byte[] bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- Win32

    private const int SIIGBF_RESIZETOFIT = 0x00, SIIGBF_ICONONLY = 0x04;
    private const uint BI_RGB = 0, DIB_RGB_COLORS = 0;

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx, cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    // Cabecera y sitio para las máscaras de color (GetDIBits no las escribe con BI_RGB a 32 bpp, pero el búfer es el de la API).
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors0, bmiColors1, bmiColors2;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr h, int c, out BITMAP pv);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFO bmi,
        uint usage);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr h);
}
