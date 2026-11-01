using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.UI.Onboarding;
using static ClaudeBarWin.Tests.SetupProfilesTests;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El asistente en movimiento sin ventana (<see cref="OnboardingScene"/>): teclado, clics, saltar con confirmación, el aviso
/// de error, el relevo y la vista previa; y sus textos (<see cref="OnboardingText"/>), ya formateados en es/en.
/// </summary>
public sealed class OnboardingSceneTests : IDisposable
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);
    private static readonly SetupMachine Desk = Machine(true, Mon(@"\\.\DISPLAY1", 0, 0, 2560, 1440, primary: true));

    private readonly OnboardingPainter _painter = new(Localization.Get("es"));
    private readonly List<SetupAnswers> _applied = new();
    private SetupStatus _status = SetupStatus.Applied;
    private TimeSpan _now;

    public void Dispose() => _painter.Dispose();

    private OnboardingScene Scene(bool fresh = true, SetupMachine? machine = null, string language = "es", bool reduce = false)
    {
        var services = new OnboardingServices(a =>
        {
            _applied.Add(a);
            return _status == SetupStatus.Applied
                ? new SetupOutcome(SetupStatus.Applied, "ok", Array.Empty<string>(), Array.Empty<string>())
                : new SetupOutcome(_status, "config.json bloqueado", Array.Empty<string>(), Array.Empty<string>());
        }, () => null, () => Array.Empty<SessionState>());
        var cfg = new AppConfig { Language = language }.Normalize();
        var scene = new OnboardingScene(new OnboardingWizard(new OnboardingContext(machine ?? Desk, cfg, false, false, fresh)),
            Localization.Get(language), _painter, services, reduce, localNow: () => new DateTime(2026, 10, 29, 18, 55, 0));
        scene.Place(1f, OnboardingPlacement.Surface(1f));
        _now = TimeSpan.Zero;
        scene.Start(_now);
        Run(scene, TimeSpan.FromSeconds(3), s => s.Choreography.Stage == OnboardingStage.Open);
        Assert.Equal(OnboardingStage.Open, scene.Choreography.Stage);
        return scene;
    }

    private void Run(OnboardingScene scene, TimeSpan max, Func<OnboardingScene, bool>? until = null)
    {
        var end = _now + max;
        while (_now < end && !(until?.Invoke(scene) ?? false))
        {
            _now += Frame;
            scene.Step(_now);
        }
    }

    private static Point Center(RectangleF r) => new((int)(r.Left + r.Width / 2f), (int)(r.Top + r.Height / 2f));

    // ---- teclado

    [Fact]
    public void Enter_moves_on_and_tab_walks_the_controls_of_the_page()
    {
        var scene = Scene();
        Assert.True(scene.Key(Keys.Enter, false, _now));
        Assert.Equal(OnboardingPage.Profile, scene.Wizard.Page);
        Run(scene, TimeSpan.FromMilliseconds(300));

        scene.Key(Keys.Tab, false, _now);
        var f = scene.Frame();
        Assert.True(f.ShowFocus);
        Assert.Equal(new OnboardingHit(OnboardingControl.Card, 0), f.Focus);
        scene.Key(Keys.Tab, true, _now);
        Assert.Equal(new OnboardingHit(OnboardingControl.Skip), scene.Frame().Focus);
    }

    [Fact]
    public void Holding_enter_does_not_walk_the_pages_and_apply()
    {
        var scene = Scene();
        Assert.True(scene.Key(Keys.Enter, false, _now));
        Assert.Equal(OnboardingPage.Profile, scene.Wizard.Page);
        for (int i = 0; i < 20; i++)
        {
            Assert.True(scene.Key(Keys.Enter, false, _now, repeat: true));   // tragada, no llega a otro sitio
            Assert.True(scene.Key(Keys.Space, false, _now, repeat: true));
        }
        Assert.Equal(OnboardingPage.Profile, scene.Wizard.Page);
        Assert.Empty(_applied);
        Assert.Null(scene.Result);

        // Las flechas mantenidas sí repiten (cambiar de tarjeta es inofensivo).
        scene.Key(Keys.Down, false, _now, repeat: true);
        Assert.Equal(SetupProfile.FullNotch, scene.Wizard.Profile);
    }

    [Fact]
    public void The_autorepeat_is_bit_30_of_the_key_message()
    {
        Assert.False(OnboardingForm.IsAutoRepeat((IntPtr)0x001C0001));   // Enter, primera pulsación
        Assert.True(OnboardingForm.IsAutoRepeat((IntPtr)0x401C0001));    // Enter mantenido
    }

    [Fact]
    public void Arrows_choose_another_card_and_space_uses_the_focused_control()
    {
        var scene = Scene();
        scene.Key(Keys.Enter, false, _now);
        Run(scene, TimeSpan.FromMilliseconds(300));
        scene.Key(Keys.Down, false, _now);
        Assert.Equal(SetupProfile.FullNotch, scene.Wizard.Profile);
        Assert.Equal(new OnboardingHit(OnboardingControl.Card, (int)SetupProfile.FullNotch), scene.Frame().Focus);

        scene.Key(Keys.Enter, false, _now);   // Enter en una tarjeta: «Siguiente»
        Run(scene, TimeSpan.FromMilliseconds(300));
        Assert.Equal(OnboardingPage.Features, scene.Wizard.Page);
        Assert.True(scene.Wizard.FeatureOn(SetupFeature.Mascot));
        // Con el teclado en uso, la página nueva empieza con el foco en su primer control: la casilla del notch.
        Assert.Equal(new OnboardingHit(OnboardingControl.Feature, (int)SetupFeature.Notch), scene.Frame().Focus);
        scene.Key(Keys.Space, false, _now);
        Assert.False(scene.Wizard.FeatureOn(SetupFeature.Notch));
    }

    [Fact]
    public void Escape_asks_first_and_skipping_applies_the_recommended_profile_without_live_sessions()
    {
        var scene = Scene();
        scene.Key(Keys.Escape, false, _now);
        Run(scene, TimeSpan.FromMilliseconds(300));
        Assert.Equal(OnboardingOverlay.ConfirmSkip, scene.Wizard.Overlay);
        Assert.NotNull(scene.Frame().View.Overlay);
        Assert.Empty(_applied);

        scene.Key(Keys.Enter, false, _now);   // el foco está en «Seguir configurando»: el botón seguro
        Assert.Equal(OnboardingOverlay.None, scene.Wizard.Overlay);
        Assert.Empty(_applied);

        scene.Key(Keys.Escape, false, _now);
        scene.Key(Keys.Tab, false, _now);     // «Saltar»
        scene.Key(Keys.Enter, false, _now);
        var a = Assert.Single(_applied);
        Assert.Equal(SetupProfile.Coder, a.Profile);
        Assert.False(a.LiveSessions);
        Assert.True(scene.Result!.Applied);
        Assert.True(scene.TakeHandoff());     // sin sitio donde aterrizar: el relevo va ya
        Assert.False(scene.TakeHandoff());
        Run(scene, TimeSpan.FromSeconds(2), s => s.IsGone);
        Assert.True(scene.IsGone);
    }

    [Fact]
    public void Skipping_a_set_up_again_closes_without_changing_anything()
    {
        var scene = Scene(fresh: false);
        scene.Key(Keys.Escape, false, _now);
        Assert.Equal(new OnboardingHit(OnboardingControl.OverlaySecondary), scene.Frame().Focus);
        scene.Activate(new OnboardingHit(OnboardingControl.OverlayPrimary), _now);
        Assert.Empty(_applied);
        Assert.Same(OnboardingResult.Unchanged, scene.Result);
        Assert.False(scene.Result!.Applied);
    }

    [Fact]
    public void The_usage_counter_line_is_on_the_welcome_and_its_switch_travels_with_skip()
    {
        var scene = Scene();
        var v = scene.Frame().View;
        Assert.Equal(OnboardingPage.Welcome, v.Page);
        Assert.NotNull(v.UsageStats);
        Assert.Equal(Localization.Get("es").UsageStatsSubtitle, v.UsageStats!.Label);
        Assert.True(v.UsageStats.On);   // activado por defecto, y a la vista

        var row = OnboardingGeometry.UsageStatsRow(scene.Pill, 1f);
        scene.PointerDown(Center(row));
        scene.PointerUp(Center(row), _now);
        Assert.False(scene.Wizard.UsageStats);
        Assert.False(scene.Frame().View.UsageStats!.On);

        scene.Key(Keys.Escape, false, _now);
        scene.Activate(new OnboardingHit(OnboardingControl.OverlayPrimary), _now);   // «Saltar» en la primera vez: aplica
        Assert.False(Assert.Single(_applied).UsageStats);
    }

    // ---- ratón

    [Fact]
    public void A_click_acts_only_when_released_over_the_same_control()
    {
        var scene = Scene();
        var next = OnboardingGeometry.Next(scene.Pill, 1f);
        scene.PointerDown(Center(next));
        scene.PointerUp(new Point(5, 5), _now);
        Assert.Equal(OnboardingPage.Welcome, scene.Wizard.Page);

        scene.PointerDown(Center(next));
        scene.PointerUp(Center(next), _now);
        Assert.Equal(OnboardingPage.Profile, scene.Wizard.Page);
        Run(scene, TimeSpan.FromMilliseconds(300));

        var card = OnboardingGeometry.Card(scene.Pill, 1f, (int)SetupProfile.Minimal);
        Assert.True(scene.PointerMove(Center(card)));
        Assert.True(scene.OverControl);
        scene.PointerDown(Center(card));
        scene.PointerUp(Center(card), _now);
        Assert.Equal(SetupProfile.Minimal, scene.Wizard.Profile);
    }

    [Fact]
    public void Hovering_a_card_lights_it_up_with_the_accent()
    {
        var scene = Scene();
        scene.Key(Keys.Enter, false, _now);
        Run(scene, TimeSpan.FromMilliseconds(300));
        scene.PointerMove(Center(OnboardingGeometry.Card(scene.Pill, 1f, 2)));
        Run(scene, TimeSpan.FromMilliseconds(400));
        var glow = scene.Frame().Glow!;
        Assert.True(glow[2] > 0.95f);
        Assert.True(glow[0] < 0.05f);
        scene.PointerLeave();
        Run(scene, TimeSpan.FromMilliseconds(400));
        Assert.True(scene.Frame().Glow![2] < 0.05f);
    }

    [Fact]
    public void Disabled_controls_do_not_react()
    {
        var scene = Scene();
        scene.Key(Keys.Enter, false, _now);
        scene.Key(Keys.Enter, false, _now);
        Run(scene, TimeSpan.FromMilliseconds(300));
        scene.Activate(new OnboardingHit(OnboardingControl.Feature, (int)SetupFeature.Notch), _now);   // notch fuera
        Assert.False(scene.Usable(new OnboardingHit(OnboardingControl.Feature, (int)SetupFeature.Clock)));
        var clock = OnboardingGeometry.ToggleRow(scene.Pill, 1f, SetupFeature.Clock);
        Assert.False(scene.PointerMove(Center(clock)) && scene.OverControl);
        bool before = scene.Wizard.FeatureOn(SetupFeature.Clock);
        scene.Activate(new OnboardingHit(OnboardingControl.Feature, (int)SetupFeature.Clock), _now);
        Assert.Equal(before, scene.Wizard.FeatureOn(SetupFeature.Clock));
    }

    // ---- terminar

    [Fact]
    public void If_applying_fails_the_error_covers_the_page_and_retry_tries_again()
    {
        var scene = Scene(machine: Machine(false, Mon(@"\\.\DISPLAY1", 0, 0, 1920, 1080, primary: true)));
        _status = SetupStatus.Failed;
        while (!scene.Wizard.IsLastPage) scene.Key(Keys.Enter, false, _now);
        scene.Key(Keys.Enter, false, _now);   // «Empezar»
        Assert.Single(_applied);
        Assert.Equal(OnboardingOverlay.Error, scene.Wizard.Overlay);
        Assert.Contains("config.json bloqueado", scene.View.Overlay!.Text);
        Assert.Null(scene.Result);

        _status = SetupStatus.Applied;
        scene.Activate(new OnboardingHit(OnboardingControl.OverlayPrimary), _now);   // «Reintentar»
        Assert.Equal(2, _applied.Count);
        Assert.True(scene.Result!.Applied);
    }

    [Fact]
    public void Holding_escape_does_not_close_the_error_and_open_the_skip_question()
    {
        var scene = Scene();
        _status = SetupStatus.Failed;
        scene.Key(Keys.Escape, false, _now);
        scene.Key(Keys.Tab, false, _now);     // «Saltar»
        scene.Key(Keys.Enter, false, _now);
        Assert.Equal(OnboardingOverlay.Error, scene.Wizard.Overlay);

        for (int i = 0; i < 20; i++)
            Assert.True(scene.Key(Keys.Escape, false, _now, repeat: true));   // tragada
        Assert.Equal(OnboardingOverlay.Error, scene.Wizard.Overlay);
        Assert.Single(_applied);

        // Una pulsación de verdad sí cierra el aviso de error, y mantenerla no abre el de «Saltar».
        scene.Key(Keys.Escape, false, _now);
        Assert.Equal(OnboardingOverlay.None, scene.Wizard.Overlay);
        for (int i = 0; i < 20; i++)
            scene.Key(Keys.Escape, false, _now, repeat: true);
        Assert.Equal(OnboardingOverlay.None, scene.Wizard.Overlay);
    }

    [Fact]
    public void Retrying_a_failed_skip_retries_the_skip_and_not_what_the_pages_have()
    {
        // Programo con Claude Code: las páginas traen las sesiones en vivo marcadas; «Saltar» no las instala.
        var scene = Scene();
        Assert.True(scene.Wizard.Answers().LiveSessions);
        _status = SetupStatus.Failed;
        scene.Key(Keys.Escape, false, _now);
        scene.Key(Keys.Tab, false, _now);     // «Saltar»
        scene.Key(Keys.Enter, false, _now);
        Assert.Equal(OnboardingOverlay.Error, scene.Wizard.Overlay);
        Assert.False(Assert.Single(_applied).LiveSessions);

        _status = SetupStatus.Applied;
        scene.Activate(new OnboardingHit(OnboardingControl.OverlayPrimary), _now);   // «Reintentar»
        Assert.Equal(2, _applied.Count);
        Assert.Equal(_applied[0], _applied[1]);
        Assert.False(_applied[1].LiveSessions);
        Assert.True(scene.Result!.Applied);
    }

    [Fact]
    public void Finishing_shrinks_to_the_landing_notch_and_paints_it_with_the_real_painter()
    {
        var scene = Scene();
        var surface = OnboardingPlacement.Surface(1f);
        float notchScale = 1.15f;
        scene.Landing = () => new OnboardingLanding(NotchMode.Idle, NotchLayout.TargetSize(NotchMode.Idle, notchScale, 0f),
            notchScale, surface.Width / 2f + 0.5f);
        while (!scene.Wizard.IsLastPage) scene.Key(Keys.Enter, false, _now);
        scene.Key(Keys.Enter, false, _now);
        Assert.Single(_applied);
        Assert.False(scene.TakeHandoff());   // aún encogiéndose
        Run(scene, TimeSpan.FromSeconds(2), s => s.Choreography.Stage == OnboardingStage.Landed);
        Run(scene, OnboardingMotion.LandIn + Frame);
        var f = scene.Frame();
        Assert.NotNull(f.Landed);
        Assert.Equal(notchScale, f.Landed!.Scale);
        Assert.Equal(NotchMode.Idle, f.Landed.Mode);
        Assert.Equal(surface.Width / 2f + 0.5f, f.CenterX);
        Assert.True(scene.TakeHandoff());
    }

    // ---- vista previa e idioma

    [Fact]
    public void The_profile_page_shows_a_live_preview_and_quota_only_shows_the_tray()
    {
        var scene = Scene();
        scene.Key(Keys.Enter, false, _now);
        Run(scene, TimeSpan.FromMilliseconds(600));
        var preview = scene.Frame().Preview;
        Assert.NotNull(preview);
        Assert.Equal(NotchMode.Idle, preview!.Mode);
        Assert.Equal(1f * OnboardingGeometry.PreviewScale, preview.Scale, 3);
        Assert.NotNull(scene.NextWakeIn(_now));

        scene.Activate(new OnboardingHit(OnboardingControl.Card, (int)SetupProfile.QuotaOnly), _now);
        Run(scene, TimeSpan.FromMilliseconds(600));
        Assert.Null(scene.Frame().Preview);
        Assert.Null(scene.NextWakeIn(_now));
        scene.Activate(new OnboardingHit(OnboardingControl.Card, (int)SetupProfile.Coder), _now);
        scene.Key(Keys.Enter, false, _now);
        Run(scene, TimeSpan.FromMilliseconds(300));
        Assert.Null(scene.Frame().Preview);   // fuera de la página de perfiles, nada (aunque el perfil tenga notch)
        Assert.Null(scene.NextWakeIn(_now));
    }

    [Fact]
    public void Choosing_another_language_switches_the_wizard_texts_live()
    {
        var scene = Scene();
        Assert.Equal("Siguiente", scene.View.Next);
        while (scene.Wizard.Page != OnboardingPage.Settings) scene.Key(Keys.Enter, false, _now);
        while (scene.Wizard.Language != "en") scene.Activate(new OnboardingHit(OnboardingControl.Language), _now);
        Assert.Equal("Next", scene.View.Next);
        Assert.Equal("Recommended settings", scene.View.Title);
        Assert.Equal("en", scene.Strings.Culture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void With_reduce_motion_switches_and_pages_change_at_once()
    {
        var scene = Scene(reduce: true);
        scene.Key(Keys.Enter, false, _now);
        scene.Key(Keys.Enter, false, _now);
        scene.Step(_now);
        var f = scene.Frame();
        Assert.Null(f.Previous);
        Assert.Equal(1f, f.PageBlend);
        scene.Activate(new OnboardingHit(OnboardingControl.Feature, (int)SetupFeature.Clock), _now);
        scene.Step(_now);
        Assert.Equal(1f, scene.Frame().Knobs![(int)SetupFeature.Clock]);
    }

    // ---- textos

    [Fact]
    public void Texts_are_built_once_per_state_in_spanish_and_english()
    {
        foreach (var lang in new[] { "es", "en" })
        {
            var s = Localization.Get(lang);
            var w = new OnboardingWizard(new OnboardingContext(Desk, new AppConfig().Normalize(), false, false, true, @"C:\u\.claude"));
            var v = OnboardingText.Build(w, s);
            Assert.Equal(OnboardingPage.Welcome, v.Page);
            Assert.Equal(s.OnbNext, v.Next);
            Assert.Equal(5, v.Cards.Count);
            Assert.True(v.Cards[(int)SetupProfile.Coder].Recommended);
            Assert.All(v.Cards, c => Assert.False(string.IsNullOrWhiteSpace(c.Title + c.Line)));
            Assert.Equal(SetupFeatures.All.Count, v.Toggles.Count);
            Assert.All(v.Toggles, t => Assert.False(string.IsNullOrWhiteSpace(t.Label)));
            Assert.Contains(@"C:\u\.claude", v.ClaudeInfo);

            w.Next();
            w.Next();
            v = OnboardingText.Build(w, s);
            Assert.Contains(s.OnbProfileNames[(int)SetupProfile.Coder], v.Subtitle);

            while (!w.IsLastPage) w.Next();
            v = OnboardingText.Build(w, s);
            Assert.Equal(s.OnbStart, v.Next);
            Assert.Equal(s.OnbProfileNames[(int)SetupProfile.Coder], v.Summary[0].Value);
            Assert.Contains(v.Summary, r => r.Label == s.OnbSumLive && r.Value == s.OnbLiveInstall);
            Assert.True(v.Summary.Sum(r => r.Lines) <= OnboardingGeometry.SummaryMaxLines);
        }
    }

    [Fact]
    public void Without_Claude_Code_the_preview_caption_says_so_and_the_notch_off_note_appears()
    {
        var s = Localization.Get("es");
        var w = new OnboardingWizard(new OnboardingContext(Machine(false, Mon(@"\\.\DISPLAY1", 0, 0, 1920, 1080, primary: true)),
            new AppConfig().Normalize(), false, false, true));
        var v = OnboardingText.Build(w, s);
        Assert.Equal(s.OnbClaudeMissing, v.PreviewCaption);
        Assert.False(v.DetectedOk);
        w.Next();
        w.Next();
        w.Next();   // ajustes, con Solo cuota: notch apagado
        v = OnboardingText.Build(w, s);
        Assert.Equal(s.OnbNotchOff, v.NotchOffNote);
        Assert.All(v.Settings.Where(r => r.Row is OnboardingControl.Size or OnboardingControl.Open), r => Assert.False(r.Enabled));
        Assert.DoesNotContain(v.Settings, r => r.Row == OnboardingControl.Monitor);   // un solo monitor
        Assert.Contains(v.Summary, r => r.Label == s.OnbSumNotch && r.Value == s.OnbNo);
    }

    [Fact]
    public void The_recommended_option_is_marked_like_in_the_installer()
    {
        var s = Localization.Get("en");
        var w = new OnboardingWizard(new OnboardingContext(Machine(true, Mon(@"\\.\DISPLAY1", 0, 0, 1366, 768, primary: true)),
            new AppConfig().Normalize(), false, false, true));
        var v = OnboardingText.Build(w, s);
        var language = v.Settings.Single(r => r.Row == OnboardingControl.Language);
        Assert.EndsWith($"({s.OnbRecommendedTag})", language.Value);
        var open = v.Settings.Single(r => r.Row == OnboardingControl.Open);
        Assert.Equal(1, open.Recommended);   // pantalla pequeña: con un clic
        Assert.Equal(1, open.Selected);
        Assert.Contains("1366", open.Hint);
        Assert.EndsWith($"({s.OnbRecommendedTag})", v.Live.Label);
    }
}
