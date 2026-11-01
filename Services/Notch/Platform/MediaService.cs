using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>Metadatos de la pista según el reproductor. <paramref name="Thumbnail"/>: bytes crudos de la carátula (PNG/JPEG) o null.</summary>
public sealed record MediaProperties(string Title, string Artist, string Album, byte[]? Thumbnail);

/// <summary>Línea de tiempo según el reproductor: posición medida en <paramref name="LastUpdatedUtc"/> y duración (0 = desconocida).</summary>
public readonly record struct MediaTimeline(TimeSpan Position, TimeSpan Duration, DateTime LastUpdatedUtc);

/// <summary>
/// Una sesión de medios del sistema (lo que en Windows es <c>GlobalSystemMediaTransportControlsSession</c>).
/// Interfaz para poder probar <see cref="MediaService"/> sin WinRT. Los eventos pueden llegar en cualquier hilo.
/// </summary>
public interface IMediaSession
{
    string SourceAppId { get; }
    event Action? PropertiesChanged;
    event Action? PlaybackChanged;
    event Action? TimelineChanged;
    Task<MediaProperties?> GetPropertiesAsync();
    bool IsPlaying { get; }
    MediaTimeline Timeline { get; }
    Task TogglePlayPauseAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(TimeSpan to);
}

/// <summary>El gestor de sesiones del sistema: cuál es la sesión actual y cuándo cambia.</summary>
public interface IMediaSessionHub : IDisposable
{
    event Action? CurrentSessionChanged;
    IMediaSession? CurrentSession { get; }
}

/// <summary>
/// Servicio de música del notch: publica <see cref="NowPlaying"/> de la sesión de medios actual de Windows.
/// <list type="bullet">
/// <item>SOLO eventos (cambio de sesión, propiedades, reproducción, línea de tiempo): ningún temporizador.
/// La posición la interpola quien pinta con <see cref="NowPlaying.PositionAt"/>.</item>
/// <item>Generación: cada lectura de propiedades y cada cambio de sesión suben <c>_fetchSeq</c>; una respuesta
/// que llega tarde (canción o sesión anterior) se descarta, así nunca se pinta lo de la canción previa.</item>
/// <item>Reproducción/línea de tiempo NO vuelven a leer propiedades ni la carátula; la carátula se procesa una
/// sola vez por imagen (mismos bytes → mismo <see cref="AlbumArtResult"/>).</item>
/// <item><see cref="Changed"/> solo si la instantánea cambia, y se publica en el <see cref="SynchronizationContext"/>
/// capturado al crear el servicio (el de la UI); sin contexto, en el hilo del evento de WinRT.</item>
/// <item>Los controles nunca lanzan: sin sesión no hacen nada y un fallo del reproductor se registra y se traga.</item>
/// </list>
/// Cargar la proyección de WinRT cuesta memoria: créalo (<see cref="TryCreateAsync"/>) solo con la música activada.
/// </summary>
public sealed class MediaService : IDisposable
{
    /// <summary>Un «última actualización» más allá de esto en el futuro es un reloj roto del reproductor.</summary>
    private static readonly TimeSpan FutureSlack = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly IMediaSessionHub _hub;
    private readonly Func<byte[], AlbumArtResult?> _processArt;
    private readonly Func<DateTime> _utcNow;
    private readonly SynchronizationContext? _context;

    private IMediaSession? _session;
    private Action? _onProps, _onPlayback, _onTimeline;
    private long _fetchSeq;
    private MediaProperties? _props;
    private byte[]? _artSource;
    private AlbumArtResult? _art;
    private NowPlaying? _current;
    private bool _disposed;

    /// <summary>Se dispara cuando cambia lo que suena (null = no suena nada / no hay sesión).</summary>
    public event Action<NowPlaying?>? Changed;

    public MediaService(IMediaSessionHub hub)
        : this(hub, AlbumArt.Process, static () => DateTime.UtcNow, SynchronizationContext.Current)
    {
    }

    internal MediaService(IMediaSessionHub hub, Func<byte[], AlbumArtResult?> processArt, Func<DateTime> utcNow,
        SynchronizationContext? context)
    {
        _hub = hub;
        _processArt = processArt;
        _utcNow = utcNow;
        _context = context;
        _hub.CurrentSessionChanged += OnCurrentSessionChanged;
        AttachCurrent();
    }

    /// <summary>
    /// Crea el servicio sobre la API de medios de Windows (desde Windows 10 1809). Devuelve null si no está
    /// disponible. Llamar desde el hilo de UI: ahí se captura el contexto en el que se publica <see cref="Changed"/>.
    /// </summary>
    public static async Task<MediaService?> TryCreateAsync()
    {
        var context = SynchronizationContext.Current;
        var hub = await GsmtcMediaHub.TryCreateAsync().ConfigureAwait(false);
        return hub is null ? null : new MediaService(hub, AlbumArt.Process, static () => DateTime.UtcNow, context);
    }

    public NowPlaying? Current
    {
        get { lock (_gate) return _current; }
    }

    public Task PlayPauseAsync() => Run(static s => s.TogglePlayPauseAsync(), "play/pausa");
    public Task NextAsync() => Run(static s => s.NextAsync(), "siguiente");
    public Task PreviousAsync() => Run(static s => s.PreviousAsync(), "anterior");

    /// <summary>Salta a <paramref name="to"/>, recortado a [0, duración] (sin duración conocida, solo ≥ 0).</summary>
    public Task SeekAsync(TimeSpan to)
    {
        var duration = Current?.Duration ?? TimeSpan.Zero;
        if (to < TimeSpan.Zero) to = TimeSpan.Zero;
        if (duration > TimeSpan.Zero && to > duration) to = duration;
        return Run(s => s.SeekAsync(to), "saltar");
    }

    public void Dispose()
    {
        IMediaSession? old;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _fetchSeq++;
            old = _session;
            Detach(old);
            _session = null;
        }
        _hub.CurrentSessionChanged -= OnCurrentSessionChanged;
        _hub.Dispose();
    }

    // ---- Sesión ----

    private void OnCurrentSessionChanged() => AttachCurrent();

    private void AttachCurrent()
    {
        IMediaSession? next;
        long seq;
        lock (_gate)
        {
            if (_disposed) return;
            next = _hub.CurrentSession;
            Detach(_session);
            _session = next;
            _props = null;   // la carátula procesada se conserva: si la nueva trae la misma imagen, se reutiliza
            seq = ++_fetchSeq;
            if (next is not null) Attach(next);
        }
        if (next is null) Publish();
        else _ = RefreshPropertiesAsync(next, seq);
    }

    private void Attach(IMediaSession s)
    {
        _onProps = () => OnPropertiesChanged(s);
        _onPlayback = () => OnStateChanged(s);
        _onTimeline = () => OnStateChanged(s);
        s.PropertiesChanged += _onProps;
        s.PlaybackChanged += _onPlayback;
        s.TimelineChanged += _onTimeline;
    }

    private void Detach(IMediaSession? s)
    {
        if (s is null) return;
        try
        {
            s.PropertiesChanged -= _onProps;
            s.PlaybackChanged -= _onPlayback;
            s.TimelineChanged -= _onTimeline;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"música: no se pudo soltar la sesión anterior: {ex.Message}");
        }
        _onProps = _onPlayback = _onTimeline = null;
    }

    private void OnPropertiesChanged(IMediaSession s)
    {
        long seq;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(s, _session)) return;
            seq = ++_fetchSeq;
        }
        _ = RefreshPropertiesAsync(s, seq);
    }

    private void OnStateChanged(IMediaSession s)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(s, _session)) return;
        }
        Publish();
    }

    private async Task RefreshPropertiesAsync(IMediaSession s, long seq)
    {
        MediaProperties? props;
        try
        {
            props = await s.GetPropertiesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"música: no se pudieron leer las propiedades de {SafeAppId(s)}: {ex.Message}");
            return;   // se queda lo anterior; si la sesión murió, llegará un cambio de sesión
        }

        // La carátula se procesa fuera del candado (es CPU) y solo si son bytes nuevos.
        byte[]? thumb = props?.Thumbnail is { Length: > 0 } t ? t : null;
        byte[]? cachedSource;
        AlbumArtResult? cachedArt;
        lock (_gate)
        {
            if (_disposed || seq != _fetchSeq) return;
            cachedSource = _artSource;
            cachedArt = _art;
        }
        AlbumArtResult? art = null;
        if (thumb is not null)
        {
            if (cachedSource is not null && thumb.AsSpan().SequenceEqual(cachedSource)) art = cachedArt;
            else art = TryProcessArt(thumb);
        }

        lock (_gate)
        {
            if (_disposed || seq != _fetchSeq || !ReferenceEquals(s, _session)) return;
            _props = props;
            if (thumb is not null) { _artSource = thumb; _art = art; }
        }
        Publish();
    }

    private AlbumArtResult? TryProcessArt(byte[] thumb)
    {
        try { return _processArt(thumb); }
        catch (Exception ex)
        {
            AppLog.Warn($"música: carátula ilegible: {ex.Message}");
            return null;
        }
    }

    // ---- Publicación ----

    private void Publish()
    {
        NowPlaying? np;
        lock (_gate)
        {
            if (_disposed) return;
            var s = _session;
            if (s is not null && _props is null) return;   // sesión nueva aún sin propiedades: no parpadear a null
            try
            {
                np = s is null ? null : Build(s, _props!);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"música: estado de la sesión ilegible: {ex.Message}");
                return;
            }
            if (Equals(np, _current)) return;
            _current = np;
            // Con contexto se encola DENTRO del candado: el orden de publicación es el de los cambios.
            if (_context is not null)
            {
                _context.Post(static st =>
                {
                    var (self, value) = ((MediaService, NowPlaying?))st!;
                    if (!self._disposed) self.Changed?.Invoke(value);
                }, (this, np));
                return;
            }
        }
        Changed?.Invoke(np);
    }

    private NowPlaying? Build(IMediaSession s, MediaProperties p)
    {
        var title = (p.Title ?? "").Trim();
        if (title.Length == 0) return null;
        var t = s.Timeline;
        bool hasArt = p.Thumbnail is { Length: > 0 } && _art is not null;
        return new NowPlaying(
            title,
            (p.Artist ?? "").Trim(),
            (p.Album ?? "").Trim(),
            s.IsPlaying,
            t.Position,
            t.Duration > TimeSpan.Zero ? t.Duration : TimeSpan.Zero,
            Anchor(t.LastUpdatedUtc),
            hasArt ? _art!.Png : null,
            hasArt ? _art!.Accent : AlbumAccent.Default,
            s.SourceAppId ?? "");
    }

    /// <summary>El ancla de la posición; sin fecha o con una en el futuro (reloj roto del reproductor) → ahora.</summary>
    private DateTime Anchor(DateTime lastUpdatedUtc)
    {
        var now = _utcNow();
        if (lastUpdatedUtc == default || lastUpdatedUtc.Year < 2000 || lastUpdatedUtc > now + FutureSlack) return now;
        return lastUpdatedUtc;
    }

    // ---- Controles ----

    private async Task Run(Func<IMediaSession, Task> op, string what)
    {
        IMediaSession? s;
        lock (_gate) s = _disposed ? null : _session;
        if (s is null) return;
        try
        {
            await op(s).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"música: «{what}» falló en {SafeAppId(s)}: {ex.Message}");
        }
    }

    private static string SafeAppId(IMediaSession s)
    {
        try { return s.SourceAppId; } catch { return "?"; }
    }
}
