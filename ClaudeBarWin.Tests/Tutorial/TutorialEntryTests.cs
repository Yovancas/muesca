using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.UI;
using ClaudeBarWin.UI.Onboarding;
using static ClaudeBarWin.Tests.SetupProfilesTests;

namespace ClaudeBarWin.Tests.Tutorial;

/// <summary>Por dónde se llega al tutorial: «Ver el tutorial al terminar (2 min)» en el resumen de la bienvenida (marcado la
/// primera vez, no al volver a configurar) y el enlace de Ajustes › Acerca de.</summary>
public sealed class TutorialEntryTests : IDisposable
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);
    private static readonly SetupMachine Desk = Machine(true, Mon(@"\\.\DISPLAY1", 0, 0, 2560, 1440, primary: true));
    private readonly OnboardingPainter _painter = new(Localization.Get("es"));
    private TimeSpan _now;

    public void Dispose() => _painter.Dispose();

    private static OnboardingWizard Wizard(bool fresh) =>
        new(new OnboardingContext(Desk, new AppConfig { Language = "es" }.Normalize(), false, false, fresh));

    [Fact]
    public void The_tutorial_comes_ticked_the_first_time_only()
    {
        var first = Wizard(fresh: true);
        Assert.True(first.ShowTutorial);
        first.ToggleTutorial();
        Assert.False(first.ShowTutorial);
        Assert.False(Wizard(fresh: false).ShowTutorial);
    }

    [Fact]
    public void The_summary_has_the_tutorial_switch()
    {
        var w = Wizard(fresh: true);
        var v = OnboardingText.Build(w, Localization.Get("es"));
        Assert.NotNull(v.Tutorial);
        Assert.Equal("Ver el tutorial al terminar (2 min)", v.Tutorial!.Label);
        Assert.True(v.Tutorial.On);
        var pill = OnboardingGeometry.PillRect(OnboardingGeometry.Width, OnboardingGeometry.Height, 400f);
        var shape = new OnboardingShape(w.Pages.Count, w.AskMonitor, OnboardingOverlay.None);
        var controls = OnboardingGeometry.Controls(OnboardingPage.Summary, pill, 1f, shape);
        var row = Assert.Single(controls, c => c.Hit.Control == OnboardingControl.Tutorial).Rect;
        Assert.False(row.IntersectsWith(OnboardingGeometry.SummaryNote(pill, 1f)));
        Assert.True(row.Bottom <= OnboardingGeometry.Back(pill, 1f).Top);
        Assert.Contains(OnboardingGeometry.FocusOrder(OnboardingPage.Summary, shape, _ => true, _ => 0),
            h => h.Control == OnboardingControl.Tutorial);
        var knobs = new[] { OnboardingControl.Startup, OnboardingControl.LiveSessions, OnboardingControl.Permissions,
            OnboardingControl.UsageStats, OnboardingControl.Tutorial }.Select(c => OnboardingPainter.KnobIndex(c)).ToList();
        Assert.Equal(knobs.Count, knobs.Distinct().Count());
        Assert.All(knobs, k => Assert.InRange(k, 0, OnboardingPainter.KnobCount - 1));
    }

    private OnboardingScene Scene(bool fresh)
    {
        var services = new OnboardingServices(_ => new SetupOutcome(SetupStatus.Applied, "ok", Array.Empty<string>(), Array.Empty<string>()),
            () => null, () => Array.Empty<SessionState>());
        var scene = new OnboardingScene(Wizard(fresh), Localization.Get("es"), _painter, services, reduceMotion: true);
        scene.Place(1f, OnboardingPlacement.Surface(1f));
        _now = TimeSpan.Zero;
        scene.Start(_now);
        for (int i = 0; i < 60; i++) scene.Step(_now += Frame);
        return scene;
    }

    private void Finish(OnboardingScene scene)
    {
        while (scene.Result is null && scene.Wizard.Page != OnboardingPage.Summary) scene.Key(Keys.Enter, false, _now);
        Assert.Equal(OnboardingPage.Summary, scene.Wizard.Page);
    }

    [Fact]
    public void Starting_with_the_switch_on_asks_for_the_tutorial()
    {
        var scene = Scene(fresh: true);
        Finish(scene);
        scene.Key(Keys.Enter, false, _now);
        Assert.True(scene.Result!.Applied);
        Assert.True(scene.Result.Tutorial);
    }

    [Fact]
    public void Unticking_it_or_skipping_does_not()
    {
        var scene = Scene(fresh: true);
        Finish(scene);
        scene.Activate(new OnboardingHit(OnboardingControl.Tutorial), _now);
        Assert.False(scene.Wizard.ShowTutorial);
        scene.Key(Keys.Enter, false, _now);
        Assert.False(scene.Result!.Tutorial);

        var skipped = Scene(fresh: true);
        skipped.Key(Keys.Escape, false, _now);
        skipped.Key(Keys.Tab, false, _now);
        skipped.Key(Keys.Enter, false, _now);
        Assert.True(skipped.Result!.Applied);
        Assert.False(skipped.Result.Tutorial);
    }

    [Fact]
    public void Settings_links_to_the_tutorial_in_about()
    {
        using var bmp = new Bitmap(340, 1400);
        using var g = Graphics.FromImage(bmp);
        var cfg = new AppConfig { Language = "es", Theme = "dark" }.Normalize();
        var rects = new Dictionary<string, Rectangle>();
        DashboardSettingsView.Draw(g, true, 16, 0, 308, cfg, Localization.Get("es"), Theme.Dark, Typography.Body, Typography.Caption,
            rects, "0.5.0");
        Assert.True(rects.TryGetValue(DashboardSettingsView.TutorialKey, out var link));
        Assert.True(rects.TryGetValue("special:importtheme", out var import));
        Assert.True(link.Y < import.Y, "el tutorial, en Acerca de, antes de importar el tema");
        Assert.True(rects.TryGetValue("toggle:Startup", out var startup));
        Assert.True(link.Y > startup.Y);
        Assert.Null(DashboardSettingsView.ActionFor(DashboardSettingsView.TutorialKey));   // la enruta la bandeja
    }
}
