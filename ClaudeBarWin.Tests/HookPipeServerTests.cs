using System.IO.Pipes;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class HookPipeServerTests
{
    [Fact]
    public async Task Server_receives_and_parses_a_client_line()
    {
        // Pipe con nombre único para no chocar con una instancia real corriendo.
        var pipeName = "claudebar-test-" + Guid.NewGuid().ToString("N");
        using var server = new HookPipeServer(pipeName);
        HookEvent? got = null;
        var tcs = new TaskCompletionSource();
        server.EventReceived += e => { got = e; tcs.TrySetResult(); };
        server.Start();

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await client.ConnectAsync(2000);
            using var w = new StreamWriter(client) { AutoFlush = true };
            await w.WriteLineAsync("""{"session_id":"s1","cwd":"c","event":"PreToolUse","status":"running_tool","tool":"Bash"}""");
        }

        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.NotNull(got);
        Assert.Equal("s1", got!.SessionId);
        Assert.Equal("Bash", got.Tool);
    }

    [Fact]
    public void DefaultPipeName_is_stable()
        => Assert.Equal("claudebar", HookPipeServer.DefaultPipeName);
}
