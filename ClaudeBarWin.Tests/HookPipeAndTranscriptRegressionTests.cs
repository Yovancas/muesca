using System.IO.Pipes;
using System.Reflection;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Regresiones de HookPipeServer (slots y orden de eventos) y de TranscriptParser (líneas parciales e
/// índice de la ventana de 90 días). Nacieron como tests adversariales de la 2ª inspección de Codex
/// (2026-09-26, hallazgos CR-01, CR-02, CR-03 y CR-06): fallaban de forma determinista sobre la primera
/// versión de esos arreglos y se conservan como regresión (con la espera intermedia del de orden
/// relajada: ahora cada línea se despacha al llegar).
/// </summary>
public sealed class HookPipeAndTranscriptRegressionTests
{
    private static string UsageLine(string id, DateTime ts) =>
        $"{{\"type\":\"assistant\",\"requestId\":\"r-{id}\",\"timestamp\":\"{ts:O}\",\"message\":{{\"id\":\"{id}\",\"model\":\"claude-opus-5-5\",\"usage\":{{\"input_tokens\":1,\"output_tokens\":2}}}}}}";

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 3000)
    {
        var stop = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!predicate() && DateTime.UtcNow < stop)
            await Task.Delay(10);
        Assert.True(predicate(), "La condicion no se cumplio dentro del timeout");
    }

    [Fact]
    public async Task Stop_after_accept_wait_must_restore_the_acquired_slot()
    {
        using var server = new HookPipeServer("claudebar-codex-slot-" + Guid.NewGuid().ToString("N"));
        var field = typeof(HookPipeServer).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var slots = (SemaphoreSlim)field.GetValue(server)!;

        server.Start();
        await WaitUntilAsync(() => slots.CurrentCount == HookPipeServer.MaxConcurrent - 1);
        server.Stop();

        // Antes: Task.Delay(200) fijo. Ahora se espera a la condición (con timeout) y se comprueba igual.
        await WaitUntilAsync(() => slots.CurrentCount == HookPipeServer.MaxConcurrent);
        Assert.Equal(HookPipeServer.MaxConcurrent, slots.CurrentCount);
    }

    [Fact]
    public async Task Older_pipe_event_must_not_override_a_newer_event_from_the_same_session()
    {
        string name = "claudebar-codex-order-" + Guid.NewGuid().ToString("N");
        using var server = new HookPipeServer(name);
        var store = new SessionStore();
        var now = DateTime.UtcNow;
        store.Apply(new HookEvent
        {
            SessionId = "s", Cwd = "c", Event = "UserPromptSubmit", Status = "processing", Ts = 0,
        }, now);
        int received = 0;
        server.EventReceived += e =>
        {
            store.Apply(e, now.AddSeconds(e.Ts));
            Interlocked.Increment(ref received);
        };
        server.Start();

        using var older = new NamedPipeClientStream(".", name, PipeDirection.Out);
        await older.ConnectAsync(2000);
        var olderWriter = new StreamWriter(older) { AutoFlush = true };
        await olderWriter.WriteLineAsync("""{"session_id":"s","cwd":"c","event":"PermissionRequest","status":"waiting_for_approval","ts":1}""");
        // Mantener la primera conexion abierta (antes su handler esperaba EOF y aplicaba DESPUÉS).

        using (var newer = new NamedPipeClientStream(".", name, PipeDirection.Out))
        {
            await newer.ConnectAsync(2000);
            using var newerWriter = new StreamWriter(newer) { AutoFlush = true };
            await newerWriter.WriteLineAsync("""{"session_id":"s","cwd":"c","event":"PostToolUse","status":"processing","ts":2}""");
        }
        await WaitUntilAsync(() => Volatile.Read(ref received) >= 1);

        olderWriter.Dispose();
        await WaitUntilAsync(() => Volatile.Read(ref received) == 2);

        Assert.Equal(SessionPhase.Processing, store.Snapshot().Single().Phase);
    }

    [Fact]
    public void Settled_partial_transcript_line_must_not_be_discarded_if_it_is_later_completed()
    {
        string root = Path.Combine(Path.GetTempPath(), "claudebar-codex-parser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ts = DateTime.UtcNow.AddHours(-1);
            string first = UsageLine("m1", ts);
            string second = UsageLine("m2", ts);
            int split = second.Length / 2;
            string path = Path.Combine(root, "s.jsonl");
            File.WriteAllText(path, first + "\n" + second[..split]);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-11));

            var parser = new TranscriptParser(root);
            Assert.Single(parser.Read(ts.AddMinutes(-1)));

            File.AppendAllText(path, second[split..] + "\n");
            Assert.Equal(2, parser.Read(ts.AddMinutes(-1)).Count);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Files_inside_the_90_day_window_stay_indexed_between_calls()
    {
        string root = Path.Combine(Path.GetTempPath(), "claudebar-codex-evict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ts = DateTime.UtcNow.AddDays(-40);
            string path = Path.Combine(root, "old.jsonl");
            File.WriteAllText(path, UsageLine("m1", ts) + "\n");
            File.SetLastWriteTimeUtc(path, ts);

            var parser = new TranscriptParser(root);
            Assert.Single(parser.Read(DateTime.UtcNow.AddDays(-90)));
            Assert.Equal(1, parser.IndexedFiles);
            Assert.Single(parser.Read(DateTime.UtcNow.AddDays(-90)));
            Assert.Equal(0, parser.LastBytesRead); // antes: se re-leía entero en cada llamada
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
