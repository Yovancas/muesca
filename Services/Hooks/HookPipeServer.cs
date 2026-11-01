using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using ClaudeBarWin.Models;
using Microsoft.Win32.SafeHandles;

namespace ClaudeBarWin.Services;

/// <summary>
/// Servidor del Named Pipe que recibe eventos del hook de Claude Code (una línea JSON por evento).
///
/// v0.3.10 (auditoría cruzada Claude + Codex):
///  • Concurrente: tras aceptar una conexión se abre YA la siguiente instancia y la conexión se atiende
///    aparte (máx. <see cref="MaxConcurrent"/>). Antes era estrictamente secuencial: un cliente que se
///    quedaba conectado sin mandar '\n' bloqueaba a todos los hooks (cada uno perdía su Connect).
///  • Orden: cada conexión recibe un número de secuencia AL ACEPTARLA (el bucle de accept es secuencial y
///    un hook de Claude Code no arranca hasta que el anterior terminó) y cada línea se despacha en cuanto
///    llega, sin esperar al cierre. <see cref="SessionStore"/> descarta un evento con secuencia menor que el
///    último aplicado: la concurrencia no puede reordenar la fase de una sesión (CR-01 de la 2ª revisión).
///  • Límites por conexión: <see cref="ReadTimeout"/> y <see cref="MaxBytes"/> (antes ReadLine sin tope).
///  • Permisos explícitos: lectura/escritura para el USUARIO actual (su SID, no el "owner"): si ClaudeBar
///    arrancaba elevado, el DACL por defecto solo dejaba escribir a Administradores y los hooks de las
///    sesiones NO elevadas se descartaban en silencio.
///  • Un fallo persistente al crear el pipe ya no gira al 100 % de CPU (backoff), una excepción en el
///    manejador no corta nada, y cancelar devuelve siempre el hueco del semáforo.
///  • Resuelve los ancestros del proceso cliente (claude.exe, shell, terminal) para el foco y la poda.
/// </summary>
public sealed class HookPipeServer : IDisposable
{
    public const string DefaultPipeName = "claudebar";
    internal const int MaxConcurrent = 8;
    internal const int MaxBytes = 64 * 1024;
    internal static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    private readonly string _pipeName;
    private readonly SemaphoreSlim _slots = new(MaxConcurrent, MaxConcurrent);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _seq;

    /// <summary>Se dispara (en un hilo del thread-pool) por cada evento recibido.</summary>
    public event Action<HookEvent>? EventReceived;

    public HookPipeServer(string pipeName = DefaultPipeName) => _pipeName = pipeName;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        var loop = _loop;
        try { _cts?.Cancel(); } catch { }
        try { loop?.Wait(TimeSpan.FromSeconds(1)); } catch { /* cancelado */ }
        _loop = null;
    }

    private NamedPipeServerStream CreateInstance()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            _pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            bool slotHeld = false;
            NamedPipeServerStream? server = null;
            try
            {
                await _slots.WaitAsync(ct).ConfigureAwait(false);
                slotHeld = true;
                server = CreateInstance();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                failures = 0;
                long seq = Interlocked.Increment(ref _seq);
                var conn = server;
                server = null;     // la conexión y el hueco pasan a ser del manejador
                slotHeld = false;
                _ = Task.Run(() => HandleConnectionAsync(conn, seq, ct));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                if (failures == 1 || failures % 50 == 0) AppLog.Warn($"pipe {_pipeName}: fallo al aceptar (x{failures}): {ex.Message}");
                try { await Task.Delay(Math.Min(5000, 100 * failures), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                server?.Dispose();
                if (slotHeld) _slots.Release();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, long seq, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<ProcessInfo>? ancestors = null;
            try
            {
                if (GetNamedPipeClientProcessId(server.SafePipeHandle, out uint clientPid) && clientPid != 0)
                    ancestors = ProcessTree.Ancestors((int)clientPid);
            }
            catch { /* sin ancestros: el foco/poda degradan a TTL */ }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ReadTimeout);
            int lineNo = 0;
            await ReadLinesAsync(server, timeout.Token, line =>
            {
                var e = HookEvent.Parse(line);
                if (e is null) return;
                e.Ancestors = ancestors;
                e.Seq = seq * 1000 + Math.Min(lineNo++, 999);
                try { EventReceived?.Invoke(e); }
                catch (Exception ex) { AppLog.Error("manejador de evento de sesión", ex); }
            }).ConfigureAwait(false);
        }
        catch { /* conexión rota / timeout / demasiado grande: se descarta el resto */ }
        finally
        {
            server.Dispose();
            _slots.Release();
        }
    }

    /// <summary>
    /// Lee de <paramref name="s"/> hasta EOF con tope de bytes y entrega CADA línea en cuanto se completa
    /// (no espera al cierre). Una línea final sin '\n' también se entrega al llegar EOF.
    /// </summary>
    internal static async Task ReadLinesAsync(Stream s, CancellationToken ct, Action<string> onLine)
    {
        var buf = new byte[4096];
        using var pending = new MemoryStream();
        long total = 0;
        int n;
        while ((n = await s.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            total += n;
            if (total > MaxBytes) throw new InvalidDataException("mensaje del hook demasiado grande");
            int start = 0;
            for (int i = Array.IndexOf(buf, (byte)'\n', 0, n); i >= 0; i = start < n ? Array.IndexOf(buf, (byte)'\n', start, n - start) : -1)
            {
                pending.Write(buf, start, i - start);
                Emit(pending, onLine);
                start = i + 1;
            }
            if (start < n) pending.Write(buf, start, n - start);
        }
        Emit(pending, onLine);
    }

    private static void Emit(MemoryStream pending, Action<string> onLine)
    {
        if (pending.Length == 0) return;
        var line = Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length).TrimEnd('\r');
        pending.SetLength(0);
        if (line.Length > 0) onLine(line);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
