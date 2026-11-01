using System.Text;
using System.Text.Json;
using ClaudeBarWin.Helper;

/// <summary>Campos v2 del payload (notch): versión, mensaje, título y vista previa de la herramienta.</summary>
public sealed class HookPayloadV2Tests
{
    private static JsonElement Payload(string input)
    {
        var json = HookForwarder.BuildPayload(Encoding.UTF8.GetBytes(input), 1, 0)!;
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void V2_includes_preview_of_bash_command()
    {
        var p = Payload("""{"session_id":"s","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"npm test\n--watch"}}""");
        Assert.Equal(2, p.GetProperty("v").GetInt32());
        Assert.Equal("npm test --watch", p.GetProperty("tool_input_preview").GetString());
    }

    // CRLF cuenta como UN salto (un solo espacio), no dos.
    [Fact]
    public void Crlf_becomes_a_single_space()
    {
        var p = Payload("""{"hook_event_name":"PreToolUse","tool_input":{"command":"a\r\nb"}}""");
        Assert.Equal("a b", p.GetProperty("tool_input_preview").GetString());
    }

    [Fact]
    public void Preview_is_truncated_to_160()
    {
        var command = new string('x', 500);
        var p = Payload($$$"""{"hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"{{{command}}}"}}""");
        var preview = p.GetProperty("tool_input_preview").GetString()!;
        Assert.Equal(160, preview.Length);
        Assert.EndsWith("…", preview);
    }

    // Exactamente 160 caracteres cabe entero: sin «…».
    [Fact]
    public void Preview_of_exactly_160_is_not_truncated()
    {
        var command = new string('y', 160);
        var p = Payload($$$"""{"hook_event_name":"PreToolUse","tool_input":{"command":"{{{command}}}"}}""");
        Assert.Equal(command, p.GetProperty("tool_input_preview").GetString());
    }

    // El recorte no deja medio par sustituto (emoji) colgando antes de la «…».
    [Fact]
    public void Truncation_does_not_split_surrogate_pairs()
    {
        var command = new string('z', 158) + "😀😀😀";
        var p = Payload($$$"""{"hook_event_name":"PreToolUse","tool_input":{"command":"{{{command}}}"}}""");
        var preview = p.GetProperty("tool_input_preview").GetString()!;
        Assert.True(preview.Length <= 160);
        Assert.EndsWith("…", preview);
        Assert.False(char.IsHighSurrogate(preview[^2]));
    }

    [Theory]
    [InlineData("""{"file_path":"C:\\a.cs","url":"u","pattern":"p"}""", "C:\\a.cs")]
    [InlineData("""{"url":"https://x.dev","pattern":"p"}""", "https://x.dev")]
    [InlineData("""{"pattern":"TODO.*"}""", "TODO.*")]
    [InlineData("""{"command":"git status","file_path":"f"}""", "git status")]
    [InlineData("""{"command":"","file_path":"f"}""", "f")]
    [InlineData("""{"command":42,"file_path":"f"}""", "f")]
    public void Preview_follows_field_priority(string toolInput, string expected)
    {
        var p = Payload($$"""{"hook_event_name":"PreToolUse","tool_input":{{toolInput}}}""");
        Assert.Equal(expected, p.GetProperty("tool_input_preview").GetString());
    }

    [Fact]
    public void Notification_message_and_title_are_forwarded()
    {
        var p = Payload("""{"session_id":"s","hook_event_name":"Notification","notification_type":"permission_prompt","message":"Claude needs your permission","title":"X"}""");
        Assert.Equal("Claude needs your permission", p.GetProperty("message").GetString());
        Assert.Equal("X", p.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("""{"session_id":"s","hook_event_name":"Stop"}""")]
    [InlineData("""{"session_id":"s","hook_event_name":"PreToolUse","tool_input":null}""")]
    [InlineData("""{"session_id":"s","hook_event_name":"PreToolUse","tool_input":"cadena"}""")]
    [InlineData("""{"session_id":"s","hook_event_name":"PreToolUse","tool_input":{"description":"sin campos conocidos"}}""")]
    public void Missing_tool_input_gives_empty_preview(string input)
    {
        var p = Payload(input);
        Assert.Equal(string.Empty, p.GetProperty("tool_input_preview").GetString());
        Assert.Equal(string.Empty, p.GetProperty("message").GetString());
        Assert.Equal(string.Empty, p.GetProperty("title").GetString());
    }
}
