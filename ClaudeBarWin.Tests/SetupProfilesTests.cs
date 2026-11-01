using System.Drawing;
using System.Text.Json;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Perfiles del asistente de instalación (<see cref="SetupProfiles"/>), su lista blanca de ajustes
/// (<see cref="SetupSettings"/>) y las recomendaciones según el equipo (<see cref="SetupRecommendations"/>).
/// </summary>
public class SetupProfilesTests
{
    /// <summary>Aplica solo los cambios del perfil (sin recomendaciones ni respuestas) sobre una config.</summary>
    internal static AppConfig ApplyProfile(SetupProfile profile, AppConfig? start = null)
    {
        var c = start ?? new AppConfig();
        foreach (var (key, value) in SetupProfiles.Changes(profile)) SetupSettings.Set(c, key, value);
        return c;
    }

    private static string Json(AppConfig c) => JsonSerializer.Serialize(c);

    // ---- identificadores ----

    [Fact]
    public void Ids_are_stable_and_roundtrip()
    {
        Assert.Equal("quota", SetupProfiles.Id(SetupProfile.QuotaOnly));
        Assert.Equal("coder", SetupProfiles.Id(SetupProfile.Coder));
        Assert.Equal("full", SetupProfiles.Id(SetupProfile.FullNotch));
        Assert.Equal("minimal", SetupProfiles.Id(SetupProfile.Minimal));
        Assert.Equal("custom", SetupProfiles.Id(SetupProfile.Custom));
        foreach (var p in SetupProfiles.All)
        {
            Assert.True(SetupProfiles.TryParse(SetupProfiles.Id(p), out var back));
            Assert.Equal(p, back);
        }
    }

    [Theory]
    [InlineData("CODER", true)]
    [InlineData(" full ", true)]
    [InlineData("gamer", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_ignores_case_and_rejects_unknown(string? id, bool ok) =>
        Assert.Equal(ok, SetupProfiles.TryParse(id, out _));

    // ---- lo que produce cada perfil ----

    [Fact]
    public void Every_profile_change_is_a_valid_catalog_setting()
    {
        foreach (var p in SetupProfiles.All)
            foreach (var (key, value) in SetupProfiles.Changes(p))
                Assert.True(SetupSettings.IsValid(key, value), $"{SetupProfiles.Id(p)}: {key}={value}");
    }

    [Fact]
    public void QuotaOnly_turns_the_notch_off_and_nothing_else()
    {
        var c = ApplyProfile(SetupProfile.QuotaOnly, new AppConfig { NotchEnabled = true });
        Assert.False(c.NotchEnabled);
        // Sobre los valores por defecto (notch ya apagado) no cambia nada.
        Assert.Equal(Json(new AppConfig()), Json(ApplyProfile(SetupProfile.QuotaOnly)));
        Assert.False(SetupProfiles.WantsLiveSessions(SetupProfile.QuotaOnly));
    }

    [Fact]
    public void Coder_turns_on_the_notch_with_sound_music_and_lyrics()
    {
        var c = ApplyProfile(SetupProfile.Coder);
        Assert.True(c.NotchEnabled);
        Assert.False(c.NotchMinimal);
        Assert.True(c.NotchSound);
        Assert.True(c.NotchMusic);
        Assert.True(c.NotchLyrics);
        // Lo que añade el notch completo, fuera.
        Assert.False(c.NotchCompactLyrics);
        Assert.False(c.NotchClock);
        Assert.False(c.NotchMascot);
        // Permisos opt-in (se marcan aparte) y las sesiones en vivo solo con los hooks.
        Assert.False(c.NotchPermissions);
        Assert.False(c.LiveSessionsEnabled);
        Assert.True(SetupProfiles.WantsLiveSessions(SetupProfile.Coder));
    }

    [Fact]
    public void FullNotch_is_coder_plus_compact_lyrics_clock_mascot_and_tasks()
    {
        var full = ApplyProfile(SetupProfile.FullNotch, new AppConfig
        {
            NotchCompactLyrics = false, NotchCompactLyricsArt = false, NotchCompactLyricsQuota = false,
            NotchClock = false, NotchMascot = false, NotchTasks = false,
        });
        Assert.True(full.NotchEnabled);
        Assert.False(full.NotchMinimal);
        Assert.True(full.NotchSound);
        Assert.True(full.NotchMusic);
        Assert.True(full.NotchLyrics);
        Assert.True(full.NotchCompactLyrics);
        Assert.True(full.NotchCompactLyricsArt);
        Assert.True(full.NotchCompactLyricsQuota);
        Assert.True(full.NotchClock);
        Assert.True(full.NotchMascot);
        Assert.True(full.NotchTasks);
        Assert.False(full.NotchPermissions);
        Assert.True(SetupProfiles.WantsLiveSessions(SetupProfile.FullNotch));

        // Todo lo que fija el de programar lo fija también el completo (salvo lo que el completo enciende).
        foreach (var (key, _) in SetupProfiles.Changes(SetupProfile.Coder))
            Assert.Contains(key, SetupProfiles.Changes(SetupProfile.FullNotch).Keys);
    }

    [Fact]
    public void Minimal_is_a_quiet_stripe_without_lyrics_or_motion()
    {
        var c = ApplyProfile(SetupProfile.Minimal, new AppConfig { NotchHideInFullscreen = false });
        Assert.True(c.NotchEnabled);
        Assert.True(c.NotchMinimal);
        Assert.True(c.NotchHideInFullscreen);
        Assert.False(c.NotchSound);
        Assert.True(c.ReduceMotion);
        Assert.False(c.NotchLyrics);
        Assert.False(c.NotchCompactLyrics);
        Assert.False(c.NotchClock);
        Assert.False(c.NotchMascot);
        Assert.False(SetupProfiles.WantsLiveSessions(SetupProfile.Minimal));
    }

    [Fact]
    public void Custom_changes_nothing_by_itself_but_offers_live_sessions()
    {
        Assert.Empty(SetupProfiles.Changes(SetupProfile.Custom));
        Assert.Equal(Json(new AppConfig()), Json(ApplyProfile(SetupProfile.Custom)));
        Assert.True(SetupProfiles.WantsLiveSessions(SetupProfile.Custom));
    }

    [Fact]
    public void Profiles_never_touch_settings_outside_the_notch()
    {
        foreach (var p in SetupProfiles.All)
        {
            var c = ApplyProfile(p, new AppConfig
            {
                Theme = "light", Language = "es", NotchQuotaThresholds = new[] { 70, 90 }, RefreshSeconds = 300,
                DashboardPosition = "TopLeft", NotchMonitor = @"\\.\DISPLAY2", NotchSizePercent = 130, NotchOpenOnClick = true,
            });
            Assert.Equal("light", c.Theme);
            Assert.Equal("es", c.Language);
            Assert.Equal(new[] { 70, 90 }, c.NotchQuotaThresholds);
            Assert.Equal(300, c.RefreshSeconds);
            Assert.Equal("TopLeft", c.DashboardPosition);
            Assert.Equal(@"\\.\DISPLAY2", c.NotchMonitor);
            Assert.Equal(130, c.NotchSizePercent);
            Assert.True(c.NotchOpenOnClick);
        }
    }

    [Fact]
    public void Reduce_motion_is_only_switched_on_by_minimal_never_off()
    {
        // Accesibilidad: quien la tenía activa la conserva con cualquier perfil.
        foreach (var p in SetupProfiles.All)
            Assert.True(ApplyProfile(p, new AppConfig { ReduceMotion = true }).ReduceMotion, SetupProfiles.Id(p));
        Assert.True(ApplyProfile(SetupProfile.Minimal).ReduceMotion);
        Assert.False(ApplyProfile(SetupProfile.Coder).ReduceMotion);
    }

    // ---- lista blanca de ajustes ----

    [Theory]
    [InlineData("Language", "es", true)]
    [InlineData("Language", "system", true)]
    [InlineData("Language", "zh-Hant", true)]
    [InlineData("Language", "xx", false)]
    [InlineData("NotchSizePercent", 115, true)]
    [InlineData("NotchSizePercent", 0, true)]
    [InlineData("NotchSizePercent", 120, false)]
    [InlineData("NotchEnabled", true, true)]
    [InlineData("NotchEnabled", 1, false)]
    [InlineData("NotchEnabled", "true", false)]
    [InlineData("NotchMonitor", "", true)]
    [InlineData(@"NotchMonitor", @"\\.\DISPLAY2", true)]
    [InlineData("NotchMonitor", "a\nb", false)]
    public void IsValid_checks_type_and_range(string key, object value, bool ok) =>
        Assert.Equal(ok, SetupSettings.IsValid(key, value));

    [Fact]
    public void Monitor_names_longer_than_a_device_name_are_rejected() =>
        Assert.False(SetupSettings.IsValid("NotchMonitor", new string('x', 65)));

    [Theory]
    [InlineData("LiveSessionsEnabled")]       // va con los hooks, nunca suelto
    [InlineData("NotchMusixmatchApiKey")]     // un secreto: nunca desde el instalador
    [InlineData("Theme")]
    [InlineData("NotchQuotaThresholds")]
    [InlineData("notchenabled")]              // las claves distinguen mayúsculas, como el JSON de la config
    public void Settings_outside_the_whitelist_are_unknown(string key)
    {
        Assert.False(SetupSettings.IsKnown(key));
        Assert.Null(SetupSettings.TypeOf(key));
        Assert.False(SetupSettings.IsValid(key, true));
    }

    [Fact]
    public void Every_catalog_key_is_a_config_property_that_reads_back_what_was_set()
    {
        var props = typeof(AppConfig).GetProperties().ToDictionary(p => p.Name);
        foreach (var key in SetupSettings.Keys)
        {
            Assert.Contains(key, props.Keys);
            var type = SetupSettings.TypeOf(key)!;
            Assert.Equal(props[key].PropertyType, type);

            var c = new AppConfig();
            object value = type == typeof(bool) ? !(bool)SetupSettings.Get(c, key)
                : type == typeof(int) ? 150
                : key == nameof(AppConfig.Language) ? "ja" : @"\\.\DISPLAY3";
            Assert.True(SetupSettings.Set(c, key, value), key);
            Assert.Equal(value, SetupSettings.Get(c, key));
            Assert.Equal(value, props[key].GetValue(c));
            Assert.False(SetupSettings.Set(c, key, value), $"{key}: el mismo valor no es un cambio");
        }
    }

    // ---- recomendaciones según el equipo ----

    internal static SetupMonitor Mon(string name, int x, int y, int w, int h, bool primary = false, int dpi = 96) =>
        new(name, new Rectangle(x, y, w, h), primary, dpi);

    internal static SetupMachine Machine(bool claude, params SetupMonitor[] monitors) => new(monitors, claude);

    private static readonly SetupMonitor FullHd = Mon(@"\\.\DISPLAY1", 0, 0, 1920, 1080, primary: true);

    [Fact]
    public void Recommended_profile_depends_on_claude_code()
    {
        Assert.Equal(SetupProfile.Coder, SetupRecommendations.Profile(Machine(true, FullHd)));
        Assert.Equal(SetupProfile.QuotaOnly, SetupRecommendations.Profile(Machine(false, FullHd)));
    }

    [Fact]
    public void Live_sessions_are_offered_only_with_claude_code_and_a_profile_that_uses_them()
    {
        var with = Machine(true, FullHd);
        var without = Machine(false, FullHd);
        Assert.True(SetupRecommendations.OfferLiveSessions(SetupProfile.Coder, with));
        Assert.True(SetupRecommendations.OfferLiveSessions(SetupProfile.FullNotch, with));
        Assert.True(SetupRecommendations.OfferLiveSessions(SetupProfile.Custom, with));
        Assert.False(SetupRecommendations.OfferLiveSessions(SetupProfile.Minimal, with));
        Assert.False(SetupRecommendations.OfferLiveSessions(SetupProfile.QuotaOnly, with));
        Assert.False(SetupRecommendations.OfferLiveSessions(SetupProfile.Coder, without));
    }

    [Fact]
    public void The_monitor_question_needs_two_monitors()
    {
        Assert.False(SetupRecommendations.AskMonitor(Machine(true)));
        Assert.False(SetupRecommendations.AskMonitor(Machine(true, FullHd)));
        Assert.True(SetupRecommendations.AskMonitor(Machine(true, FullHd, Mon(@"\\.\DISPLAY2", 1920, 0, 1920, 1080))));
    }

    [Theory]
    [InlineData(1920, 96, 100)]
    [InlineData(2560, 96, 115)]
    [InlineData(3840, 96, 130)]
    [InlineData(3840, 144, 115)]   // 4K al 150 %: 2560 lógicos
    [InlineData(3840, 192, 100)]   // 4K al 200 %: 1920 lógicos
    [InlineData(2560, 120, 100)]   // 1440p al 125 %: 2048 lógicos
    [InlineData(5120, 120, 130)]   // 5K al 125 %: 4096 lógicos
    public void Auto_size_follows_the_logical_width_so_the_dpi(int width, int dpi, int expected) =>
        Assert.Equal(expected, SetupRecommendations.AutoSizePercent(Mon("m", 0, 0, width, 1080, true, dpi)));

    [Fact]
    public void A_large_single_screen_opens_with_hover()
    {
        Assert.False(SetupRecommendations.OpenOnClick(Machine(true, FullHd), ""));
        Assert.False(SetupRecommendations.OpenOnClick(Machine(true, Mon("m", 0, 0, 1440, 900, true)), ""));
    }

    [Theory]
    [InlineData(1366, 96)]     // portátil 1366 × 768
    [InlineData(1920, 144)]    // Full HD al 150 %: 1280 lógicos
    [InlineData(2560, 192)]    // 2560 × 1600 al 200 %: 1280 lógicos
    public void A_small_logical_screen_opens_with_a_click(int width, int dpi) =>
        Assert.True(SetupRecommendations.OpenOnClick(Machine(true, Mon("m", 0, 0, width, 1000, true, dpi)), ""));

    [Fact]
    public void A_monitor_right_above_the_notch_recommends_click()
    {
        var above = Mon(@"\\.\DISPLAY2", 0, -1080, 1920, 1080);
        var m = Machine(true, FullHd, above);
        Assert.True(SetupRecommendations.HasMonitorAbove(m, FullHd));
        Assert.True(SetupRecommendations.OpenOnClick(m, ""));
    }

    [Fact]
    public void A_monitor_above_that_does_not_cover_the_notch_or_sits_aside_keeps_hover()
    {
        // Encima pero a la izquierda (no cubre el centro), y otro a la derecha.
        var aboveLeft = Mon(@"\\.\DISPLAY2", -1500, -1080, 1920, 1080);
        var right = Mon(@"\\.\DISPLAY3", 1920, 0, 1920, 1080);
        var m = Machine(true, FullHd, aboveLeft, right);
        Assert.False(SetupRecommendations.HasMonitorAbove(m, FullHd));
        Assert.False(SetupRecommendations.OpenOnClick(m, ""));
    }

    [Fact]
    public void The_recommendation_follows_the_chosen_monitor()
    {
        var laptop = Mon(@"\\.\DISPLAY2", 1920, 0, 2560, 1600, dpi: 192);   // 200 %: 1280 lógicos
        var m = Machine(true, FullHd, laptop);
        Assert.False(SetupRecommendations.OpenOnClick(m, ""));
        Assert.True(SetupRecommendations.OpenOnClick(m, @"\\.\display2"));
        Assert.Equal(laptop, SetupRecommendations.NotchMonitor(m, @"\\.\DISPLAY2"));
        Assert.Equal(100, SetupRecommendations.AutoSizePercent(laptop));
    }

    [Fact]
    public void An_unknown_or_empty_monitor_falls_back_to_the_primary()
    {
        var second = Mon(@"\\.\DISPLAY2", -1920, 0, 1920, 1080);
        var m = Machine(true, second, FullHd);
        Assert.Equal(FullHd, SetupRecommendations.NotchMonitor(m, ""));
        Assert.Equal(FullHd, SetupRecommendations.NotchMonitor(m, @"\\.\DISPLAY9"));
        // Sin ninguno marcado como principal: el que empieza en 0,0.
        var unflagged = Machine(true, second, FullHd with { IsPrimary = false });
        Assert.Equal(FullHd with { IsPrimary = false }, SetupRecommendations.NotchMonitor(unflagged, null));
        Assert.Null(SetupRecommendations.NotchMonitor(Machine(true), ""));
        Assert.False(SetupRecommendations.OpenOnClick(Machine(true), ""));
    }

    [Fact]
    public void Recommended_answers_depend_on_profile_and_machine()
    {
        var small = Machine(true, Mon("m", 0, 0, 1366, 768, true));
        var quota = SetupRecommendations.Values(SetupProfile.QuotaOnly, small);
        Assert.Equal(new[] { nameof(AppConfig.Language) }, quota.Keys.ToArray());
        Assert.Equal("system", quota[nameof(AppConfig.Language)]);

        var coder = SetupRecommendations.Values(SetupProfile.Coder, small);
        Assert.Equal("system", coder[nameof(AppConfig.Language)]);
        Assert.Equal("", coder[nameof(AppConfig.NotchMonitor)]);
        Assert.Equal(0, coder[nameof(AppConfig.NotchSizePercent)]);
        Assert.True((bool)coder[nameof(AppConfig.NotchOpenOnClick)]);
        Assert.False((bool)SetupRecommendations.Values(SetupProfile.Coder, Machine(true, FullHd))[nameof(AppConfig.NotchOpenOnClick)]);
        foreach (var (key, value) in coder) Assert.True(SetupSettings.IsValid(key, value), key);
        Assert.True(SetupRecommendations.StartWithWindows);
    }
}
