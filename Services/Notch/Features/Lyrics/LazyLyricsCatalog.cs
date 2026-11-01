namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// El catálogo de letras de la app (letras v2 · C): un <see cref="LyricsAggregator"/> con LRCLIB, la única fuente (NetEase y
/// Musixmatch se retiraron el 29-oct), que se crea al PRIMER uso (nada de <c>HttpClient</c> ni red si nunca se abre la
/// vista de letra) con <see cref="TrackNameParser.Candidates"/>. LRCLIB devuelve varias letras por búsqueda y no todas son
/// de la canción buscada: las de otra se descartan con <see cref="LyricsMatch"/> y las demás llegan a «Otra letra».
/// Liberado: <see cref="LyricsResult.Error"/> y ningún candidato.
/// </summary>
public sealed class LazyLyricsCatalog : ILyricsCatalog, IDisposable
{
    private readonly Func<IReadOnlyList<ILyricsProvider>> _factory;
    private readonly object _gate = new();
    private LyricsAggregator? _aggregator;
    private IReadOnlyList<ILyricsProvider> _providers = Array.Empty<ILyricsProvider>();
    private bool _disposed;

    public LazyLyricsCatalog()
        : this(CreateProviders)
    {
    }

    /// <summary>Para tests: fábrica de fuentes inyectable (dobles sin red).</summary>
    internal LazyLyricsCatalog(Func<IReadOnlyList<ILyricsProvider>> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>Las fuentes de la app: solo LRCLIB. Crearla no sale a la red (el cliente comprueba su host antes de cada
    /// petición).</summary>
    public static IReadOnlyList<ILyricsProvider> CreateProviders() => new ILyricsProvider[] { new LrclibProvider() };

    /// <summary>¿Vale lo guardado de esta fuente (<see cref="CachedLyrics.Source"/>, <see cref="LyricsChoice.Source"/>)?
    /// Solo lo de LRCLIB: una letra o una elección de NetEase o Musixmatch (de versiones anteriores) cuenta como si no
    /// hubiera nada, así que no se enseña y la letra se vuelve a pedir a LRCLIB. PURO.</summary>
    public static bool Supports(string? source) => source == LrclibProvider.SourceName;

    public Task<LyricsResult> FetchAsync(TrackQuery q, CancellationToken ct) =>
        Current() is { } agg ? agg.FetchAsync(q, ct) : Task.FromResult(LyricsResult.Error);

    public Task<IReadOnlyList<LyricsCandidate>> AllCandidatesAsync(TrackQuery q, CancellationToken ct) =>
        Current() is { } agg
            ? agg.AllCandidatesAsync(q, ct)
            : Task.FromResult<IReadOnlyList<LyricsCandidate>>(Array.Empty<LyricsCandidate>());

    public void Dispose()
    {
        IReadOnlyList<ILyricsProvider> old;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            old = _providers;
            _providers = Array.Empty<ILyricsProvider>();
            _aggregator = null;
        }
        foreach (var p in old)
            (p as IDisposable)?.Dispose();
    }

    private LyricsAggregator? Current()
    {
        lock (_gate)
        {
            if (_disposed) return null;
            if (_aggregator is null)
            {
                _providers = _factory() ?? Array.Empty<ILyricsProvider>();
                _aggregator = new LyricsAggregator(_providers, TrackNameParser.Candidates, LyricsAggregator.DefaultPerProviderTimeout,
                    LyricsMatch.IsMatch);
            }
            return _aggregator;
        }
    }
}
