using System.Text.Json;

namespace ClaudeBarWin.Services;

/// <summary>Una ventana de cuota observada: % usado, fin de la ventana y cuándo se vio (y si vino de la statusline).</summary>
public sealed record RateWindowObs(double Pct, DateTimeOffset? ResetsAt, DateTime ObservedUtc, bool FromStatusline);

public sealed record RateLimitSnapshot(string SessionId, DateTime SeenUtc, DateTime ChangedUtc,
    IReadOnlyDictionary<string, RateWindowObs> Windows);

/// <summary>
/// v0.4 — lee los snapshots que escribe claudebar-helper desde la statusline de cada sesión de Claude Code
/// (contrato v1, spec §4: v, session_id, seen_at, changed_at, rate_limits). Tolerante a archivos rotos o a medio
/// escribir; poda los de más de 8 días (sesiones que ya no existen).
/// </summary>
public sealed class RateLimitSnapshotStore
{
    internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(8);
    private readonly string _dir;

    public RateLimitSnapshotStore(string dir) => _dir = dir;

    public static string DefaultDir => Path.Combine(HelperDeployer.DataDir, "ratelimits");

    public string Dir => _dir;

    public IReadOnlyList<RateLimitSnapshot> ReadAll(DateTime nowUtc)
    {
        var list = new List<RateLimitSnapshot>();
        if (!Directory.Exists(_dir)) return list;
        foreach (var f in Directory.EnumerateFiles(_dir, "*.json"))
        {
            RateLimitSnapshot? s = null;
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                s = Parse(sr.ReadToEnd());
            }
            catch { /* a medio escribir o bloqueado: se lee en el próximo refresco */ }
            var seen = s?.SeenUtc ?? SafeMtime(f);
            if (nowUtc - seen > MaxAge) { try { File.Delete(f); } catch { } continue; }
            if (s is not null) list.Add(s);
        }
        return list;
    }

    /// <summary>Borra todos los snapshots (al desactivar el enganche: la API vuelve a mandar al momento).</summary>
    public void Clear()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir)) try { File.Delete(f); } catch { }
    }

    public static RateLimitSnapshot? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1) return null;
            if (!r.TryGetProperty("rate_limits", out var rl) || rl.ValueKind != JsonValueKind.Object) return null;
            var seen = DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("seen_at").GetInt64()).UtcDateTime;
            var changed = DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("changed_at").GetInt64()).UtcDateTime;
            var sid = r.TryGetProperty("session_id", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";

            var windows = new Dictionary<string, RateWindowObs>();
            foreach (var p in rl.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;
                if (!p.Value.TryGetProperty("used_percentage", out var u) || u.ValueKind != JsonValueKind.Number) continue;
                DateTimeOffset? reset = p.Value.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds((long)ra.GetDouble())
                    : null;
                windows[p.Name] = new RateWindowObs(u.GetDouble(), reset, seen, true);
            }
            return new RateLimitSnapshot(sid, seen, changed, windows);
        }
        catch { return null; }
    }

    private static DateTime SafeMtime(string f)
    {
        try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.UtcNow; }
    }
}
