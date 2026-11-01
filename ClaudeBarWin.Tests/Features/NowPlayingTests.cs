using System.Drawing;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="NowPlaying.PositionAt"/>: la posición se interpola desde el ancla (<c>PositionAtUtc</c>) mientras
/// suena, sin sondear al reproductor; en pausa se queda quieta. Siempre dentro de [0, duración].
/// </summary>
public class NowPlayingTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 18, 0, 0, DateTimeKind.Utc);

    private static NowPlaying Song(bool playing, double posS, double durS) =>
        new("Título", "Artista", "Álbum", playing, TimeSpan.FromSeconds(posS), TimeSpan.FromSeconds(durS), T0,
            null, Color.LightGray, "Spotify.exe");

    [Fact]
    public void Paused_position_is_frozen()
    {
        var s = Song(playing: false, posS: 42, durS: 200);
        Assert.Equal(TimeSpan.FromSeconds(42), s.PositionAt(T0.AddMinutes(5)));
    }

    [Fact]
    public void Playing_position_interpolates_from_anchor()
    {
        var s = Song(playing: true, posS: 42, durS: 200);
        Assert.Equal(TimeSpan.FromSeconds(52.5), s.PositionAt(T0.AddSeconds(10.5)));
    }

    [Fact]
    public void Interpolated_position_is_clamped_to_duration()
    {
        var s = Song(playing: true, posS: 190, durS: 200);
        Assert.Equal(TimeSpan.FromSeconds(200), s.PositionAt(T0.AddMinutes(3)));
    }

    [Fact]
    public void Unknown_duration_only_clamps_at_zero()
    {
        var live = Song(playing: true, posS: 5, durS: 0);   // directo/radio: sin duración
        Assert.Equal(TimeSpan.FromSeconds(65), live.PositionAt(T0.AddMinutes(1)));
        var negative = Song(playing: false, posS: -3, durS: 0);
        Assert.Equal(TimeSpan.Zero, negative.PositionAt(T0));
    }

    [Fact]
    public void Clock_going_backwards_does_not_rewind()
    {
        var s = Song(playing: true, posS: 42, durS: 200);
        Assert.Equal(TimeSpan.FromSeconds(42), s.PositionAt(T0.AddSeconds(-30)));
    }
}
