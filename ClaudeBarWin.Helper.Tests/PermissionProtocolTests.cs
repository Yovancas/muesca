using System.Text;
using System.Text.Json;
using ClaudeBarWin.Helper;

public sealed class PermissionProtocolTests
{
    private const string Id = "0f8fad5b-d9cb-469f-a165-70867728950e";

    [Fact]
    public void Constants_match_the_plan()
    {
        Assert.Equal("claudebar-perm", PermissionProtocol.PipeName);
        Assert.Equal(300, PermissionProtocol.ConnectTimeoutMs);
        Assert.Equal(25000, PermissionProtocol.DecisionTimeoutMs);
    }

    [Fact]
    public void Render_allow_is_exact() => Assert.Equal(
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""",
        PermissionProtocol.RenderDecision(PermissionVerdict.Allow));

    [Fact]
    public void Render_deny_has_message() => Assert.Equal(
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denegado desde Muesca"}}}""",
        PermissionProtocol.RenderDecision(PermissionVerdict.Deny));

    [Fact]
    public void Render_none_is_empty() => Assert.Equal("", PermissionProtocol.RenderDecision(PermissionVerdict.None));

    [Fact]
    public void Render_unknown_value_is_empty() => Assert.Equal("", PermissionProtocol.RenderDecision((PermissionVerdict)42));

    [Theory]
    [InlineData("allow", "Allow")]
    [InlineData("deny", "Deny")]
    public void Valid_response_is_parsed(string decision, string expected)
    {
        var line = $$"""{"v":1,"request_id":"{{Id}}","decision":"{{decision}}"}""";
        Assert.True(PermissionProtocol.TryParseResponse(line, Id, out var verdict));
        Assert.Equal(Enum.Parse<PermissionVerdict>(expected), verdict);
    }

    [Fact]
    public void Response_tolerates_trailing_line_break()
    {
        Assert.True(PermissionProtocol.TryParseResponse($$"""{"v":1,"request_id":"{{Id}}","decision":"allow"}""" + "\r\n", Id, out var verdict));
        Assert.Equal(PermissionVerdict.Allow, verdict);
    }

    [Fact]
    public void Response_with_other_request_id_is_None()
    {
        var line = """{"v":1,"request_id":"11111111-1111-1111-1111-111111111111","decision":"allow"}""";
        Assert.False(PermissionProtocol.TryParseResponse(line, Id, out var verdict));
        Assert.Equal(PermissionVerdict.None, verdict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"v":1,"request_id":"ID","decision":"maybe"}""")]
    [InlineData("""{"v":1,"request_id":"ID","decision":"Allow"}""")]
    [InlineData("""{"v":1,"request_id":"ID","decision":"ask"}""")]
    [InlineData("""{"v":1,"request_id":"ID","decision":true}""")]
    [InlineData("""{"v":2,"request_id":"ID","decision":"allow"}""")]
    [InlineData("""{"v":"1","request_id":"ID","decision":"allow"}""")]
    [InlineData("""{"v":1.0,"request_id":"ID","decision":"allow"}""")]
    [InlineData("""{"request_id":"ID","decision":"allow"}""")]
    [InlineData("""{"v":1,"decision":"allow"}""")]
    [InlineData("""{"v":1,"request_id":"ID"}""")]
    [InlineData("""{"v":1,"request_id":"ID","decision":"deny","decision":"allow"}""")]
    [InlineData("""{"v":1,"request_id":"ID","request_id":"ID","decision":"allow"}""")]
    [InlineData("""{"v":1,"request_id":"ID","decision":"allow"} {"v":1}""")]
    [InlineData("""{"v":1,"request_id":"ID","decision":"allow"}""" + "\n" + """{"v":1,"request_id":"ID","decision":"deny"}""")]
    public void Malformed_or_unknown_decision_is_None(string template)
    {
        Assert.False(PermissionProtocol.TryParseResponse(template.Replace("ID", Id), Id, out var verdict));
        Assert.Equal(PermissionVerdict.None, verdict);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_expected_request_id_never_matches(string? expected)
    {
        Assert.False(PermissionProtocol.TryParseResponse("""{"v":1,"request_id":"","decision":"allow"}""", expected!, out var verdict));
        Assert.Equal(PermissionVerdict.None, verdict);
    }

    [Fact]
    public void Null_line_is_None()
    {
        Assert.False(PermissionProtocol.TryParseResponse(null!, Id, out var verdict));
        Assert.Equal(PermissionVerdict.None, verdict);
    }

    [Fact]
    public void Request_has_fields_in_order_on_one_line()
    {
        var input = Encoding.UTF8.GetBytes("""{"session_id":"s1","cwd":"C:\\español","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"echo hola\nadiós"}}""");
        var line = PermissionProtocol.BuildRequest(Id, input);

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal(
            ["v", "request_id", "session_id", "cwd", "tool", "tool_input_preview", "preview_complete"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        Assert.Equal(Id, root.GetProperty("request_id").GetString());
        Assert.Equal("s1", root.GetProperty("session_id").GetString());
        Assert.Equal("C:\\español", root.GetProperty("cwd").GetString());
        Assert.Equal("Bash", root.GetProperty("tool").GetString());
        // Bash con solo el comando: vista previa completa, entera y con su salto de línea (escapado en el JSON).
        Assert.Equal("echo hola\nadiós", root.GetProperty("tool_input_preview").GetString());
        Assert.True(root.GetProperty("preview_complete").GetBoolean());
    }

    [Fact]
    public void Request_contains_preview_not_full_input()
    {
        var command = "SECRET-" + new string('x', 2000) + "-TAIL";
        var input = Encoding.UTF8.GetBytes($$$"""{"session_id":"s","cwd":"c","tool_name":"Bash","tool_input":{"command":"{{{command}}}","description":"desc-oculta"}}""");
        var line = PermissionProtocol.BuildRequest(Id, input);

        using var document = JsonDocument.Parse(line);
        var preview = document.RootElement.GetProperty("tool_input_preview").GetString()!;
        Assert.True(preview.Length <= 160);
        Assert.StartsWith("SECRET-", preview);
        Assert.DoesNotContain("-TAIL", line);
        Assert.DoesNotContain("desc-oculta", line);
        Assert.DoesNotContain("tool_input\"", line);
    }

    [Fact]
    public void Request_without_fields_uses_empty_strings()
    {
        var line = PermissionProtocol.BuildRequest(Id, "{}"u8.ToArray());
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("", root.GetProperty("session_id").GetString());
        Assert.Equal("", root.GetProperty("tool").GetString());
        Assert.Equal("", root.GetProperty("tool_input_preview").GetString());
    }

    [Fact]
    public void Request_accepts_utf8_bom()
    {
        var input = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("""{"tool_name":"Edit"}""")).ToArray();
        using var document = JsonDocument.Parse(PermissionProtocol.BuildRequest(Id, input));
        Assert.Equal("Edit", document.RootElement.GetProperty("tool").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{roto")]
    [InlineData("[]")]
    [InlineData("123")]
    public void Request_throws_for_invalid_or_non_object_input(string raw) =>
        Assert.ThrowsAny<Exception>(() => PermissionProtocol.BuildRequest(Id, Encoding.UTF8.GetBytes(raw)));

    [Fact]
    public void Request_throws_for_invalid_utf8() =>
        Assert.ThrowsAny<Exception>(() => PermissionProtocol.BuildRequest(Id, [(byte)'{', (byte)'"', 0xFF, (byte)'"', (byte)':', (byte)'1', (byte)'}']));
}
