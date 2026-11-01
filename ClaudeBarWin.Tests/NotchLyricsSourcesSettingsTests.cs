using System.Text.Json;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Fuentes de letras retiradas el 29-oct (NetEase y Musixmatch; solo queda LRCLIB): un <c>config.json</c> de antes con
/// <c>NotchLyricsNetEase</c> y la clave de Musixmatch (cifrada con DPAPI, «dpapi1:…») se carga sin errores y sin perder
/// el resto, las dos claves desaparecen al guardar, y en Ajustes → Notch ya no hay filas ni textos de esas fuentes.
/// </summary>
public sealed class NotchLyricsSourcesSettingsTests : IDisposable
{
    private const int X = 16, W = 308;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-lyrics-cfg-" + Guid.NewGuid().ToString("N"));

    // Como lo escribía la versión con las fuentes extra (clave cifrada con DPAPI y NetEase activado).
    private const string OldConfig = """
        {
          "Language": "es",
          "NotchEnabled": true,
          "NotchLyrics": true,
          "NotchCompactLyricsQuota": false,
          "NotchLyricsNetEase": true,
          "NotchMusixmatchApiKey": "dpapi1:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA",
          "NotchTasks": false
        }
        """;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string WriteOld()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, OldConfig);
        return path;
    }

    [Fact]
    public void Old_config_with_the_retired_keys_loads_without_errors_and_keeps_the_rest()
    {
        var path = WriteOld();
        var c = AppConfig.Load(path);

        Assert.False(File.Exists(path + ".bad"));   // no se tomó por un config.json roto
        Assert.Equal("es", c.Language);
        Assert.True(c.NotchEnabled);
        Assert.True(c.NotchLyrics);
        Assert.False(c.NotchCompactLyricsQuota);
        Assert.False(c.NotchTasks);
    }

    [Fact]
    public void Retired_keys_disappear_when_saving()
    {
        var path = WriteOld();
        AppConfig.Load(path).Save(path);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("NetEase", json);
        Assert.DoesNotContain("Musixmatch", json);
        Assert.DoesNotContain("dpapi1:", json);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("NotchLyricsNetEase", out _));
        Assert.False(doc.RootElement.TryGetProperty("NotchMusixmatchApiKey", out _));
        Assert.True(doc.RootElement.GetProperty("NotchEnabled").GetBoolean());   // lo demás sigue
    }

    [Theory]
    [InlineData("clave-en-claro")]
    [InlineData("dpapi1:no-es-base64!")]
    [InlineData("")]
    public void Any_old_key_value_is_ignored(string stored)
    {
        var c = JsonSerializer.Deserialize<AppConfig>(
            $"{{\"NotchMusixmatchApiKey\":\"{stored}\",\"NotchLyricsNetEase\":true,\"NotchMusic\":false}}")!;
        Assert.False(c.NotchMusic);
        Assert.DoesNotContain("NetEase", JsonSerializer.Serialize(c));
        Assert.DoesNotContain("Musixmatch", JsonSerializer.Serialize(c));
    }

    [Fact]
    public void Settings_have_no_rows_for_the_retired_sources()
    {
        var cfg = new AppConfig { NotchEnabled = true };
        var rects = new Dictionary<string, Rectangle>();
        using var bmp = new Bitmap(W + X * 2, 1600);
        using var g = Graphics.FromImage(bmp);
        DashboardSettingsView.Draw(g, true, X, 0, W, cfg, Localization.Get("es"), Theme.Dark, Typography.Body,
            Typography.Caption, rects);

        Assert.Contains("toggle:NotchLyrics", rects.Keys);
        Assert.DoesNotContain(rects.Keys, k => k.Contains("NetEase", StringComparison.OrdinalIgnoreCase)
            || k.Contains("musixmatch", StringComparison.OrdinalIgnoreCase));
        Assert.Null(DashboardSettingsView.ActionFor("toggle:NotchLyricsNetEase"));
        Assert.Null(DashboardSettingsView.ActionFor("special:musixmatchkey"));
    }

    [Fact]
    public void Lyrics_subtitle_only_mentions_lrclib()
    {
        var es = Localization.Get("es");
        var en = Localization.Get("en");
        Assert.Equal("Envía título y artista a lrclib.net", es.NotchLyricsSubtitle);
        Assert.Equal("Sends title and artist to lrclib.net", en.NotchLyricsSubtitle);
        Assert.Null(typeof(Strings).GetProperty("NotchLyricsNetEase"));
        Assert.Null(typeof(Strings).GetProperty("NotchMusixmatchKey"));
    }
}
