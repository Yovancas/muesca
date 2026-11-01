using System.Text.Json;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Ajustes del notch F5 (B8): reloj, mascota, música, letras, tareas, bandeja y contraste en <see cref="AppConfig"/>
/// (por defecto y de ida y vuelta), sus filas en la sección Notch de Ajustes, sus textos es/en, las páginas que se ofrecen
/// según los ajustes y que apagar Letras no haga ninguna petición.
/// </summary>
public class NotchF5SettingsTests
{
    private const int X = 16, W = 308;

    private static readonly string[] Keys =
    {
        "toggle:NotchClock", "toggle:NotchMascot", "toggle:NotchMusic", "toggle:NotchLyrics", "toggle:NotchTasks",
        "toggle:NotchShelfClear", "toggle:NotchContrast",
    };

    // ---------------------------------------------------------------- AppConfig

    [Fact]
    public void Defaults_match_the_plan()
    {
        var c = new AppConfig();
        Assert.True(c.NotchClock);
        Assert.True(c.NotchMascot);
        Assert.True(c.NotchMusic);
        Assert.True(c.NotchLyrics);
        Assert.True(c.NotchTasks);
        Assert.False(c.NotchShelfClearOnStart);
        Assert.True(c.NotchAdaptiveContrast);
    }

    [Fact]
    public void Settings_roundtrip()
    {
        var c = new AppConfig
        {
            NotchClock = false, NotchMascot = false, NotchMusic = false, NotchLyrics = false, NotchTasks = false,
            NotchShelfClearOnStart = true, NotchAdaptiveContrast = false,
        };
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c))!;
        Assert.False(back.NotchClock);
        Assert.False(back.NotchMascot);
        Assert.False(back.NotchMusic);
        Assert.False(back.NotchLyrics);
        Assert.False(back.NotchTasks);
        Assert.True(back.NotchShelfClearOnStart);
        Assert.False(back.NotchAdaptiveContrast);
    }

    [Fact]
    public void Old_config_without_the_keys_gets_the_defaults()
    {
        var back = JsonSerializer.Deserialize<AppConfig>("""{ "NotchEnabled": true }""")!;
        Assert.True(back.NotchClock);
        Assert.True(back.NotchMascot);
        Assert.True(back.NotchMusic);
        Assert.True(back.NotchLyrics);
        Assert.True(back.NotchTasks);
        Assert.False(back.NotchShelfClearOnStart);
        Assert.True(back.NotchAdaptiveContrast);
    }

    // ---------------------------------------------------------------- Ajustes

    private static int Draw(AppConfig cfg, Dictionary<string, Rectangle> rects, bool draw = true)
    {
        using var bmp = new Bitmap(W + X * 2, 1200);
        using var g = Graphics.FromImage(bmp);
        return DashboardSettingsView.Draw(g, draw, X, 0, W, cfg, Localization.Get("es"), Theme.Dark, Typography.Body,
            Typography.Caption, rects);
    }

    [Fact]
    public void Rows_are_registered_only_with_the_notch_on_and_lyrics_need_music()
    {
        var off = new Dictionary<string, Rectangle>();
        Draw(new AppConfig(), off);
        foreach (var k in Keys) Assert.DoesNotContain(k, off.Keys);

        var cfg = new AppConfig { NotchEnabled = true };
        var on = new Dictionary<string, Rectangle>();
        int yOn = Draw(cfg, on);
        foreach (var k in Keys) Assert.Contains(k, on.Keys);
        Assert.Equal(Draw(cfg, new(), draw: false), yOn);   // medir == pintar

        cfg.NotchMusic = false;
        var noMusic = new Dictionary<string, Rectangle>();
        int yNoMusic = Draw(cfg, noMusic);
        Assert.DoesNotContain("toggle:NotchLyrics", noMusic.Keys);   // atenuada e inerte sin música
        Assert.Contains("toggle:NotchMusic", noMusic.Keys);
        Assert.Equal(yOn, yNoMusic);                                  // misma geometría
    }

    [Fact]
    public void Actions_toggle_each_setting()
    {
        var c = new AppConfig();
        foreach (var k in Keys) DashboardSettingsView.ActionFor(k)!(c);
        Assert.False(c.NotchClock);
        Assert.False(c.NotchMascot);
        Assert.False(c.NotchMusic);
        Assert.False(c.NotchLyrics);
        Assert.False(c.NotchTasks);
        Assert.True(c.NotchShelfClearOnStart);
        Assert.False(c.NotchAdaptiveContrast);
        foreach (var k in Keys) DashboardSettingsView.ActionFor(k)!(c);
        Assert.True(c.NotchClock);
        Assert.False(c.NotchShelfClearOnStart);
    }

    [Fact]
    public void Texts_exist_in_spanish_and_english_and_lyrics_says_what_it_sends()
    {
        var en = Localization.Get("en");
        var es = Localization.Get("es");
        Assert.Equal("Reloj en reposo", es.NotchClock);
        Assert.Equal("Mascota", es.NotchMascot);
        Assert.Equal("Música", es.NotchMusic);
        Assert.Equal("Letras de LRCLIB", es.NotchLyricsSetting);
        Assert.Equal("Bandeja: vaciar al arrancar", es.NotchShelfClearOnStart);
        Assert.Equal("Contraste adaptativo", es.NotchAdaptiveContrast);
        Assert.Contains("lrclib.net", es.NotchLyricsSubtitle);
        Assert.Contains("lrclib.net", en.NotchLyricsSubtitle);
        Assert.Contains("título y artista", es.NotchLyricsSubtitle);
        Assert.NotEqual(en.NotchClock, es.NotchClock);
        Assert.NotEqual(en.NotchMascot, es.NotchMascot);
        Assert.NotEqual(en.NotchMusic, es.NotchMusic);
        Assert.NotEqual(en.NotchLyricsSetting, es.NotchLyricsSetting);
        Assert.NotEqual(en.NotchTasks, es.NotchTasks);
        Assert.NotEqual(en.NotchShelfClearOnStart, es.NotchShelfClearOnStart);
        Assert.NotEqual(en.NotchAdaptiveContrast, es.NotchAdaptiveContrast);
    }

    // ---------------------------------------------------------------- páginas según los ajustes

    [Fact]
    public void Pages_follow_music_and_tasks_settings()
    {
        Assert.Equal(NotchPages.Default, NotchPages.Effective(null, music: true, tasks: true));
        Assert.Equal(new[] { NotchPage.Claude, NotchPage.Usage, NotchPage.Tasks, NotchPage.Focus, NotchPage.Shelf },
            NotchPages.Effective(null, music: false, tasks: true));
        // Sin tareas, la página Foco sigue (el pomodoro no las necesita).
        Assert.Equal(new[] { NotchPage.Claude, NotchPage.Usage, NotchPage.Music, NotchPage.Focus, NotchPage.Shelf },
            NotchPages.Effective(null, music: true, tasks: false));
        Assert.Equal(new[] { NotchPage.Claude }, NotchPages.Effective(new[] { NotchPage.Tasks }, music: true, tasks: false));
    }

    // ---------------------------------------------------------------- letras apagadas: cero peticiones

    private sealed class CountingSource : ILyricsSource
    {
        public int Calls;
        public Task<LyricsResult> FetchAsync(string title, string artist, TimeSpan duration, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new LyricsResult(LyricsStatus.NotFound, Array.Empty<LyricLine>()));
        }
    }

    private static NowPlaying Track(string title) =>
        new(title, "Artist", "Album", true, TimeSpan.Zero, TimeSpan.FromSeconds(200), DateTime.UtcNow, null,
            AlbumAccent.Default, "Spotify.exe");

    [Fact]
    public void Lyrics_off_never_requests_anything()
    {
        Assert.False(LyricsController.Wanted(enabled: false, musicPageVisible: true, lyricsViewOpen: true));
        Assert.True(LyricsController.Wanted(enabled: true, musicPageVisible: true, lyricsViewOpen: true));
        Assert.False(LyricsController.Wanted(enabled: true, musicPageVisible: false, lyricsViewOpen: true));
        Assert.False(LyricsController.Wanted(enabled: true, musicPageVisible: true, lyricsViewOpen: false));

        var src = new CountingSource();
        using var c = new LyricsController(src);
        foreach (var title in new[] { "Uno", "Dos", "Tres" })
            for (int i = 0; i < 3; i++)
                c.Update(Track(title), LyricsController.Wanted(enabled: false, musicPageVisible: true, lyricsViewOpen: true));
        Assert.Equal(0, src.Calls);
        Assert.Equal(LyricsState.Idle, c.State);

        // Con el ajuste encendido, la misma secuencia sí pide (una vez por pista): el doble cuenta de verdad.
        c.Update(Track("Cuatro"), LyricsController.Wanted(enabled: true, musicPageVisible: true, lyricsViewOpen: true));
        Assert.Equal(1, src.Calls);
    }
}
