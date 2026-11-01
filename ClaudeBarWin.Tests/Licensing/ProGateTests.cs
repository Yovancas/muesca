using ClaudeBarWin.Services.Licensing;

namespace ClaudeBarWin.Tests.Licensing;

/// <summary>El reparto gratis / Pro en un único sitio (<see cref="ProGate"/>): todas las combinaciones.</summary>
public class ProGateTests
{
    public static TheoryData<ProFeature, ProState> AllCombinations()
    {
        var data = new TheoryData<ProFeature, ProState>();
        foreach (var f in Enum.GetValues<ProFeature>())
            foreach (var s in Enum.GetValues<ProState>()) data.Add(f, s);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Every_paid_feature_is_unlocked_with_trial_or_pro_and_locked_when_free(ProFeature feature, ProState state)
    {
        Assert.Equal(state != ProState.Free, ProGate.IsUnlocked(feature, state));
    }

    [Fact]
    public void The_list_has_exactly_the_paid_features_of_the_design()
    {
        Assert.Equal(new[] { ProFeature.Permissions, ProFeature.Music, ProFeature.Tasks, ProFeature.Shelf, ProFeature.MultiMonitor,
            ProFeature.NotchUsage, ProFeature.NotchFocus }, ProGate.All);
    }

    [Fact]
    public void Unknown_values_count_as_locked()
    {
        Assert.False(ProGate.IsUnlocked((ProFeature)99, ProState.Pro));
        Assert.False(ProGate.IsUnlocked((ProFeature)(-1), ProState.Trial));
        foreach (var f in ProGate.All)
        {
            Assert.False(ProGate.IsUnlocked(f, (ProState)42));
            Assert.False(ProGate.IsUnlocked(f, (ProState)(-1)));
        }
    }

    [Fact]
    public void Something_is_locked_only_when_free()
    {
        Assert.True(ProGate.AnyLocked(ProState.Free));
        Assert.False(ProGate.AnyLocked(ProState.Trial));
        Assert.False(ProGate.AnyLocked(ProState.Pro));
        Assert.True(ProGate.AnyLocked((ProState)7));
    }
}
