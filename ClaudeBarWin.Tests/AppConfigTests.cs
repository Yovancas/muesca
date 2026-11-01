using System.Text.Json;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests;

public class AppConfigTests
{
    [Fact]
    public void Defaults_are_opt_in_for_live_sessions()
    {
        var c = new AppConfig();
        Assert.False(c.LiveSessionsEnabled);
        Assert.True(c.ShowMascot);
        Assert.True(c.SuppressWhenFocused);
        Assert.Equal("cat", c.MascotKind);
    }

    [Fact]
    public void Roundtrips_through_json()
    {
        var c = new AppConfig { LiveSessionsEnabled = true, ShowMascot = false, SuppressWhenFocused = false, MascotKind = "cat" };
        var json = JsonSerializer.Serialize(c);
        var back = JsonSerializer.Deserialize<AppConfig>(json)!;
        Assert.True(back.LiveSessionsEnabled);
        Assert.False(back.ShowMascot);
        Assert.False(back.SuppressWhenFocused);
    }

    [Fact]
    public void Missing_keys_fall_back_to_defaults()
    {
        var back = JsonSerializer.Deserialize<AppConfig>("{}")!;
        Assert.False(back.LiveSessionsEnabled);
        Assert.True(back.ShowMascot);
    }

    // ---- Cuota con el token OAuth: `claude -p .` gasta un mensaje → apagado por defecto ----

    [Fact]
    public void Cli_refresh_is_off_by_default_also_for_an_old_config_json()
    {
        Assert.False(new AppConfig().QuotaCliRefresh);
        // config.json de antes del ajuste (sin la clave): también apagado, aunque antes fuese automático.
        Assert.False(JsonSerializer.Deserialize<AppConfig>("""{"RefreshSeconds":60}""")!.QuotaCliRefresh);
        Assert.True(JsonSerializer.Deserialize<AppConfig>("""{"QuotaCliRefresh":true}""")!.QuotaCliRefresh);
    }

    // ---- Reduce motion (F3 Tarea 7): default false = animaciones ON (decisión de Yovan) ----

    [Fact]
    public void ReduceMotion_defaults_to_false_animations_on()
    {
        var c = new AppConfig();
        Assert.False(c.ReduceMotion);
    }

    [Fact]
    public void ReduceMotion_missing_key_falls_back_to_false()
    {
        var back = JsonSerializer.Deserialize<AppConfig>("{}")!;
        Assert.False(back.ReduceMotion);
    }

    [Fact]
    public void ReduceMotion_roundtrips_through_json()
    {
        var c = new AppConfig { ReduceMotion = true };
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c))!;
        Assert.True(back.ReduceMotion);
    }

    // ---- Notch (F1): apagado por defecto, ajustes seguros y round-trip ----

    [Fact]
    public void Notch_defaults_are_off_and_safe()
    {
        var c = new AppConfig();
        Assert.False(c.NotchEnabled);
        Assert.False(c.NotchOpenOnClick);
        Assert.False(c.NotchMinimal);
        Assert.Equal(15, c.NotchAutoMinimalSeconds);
        Assert.True(c.NotchHideInFullscreen);
        Assert.True(c.NotchSound);
        Assert.Equal(0, c.NotchSizePercent);
        Assert.Equal("", c.NotchMonitor);
        Assert.Equal(new[] { 80, 95 }, c.NotchQuotaThresholds);
        Assert.False(c.NotchPermissions);   // F2: opt-in, sin entrada «permission» ni pipe hasta que se active
    }

    [Fact]
    public void Notch_settings_roundtrip()
    {
        var c = new AppConfig
        {
            NotchEnabled = true, NotchOpenOnClick = true, NotchMinimal = true, NotchHideInFullscreen = false,
            NotchSound = false, NotchSizePercent = 130, NotchMonitor = @"\\.\DISPLAY2", NotchQuotaThresholds = new[] { 70, 90 }, NotchPermissions = true,
        };
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c))!;
        Assert.True(back.NotchEnabled);
        Assert.True(back.NotchOpenOnClick);
        Assert.True(back.NotchMinimal);
        Assert.False(back.NotchHideInFullscreen);
        Assert.False(back.NotchSound);
        Assert.Equal(130, back.NotchSizePercent);
        Assert.Equal(@"\\.\DISPLAY2", back.NotchMonitor);
        Assert.Equal(new[] { 70, 90 }, back.NotchQuotaThresholds);
        Assert.True(back.NotchPermissions);
    }

    [Fact]
    public void Notch_missing_keys_fall_back_to_defaults()
    {
        var back = JsonSerializer.Deserialize<AppConfig>("{}")!.Normalize();
        Assert.False(back.NotchEnabled);
        Assert.True(back.NotchHideInFullscreen);
        Assert.Equal(new[] { 80, 95 }, back.NotchQuotaThresholds);
    }

    [Fact]
    public void Normalize_brings_notch_values_into_range()
    {
        // config.json se edita a mano: tamaños fuera de la lista vuelven a Auto, el monitor nunca es null y
        // los umbrales quedan en (0, 100], sin duplicados y ordenados (null o ninguno válido ⇒ 80/95).
        var c = new AppConfig { NotchSizePercent = 999, NotchMonitor = null!, NotchQuotaThresholds = new[] { 95, 0, 80, 95, 120 } }.Normalize();
        Assert.Equal(0, c.NotchSizePercent);
        Assert.Equal("", c.NotchMonitor);
        Assert.Equal(new[] { 80, 95 }, c.NotchQuotaThresholds);

        Assert.Equal(115, new AppConfig { NotchSizePercent = 115 }.Normalize().NotchSizePercent);
        Assert.Equal(new[] { 80, 95 }, new AppConfig { NotchQuotaThresholds = null! }.Normalize().NotchQuotaThresholds);
        Assert.Equal(new[] { 80, 95 }, new AppConfig { NotchQuotaThresholds = new[] { 0, 120 } }.Normalize().NotchQuotaThresholds);
    }

    // ---- Modo del monitor del notch (29-oct): fijo por defecto, por nombre en config.json y tolerante ----

    // ---- Pomodoro (página Foco)

    [Fact]
    public void Pomodoro_defaults_are_25_5_15_every_4_with_sound_and_stopped()
    {
        var c = new AppConfig();
        Assert.Equal(FocusDurations.Default, c.NotchFocusDurations);
        Assert.True(c.NotchFocusSound);
        Assert.Null(c.NotchFocusState);
        var old = JsonSerializer.Deserialize<AppConfig>("{\"NotchEnabled\": true}")!.Normalize();
        Assert.Equal(FocusDurations.Default, old.NotchFocusDurations);
        Assert.Null(old.NotchFocusState);
    }

    [Fact]
    public void Pomodoro_durations_from_a_hand_edited_file_are_brought_into_range()
    {
        var c = JsonSerializer.Deserialize<AppConfig>(
            "{\"NotchFocusMinutes\": 0, \"NotchShortBreakMinutes\": 900, \"NotchLongBreakMinutes\": -2, \"NotchLongBreakEvery\": 99}")!
            .Normalize();
        Assert.Equal((1, 180, 1, 8), (c.NotchFocusMinutes, c.NotchShortBreakMinutes, c.NotchLongBreakMinutes, c.NotchLongBreakEvery));
    }

    [Fact]
    public void A_running_pomodoro_survives_saving_and_loading_the_config_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cb-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "config.json");
            var utc = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
            var now = new FocusNow(utc, TimeSpan.FromMinutes(5), new DateTime(2026, 10, 30, 10, 0, 0));
            var timer = new PomodoroTimer();
            timer.Link(Guid.NewGuid());
            timer.Start(now);
            new AppConfig { NotchFocusState = timer.Save() }.Save(path);

            var back = AppConfig.Load(path);
            Assert.NotNull(back.NotchFocusState);
            Assert.Equal(utc.AddMinutes(25), back.NotchFocusState!.EndsAtUtc);
            Assert.Equal(DateTimeKind.Utc, back.NotchFocusState.EndsAtUtc!.Value.Kind);
            var restored = PomodoroTimer.Restore(back.NotchFocusState, back.NotchFocusDurations,
                now with { Utc = utc.AddMinutes(10), Mono = TimeSpan.Zero });
            Assert.Equal(FocusRun.Running, restored.Run);
            Assert.Equal(TimeSpan.FromMinutes(15), restored.Remaining(now with { Utc = utc.AddMinutes(10), Mono = TimeSpan.Zero }));
            Assert.Equal(timer.TaskId, restored.TaskId);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temporal */ } }
    }

    [Fact]
    public void Notch_monitor_mode_defaults_to_fixed()
    {
        Assert.Equal(NotchMonitorMode.Fixed, new AppConfig().NotchMonitorMode);
        Assert.Equal(NotchMonitorMode.Fixed, JsonSerializer.Deserialize<AppConfig>("{}")!.Normalize().NotchMonitorMode);
    }

    [Theory]
    [InlineData(NotchMonitorMode.Fixed)]
    [InlineData(NotchMonitorMode.FollowActive)]
    [InlineData(NotchMonitorMode.AllMonitors)]
    public void Notch_monitor_mode_roundtrips_by_name(NotchMonitorMode mode)
    {
        var json = JsonSerializer.Serialize(new AppConfig { NotchMonitorMode = mode });
        Assert.Contains($"\"NotchMonitorMode\":\"{mode}\"", json);
        Assert.Equal(mode, JsonSerializer.Deserialize<AppConfig>(json)!.NotchMonitorMode);
    }

    [Theory]
    [InlineData("\"allmonitors\"", NotchMonitorMode.AllMonitors)]   // a mano, sin cuidar las mayúsculas
    [InlineData("1", NotchMonitorMode.FollowActive)]                  // como número
    [InlineData("\"Everywhere\"", NotchMonitorMode.Fixed)]           // desconocido
    [InlineData("7", NotchMonitorMode.Fixed)]
    [InlineData("null", NotchMonitorMode.Fixed)]
    [InlineData("{\"x\":1}", NotchMonitorMode.Fixed)]
    public void Notch_monitor_mode_is_tolerant_and_never_breaks_the_rest_of_the_file(string value, NotchMonitorMode expected)
    {
        var c = JsonSerializer.Deserialize<AppConfig>($"{{\"NotchMonitorMode\":{value},\"NotchEnabled\":true}}")!.Normalize();
        Assert.Equal(expected, c.NotchMonitorMode);
        Assert.True(c.NotchEnabled);   // el campo siguiente se lee igual
    }

    // ---- config.json bloqueado un momento (antivirus, OneDrive): quien va a guardar encima tiene que enterarse ----

    [Fact]
    public void A_locked_config_throws_for_LoadOrThrow_and_gives_untouched_defaults_for_Load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cb-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "config.json");
            new AppConfig { Theme = "light", NotchEnabled = true }.SaveOrThrow(path);
            var before = File.ReadAllBytes(path);
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => AppConfig.LoadOrThrow(path));
                var c = AppConfig.Load(path);              // la app sigue con los valores por defecto, sin tocar el disco
                Assert.Equal("system", c.Theme);
            }
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal("light", AppConfig.LoadOrThrow(path).Theme);   // ya libre, se lee como siempre
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ } }
    }

    [Fact]
    public void LoadOrThrow_without_a_file_gives_defaults_and_writes_nothing()
    {
        var path = Path.Combine(Path.GetTempPath(), "cb-cfg-" + Guid.NewGuid().ToString("N"), "config.json");
        Assert.Equal("system", AppConfig.LoadOrThrow(path).Theme);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SaveOrThrow_reports_what_Save_swallows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cb-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "config.json");
            Directory.CreateDirectory(path);   // una carpeta donde va el archivo: no se puede reemplazar
            Assert.ThrowsAny<Exception>(() => new AppConfig().SaveOrThrow(path));
            new AppConfig().Save(path);        // la app: se anota y sigue
            Assert.True(Directory.Exists(path));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ } }
    }

    [Fact]
    public void Normalize_puts_an_out_of_range_monitor_mode_back_to_fixed()
    {
        Assert.Equal(NotchMonitorMode.Fixed, new AppConfig { NotchMonitorMode = (NotchMonitorMode)42 }.Normalize().NotchMonitorMode);
        Assert.Equal(NotchMonitorMode.AllMonitors,
            new AppConfig { NotchMonitorMode = NotchMonitorMode.AllMonitors }.Normalize().NotchMonitorMode);
    }
}
