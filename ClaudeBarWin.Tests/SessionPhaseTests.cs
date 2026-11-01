using ClaudeBarWin.Models;

namespace ClaudeBarWin.Tests;

public class SessionPhaseTests
{
    [Fact]
    public void Idle_can_go_to_processing()
        => Assert.True(SessionPhase.Idle.CanTransition(SessionPhase.Processing));

    [Fact]
    public void Ended_is_terminal()
        => Assert.False(SessionPhase.Ended.CanTransition(SessionPhase.Processing));

    [Fact]
    public void Any_phase_can_end()
        => Assert.True(SessionPhase.Processing.CanTransition(SessionPhase.Ended));

    [Fact]
    public void Same_phase_is_a_noop_transition()
        => Assert.True(SessionPhase.Processing.CanTransition(SessionPhase.Processing));

    [Theory]
    [InlineData(SessionPhase.WaitingForApproval, true)]
    [InlineData(SessionPhase.WaitingForInput, true)]
    [InlineData(SessionPhase.Processing, false)]
    [InlineData(SessionPhase.Idle, false)]
    public void NeedsAttention_only_for_waiting(SessionPhase p, bool expected)
        => Assert.Equal(expected, p.NeedsAttention());

    [Theory]
    [InlineData(SessionPhase.Processing, true)]
    [InlineData(SessionPhase.Compacting, true)]
    [InlineData(SessionPhase.Idle, false)]
    public void IsActive_for_processing_and_compacting(SessionPhase p, bool expected)
        => Assert.Equal(expected, p.IsActive());
}
