using System.Diagnostics;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Sonda MANUAL (no corre en CI): con CLAUDEBAR_REALDATA_OUT=&lt;ruta&gt; lee los transcripts reales del
/// usuario (solo lectura) dos veces y vuelca recuento, tokens y tiempos, para comparar con el parser
/// anterior y medir el ahorro del índice incremental. Sin la variable no hace nada.
/// </summary>
public class TranscriptParserRealDataProbe
{
    [Fact]
    public void Probe_real_transcripts_when_requested()
    {
        var outPath = Environment.GetEnvironmentVariable("CLAUDEBAR_REALDATA_OUT");
        if (string.IsNullOrEmpty(outPath)) return;

        var since = DateTime.UtcNow.AddDays(-7);
        var parser = new TranscriptParser();
        var sw = Stopwatch.StartNew();
        var first = parser.Read(since);
        var t1 = sw.ElapsedMilliseconds; long b1 = parser.LastBytesRead;
        sw.Restart();
        var second = parser.Read(since);
        var t2 = sw.ElapsedMilliseconds; long b2 = parser.LastBytesRead;

        File.WriteAllText(outPath,
            $"since={since:O}\ncount={first.Count}\ntokens={first.Sum(r => r.TotalTokens)}\n" +
            $"input={first.Sum(r => r.InputTokens)} output={first.Sum(r => r.OutputTokens)}\n" +
            $"first_ms={t1} first_bytes={b1}\nsecond_ms={t2} second_bytes={b2} second_count={second.Count}\n");
    }
}
