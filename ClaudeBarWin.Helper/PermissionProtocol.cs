using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClaudeBarWin.Helper;

/// <summary>Decisión de ClaudeBar ante un PermissionRequest. <see cref="None"/> = sin decisión (Claude Code muestra su diálogo).</summary>
internal enum PermissionVerdict
{
    None,
    Allow,
    Deny,
}

/// <summary>
/// Protocolo del pipe de permisos (una línea JSON por sentido) y salida exacta del hook PermissionRequest.
/// Petición: {"v":1,"request_id":"…","session_id":"…","cwd":"…","tool":"…","tool_input_preview":"…","preview_complete":true|false}.
/// Respuesta: {"v":1,"request_id":"…","decision":"allow"|"deny"}. Cualquier otra cosa ⇒ sin decisión.
/// </summary>
internal static class PermissionProtocol
{
    public const string PipeName = "claudebar-perm";
    public const int ConnectTimeoutMs = 300;
    public const int DecisionTimeoutMs = 25000;
    public const int Version = 1;

    private const string AllowOutput =
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""";
    private const string DenyOutput =
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denegado desde Muesca"}}}""";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Petición de una línea (sin salto final) con solo la vista previa de tool_input, nunca el tool_input completo.
    /// <c>preview_complete</c> (<see cref="PermissionPreview"/>) dice si esa vista previa es TODO lo que se aprueba: sin
    /// ella el notch no ofrece botones. Los saltos de línea de una vista previa completa van escapados en el JSON.
    /// Lanza si la entrada del hook no es UTF-8 válido o no es un objeto JSON: el llamante lo trata como «sin decisión».
    /// </summary>
    public static string BuildRequest(string requestId, byte[] hookInput)
    {
        ReadOnlyMemory<byte> json = hookInput;
        if (hookInput.Length >= 3 && hookInput[0] == 0xEF && hookInput[1] == 0xBB && hookInput[2] == 0xBF)
            json = hookInput.AsMemory(3);

        _ = StrictUtf8.GetCharCount(json.Span);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("hook input is not a JSON object");

        string StringValue(string propertyName) =>
            root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WriteString("request_id", requestId);
            writer.WriteString("session_id", StringValue("session_id"));
            writer.WriteString("cwd", StringValue("cwd"));
            var tool = StringValue("tool_name");
            var (preview, complete) = root.TryGetProperty("tool_input", out var toolInput)
                ? PermissionPreview.Build(tool, toolInput)
                : (string.Empty, false);
            writer.WriteString("tool", tool);
            writer.WriteString("tool_input_preview", preview);
            writer.WriteBoolean("preview_complete", complete);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>
    /// Acepta solo una respuesta v1 con el mismo request_id y decision "allow" o "deny" exactos, sin claves duplicadas
    /// ni una segunda línea. Si no, devuelve false y <see cref="PermissionVerdict.None"/>.
    /// </summary>
    public static bool TryParseResponse(string line, string expectedRequestId, out PermissionVerdict verdict)
    {
        verdict = PermissionVerdict.None;
        try
        {
            if (line is null || string.IsNullOrEmpty(expectedRequestId))
                return false;

            var text = line.EndsWith("\r\n", StringComparison.Ordinal) ? line[..^2]
                : line.EndsWith('\n') ? line[..^1]
                : line;
            if (text.Contains('\n') || text.Contains('\r'))
                return false;

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            JsonElement? version = null, requestId = null, decision = null;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "v":
                        if (version is not null) return false;
                        version = property.Value;
                        break;
                    case "request_id":
                        if (requestId is not null) return false;
                        requestId = property.Value;
                        break;
                    case "decision":
                        if (decision is not null) return false;
                        decision = property.Value;
                        break;
                }
            }

            if (version is not { ValueKind: JsonValueKind.Number } v || !v.TryGetInt32(out var number) || number != Version)
                return false;
            if (requestId is not { ValueKind: JsonValueKind.String } id
                || !string.Equals(id.GetString(), expectedRequestId, StringComparison.Ordinal))
                return false;
            if (decision is not { ValueKind: JsonValueKind.String } d)
                return false;

            var parsed = d.GetString() switch
            {
                "allow" => PermissionVerdict.Allow,
                "deny" => PermissionVerdict.Deny,
                _ => PermissionVerdict.None,
            };
            if (parsed == PermissionVerdict.None)
                return false;

            verdict = parsed;
            return true;
        }
        catch
        {
            verdict = PermissionVerdict.None;
            return false;
        }
    }

    /// <summary>Salida exacta del hook para Claude Code; cadena vacía si no hay decisión explícita.</summary>
    public static string RenderDecision(PermissionVerdict v) => v switch
    {
        PermissionVerdict.Allow => AllowOutput,
        PermissionVerdict.Deny => DenyOutput,
        _ => string.Empty,
    };
}
