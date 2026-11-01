using System.IO.Pipes;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClaudeBarWin.Helper;

/// <summary>Native replacement for hooks/claudebar-hook.ps1.</summary>
internal static class HookForwarder
{
    public const string DefaultPipeName = "claudebar";

    /// <summary>Versión del payload. v1 = el del .ps1 de respaldo (sin «v»); v2 añade mensaje, título y vista previa.</summary>
    public const int PayloadVersion = 2;

    /// <summary>Tope de la vista previa de tool_input (incluida la «…» del recorte).</summary>
    public const int PreviewMaxChars = 160;

    // Campos de tool_input que sirven de vista previa, por prioridad.
    private static readonly string[] PreviewFields = ["command", "file_path", "url", "pattern"];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string MapStatus(string eventName, string notificationType)
    {
        static bool Is(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        if (Is(eventName, "PreToolUse")) return "running_tool";
        if (Is(eventName, "PostToolUse")) return "processing";
        if (Is(eventName, "PermissionRequest")) return "waiting_for_approval";
        if (Is(eventName, "Notification"))
        {
            if (Is(notificationType, "permission_prompt")) return "waiting_for_approval";
            if (notificationType.Length == 0
                || Is(notificationType, "idle_prompt")
                || Is(notificationType, "agent_needs_input")
                || Is(notificationType, "elicitation_dialog"))
                return "waiting_for_input";
            return "noop";
        }
        if (Is(eventName, "Stop")) return "waiting_for_input";
        if (Is(eventName, "SubagentStop")) return "processing";
        if (Is(eventName, "UserPromptSubmit")) return "processing";
        if (Is(eventName, "PreCompact")) return "compacting";
        if (Is(eventName, "SessionStart")) return "starting";
        if (Is(eventName, "SessionEnd")) return "ended";
        return "noop";
    }

    public static string? BuildPayload(byte[] input, int pid, long ts)
    {
        try
        {
            ReadOnlyMemory<byte> json = input;
            if (input.Length >= 3 && input[0] == 0xEF && input[1] == 0xBB && input[2] == 0xBF)
                json = input.AsMemory(3);

            _ = StrictUtf8.GetCharCount(json.Span);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            string StringValue(string propertyName) =>
                root.TryGetProperty(propertyName, out var value) ? PowerShellString(value) : string.Empty;

            var eventName = StringValue("hook_event_name");
            var notificationType = StringValue("notification_type");
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
            {
                writer.WriteStartObject();
                writer.WriteString("session_id", StringValue("session_id"));
                writer.WriteString("cwd", StringValue("cwd"));
                writer.WriteNumber("pid", pid);
                writer.WriteString("event", eventName);
                writer.WriteString("status", MapStatus(eventName, notificationType));
                writer.WriteString("tool", StringValue("tool_name"));
                writer.WriteString("tool_use_id", StringValue("tool_use_id"));
                writer.WriteString("notification_type", notificationType);
                writer.WriteNumber("ts", ts);
                // v2 (notch): detrás de los campos del .ps1 para no alterar el orden de v1.
                writer.WriteNumber("v", PayloadVersion);
                writer.WriteString("message", StringValue("message"));
                writer.WriteString("title", StringValue("title"));
                writer.WriteString("tool_input_preview",
                    root.TryGetProperty("tool_input", out var toolInput) ? ToolInputPreview(toolInput) : string.Empty);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Vista previa de una línea del tool_input: el primer campo de texto no vacío entre command,
    /// file_path, url y pattern; saltos de línea → espacio; recorte a <see cref="PreviewMaxChars"/> con «…».
    /// Puede llevar datos sensibles (comandos, rutas): el receptor nunca debe escribirla en el log.
    /// </summary>
    internal static string ToolInputPreview(JsonElement toolInput)
    {
        if (toolInput.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var field in PreviewFields)
        {
            if (!toolInput.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
                continue;
            var text = value.GetString();
            if (string.IsNullOrEmpty(text)) continue;
            return Truncate(text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' '));
        }
        return string.Empty;
    }

    private static string Truncate(string text)
    {
        if (text.Length <= PreviewMaxChars) return text;
        var cut = PreviewMaxChars - 1;
        // No dejar medio par sustituto (emoji) colgando antes de la «…».
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return string.Concat(text.AsSpan(0, cut), "…");
    }

    private static string PowerShellString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Number when value.TryGetDouble(out var number) => number.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        JsonValueKind.Object => "@{" + string.Join("; ", value.EnumerateObject().Select(
            property => property.Name + "=" + ObjectPropertyString(property.Value))) + "}",
        JsonValueKind.Array => string.Join(" ", value.EnumerateArray().Select(ArrayItemString)),
        _ => string.Empty,
    };

    private static string ObjectPropertyString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => PowerShellString(value),
    };

    private static string ArrayItemString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => PowerShellString(value),
    };

    public static void Run(byte[] input, string pipeName, int connectTimeoutMs)
    {
        try
        {
            var payload = BuildPayload(
                input,
                Environment.ProcessId,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (payload is null)
                return;

            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(connectTimeoutMs);
            var bytes = Encoding.UTF8.GetBytes(payload + "\n");
            pipe.Write(bytes);
            pipe.Flush();
        }
        catch
        {
            // Missing, busy, or timed-out pipe: the hook is intentionally silent.
        }
    }
}
