using System.Diagnostics;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>
/// Hilo de fondo STA con una cola (F5 · B4: miniaturas de la bandeja). STA porque los extractores de miniaturas del shell
/// suelen ser de apartamento: pedidos desde el MTA, COM los crearía en otro hilo y la llamada saltaría de uno a otro. Se crea
/// al primer trabajo y termina solo tras <see cref="IdleExit"/> sin nada que hacer (quieto, ni hilo). Un trabajo que lanza
/// se anota y la cola sigue. Los trabajos van de uno en uno, en orden.
/// <para>Vigilante: un trabajo que pasa de <see cref="HangAfter"/> (un extractor de terceros colgado, una unidad que no
/// contesta) se abandona en su hilo, que no se puede matar, y la cola sigue en un hilo STA nuevo; lo que devuelva al final lo
/// descarta quien lo pidió (<see cref="Features.ShelfThumbnails"/>, por generación). Como mucho <see cref="MaxAbandoned"/>
/// hilos abandonados a la vez: con el tope lleno, la cola espera a que vuelva alguno. El vigilante es un temporizador de un
/// disparo armado solo mientras hay un trabajo en marcha (quieto, 0 % CPU).</para>
/// </summary>
public sealed class StaWorker : IDisposable
{
    /// <summary>Espera sin trabajo antes de que el hilo termine.</summary>
    public static readonly TimeSpan IdleExit = TimeSpan.FromSeconds(30);

    /// <summary>Lo que puede durar un trabajo antes de darlo por colgado.</summary>
    public static readonly TimeSpan HangAfter = TimeSpan.FromSeconds(10);

    /// <summary>Hilos abandonados (aún dentro de su trabajo colgado) que se toleran a la vez.</summary>
    public const int MaxAbandoned = 4;

    private readonly object _gate = new();
    private readonly Queue<Action> _queue = new();
    private readonly string _name;
    private readonly TimeSpan _idleExit, _hangAfter;
    private readonly int _maxAbandoned;
    private readonly System.Threading.Timer _watchdog;
    private Lane? _lane;            // el hilo que atiende la cola ahora
    private int _abandoned;         // hilos abandonados que aún no han vuelto de su trabajo
    private bool _disposed;

    /// <summary>Un hilo de la cola y su trabajo en marcha (todo bajo <see cref="_gate"/>).</summary>
    private sealed class Lane
    {
        public bool Busy, Abandoned;
        public long Started;        // Stopwatch.GetTimestamp() al empezar el trabajo en marcha
    }

    public StaWorker(string name) : this(name, IdleExit, HangAfter, MaxAbandoned) { }

    /// <summary>Con la espera antes de terminar, el plazo del vigilante y el tope de abandonados inyectados (tests).</summary>
    internal StaWorker(string name, TimeSpan idleExit, TimeSpan hangAfter, int maxAbandoned = MaxAbandoned)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _idleExit = idleExit;
        _hangAfter = hangAfter;
        _maxAbandoned = Math.Max(1, maxAbandoned);
        _watchdog = new System.Threading.Timer(_ => Watch(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>¿Hay un hilo atendiendo la cola? (tests).</summary>
    internal bool Running
    {
        get { lock (_gate) return _lane is not null; }
    }

    /// <summary>Hilos abandonados que aún no han vuelto (tests).</summary>
    internal int Abandoned
    {
        get { lock (_gate) return _abandoned; }
    }

    /// <summary>Encola <paramref name="work"/>. Desde cualquier hilo. Tras <see cref="Dispose"/> no hace nada.</summary>
    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            if (_disposed) return;
            _queue.Enqueue(work);
            if (_lane is not null) Monitor.PulseAll(_gate);
            else StartLane();
        }
    }

    /// <summary>Descarta lo pendiente; lo que esté en marcha termina y su hilo sale.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _queue.Clear();
            Monitor.PulseAll(_gate);
            _watchdog.Dispose();
        }
    }

    // Bajo _gate.
    private void StartLane()
    {
        var lane = new Lane();
        var thread = new Thread(() => Loop(lane)) { IsBackground = true, Name = _name };
        thread.SetApartmentState(ApartmentState.STA);
        _lane = lane;
        thread.Start();
    }

    private void Loop(Lane lane)
    {
        while (true)
        {
            Action work;
            lock (_gate)
            {
                if (lane.Abandoned)
                {
                    // Volvió de un trabajo que ya se dio por colgado: otro hilo lleva la cola (o la llevará).
                    _abandoned--;
                    if (!_disposed && _lane is null && _queue.Count > 0) StartLane();
                    return;
                }
                while (_queue.Count == 0)
                {
                    // Sin trabajo durante la espera (o cerrado): el hilo sale y el siguiente Post crea otro.
                    if (_disposed || (!Monitor.Wait(_gate, _idleExit) && _queue.Count == 0))
                    {
                        if (_lane == lane) _lane = null;
                        return;
                    }
                }
                work = _queue.Dequeue();
                lane.Busy = true;
                lane.Started = Stopwatch.GetTimestamp();
                _watchdog.Change(_hangAfter, Timeout.InfiniteTimeSpan);
            }
            try { work(); }
            catch (Exception ex)
            {
                AppLog.Warn($"{_name}: {ex.GetType().Name}: {ex.Message}");
            }
            lock (_gate)
            {
                lane.Busy = false;
                if (_lane == lane && !_disposed) _watchdog.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    /// <summary>Disparo del vigilante (hilo del sistema): si el trabajo en marcha pasa del plazo, se abandona su hilo y la cola
    /// sigue en otro; con el tope de abandonados lleno, se vuelve a mirar dentro de otro plazo.</summary>
    private void Watch()
    {
        lock (_gate)
        {
            var lane = _lane;
            if (_disposed || lane is null || !lane.Busy) return;
            var busy = Stopwatch.GetElapsedTime(lane.Started);
            if (busy < _hangAfter)
            {
                _watchdog.Change(_hangAfter - busy, Timeout.InfiniteTimeSpan);   // disparo de un trabajo anterior
                return;
            }
            if (_abandoned >= _maxAbandoned)
            {
                _watchdog.Change(_hangAfter, Timeout.InfiniteTimeSpan);
                return;
            }
            lane.Abandoned = true;
            _abandoned++;
            _lane = null;
            AppLog.Warn($"{_name}: un trabajo lleva más de {_hangAfter.TotalSeconds:0} s; la cola sigue en otro hilo");
            if (_queue.Count > 0) StartLane();
        }
    }
}
