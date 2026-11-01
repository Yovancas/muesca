using System.Text;
using System.Text.Json;

namespace ClaudeBarWin.Helper;

/// <summary>Persists the latest status-line rate limits using the v1 snapshot contract.</summary>
internal static class SnapshotWriter
{
    internal const int HeartbeatSeconds = 60;

    public static string SanitizeId(string? raw)
    {
        var result = new StringBuilder(64);
        foreach (var character in raw ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                result.Append(character);
            if (result.Length == 64)
                break;
        }

        return result.Length == 0 ? "unknown" : result.ToString();
    }

    public static bool WriteIfNeeded(string dir, string? sessionId, JsonElement rateLimits, long nowUnix)
    {
        if (rateLimits.ValueKind != JsonValueKind.Object)
            return false;

        var id = SanitizeId(sessionId);
        try
        {
            using var mutex = new Mutex(initiallyOwned: false, $@"Local\ClaudeBarWin.RateLimits.{id}");
            var acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromMilliseconds(250)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                    return false;

                return WriteUnderLock(dir, id, sessionId, rateLimits, nowUnix);
            }
            finally
            {
                if (acquired)
                    mutex.ReleaseMutex();
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool WriteUnderLock(string dir, string id, string? sessionId, JsonElement rateLimits, long nowUnix)
    {
        var path = Path.Combine(dir, id + ".json");
        var canonical = Canonicalize(rateLimits);
        var changedAt = nowUnix;

        if (File.Exists(path))
        {
            try
            {
                using var old = JsonDocument.Parse(File.ReadAllBytes(path));
                var root = old.RootElement;
                var oldCanonical = Canonicalize(root.GetProperty("rate_limits"));
                var seenAt = root.GetProperty("seen_at").GetInt64();
                if (seenAt > nowUnix)
                    return false;
                if (oldCanonical.AsSpan().SequenceEqual(canonical))
                {
                    if (nowUnix - seenAt < HeartbeatSeconds)
                        return false;
                    changedAt = root.GetProperty("changed_at").GetInt64();
                }
            }
            catch
            {
                // A corrupt or incomplete destination is replaced below.
            }
        }

        Directory.CreateDirectory(dir);
        var temporaryPath = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            using (var stream = File.Create(temporaryPath))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("v", 1);
                writer.WriteString("session_id", sessionId ?? string.Empty);
                writer.WriteNumber("seen_at", nowUnix);
                writer.WriteNumber("changed_at", changedAt);
                writer.WritePropertyName("rate_limits");
                rateLimits.WriteTo(writer);
                writer.WriteEndObject();
            }

            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch { }
        }
    }

    private static byte[] Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(element, writer);
        return stream.ToArray();
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
