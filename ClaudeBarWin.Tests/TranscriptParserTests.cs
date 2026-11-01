using System.Text;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// v0.3.10: el parser no tenía NINGÚN test y es el que produce el gasto estimado y la gráfica.
/// Cubre el índice incremental (solo se lee lo añadido), líneas a medio escribir, truncado,
/// dedup entre archivos y el filtro por fecha. Todo en un directorio temporal propio.
/// </summary>
public sealed class TranscriptParserTests : IDisposable
{
    private readonly string _root;
    private static readonly DateTime T0 = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    public TranscriptParserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "claudebar-tests", "parser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "proj-a"));
        Directory.CreateDirectory(Path.Combine(_root, "proj-b"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string Line(string msgId, string reqId, DateTime ts, long input = 10, long output = 20,
        string model = "claude-opus-5-5") =>
        $"{{\"type\":\"assistant\",\"requestId\":\"{reqId}\",\"timestamp\":\"{ts:yyyy-MM-ddTHH:mm:ss.fffZ}\"," +
        $"\"message\":{{\"id\":\"{msgId}\",\"model\":\"{model}\",\"usage\":{{\"input_tokens\":{input},\"output_tokens\":{output}," +
        $"\"cache_read_input_tokens\":5,\"cache_creation\":{{\"ephemeral_5m_input_tokens\":3,\"ephemeral_1h_input_tokens\":2}}}}}}}}";

    private string Write(string rel, string content, bool append = false)
    {
        var p = Path.Combine(_root, rel);
        if (append) File.AppendAllText(p, content, new UTF8Encoding(false));
        else File.WriteAllText(p, content, new UTF8Encoding(false));
        return p;
    }

    [Fact]
    public void Parses_assistant_usage_lines_and_ignores_the_rest()
    {
        Write("proj-a/s1.jsonl",
            Line("m1", "r1", T0) + "\n" +
            "{\"type\":\"user\",\"message\":{\"content\":\"hola, esto no cuenta aunque diga usage y assistant\"}}\n" +
            "no-json-pero-largo-de-verdad-para-pasar-el-filtro-de-longitud \"usage\" \"assistant\"\n" +
            Line("m2", "r2", T0.AddMinutes(1), input: 100, output: 200) + "\n");

        var recs = new TranscriptParser(_root).Read(T0.AddHours(-1));

        Assert.Equal(2, recs.Count);
        var r2 = recs.Single(r => r.InputTokens == 100);
        Assert.Equal(200, r2.OutputTokens);
        Assert.Equal(5, r2.CacheReadTokens);
        Assert.Equal(3, r2.CacheCreate5mTokens);
        Assert.Equal(2, r2.CacheCreate1hTokens);
        Assert.Equal("claude-opus-5-5", r2.Model);
        Assert.Equal(DateTimeKind.Utc, r2.TimestampUtc.Kind);
    }

    [Fact]
    public void Second_read_only_reads_the_appended_bytes()
    {
        var p = Write("proj-a/s1.jsonl", Line("m1", "r1", T0) + "\n" + Line("m2", "r2", T0) + "\n");
        var parser = new TranscriptParser(_root);

        Assert.Equal(2, parser.Read(T0.AddHours(-1)).Count);
        Assert.Equal(new FileInfo(p).Length, parser.LastBytesRead);

        // Sin cambios: 0 bytes leídos y mismo resultado (antes se releía el archivo entero cada vez).
        Assert.Equal(2, parser.Read(T0.AddHours(-1)).Count);
        Assert.Equal(0, parser.LastBytesRead);

        var extra = Line("m3", "r3", T0) + "\n";
        Write("proj-a/s1.jsonl", extra, append: true);
        Assert.Equal(3, parser.Read(T0.AddHours(-1)).Count);
        Assert.Equal(Encoding.UTF8.GetByteCount(extra), parser.LastBytesRead);
    }

    [Fact]
    public void Half_written_last_line_is_not_consumed_until_complete()
    {
        var full = Line("m2", "r2", T0);
        Write("proj-a/s1.jsonl", Line("m1", "r1", T0) + "\n" + full[..30]);
        var parser = new TranscriptParser(_root);

        Assert.Single(parser.Read(T0.AddHours(-1)));

        Write("proj-a/s1.jsonl", full[30..] + "\n", append: true);
        var recs = parser.Read(T0.AddHours(-1));
        Assert.Equal(2, recs.Count);
    }

    [Fact]
    public void Truncated_or_rewritten_file_is_reindexed_from_zero()
    {
        Write("proj-a/s1.jsonl", Line("m1", "r1", T0) + "\n" + Line("m2", "r2", T0) + "\n");
        var parser = new TranscriptParser(_root);
        Assert.Equal(2, parser.Read(T0.AddHours(-1)).Count);

        Write("proj-a/s1.jsonl", Line("m9", "r9", T0, input: 999) + "\n"); // más corto que el offset guardado
        var recs = parser.Read(T0.AddHours(-1));
        Assert.Single(recs);
        Assert.Equal(999, recs[0].InputTokens);
    }

    [Fact]
    public void Duplicate_turns_across_files_count_once()
    {
        // Una sesión reanudada copia el historial en otro .jsonl: mismo message.id + requestId.
        Write("proj-a/s1.jsonl", Line("m1", "r1", T0) + "\n");
        Write("proj-b/s2.jsonl", Line("m1", "r1", T0) + "\n" + Line("m2", "r2", T0) + "\n");

        var parser = new TranscriptParser(_root);
        Assert.Equal(2, parser.Read(T0.AddHours(-1)).Count);
        Assert.Equal(2, parser.Read(T0.AddHours(-1)).Count); // la dedup se recalcula por llamada, no acumula
    }

    [Fact]
    public void Since_filter_is_applied_per_call_over_the_same_index()
    {
        Write("proj-a/s1.jsonl",
            Line("old", "r0", T0.AddDays(-3)) + "\n" + Line("new", "r1", T0) + "\n");
        var parser = new TranscriptParser(_root);

        Assert.Single(parser.Read(T0.AddHours(-1)));
        Assert.Equal(2, parser.Read(T0.AddDays(-4)).Count);
        Assert.Single(parser.Read(T0.AddHours(-1)));
    }

    [Fact]
    public void Deleted_files_leave_the_index()
    {
        var p = Write("proj-a/s1.jsonl", Line("m1", "r1", T0) + "\n");
        var parser = new TranscriptParser(_root);
        parser.Read(T0.AddHours(-1));
        Assert.Equal(1, parser.IndexedFiles);

        File.Delete(p);
        Assert.Empty(parser.Read(T0.AddHours(-1)));
        Assert.Equal(0, parser.IndexedFiles);
    }

    [Fact]
    public void Crlf_lines_and_missing_root_are_handled()
    {
        Write("proj-a/s1.jsonl", Line("m1", "r1", T0) + "\r\n");
        Assert.Single(new TranscriptParser(_root).Read(T0.AddHours(-1)));
        Assert.Empty(new TranscriptParser(Path.Combine(_root, "no-existe")).Read(T0));
    }
}
