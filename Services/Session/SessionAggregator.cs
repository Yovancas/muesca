using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>Lógica pura: snapshot de sesiones → vista agregada + diffing de avisos.</summary>
public sealed class SessionAggregator
{
    // IDs que en el snapshot anterior estaban esperando atención (para no re-avisar).
    private readonly HashSet<string> _knownWaiting = new();
    private bool _seeded;

    /// <summary>Ordena por prioridad de fase y luego por actividad reciente; deriva la fase global.</summary>
    public LiveSessionsView BuildView(IReadOnlyList<SessionState> snapshot)
    {
        var ordered = snapshot
            .OrderBy(s => s.Phase.Priority())
            .ThenByDescending(s => s.LastActivityUtc)
            .ToList();

        var global = ordered.Count == 0 ? SessionPhase.Idle : ordered[0].Phase;
        return new LiveSessionsView
        {
            GlobalPhase = global,
            Instances = ordered,
            ActiveCount = ordered.Count(s => s.Phase.IsActive() || s.Phase.NeedsAttention()),
        };
    }

    /// <summary>
    /// Devuelve las sesiones que pasaron a "necesita atención" desde la última llamada.
    /// La primera llamada solo siembra (no avisa) para no disparar al arrancar.
    /// </summary>
    public IReadOnlyList<SessionState> DiffNotifications(IReadOnlyList<SessionState> snapshot, DateTime nowUtc)
    {
        var waitingNow = snapshot.Where(s => s.Phase.NeedsAttention()).ToList();
        var idsNow = waitingNow.Select(s => s.SessionId).ToHashSet();

        if (!_seeded)
        {
            _seeded = true;
            SyncKnown(idsNow);
            return Array.Empty<SessionState>();
        }

        var fresh = waitingNow.Where(s => !_knownWaiting.Contains(s.SessionId)).ToList();
        SyncKnown(idsNow);
        return fresh;
    }

    private void SyncKnown(HashSet<string> idsNow)
    {
        _knownWaiting.RemoveWhere(id => !idsNow.Contains(id)); // los que ya no esperan se olvidan
        foreach (var id in idsNow) _knownWaiting.Add(id);
    }
}
