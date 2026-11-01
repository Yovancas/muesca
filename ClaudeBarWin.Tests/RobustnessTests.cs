using System.IO.Pipes;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>v0.3.10: config fuera de rango/corrupta, escritura atómica, redacción del log y pipe robusto.</summary>
public sealed class RobustnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "claudebar-tests", "rob-" + Guid.NewGuid().ToString("N"));

    public RobustnessTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Theory]
    [InlineData(2147483647)]
    [InlineData(2147484)]  // el primer valor cuyo *1000 desbordaba a negativo
    [InlineData(-5)]
    [InlineData(0)]
    public void Refresh_interval_never_overflows_and_stays_in_range(int seconds)
    {
        var c = new AppConfig { RefreshSeconds = seconds };
        Assert.InRange(c.RefreshIntervalMs, AppConfig.MinRefreshSeconds * 1000, AppConfig.MaxRefreshSeconds * 1000);
        c.Normalize();
        Assert.InRange(c.RefreshSeconds, AppConfig.MinRefreshSeconds, AppConfig.MaxRefreshSeconds);
    }

    [Fact]
    public void Normalize_fixes_thresholds_opacity_window_and_milestones()
    {
        var c = new AppConfig
        {
            WarnThresholdPct = 95, CriticalThresholdPct = 60, DashboardOpacity = double.NaN,
            SpendWindowDays = 100_000, NotifyMilestones = new[] { 0, 50, 50, 150, 25 },
        }.Normalize();
        Assert.Equal(60, c.WarnThresholdPct);
        Assert.Equal(95, c.CriticalThresholdPct);
        Assert.Equal(1.0, c.DashboardOpacity);
        Assert.Equal(AppConfig.MaxSpendWindowDays, c.SpendWindowDays);
        Assert.Equal(new[] { 25, 50 }, c.NotifyMilestones);
    }

    [Fact]
    public void Corrupt_config_is_set_aside_not_overwritten()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{ \"RefreshSeconds\": 60, esto no es json");

        var c = AppConfig.Load(path);

        Assert.Equal(60, c.RefreshSeconds);                   // defaults
        Assert.True(File.Exists(path + ".bad"));               // el original se conserva
        Assert.Contains("esto no es json", File.ReadAllText(path + ".bad"));
    }

    [Fact]
    public void Out_of_range_config_on_disk_loads_normalized()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{"RefreshSeconds": 2147483647, "SpendWindowDays": -3}""");
        var c = AppConfig.Load(path);
        Assert.Equal(AppConfig.MaxRefreshSeconds, c.RefreshSeconds);
        Assert.Equal(AppConfig.MinSpendWindowDays, c.SpendWindowDays);
    }

    [Fact]
    public void AtomicFile_replaces_content_and_leaves_no_temp_files()
    {
        var path = Path.Combine(_dir, "a.json");
        AtomicFile.WriteAllText(path, "uno");
        AtomicFile.WriteAllText(path, "dos ñ");
        Assert.Equal("dos ñ", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("Authorization: Bearer abc.def-123", "Authorization: ***")]
    [InlineData("token sk-ant-oat01-XYZ_123 leaked", "token *** leaked")]
    [InlineData("{\"accessToken\": \"secreto\"}", "{\"accessToken\": \"***\"}")]
    public void Log_redacts_secrets(string input, string expected)
        => Assert.Equal(expected, AppLog.Redact(input));

    [Fact]
    public async Task Pipe_client_that_never_finishes_does_not_block_other_hooks()
    {
        var name = "claudebar-test-" + Guid.NewGuid().ToString("N");
        using var server = new HookPipeServer(name);
        var got = new TaskCompletionSource<HookEvent>();
        server.EventReceived += e => got.TrySetResult(e);
        server.Start();

        // Cliente "malo": conecta y se queda callado, sin salto de línea ni cierre.
        using var stuck = new NamedPipeClientStream(".", name, PipeDirection.Out);
        await stuck.ConnectAsync(2000);
        await stuck.WriteAsync("{\"session_id\":\"sin-fin"u8.ToArray());

        // Un hook normal tiene que entrar igualmente (antes el servidor era secuencial).
        using (var ok = new NamedPipeClientStream(".", name, PipeDirection.Out))
        {
            await ok.ConnectAsync(1000);
            using var w = new StreamWriter(ok) { AutoFlush = true };
            await w.WriteLineAsync("""{"session_id":"s2","cwd":"c","event":"Stop","status":"waiting_for_input"}""");
        }

        var done = await Task.WhenAny(got.Task, Task.Delay(3000));
        Assert.Same(got.Task, done);
        Assert.Equal("s2", (await got.Task).SessionId);
    }

    [Fact]
    public async Task Oversized_pipe_message_is_dropped()
    {
        using var ms = new MemoryStream(new byte[HookPipeServer.MaxBytes + 10]);
        await Assert.ThrowsAsync<InvalidDataException>(() => HookPipeServer.ReadLinesAsync(ms, CancellationToken.None, _ => { }));
    }

    [Fact]
    public async Task Pipe_lines_are_delivered_as_they_arrive_and_last_line_without_newline_too()
    {
        var lines = new List<string>();
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("a\r\nb\n\nc"));
        await HookPipeServer.ReadLinesAsync(ms, CancellationToken.None, lines.Add);
        Assert.Equal(new[] { "a", "b", "c" }, lines);
    }

    [Fact]
    public void Stale_pipe_event_is_ignored_by_sequence()
    {
        var s = new SessionStore();
        var t = DateTime.UtcNow;
        s.Apply(new HookEvent { SessionId = "s", Cwd = "c", Event = "UserPromptSubmit", Status = "processing", Seq = 5000 }, t);
        s.Apply(new HookEvent { SessionId = "s", Cwd = "c", Event = "PermissionRequest", Status = "waiting_for_approval", Seq = 4000 }, t);
        Assert.Equal(SessionPhase.Processing, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Noop_notifications_do_not_change_the_phase()
    {
        var s = new SessionStore();
        var t = DateTime.UtcNow;
        s.Apply(new HookEvent { SessionId = "s", Cwd = "c", Event = "UserPromptSubmit", Status = "processing" }, t);
        s.Apply(new HookEvent { SessionId = "s", Cwd = "c", Event = "Notification", Status = "noop", NotificationType = "auth_success" }, t);
        Assert.Equal(SessionPhase.Processing, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Smart_prune_keeps_long_waiting_sessions_and_drops_dead_processes()
    {
        var s = new SessionStore();
        var t0 = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
        var claude = new ProcessInfo(4242, "claude.exe", t0);
        foreach (var id in new[] { "waiting", "dead" })
            s.Apply(new HookEvent { SessionId = id, Cwd = "c", Event = "UserPromptSubmit", Status = "processing" }, t0);
        s.Apply(new HookEvent { SessionId = "waiting", Cwd = "c", Event = "Stop", Status = "waiting_for_input" }, t0);
        s.Apply(new HookEvent { SessionId = "dead", Cwd = "c", Event = "Stop", Status = "waiting_for_input",
            Ancestors = new[] { new ProcessInfo(1, "bash.exe", t0), claude } }, t0);

        // 2 h después: la que espera respuesta sigue (antes se borraba a los 10 min); la de proceso muerto no.
        s.Prune(t0.AddHours(2), isAlive: p => p.Pid != 4242);
        Assert.Equal("waiting", Assert.Single(s.Snapshot()).SessionId);
        Assert.Equal(TimeSpan.FromHours(12), SessionStore.TtlFor(SessionPhase.WaitingForInput));
    }
}
