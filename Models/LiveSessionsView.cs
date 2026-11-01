namespace ClaudeBarWin.Models;

/// <summary>Vista agregada de las sesiones para la UI (mascota + lista).</summary>
public sealed class LiveSessionsView
{
    public SessionPhase GlobalPhase { get; init; } = SessionPhase.Idle;
    public IReadOnlyList<SessionState> Instances { get; init; } = Array.Empty<SessionState>();
    public int ActiveCount { get; init; }
}
