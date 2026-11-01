using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class SessionAggregatorTests
{
    private static SessionState S(string id, SessionPhase phase, DateTime when)
        => new() { SessionId = id, Cwd = "c\\" + id, ProjectName = id, Phase = phase, LastActivityUtc = when };

    private readonly DateTime _t0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Global_phase_is_most_urgent()
    {
        var agg = new SessionAggregator();
        var view = agg.BuildView(new[]
        {
            S("a", SessionPhase.Processing, _t0),
            S("b", SessionPhase.WaitingForApproval, _t0),
            S("c", SessionPhase.Idle, _t0),
        });
        Assert.Equal(SessionPhase.WaitingForApproval, view.GlobalPhase);
        Assert.Equal("b", view.Instances[0].SessionId); // el más prioritario primero
    }

    [Fact]
    public void Empty_snapshot_is_idle()
        => Assert.Equal(SessionPhase.Idle, new SessionAggregator().BuildView(Array.Empty<SessionState>()).GlobalPhase);

    [Fact]
    public void Seeding_does_not_notify_on_first_call()
    {
        var agg = new SessionAggregator();
        var n = agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0);
        Assert.Empty(n); // primera llamada solo siembra
    }

    [Fact]
    public void New_waiting_session_notifies_after_seed()
    {
        var agg = new SessionAggregator();
        agg.DiffNotifications(Array.Empty<SessionState>(), _t0); // seed vacío
        var n = agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(1));
        Assert.Single(n);
        Assert.Equal("a", n[0].SessionId);
    }

    [Fact]
    public void Same_waiting_session_does_not_renotify()
    {
        var agg = new SessionAggregator();
        agg.DiffNotifications(Array.Empty<SessionState>(), _t0);
        agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(1));
        var again = agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(2));
        Assert.Empty(again);
    }

    [Fact]
    public void Resolving_then_waiting_again_renotifies()
    {
        var agg = new SessionAggregator();
        agg.DiffNotifications(Array.Empty<SessionState>(), _t0);
        agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(1));
        agg.DiffNotifications(new[] { S("a", SessionPhase.Processing, _t0) }, _t0.AddSeconds(2)); // resuelto
        var renote = agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(3));
        Assert.Single(renote);
    }
}
