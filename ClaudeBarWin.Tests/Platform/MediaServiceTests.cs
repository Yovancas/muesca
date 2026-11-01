using System.Drawing;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Notch.Platform;

namespace ClaudeBarWin.Tests.Platform;

/// <summary>
/// <see cref="MediaService"/> con dobles de la API de medios de Windows (<see cref="IMediaSessionHub"/> /
/// <see cref="IMediaSession"/>): todo por eventos, sin sondeo; al cambiar de sesión o de canción no se cuela
/// nada de la anterior (generación), la carátula se procesa una vez por imagen y los controles nunca lanzan.
/// </summary>
public class MediaServiceTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 18, 0, 0, DateTimeKind.Utc);
    private static readonly Color ArtAccent = Color.FromArgb(30, 60, 255);

    private sealed class FakeSession : IMediaSession
    {
        public string SourceAppId { get; init; } = "Spotify.exe";
        public bool IsPlaying { get; set; } = true;
        public MediaTimeline Timeline { get; set; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(200), T0);
        public MediaProperties? Props { get; set; } = new("Canción", "Artista", "Álbum", new byte[] { 1, 2, 3 });

        /// <summary>Si no es null, la próxima lectura de propiedades espera a este TCS (para simular lentitud).</summary>
        public Queue<TaskCompletionSource<MediaProperties?>> Pending { get; } = new();
        public int PropertyReads;
        public List<string> Commands { get; } = new();
        public bool ThrowOnCommand;

        public event Action? PropertiesChanged;
        public event Action? PlaybackChanged;
        public event Action? TimelineChanged;

        public int Subscribers => (PropertiesChanged?.GetInvocationList().Length ?? 0)
                                + (PlaybackChanged?.GetInvocationList().Length ?? 0)
                                + (TimelineChanged?.GetInvocationList().Length ?? 0);

        public Task<MediaProperties?> GetPropertiesAsync()
        {
            PropertyReads++;
            return Pending.Count > 0 ? Pending.Dequeue().Task : Task.FromResult(Props);
        }

        public void RaiseProperties() => PropertiesChanged?.Invoke();
        public void RaisePlayback() => PlaybackChanged?.Invoke();
        public void RaiseTimeline() => TimelineChanged?.Invoke();

        private Task Cmd(string name)
        {
            Commands.Add(name);
            return ThrowOnCommand ? Task.FromException(new InvalidOperationException("sesión cerrada")) : Task.CompletedTask;
        }

        public Task TogglePlayPauseAsync() => Cmd("playpause");
        public Task NextAsync() => Cmd("next");
        public Task PreviousAsync() => Cmd("previous");
        public Task SeekAsync(TimeSpan to) => Cmd("seek " + to.TotalSeconds);
    }

    private sealed class FakeHub : IMediaSessionHub
    {
        public IMediaSession? CurrentSession { get; set; }
        public event Action? CurrentSessionChanged;
        public bool Disposed;
        public int Subscribers => CurrentSessionChanged?.GetInvocationList().Length ?? 0;
        public void Switch(IMediaSession? s) { CurrentSession = s; CurrentSessionChanged?.Invoke(); }
        public void Dispose() => Disposed = true;
    }

    private int _artCalls;
    private DateTime _now = T0.AddSeconds(1);

    private AlbumArtResult? ProcessArt(byte[] raw)
    {
        _artCalls++;
        return new AlbumArtResult(new byte[] { 0x89, (byte)raw.Length }, ArtAccent);
    }

    private MediaService New(FakeHub hub, SynchronizationContext? ctx = null) => new(hub, ProcessArt, () => _now, ctx);

    /// <summary>
    /// Sin el contexto de xunit, para que <c>SetResult</c> ejecute la continuación del servicio en línea
    /// (con un contexto que no es el base, .NET la manda al pool y el test tendría que esperar a ciegas).
    /// </summary>
    private static void WithoutContext(Action body)
    {
        var saved = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { body(); }
        finally { SynchronizationContext.SetSynchronizationContext(saved); }
    }

    private static (FakeHub Hub, FakeSession Session) Setup()
    {
        var s = new FakeSession();
        return (new FakeHub { CurrentSession = s }, s);
    }

    [Fact]
    public void No_session_means_null_current()
    {
        using var m = New(new FakeHub());
        Assert.Null(m.Current);
    }

    [Fact]
    public void Session_maps_to_NowPlaying()
    {
        var (hub, s) = Setup();
        using var m = New(hub);
        var np = m.Current;
        Assert.NotNull(np);
        Assert.Equal("Canción", np!.Title);
        Assert.Equal("Artista", np.Artist);
        Assert.Equal("Álbum", np.Album);
        Assert.True(np.Playing);
        Assert.Equal(TimeSpan.FromSeconds(10), np.Position);
        Assert.Equal(TimeSpan.FromSeconds(200), np.Duration);
        Assert.Equal(T0, np.PositionAtUtc);
        Assert.Equal(ArtAccent, np.Accent);
        Assert.NotNull(np.ArtPng);
        Assert.Equal("Spotify.exe", np.SourceAppId);
        Assert.Equal(1, s.PropertyReads);
    }

    [Fact]
    public void Missing_art_uses_default_accent()
    {
        var (hub, s) = Setup();
        s.Props = new("Canción", "Artista", "", null);
        using var m = New(hub);
        Assert.Null(m.Current!.ArtPng);
        Assert.Equal(AlbumAccent.Default, m.Current.Accent);
        Assert.Equal(0, _artCalls);
    }

    [Fact]
    public void Playback_and_timeline_changes_republish_without_refetching_properties()
    {
        var (hub, s) = Setup();
        using var m = New(hub);
        var seen = new List<NowPlaying?>();
        m.Changed += seen.Add;

        s.IsPlaying = false;
        s.RaisePlayback();
        s.Timeline = new(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(200), T0.AddSeconds(40));
        s.RaiseTimeline();

        Assert.Equal(2, seen.Count);
        Assert.False(seen[0]!.Playing);
        Assert.Equal(TimeSpan.FromSeconds(50), seen[1]!.Position);
        Assert.Equal(1, s.PropertyReads);
        Assert.Equal(1, _artCalls);
    }

    [Fact]
    public void Identical_snapshot_does_not_raise_Changed()
    {
        var (hub, s) = Setup();
        using var m = New(hub);
        int raised = 0;
        m.Changed += _ => raised++;
        s.RaisePlayback();
        s.RaiseTimeline();
        s.RaiseProperties();   // mismas propiedades y misma carátula
        Assert.Equal(0, raised);
        Assert.Equal(1, _artCalls);
    }

    [Fact]
    public void Same_thumbnail_is_processed_once()
    {
        var (hub, s) = Setup();
        using var m = New(hub);
        s.Props = s.Props! with { Title = "Otra", Thumbnail = new byte[] { 1, 2, 3 } }; // bytes iguales, otro array
        s.RaiseProperties();
        Assert.Equal("Otra", m.Current!.Title);
        Assert.Equal(1, _artCalls);

        s.Props = s.Props with { Thumbnail = new byte[] { 9, 9 } };
        s.RaiseProperties();
        Assert.Equal(2, _artCalls);
    }

    [Fact]
    public void Stale_properties_of_previous_song_are_dropped()
    {
        WithoutContext(() =>
        {
            var (hub, s) = Setup();
            using var m = New(hub);
            var slowA = new TaskCompletionSource<MediaProperties?>();
            var fastB = new TaskCompletionSource<MediaProperties?>();
            s.Pending.Enqueue(slowA);
            s.Pending.Enqueue(fastB);

            s.RaiseProperties();   // canción A (lenta)
            s.RaiseProperties();   // canción B (rápida)
            fastB.SetResult(new("B", "Artista", "", null));
            slowA.SetResult(new("A", "Artista", "", null));

            Assert.Equal("B", m.Current!.Title);
    
        });
    }

    [Fact]
    public void Session_switch_detaches_old_session_and_drops_its_late_answers()
    {
        WithoutContext(() =>
        {
            var (hub, old) = Setup();
            var late = new TaskCompletionSource<MediaProperties?>();
            using var m = New(hub);
            old.Pending.Enqueue(late);
            old.RaiseProperties();                       // petición en vuelo de la sesión vieja

            var brave = new FakeSession { SourceAppId = "Brave", Props = new("Vídeo", "Canal", "", null) };
            hub.Switch(brave);
            Assert.Equal(0, old.Subscribers);
            Assert.Equal(3, brave.Subscribers);
            Assert.Equal("Brave", m.Current!.SourceAppId);

            late.SetResult(new("Vieja", "X", "", null));
            old.IsPlaying = false;
            old.RaisePlayback();
            Assert.Equal("Vídeo", m.Current!.Title);
            Assert.True(m.Current.Playing);
    
        });
    }

    [Fact]
    public void Session_going_away_publishes_null()
    {
        var (hub, s) = Setup();
        using var m = New(hub);
        NowPlaying? last = m.Current;
        m.Changed += np => last = np;
        hub.Switch(null);
        Assert.Null(last);
        Assert.Null(m.Current);
        Assert.Equal(0, s.Subscribers);
    }

    [Fact]
    public void Empty_title_publishes_null()
    {
        var (hub, s) = Setup();
        s.Props = new("   ", "Artista", "", null);
        using var m = New(hub);
        Assert.Null(m.Current);
    }

    [Fact]
    public void Failed_property_read_keeps_previous_song()
    {
        WithoutContext(() =>
        {
            var (hub, s) = Setup();
            using var m = New(hub);
            var broken = new TaskCompletionSource<MediaProperties?>();
            s.Pending.Enqueue(broken);
            s.RaiseProperties();
            broken.SetException(new InvalidOperationException("RPC caído"));
            Assert.Equal("Canción", m.Current!.Title);
    
        });
    }

    [Fact]
    public void Bogus_last_updated_is_anchored_to_now()
    {
        var (hub, s) = Setup();
        s.Timeline = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(200), default);
        using var m = New(hub);
        Assert.Equal(_now, m.Current!.PositionAtUtc);

        s.Timeline = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(200), _now.AddHours(3)); // en el futuro
        s.RaiseTimeline();
        Assert.Equal(_now, m.Current!.PositionAtUtc);
    }

    [Fact]
    public async Task Commands_forward_to_current_session_and_seek_is_clamped()
    {
        var (hub, s) = Setup();
        using var m = New(hub);
        await m.PlayPauseAsync();
        await m.NextAsync();
        await m.PreviousAsync();
        await m.SeekAsync(TimeSpan.FromSeconds(500));
        await m.SeekAsync(TimeSpan.FromSeconds(-5));
        Assert.Equal(new[] { "playpause", "next", "previous", "seek 200", "seek 0" }, s.Commands);
    }

    [Fact]
    public async Task Commands_without_session_or_failing_never_throw()
    {
        var hub = new FakeHub();
        using var m = New(hub);
        await m.PlayPauseAsync();
        await m.SeekAsync(TimeSpan.FromSeconds(3));

        var s = new FakeSession { ThrowOnCommand = true };
        hub.Switch(s);
        await m.NextAsync();
        await m.PlayPauseAsync();
        Assert.Equal(new[] { "next", "playpause" }, s.Commands);
    }

    [Fact]
    public void Changed_is_posted_to_the_captured_context()
    {
        var (hub, s) = Setup();
        var ctx = new RecordingContext();
        using var m = New(hub, ctx);
        int raised = 0;
        m.Changed += _ => raised++;
        ctx.Posts.Clear();

        s.IsPlaying = false;
        s.RaisePlayback();
        Assert.Equal(0, raised);           // no se invoca en el hilo del evento de WinRT…
        Assert.Single(ctx.Posts);
        ctx.RunAll();
        Assert.Equal(1, raised);           // …sino en el de la UI
    }

    [Fact]
    public void Dispose_detaches_everything_and_goes_quiet()
    {
        var (hub, s) = Setup();
        var m = New(hub);
        int raised = 0;
        m.Changed += _ => raised++;
        m.Dispose();

        Assert.True(hub.Disposed);
        Assert.Equal(0, hub.Subscribers);
        Assert.Equal(0, s.Subscribers);
        s.RaisePlayback();
        hub.Switch(new FakeSession());
        Assert.Equal(0, raised);
        m.Dispose();   // idempotente
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public List<(SendOrPostCallback Cb, object? State)> Posts { get; } = new();
        public override void Post(SendOrPostCallback d, object? state) => Posts.Add((d, state));
        public void RunAll()
        {
            foreach (var (cb, st) in Posts.ToArray()) cb(st);
            Posts.Clear();
        }
    }
}
