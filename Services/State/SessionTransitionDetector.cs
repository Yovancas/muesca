using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services.State;

/// <summary>Algo que acaba de pasar en una sesión de Claude Code (lo consumen el notch y los avisos).</summary>
public abstract record ClaudeSignal(string SessionId, string ProjectName, DateTime AtUtc);

/// <summary>La sesión, mientras procesa, ha usado una herramienta nueva (PostToolUse en F1).</summary>
public sealed record ToolActivity(string SessionId, string ProjectName, DateTime AtUtc, string Tool, string? Preview)
    : ClaudeSignal(SessionId, ProjectName, AtUtc);

/// <summary>La sesión terminó su turno (Stop) y espera tu siguiente mensaje.</summary>
public sealed record TaskCompleted(string SessionId, string ProjectName, DateTime AtUtc)
    : ClaudeSignal(SessionId, ProjectName, AtUtc);

/// <summary>La sesión pide permiso para una herramienta. <paramref name="RequestId"/>: la petición del helper de permisos
/// (F2) que se puede contestar desde el notch; <c>null</c> si solo lo sabemos por el hook de sesión (solo lectura).</summary>
public sealed record PermissionPending(string SessionId, string ProjectName, DateTime AtUtc, string Tool, string? Preview,
    string? RequestId = null)
    : ClaudeSignal(SessionId, ProjectName, AtUtc);

/// <summary>La petición <paramref name="RequestId"/> del helper ya no se puede contestar (resuelta, caducada, cancelada o
/// el helper se fue). No dice nada de la fase de la sesión: si sigue esperando, Claude Code muestra su diálogo.</summary>
public sealed record PermissionRequestClosed(string SessionId, string ProjectName, DateTime AtUtc, string RequestId)
    : ClaudeSignal(SessionId, ProjectName, AtUtc);

/// <summary>La petición de permiso se resolvió (aprobada, denegada o abandonada).</summary>
public sealed record PermissionCleared(string SessionId, string ProjectName, DateTime AtUtc)
    : ClaudeSignal(SessionId, ProjectName, AtUtc);

/// <summary>La sesión terminó (Ended) o desapareció del store (poda).</summary>
public sealed record SessionGone(string SessionId, string ProjectName, DateTime AtUtc)
    : ClaudeSignal(SessionId, ProjectName, AtUtc);

/// <summary>
/// Traduce snapshots sucesivos de <see cref="SessionState"/> en <see cref="ClaudeSignal"/>.
/// PURO: el instante llega por parámetro (regla del repo) y solo recuerda, por sesión, lo mínimo del
/// snapshot anterior (fase, último evento, herramienta y desde cuándo está en la fase).
///
/// <list type="bullet">
/// <item><b>TaskCompleted</b> solo si la sesión llega a WaitingForInput con <c>LastEvent == "Stop"</c>
/// viniendo de otra fase, o sigue en WaitingForInput pero con un <c>PhaseSinceUtc</c> nuevo (procesó y
/// terminó entre dos snapshots). Quedarse esperando por <c>Notification idle_prompt</c> NO cuenta.</item>
/// <item><b>PermissionPending</b> al entrar en WaitingForApproval (o con un <c>PhaseSinceUtc</c> nuevo:
/// otra petición); <b>PermissionCleared</b> al salir.</item>
/// <item><b>ToolActivity</b> cuando, en Processing, cambia la herramienta o su vista previa.</item>
/// <item><b>SessionGone</b> una sola vez: al pasar a Ended o al desaparecer del snapshot.</item>
/// <item><b>Primer snapshot</b>: solo PermissionPending para las que ya esperan permiso (no sabemos
/// cuándo terminaron las demás, así que nada de «Terminado» ni actividad al arrancar).</item>
/// </list>
/// </summary>
public sealed class SessionTransitionDetector
{
    private readonly Dictionary<string, Prev> _prev = new(StringComparer.Ordinal);
    private bool _primed;

    public IReadOnlyList<ClaudeSignal> Diff(IReadOnlyList<SessionState> snapshot, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var signals = new List<ClaudeSignal>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in snapshot)
        {
            if (s.Phase == SessionPhase.Ended) continue;   // terminada = ausente (SessionGone abajo)
            if (!seen.Add(s.SessionId)) continue;          // id duplicado: manda el primero
            _prev.TryGetValue(s.SessionId, out var p);

            if (p is null)
            {
                // Sesión nueva (o primer snapshot): solo lo que sigue vigente ahora mismo.
                if (s.Phase == SessionPhase.WaitingForApproval)
                    signals.Add(Pending(s, nowUtc));
                else if (_primed && s.Phase == SessionPhase.Processing && !string.IsNullOrEmpty(s.LastTool))
                    signals.Add(new ToolActivity(s.SessionId, s.ProjectName, nowUtc, s.LastTool!, s.LastToolPreview));
            }
            else
            {
                bool phaseChanged = p.Phase != s.Phase;
                bool reentered = !phaseChanged && s.PhaseSinceUtc != p.PhaseSinceUtc;

                if (p.Phase == SessionPhase.WaitingForApproval && phaseChanged)
                    signals.Add(new PermissionCleared(s.SessionId, s.ProjectName, nowUtc));

                if (s.Phase == SessionPhase.WaitingForApproval && (phaseChanged || reentered))
                    signals.Add(Pending(s, nowUtc));

                if (s.Phase == SessionPhase.WaitingForInput && (phaseChanged || reentered)
                    && string.Equals(s.LastEvent, "Stop", StringComparison.OrdinalIgnoreCase))
                    signals.Add(new TaskCompleted(s.SessionId, s.ProjectName, nowUtc));

                if (s.Phase == SessionPhase.Processing && !string.IsNullOrEmpty(s.LastTool)
                    && (s.LastTool != p.LastTool || s.LastToolPreview != p.LastToolPreview))
                    signals.Add(new ToolActivity(s.SessionId, s.ProjectName, nowUtc, s.LastTool!, s.LastToolPreview));
            }

            _prev[s.SessionId] = new Prev(s.Phase, s.LastEvent, s.LastTool, s.LastToolPreview, s.PhaseSinceUtc, s.ProjectName);
        }

        // Las que ya no están (o han pasado a Ended): una sola vez.
        List<string>? gone = null;
        foreach (var (id, p) in _prev)
        {
            if (seen.Contains(id)) continue;
            signals.Add(new SessionGone(id, p.ProjectName, nowUtc));
            (gone ??= new()).Add(id);
        }
        if (gone is not null) foreach (var id in gone) _prev.Remove(id);

        _primed = true;
        return signals;
    }

    private static PermissionPending Pending(SessionState s, DateTime nowUtc) =>
        new(s.SessionId, s.ProjectName, nowUtc, s.PendingTool ?? s.LastTool ?? "", s.LastToolPreview);

    private sealed record Prev(SessionPhase Phase, string LastEvent, string? LastTool, string? LastToolPreview,
        DateTime PhaseSinceUtc, string ProjectName);
}
