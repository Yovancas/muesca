using ClaudeBarWin.Models;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="MascotDirector"/> (lenguaje de movimiento §5): identidad única del gato fuera de
/// <c>DashboardForm</c>. Guarda la fase y su reloj, el humor (700 ms / 4 s), la semilla del parpadeo
/// y la hora del último salto; los saltos siguen el calendario de atención. Puro: recibe la hora.
/// </summary>
public class MascotDirectorTests
{
    private static readonly double Total = Hop.DurationMs(Motion.HopPanelV0);

    [Fact]
    public void Phase_change_restarts_the_phase_clock()
    {
        var d = new MascotDirector();
        d.Sync(SessionPhase.Processing, MoodEvent.LongProcessing, 1000, wantsHops: false);
        Assert.Equal(500, d.PhaseElapsedMs(1500));
        d.Sync(SessionPhase.Processing, MoodEvent.LongProcessing, 1200, wantsHops: false);
        Assert.Equal(500, d.PhaseElapsedMs(1500));   // misma fase: no reinicia
        d.Sync(SessionPhase.Idle, MoodEvent.None, 2000, wantsHops: false);
        Assert.Equal(0, d.PhaseElapsedMs(2000));
    }

    [Fact]
    public void Mood_keeps_its_inertia()
    {
        var d = new MascotDirector();
        d.Sync(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 0, wantsHops: false);
        Assert.Equal(Mood.Alert, d.Mood);
        d.Sync(SessionPhase.Idle, MoodEvent.None, 3000, wantsHops: false);
        Assert.Equal(Mood.Alert, d.Mood);            // < 4 s sin eventos
        d.Sync(SessionPhase.Idle, MoodEvent.None, 4200, wantsHops: false);
        Assert.Equal(Mood.Neutral, d.Mood);
    }

    [Fact]
    public void Attention_hops_follow_the_schedule_and_then_stop()
    {
        var d = new MascotDirector();
        d.Sync(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 10_000, wantsHops: true);
        Assert.True(d.HopActive(10_000 + 200));
        Assert.Equal(6, d.HopOffsetPx(10_000 + 419 / 2.0, 1.0));
        Assert.False(d.HopActive(10_000 + 2000));       // entre botes: quieto
        Assert.Equal(0, d.HopOffsetPx(10_000 + 2000, 1.0));
        Assert.True(d.HopActive(10_000 + 3200 + 100));  // primera repetición
        Assert.True(d.HopActive(10_000 + 22_800 + 100));// última
        Assert.False(d.HopActive(10_000 + 40_000));     // después, quieto
        Assert.Equal(10_000 + 22_800, d.LastHopStartMs(10_000 + 40_000));
    }

    [Fact]
    public void Ending_attention_lets_the_current_hop_finish_but_schedules_no_more()
    {
        var d = new MascotDirector();
        d.Sync(SessionPhase.WaitingForInput, MoodEvent.AttentionRequired, 0, wantsHops: true);
        d.Sync(SessionPhase.Processing, MoodEvent.LongProcessing, 100, wantsHops: false);
        Assert.True(d.HopActive(300));                  // el bote en curso acaba solo
        Assert.False(d.HopActive(3300));                // sin repetición
    }

    [Fact]
    public void Hops_restart_when_attention_comes_back()
    {
        var d = new MascotDirector();
        d.Sync(SessionPhase.WaitingForInput, MoodEvent.AttentionRequired, 0, wantsHops: true);
        d.Sync(SessionPhase.Idle, MoodEvent.None, 5000, wantsHops: false);
        d.Sync(SessionPhase.WaitingForInput, MoodEvent.AttentionRequired, 30_000, wantsHops: true);
        Assert.True(d.HopActive(30_100));
    }

    [Fact]
    public void Reduce_motion_cancels_hops()
    {
        var d = new MascotDirector();
        d.Sync(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 0, wantsHops: true);
        d.Sync(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 50, wantsHops: true, reduceMotion: true);
        Assert.False(d.HopActive(100));
        Assert.Equal(0, d.HopOffsetPx(200, 1.0));
    }

    [Fact]
    public void Landing_face_only_right_after_a_touchdown()
    {
        var d = new MascotDirector();
        d.Sync(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 0, wantsHops: true);
        Assert.False(d.Landing(200));
        Assert.True(d.Landing(450));
        Assert.False(d.Landing(Total + 200));
    }

    [Theory]
    [InlineData("O.O", Mood.Alert, "o.o")]
    [InlineData("^.^", Mood.Alert, "o.o")]
    [InlineData("-.-", Mood.Happy, "^.^")]
    [InlineData("^.^", Mood.Happy, "o.o")]
    public void Landing_face_differs_from_the_base_face(string baseFace, Mood mood, string expected)
        => Assert.Equal(expected, MascotDirector.LandingFace(baseFace, mood));
}
