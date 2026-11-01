using System.Diagnostics;
using System.Text;
using System.Text.Json;

public sealed class HelperProcessTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "cbh-process-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, true); }
        catch { }
    }

    [Theory]
    [InlineData("statusline --tee", "")]
    [InlineData("statusline --tee", "{broken")]
    [InlineData("statusline", "123")]
    [InlineData("hook", "")]
    [InlineData("hook", "{broken")]
    [InlineData("hook", "{\"hook_event_name\":123,\"cwd\":{}}")]
    public async Task Statusline_and_hook_always_exit_zero_without_stderr(string arguments, string input)
    {
        var result = await RunHelper(arguments, input);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
    }

    // Sin ClaudeBar escuchando (o con entrada inválida) no hay decisión: exit 0, stdout y stderr vacíos, y rápido.
    [Theory]
    [InlineData("")]
    [InlineData("{broken")]
    [InlineData("{\"session_id\":\"s\",\"hook_event_name\":\"PermissionRequest\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"echo hola\"}}")]
    public async Task Permission_without_decision_exits_zero_with_empty_output(string input)
    {
        var sw = Stopwatch.StartNew();
        var result = await RunHelper("permission", input);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal(string.Empty, result.Error);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Version_reports_assembly_version_without_stderr()
    {
        var result = await RunHelper("--version", string.Empty);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0.4.0", result.Output.Trim());
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public async Task Two_helpers_can_write_the_same_session_concurrently()
    {
        const string input = """{"session_id":"same","rate_limits":{"five_hour":{"used_percentage":25,"resets_at":1759020000}}}""";
        var first = RunHelper("statusline --tee", input);
        var second = RunHelper("statusline --tee", input);
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result =>
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
        });
        using var snapshot = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_dataDir, "ratelimits", "same.json")));
        Assert.Equal(25, snapshot.RootElement.GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetInt32());
    }

    private async Task<ProcessResult> RunHelper(string arguments, string input)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "claudebar-helper.exe");
        var start = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.Environment["CLAUDEBAR_DATA_DIR"] = _dataDir;

        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
