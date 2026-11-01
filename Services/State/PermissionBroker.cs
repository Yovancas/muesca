namespace ClaudeBarWin.Services.State;

/// <summary>
/// Decisión del notch ante un PermissionRequest. <see cref="None"/> = sin decisión: el helper no escribe nada y
/// Claude Code muestra su diálogo normal. (Espejo del enum del helper, que es otro ensamblado.)
/// </summary>
public enum PermissionVerdict { None, Allow, Deny }

/// <summary>Una petición de permiso pendiente, tal como llega por el pipe (solo la vista previa, nunca el tool_input).
/// <paramref name="PreviewComplete"/>: la vista previa es TODO lo que se aprueba (<c>preview_complete</c> del helper, ya
/// revalidado por el servidor); sin ella no hay botones.</summary>
public sealed record PermissionRequestInfo(string RequestId, string SessionId, string ProjectName, string Tool,
    string? Preview, DateTime ReceivedUtc, bool PreviewComplete);

/// <summary>
/// Peticiones de permiso pendientes y su decisión. PURO: sin timers ni hilos propios; el instante llega por
/// parámetro (regla del repo). Quien hace cumplir el plazo es <c>PermissionPipeServer</c>, que llama a
/// <see cref="Expire"/> al vencer.
///
/// <list type="bullet">
/// <item><b>Un solo uso:</b> cada <c>request_id</c> se registra una vez; repetido (o ya visto) ⇒ <see cref="PermissionVerdict.None"/>.</item>
/// <item><b>Fallo seguro:</b> caducar (<see cref="Expire"/>, o un <see cref="Resolve"/> tardío), cancelar la sesión o
/// que el helper se vaya completan la tarea con <see cref="PermissionVerdict.None"/>.</item>
/// <item><b>Nadie mirando:</b> con <see cref="Accepting"/> a <c>false</c> (el notch oculto en pantalla completa),
/// <see cref="Register"/> contesta <see cref="PermissionVerdict.None"/> al momento y lo pendiente se cerró al apagarlo: el
/// helper suelta enseguida y Claude Code muestra su diálogo, en vez de esperar el plazo sin que nadie vea el aviso.</item>
/// <item><b>Hilos:</b> lo llaman el servidor del pipe (thread-pool) y el notch (hilo de UI), así que todo va bajo un
/// candado. Las tareas se completan FUERA del candado y con continuaciones asíncronas: el clic del notch nunca
/// ejecuta en línea la escritura del pipe. <see cref="Changed"/> se dispara en el hilo que hizo el cambio.</item>
/// </list>
/// </summary>
public sealed class PermissionBroker
{
    /// <summary>
    /// Plazo por defecto: 24 s, un segundo MENOS que el plazo interno del helper (25 s,
    /// <c>PermissionProtocol.DecisionTimeoutMs</c>). El del helper empieza antes (antes de conectar y de escribir la
    /// petición) que este (desde <see cref="PermissionRequestInfo.ReceivedUtc"/>): con plazos iguales, un clic en los
    /// últimos milisegundos lo aceptaría el broker («Permitido» en el notch) con el helper ya colgado, y Claude Code
    /// mostraría su diálogo igualmente. Con el margen, el broker siempre caduca primero.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(24);

    // Ids ya vistos que se recuerdan para rechazar repeticiones (los más antiguos se olvidan).
    private const int SeenCapacity = 256;

    private readonly object _gate = new();
    private readonly List<Entry> _pending = new();             // en orden de llegada
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private bool _accepting = true;

    public PermissionBroker(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
        Ttl = ttl;
    }

    /// <summary>Plazo de cada petición desde <see cref="PermissionRequestInfo.ReceivedUtc"/>.</summary>
    public TimeSpan Ttl { get; }

    /// <summary>¿Se admiten peticiones? <c>false</c> mientras nadie puede ver el notch (ver <see cref="SetAccepting"/>).</summary>
    public bool Accepting
    {
        get { lock (_gate) return _accepting; }
    }

    /// <summary>
    /// Admite o no peticiones. Al dejar de admitirlas se cierran sin decisión TODAS las pendientes y, mientras siga así,
    /// <see cref="Register"/> devuelve <see cref="PermissionVerdict.None"/> al instante. Al volver a admitirlas, las nuevas
    /// se registran como siempre (las cerradas no vuelven).
    /// </summary>
    public void SetAccepting(bool accepting)
    {
        lock (_gate)
        {
            if (_accepting == accepting) return;
            _accepting = accepting;
        }
        if (!accepting) CloseWhere(_ => true);
    }

    /// <summary>Algo cambió en <see cref="Pending"/> (registro, decisión, cancelación o caducidad).</summary>
    public event Action? Changed;

    /// <summary>Copia de las peticiones pendientes, en orden de llegada.</summary>
    public IReadOnlyList<PermissionRequestInfo> Pending
    {
        get { lock (_gate) return _pending.Select(e => e.Info).ToArray(); }
    }

    /// <summary>Registra una petición; la tarea se completa con la decisión (o <see cref="PermissionVerdict.None"/>, al
    /// momento si no se admiten peticiones: <see cref="Accepting"/>).</summary>
    public Task<PermissionVerdict> Register(PermissionRequestInfo r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var entry = new Entry(r);
        lock (_gate)
        {
            if (!_accepting) return Task.FromResult(PermissionVerdict.None);
            if (string.IsNullOrEmpty(r.RequestId) || !_seen.Add(r.RequestId))
                return Task.FromResult(PermissionVerdict.None);
            _seenOrder.Enqueue(r.RequestId);
            while (_seenOrder.Count > SeenCapacity) _seen.Remove(_seenOrder.Dequeue());
            _pending.Add(entry);
        }
        Changed?.Invoke();
        return entry.Done.Task;
    }

    /// <summary>
    /// Contesta una petición. <c>false</c> si no existe, ya se resolvió o caducó en <paramref name="nowUtc"/>
    /// (en ese caso se cierra con <see cref="PermissionVerdict.None"/>: un clic tardío nunca decide).
    /// </summary>
    public bool Resolve(string requestId, PermissionVerdict v, DateTime nowUtc)
    {
        Entry? e;
        bool expired;
        lock (_gate)
        {
            e = Take(x => string.Equals(x.Info.RequestId, requestId, StringComparison.Ordinal));
            if (e is null) return false;
            expired = nowUtc - e.Info.ReceivedUtc >= Ttl;
        }
        e.Done.TrySetResult(expired ? PermissionVerdict.None : v);
        Changed?.Invoke();
        return !expired;
    }

    /// <summary>Cierra sin decisión una petición concreta (el helper se fue). <c>false</c> si ya no estaba.</summary>
    public bool Cancel(string requestId)
    {
        Entry? e;
        lock (_gate) e = Take(x => string.Equals(x.Info.RequestId, requestId, StringComparison.Ordinal));
        if (e is null) return false;
        e.Done.TrySetResult(PermissionVerdict.None);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Cierra sin decisión todas las de <paramref name="sessionId"/> (hubo actividad: ya se contestó en la terminal).</summary>
    public void CancelSession(string sessionId) =>
        CloseWhere(x => string.Equals(x.Info.SessionId, sessionId, StringComparison.Ordinal));

    /// <summary>Cierra sin decisión las que en <paramref name="nowUtc"/> llevan <see cref="Ttl"/> o más.</summary>
    public void Expire(DateTime nowUtc) => CloseWhere(x => nowUtc - x.Info.ReceivedUtc >= Ttl);

    private void CloseWhere(Func<Entry, bool> match)
    {
        List<Entry>? closed = null;
        lock (_gate)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (!match(_pending[i])) continue;
                (closed ??= new()).Add(_pending[i]);
                _pending.RemoveAt(i);
            }
        }
        if (closed is null) return;
        foreach (var e in closed) e.Done.TrySetResult(PermissionVerdict.None);
        Changed?.Invoke();
    }

    private Entry? Take(Func<Entry, bool> match)
    {
        int i = _pending.FindIndex(x => match(x));
        if (i < 0) return null;
        var e = _pending[i];
        _pending.RemoveAt(i);
        return e;
    }

    private sealed class Entry(PermissionRequestInfo info)
    {
        public PermissionRequestInfo Info { get; } = info;
        public TaskCompletionSource<PermissionVerdict> Done { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
