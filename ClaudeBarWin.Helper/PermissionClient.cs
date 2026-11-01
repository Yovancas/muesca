using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeBarWin.Helper;

/// <summary>
/// Cliente del pipe de permisos con fallo seguro: cualquier fallo (sin servidor, servidor no confiable, plazo agotado,
/// respuesta rota o de otra petición) devuelve <see cref="PermissionVerdict.None"/> y Claude Code muestra su diálogo.
/// </summary>
internal static class PermissionClient
{
    private const string ServerExeName = "ClaudeBarWin.exe";

    // Una respuesta válida ronda los 90 bytes; más de esto no es ClaudeBar.
    private const int MaxResponseBytes = 4096;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static PermissionVerdict Ask(byte[] hookInput, string pipeName, int connectMs, int decisionMs, Func<int, bool> isTrustedServer)
    {
        try
        {
            var requestId = Guid.NewGuid().ToString();
            var request = PermissionProtocol.BuildRequest(requestId, hookInput);

            // Sin PipeOptions.CurrentUserOnly: compara el propietario del pipe con el del token, que es
            // «Administradores» en un proceso elevado y el usuario en uno normal, así que ClaudeBar y Claude Code a
            // distinto nivel no se entendían nunca. El acceso ya lo limita la ACL del servidor (usuario + SYSTEM) y
            // la identidad del servidor se comprueba abajo por PID y ruta del ejecutable.
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(connectMs);

            // El servidor se valida ANTES de enviarle nada: un pipe suplantado no recibe ni la vista previa.
            if (!TryGetServerProcessId(pipe, out var serverPid) || !isTrustedServer(serverPid))
                return PermissionVerdict.None;

            using var deadline = new CancellationTokenSource(decisionMs);
            var exchange = ExchangeAsync(pipe, request, deadline.Token);
            // Doble cerrojo: si una E/S no atendiera la cancelación, el helper no espera más allá del plazo.
            if (!exchange.Wait(decisionMs))
                return PermissionVerdict.None;

            return exchange.Result is { } line && PermissionProtocol.TryParseResponse(line, requestId, out var verdict)
                ? verdict
                : PermissionVerdict.None;
        }
        catch
        {
            return PermissionVerdict.None;
        }
    }

    /// <summary>Escribe la petición y lee una única línea terminada en '\n'; null si el servidor cierra antes o se pasa de tamaño.</summary>
    private static async Task<string?> ExchangeAsync(Stream pipe, string request, CancellationToken token)
    {
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);

        var buffer = new byte[MaxResponseBytes];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await pipe.ReadAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
            if (read == 0)
                return null;

            var newline = Array.IndexOf(buffer, (byte)'\n', count, read);
            count += read;
            if (newline >= 0)
                return StrictUtf8.GetString(buffer, 0, newline);
        }

        return null;
    }

    /// <summary>
    /// PID del servidor del pipe. Solo Windows lo puede obtener (GetNamedPipeServerProcessId); en otros SO se
    /// devuelve 0 y decide <c>isTrustedServer</c> (el real rechaza todo fuera de Windows).
    /// </summary>
    private static bool TryGetServerProcessId(NamedPipeClientStream pipe, out int pid)
    {
        pid = 0;
        if (!OperatingSystem.IsWindows())
            return true;

        var handle = pipe.SafePipeHandle;
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            if (!GetNamedPipeServerProcessId(handle.DangerousGetHandle(), out var serverPid) || serverPid == 0 || serverPid > int.MaxValue)
                return false;
            pid = (int)serverPid;
            return true;
        }
        finally
        {
            if (added)
                handle.DangerousRelease();
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);

    /// <summary>
    /// Validación real del servidor: el proceso es ClaudeBarWin.exe y está en la carpeta del helper o en la de instalación
    /// (%LOCALAPPDATA%\ClaudeBar\current con Velopack; %LOCALAPPDATA%\Programs\ClaudeBarWin con Inno, hasta la 0.4) o, si el
    /// helper es el de un paquete MSIX (edición de la Store, con identidad o ejecutado desde su carpeta por Git Bash), SOLO en
    /// la carpeta de su paquete (<see cref="HelperPackage.TrustedPackageDir"/>); y no tiene menos nivel que este helper
    /// (<see cref="IsServerIntegrityOk"/>).
    /// Fuera de Windows, o ante cualquier fallo, no es confiable.
    /// </summary>
    public static bool IsTrustedServer(int pid)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0)
            return false;

        try
        {
            return TryGetProcessImagePath(pid, out var path) && IsTrustedServerPath(
                path,
                AppContext.BaseDirectory,
                HelperPaths.LocalAppData(Environment.GetEnvironmentVariable),
                HelperPackage.TrustedPackageDir(HelperPackage.CurrentPath(), AppContext.BaseDirectory))
                && IsServerIntegrityOk(pid);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Nivel de un token: elevado (UAC) y RID de su etiqueta de integridad (0x2000 media, 0x3000 alta…).</summary>
    internal readonly record struct TokenLevel(bool Elevated, int Integrity);

    /// <summary>
    /// ¿Puede este helper aceptar decisiones de ese servidor? Solo si el servidor no tiene MENOS nivel: si el helper está
    /// elevado, el servidor también; y su integridad no es menor. Si no, un ClaudeBar de integridad media (o cualquier
    /// proceso medio que le mande clics sintéticos) aprobaría comandos de un Claude Code administrador: salto de UAC.
    /// </summary>
    internal static bool IntegrityAllows(TokenLevel helper, TokenLevel server) =>
        (!helper.Elevated || server.Elevated) && helper.Integrity <= server.Integrity;

    /// <summary>
    /// Compara el token de este proceso con el del servidor (<c>OpenProcessToken</c> con PROCESS_QUERY_LIMITED_INFORMATION
    /// + <c>TokenElevation</c>/<c>TokenIntegrityLevel</c>). Ante cualquier fallo (no se abre, no se lee), <c>false</c>.
    /// </summary>
    internal static bool IsServerIntegrityOk(int pid)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0) return false;
        try
        {
            return TryGetTokenLevel(GetCurrentProcess(), out var helper)
                && TryGetProcessTokenLevel(pid, out var server)
                && IntegrityAllows(helper, server);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Nivel del token del proceso <paramref name="pid"/>; <c>false</c> si no se puede leer.</summary>
    internal static bool TryGetProcessTokenLevel(int pid, out TokenLevel level)
    {
        level = default;
        if (!OperatingSystem.IsWindows() || pid <= 0) return false;
        IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (h == IntPtr.Zero) return false;
        try { return TryGetTokenLevel(h, out level); }
        finally { CloseHandle(h); }
    }

    private static bool TryGetTokenLevel(IntPtr process, out TokenLevel level)
    {
        level = default;
        if (!OpenProcessToken(process, TokenQuery, out var token) || token == IntPtr.Zero) return false;
        try
        {
            // TOKEN_ELEVATION: un DWORD.
            IntPtr elevation = Marshal.AllocHGlobal(sizeof(int));
            bool elevated;
            try
            {
                if (!GetTokenInformation(token, TokenElevationClass, elevation, sizeof(int), out _)) return false;
                elevated = Marshal.ReadInt32(elevation) != 0;
            }
            finally { Marshal.FreeHGlobal(elevation); }

            // TOKEN_MANDATORY_LABEL: SID_AND_ATTRIBUTES; el último sub-authority del SID es el nivel.
            _ = GetTokenInformation(token, TokenIntegrityLevelClass, IntPtr.Zero, 0, out int size);
            if (size <= 0 || size > 1024) return false;
            IntPtr label = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevelClass, label, size, out _)) return false;
                IntPtr sid = Marshal.ReadIntPtr(label);
                if (sid == IntPtr.Zero) return false;
                IntPtr countPtr = GetSidSubAuthorityCount(sid);
                if (countPtr == IntPtr.Zero) return false;
                int count = Marshal.ReadByte(countPtr);
                if (count < 1) return false;
                IntPtr rid = GetSidSubAuthority(sid, (uint)(count - 1));
                if (rid == IntPtr.Zero) return false;
                level = new TokenLevel(elevated, Marshal.ReadInt32(rid));
                return true;
            }
            finally { Marshal.FreeHGlobal(label); }
        }
        finally { CloseHandle(token); }
    }

    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private const int TokenIntegrityLevelClass = 25;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    /// <summary>
    /// Ruta del ejecutable de <paramref name="pid"/> con <c>QueryFullProcessImageName</c>, que solo necesita
    /// PROCESS_QUERY_LIMITED_INFORMATION y por eso funciona aunque el otro proceso esté elevado y este no
    /// (<c>Process.MainModule</c> pide leer su memoria y ahí falla).
    /// </summary>
    internal static bool TryGetProcessImagePath(int pid, out string? path)
    {
        path = null;
        if (!OperatingSystem.IsWindows() || pid <= 0) return false;
        IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            if (!QueryFullProcessImageName(h, 0, buffer, ref size)) return false;
            path = buffer.ToString(0, size);
            return path.Length > 0;
        }
        finally { CloseHandle(h); }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <param name="packagePath">Carpeta de instalación del paquete del helper si es el de un paquete MSIX
    /// (<see cref="HelperPackage.TrustedPackageDir"/>); null si no. Con ella SOLO vale un servidor de esa carpeta: es de solo
    /// lectura y Windows no deja arrancar nada de ella si se toca. La carpeta del propio helper no cuenta entonces, porque
    /// lanzado por el alias puede ser la del alias (%LOCALAPPDATA%\Microsoft\WindowsApps\…), que el usuario puede escribir; ni
    /// las de Velopack o Inno, que también puede escribir: la app de la Store reescribe los hooks a su alias al arrancar.</param>
    internal static bool IsTrustedServerPath(string? serverExePath, string helperDir, string? localAppData, string? packagePath = null)
    {
        if (string.IsNullOrWhiteSpace(serverExePath))
            return false;

        var full = Path.GetFullPath(serverExePath);
        if (!string.Equals(Path.GetFileName(full), ServerExeName, StringComparison.OrdinalIgnoreCase))
            return false;

        var serverDir = Path.GetDirectoryName(full);
        if (serverDir is null)
            return false;

        if (!string.IsNullOrWhiteSpace(packagePath))
            return SameDirectory(serverDir, packagePath);

        if (SameDirectory(serverDir, helperDir))
            return true;

        return !string.IsNullOrWhiteSpace(localAppData)
               && (SameDirectory(serverDir, HelperPaths.VelopackAppDir(localAppData))
                   || SameDirectory(serverDir, HelperPaths.InstallDir(localAppData)));
    }

    private static bool SameDirectory(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);
}
