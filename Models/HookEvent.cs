using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeBarWin.Models;

/// <summary>Un evento emitido por el hook de Claude Code a través del Named Pipe.</summary>
public sealed class HookEvent
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("cwd")] public string Cwd { get; set; } = "";
    [JsonPropertyName("pid")] public int? Pid { get; set; }
    [JsonPropertyName("event")] public string Event { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("tool")] public string? Tool { get; set; }
    [JsonPropertyName("tool_use_id")] public string? ToolUseId { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("ts")] public long Ts { get; set; }
    [JsonPropertyName("notification_type")] public string? NotificationType { get; set; }

    /// <summary>Versión del payload: el .ps1 de respaldo manda v1 sin «v», así que ausente = 1.</summary>
    [JsonPropertyName("v")] public int Version { get; set; } = 1;
    /// <summary>Título de la Notification (v2).</summary>
    [JsonPropertyName("title")] public string? Title { get; set; }
    /// <summary>
    /// Vista previa de la herramienta (comando, ruta, URL o patrón; ≤160 caracteres) (v2).
    /// Puede llevar datos sensibles: NUNCA se escribe en AppLog.
    /// </summary>
    [JsonPropertyName("tool_input_preview")] public string? ToolInputPreview { get; set; }

    /// <summary>
    /// Orden de llegada asignado por el servidor del pipe al ACEPTAR la conexión (0 = desconocido, p. ej.
    /// en tests). SessionStore descarta eventos con secuencia menor que el último aplicado de la sesión.
    /// </summary>
    [JsonIgnore] public long Seq { get; set; }

    /// <summary>Ancestros del cliente del pipe; los rellena el servidor (no vienen en el JSON).</summary>
    [JsonIgnore] public IReadOnlyList<ClaudeBarWin.Services.ProcessInfo>? Ancestors { get; set; }

    /// <summary>
    /// Evento que no cambia la fase (p. ej. Notification de tipo auth_success/agent_completed): antes
    /// caía en el <c>_ =&gt; Idle</c> del mapeo y sacaba a la sesión de Processing sin motivo.
    /// </summary>
    [JsonIgnore] public bool IsNoop => Status == "noop";

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Parsea una línea JSON. Devuelve null si no es JSON válido o falta session_id.</summary>
    public static HookEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var e = JsonSerializer.Deserialize<HookEvent>(line, Opts);
            if (e is null || string.IsNullOrEmpty(e.SessionId)) return null;
            return e;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Mapea el evento a una fase de sesión.</summary>
    public SessionPhase ToPhase()
    {
        if (Event == "PreCompact") return SessionPhase.Compacting;
        return Status switch
        {
            "waiting_for_approval" => SessionPhase.WaitingForApproval,
            "waiting_for_input" => SessionPhase.WaitingForInput,
            "running_tool" or "processing" or "starting" => SessionPhase.Processing,
            "compacting" => SessionPhase.Compacting,
            "ended" => SessionPhase.Ended,
            _ => SessionPhase.Idle,
        };
    }
}
