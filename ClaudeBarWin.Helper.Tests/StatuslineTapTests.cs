using System.Text;
using System.Text.Json;
using ClaudeBarWin.Helper;

public sealed class StatuslineTapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbh-" + Guid.NewGuid().ToString("N"));

    private const string Sample = """
        {"session_id":"abc","cwd":"C:\\Proyectos\\español 🚀","model":{"display_name":"Opus <5>"},
         "rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1759020000},
                        "seven_day":{"used_percentage":41.2,"resets_at":1759500000}}}
        """;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch { }
    }

    private (string Text, byte[] Bytes) Run(string input, bool tee)
    {
        using var output = new MemoryStream();
        StatuslineTap.Run(Encoding.UTF8.GetBytes(input), output, tee, _dir, 1000);
        return (Encoding.UTF8.GetString(output.ToArray()), output.ToArray());
    }

    [Fact]
    public void Tee_output_is_pure_ascii_and_semantically_identical()
    {
        var (output, bytes) = Run(Sample, tee: true);
        Assert.All(bytes, value => Assert.True(value < 0x80));
        using var expected = JsonDocument.Parse(Sample);
        using var actual = JsonDocument.Parse(output);
        Assert.Equal("C:\\Proyectos\\español 🚀", actual.RootElement.GetProperty("cwd").GetString());
        Assert.Equal("Opus <5>", actual.RootElement.GetProperty("model").GetProperty("display_name").GetString());
        Assert.Equal(
            expected.RootElement.GetProperty("rate_limits").GetRawText().Replace(" ", "").Replace("\n", "").Replace("\r", ""),
            actual.RootElement.GetProperty("rate_limits").GetRawText());
    }

    [Fact]
    public void Tee_escapes_every_shell_sensitive_character_named_by_the_contract()
    {
        var (output, bytes) = Run("""{"value":"<>&'+"}""", tee: true);
        Assert.All(bytes, value => Assert.True(value < 0x80));
        Assert.DoesNotContain("<", output);
        Assert.DoesNotContain(">", output);
        Assert.DoesNotContain("&", output);
        Assert.DoesNotContain("'", output);
        Assert.DoesNotContain("+", output);
        Assert.Contains(@"\u002B", output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<>&'+", JsonDocument.Parse(output).RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public void Tee_writes_snapshot()
    {
        Run(Sample, tee: true);
        Assert.True(File.Exists(Path.Combine(_dir, "abc.json")));
    }

    [Fact]
    public void Tee_with_invalid_json_passes_bytes_through_unchanged()
    {
        var input = "no es json ñ {";
        var (_, bytes) = Run(input, tee: true);
        Assert.Equal(Encoding.UTF8.GetBytes(input), bytes);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Tee_with_invalid_utf8_passes_bytes_through_unchanged()
    {
        byte[] input = [0x7B, 0x22, 0x78, 0x22, 0x3A, 0x22, 0xFF, 0x22, 0x7D];
        using var output = new MemoryStream();
        StatuslineTap.Run(input, output, tee: true, _dir, 1000);
        Assert.Equal(input, output.ToArray());
    }

    [Theory]
    [InlineData("{\"x\":\"\\uD800\"}")]
    [InlineData("{\"session_id\":\"\\uD800\",\"rate_limits\":{}}")]
    public void Tee_with_json_that_cannot_be_materialized_passes_bytes_through_unchanged(string input)
    {
        var (_, bytes) = Run(input, tee: true);
        Assert.Equal(Encoding.UTF8.GetBytes(input), bytes);
    }

    [Fact]
    public void Tee_without_rate_limits_forwards_json_and_writes_nothing()
    {
        var (output, _) = Run("""{"session_id":"x","cwd":"C:/a"}""", tee: true);
        Assert.Equal("C:/a", JsonDocument.Parse(output).RootElement.GetProperty("cwd").GetString());
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Tee_with_null_rate_limits_forwards_json_and_writes_nothing()
    {
        var (output, _) = Run("""{"session_id":"x","rate_limits":null}""", tee: true);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(output).RootElement.GetProperty("rate_limits").ValueKind);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Tee_skips_utf8_bom_for_parseable_json()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        using var output = new MemoryStream();
        StatuslineTap.Run([.. bom, .. Encoding.UTF8.GetBytes(Sample)], output, tee: true, _dir, 1000);
        Assert.Equal((byte)'{', output.ToArray()[0]);
    }

    [Fact]
    public void Tee_accepts_input_larger_than_64_kib()
    {
        var padding = new string('x', 70_000);
        var (output, _) = Run("{\"session_id\":\"large\",\"padding\":\"" + padding + "\",\"rate_limits\":{}}", tee: true);
        Assert.Equal(70_000, JsonDocument.Parse(output).RootElement.GetProperty("padding").GetString()!.Length);
        Assert.True(File.Exists(Path.Combine(_dir, "large.json")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("true")]
    public void Tee_accepts_non_object_json_roots_without_snapshot(string input)
    {
        var (output, _) = Run(input, tee: true);
        Assert.Equal(JsonDocument.Parse(input).RootElement.ValueKind, JsonDocument.Parse(output).RootElement.ValueKind);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Empty_stdin_produces_no_output_in_either_mode()
    {
        Assert.Empty(Run(string.Empty, tee: true).Bytes);
        Assert.Empty(Run(string.Empty, tee: false).Bytes);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Standalone_prints_compact_line_with_contract_separator()
    {
        var (output, _) = Run(Sample, tee: false);
        Assert.Equal("5h 24% · 7d 41%", output.TrimEnd('\r', '\n'));
    }

    [Theory]
    [InlineData("""{"five_hour":{"used_percentage":7}}""", "5h 7%")]
    [InlineData("""{"seven_day":{"used_percentage":99.5}}""", "7d 100%")]
    [InlineData("""{"five_hour":null,"seven_day":{"used_percentage":5}}""", "7d 5%")]
    [InlineData("{}", "")]
    public void CompactLine_shows_only_valid_windows(string rateLimits, string expected) =>
        Assert.Equal(expected, StatuslineTap.CompactLine(JsonDocument.Parse(rateLimits).RootElement));

    [Theory]
    [InlineData("{{{")]
    [InlineData("[]")]
    [InlineData("123")]
    public void Standalone_with_unusable_input_prints_nothing(string input)
    {
        var (output, _) = Run(input, tee: false);
        Assert.Equal(string.Empty, output);
    }
}
