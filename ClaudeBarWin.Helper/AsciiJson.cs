using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace ClaudeBarWin.Helper;

/// <summary>Writes semantically equivalent JSON restricted to shell-safe ASCII.</summary>
internal static class AsciiJson
{
    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = CreateEncoder(),
        Indented = false,
    };

    public static void Write(JsonElement root, Stream output)
    {
        using var writer = new Utf8JsonWriter(output, Options);
        root.WriteTo(writer);
    }

    private static JavaScriptEncoder CreateEncoder()
    {
        var settings = new TextEncoderSettings(UnicodeRanges.BasicLatin);
        foreach (var character in "<>&'+")
            settings.ForbidCharacter(character);
        return JavaScriptEncoder.Create(settings);
    }
}
