using ClaudeBarWin.Services;

public class QuotaIntegrationTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact] public void Unavailable_without_helper() => Assert.Equal(TapStatus.Unavailable, QuotaIntegration.Status(false, true, Now, Now));
    [Fact] public void Off_when_not_installed() => Assert.Equal(TapStatus.Off, QuotaIntegration.Status(true, false, Now, Now));
    [Fact] public void Waiting_without_fresh_data() => Assert.Equal(TapStatus.Waiting, QuotaIntegration.Status(true, true, Now.AddMinutes(-11), Now));
    [Fact] public void Waiting_without_any_data() => Assert.Equal(TapStatus.Waiting, QuotaIntegration.Status(true, true, null, Now));
    [Fact] public void Active_with_fresh_data() => Assert.Equal(TapStatus.Active, QuotaIntegration.Status(true, true, Now.AddSeconds(-12), Now));
}
