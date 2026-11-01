using ClaudeBarWin.Services.Licensing;

namespace ClaudeBarWin.Tests.Licensing;

/// <summary>La prueba de 7 días: días que quedan, fin y el próximo cambio (un solo temporizador).</summary>
public class ProTrialTests
{
    private static readonly DateTime Start = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Days_left_count_down_rounding_up_and_end_after_seven_days()
    {
        Assert.Null(ProTrial.DaysLeft(null, Start));
        Assert.Equal(7, ProTrial.DaysLeft(Start, Start));
        Assert.Equal(7, ProTrial.DaysLeft(Start, Start.AddHours(23)));
        Assert.Equal(6, ProTrial.DaysLeft(Start, Start.AddDays(1)));
        Assert.Equal(1, ProTrial.DaysLeft(Start, Start.AddDays(6.5)));
        Assert.Equal(1, ProTrial.DaysLeft(Start, Start.AddDays(7).AddSeconds(-1)));
        Assert.Null(ProTrial.DaysLeft(Start, Start.AddDays(7)));
        Assert.Null(ProTrial.DaysLeft(Start, Start.AddDays(30)));
        // Reloj atrasado (el inicio queda en el futuro): como mucho 7, nunca más.
        Assert.Equal(7, ProTrial.DaysLeft(Start, Start.AddDays(-3)));
    }

    [Fact]
    public void Ended_only_after_it_started()
    {
        Assert.False(ProTrial.Ended(null, Start));
        Assert.False(ProTrial.Ended(Start, Start.AddDays(3)));
        Assert.True(ProTrial.Ended(Start, Start.AddDays(7)));
    }

    [Fact]
    public void The_next_change_is_the_next_day_boundary_or_the_end()
    {
        Assert.Null(ProTrial.NextChangeIn(null, Start));
        Assert.Equal(TimeSpan.FromDays(1), ProTrial.NextChangeIn(Start, Start));
        Assert.Equal(TimeSpan.FromHours(4), ProTrial.NextChangeIn(Start, Start.AddHours(20)));
        Assert.Equal(TimeSpan.FromHours(12), ProTrial.NextChangeIn(Start, Start.AddDays(6.5)));
        Assert.Null(ProTrial.NextChangeIn(Start, Start.AddDays(7)));
        // En cada cambio, los días que quedan bajan uno (o se acaba).
        var now = Start.AddMinutes(7);
        for (int expected = 7; expected >= 1; expected--)
        {
            Assert.Equal(expected, ProTrial.DaysLeft(Start, now));
            now += ProTrial.NextChangeIn(Start, now)!.Value;
        }
        Assert.Null(ProTrial.DaysLeft(Start, now));
    }

    [Fact]
    public void Owning_pro_wins_then_the_trial_then_free()
    {
        Assert.Equal(ProState.Pro, ProTrial.Resolve(true, null, Start));
        Assert.Equal(ProState.Pro, ProTrial.Resolve(true, Start, Start.AddDays(40)));
        Assert.Equal(ProState.Trial, ProTrial.Resolve(false, Start, Start.AddDays(2)));
        Assert.Equal(ProState.Free, ProTrial.Resolve(false, Start, Start.AddDays(8)));
        Assert.Equal(ProState.Free, ProTrial.Resolve(false, null, Start));
    }
}
