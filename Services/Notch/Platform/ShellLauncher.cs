using System.ComponentModel;
using System.Diagnostics;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>
/// Lo que la bandeja pide al shell de Windows (F5 · B4). Qué se abre y qué no lo decide <see cref="ShelfOpen"/> (el texto
/// nunca llega aquí para ejecutarse, los programas solo se enseñan); esto solo lo hace. Nada lanza: un fallo se anota y
/// devuelve <c>false</c>. Hilo de UI (el portapapeles necesita STA).
/// </summary>
public sealed class ShellLauncher : IShellLauncher
{
    private readonly Func<ProcessStartInfo, bool> _start;
    private readonly Func<string, bool> _isDirectory;
    private readonly Func<string, bool> _isFile;

    public ShellLauncher() : this(null, null, null) { }

    /// <summary>Con el lanzamiento y las comprobaciones de disco inyectados (tests). Por defecto, <c>Process.Start</c>,
    /// <see cref="Directory.Exists"/> y <see cref="File.Exists"/>.</summary>
    internal ShellLauncher(Func<ProcessStartInfo, bool>? start, Func<string, bool>? isDirectory, Func<string, bool>? isFile)
    {
        _start = start ?? StartProcess;
        _isDirectory = isDirectory ?? Directory.Exists;
        _isFile = isFile ?? File.Exists;
    }

    /// <summary>Explorador por ruta absoluta: nada de buscar «explorer.exe» en el PATH ni en la carpeta actual.</summary>
    public static string ExplorerPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>Argumentos para enseñar <paramref name="path"/> seleccionado en el Explorador.</summary>
    public static string RevealArguments(string path) => "/select,\"" + path + "\"";

    /// <summary>Documento con su programa asociado (<c>ShellExecute</c> «open»). Nunca una ruta de red o de dispositivo
    /// (refuerzo: <see cref="ShelfOpen"/> ya no las deja pasar).</summary>
    public bool OpenFile(string path) =>
        !ShelfStore.IsUncOrDevicePath(path) && _start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });

    /// <summary>Carpeta en el Explorador (por el Explorador, no por <c>ShellExecute</c> sobre la ruta: una carpeta que se
    /// llame «x.exe» no se confunde con un programa). Se comprueba JUSTO antes que sigue siendo una carpeta: si en su lugar
    /// hay ahora un archivo (borrada y sustituida tras la foto), el Explorador lo abriría con su programa; en ese caso solo
    /// se enseña seleccionado, y si no hay nada, nada.</summary>
    public bool OpenFolder(string path)
    {
        if (ShelfStore.IsUncOrDevicePath(path)) return false;   // ni comprobarla: podría colgar o autenticarse por NTLM
        if (SafeCheck(_isDirectory, path))
            return _start(new ProcessStartInfo(ExplorerPath, "\"" + path + "\"") { UseShellExecute = false });
        return SafeCheck(_isFile, path) && Reveal(path);
    }

    public bool Reveal(string path) =>
        !ShelfStore.IsUncOrDevicePath(path) && _start(new ProcessStartInfo(ExplorerPath, RevealArguments(path)) { UseShellExecute = false });

    /// <summary>Enlace http(s) con el navegador predeterminado. Al shell va la forma canónica (<c>uri.AbsoluteUri</c>, ya
    /// escapada), nunca el texto tal cual; lo que no sea un http(s) absoluto no se lanza.</summary>
    public bool OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;
        return _start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public bool CopyText(string text)
    {
        try
        {
            // Reintentos cortos: otra app puede tener abierto el portapapeles un instante.
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), copy: true, retryTimes: 5, retryDelay: 50);
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException
            or ArgumentException)
        {
            AppLog.Warn($"notch: bandeja: no se pudo copiar el texto: {ex.Message}");
            return false;
        }
    }

    private static bool SafeCheck(Func<string, bool> check, string path)
    {
        try { return check(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static bool StartProcess(ProcessStartInfo psi)
    {
        try
        {
            using var p = Process.Start(psi);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException
            or PlatformNotSupportedException)
        {
            AppLog.Warn(FailureLog(psi, ex));
            return false;
        }
    }

    /// <summary>
    /// Línea del log al fallar un lanzamiento: el tipo de entrada y el código de Windows, NUNCA la ruta ni la URL (el
    /// mensaje de la excepción las lleva y el log se comparte al pedir ayuda). PURO.
    /// </summary>
    internal static string FailureLog(ProcessStartInfo psi, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(psi);
        ArgumentNullException.ThrowIfNull(ex);
        string kind = !psi.UseShellExecute ? (psi.Arguments.StartsWith("/select,", StringComparison.Ordinal) ? "revelar" : "carpeta")
            : string.Equals(psi.Verb, "open", StringComparison.Ordinal) ? "archivo" : "enlace";
        string code = ex is Win32Exception w ? w.NativeErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : ex.GetType().Name;
        return $"notch: bandeja: no se pudo abrir ({kind}): código {code}";
    }
}
