using Microsoft.Data.Sqlite;

namespace ClaudeBarWin.Services;

public sealed record PctPoint(DateTime TsUtc, double? FivePct, double? SevenPct);

/// <summary>
/// SQLite store of real-utilization samples (5h/7d %) over time. This is the only
/// source of historical % data — the API only returns a current snapshot. Used by the
/// "% real" chart mode and by the pace calculator.
/// </summary>
public sealed class UsageHistoryStore
{
    private readonly string _connStr;
    private long _lastAppendMs;

    public UsageHistoryStore(string? dbPath = null)
    {
        dbPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeBarWin", "history.db");
        try { Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!); } catch { }
        _connStr = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        Init();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        return c;
    }

    private void Init()
    {
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                @"CREATE TABLE IF NOT EXISTS usage_samples(
                    ts INTEGER PRIMARY KEY,
                    five_pct REAL, seven_pct REAL, opus_pct REAL, sonnet_pct REAL,
                    five_reset INTEGER, seven_reset INTEGER);";
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    private static long ToMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    /// <summary>
    /// Insert one sample. Throttled to at most once per 45s. (Era 60 s con el refresco por defecto de
    /// 60 s: el tick del timer llega a veces a 59,9 s y se descartaba ~la mitad de las muestras.)
    /// </summary>
    public void Append(RealUsage u, DateTime nowUtc)
    {
        long ms = ToMs(nowUtc);
        if (ms - _lastAppendMs < 45_000) return;
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                @"INSERT OR REPLACE INTO usage_samples
                    (ts, five_pct, seven_pct, opus_pct, sonnet_pct, five_reset, seven_reset)
                  VALUES ($ts, $f, $s, $o, $so, $fr, $sr);";
            cmd.Parameters.AddWithValue("$ts", ms);
            cmd.Parameters.AddWithValue("$f", (object?)u.FiveHour?.UtilizationPct ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$s", (object?)u.SevenDay?.UtilizationPct ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$o", (object?)u.SevenDayOpus?.UtilizationPct ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$so", (object?)u.SevenDaySonnet?.UtilizationPct ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$fr", u.FiveHour?.ResetsAt is { } fr ? fr.ToUnixTimeMilliseconds() : DBNull.Value);
            cmd.Parameters.AddWithValue("$sr", u.SevenDay?.ResetsAt is { } sr ? sr.ToUnixTimeMilliseconds() : DBNull.Value);
            cmd.ExecuteNonQuery();
            _lastAppendMs = ms;
        }
        catch { }
    }

    /// <summary>Samples in [fromUtc, toUtc], downsampled to ~maxPoints by averaging.</summary>
    public List<PctPoint> QueryPercent(DateTime fromUtc, DateTime toUtc, int maxPoints)
    {
        var raw = new List<PctPoint>();
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT ts, five_pct, seven_pct FROM usage_samples WHERE ts>=$from AND ts<=$to ORDER BY ts;";
            cmd.Parameters.AddWithValue("$from", ToMs(fromUtc));
            cmd.Parameters.AddWithValue("$to", ToMs(toUtc));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                double? f = r.IsDBNull(1) ? null : r.GetDouble(1);
                double? s = r.IsDBNull(2) ? null : r.GetDouble(2);
                raw.Add(new PctPoint(FromMs(r.GetInt64(0)), f, s));
            }
        }
        catch { }
        return Downsample(raw, maxPoints);
    }

    /// <summary>Recent raw samples (no downsample) for slope/rate estimation.</summary>
    public List<PctPoint> RecentForRate(DateTime sinceUtc)
    {
        var list = new List<PctPoint>();
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT ts, five_pct, seven_pct FROM usage_samples WHERE ts>=$from ORDER BY ts;";
            cmd.Parameters.AddWithValue("$from", ToMs(sinceUtc));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                double? f = r.IsDBNull(1) ? null : r.GetDouble(1);
                double? s = r.IsDBNull(2) ? null : r.GetDouble(2);
                list.Add(new PctPoint(FromMs(r.GetInt64(0)), f, s));
            }
        }
        catch { }
        return list;
    }

    public void Prune(int days = 40)
    {
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM usage_samples WHERE ts < $cut;";
            cmd.Parameters.AddWithValue("$cut", ToMs(DateTime.UtcNow.AddDays(-days)));
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    public int Count()
    {
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM usage_samples;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return -1; }
    }

    private static List<PctPoint> Downsample(List<PctPoint> raw, int maxPoints)
    {
        if (raw.Count <= maxPoints || maxPoints <= 0) return raw;
        var result = new List<PctPoint>(maxPoints);
        double bucket = (double)raw.Count / maxPoints;
        for (int b = 0; b < maxPoints; b++)
        {
            int start = (int)(b * bucket);
            int end = (int)((b + 1) * bucket);
            if (end <= start) end = start + 1;
            if (start >= raw.Count) break;
            end = Math.Min(end, raw.Count);

            double fSum = 0, sSum = 0; int fN = 0, sN = 0;
            long tsSum = 0; int tsN = 0;
            for (int i = start; i < end; i++)
            {
                tsSum += ToMs(raw[i].TsUtc); tsN++;
                if (raw[i].FivePct is { } f) { fSum += f; fN++; }
                if (raw[i].SevenPct is { } s) { sSum += s; sN++; }
            }
            if (tsN == 0) continue;
            result.Add(new PctPoint(
                FromMs(tsSum / tsN),
                fN > 0 ? fSum / fN : null,
                sN > 0 ? sSum / sN : null));
        }
        return result;
    }
}
