using System.Buffers;
using System.Text.Json;
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Reads Claude Code transcript files (~/.claude/projects/**/*.jsonl) and extracts
/// per-turn token usage.
///
/// v0.3.10 — índice INCREMENTAL en memoria (auditoría cruzada Claude + Codex): antes cada refresco
/// (60 s) y cada recarga de la gráfica abría cada .jsonl reciente desde el byte 0 y lo volvía a
/// leer/parsear entero (en la máquina de Yovan: 63 MB para 7 días, ~300 MB para 30 días, y además en
/// cada evento de hook con el panel abierto). Ahora se recuerda por archivo hasta qué byte se leyó
/// (siempre justo tras un '\n') y los registros ya extraídos; en cada llamada solo se lee lo AÑADIDO.
/// Si el archivo encoge o se reemplaza (otra fecha de creación) se reindexa desde 0. Una línea sin
/// '\n' final (Claude Code escribiéndola) no se consume hasta que esté completa.
/// Thread-safe: las lecturas se serializan (refresco + gráfica pueden pedir a la vez).
/// </summary>
public sealed class TranscriptParser
{
    private readonly string _projectsRoot;
    private readonly object _gate = new();
    private readonly Dictionary<string, FileIndex> _index = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Archivos más viejos que esto salen del índice. Alineado con la ventana máxima configurable
    /// (AppConfig.MaxSpendWindowDays = 90) + margen: con 35 días fijos, una ventana de 90 re-leía entero
    /// en cada tick todo lo que tuviera entre 35 y 90 días (CR-03 de la 2ª revisión de Codex).
    /// </summary>
    private static readonly TimeSpan EvictAfter = TimeSpan.FromDays(ClaudeBarWin.Config.AppConfig.MaxSpendWindowDays + 5);

    /// <summary>
    /// Una línea final sin '\n' en un archivo que lleva este tiempo sin tocarse se da por completa
    /// (sesión terminada cuyo último registro no llevó salto de línea).
    /// </summary>
    private static readonly TimeSpan SettledAfter = TimeSpan.FromMinutes(10);

    private static readonly byte[] UsageToken = "\"usage\""u8.ToArray();
    private static readonly byte[] AssistantToken = "\"assistant\""u8.ToArray();

    public TranscriptParser(string? projectsRoot = null)
    {
        _projectsRoot = projectsRoot ?? ClaudePaths.ProjectsDir;
    }

    public string ProjectsRoot => _projectsRoot;

    /// <summary>Número de archivos en el índice (diagnóstico/tests).</summary>
    internal int IndexedFiles { get { lock (_gate) return _index.Count; } }

    /// <summary>Bytes leídos del disco en la última llamada a <see cref="Read"/> (tests de incrementalidad).</summary>
    internal long LastBytesRead { get; private set; }

    private sealed class FileIndex
    {
        public long Offset;
        public DateTime CreationUtc;
        public readonly List<(string? Key, UsageRecord Rec)> Records = new();

        public void Reset(DateTime creationUtc)
        {
            Offset = 0;
            CreationUtc = creationUtc;
            Records.Clear();
        }
    }

    public IReadOnlyList<UsageRecord> Read(DateTime sinceUtc)
    {
        var records = new List<UsageRecord>();
        if (!Directory.Exists(_projectsRoot)) return records;

        lock (_gate)
        {
            LastBytesRead = 0;
            // Grace period: a file's mtime is its last line; older writes can still hold in-window lines.
            var fileCutoff = sinceUtc.AddHours(-2);
            var evictBefore = DateTime.UtcNow - EvictAfter;
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(_projectsRoot, "*.jsonl", SearchOption.AllDirectories); }
            catch { return records; }

            foreach (var file in files)
            {
                DateTime mtime, ctime;
                try
                {
                    mtime = File.GetLastWriteTimeUtc(file);
                    ctime = File.GetCreationTimeUtc(file);
                }
                catch { continue; }
                present.Add(file);
                if (mtime < fileCutoff) continue;

                if (!_index.TryGetValue(file, out var idx))
                {
                    idx = new FileIndex { CreationUtc = ctime };
                    _index[file] = idx;
                }
                else if (idx.CreationUtc != ctime)
                {
                    idx.Reset(ctime); // reemplazado por otro archivo con el mismo nombre
                }

                try { LastBytesRead += ReadAppended(file, idx, DateTime.UtcNow - mtime >= SettledAfter); }
                catch { /* locked or unreadable file: se reintenta en la próxima llamada */ }

                foreach (var (key, rec) in idx.Records)
                {
                    if (rec.TimestampUtc < sinceUtc) continue;
                    // Dedup by message id + requestId (streaming / sesiones reanudadas repiten turnos).
                    if (key is not null && !seen.Add(key)) continue;
                    records.Add(rec);
                }
            }

            // Poda del índice: archivos borrados o ya fuera de cualquier ventana.
            foreach (var stale in _index.Keys.Where(k => !present.Contains(k) || SafeMtime(k) < evictBefore).ToList())
                _index.Remove(stale);
        }

        return records;
    }

    private static DateTime SafeMtime(string file)
    {
        try { return File.GetLastWriteTimeUtc(file); } catch { return DateTime.MinValue; }
    }

    /// <summary>Lee desde idx.Offset hasta el último '\n' completo y añade los registros. Devuelve bytes leídos.</summary>
    private static long ReadAppended(string file, FileIndex idx, bool settled)
    {
        // Open with shared read so we don't fight Claude Code writing the file.
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.SequentialScan);
        long len = fs.Length;
        if (len < idx.Offset) idx.Reset(idx.CreationUtc); // truncado/reescrito: reindexar
        if (len == idx.Offset) return 0;

        fs.Seek(idx.Offset, SeekOrigin.Begin);
        var buf = ArrayPool<byte>.Shared.Rent(1 << 20);
        var carry = new ArrayBufferWriter<byte>();
        long pos = idx.Offset, read = 0;
        try
        {
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
            {
                read += n;
                int start = 0;
                for (int i = Array.IndexOf(buf, (byte)'\n', 0, n); i >= 0; i = Array.IndexOf(buf, (byte)'\n', start, n - start))
                {
                    if (carry.WrittenCount > 0)
                    {
                        carry.Write(buf.AsSpan(start, i - start));
                        ProcessLine(carry.WrittenSpan, idx);
                        carry.Clear();
                    }
                    else
                    {
                        ProcessLine(buf.AsSpan(start, i - start), idx);
                    }
                    start = i + 1;
                    if (start >= n) break;
                }
                if (start < n) carry.Write(buf.AsSpan(start, n - start));
                pos += n;
            }

            // Última línea sin '\n': solo se consume si el archivo está "asentado" Y es un JSON completo. Un
            // fragmento a medio escribir no avanza el offset aunque el mtime sea viejo: si luego se completa,
            // se lee entero (antes se descartaba para siempre — CR-02 de la 2ª revisión de Codex).
            if (carry.WrittenCount > 0 && settled && IsCompleteJson(carry.WrittenSpan))
            {
                ProcessLine(carry.WrittenSpan, idx);
                carry.Clear();
            }
            idx.Offset = pos - carry.WrittenCount;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
        return read;
    }

    private static bool IsCompleteJson(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8);
            using var _ = JsonDocument.ParseValue(ref reader);
            return true;
        }
        catch { return false; }
    }

    private static void ProcessLine(ReadOnlySpan<byte> line, FileIndex idx)
    {
        if (line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];
        if (line.Length < 40) return;
        if (line.IndexOf(UsageToken) < 0) return;
        if (line.IndexOf(AssistantToken) < 0) return;

        var parsed = TryParseLine(line);
        if (parsed is { } p) idx.Records.Add(p);
    }

    internal static (string? Key, UsageRecord Rec)? TryParseLine(ReadOnlySpan<byte> utf8Line)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Line);
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String
                || typeEl.GetString() != "assistant")
                return null;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
                return null;
            if (!msg.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("timestamp", out var tsEl) || tsEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(tsEl.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var tso))
                return null;

            var tsUtc = tso.UtcDateTime;

            string msgId = msg.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() ?? "" : "";
            string reqId = root.TryGetProperty("requestId", out var rqEl) && rqEl.ValueKind == JsonValueKind.String ? rqEl.GetString() ?? "" : "";
            string key = msgId + "|" + reqId;

            string model = msg.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String ? mEl.GetString() ?? "" : "";

            long input = GetLong(usage, "input_tokens");
            long output = GetLong(usage, "output_tokens");
            long cacheRead = GetLong(usage, "cache_read_input_tokens");

            long c5, c1 = 0;
            if (usage.TryGetProperty("cache_creation", out var cc) && cc.ValueKind == JsonValueKind.Object)
            {
                c5 = GetLong(cc, "ephemeral_5m_input_tokens");
                c1 = GetLong(cc, "ephemeral_1h_input_tokens");
            }
            else
            {
                c5 = GetLong(usage, "cache_creation_input_tokens");
            }

            return (key.Length > 1 ? key : null, new UsageRecord(tsUtc, model, input, output, c5, c1, cacheRead));
        }
        catch
        {
            return null;
        }
    }

    private static long GetLong(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;
}
