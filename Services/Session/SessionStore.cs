using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>Fuente única de verdad de las sesiones vivas. Thread-safe vía lock.</summary>
public sealed class SessionStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SessionState> _sessions = new();

    /// <summary>Se dispara tras cualquier mutación (en el hilo que llamó Apply/Prune).</summary>
    public event Action? Changed;

    /// <summary>Aplica un evento del hook. nowUtc se inyecta para testabilidad.</summary>
    public void Apply(HookEvent e, DateTime nowUtc)
    {
        bool changed;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(e.SessionId, out var s))
            {
                s = new SessionState { SessionId = e.SessionId, Cwd = e.Cwd, PhaseSinceUtc = nowUtc };
                _sessions[e.SessionId] = s;
                _endedIds.Remove(e.SessionId); // vuelve a hablar por hook (p. ej. --resume): ya no está "terminada"
            }

            // Evento más viejo que el último aplicado (conexiones concurrentes del pipe): no reordenar la fase.
            if (e.Seq > 0 && e.Seq < s.LastSeq) return;
            if (e.Seq > 0) s.LastSeq = e.Seq;

            if (!string.IsNullOrEmpty(e.Cwd)) s.Cwd = e.Cwd;
            s.ProjectName = SessionState.ProjectNameFromCwd(s.Cwd);
            if (e.Pid is { } pid) s.Pid = pid;
            if (e.Ancestors is { Count: > 0 } anc)
            {
                s.Ancestors = anc;
                // Si el registro ya dijo cuál es su proceso, manda él: en instalaciones npm el ancestro que
                // encuentra el hook es node.exe/bun.exe y cambiarlo en cada evento dispararía Changed sin motivo.
                if (!s.InRegistry || s.ClaudeProcess is null)
                    s.ClaudeProcess = ProcessTree.FindClaude(anc) ?? s.ClaudeProcess;
            }

            if (!e.IsNoop)
            {
                var next = e.ToPhase();
                // Si el registro bajó la sesión a Idle antes de que llegara el Stop (o el idle_prompt), ese
                // «tu turno» tiene que entrar igual: Idle→WaitingForInput no vale por hooks, pero sí aquí.
                bool lateTurn = s.IdleByRegistry && s.Phase == SessionPhase.Idle && next == SessionPhase.WaitingForInput;
                if (next != s.Phase && (s.Phase.CanTransition(next) || lateTurn))
                {
                    s.Phase = next;
                    s.PhaseSinceUtc = nowUtc;
                    s.IdleByRegistry = false;
                }

                // Estado ampliado del notch: los noop (auth_success…) no lo tocan.
                s.LastEvent = e.Event;
                if (IsEvent(e, "PostToolUse") || IsEvent(e, "PermissionRequest"))
                {
                    if (!string.IsNullOrEmpty(e.Tool))
                    {
                        s.LastTool = e.Tool;
                        // Herramienta nueva sin vista previa: no heredar la de la anterior.
                        s.LastToolPreview = string.IsNullOrEmpty(e.ToolInputPreview) ? null : e.ToolInputPreview;
                    }
                }
                else if (IsEvent(e, "Notification") && !string.IsNullOrEmpty(e.Message))
                {
                    s.LastMessage = e.Message;
                }
            }

            s.PendingTool = s.Phase == SessionPhase.WaitingForApproval ? (e.Tool ?? s.PendingTool) : null;
            s.LastActivityUtc = nowUtc;
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    private static bool IsEvent(HookEvent e, string name) =>
        string.Equals(e.Event, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Elimina sesiones Ended o sin actividad desde hace más de ttl. Devuelve cuántas quitó.</summary>
    public int Prune(DateTime nowUtc, TimeSpan ttl)
    {
        int removed;
        lock (_lock)
        {
            var dead = _sessions.Values
                .Where(s => s.Phase == SessionPhase.Ended || (!s.InRegistry && nowUtc - s.LastActivityUtc >= ttl))
                .Select(s => s.SessionId)
                .ToList();
            foreach (var id in dead) RemoveLocked(id);
            removed = dead.Count;
        }
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    /// <summary>
    /// Poda "inteligente" (v0.3.10), llamada también en cada tick del refresco (antes solo al llegar
    /// eventos, así que una sesión muerta se quedaba en la lista para siempre si no llegaba otro evento):
    ///  • Ended → fuera.
    ///  • Si se conoce su claude.exe y ya no vive (mismo pid + misma hora de inicio) → fuera.
    ///  • TTL por fase: una sesión esperando tu respuesta puede estar horas quieta y sigue viva (antes
    ///    se borraba a los 10 min); una "procesando" sin eventos en 45 min casi seguro murió.
    /// </summary>
    public int Prune(DateTime nowUtc, Func<ProcessInfo, bool>? isAlive = null)
    {
        isAlive ??= ProcessTree.IsAlive;
        int removed;
        lock (_lock)
        {
            var dead = _sessions.Values.Where(s =>
                    s.Phase == SessionPhase.Ended
                    || (s.ClaudeProcess is { } cp && !isAlive(cp))
                    || (!s.InRegistry && nowUtc - s.LastActivityUtc >= TtlFor(s.Phase)))
                .Select(s => s.SessionId)
                .ToList();
            foreach (var id in dead) RemoveLocked(id);
            removed = dead.Count;
        }
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    internal static TimeSpan TtlFor(SessionPhase phase) => phase switch
    {
        SessionPhase.WaitingForInput or SessionPhase.WaitingForApproval => TimeSpan.FromHours(12),
        SessionPhase.Processing or SessionPhase.Compacting => TimeSpan.FromMinutes(45),
        _ => TimeSpan.FromMinutes(30),
    };

    /// <summary>
    /// Sesiones que terminaron (Ended) y se podaron, pero cuyo archivo del registro puede tardar un poco
    /// en desaparecer: sin esto, la siguiente pasada del registro la resucitaría como Idle.
    /// </summary>
    private readonly HashSet<string> _endedIds = new();

    /// <summary>
    /// Sesiones que faltaron UNA vez en el registro con su proceso aún vivo: basta con que falle una lectura
    /// de la hora de inicio para que el filtro la descarte, y quitarla perdería un permiso pendiente.
    /// </summary>
    private readonly HashSet<string> _missedOnce = new();

    private void RemoveLocked(string id)
    {
        if (_sessions.TryGetValue(id, out var s) && s.Phase == SessionPhase.Ended) _endedIds.Add(id);
        _sessions.Remove(id);
        _missedOnce.Remove(id);
    }

    /// <summary>
    /// Fusiona las sesiones abiertas según el registro de Claude Code (~/.claude/sessions), ya filtradas a
    /// procesos vivos. Reglas:
    ///  • Sesión desconocida → se añade (Processing si "busy", Idle si no) con su proceso de Claude Code.
    ///  • Conocida → se actualiza cwd/proceso; la fase la mandan los hooks: el registro solo mueve
    ///    Idle→Processing ("busy") y Processing→Idle ("idle"), y solo si su estado es MÁS NUEVO que el último
    ///    cambio de fase (si no, un "idle" atrasado pisaría el UserPromptSubmit recién llegado). Sin la vuelta a
    ///    Idle, un Stop perdido dejaría la sesión "procesando" para siempre, porque ya no caduca por TTL.
    ///    Esperas (aprobación/input), compactación y Ended nunca las toca.
    ///  • Estaba en el registro y ya no aparece → el proceso se cerró (o /clear cambió de sessionId): fuera.
    ///    Si su proceso sigue vivo, se espera a que falte DOS pasadas seguidas (un fallo puntual al leer la
    ///    hora de inicio no debe borrar una espera de aprobación).
    /// Dispara <see cref="Changed"/> solo si algo cambió. Devuelve si hubo cambios.
    /// </summary>
    public bool SyncRegistry(IReadOnlyList<RegistryEntry> live, DateTime nowUtc, Func<ProcessInfo, bool>? isAlive = null)
    {
        isAlive ??= ProcessTree.IsAlive;
        bool changed = false;
        lock (_lock)
        {
            var liveIds = new HashSet<string>();
            foreach (var e in live)
            {
                liveIds.Add(e.SessionId);
                _missedOnce.Remove(e.SessionId);
                if (_endedIds.Contains(e.SessionId)) continue;

                if (!_sessions.TryGetValue(e.SessionId, out var s))
                {
                    var since = e.StatusSinceUtc is { } ss && ss <= nowUtc ? ss : nowUtc;
                    _sessions[e.SessionId] = new SessionState
                    {
                        SessionId = e.SessionId,
                        Cwd = e.Cwd,
                        ProjectName = SessionState.ProjectNameFromCwd(e.Cwd),
                        Pid = e.Pid,
                        ClaudeProcess = e.Process,
                        Phase = e.IsBusy ? SessionPhase.Processing : SessionPhase.Idle,
                        PhaseSinceUtc = since,
                        LastActivityUtc = nowUtc,
                        InRegistry = true,
                    };
                    changed = true;
                    continue;
                }

                if (!s.InRegistry) { s.InRegistry = true; changed = true; }
                if (!string.IsNullOrEmpty(e.Cwd) && !string.Equals(s.Cwd, e.Cwd, StringComparison.Ordinal))
                {
                    s.Cwd = e.Cwd;
                    s.ProjectName = SessionState.ProjectNameFromCwd(e.Cwd);
                    changed = true;
                }
                if (e.Process is { } proc && s.ClaudeProcess != proc) { s.ClaudeProcess = proc; changed = true; }
                if (s.Pid is null) { s.Pid = e.Pid; changed = true; }

                bool newer = e.StatusSinceUtc is not { } st || st > s.PhaseSinceUtc;
                SessionPhase? next = (s.Phase, e.IsBusy, e.IsIdle) switch
                {
                    (SessionPhase.Idle, true, _) when newer => SessionPhase.Processing,
                    (SessionPhase.Processing, _, true) when newer && e.StatusSinceUtc is not null => SessionPhase.Idle,
                    _ => null,
                };
                if (next is { } n)
                {
                    s.Phase = n;
                    s.PhaseSinceUtc = nowUtc;
                    s.PendingTool = null;
                    s.IdleByRegistry = n == SessionPhase.Idle;
                    changed = true;
                }
            }

            foreach (var gone in _sessions.Values.Where(s => s.InRegistry && !liveIds.Contains(s.SessionId)).ToList())
            {
                if (gone.ClaudeProcess is { } cp && isAlive(cp) && _missedOnce.Add(gone.SessionId))
                    continue; // primera falta con el proceso vivo: se le da otra pasada
                RemoveLocked(gone.SessionId);
                changed = true;
            }
            _missedOnce.IntersectWith(_sessions.Keys);
            _endedIds.IntersectWith(liveIds); // solo hace falta recordar las que el registro aún lista
        }
        if (changed) Changed?.Invoke();
        return changed;
    }

    /// <summary>Snapshot inmutable de las sesiones actuales.</summary>
    public IReadOnlyList<SessionState> Snapshot()
    {
        lock (_lock) return _sessions.Values.Select(s => s.Clone()).ToList();
    }
}
