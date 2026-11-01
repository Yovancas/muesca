using ClaudeBarWin.Models;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="SessionTransitionDetector"/> es PURO: compara cada snapshot de sesiones con el anterior y
/// traduce los cambios en señales para el notch (herramienta, tarea terminada, permiso, sesión cerrada).
/// Lo crítico: quedarse ociosa por <c>Notification idle_prompt</c> NO es «Terminado».
/// </summary>
public class SessionTransitionDetectorTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static SessionState S(string id, SessionPhase phase, string lastEvent = "", DateTime? since = null,
        string? lastTool = null, string? preview = null, string? pendingTool = null) => new()
    {
        SessionId = id,
        Cwd = $"C:\\Proyectos\\{id}",
        ProjectName = id,
        Phase = phase,
        LastEvent = lastEvent,
        PhaseSinceUtc = since ?? T0,
        LastTool = lastTool,
        LastToolPreview = preview,
        PendingTool = pendingTool,
    };

    private static IReadOnlyList<SessionState> Snap(params SessionState[] s) => s;

    [Fact]
    public void Processing_to_WaitingForInput_via_Stop_is_TaskCompleted()
    {
        var d = new SessionTransitionDetector();
        d.Diff(Snap(S("a", SessionPhase.Processing, "UserPromptSubmit")), T0);
        var sig = d.Diff(Snap(S("a", SessionPhase.WaitingForInput, "Stop", T0.AddSeconds(30))), T0.AddSeconds(30));
        var done = Assert.IsType<TaskCompleted>(Assert.Single(sig));
        Assert.Equal("a", done.SessionId);
        Assert.Equal("a", done.ProjectName);
        Assert.Equal(T0.AddSeconds(30), done.AtUtc);
    }

    [Fact]
    public void Processing_to_WaitingForInput_via_idle_prompt_is_not_TaskCompleted()
    {
        // Review Focus 1: la sesión se queda esperando por Notification idle_prompt, no por Stop.
        var d = new SessionTransitionDetector();
        d.Diff(Snap(S("a", SessionPhase.Processing, "PostToolUse")), T0);
        var sig = d.Diff(Snap(S("a", SessionPhase.WaitingForInput, "Notification", T0.AddSeconds(60))), T0.AddSeconds(60));
        Assert.DoesNotContain(sig, x => x is TaskCompleted);
    }

    [Fact]
    public void Stop_between_snapshots_after_returning_to_WaitingForInput_is_TaskCompleted()
    {
        // Esperando → (procesa y termina entre dos snapshots) → esperando: lo delata PhaseSinceUtc.
        var d = new SessionTransitionDetector();
        d.Diff(Snap(S("a", SessionPhase.WaitingForInput, "Stop", T0)), T0);
        var sig = d.Diff(Snap(S("a", SessionPhase.WaitingForInput, "Stop", T0.AddSeconds(40))), T0.AddSeconds(40));
        Assert.IsType<TaskCompleted>(Assert.Single(sig));
        // Mismo snapshot repetido: nada nuevo.
        Assert.Empty(d.Diff(Snap(S("a", SessionPhase.WaitingForInput, "Stop", T0.AddSeconds(40))), T0.AddSeconds(50)));
    }

    [Fact]
    public void Entering_WaitingForApproval_emits_PermissionPending_with_tool_and_preview()
    {
        var d = new SessionTransitionDetector();
        d.Diff(Snap(S("a", SessionPhase.Processing, "PostToolUse", lastTool: "Read", preview: "x.cs")), T0);
        var sig = d.Diff(Snap(S("a", SessionPhase.WaitingForApproval, "PermissionRequest", T0.AddSeconds(5),
            lastTool: "Bash", preview: "npm test", pendingTool: "Bash")), T0.AddSeconds(5));
        var p = Assert.IsType<PermissionPending>(Assert.Single(sig));
        Assert.Equal("Bash", p.Tool);
        Assert.Equal("npm test", p.Preview);
        Assert.Equal("a", p.SessionId);
    }

    [Fact]
    public void Leaving_WaitingForApproval_emits_PermissionCleared()
    {
        var d = new SessionTransitionDetector();
        d.Diff(Snap(S("a", SessionPhase.WaitingForApproval, "PermissionRequest", pendingTool: "Bash")), T0);
        var sig = d.Diff(Snap(S("a", SessionPhase.Processing, "PostToolUse", T0.AddSeconds(3), lastTool: "Bash")), T0.AddSeconds(3));
        Assert.Contains(sig, x => x is PermissionCleared { SessionId: "a" });
        Assert.DoesNotContain(sig, x => x is PermissionPending);
    }

    [Fact]
    public void New_LastTool_while_Processing_emits_ToolActivity()
    {
        var d = new SessionTransitionDetector();
        d.Diff(Snap(S("a", SessionPhase.Processing, "UserPromptSubmit")), T0);
        var sig = d.Diff(Snap(S("a", SessionPhase.Processing, "PostToolUse", lastTool: "Edit", preview: "Program.cs")), T0.AddSeconds(2));
        var t = Assert.IsType<ToolActivity>(Assert.Single(sig));
        Assert.Equal("Edit", t.Tool);
        Assert.Equal("Program.cs", t.Preview);
        // Misma herramienta y vista previa: no se repite.
        Assert.Empty(d.Diff(Snap(S("a", SessionPhase.Processing, "PostToolUse", lastTool: "Edit", preview: "Program.cs")), T0.AddSeconds(3)));
        // Misma herramienta con otra vista previa: actividad nueva.
        Assert.IsType<ToolActivity>(Assert.Single(
            d.Diff(Snap(S("a", SessionPhase.Processing, "PostToolUse", lastTool: "Edit", preview: "Form.cs")), T0.AddSeconds(4))));
    }

    [Fact]
    public void Session_disappearing_emits_SessionGone()
    {
        var d = new SessionTransitionDetector();
        d.Diff(Snap(S("a", SessionPhase.Processing), S("b", SessionPhase.WaitingForInput)), T0);
        var sig = d.Diff(Snap(S("b", SessionPhase.WaitingForInput)), T0.AddSeconds(1));
        var g = Assert.IsType<SessionGone>(Assert.Single(sig));
        Assert.Equal("a", g.SessionId);
        Assert.Equal("a", g.ProjectName);
        Assert.Empty(d.Diff(Snap(S("b", SessionPhase.WaitingForInput)), T0.AddSeconds(2)));
    }

    [Fact]
    public void Ended_session_emits_SessionGone_once()
    {
        var d = new SessionTransitionDetector();
        d.Diff(Snap(S("a", SessionPhase.Processing)), T0);
        var sig = d.Diff(Snap(S("a", SessionPhase.Ended, "SessionEnd", T0.AddSeconds(1))), T0.AddSeconds(1));
        Assert.IsType<SessionGone>(Assert.Single(sig));
        // La poda posterior la quita del snapshot: no se duplica.
        Assert.Empty(d.Diff(Snap(), T0.AddSeconds(2)));
    }

    [Fact]
    public void First_snapshot_emits_PermissionPending_for_already_waiting_sessions_but_no_TaskCompleted()
    {
        var d = new SessionTransitionDetector();
        var sig = d.Diff(Snap(
            S("a", SessionPhase.WaitingForApproval, "PermissionRequest", lastTool: "Bash", preview: "rm x", pendingTool: "Bash"),
            S("b", SessionPhase.WaitingForInput, "Stop"),
            S("c", SessionPhase.Processing, "PostToolUse", lastTool: "Read")), T0);
        var p = Assert.IsType<PermissionPending>(Assert.Single(sig));
        Assert.Equal("a", p.SessionId);
        Assert.Equal("Bash", p.Tool);
        Assert.Equal("rm x", p.Preview);
    }
}
