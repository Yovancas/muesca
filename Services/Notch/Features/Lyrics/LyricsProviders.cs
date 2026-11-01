namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Una letra que ofrece una fuente. <paramref name="Body"/> es LRC si <paramref name="Synced"/>, texto plano si no.
/// <paramref name="Duration"/> es la de la pista según la fuente (<see cref="TimeSpan.Zero"/> si no la da).
/// </summary>
public sealed record LyricsCandidate(string Source, string ExternalId, string Title, string Artist, TimeSpan Duration, bool Synced, string Body);

/// <summary>
/// Una fuente de letras (en la app, solo LRCLIB; en los tests, dobles). <see cref="SearchAsync"/> devuelve todo lo que
/// encuentra para una búsqueda (lista vacía = nada) y lanza si la fuente falla (red, HTTP, respuesta rota): el agregador
/// lo distingue.
/// </summary>
public interface ILyricsProvider
{
    string Name { get; }

    Task<IReadOnlyList<LyricsCandidate>> SearchAsync(SearchCandidate q, TimeSpan duration, CancellationToken ct);
}

/// <summary>Fallo de una fuente (red, HTTP, host inesperado, respuesta rota). El mensaje nunca lleva URL, título ni
/// artista.</summary>
public sealed class LyricsProviderException : Exception
{
    public LyricsProviderException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Catálogo de letras v2 (en la app, <see cref="LyricsAggregator"/> detrás de <see cref="LazyLyricsCatalog"/>; en los
/// tests, un doble): la mejor letra para una pista y todas las que hay, para el botón «Otra letra».
/// </summary>
public interface ILyricsCatalog
{
    /// <summary>La mejor letra; <see cref="LyricsResult.From"/> dice de dónde salió (null si la fuente no lo sabe).</summary>
    Task<LyricsResult> FetchAsync(TrackQuery q, CancellationToken ct);

    /// <summary>Todas las letras encontradas, en orden de búsqueda (vacía = nada, o la fuente no lo ofrece).</summary>
    Task<IReadOnlyList<LyricsCandidate>> AllCandidatesAsync(TrackQuery q, CancellationToken ct);
}

/// <summary>
/// Reúne varias <see cref="ILyricsProvider"/> detrás de <see cref="ILyricsSource"/> (letras v2 · B1).
/// <list type="bullet">
/// <item>Los candidatos de búsqueda (los da <c>candidates</c>, en la app <c>TrackNameParser.Candidates</c>) se recorren
/// por rank; para cada uno se consulta a TODAS las fuentes en paralelo, cada una con su timeout. En cuanto hay alguna
/// letra sincronizada se para; si no, se sigue con el siguiente candidato (las planas encontradas se guardan).</item>
/// <item>Con <c>accept</c> (en la app <see cref="LyricsMatch.IsMatch"/>), las letras que no son de la canción buscada
/// con ese candidato se descartan ANTES de todo lo demás: no cuentan para parar, ni para <see cref="Best"/>, ni para
/// «Otra letra». Sin él, todo vale (como antes).</item>
/// <item>Una fuente que falla, lanza o no contesta a tiempo (aunque ignore la cancelación) no tumba a las demás ni deja
/// la búsqueda colgada.</item>
/// <item><see cref="Best"/> elige: sincronizada con |Δduración| ≤ 3 s, luego la sincronizada más cercana, luego la plana
/// más cercana; empate → orden de llegada a la lista (rank del candidato y después orden de las fuentes).</item>
/// <item><see cref="FetchAsync"/>: algo → <see cref="LyricsStatus.Found"/> (plana: <see cref="LyricsResult.Plain"/>);
/// nada → <see cref="LyricsStatus.NotFound"/>; si TODAS las consultas fallaron → <see cref="LyricsStatus.Error"/>.
/// La cancelación del llamador se propaga como <see cref="OperationCanceledException"/>.</item>
/// <item>Sin caché ni reintentos propios (eso va en la caché en disco). Nunca registra título, artista ni URL.</item>
/// </list>
/// </summary>
public sealed class LyricsAggregator : ILyricsSource, ILyricsCatalog
{
    public static readonly TimeSpan DefaultPerProviderTimeout = TimeSpan.FromSeconds(6);

    /// <summary>Ventana de duración (± segundos) en la que una letra sincronizada se da por «la misma versión».</summary>
    public const double PreferredWindowSeconds = 3;

    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly Func<TrackQuery, IReadOnlyList<SearchCandidate>> _candidates;
    private readonly TimeSpan _timeout;
    private readonly Func<SearchCandidate, LyricsCandidate, bool>? _accept;

    /// <param name="accept">¿La letra es de la canción buscada con ese candidato? null = todas valen.</param>
    public LyricsAggregator(
        IReadOnlyList<ILyricsProvider> providers,
        Func<TrackQuery, IReadOnlyList<SearchCandidate>> candidates,
        TimeSpan perProviderTimeout,
        Func<SearchCandidate, LyricsCandidate, bool>? accept = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(perProviderTimeout, TimeSpan.Zero);
        _timeout = perProviderTimeout;
        _accept = accept;
    }

    /// <summary>Contrato de <see cref="ILyricsSource"/>: como <see cref="FetchAsync(TrackQuery, CancellationToken)"/> sin
    /// álbum ni app (<c>SourceAppId</c> vacío: el limpiador no lo trata como vídeo del navegador).</summary>
    public Task<LyricsResult> FetchAsync(string title, string artist, TimeSpan duration, CancellationToken ct) =>
        FetchAsync(new TrackQuery(title ?? "", artist ?? "", null, duration, ""), ct);

    /// <summary>La mejor letra para <paramref name="q"/>, con <see cref="LyricsResult.From"/> = la letra elegida. Con el
    /// <c>SourceAppId</c> del reproductor, los candidatos saben si es un vídeo del navegador.</summary>
    public async Task<LyricsResult> FetchAsync(TrackQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var (all, anyOk, anyQuery) = await SearchAllAsync(q, ct).ConfigureAwait(false);

        var pool = all.ToList();
        while (Best(pool, q.Duration) is { } best)
        {
            if (LyricsResult.FromCandidate(best) is { } found) return found;
            pool.Remove(best);   // «sincronizada» sin una sola marca válida: la siguiente
        }
        return anyQuery && !anyOk ? LyricsResult.Error : LyricsResult.NotFound;
    }

    /// <summary>Todas las letras encontradas para <paramref name="q"/> (sin duplicados por fuente + id), en el orden de
    /// la búsqueda. Para el botón «Otra letra».</summary>
    public async Task<IReadOnlyList<LyricsCandidate>> AllCandidatesAsync(TrackQuery q, CancellationToken ct) =>
        (await SearchAllAsync(q, ct).ConfigureAwait(false)).All;

    /// <summary>La mejor letra de <paramref name="all"/> para una pista de <paramref name="duration"/> (≤ 0 = desconocida);
    /// null si no hay ninguna con texto. Por grupos (sincronizada en la ventana de ±3 s, otra sincronizada, plana) y dentro
    /// de cada grupo la de duración más cercana. PURO.</summary>
    public static LyricsCandidate? Best(IReadOnlyList<LyricsCandidate> all, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(all);
        LyricsCandidate? best = null;
        (int Tier, double Diff) bestKey = (int.MaxValue, double.MaxValue);
        foreach (var c in all)
        {
            if (string.IsNullOrWhiteSpace(c.Body)) continue;
            double diff = duration <= TimeSpan.Zero ? 0
                : c.Duration <= TimeSpan.Zero ? double.MaxValue / 2
                : Math.Abs((c.Duration - duration).TotalSeconds);
            int tier = !c.Synced ? 2 : diff <= PreferredWindowSeconds ? 0 : 1;
            var key = (tier, diff);
            if (key.CompareTo(bestKey) < 0)   // estricto: empate → el primero
            {
                best = c;
                bestKey = key;
            }
        }
        return best;
    }

    private async Task<(IReadOnlyList<LyricsCandidate> All, bool AnyOk, bool AnyQuery)> SearchAllAsync(TrackQuery q, CancellationToken ct)
    {
        var all = new List<LyricsCandidate>();
        var seen = new HashSet<(string, string)>();
        bool anyOk = false, anyQuery = false;
        if (_providers.Count == 0) return (all, anyOk, anyQuery);

        foreach (var cand in _candidates(q).OrderBy(c => c.Rank))   // OrderBy es estable: mismo rank → orden dado
        {
            ct.ThrowIfCancellationRequested();
            anyQuery = true;
            var results = await Task.WhenAll(_providers.Select(p => QueryAsync(p, cand, q.Duration, ct))).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            bool synced = false;
            foreach (var r in results)
            {
                if (r is null) continue;
                anyOk = true;
                foreach (var c in r)
                {
                    // Filtro antes que duplicados: una letra que no encaja con este candidato puede encajar con otro.
                    if (c is null || (_accept is not null && !_accept(cand, c)) || !seen.Add((c.Source, c.ExternalId))) continue;
                    all.Add(c);
                    synced |= c.Synced && !string.IsNullOrWhiteSpace(c.Body);
                }
            }
            if (synced) break;
        }
        return (all, anyOk, anyQuery);
    }

    // Resultados de una fuente, o null si falló o no contestó a tiempo. Solo la cancelación del llamador sale como excepción.
    private async Task<IReadOnlyList<LyricsCandidate>?> QueryAsync(ILyricsProvider p, SearchCandidate q, TimeSpan duration, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<IReadOnlyList<LyricsCandidate>> task;
        try
        {
            task = p.SearchAsync(q, duration, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Warn(p, ex);
            return null;
        }

        // La espera no depende de que la fuente respete la cancelación: si no termina a tiempo, se la deja atrás.
        var delay = Task.Delay(_timeout, ct);
        var first = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (first != task)
        {
            cts.Cancel();
            Observe(task);
            ct.ThrowIfCancellationRequested();
            AppLog.Warn($"letras: la fuente {p.Name} no contestó en {_timeout.TotalSeconds:0.#} s");
            return null;
        }

        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Warn(p, ex);
            return null;
        }
    }

    private static void Warn(ILyricsProvider p, Exception ex) =>
        AppLog.Warn($"letras: la fuente {p.Name} falló ({Describe(ex)})");

    /// <summary>Por qué falló una fuente, sin título ni artista: de <see cref="LyricsProviderException"/> su mensaje (los
    /// escribimos nosotros y ninguno lleva la URL); de lo demás, solo tipos y códigos, nunca el mensaje de red, que podría
    /// llevarla. Sin esto el log decía «LyricsProviderException» y no se distinguía un 503 de un timeout.</summary>
    internal static string Describe(Exception ex)
    {
        string text = ex is LyricsProviderException ? $"{ex.GetType().Name}: {ex.Message}" : ex.GetType().Name;
        if (ex.InnerException is HttpRequestException http)
            text += $" ← HttpRequestException {http.HttpRequestError}" + (http.StatusCode is { } s ? $" HTTP {(int)s}" : "");
        else if (ex.InnerException is { } inner)
            text += $" ← {inner.GetType().Name}";
        return text;
    }

    // Una fuente abandonada que falle más tarde no debe dejar una excepción sin observar.
    private static void Observe(Task task) =>
        _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
