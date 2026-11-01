using ClaudeBarWin.Services;

public class RateLimitSnapshotStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbs-" + Guid.NewGuid().ToString("N"));
    public RateLimitSnapshotStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string Good = """{"v":1,"session_id":"s1","seen_at":1759060800,"changed_at":1759060700,"rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1759070000},"seven_day":null}}""";

    [Fact]
    public void Parse_reads_contract()
    {
        var s = RateLimitSnapshotStore.Parse(Good)!;
        Assert.Equal("s1", s.SessionId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1759060800).UtcDateTime, s.SeenUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1759060700).UtcDateTime, s.ChangedUtc);
        Assert.Equal(23.5, s.Windows["five_hour"].Pct);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1759070000), s.Windows["five_hour"].ResetsAt);
        Assert.True(s.Windows["five_hour"].FromStatusline);
        Assert.False(s.Windows.ContainsKey("seven_day"));
    }

    [Theory]
    [InlineData("{roto")]
    [InlineData("""{"v":2,"seen_at":1,"changed_at":1,"rate_limits":{}}""")]
    [InlineData("""{"v":1,"rate_limits":{}}""")]
    [InlineData("""{"v":1,"seen_at":1,"changed_at":1,"rate_limits":[]}""")]
    public void Parse_rejects_bad_files(string json) => Assert.Null(RateLimitSnapshotStore.Parse(json));

    [Fact]
    public void Parse_ignores_windows_without_percentage()
    {
        var s = RateLimitSnapshotStore.Parse("""{"v":1,"seen_at":1,"changed_at":1,"rate_limits":{"five_hour":{"resets_at":5},"spend_limit":{"used_percentage":120.5}}}""")!;
        Assert.False(s.Windows.ContainsKey("five_hour"));
        Assert.Equal(120.5, s.Windows["spend_limit"].Pct);
        Assert.Null(s.Windows["spend_limit"].ResetsAt);
    }

    [Fact]
    public void ReadAll_skips_bad_files_and_prunes_old_ones()
    {
        File.WriteAllText(Path.Combine(_dir, "s1.json"), Good);
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{");
        var old = Path.Combine(_dir, "old.json");
        File.WriteAllText(old, Good.Replace("\"s1\"", "\"old\"").Replace("1759060800", "1758000000"));
        var now = DateTimeOffset.FromUnixTimeSeconds(1759060800 + 60).UtcDateTime;
        var all = new RateLimitSnapshotStore(_dir).ReadAll(now);
        Assert.Single(all);
        Assert.Equal("s1", all[0].SessionId);
        Assert.False(File.Exists(old)); // > 8 días
    }

    [Fact]
    public void ReadAll_on_missing_dir_is_empty() =>
        Assert.Empty(new RateLimitSnapshotStore(Path.Combine(_dir, "nope")).ReadAll(DateTime.UtcNow));

    [Fact]
    public void Clear_removes_snapshots()
    {
        File.WriteAllText(Path.Combine(_dir, "s1.json"), Good);
        new RateLimitSnapshotStore(_dir).Clear();
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
