using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>
/// Adaptador fino de <c>Windows.Media.Control</c> (GSMTC, Windows 10 1809+) a <see cref="IMediaSessionHub"/>.
/// Sin lógica que decidir (eso vive en <see cref="MediaService"/>, probado con dobles): solo traduce tipos y
/// eventos. Cada cambio de sesión actual crea un envoltorio nuevo y suelta los eventos WinRT del anterior.
/// Los eventos llegan en hilos del pool de WinRT.
/// </summary>
internal sealed class GsmtcMediaHub : IMediaSessionHub
{
    private readonly object _gate = new();
    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> _onChanged;
    private GsmtcSession? _current;
    private bool _disposed;

    public event Action? CurrentSessionChanged;

    private GsmtcMediaHub(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _manager = manager;
        _onChanged = (_, _) => Refresh(raise: true);
        Refresh(raise: false);
        _manager.CurrentSessionChanged += _onChanged;
    }

    /// <summary>Null si la API no existe o falla (Windows antiguo, servicio de medios caído…).</summary>
    public static async Task<GsmtcMediaHub?> TryCreateAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().ConfigureAwait(false);
            return manager is null ? null : new GsmtcMediaHub(manager);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"música: la API de medios de Windows no está disponible: {ex.Message}");
            return null;
        }
    }

    public IMediaSession? CurrentSession
    {
        get { lock (_gate) return _current; }
    }

    private void Refresh(bool raise)
    {
        GsmtcSession? old;
        lock (_gate)
        {
            if (_disposed) return;
            old = _current;
            GlobalSystemMediaTransportControlsSession? s = null;
            try { s = _manager.GetCurrentSession(); }
            catch (Exception ex) { AppLog.Warn($"música: no se pudo leer la sesión actual: {ex.Message}"); }
            _current = s is null ? null : new GsmtcSession(s);
        }
        old?.Dispose();
        if (raise) CurrentSessionChanged?.Invoke();
    }

    public void Dispose()
    {
        GsmtcSession? old;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            old = _current;
            _current = null;
        }
        try { _manager.CurrentSessionChanged -= _onChanged; } catch { /* el servicio ya no está */ }
        old?.Dispose();
    }

    private sealed class GsmtcSession : IMediaSession, IDisposable
    {
        private readonly GlobalSystemMediaTransportControlsSession _s;
        private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onProps;
        private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlayback;
        private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimeline;
        private int _disposed;

        public GsmtcSession(GlobalSystemMediaTransportControlsSession s)
        {
            _s = s;
            SourceAppId = Safe(() => s.SourceAppUserModelId, "");
            _onProps = (_, _) => PropertiesChanged?.Invoke();
            _onPlayback = (_, _) => PlaybackChanged?.Invoke();
            _onTimeline = (_, _) => TimelineChanged?.Invoke();
            s.MediaPropertiesChanged += _onProps;
            s.PlaybackInfoChanged += _onPlayback;
            s.TimelinePropertiesChanged += _onTimeline;
        }

        public string SourceAppId { get; }
        public event Action? PropertiesChanged;
        public event Action? PlaybackChanged;
        public event Action? TimelineChanged;

        public bool IsPlaying =>
            _s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        public MediaTimeline Timeline
        {
            get
            {
                var t = _s.GetTimelineProperties();
                if (t is null) return default;
                // Posición y duración relativas al inicio (casi siempre 0).
                return new MediaTimeline(t.Position - t.StartTime, t.EndTime - t.StartTime, t.LastUpdatedTime.UtcDateTime);
            }
        }

        public async Task<MediaProperties?> GetPropertiesAsync()
        {
            var p = await _s.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
            if (p is null) return null;
            byte[]? thumb = p.Thumbnail is null ? null : await ReadThumbnailAsync(p.Thumbnail).ConfigureAwait(false);
            return new MediaProperties(p.Title ?? "", p.Artist ?? "", p.AlbumTitle ?? "", thumb);
        }

        private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference reference)
        {
            try
            {
                using var ras = await reference.OpenReadAsync().AsTask().ConfigureAwait(false);
                if (ras.Size == 0 || ras.Size > (ulong)AlbumArt.MaxInputBytes) return null;
                using var stream = ras.AsStreamForRead();
                using var ms = new MemoryStream((int)ras.Size);
                await stream.CopyToAsync(ms).ConfigureAwait(false);
                return ms.Length > AlbumArt.MaxInputBytes ? null : ms.ToArray();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"música: no se pudo leer la carátula: {ex.Message}");
                return null;
            }
        }

        public Task TogglePlayPauseAsync() => _s.TryTogglePlayPauseAsync().AsTask();
        public Task NextAsync() => _s.TrySkipNextAsync().AsTask();
        public Task PreviousAsync() => _s.TrySkipPreviousAsync().AsTask();

        public Task SeekAsync(TimeSpan to)
        {
            var start = Safe(() => _s.GetTimelineProperties()?.StartTime ?? TimeSpan.Zero, TimeSpan.Zero);
            return _s.TryChangePlaybackPositionAsync((start + to).Ticks).AsTask();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                _s.MediaPropertiesChanged -= _onProps;
                _s.PlaybackInfoChanged -= _onPlayback;
                _s.TimelinePropertiesChanged -= _onTimeline;
            }
            catch { /* la sesión ya se cerró en el reproductor */ }
        }

        private static T Safe<T>(Func<T> get, T fallback)
        {
            try { return get(); } catch { return fallback; }
        }
    }
}
