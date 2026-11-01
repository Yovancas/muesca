namespace ClaudeBarWin.Models;

/// <summary>Estado vivo de una sesión de Claude Code. Mutado solo por SessionStore.</summary>
public sealed class SessionState
{
    public required string SessionId { get; init; }
    public required string Cwd { get; set; }
    public string ProjectName { get; set; } = "";
    public int? Pid { get; set; }
    /// <summary>
    /// Ancestros del proceso del hook (claude.exe, shell, terminal…), resueltos por el servidor del
    /// pipe. Sirven para "silenciar si tiene el foco" y para detectar sesiones muertas.
    /// </summary>
    public IReadOnlyList<ClaudeBarWin.Services.ProcessInfo> Ancestors { get; set; } =
        Array.Empty<ClaudeBarWin.Services.ProcessInfo>();
    /// <summary>El proceso de Claude Code de esta sesión (si se pudo identificar).</summary>
    public ClaudeBarWin.Services.ProcessInfo? ClaudeProcess { get; set; }
    public SessionPhase Phase { get; set; } = SessionPhase.Idle;
    public string? PendingTool { get; set; }
    public DateTime LastActivityUtc { get; set; }
    /// <summary>Secuencia del último evento aplicado (ver HookEvent.Seq).</summary>
    public long LastSeq { get; set; }
    /// <summary>Nombre del último evento no-noop ("Stop" vs "Notification": distingue terminar de quedarse ociosa).</summary>
    public string LastEvent { get; set; } = "";
    /// <summary>Cuándo cambió <see cref="Phase"/> por última vez (o cuándo se creó la sesión).</summary>
    public DateTime PhaseSinceUtc { get; set; }
    /// <summary>Herramienta del último PostToolUse/PermissionRequest.</summary>
    public string? LastTool { get; set; }
    /// <summary>Vista previa de <see cref="LastTool"/> (payload v2). Nunca al log.</summary>
    public string? LastToolPreview { get; set; }
    /// <summary>Mensaje de la última Notification (payload v2).</summary>
    public string? LastMessage { get; set; }
    /// <summary>
    /// ¿Aparece ahora en el registro de sesiones abiertas de Claude Code (~/.claude/sessions)? Mientras
    /// sea true no se poda por TTL: la sesión está abierta aunque lleve horas sin mandar hooks.
    /// </summary>
    public bool InRegistry { get; set; }
    /// <summary>
    /// ¿La fase Idle la puso el registro (Processing→Idle) y no un hook? Entonces un Stop/idle_prompt que
    /// llegue después todavía puede pasarla a WaitingForInput (si no, se perdería el «tu turno»).
    /// </summary>
    public bool IdleByRegistry { get; set; }

    /// <summary>Copia superficial para exponer snapshots inmutables a la UI.</summary>
    public SessionState Clone() => new()
    {
        SessionId = SessionId,
        Cwd = Cwd,
        ProjectName = ProjectName,
        Pid = Pid,
        Ancestors = Ancestors,
        ClaudeProcess = ClaudeProcess,
        Phase = Phase,
        PendingTool = PendingTool,
        LastActivityUtc = LastActivityUtc,
        LastSeq = LastSeq,
        LastEvent = LastEvent,
        PhaseSinceUtc = PhaseSinceUtc,
        LastTool = LastTool,
        LastToolPreview = LastToolPreview,
        LastMessage = LastMessage,
        InRegistry = InRegistry,
        IdleByRegistry = IdleByRegistry,
    };

    public static string ProjectNameFromCwd(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return "(sin proyecto)";
        var trimmed = cwd.TrimEnd('\\', '/');
        var idx = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : trimmed;
    }
}
