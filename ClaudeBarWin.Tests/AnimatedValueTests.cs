using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

public class AnimatedValueTests
{
    private const double Dur = 200.0; // ms

    [Fact]
    public void New_value_starts_at_initial_and_is_not_animating()
    {
        var v = new AnimatedValue(0);
        Assert.Equal(0.0, v.Value, 9);
        Assert.False(v.IsAnimating);
    }

    [Fact]
    public void Set_then_advance_zero_stays_at_start()
    {
        var v = new AnimatedValue(0);
        v.Set(100, Dur, Easing.OutCubic);
        v.Advance(0);
        Assert.Equal(0.0, v.Value, 9);
    }

    [Fact]
    public void Halfway_is_strictly_between_start_and_target()
    {
        var v = new AnimatedValue(0);
        v.Set(100, Dur, Easing.OutCubic);
        v.Advance(Dur / 2);
        Assert.True(v.Value > 0.0 && v.Value < 100.0, $"got {v.Value}");
    }

    [Fact]
    public void At_full_duration_reaches_target()
    {
        var v = new AnimatedValue(0);
        v.Set(100, Dur, Easing.OutCubic);
        v.Advance(Dur);
        Assert.Equal(100.0, v.Value, 6);
    }

    [Fact]
    public void Past_duration_clamps_at_target()
    {
        var v = new AnimatedValue(0);
        v.Set(100, Dur, Easing.OutCubic);
        v.Advance(Dur * 2);
        Assert.Equal(100.0, v.Value, 6);
        Assert.False(v.IsAnimating);
    }

    [Fact]
    public void IsAnimating_true_until_target_reached()
    {
        var v = new AnimatedValue(0);
        v.Set(100, Dur, Easing.OutCubic);
        Assert.True(v.IsAnimating);
        v.Advance(Dur / 2);
        Assert.True(v.IsAnimating);
        v.Advance(Dur);
        Assert.False(v.IsAnimating);
    }

    [Fact]
    public void Set_midway_rearms_from_current_value_without_jump()
    {
        var v = new AnimatedValue(0);
        v.Set(100, Dur, Easing.OutCubic);
        v.Advance(Dur / 2);
        double mid = v.Value;
        Assert.True(mid > 0 && mid < 100);

        // Re-target while in flight: the value must NOT snap; it stays at `mid`
        // immediately after Set, then eases toward the new target from there.
        v.Set(0, Dur, Easing.OutCubic);
        Assert.Equal(mid, v.Value, 9);

        v.Advance(Dur / 2);
        Assert.True(v.Value < mid, $"should be heading back down from {mid}, got {v.Value}");
        v.Advance(Dur);
        Assert.Equal(0.0, v.Value, 6);
    }

    [Fact]
    public void Snap_jumps_to_target_immediately()
    {
        var v = new AnimatedValue(0);
        v.Set(100, Dur, Easing.OutCubic);
        v.Snap();
        Assert.Equal(100.0, v.Value, 9);
        Assert.False(v.IsAnimating);
    }

    [Fact]
    public void Zero_or_negative_duration_is_instant()
    {
        var v = new AnimatedValue(0);
        v.Set(100, 0, Easing.OutCubic);
        Assert.Equal(100.0, v.Value, 9);
        Assert.False(v.IsAnimating);

        var v2 = new AnimatedValue(0);
        v2.Set(50, -10, Easing.OutCubic);
        Assert.Equal(50.0, v2.Value, 9);
        Assert.False(v2.IsAnimating);
    }

    [Fact]
    public void Setting_same_target_does_not_restart_animation()
    {
        var v = new AnimatedValue(42);
        v.Set(42, Dur, Easing.OutCubic);
        Assert.False(v.IsAnimating);
        Assert.Equal(42.0, v.Value, 9);
    }

    [Fact]
    public void Advance_accumulates_across_calls()
    {
        var v = new AnimatedValue(0);
        v.Set(100, Dur, Easing.OutCubic);
        v.Advance(Dur / 4);
        v.Advance(Dur / 4);
        double half = v.Value;

        var w = new AnimatedValue(0);
        w.Set(100, Dur, Easing.OutCubic);
        w.Advance(Dur / 2);

        Assert.Equal(w.Value, half, 9);
    }
}
