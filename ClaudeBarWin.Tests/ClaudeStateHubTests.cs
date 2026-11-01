using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="ClaudeStateHub"/>: API única (hilo de UI) que reparte sesiones, señales de transición y
/// cuota con sus cruces de umbral. Sin hilos propios: todo se dispara síncrono dentro de Publish*.
/// </summary>
public class ClaudeStateHubTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset R = new(2026, 10, 28, 15, 0, 0, TimeSpan.Zero);

    private static RealUsage U(double fiveHour) => new()
    {
        FiveHour = new UsageWindow(fiveHour, R),
        SevenDay = new UsageWindow(10, R.AddDays(3)),
    };

    private static SessionState S(SessionPhase phase, string lastEvent, DateTime since) => new()
    {
        SessionId = "a", Cwd = "C:\\p\\a", ProjectName = "a", Phase = phase, LastEvent = lastEvent, PhaseSinceUtc = since,
    };

    /// <summary>Beta del 29-oct: con la API en 429 al arrancar, el notch salía sin barras («—»). La última cuota conocida
    /// se ENSEÑA, pero no cuenta para los avisos 80/95 (su gracia de arranque necesita una lectura real).</summary>
    [Fact]
    public void Display_only_quota_is_shown_but_never_feeds_the_threshold_tracker()
    {
        var hub = new ClaudeStateHub(new QuotaThresholdTracker(new[] { 80, 95 }));
        var changes = new List<RealUsage>();
        var crossings = new List<QuotaCrossing>();
        hub.QuotaChanged += changes.Add;
        hub.QuotaThresholdCrossed += crossings.Add;

        hub.PublishQuota(U(85), observe: false);     // caché del arranque
        Assert.Equal(85, hub.Quota!.FiveHour!.UtilizationPct);
        Assert.Single(changes);

        hub.PublishQuota(U(86));                      // primera lectura real: gracia (ya estaba por encima)
        Assert.Empty(crossings);
    }

    [Fact]
    public void Hub_forwards_quota_crossings_from_tracker()
    {
        var hub = new ClaudeStateHub(new QuotaThresholdTracker(new[] { 80, 95 }));
        var changes = new List<RealUsage>();
        var crossings = new List<QuotaCrossing>();
        hub.QuotaChanged += changes.Add;
        hub.QuotaThresholdCrossed += crossings.Add;

        hub.PublishQuota(U(70));   // gracia de arranque
        hub.PublishQuota(U(82));
        hub.PublishQuota(U(83));

        Assert.Equal(3, changes.Count);
        var c = Assert.Single(crossings);
        Assert.Equal(QuotaWindowKind.FiveHour, c.Window);
        Assert.Equal(80, c.Threshold);
        Assert.Equal(83, hub.Quota!.FiveHour!.UtilizationPct);
    }

    [Fact]
    public void Default_hub_uses_80_and_95()
    {
        var hub = new ClaudeStateHub();
        var crossings = new List<QuotaCrossing>();
        hub.QuotaThresholdCrossed += crossings.Add;
        hub.PublishQuota(U(90));
        hub.PublishQuota(U(96));
        Assert.Equal(95, Assert.Single(crossings).Threshold);
    }

    [Fact]
    public void Hub_publishes_sessions_and_signals()
    {
        var hub = new ClaudeStateHub();
        var snapshots = new List<IReadOnlyList<SessionState>>();
        var signals = new List<ClaudeSignal>();
        hub.SessionsChanged += snapshots.Add;
        hub.Signal += signals.Add;

        hub.PublishSessions(new[] { S(SessionPhase.Processing, "UserPromptSubmit", T0) }, T0);
        var last = new[] { S(SessionPhase.WaitingForInput, "Stop", T0.AddSeconds(9)) };
        hub.PublishSessions(last, T0.AddSeconds(9));

        Assert.Equal(2, snapshots.Count);
        Assert.Same(last, hub.Sessions);
        Assert.IsType<TaskCompleted>(Assert.Single(signals));
    }

    [Fact]
    public void Initial_state_is_empty()
    {
        var hub = new ClaudeStateHub();
        Assert.Empty(hub.Sessions);
        Assert.Null(hub.Quota);
    }
}
