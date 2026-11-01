using System.Text.Json;
using ClaudeBarWin.Helper;

public sealed class SnapshotWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbh-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch { }
    }

    private static JsonElement RateLimits(double five, long reset = 1759020000) =>
        JsonDocument.Parse("{\"five_hour\":{\"used_percentage\":"
            + five.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"resets_at\":" + reset.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "},\"seven_day\":{\"used_percentage\":41.2,\"resets_at\":1759500000}}").RootElement;

    private JsonElement Read(string id) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, id + ".json"))).RootElement;

    [Theory]
    [InlineData("abc-DEF_123", "abc-DEF_123")]
    [InlineData("a/b\\c:..d", "abcd")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("../../", "unknown")]
    public void SanitizeId_keeps_only_safe_chars(string? raw, string expected) =>
        Assert.Equal(expected, SnapshotWriter.SanitizeId(raw));

    [Fact]
    public void SanitizeId_truncates_to_64() =>
        Assert.Equal(64, SnapshotWriter.SanitizeId(new string('x', 200)).Length);

    [Fact]
    public void First_write_creates_file_with_contract_fields()
    {
        Assert.True(SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(23.5), 1000));
        var json = Read("s1");
        Assert.Equal(1, json.GetProperty("v").GetInt32());
        Assert.Equal("s1", json.GetProperty("session_id").GetString());
        Assert.Equal(1000, json.GetProperty("seen_at").GetInt64());
        Assert.Equal(1000, json.GetProperty("changed_at").GetInt64());
        Assert.Equal(23.5, json.GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Same_data_within_heartbeat_does_not_write()
    {
        SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(23.5), 1000);
        Assert.False(SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(23.5), 1059));
        Assert.Equal(1000, Read("s1").GetProperty("seen_at").GetInt64());
    }

    [Fact]
    public void Same_data_after_heartbeat_updates_seen_but_not_changed()
    {
        SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(23.5), 1000);
        Assert.True(SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(23.5), 1060));
        var json = Read("s1");
        Assert.Equal(1060, json.GetProperty("seen_at").GetInt64());
        Assert.Equal(1000, json.GetProperty("changed_at").GetInt64());
    }

    [Fact]
    public void Changed_data_writes_immediately_and_moves_changed_at()
    {
        SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(23.5), 1000);
        Assert.True(SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(24.0), 1005));
        var json = Read("s1");
        Assert.Equal(1005, json.GetProperty("changed_at").GetInt64());
        Assert.Equal(24.0, json.GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void Older_observation_cannot_overwrite_newer_snapshot()
    {
        SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(30), 1001);

        Assert.False(SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(20), 1000));
        var json = Read("s1");
        Assert.Equal(1001, json.GetProperty("seen_at").GetInt64());
        Assert.Equal(30, json.GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public async Task Concurrent_writers_cannot_regress_snapshot()
    {
        var writes = Enumerable.Range(0, 50).Select(index => Task.Run(() =>
            SnapshotWriter.WriteIfNeeded(_dir, "same", RateLimits(index), 1000 + index)));

        await Task.WhenAll(writes);
        var json = Read("same");
        Assert.Equal(1049, json.GetProperty("seen_at").GetInt64());
        Assert.Equal(49, json.GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void Canonically_equal_data_with_reordered_properties_is_unchanged()
    {
        var first = JsonDocument.Parse("""{"five_hour":{"used_percentage":23.5,"resets_at":10},"future":{"a":1,"b":2}}""").RootElement;
        var reordered = JsonDocument.Parse("""{"future":{"b":2,"a":1},"five_hour":{"resets_at":10,"used_percentage":23.5}}""").RootElement;
        SnapshotWriter.WriteIfNeeded(_dir, "s1", first, 1000);

        Assert.False(SnapshotWriter.WriteIfNeeded(_dir, "s1", reordered, 1001));
        Assert.Equal(1000, Read("s1").GetProperty("changed_at").GetInt64());
    }

    [Fact]
    public void Corrupt_existing_file_is_overwritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "s1.json"), "{ roto");
        Assert.True(SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(23.5), 1000));
        Assert.Equal(1000, Read("s1").GetProperty("changed_at").GetInt64());
    }

    [Fact]
    public void Failed_replace_returns_false_and_removes_own_temporary_file()
    {
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "s1.json"));

        Assert.False(SnapshotWriter.WriteIfNeeded(_dir, "s1", RateLimits(23.5), 1000));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Non_object_rate_limits_is_ignored()
    {
        Assert.False(SnapshotWriter.WriteIfNeeded(_dir, "s1", JsonDocument.Parse("null").RootElement, 1000));
        Assert.False(Directory.Exists(_dir) && File.Exists(Path.Combine(_dir, "s1.json")));
    }

    [Fact]
    public void Unknown_windows_are_copied_verbatim()
    {
        var rateLimits = JsonDocument.Parse("""{"spend_limit":{"used_percentage":120.5,"resets_at":1},"future_window":{"x":[1,2]}}""").RootElement;
        SnapshotWriter.WriteIfNeeded(_dir, "s1", rateLimits, 1000);
        var got = Read("s1").GetProperty("rate_limits");
        Assert.Equal(120.5, got.GetProperty("spend_limit").GetProperty("used_percentage").GetDouble());
        Assert.Equal(2, got.GetProperty("future_window").GetProperty("x").GetArrayLength());
    }

    [Fact]
    public void DataDir_prefers_CLAUDEBAR_DATA_DIR()
    {
        string? Env(string key) => key == "CLAUDEBAR_DATA_DIR" ? @"D:\x" : key == "LOCALAPPDATA" ? @"C:\L" : null;
        Assert.Equal(@"D:\x", HelperPaths.DataDir(Env));
        Assert.Equal(Path.Combine(@"D:\x", "ratelimits"), HelperPaths.RateLimitsDir(Env));

        string? Env2(string key) => key == "LOCALAPPDATA" ? @"C:\L" : null;
        Assert.Equal(Path.Combine(@"C:\L", "ClaudeBarWin"), HelperPaths.DataDir(Env2));
    }
}
