namespace ClaudeBarWin.Models;

/// <summary>Fase del ciclo de vida de una sesión de Claude Code (máquina de estados).</summary>
public enum SessionPhase
{
    Idle,
    Processing,
    WaitingForApproval,
    WaitingForInput,
    Compacting,
    Ended,
}

public static class SessionPhaseExtensions
{
    /// <summary>¿La sesión necesita atención del usuario (espera OK o input)?</summary>
    public static bool NeedsAttention(this SessionPhase p)
        => p is SessionPhase.WaitingForApproval or SessionPhase.WaitingForInput;

    /// <summary>¿La sesión está trabajando (procesando o compactando)?</summary>
    public static bool IsActive(this SessionPhase p)
        => p is SessionPhase.Processing or SessionPhase.Compacting;

    /// <summary>Prioridad para ordenar instancias y elegir la fase global (menor = más prioritario).</summary>
    public static int Priority(this SessionPhase p) => p switch
    {
        SessionPhase.WaitingForApproval => 0,
        SessionPhase.WaitingForInput => 1,
        SessionPhase.Processing => 2,
        SessionPhase.Compacting => 2,
        SessionPhase.Idle => 3,
        SessionPhase.Ended => 4,
        _ => 5,
    };

    /// <summary>¿Es válida la transición a <paramref name="next"/>?</summary>
    public static bool CanTransition(this SessionPhase from, SessionPhase next)
    {
        if (from == next) return true;          // no-op
        if (from == SessionPhase.Ended) return false; // terminal
        if (next == SessionPhase.Ended) return true;  // cualquiera puede terminar
        return (from, next) switch
        {
            (SessionPhase.Idle, SessionPhase.Processing) => true,
            (SessionPhase.Idle, SessionPhase.WaitingForApproval) => true,
            (SessionPhase.Idle, SessionPhase.Compacting) => true,
            (SessionPhase.Processing, SessionPhase.WaitingForInput) => true,
            (SessionPhase.Processing, SessionPhase.WaitingForApproval) => true,
            (SessionPhase.Processing, SessionPhase.Compacting) => true,
            (SessionPhase.Processing, SessionPhase.Idle) => true,
            (SessionPhase.WaitingForInput, SessionPhase.Processing) => true,
            (SessionPhase.WaitingForInput, SessionPhase.Idle) => true,
            (SessionPhase.WaitingForInput, SessionPhase.Compacting) => true,
            (SessionPhase.WaitingForApproval, SessionPhase.Processing) => true,
            (SessionPhase.WaitingForApproval, SessionPhase.Idle) => true,
            (SessionPhase.WaitingForApproval, SessionPhase.WaitingForInput) => true,
            (SessionPhase.Compacting, SessionPhase.Processing) => true,
            (SessionPhase.Compacting, SessionPhase.Idle) => true,
            (SessionPhase.Compacting, SessionPhase.WaitingForInput) => true,
            _ => false,
        };
    }
}
