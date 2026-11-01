using ClaudeBarWin.Services;

namespace ClaudeBarWin.Config;

/// <summary>
/// Guarda UN valor en <c>config.json</c> sin tocar el disco en el hilo de quien llama: <see cref="Save"/> lo deja pendiente
/// y vuelve al momento; un trabajo del pool relee el archivo, aplica el ÚLTIMO pendiente (varios seguidos = una escritura) y
/// lo guarda, todo bajo <see cref="AppConfig.FileGate"/> para que ningún otro guardado se cuele entre la lectura y la
/// escritura. Si el archivo no se puede leer ahora (antivirus, OneDrive), no se escribe encima con valores por defecto: se
/// registra y el siguiente <see cref="Save"/> lo vuelve a intentar.
/// </summary>
public sealed class BackgroundConfigWriter<T> where T : class
{
    private readonly string _path;
    private readonly Action<AppConfig, T> _apply;
    private readonly object _gate = new();
    private T? _pending;
    private bool _draining;                      // hay un trabajo que aún mirará _pending antes de acabar
    private Task _worker = Task.CompletedTask;

    public BackgroundConfigWriter(string path, Action<AppConfig, T> apply)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    /// <summary>Deja <paramref name="value"/> para guardar (sustituye a lo que aún no se hubiera escrito). Nunca espera.</summary>
    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            _pending = value;
            if (_draining) return;
            _draining = true;
            _worker = Task.Run(Drain);
        }
    }

    /// <summary>Termina cuando no queda nada por escribir (tests y salida ordenada).</summary>
    public Task WhenIdle()
    {
        lock (_gate) return _worker;
    }

    private void Drain()
    {
        while (true)
        {
            T value;
            lock (_gate)
            {
                if (_pending is null)
                {
                    _draining = false;
                    return;
                }
                value = _pending;
                _pending = null;
            }
            try
            {
                lock (AppConfig.FileGate)
                {
                    var c = AppConfig.LoadOrThrow(_path);
                    _apply(c, value);
                    c.SaveOrThrow(_path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"config: no se pudo guardar en segundo plano: {ex.Message}");
            }
        }
    }
}