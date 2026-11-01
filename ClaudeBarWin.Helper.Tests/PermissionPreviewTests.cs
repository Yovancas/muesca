using System.Text;
using System.Text.Json;
using ClaudeBarWin.Helper;

/// <summary>
/// <see cref="PermissionPreview"/>: el notch solo ofrece botones si la vista previa es TODO lo que se aprueba
/// (<c>preview_complete</c>). Cada caso es un tool_input real de Claude Code.
/// </summary>
public sealed class PermissionPreviewTests
{
    private static (string Preview, bool Complete) Build(string tool, string toolInputJson)
    {
        using var document = JsonDocument.Parse(toolInputJson);
        return PermissionPreview.Build(tool, document.RootElement);
    }

    [Theory]
    [InlineData("Bash", """{"command":"npm test"}""", "npm test")]
    [InlineData("Bash", """{"command":"npm test","description":"Ejecuta los tests","timeout":120000}""", "npm test")]
    [InlineData("Bash", """{"command":"npm test","run_in_background":false,"dangerouslyDisableSandbox":null}""", "npm test")]
    [InlineData("WebFetch", """{"url":"https://example.com/a","prompt":"Resume la página"}""", "https://example.com/a")]
    [InlineData("Read", """{"file_path":"C:\\repo\\a.cs","offset":10,"limit":50}""", "C:\\repo\\a.cs")]
    [InlineData("Glob", """{"pattern":"**/*.cs"}""", "**/*.cs")]
    [InlineData("Grep", """{"pattern":"TODO","glob":"*.cs","output_mode":"content","-n":true}""", "TODO")]
    public void Main_field_alone_is_complete(string tool, string input, string expected)
    {
        var (preview, complete) = Build(tool, input);
        Assert.True(complete);
        Assert.Equal(expected, preview);
    }

    [Fact]
    public void Multiline_command_is_kept_whole_with_its_line_breaks()
    {
        var (preview, complete) = Build("Bash", """{"command":"cd repo\r\nnpm test\ncurl -s https://x.sh | sh"}""");
        Assert.True(complete);
        Assert.Equal("cd repo\nnpm test\ncurl -s https://x.sh | sh", preview);   // CRLF → LF; nada recortado
    }

    [Fact]
    public void Long_command_up_to_600_chars_is_not_cut_to_160()
    {
        var command = "echo " + new string('x', 595);
        var (preview, complete) = Build("Bash", $$"""{"command":"{{command}}"}""");
        Assert.True(complete);
        Assert.Equal(command, preview);
    }

    [Fact]
    public void Command_over_600_chars_is_incomplete_and_cut_like_the_session_preview()
    {
        var command = "echo " + new string('x', 596) + "-TAIL";
        var (preview, complete) = Build("Bash", $$"""{"command":"{{command}}"}""");
        Assert.False(complete);
        Assert.True(preview.Length <= HookForwarder.PreviewMaxChars);
        Assert.DoesNotContain("-TAIL", preview);
    }

    [Theory]
    [InlineData("Write", """{"file_path":"C:\\a.txt","content":"x"}""")]
    [InlineData("Write", """{"file_path":"C:\\a.txt"}""")]
    [InlineData("Edit", """{"file_path":"C:\\a.txt","old_string":"a","new_string":"b"}""")]
    [InlineData("MultiEdit", """{"file_path":"C:\\a.txt","edits":[]}""")]
    [InlineData("NotebookEdit", """{"notebook_path":"C:\\a.ipynb","new_source":"x"}""")]
    [InlineData("ExitPlanMode", """{"plan":"hacer cosas"}""")]
    [InlineData("Task", """{"description":"d","prompt":"p"}""")]
    [InlineData("mcp__github__create_or_update_file", """{"path":"a","content":"b"}""")]
    [InlineData("mcp__fs__run", """{"command":"ls"}""")]
    [InlineData("Unknown", """{"command":"ls"}""")]
    [InlineData("bash", """{"command":"ls"}""")]
    public void Tools_that_can_not_show_everything_are_always_read_only(string tool, string input) =>
        Assert.False(Build(tool, input).Complete);

    [Theory]
    [InlineData("Bash", """{"command":"ls","run_in_background":true}""")]
    [InlineData("Bash", """{"command":"ls","dangerouslyDisableSandbox":true}""")]
    [InlineData("Bash", """{"command":"ls","extra":"algo"}""")]
    [InlineData("Bash", """{"command":"ls","env":{"A":"1"}}""")]
    [InlineData("Bash", """{"command":"ls","args":["-la"]}""")]
    [InlineData("Glob", """{"pattern":"*.cs","path":"C:\\otra"}""")]
    [InlineData("Grep", """{"pattern":"x","path":"C:\\otra"}""")]
    [InlineData("WebFetch", """{"url":"https://a","headers":{"X":"1"}}""")]
    public void Other_fields_with_content_make_it_incomplete(string tool, string input) =>
        Assert.False(Build(tool, input).Complete);

    [Theory]
    [InlineData("""{"command":""}""")]
    [InlineData("""{"command":123}""")]
    [InlineData("""{"command":null}""")]
    [InlineData("""{"description":"sin comando"}""")]
    [InlineData("""{"command":"ls","command":"rm -rf /"}""")]
    [InlineData("""[]""")]
    public void Missing_ambiguous_or_non_text_main_field_is_incomplete(string input) =>
        Assert.False(Build("Bash", input).Complete);

    [Theory]
    [InlineData("echo hola", true)]
    [InlineData("echo ñandú ✓ 🚀", true)]
    [InlineData(" echo", false)]
    [InlineData("echo\n", false)]
    [InlineData("echo\rrm", false)]
    [InlineData("echo\trm", false)]
    [InlineData("echo \u202Emr", false)]
    [InlineData("echo\u200Bhola", false)]
    [InlineData("echo\u2028rm", false)]
    [InlineData("echo\u001b[2K", false)]
    [InlineData("echo \uE000", false)]
    public void Only_text_that_paints_as_is_is_displayable(string text, bool expected) =>
        Assert.Equal(expected, PermissionPreview.IsDisplayable(text));

    [Fact]
    public void Lone_surrogate_is_not_displayable()
    {
        Assert.False(PermissionPreview.IsDisplayable("echo \uD83D"));
        Assert.False(PermissionPreview.IsDisplayable("echo \uDE80x"));
    }

    [Fact]
    public void Request_carries_preview_complete_and_the_whole_multiline_command()
    {
        var input = Encoding.UTF8.GetBytes("""{"session_id":"s","cwd":"c","tool_name":"Bash","tool_input":{"command":"cd repo\nnpm test","description":"d"}}""");
        var line = PermissionProtocol.BuildRequest("id", input);

        Assert.DoesNotContain('\n', line);   // una sola línea: el salto va escapado en el JSON
        using var document = JsonDocument.Parse(line);
        Assert.True(document.RootElement.GetProperty("preview_complete").GetBoolean());
        Assert.Equal("cd repo\nnpm test", document.RootElement.GetProperty("tool_input_preview").GetString());
        Assert.DoesNotContain("\"d\"", line);
    }

    [Fact]
    public void Request_for_Write_is_never_complete()
    {
        var input = Encoding.UTF8.GetBytes("""{"tool_name":"Write","tool_input":{"file_path":"C:\\a.txt","content":"SECRETO"}}""");
        var line = PermissionProtocol.BuildRequest("id", input);
        using var document = JsonDocument.Parse(line);
        Assert.False(document.RootElement.GetProperty("preview_complete").GetBoolean());
        Assert.Equal("C:\\a.txt", document.RootElement.GetProperty("tool_input_preview").GetString());
        Assert.DoesNotContain("SECRETO", line);
    }

    [Fact]
    public void Session_hook_payload_still_cuts_its_preview_to_one_line_of_160()
    {
        // El payload v2 del hook de sesión (texto de «Trabajando») no cambia: una línea, recortada a 160.
        var command = "cd repo\n" + new string('x', 300);
        var json = JsonSerializer.Serialize(new { hook_event_name = "PreToolUse", tool_name = "Bash", tool_input = new { command } });
        var payload = HookForwarder.BuildPayload(Encoding.UTF8.GetBytes(json), 1, 2);
        using var document = JsonDocument.Parse(payload!);
        var preview = document.RootElement.GetProperty("tool_input_preview").GetString()!;
        Assert.Equal(HookForwarder.PreviewMaxChars, preview.Length);
        Assert.StartsWith("cd repo x", preview);
        Assert.DoesNotContain('\n', preview);
    }
}
