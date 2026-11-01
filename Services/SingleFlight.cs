namespace ClaudeBarWin.Services;

/// <summary>
/// Un trabajo en segundo plano a la vez, sin que quien lo pide espere nunca (lo pide el hilo de UI). Mientras uno está en
/// marcha, <see cref="TryStart"/> rechaza el siguiente en vez de encolarlo: un segundo clic sobre un interruptor que está
/// «cambiando…» no hace nada. El aviso <c>changed</c> sale al empezar y al acabar (desde el pool, pase lo que pase), para que
/// la UI repinte con <see cref="Busy"/> al día. Lo usa la tarea de inicio de la edición de la Store (<c>StoreStartupTask</c>).
/// </summary>
internal sealed class SingleFlight
{
    private readonly object _gate = new();
    private Task _current = Task.CompletedTask;
    private bool _busy;

    /// <summary>¿Hay un trabajo en marcha?</summary>
    public bool Busy
    {
        get { lock (_gate) return _busy; }
    }

    /// <summary>Arranca <paramref name="work"/> en el pool si no hay otro en marcha; false si lo hay. Nunca espera.</summary>
    public bool TryStart(Func<Task> work, Action? changed = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            if (_busy) return false;
            _busy = true;
            _current = Task.Run(async () =>
            {
                try { await work().ConfigureAwait(false); }
                catch (Exception ex) { AppLog.Warn($"cambio en segundo plano: {ex.GetBaseException().Message}"); }
                finally
                {
                    lock (_gate) _busy = false;
                    Notify(changed);
                }
            });
        }
        Notify(changed);
        return true;
    }

    /// <summary>Espera, como mucho <paramref name="timeout"/>, a que acabe lo que esté en marcha (salida ordenada, CLI).</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        Task current;
        lock (_gate) current = _current;
        try { return current.Wait(timeout); }
        catch (AggregateException) { return true; }   // ya se anotó dentro
    }

    private static void Notify(Action? changed)
    {
        try { changed?.Invoke(); }
        catch (Exception ex) { AppLog.Warn($"cambio en segundo plano: el aviso falló: {ex.Message}"); }
    }
}
