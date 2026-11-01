using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class SessionStoreTests
{
    private static HookEvent Ev(string id, string cwd, string ev, string status, string? tool = null)
        => new() { SessionId = id, Cwd = cwd, Event = ev, Status = status, Tool = tool };

    [Fact]
    public void Apply_creates_session_with_project_name_from_cwd()
    {
        var s = new SessionStore();
        s.Apply(Ev("s1", "C:\\Users\\z\\Proyectos\\phoenix", "PreToolUse", "running_tool"), DateTime.UtcNow);
        var sess = Assert.Single(s.Snapshot());
        Assert.Equal("s1", sess.SessionId);
        Assert.Equal("phoenix", sess.ProjectName);
        Assert.Equal(SessionPhase.Processing, sess.Phase);
    }

    [Fact]
    public void Apply_ignores_invalid_transition_but_keeps_session()
    {
        var s = new SessionStore();
        var t0 = DateTime.UtcNow;
        s.Apply(Ev("s1", "c", "SessionEnd", "ended"), t0);
        // Ended es terminal: un nuevo evento processing no debe revivirla a Processing
        s.Apply(Ev("s1", "c", "PreToolUse", "running_tool"), t0.AddSeconds(1));
        Assert.Equal(SessionPhase.Ended, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Apply_records_pending_tool_on_approval()
    {
        var s = new SessionStore();
        s.Apply(Ev("s1", "c", "PermissionRequest", "waiting_for_approval", tool: "Bash"), DateTime.UtcNow);
        Assert.Equal("Bash", s.Snapshot()[0].PendingTool);
    }

    [Fact]
    public void Prune_removes_stale_sessions()
    {
        var s = new SessionStore();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        s.Apply(Ev("old", "c", "PreToolUse", "running_tool"), t0);
        s.Apply(Ev("fresh", "c", "PreToolUse", "running_tool"), t0.AddMinutes(9));
        var removed = s.Prune(t0.AddMinutes(10), TimeSpan.FromMinutes(10));
        Assert.Equal(1, removed);
        Assert.Equal("fresh", Assert.Single(s.Snapshot()).SessionId);
    }

    [Fact]
    public void Apply_raises_Changed()
    {
        var s = new SessionStore();
        var fired = 0;
        s.Changed += () => fired++;
        s.Apply(Ev("s1", "c", "PreToolUse", "running_tool"), DateTime.UtcNow);
        Assert.Equal(1, fired);
    }
    // ── Estado ampliado para el notch (payload v2) ──

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static HookEvent E(string ev, string status, string? tool = null, string? preview = null, string? message = null)
        => new() { SessionId = "s", Cwd = "c", Event = ev, Status = status, Tool = tool, ToolInputPreview = preview, Message = message };

    [Fact]
    public void Stop_records_LastEvent_and_PhaseSince()
    {
        var st = new SessionStore();
        st.Apply(E("UserPromptSubmit", "processing"), T0);
        st.Apply(E("Stop", "waiting_for_input"), T0.AddSeconds(30));
        var s = st.Snapshot().Single();
        Assert.Equal("Stop", s.LastEvent);
        Assert.Equal(T0.AddSeconds(30), s.PhaseSinceUtc);
    }

    // PhaseSinceUtc marca cuándo CAMBIÓ la fase: más eventos en la misma fase no la mueven.
    [Fact]
    public void PhaseSince_only_moves_when_phase_changes()
    {
        var st = new SessionStore();
        st.Apply(E("UserPromptSubmit", "processing"), T0);
        st.Apply(E("PreToolUse", "running_tool", tool: "Bash"), T0.AddSeconds(5));
        st.Apply(E("PostToolUse", "processing", tool: "Bash"), T0.AddSeconds(9));
        var s = st.Snapshot().Single();
        Assert.Equal(SessionPhase.Processing, s.Phase);
        Assert.Equal(T0, s.PhaseSinceUtc);
        Assert.Equal("PostToolUse", s.LastEvent);
    }

    [Fact]
    public void PostToolUse_sets_LastTool_and_preview()
    {
        var st = new SessionStore();
        st.Apply(E("PostToolUse", "processing", tool: "Bash", preview: "npm test"), T0);
        var s = st.Snapshot().Single();
        Assert.Equal("Bash", s.LastTool);
        Assert.Equal("npm test", s.LastToolPreview);
    }

    [Fact]
    public void PermissionRequest_sets_LastTool_and_preview()
    {
        var st = new SessionStore();
        st.Apply(E("PostToolUse", "processing", tool: "Bash", preview: "npm test"), T0);
        st.Apply(E("PermissionRequest", "waiting_for_approval", tool: "Edit", preview: "C:\a.cs"), T0.AddSeconds(1));
        var s = st.Snapshot().Single();
        Assert.Equal("Edit", s.LastTool);
        Assert.Equal("C:\a.cs", s.LastToolPreview);
    }

    // Una herramienta nueva sin vista previa no hereda la de la anterior.
    [Fact]
    public void New_tool_without_preview_clears_old_preview()
    {
        var st = new SessionStore();
        st.Apply(E("PostToolUse", "processing", tool: "Bash", preview: "npm test"), T0);
        st.Apply(E("PostToolUse", "processing", tool: "TodoWrite", preview: ""), T0.AddSeconds(1));
        var s = st.Snapshot().Single();
        Assert.Equal("TodoWrite", s.LastTool);
        Assert.Null(s.LastToolPreview);
    }

    // PreToolUse no toca LastTool (solo PostToolUse y PermissionRequest, según la spec).
    [Fact]
    public void PreToolUse_does_not_set_LastTool()
    {
        var st = new SessionStore();
        st.Apply(E("PreToolUse", "running_tool", tool: "Bash", preview: "ls"), T0);
        Assert.Null(st.Snapshot().Single().LastTool);
    }

    [Fact]
    public void Notification_sets_LastMessage()
    {
        var st = new SessionStore();
        st.Apply(E("Notification", "waiting_for_approval", message: "Claude needs your permission"), T0);
        Assert.Equal("Claude needs your permission", st.Snapshot().Single().LastMessage);
    }

    [Fact]
    public void Noop_event_does_not_change_LastEvent()
    {
        var st = new SessionStore();
        st.Apply(E("UserPromptSubmit", "processing"), T0);
        st.Apply(E("Notification", "noop", message: "auth ok"), T0.AddSeconds(3));
        var s = st.Snapshot().Single();
        Assert.Equal("UserPromptSubmit", s.LastEvent);
        Assert.Equal(T0, s.PhaseSinceUtc);
        Assert.Null(s.LastMessage);
    }

    [Fact]
    public void Clone_copies_extended_fields()
    {
        var st = new SessionStore();
        st.Apply(E("PermissionRequest", "waiting_for_approval", tool: "Bash", preview: "rm x"), T0);
        st.Apply(E("Notification", "waiting_for_approval", message: "m"), T0.AddSeconds(1));
        var c = st.Snapshot().Single().Clone();
        Assert.Equal("Notification", c.LastEvent);
        Assert.Equal(T0, c.PhaseSinceUtc);
        Assert.Equal("Bash", c.LastTool);
        Assert.Equal("rm x", c.LastToolPreview);
        Assert.Equal("m", c.LastMessage);
    }
}
