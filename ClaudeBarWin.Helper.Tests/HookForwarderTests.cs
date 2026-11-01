using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ClaudeBarWin.Helper;

public sealed class HookForwarderTests
{
    [Theory]
    [InlineData("PreToolUse", "", "running_tool")]
    [InlineData("PostToolUse", "", "processing")]
    [InlineData("PermissionRequest", "", "waiting_for_approval")]
    [InlineData("Notification", "permission_prompt", "waiting_for_approval")]
    [InlineData("Notification", "PERMISSION_PROMPT", "waiting_for_approval")]
    [InlineData("Notification", "idle_prompt", "waiting_for_input")]
    [InlineData("Notification", "agent_needs_input", "waiting_for_input")]
    [InlineData("Notification", "elicitation_dialog", "waiting_for_input")]
    [InlineData("Notification", "", "waiting_for_input")]
    [InlineData("Notification", "auth_success", "noop")]
    [InlineData("Stop", "", "waiting_for_input")]
    [InlineData("stop", "", "waiting_for_input")]
    [InlineData("SubagentStop", "", "processing")]
    [InlineData("UserPromptSubmit", "", "processing")]
    [InlineData("PreCompact", "", "compacting")]
    [InlineData("SessionStart", "", "starting")]
    [InlineData("SessionEnd", "", "ended")]
    [InlineData("Whatever", "", "noop")]
    [InlineData("", "", "noop")]
    public void MapStatus_matches_ps1(string eventName, string notificationType, string expected) =>
        Assert.Equal(expected, HookForwarder.MapStatus(eventName, notificationType));

    [Fact]
    public void Payload_has_ps1_fields_and_order()
    {
        var input = Encoding.UTF8.GetBytes("""{"session_id":"s","cwd":"C:\\español","hook_event_name":"PostToolUse","tool_name":"Bash","tool_use_id":"t1"}""");
        var json = HookForwarder.BuildPayload(input, pid: 42, ts: 1759000000)!;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(
            // Los campos del .ps1 (v1) van primero y en su orden; los v2 del notch, detrás.
            ["session_id", "cwd", "pid", "event", "status", "tool", "tool_use_id", "notification_type", "ts",
             "v", "message", "title", "tool_input_preview"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("C:\\español", root.GetProperty("cwd").GetString());
        Assert.Equal(42, root.GetProperty("pid").GetInt32());
        Assert.Equal("processing", root.GetProperty("status").GetString());
        Assert.Equal(string.Empty, root.GetProperty("notification_type").GetString());
        Assert.Equal(1759000000, root.GetProperty("ts").GetInt64());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{roto")]
    [InlineData("[]")]
    [InlineData("123")]
    public void Payload_is_null_for_empty_invalid_or_non_object_json(string raw) =>
        Assert.Null(HookForwarder.BuildPayload(Encoding.UTF8.GetBytes(raw), 1, 1));

    [Fact]
    public void Payload_is_null_for_invalid_utf8()
    {
        byte[] input = [0x7B, 0x22, 0x78, 0x22, 0x3A, 0x22, 0xFF, 0x22, 0x7D];
        Assert.Null(HookForwarder.BuildPayload(input, 1, 1));
    }

    [Fact]
    public void Payload_with_non_string_hook_fields_remains_valid_and_safe()
    {
        var input = Encoding.UTF8.GetBytes("""{"session_id":123,"cwd":{"x":1},"hook_event_name":false,"tool_name":[1,2],"tool_use_id":null,"notification_type":42}""");
        var json = HookForwarder.BuildPayload(input, 7, 9);
        Assert.NotNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("123", root.GetProperty("session_id").GetString());
        Assert.Equal("False", root.GetProperty("event").GetString());
        Assert.Equal("@{x=1}", root.GetProperty("cwd").GetString());
        Assert.Equal("1 2", root.GetProperty("tool").GetString());
        Assert.Equal("noop", root.GetProperty("status").GetString());
    }

    [Fact]
    public void Numeric_notification_type_matches_ps1_status_semantics()
    {
        var input = Encoding.UTF8.GetBytes("""{"hook_event_name":"Notification","notification_type":42}""");
        var json = HookForwarder.BuildPayload(input, 7, 9)!;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("42", root.GetProperty("notification_type").GetString());
        Assert.Equal("noop", root.GetProperty("status").GetString());
    }

    [Fact]
    public void Run_without_server_returns_quickly_and_silently()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        HookForwarder.Run(
            Encoding.UTF8.GetBytes("""{"hook_event_name":"Stop"}"""),
            "cbh-none-" + Guid.NewGuid().ToString("N"),
            100);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000);
    }

    [Fact]
    public async Task Run_sends_one_utf8_line_without_bom_to_the_pipe()
    {
        var name = "cbh-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(
            name,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync();
        var send = Task.Run(() => HookForwarder.Run(
            Encoding.UTF8.GetBytes("""{"session_id":"s","cwd":"C:\\ñ","hook_event_name":"Stop"}"""),
            name,
            2000));

        await accept.WaitAsync(TimeSpan.FromSeconds(5));
        using var bytes = new MemoryStream();
        var buffer = new byte[256];
        while (true)
        {
            var count = await server.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            if (count == 0)
                break;
            bytes.Write(buffer, 0, count);
            if (buffer.AsSpan(0, count).Contains((byte)'\n'))
                break;
        }
        await send.WaitAsync(TimeSpan.FromSeconds(5));

        var raw = bytes.ToArray();
        Assert.False(raw.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal((byte)'\n', raw[^1]);
        using var document = JsonDocument.Parse(raw.AsMemory(0, raw.Length - 1));
        Assert.Equal("waiting_for_input", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("C:\\ñ", document.RootElement.GetProperty("cwd").GetString());
    }
}
