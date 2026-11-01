using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests;

/// <summary><see cref="BackgroundConfigWriter{T}"/>: guarda un valor en <c>config.json</c> fuera del hilo de UI (nunca
/// espera al disco), junta los seguidos en una escritura con el último y no pisa lo demás del archivo.</summary>
public sealed class BackgroundConfigWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-writer-" + Guid.NewGuid().ToString("N"));
    private string Path_ => Path.Combine(_dir, "config.json");

    public BackgroundConfigWriterTests()
    {
        Directory.CreateDirectory(_dir);
        new AppConfig { NotchMusic = false }.Normalize().SaveOrThrow(Path_);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Save_returns_at_once_even_while_the_file_is_busy_and_the_last_value_wins()
    {
        var writer = new BackgroundConfigWriter<UsageViewState>(Path_, (c, v) => c.NotchUsageView = v);
        var first = new UsageViewState(UsageChartMode.Quota, QuotaFocus.Five, ChartRange.Hours5);
        var last = new UsageViewState(UsageChartMode.Quota, QuotaFocus.Seven, ChartRange.Week1);
        Task idle;
        lock (AppConfig.FileGate)   // otro guardado en curso: Save no puede esperarlo
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            writer.Save(first);
            writer.Save(last);
            idle = writer.WhenIdle();
            Assert.True(sw.ElapsedMilliseconds < 200, $"Save bloqueó {sw.ElapsedMilliseconds} ms");
            Assert.False(idle.IsCompleted);
        }
        await idle.WaitAsync(TimeSpan.FromSeconds(10));
        var back = AppConfig.LoadOrThrow(Path_);
        Assert.Equal(last, back.NotchUsageView);
        Assert.False(back.NotchMusic);   // lo demás del archivo, intacto
    }
}
