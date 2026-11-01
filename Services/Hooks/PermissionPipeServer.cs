using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services.State;
using Microsoft.Win32.SafeHandles;

namespace ClaudeBarWin.Services;

/// <summary>
/// Servidor del pipe de permisos (F2, spec §4.3): habla con <c>claudebar-helper permission</c>, una petición por
/// conexión. Fallo seguro de punta a punta: solo escribe la respuesta si el notch dio una decisión explícita; en
/// cualquier otro caso cierra sin escribir y el helper sale sin decisión (Claude Code muestra su diálogo).
///
/// <list type="bullet">
/// <item><b>Pipe propio:</b> dúplex, con la ACL de <see cref="HookPipeServer"/> (lectura/escritura solo para el SID del
/// usuario actual + SYSTEM) y <c>FirstPipeInstance</c> siempre que no quede ninguna instancia nuestra viva: si otro
/// proceso creó antes <c>claudebar-perm</c>, el servidor no se une a su pipe (lo reintenta con espera creciente).</item>
/// <item><b>Sin <c>PipeOptions.CurrentUserOnly</c> en el servidor</b> (a diferencia del cliente): en .NET esa opción
/// sustituye la ACL por una del «owner» del token, que con ClaudeBar elevado es Administradores, justo el fallo que
/// la ACL explícita de <see cref="HookPipeServer"/> evita. La ACL ya limita el pipe al usuario.</item>
/// <item><b>Cliente de confianza:</b> su PID (<c>GetNamedPipeClientProcessId</c>) debe descender de Claude Code
/// (<see cref="ProcessTree.FindClaude"/>, la misma regla que la sesión). Si no, se cuelga sin leer.</item>
/// <item><b>Por conexión:</b> leer UNA línea (tope <see cref="MaxRequestBytes"/>, plazo <see cref="ReadTimeout"/>) →
/// <see cref="PermissionBroker.Register"/> → esperar la decisión, el plazo del broker o que el helper cuelgue (una
/// lectura pendiente que termina) → escribir la respuesta o nada. El plazo lo hace cumplir este servidor
/// (<see cref="PermissionBroker.Expire"/>): el broker es puro.</item>
/// <item><b>Privacidad:</b> nada de la petición va a <see cref="AppLog"/> (la vista previa solo llega al notch).</item>
/// <item>Hasta <see cref="MaxInstances"/> instancias a la vez (conexiones + la que escucha).</item>
/// </list>
/// </summary>
public sealed class PermissionPipeServer : IDisposable
{
    public const string DefaultPipeName = "claudebar-perm";
    public const int ProtocolVersion = 1;
    internal const int MaxInstances = 4;
    internal const int MaxRequestBytes = 16 * 1024;
    /// <summary>La vista previa incompleta ya llega recortada por el helper (F1, Task 2); aquí se vuelve a recortar por si acaso.</summary>
    internal const int MaxPreviewChars = 160;
    /// <summary>Tope de una vista previa COMPLETA (espejo de <c>PermissionPreview.MaxChars</c> del helper): más larga ⇒ incompleta.</summary>
    internal const int MaxCompletePreviewChars = 600;
    /// <summary>
    /// Herramientas que pueden llevar botones (espejo de las del helper). El resto (Write, Edit, MultiEdit, NotebookEdit,
    /// ExitPlanMode, Task, <c>mcp__*</c>…) va siempre en solo lectura aunque una petición diga lo contrario.
    /// </summary>
    internal static readonly IReadOnlySet<string> AnswerableTools =
        new HashSet<string>(StringComparer.Ordinal) { "Bash", "WebFetch", "Read", "Glob", "Grep" };
    private const int MaxRequestIdChars = 64;
    internal static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);
    /// <summary>Tras escribir la respuesta, cuánto se espera a que el helper la lea y cuelgue antes de cerrar.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(1);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly PermissionBroker _broker;
    private readonly string _pipeName;
    private readonly Func<int, IReadOnlyList<ProcessInfo>> _ancestorsOf;
    private readonly SemaphoreSlim _slots = new(MaxInstances, MaxInstances);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _live;   // instancias nuestras vivas (para decidir FirstPipeInstance)

    /// <param name="ancestorsOf">Ancestros del PID cliente; por defecto <see cref="ProcessTree.Ancestors"/> (los tests lo sustituyen).</param>
    public PermissionPipeServer(PermissionBroker broker, string pipeName = DefaultPipeName,
        Func<int, IReadOnlyList<ProcessInfo>>? ancestorsOf = null)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _pipeName = pipeName;
        _ancestorsOf = ancestorsOf ?? (pid => ProcessTree.Ancestors(pid));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    /// <summary>¿Esa cadena de ancestros es de un proceso lanzado por Claude Code?</summary>
    public static bool IsClaudeDescendant(IReadOnlyList<ProcessInfo> ancestors) =>
        ancestors is not null && ProcessTree.FindClaude(ancestors) is not null;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;   // copia local: Stop anula y libera _cts aunque el Task.Run aún no haya arrancado
        _loop = Task.Run(() => AcceptLoopAsync(token));
    }

    /// <summary>
    /// Para el bucle de aceptación y libera su CTS (un <see cref="Start"/> posterior crea otro). No espera a los
    /// manejadores de conexión en curso: se cancelan con el mismo token y la petición pendiente se cierra sin decisión
    /// (nunca se escribe nada sin un clic explícito).
    /// </summary>
    public void Stop()
    {
        var loop = _loop;
        var cts = _cts;
        try { cts?.Cancel(); } catch { }
        try { loop?.Wait(TimeSpan.FromSeconds(1)); } catch { /* cancelado */ }
        _loop = null;
        _cts = null;
        cts?.Dispose();
    }

    private NamedPipeServerStream CreateInstance()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        // Sin instancias nuestras vivas, la nueva tiene que ser la PRIMERA del pipe: si ya existe, es de otro.
        var options = PipeOptions.Asynchronous | (Volatile.Read(ref _live) == 0 ? PipeOptions.FirstPipeInstance : 0);
        var server = NamedPipeServerStreamAcl.Create(
            _pipeName, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte, options, 0, 0, security);
        Interlocked.Increment(ref _live);
        return server;
    }

    /// <summary>Se resta ANTES de cerrar: mejor creer que no queda ninguna (FirstPipeInstance falla y se reintenta)
    /// que creer que queda una y unirse sin el flag a un pipe ajeno.</summary>
    private void Release(NamedPipeServerStream server)
    {
        Interlocked.Decrement(ref _live);
        try { server.Dispose(); } catch { }
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
                var conn = server;
                server = null;     // la conexión y su hueco pasan a ser del manejador
                slotHeld = false;
                _ = Task.Run(() => HandleConnectionAsync(conn, ct));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                if (failures == 1 || failures % 50 == 0) AppLog.Warn($"pipe {_pipeName}: fallo al aceptar (x{failures}): {ex.GetType().Name}");
                try { await Task.Delay(Math.Min(5000, 100 * failures), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                if (server is not null) Release(server);
                if (slotHeld) _slots.Release();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            if (!IsTrustedClient(pipe)) return;

            string? line;
            using (var read = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                read.CancelAfter(ReadTimeout);
                line = await ReadRequestLineAsync(pipe, read.Token).ConfigureAwait(false);
            }
            if (line is null || !TryParseRequest(line, DateTime.UtcNow, out var info)) return;
            // Sin vista previa completa no hay botones: se cierra YA sin decisión y Claude Code muestra su diálogo al
            // momento (el aviso de solo lectura lo pone el hook de sesión).
            if (!info.PreviewComplete) return;

            var decision = _broker.Register(info);
            using var watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Una lectura pendiente que termina (0 bytes = colgó, o datos de más) = el helper ya no espera.
            var gone = WatchAsync(pipe, watch.Token);
            var deadline = Task.Delay(_broker.Ttl, ct);
            var first = await Task.WhenAny(decision, gone, deadline).ConfigureAwait(false);
            if (first == deadline) _broker.Expire(info.ReceivedUtc + _broker.Ttl);
            else if (first == gone) _broker.Cancel(info.RequestId);

            var verdict = await decision.ConfigureAwait(false);   // ya completada (o lo estará tras Expire/Cancel)
            if (first != decision || verdict == PermissionVerdict.None || gone.IsCompleted) return;

            await pipe.WriteAsync(Encoding.UTF8.GetBytes(BuildResponse(info.RequestId, verdict) + "\n"), ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);
            // Que el helper lea antes de cerrar: él cuelga en cuanto tiene su línea.
            await Task.WhenAny(gone, Task.Delay(DrainTimeout, ct)).ConfigureAwait(false);
            watch.Cancel();
        }
        catch { /* conexión rota / cancelada / tope: se cierra sin respuesta */ }
        finally
        {
            Release(pipe);
            _slots.Release();
        }
    }

    private bool IsTrustedClient(NamedPipeServerStream pipe)
    {
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) || pid == 0 || pid > int.MaxValue) return false;
            return IsClaudeDescendant(_ancestorsOf((int)pid));
        }
        catch { return false; }
    }

    /// <summary>Una línea terminada en '\n' (UTF-8 estricto); null si cierra antes, se pasa del tope o manda algo tras el '\n'.</summary>
    private static async Task<string?> ReadRequestLineAsync(Stream s, CancellationToken ct)
    {
        var buf = new byte[MaxRequestBytes];
        int count = 0;
        while (count < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(count), ct).ConfigureAwait(false);
            if (n == 0) return null;
            int nl = Array.IndexOf(buf, (byte)'\n', count, n);
            count += n;
            if (nl >= 0) return nl == count - 1 ? StrictUtf8.GetString(buf, 0, nl).TrimEnd('\r') : null;
        }
        return null;
    }

    private static async Task WatchAsync(Stream s, CancellationToken ct)
    {
        try { await s.ReadAsync(new byte[1], ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { /* roto = colgó */ }
    }

    /// <summary>
    /// Petición del helper: <c>{"v":1,"request_id":"…","session_id":"…","cwd":"…","tool":"…","tool_input_preview":"…",
    /// "preview_complete":true}</c>. Exige v = 1 y un request_id no vacío (≤ 64); el resto, si falta o no es texto, queda
    /// vacío. El proyecto sale del cwd.
    /// <para><see cref="PermissionRequestInfo.PreviewComplete"/> solo si <c>preview_complete</c> es <c>true</c> literal, la
    /// herramienta está en <see cref="AnswerableTools"/> y la vista previa no está vacía, cabe en
    /// <see cref="MaxCompletePreviewChars"/> y se puede pintar tal cual (<see cref="IsDisplayablePreview"/>); entonces se
    /// conserva entera, saltos de línea incluidos. Si no, es de solo lectura y se recorta a <see cref="MaxPreviewChars"/>.</para>
    /// </summary>
    internal static bool TryParseRequest(string line, DateTime nowUtc, out PermissionRequestInfo info)
    {
        info = null!;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number
                || !v.TryGetInt32(out int version) || version != ProtocolVersion) return false;

            string Str(string name) =>
                root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";

            string id = Str("request_id");
            if (id.Length == 0 || id.Length > MaxRequestIdChars) return false;
            string tool = Str("tool"), preview = Str("tool_input_preview");
            bool complete = root.TryGetProperty("preview_complete", out var pc) && pc.ValueKind == JsonValueKind.True
                && AnswerableTools.Contains(tool) && preview.Length <= MaxCompletePreviewChars && IsDisplayablePreview(preview);
            if (!complete && preview.Length > MaxPreviewChars) preview = preview[..(MaxPreviewChars - 1)] + "…";

            info = new PermissionRequestInfo(id, Str("session_id"), SessionState.ProjectNameFromCwd(Str("cwd")),
                tool, preview.Length == 0 ? null : preview, nowUtc, complete);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ¿La vista previa se pinta tal cual? (espejo de <c>PermissionPreview.IsDisplayable</c> del helper): no vacía, sin
    /// blancos al principio ni al final, y sin más control que '\n' (nada de '\r', tabuladores, separadores Unicode, marcas
    /// bidi ni de ancho cero, uso privado o sustitutos sueltos), que pueden esconder o reordenar lo que se ve.
    /// </summary>
    internal static bool IsDisplayablePreview(string text)
    {
        if (string.IsNullOrEmpty(text) || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                if (CharUnicodeInfo.GetUnicodeCategory(text, i) is UnicodeCategory.Format or UnicodeCategory.PrivateUse)
                    return false;
                i++;
                continue;
            }
            if (c == '\n') continue;
            if (char.IsLowSurrogate(c)) return false;
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.PrivateUse:
                    return false;
            }
        }
        return true;
    }

    /// <summary>Respuesta de una línea (sin '\n'): <c>{"v":1,"request_id":"…","decision":"allow"|"deny"}</c>.</summary>
    internal static string BuildResponse(string requestId, PermissionVerdict verdict)
    {
        string decision = verdict switch
        {
            PermissionVerdict.Allow => "allow",
            PermissionVerdict.Deny => "deny",
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), "sin decisión no hay respuesta"),
        };
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("v", ProtocolVersion);
            w.WriteString("request_id", requestId);
            w.WriteString("decision", decision);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public void Dispose() => Stop();
}
