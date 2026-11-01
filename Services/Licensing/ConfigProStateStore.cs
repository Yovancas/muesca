using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services.Licensing;

/// <summary>
/// <see cref="ProSnapshot"/> en <c>config.json</c> (<see cref="AppConfig.ProTrialStartUtc"/>, <see cref="AppConfig.ProLastKnownPro"/>
/// y <see cref="AppConfig.ProEverOwned"/>). Leer debe LANZAR si el archivo existe y no se puede leer (en la app,
/// <see cref="AppConfig.LoadOrThrow"/>): con unos defaults callados se empezaría otra prueba y se pisaría el último estado
/// conocido. Guardar va por el camino de la bandeja (<c>MutateConfig</c>: leer, cambiar y guardar en el hilo de UI) y
/// escribe solo el campo que cambió, así no pisa otros ajustes que se estén guardando a la vez.
/// </summary>
public sealed class ConfigProStateStore : IProStateStore
{
    private readonly Func<AppConfig> _load;
    private readonly Action<Action<AppConfig>> _mutate;

    public ConfigProStateStore(Func<AppConfig> load, Action<Action<AppConfig>> mutate)
    {
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _mutate = mutate ?? throw new ArgumentNullException(nameof(mutate));
    }

    public ProSnapshot Load()
    {
        var c = _load();
        return new ProSnapshot(c.ProTrialStartUtc is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null, c.ProLastKnownPro,
            c.ProEverOwned);
    }

    public void SaveTrialStart(DateTime startUtc) => _mutate(c => c.ProTrialStartUtc = startUtc);

    public void SaveLastKnownPro(bool owns) => _mutate(c =>
    {
        c.ProLastKnownPro = owns;
        if (owns) c.ProEverOwned = true;
    });
}
