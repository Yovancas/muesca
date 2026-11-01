using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.Services.Telemetry;
using ClaudeBarWin.UI;
using ClaudeBarWin.UI.Onboarding;
using static ClaudeBarWin.Tests.SetupProfilesTests;

namespace ClaudeBarWin.Tests;

/// <summary>El contador anónimo de uso en la interfaz: Ajustes → Sistema, la bienvenida y los textos de todos los idiomas.</summary>
public class UsageStatsUiTests
{
    private static OnboardingWizard Wizard(AppConfig? cfg = null, bool fresh = true) =>
        new(new OnboardingContext(Machine(true, Mon(@"\\.\DISPLAY1", 0, 0, 2560, 1440, primary: true)),
            cfg ?? new AppConfig { OnboardingPending = true }.Normalize(), false, false, fresh));

    // ------------------------------------------------------------ textos

    [Fact]
    public void Every_language_has_its_own_texts()
    {
        var en = Localization.Get("en");
        foreach (var (code, _) in Localization.Languages)
        {
            var s = Localization.Get(code);
            foreach (var text in new[] { s.UsageStatsLabel, s.UsageStatsSubtitle, s.UsageStatsPrivacy, s.UsageStatsNotice })
            {
                Assert.False(string.IsNullOrWhiteSpace(text), code);
                if (code != "en") Assert.DoesNotContain(text, new[] { en.UsageStatsLabel, en.UsageStatsSubtitle, en.UsageStatsPrivacy, en.UsageStatsNotice });
            }
        }
        Assert.Equal("Cuenta cuánta gente usa Muesca, sin saber quién. Una vez al día manda solo versión, edición e idioma.",
            Localization.Get("es").UsageStatsSubtitle);
    }

    [Fact]
    public void The_footer_seal_no_longer_promises_no_telemetry()
    {
        foreach (var (code, _) in Localization.Languages)
        {
            string seal = Localization.Get(code).LocalSeal.ToLowerInvariant();
            foreach (var word in new[] { "telemetr", "télémétrie", "テレメトリ", "텔레메트리", "遙測" })
                Assert.DoesNotContain(word, seal);
        }
    }

    // ------------------------------------------------------------ Ajustes

    [Fact]
    public void Settings_show_the_switch_and_the_privacy_link_and_measure_equals_paint()
    {
        foreach (var lang in new[] { "es", "en", "de", "ja" })
        {
            using var bmp = new Bitmap(340, 2600);
            using var g = Graphics.FromImage(bmp);
            var cfg = new AppConfig { Language = lang }.Normalize();
            var rects = new Dictionary<string, Rectangle>();
            int measured = DashboardSettingsView.Draw(g, false, 16, 0, 308, cfg, Localization.Get(lang), Theme.Dark,
                Typography.Body, Typography.Caption, rects, "0.5.0");
            Assert.True(rects.ContainsKey("toggle:UsageStats"), lang);
            Assert.True(rects.ContainsKey(DashboardSettingsView.PrivacyKey), lang);
            // Debajo del interruptor de las tarifas (la misma sección de privacidad) y antes del idioma.
            Assert.True(rects["toggle:PricingOnline"].Bottom <= rects["toggle:UsageStats"].Top);
            Assert.True(rects["toggle:UsageStats"].Bottom <= rects[DashboardSettingsView.PrivacyKey].Top);
            Assert.True(rects[DashboardSettingsView.PrivacyKey].Bottom <= rects["cycle:lang"].Top);
            int painted = DashboardSettingsView.Draw(g, true, 16, 0, 308, cfg, Localization.Get(lang), Theme.Dark,
                Typography.Body, Typography.Caption, new Dictionary<string, Rectangle>(), "0.5.0");
            Assert.Equal(measured, painted);
        }
    }

    [Fact]
    public void The_settings_switch_turns_it_off_and_on_and_counts_as_seen()
    {
        var cfg = new AppConfig().Normalize();
        Assert.True(cfg.UsageStatsEnabled);
        DashboardSettingsView.ActionFor("toggle:UsageStats")!(cfg);
        Assert.False(cfg.UsageStatsEnabled);
        Assert.True(cfg.UsageStats!.NoticeSeen);
        Assert.Null(cfg.UsageStats.NoticeUtc);
        DashboardSettingsView.ActionFor("toggle:UsageStats")!(cfg);
        Assert.True(cfg.UsageStatsEnabled);
        Assert.Null(DashboardSettingsView.ActionFor(DashboardSettingsView.PrivacyKey));   // lo abre el host
    }

    // ------------------------------------------------------------ bienvenida

    [Fact]
    public void The_wizard_starts_from_the_config_and_sends_the_choice_in_both_answers()
    {
        var w = Wizard();
        Assert.True(w.UsageStats);
        Assert.True(w.Answers().UsageStats);
        w.ToggleUsageStats();
        Assert.False(w.Answers().UsageStats);
        Assert.False(w.SkipAnswers().UsageStats);

        var off = new AppConfig { UsageStatsEnabled = false }.Normalize();
        Assert.False(Wizard(off, fresh: false).UsageStats);
    }

    [Fact]
    public void Applying_the_welcome_saves_the_switch_and_marks_the_notice_seen()
    {
        var cfg = new AppConfig { OnboardingPending = true }.Normalize();
        var w = Wizard(cfg);
        w.ToggleUsageStats();
        var ok = new SetupOutcome(SetupStatus.Applied, "ok", Array.Empty<string>(), Array.Empty<string>());
        SetupEnvironment.ApplyOnboarding(w.Answers(), _ => ok, change => change(cfg));
        Assert.False(cfg.OnboardingPending);
        Assert.False(cfg.UsageStatsEnabled);
        Assert.True(cfg.UsageStats!.NoticeSeen);
        Assert.True(cfg.UsageStats.NewInstall);   // la instalación sigue siendo nueva para su primer aviso
        Assert.True(UsagePing.MaySend(true, true, cfg.OnboardingPending, cfg.UsageStats, DateTime.UtcNow));
    }

    [Fact]
    public void A_failed_welcome_does_not_mark_anything()
    {
        var cfg = new AppConfig { OnboardingPending = true }.Normalize();
        var failed = new SetupOutcome(SetupStatus.Failed, "x", Array.Empty<string>(), Array.Empty<string>());
        SetupEnvironment.ApplyOnboarding(Wizard(cfg).Answers(), _ => failed, change => change(cfg));
        Assert.True(cfg.OnboardingPending);
        Assert.False(cfg.UsageStats!.NoticeSeen);
    }

    [Fact]
    public void Answers_without_the_switch_leave_the_config_alone()
    {
        var cfg = new AppConfig { OnboardingPending = true, UsageStatsEnabled = false }.Normalize();
        var answers = Wizard(cfg).Answers() with { UsageStats = null };
        var ok = new SetupOutcome(SetupStatus.Applied, "ok", Array.Empty<string>(), Array.Empty<string>());
        SetupEnvironment.ApplyOnboarding(answers, _ => ok, change => change(cfg));
        Assert.False(cfg.UsageStatsEnabled);
        Assert.False(cfg.UsageStats!.NoticeSeen);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void The_welcome_line_sits_between_the_chip_and_the_keys_hint_and_takes_clicks(float s)
    {
        var pill = OnboardingGeometry.PillRect(OnboardingGeometry.Width * s, OnboardingGeometry.Height * s, 1000f);
        var row = OnboardingGeometry.UsageStatsRow(pill, s);
        Assert.True(OnboardingGeometry.Detected(pill, s).Bottom <= row.Top);
        Assert.True(row.Bottom <= OnboardingGeometry.KeysHint(pill, s).Top);
        Assert.True(row.Left >= pill.Left && row.Right <= pill.Right);
        var (sw, text) = OnboardingGeometry.UsageStatsParts(row, s);
        Assert.True(sw.Right < text.Left);
        Assert.True(sw.Top >= row.Top && sw.Bottom <= row.Bottom);
        var shape = new OnboardingShape(5, false, OnboardingOverlay.None);
        var hit = OnboardingGeometry.HitTest(new Point((int)(text.Left + 10 * s), (int)(row.Top + row.Height / 2)),
            OnboardingPage.Welcome, pill, s, shape);
        Assert.Equal(new OnboardingHit(OnboardingControl.UsageStats), hit);
    }

    [Fact]
    public void The_welcome_line_fits_in_two_lines_in_every_language()
    {
        // Con la fuente de las notas del asistente (caption) a 1x: el texto cabe en el ancho del texto y en dos líneas.
        using var bmp = new Bitmap(10, 10);
        using var g = Graphics.FromImage(bmp);
        using var font = new Font(Typography.Body.FontFamily.Name, 11.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        var pill = OnboardingGeometry.PillRect(OnboardingGeometry.Width, OnboardingGeometry.Height, 400f);
        var (_, text) = OnboardingGeometry.UsageStatsParts(OnboardingGeometry.UsageStatsRow(pill, 1f), 1f);
        foreach (var (code, _) in Localization.Languages)
        {
            var size = g.MeasureString(Localization.Get(code).UsageStatsSubtitle, font, (int)text.Width, StringFormat.GenericTypographic);
            Assert.True(size.Height <= text.Height + 0.5f, $"{code}: {size.Height} > {text.Height}");
        }
    }

    [Fact]
    public void Knob_slots_are_distinct()
    {
        var knobs = new[] { OnboardingControl.Startup, OnboardingControl.LiveSessions, OnboardingControl.Permissions,
            OnboardingControl.UsageStats }.Select(c => OnboardingPainter.KnobIndex(c)).ToList();
        Assert.Equal(knobs.Count, knobs.Distinct().Count());
        Assert.All(knobs, k => Assert.InRange(k, SetupFeatures.All.Count, OnboardingPainter.KnobCount - 1));
    }
}
