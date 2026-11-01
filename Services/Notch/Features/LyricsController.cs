namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>De dónde salen las letras (en la app, <see cref="LrclibClient"/>; en los tests, un doble).</summary>
public interface ILyricsSource
{
    Task<LyricsResult> FetchAsync(string title, string artist, TimeSpan duration, CancellationToken ct);
}

/// <summary>Qué pinta la vista de letra: nada pedido, buscando, letra sincronizada, «Sin letra», «Letra no disponible» o
/// (letras v2) letra PLANA sin marcas de tiempo (<see cref="LyricsController.PlainText"/>). <see cref="Plain"/> va al
/// final para no mover los valores de los demás.</summary>
public enum LyricsState { Idle, Loading, Found, NotFound, Error, Plain }

/// <summary>
/// Letra de la canción que suena para la página Música (F5 · B3; letras v2). Decide CUÁNDO se pide y QUÉ resultado vale:
/// <list type="bullet">
/// <item>Solo se pide con <c>wanted</c> (ajuste activo y, o bien la página Música visible con la vista de letra abierta, o
/// bien la letra en el notch pequeño activa con una pista sonando: lo decide la ventana con <see cref="Wanted"/>). Al
/// dejar de quererla se cancela lo que esté en vuelo (sus reintentos de 3 s y «Otra letra» incluidos).</item>
/// <item>Una petición por pista (título + artista normalizados): pausar, saltar dentro de la canción o los eventos del
/// reproductor no la repiten. La consulta lleva la app que reproduce (<see cref="TrackQuery.SourceAppId"/>): el
/// limpiador de títulos sabe si es un vídeo del navegador.</item>
/// <item>La duración llega a veces DESPUÉS del título (en SMTC el timeline es otro evento; la primera publicación puede
/// traer 0 o la de la pista anterior). Si cambia con la petición en vuelo, se vuelve a empezar UNA vez con la buena
/// (caché, elección y desfase de la clave correcta); después solo se pasa la clave de la caché a la nueva duración,
/// llevándose la letra que se ve y su elección, sin red. Así un directo con la duración creciendo no repite peticiones.</item>
/// <item>Caché en disco (opcional, <see cref="LyricsCache"/> con clave <see cref="LyricsCache.Key"/>): se mira ANTES de
/// la red y se guarda lo encontrado (sincronizada o plana). Una letra elegida a mano y su desfase se respetan al
/// volver a la pista y tras reiniciar. Con <c>sourceSupported</c>, la letra o la elección de una fuente que la app ya no
/// tiene (NetEase y Musixmatch, retiradas: <see cref="LazyLyricsCatalog.Supports"/>) no cuentan: se va a la red, que
/// solo pregunta a LRCLIB, y lo nuevo sustituye a lo viejo.</item>
/// <item><see cref="LyricsState.Found"/> sigue significando «líneas con tiempos» (<see cref="Lines"/> no vacías): la
/// letra plana es un estado aparte, <see cref="LyricsState.Plain"/> con <see cref="PlainText"/>. Así quien solo mira
/// <c>Found</c>/<see cref="Lines"/> (la UI de hoy) no confunde una con otra.</item>
/// <item><see cref="LyricsState.NotFound"/> (instrumental o sin letra) es definitivo para esa pista: nunca se vuelve a
/// pedir. <see cref="LyricsState.Error"/> (el cliente ya reintentó) no se reintenta solo: solo si se cierra y se vuelve
/// a abrir la vista. Así no hay reintentos infinitos. Ninguno de los dos se guarda en la caché.</item>
/// <item>Desfase manual por pista (<see cref="NudgeOffset"/>, pasos de 0,5 s acotados por la caché) solo con letra
/// sincronizada; se guarda con <see cref="LyricsCache.SetChoice"/> junto a la letra que se ve.</item>
/// <item>«Otra letra» (<see cref="NextCandidate"/>): pide <see cref="ILyricsCatalog.AllCandidatesAsync"/> una vez por
/// pista, cicla por las letras utilizables y guarda la elección (con desfase 0: otra letra, otros tiempos). Si la
/// petición falla o no trae ninguna utilizable, el siguiente clic lo vuelve a intentar.</item>
/// <item>Generación: cambiar de pista cancela la petición anterior y sube <c>_gen</c>; una respuesta que llega tarde
/// (aunque no respete la cancelación) se descarta. Nunca se pinta la letra de la canción anterior.</item>
/// </list>
/// Se usa desde el hilo de UI: la continuación vuelve a su contexto (sin <c>ConfigureAwait(false)</c>).
/// </summary>
public sealed class LyricsController : IDisposable
{
    private readonly ILyricsCatalog _catalog;
    private readonly LyricsCache? _cache;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<string, bool>? _sourceSupported;   // null = todas las fuentes valen
    private string? _key;
    private TrackQuery? _query;                              // pista actual tal como se pidió (caché y «Otra letra»)
    private string? _cacheKey;
    private LyricsCandidate? _current;                       // letra que se enseña; null si la fuente no dice de dónde salió
    private IReadOnlyList<LyricsCandidate>? _candidates;     // letras utilizables de la pista (una petición por pista)
    private bool _candidatesLoading;
    private bool _restarted;                                 // ya se volvió a empezar por la duración en esta pista
    private long _gen;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>Contrato de siempre: una <see cref="ILyricsSource"/> sin caché ni «Otra letra».</summary>
    public LyricsController(ILyricsSource source)
        : this(new SourceCatalog(source ?? throw new ArgumentNullException(nameof(source))), null)
    {
    }

    /// <summary>Letras v2: un catálogo (todos los candidatos), opcional la caché en disco y qué fuentes sigue teniendo la
    /// app (<paramref name="sourceSupported"/> con el nombre de <see cref="LyricsCandidate.Source"/>; null = todas): lo
    /// guardado de otra fuente no se enseña.</summary>
    public LyricsController(ILyricsCatalog catalog, LyricsCache? cache = null, Func<string, bool>? sourceSupported = null)
        : this(catalog, cache, static () => DateTime.UtcNow, sourceSupported)
    {
    }

    /// <summary>Para tests: reloj inyectable (marca <see cref="CachedLyrics.SavedUtc"/>).</summary>
    internal LyricsController(ILyricsCatalog catalog, LyricsCache? cache, Func<DateTime> utcNow, Func<string, bool>? sourceSupported = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _cache = cache;
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _sourceSupported = sourceSupported;
    }

    /// <summary>Regla de red de la ventana: la letra solo se quiere con el ajuste «Letras» activo y, además, o la página
    /// Música visible con la vista de letra abierta, o la letra en el notch pequeño (<paramref name="compactEnabled"/>) cuando
    /// de verdad puede llegar a verse (<paramref name="compactCanShow"/> = <see cref="CompactLyrics.CanShow"/>; con
    /// «Minimalista» o el notch oculto, no): así el reposo la tiene aunque la vista esté cerrada. Sigue siendo una petición
    /// por pista (caché primero). Con el ajuste «Letras» apagado, nunca (ni una petición a lrclib.net). PURO.</summary>
    public static bool Wanted(bool enabled, bool musicPageVisible, bool lyricsViewOpen, bool compactEnabled = false,
        bool compactCanShow = false) =>
        enabled && ((musicPageVisible && lyricsViewOpen) || (compactEnabled && compactCanShow));

    /// <summary>Cambió algo de lo que se pinta (<see cref="State"/>, <see cref="Lines"/>, <see cref="PlainText"/>,
    /// <see cref="Source"/>, <see cref="OffsetSeconds"/> o los candidatos). Puede dispararse dentro de <see cref="Update"/>.</summary>
    public event Action? Changed;

    public LyricsState State { get; private set; } = LyricsState.Idle;

    /// <summary>Líneas de la pista actual (vacías salvo en <see cref="LyricsState.Found"/>). Misma instancia mientras no
    /// cambien: la ventana la compara por referencia para saber si es otra letra. Sin el desfase aplicado (ver
    /// <see cref="LyricsPosition"/>).</summary>
    public IReadOnlyList<LyricLine> Lines { get; private set; } = Array.Empty<LyricLine>();

    /// <summary>Texto de la letra plana (saltos <c>\n</c>) en <see cref="LyricsState.Plain"/>; null en los demás.</summary>
    public string? PlainText { get; private set; }

    /// <summary>Fuente de la letra que se ve («LRCLIB»); null si no hay letra o la fuente no lo dice
    /// (<see cref="LrclibClient"/>).</summary>
    public string? Source => State is LyricsState.Found or LyricsState.Plain ? _current?.Source : null;

    /// <summary>Desfase manual de la pista en segundos (positivo = la letra va más tarde). 0 sin letra.</summary>
    public double OffsetSeconds { get; private set; }

    /// <summary>Se puede mover el desfase: solo con letra sincronizada.</summary>
    public bool CanNudge => State == LyricsState.Found;

    /// <summary>Letras utilizables de la pista; 0 hasta el primer «Otra letra» (la lista se pide entonces).</summary>
    public int CandidateCount => _candidates?.Count ?? 0;

    /// <summary>Posición de la letra que se ve en esos candidatos (0 = la primera); −1 si aún no se pidieron o no está.</summary>
    public int CandidateIndex => _candidates is { } list ? IndexOf(list, _current) : -1;

    /// <summary>«Otra letra» pidiendo la lista de candidatos (la UI puede atenuar el botón mientras tanto).</summary>
    public bool CandidatesLoading => _candidatesLoading;

    /// <summary>Instante de la LETRA para la posición del reproductor: <paramref name="playerPosition"/> − desfase (nunca
    /// negativo). Es lo que se pasa a <see cref="LrcParser.IndexAt"/>.</summary>
    public TimeSpan LyricsPosition(TimeSpan playerPosition)
    {
        var t = playerPosition - TimeSpan.FromSeconds(OffsetSeconds);
        return t < TimeSpan.Zero ? TimeSpan.Zero : t;
    }

    /// <summary>Lleva el controlador a la pista <paramref name="track"/> (null = no suena nada) y pide la letra si hace
    /// falta y <paramref name="wanted"/>. Idempotente: llamarlo en cada repintado no cuesta nada.</summary>
    public void Update(NowPlaying? track, bool wanted)
    {
        if (_disposed) return;
        bool changed = false;
        string? key = track is null ? null : Key(track.Title, track.Artist);
        if (key != _key)
        {
            Cancel();
            _key = key;
            _restarted = false;
            changed |= ForgetTrack();
            changed |= Set(LyricsState.Idle, Array.Empty<LyricLine>(), null);
        }

        if (track is null || key is null)
        {
            if (changed) Changed?.Invoke();
            return;
        }

        if (!wanted)
        {
            if (State == LyricsState.Loading)
            {
                Cancel();
                changed |= Set(LyricsState.Idle, Array.Empty<LyricLine>(), null);
            }
            else if (State == LyricsState.Error)
                changed |= Set(LyricsState.Idle, Array.Empty<LyricLine>(), null);   // al volver a quererla, un intento más
            if (_candidatesLoading)
            {
                Cancel();   // «Otra letra» en vuelo: sin vista, nada de red
                _candidatesLoading = false;
                changed = true;
            }
            if (changed) Changed?.Invoke();
            return;
        }

        if (State != LyricsState.Idle && _query is { } asked &&
            LyricsCache.DurationBucket(asked.Duration) != LyricsCache.DurationBucket(track.Duration))
            changed |= DurationChanged(asked with { Duration = track.Duration });

        if (State == LyricsState.Idle)
        {
            var q = new TrackQuery(track.Title ?? "", track.Artist ?? "", track.Album, track.Duration, track.SourceAppId ?? "");
            _query = q;
            _cacheKey = LyricsCache.Key(q);
            var choice = _cache?.Choice(_cacheKey);
            if (choice is not null && !Supported(choice.Source)) choice = null;   // de una fuente retirada: como si no hubiera
            OffsetSeconds = choice?.OffsetSeconds ?? 0;

            if (FromCache(q, _cacheKey, choice) is { } cached)
            {
                Apply(cached);
                Changed?.Invoke();
                return;
            }

            Set(LyricsState.Loading, Array.Empty<LyricLine>(), null);
            var cts = new CancellationTokenSource();
            _cts = cts;
            long gen = ++_gen;
            Changed?.Invoke();
            _ = RunAsync(gen, q, choice, cts.Token);
            return;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Mueve el desfase <paramref name="delta"/> segundos (la UI usa ±<see cref="LyricsCache.OffsetStep"/>),
    /// acotado por <see cref="LyricsCache.ClampOffset"/>, y lo guarda en la caché con la letra que se ve. Sin letra
    /// sincronizada no hace nada.</summary>
    public void NudgeOffset(double delta)
    {
        if (_disposed || !CanNudge || double.IsNaN(delta)) return;
        var next = LyricsCache.ClampOffset(OffsetSeconds + delta);
        if (next == OffsetSeconds) return;
        OffsetSeconds = next;
        if (_cache is not null && _cacheKey is not null && _current is not null)
            _cache.SetChoice(_cacheKey, new LyricsChoice(_current.Source, _current.ExternalId, next));
        Changed?.Invoke();
    }

    /// <summary>
    /// «Otra letra»: la siguiente de los candidatos de la pista (la primera vez los pide, una sola vez por pista), en
    /// ciclo. Guarda la elección en la caché (letra + <see cref="LyricsChoice"/> con desfase 0). Sin letra a la vista, con
    /// la lista en camino o con un único candidato que ya es el que se ve, no hace nada. Nunca lanza.
    /// </summary>
    public async Task NextCandidate()
    {
        if (_disposed || _candidatesLoading || _query is null || _cacheKey is null) return;
        if (State is not (LyricsState.Found or LyricsState.Plain)) return;

        if (_candidates is null)
        {
            var q = _query;
            _candidatesLoading = true;
            var old = _cts;
            var cts = new CancellationTokenSource();
            _cts = cts;
            old?.Dispose();   // la del fetch principal, ya terminado (estamos en Found o Plain)
            long gen = _gen;
            Changed?.Invoke();

            IReadOnlyList<LyricsCandidate> all;
            try
            {
                all = await _catalog.AllCandidatesAsync(q, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;   // otra pista, vista cerrada o liberado: quien canceló ya dejó el estado como toca
            }
            catch (Exception ex)
            {
                if (_disposed || gen != _gen) return;
                AppLog.Warn($"letras: fallo al pedir más letras ({ex.GetType().Name})");
                all = Array.Empty<LyricsCandidate>();
            }

            if (_disposed || gen != _gen || cts.IsCancellationRequested) return;   // de otra pista: fuera
            _candidatesLoading = false;
            var usable = Usable(all);
            if (usable.Count == 0)
            {
                Changed?.Invoke();   // fallo o nada utilizable (red caída, fuentes sin respuesta): el siguiente clic reintenta
                return;
            }
            _candidates = usable;
        }

        var list = _candidates;
        int i = IndexOf(list, _current);
        int next = list.Count == 0 ? -1 : (i + 1) % list.Count;
        if (next < 0 || next == i)
        {
            Changed?.Invoke();   // al menos el fin de «cargando» y el recuento
            return;
        }

        var c = list[next];
        OffsetSeconds = 0;
        Apply(LyricsResult.FromCandidate(c)!);   // Usable garantiza que da letra
        Save(_cacheKey, c);
        _cache?.SetChoice(_cacheKey, new LyricsChoice(c.Source, c.ExternalId, 0));
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
    }

    private async Task RunAsync(long gen, TrackQuery q, LyricsChoice? choice, CancellationToken ct)
    {
        LyricsResult r;
        IReadOnlyList<LyricsCandidate>? candidates = null;
        try
        {
            if (choice is null)
                r = await _catalog.FetchAsync(q, ct);
            else
                (r, candidates) = await FetchChosenAsync(q, choice, ct);
        }
        catch (OperationCanceledException)
        {
            return;   // otra pista, vista cerrada o liberado: quien canceló ya dejó el estado como toca
        }
        catch (Exception ex)
        {
            if (_disposed || gen != _gen) return;   // liberado o ya es otra pista: no es un fallo de esta
            AppLog.Warn($"letras: fallo inesperado ({ex.GetType().Name})");   // el mensaje podría llevar la URL
            r = LyricsResult.Error;
        }

        if (_disposed || gen != _gen || ct.IsCancellationRequested) return;   // respuesta de otra pista: fuera
        var cacheKey = _cacheKey;   // la de AHORA: la duración pudo llegar con la petición en vuelo
        bool changed = candidates is not null;
        if (candidates is not null) _candidates = candidates;
        changed |= Apply(r);
        if (cacheKey is not null && r.Status == LyricsStatus.Found && r.From is { } from &&
            State is LyricsState.Found or LyricsState.Plain)
        {
            Save(cacheKey, from);
            if (choice is not null && !Same(from, choice.Source, choice.ExternalId))
            {
                // La elegida ya no aparece: la que se ve pasa a ser la elección (con desfase 0: otros tiempos). Si no,
                // la caché nunca coincidiría con la elección y cada visita volvería a la red.
                _cache?.SetChoice(cacheKey, new LyricsChoice(from.Source, from.ExternalId, 0));
                changed |= OffsetSeconds != 0;
                OffsetSeconds = 0;
            }
        }
        if (changed) Changed?.Invoke();
    }

    private void Save(string cacheKey, LyricsCandidate c) =>
        _cache?.Put(cacheKey, new CachedLyrics(c.Source, c.ExternalId, c.Synced, c.Body, c.Duration, _utcNow()));

    // Hay una letra elegida a mano pero no está en la caché (caducó): se buscan todas y se toma esa; si ya no aparece,
    // la mejor de las que hay; si la fuente no da candidatos, la búsqueda normal.
    private async Task<(LyricsResult, IReadOnlyList<LyricsCandidate>?)> FetchChosenAsync(TrackQuery q, LyricsChoice choice, CancellationToken ct)
    {
        var usable = Usable(await _catalog.AllCandidatesAsync(q, ct));
        if (usable.Count == 0) return (await _catalog.FetchAsync(q, ct), null);
        var pick = usable.FirstOrDefault(c => Same(c, choice.Source, choice.ExternalId))
                   ?? LyricsAggregator.Best(usable, q.Duration)!;
        return (LyricsResult.FromCandidate(pick)!, usable);
    }

    // La duración de la pista cambió (llegó tarde o era la de la anterior). Con la petición en vuelo y por primera vez en
    // la pista: se vuelve a empezar (Idle; Update sigue con la caché de la clave buena). Si no: la clave de la caché pasa a
    // la nueva duración y la letra que se ve (y su elección, si la había) se guardan también con ella. true si cambió algo.
    private bool DurationChanged(TrackQuery q)
    {
        if (State == LyricsState.Loading && !_restarted)
        {
            Cancel();
            bool forgot = ForgetTrack();
            _restarted = true;
            return Set(LyricsState.Idle, Array.Empty<LyricLine>(), null) | forgot;
        }
        var oldKey = _cacheKey;
        _query = q;
        _cacheKey = LyricsCache.Key(q);
        if (_cache is not null && oldKey is not null && _current is { } shown && State is LyricsState.Found or LyricsState.Plain)
        {
            Save(_cacheKey, shown);
            if (_cache.Choice(oldKey) is not null)
                _cache.SetChoice(_cacheKey, new LyricsChoice(shown.Source, shown.ExternalId, OffsetSeconds));
        }
        return false;
    }

    // Letra de la caché, si la hay, es de una fuente que la app sigue teniendo y es la elegida (si hay elección). null = a
    // la red.
    private LyricsResult? FromCache(TrackQuery q, string cacheKey, LyricsChoice? choice)
    {
        if (_cache is null || !_cache.TryGet(cacheKey, out var c) || !Supported(c.Source)) return null;
        if (choice is not null && (c.Source != choice.Source || c.ExternalId != choice.ExternalId)) return null;
        return LyricsResult.FromCandidate(new LyricsCandidate(c.Source, c.ExternalId, q.Title, q.Artist, c.Duration, c.Synced, c.Body));
    }

    // Publica un resultado: sincronizada → Found; plana → Plain; error → Error; lo demás → NotFound.
    // true si cambió algo de lo que se pinta.
    private bool Apply(LyricsResult r)
    {
        var before = _current;
        bool changed;
        if (r.Status == LyricsStatus.Found && r.Lines.Count > 0)
        {
            _current = r.From;
            changed = Set(LyricsState.Found, r.Lines, null);
        }
        else if (r.Status == LyricsStatus.Found && !r.Synced && !string.IsNullOrWhiteSpace(r.PlainText))
        {
            _current = r.From;
            changed = Set(LyricsState.Plain, Array.Empty<LyricLine>(), r.PlainText);
        }
        else
        {
            _current = null;
            changed = Set(r.Status == LyricsStatus.Error ? LyricsState.Error : LyricsState.NotFound, Array.Empty<LyricLine>(), null);
        }
        return changed || !Equals(before, _current);
    }

    private bool Set(LyricsState state, IReadOnlyList<LyricLine> lines, string? plain)
    {
        if (state == State && plain == PlainText &&
            (ReferenceEquals(lines, Lines) || (lines.Count == 0 && Lines.Count == 0))) return false;
        State = state;
        Lines = lines;
        PlainText = plain;
        return true;
    }

    // Borra lo que es de la pista anterior (no el estado: eso lo hace Set). true si había algo que se veía.
    private bool ForgetTrack()
    {
        bool had = _current is not null || OffsetSeconds != 0 || _candidates is not null || _candidatesLoading;
        _query = null;
        _cacheKey = null;
        _current = null;
        _candidates = null;
        _candidatesLoading = false;
        OffsetSeconds = 0;
        return had;
    }

    private void Cancel()
    {
        _gen++;
        var cts = _cts;
        _cts = null;
        if (cts is null) return;
        try { cts.Cancel(); }
        catch (AggregateException ex) { AppLog.Warn($"letras: al cancelar: {ex.Message}"); }
        cts.Dispose();
    }

    // Solo las que dan algo que enseñar (cuerpo no vacío; sincronizada con al menos una marca válida).
    private static IReadOnlyList<LyricsCandidate> Usable(IReadOnlyList<LyricsCandidate>? all) =>
        all is null ? Array.Empty<LyricsCandidate>() : all.Where(c => c is not null && LyricsResult.FromCandidate(c) is not null).ToList();

    private static int IndexOf(IReadOnlyList<LyricsCandidate> list, LyricsCandidate? c)
    {
        if (c is null) return -1;
        for (int i = 0; i < list.Count; i++)
            if (Same(list[i], c.Source, c.ExternalId)) return i;
        return -1;
    }

    private bool Supported(string source) => _sourceSupported is null || _sourceSupported(source);

    private static bool Same(LyricsCandidate c, string source, string externalId) =>
        c.Source == source && c.ExternalId == externalId;

    private static string Key(string title, string artist) =>
        (title ?? "").Trim().ToLowerInvariant() + "\u001F" + (artist ?? "").Trim().ToLowerInvariant();

    /// <summary>Una <see cref="ILyricsSource"/> de las de siempre como catálogo: sin candidatos para «Otra letra».</summary>
    private sealed class SourceCatalog : ILyricsCatalog
    {
        private readonly ILyricsSource _source;

        public SourceCatalog(ILyricsSource source) => _source = source;

        public Task<LyricsResult> FetchAsync(TrackQuery q, CancellationToken ct) =>
            _source.FetchAsync(q.Title, q.Artist, q.Duration, ct);

        public Task<IReadOnlyList<LyricsCandidate>> AllCandidatesAsync(TrackQuery q, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LyricsCandidate>>(Array.Empty<LyricsCandidate>());
    }
}
