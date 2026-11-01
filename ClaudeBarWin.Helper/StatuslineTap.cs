using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClaudeBarWin.Helper;

/// <summary>Captures rate limits and emits either a compact status or an ASCII JSON tee.</summary>
internal static class StatuslineTap
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static void Run(byte[] input, Stream stdout, bool tee, string rateLimitsDir, long nowUnix)
    {
        ReadOnlyMemory<byte> json = input;
        if (input.Length >= 3 && input[0] == 0xEF && input[1] == 0xBB && input[2] == 0xBF)
            json = input.AsMemory(3);

        JsonDocument document;
        try
        {
            _ = StrictUtf8.GetCharCount(json.Span);
            document = JsonDocument.Parse(json);
        }
        catch
        {
            if (tee)
                stdout.Write(input);
            stdout.Flush();
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            byte[]? teeOutput = null;
            if (tee)
            {
                try
                {
                    using var prepared = new MemoryStream();
                    AsciiJson.Write(root, prepared);
                    teeOutput = prepared.ToArray();
                }
                catch
                {
                    stdout.Write(input);
                    stdout.Flush();
                    return;
                }
            }

            JsonElement rateLimits = default;
            var hasRateLimits = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("rate_limits", out rateLimits)
                && rateLimits.ValueKind == JsonValueKind.Object;

            if (hasRateLimits)
            {
                try
                {
                    string? sessionId = root.TryGetProperty("session_id", out var session)
                        && session.ValueKind == JsonValueKind.String
                            ? session.GetString()
                            : null;
                    SnapshotWriter.WriteIfNeeded(rateLimitsDir, sessionId, rateLimits, nowUnix);
                }
                catch
                {
                    // Disk failures must never affect the user's status line.
                }
            }

            if (tee)
            {
                stdout.Write(teeOutput!);
            }
            else if (hasRateLimits)
            {
                var line = CompactLine(rateLimits);
                if (line.Length > 0)
                    stdout.Write(Encoding.UTF8.GetBytes(line + "\n"));
            }

            stdout.Flush();
        }
    }

    public static string CompactLine(JsonElement rateLimits)
    {
        var parts = new List<string>(2);
        AddWindow("five_hour", "5h");
        AddWindow("seven_day", "7d");
        return string.Join(" · ", parts);

        void AddWindow(string key, string label)
        {
            if (rateLimits.ValueKind != JsonValueKind.Object
                || !rateLimits.TryGetProperty(key, out var window)
                || window.ValueKind != JsonValueKind.Object
                || !window.TryGetProperty("used_percentage", out var percentage)
                || percentage.ValueKind != JsonValueKind.Number
                || !percentage.TryGetDouble(out var value)
                || !double.IsFinite(value))
                return;

            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            parts.Add($"{label} {rounded.ToString("0", CultureInfo.InvariantCulture)}%");
        }
    }
}
