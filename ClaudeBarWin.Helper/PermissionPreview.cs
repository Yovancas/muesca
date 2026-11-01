using System.Globalization;
using System.Text.Json;

namespace ClaudeBarWin.Helper;

/// <summary>
/// Vista previa de una petición de permiso y si es COMPLETA (<c>preview_complete</c>): el notch solo ofrece Permitir /
/// Denegar cuando el usuario ve todo lo que aprueba. Completa = la herramienta está en la lista de abajo, su campo
/// principal es texto sin recortar (≤ <see cref="MaxChars"/>, saltos de línea conservados) y el resto del tool_input no
/// trae nada relevante (solo los campos inocuos de su lista, o campos vacíos: null, "", false, [] o {}).
///
/// <para>Write, Edit, MultiEdit, NotebookEdit, ExitPlanMode, Task, cualquier <c>mcp__*</c> y toda herramienta que no esté
/// en la lista van SIEMPRE incompletas (solo lectura). Una vista previa incompleta es la de siempre: una línea recortada a
/// <see cref="HookForwarder.PreviewMaxChars"/> (<see cref="HookForwarder.ToolInputPreview"/>), solo para mostrar.</para>
/// </summary>
internal static class PermissionPreview
{
    /// <summary>Tope de la vista previa completa. Más largo ⇒ incompleta (el notch nunca pinta tanto en 4 líneas).</summary>
    public const int MaxChars = 600;

    // Herramienta → campo principal (lo que se pinta) y campos que no cambian lo que se aprueba.
    private static readonly Dictionary<string, (string Main, string[] Harmless)> Answerable = new(StringComparer.Ordinal)
    {
        ["Bash"] = ("command", ["description", "timeout"]),
        ["WebFetch"] = ("url", ["prompt"]),
        ["Read"] = ("file_path", ["offset", "limit", "pages"]),
        // Glob/Grep con «path» buscan en otra carpeta que no se pinta: incompletas.
        ["Glob"] = ("pattern", []),
        ["Grep"] = ("pattern", ["glob", "type", "output_mode", "-i", "-n", "-A", "-B", "-C", "context", "head_limit",
            "offset", "multiline"]),
    };

    /// <summary>Vista previa para la petición y si es completa. Nunca lanza con un tool_input raro: incompleta.</summary>
    public static (string Preview, bool Complete) Build(string tool, JsonElement toolInput)
    {
        if (TryComplete(tool, toolInput, out var full)) return (full, true);
        return (HookForwarder.ToolInputPreview(toolInput), false);
    }

    private static bool TryComplete(string tool, JsonElement toolInput, out string preview)
    {
        preview = string.Empty;
        if (toolInput.ValueKind != JsonValueKind.Object || !Answerable.TryGetValue(tool ?? string.Empty, out var rule))
            return false;

        string? main = null;
        foreach (var property in toolInput.EnumerateObject())
        {
            if (string.Equals(property.Name, rule.Main, StringComparison.Ordinal))
            {
                // Clave duplicada o no texto: JSON ambiguo, no se puede asegurar qué se ejecuta.
                if (main is not null || property.Value.ValueKind != JsonValueKind.String) return false;
                main = property.Value.GetString() ?? string.Empty;
                continue;
            }
            if (Array.IndexOf(rule.Harmless, property.Name) >= 0) continue;
            if (!IsEmpty(property.Value)) return false;
        }

        if (string.IsNullOrEmpty(main)) return false;
        var text = main.Replace("\r\n", "\n");
        if (text.Length > MaxChars || !IsDisplayable(text)) return false;
        preview = text;
        return true;
    }

    /// <summary>¿Ese valor no aporta nada? (null, "", false, [] o {}).</summary>
    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => true,
        JsonValueKind.String => value.GetString()!.Length == 0,
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        _ => false,
    };

    /// <summary>
    /// ¿Se pinta tal cual? Solo '\n' como control; nada de tabuladores, '\r' suelto, separadores de línea Unicode ni
    /// caracteres de formato (marcas bidi, ancho cero…), que pueden esconder o reordenar lo que se ve. Sin espacios en
    /// blanco al principio ni al final (líneas vacías que empujan el texto fuera de la vista). Sustitutos sueltos, fuera.
    /// </summary>
    internal static bool IsDisplayable(string text)
    {
        if (text.Length == 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                if (CharUnicodeInfo.GetUnicodeCategory(text, i) is UnicodeCategory.Format or UnicodeCategory.PrivateUse)
                    return false;
                i++;
                continue;
            }
            if (c == '\n') continue;
            if (char.IsLowSurrogate(c)) return false;
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.PrivateUse:
                    return false;
            }
        }
        return true;
    }
}
