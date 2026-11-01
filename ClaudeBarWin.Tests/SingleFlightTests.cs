using ClaudeBarWin.Services;

/// <summary>
/// Un cambio en segundo plano a la vez y sin que quien lo pide espere (edición de la Store: encender o apagar la tarea de
/// inicio, que es WinRT y puede tardar segundos; antes bloqueaba el hilo de UI hasta 10 s).
/// </summary>
public class SingleFlightTests
{
    [Fact]
    public async Task Starting_never_waits_for_the_work()
    {
        var flight = new SingleFlight();
        var gate = new TaskCompletionSource();
        int changes = 0;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(flight.TryStart(() => gate.Task, () => Interlocked.Increment(ref changes)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.True(flight.Busy);
        Assert.True(Volatile.Read(ref changes) >= 1);   // «cambiando…» en cuanto empieza

        gate.SetResult();
        Assert.True(flight.WaitIdle(TimeSpan.FromSeconds(5)));
        await Task.Delay(50);
        Assert.False(flight.Busy);
        Assert.Equal(2, Volatile.Read(ref changes));    // y otra vez al acabar
    }

    [Fact]
    public void Only_one_change_at_a_time()
    {
        var flight = new SingleFlight();
        var gate = new TaskCompletionSource();
        Assert.True(flight.TryStart(() => gate.Task));
        Assert.False(flight.TryStart(() => Task.CompletedTask));   // el segundo clic no se encola
        gate.SetResult();
        Assert.True(flight.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => !flight.Busy, TimeSpan.FromSeconds(5)));
        Assert.True(flight.TryStart(() => Task.CompletedTask));    // acabado el primero, vale otro
        Assert.True(flight.WaitIdle(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_failing_change_frees_the_slot_and_still_reports()
    {
        var flight = new SingleFlight();
        int changes = 0;
        Assert.True(flight.TryStart(() => throw new InvalidOperationException("WinRT"), () => Interlocked.Increment(ref changes)));
        Assert.True(flight.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => !flight.Busy && Volatile.Read(ref changes) == 2, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_failing_listener_does_not_break_the_change()
    {
        var flight = new SingleFlight();
        bool ran = false;
        Assert.True(flight.TryStart(() => { ran = true; return Task.CompletedTask; }, () => throw new Exception("repintar")));
        Assert.True(flight.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => !flight.Busy, TimeSpan.FromSeconds(5)));
        Assert.True(ran);
    }

    [Fact]
    public void Waiting_with_nothing_in_flight_returns_at_once() => Assert.True(new SingleFlight().WaitIdle(TimeSpan.Zero));
}
