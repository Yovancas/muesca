using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Onboarding;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Coreografía del asistente (<see cref="OnboardingMotion"/>): baja con más rebote que el notch, se despliega, se queda quieta
/// abierta (0 % de CPU), se encoge hasta el notch de reposo y avisa una vez para el relevo; con reducir movimiento, solo
/// fundidos. Y las escenas de la vista previa de cada perfil (<see cref="ProfilePreview"/>).
/// </summary>
public class OnboardingMotionTests
{
    private const float S = 1.25f;
    private static readonly SizeF Idle = NotchLayout.TargetSize(NotchMode.Idle, S, 0f);
    private static readonly SizeF Wizard = new(680 * S, 440 * S);
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(1000.0 / 60);

    /// <summary>Avanza a 60 fps hasta <paramref name="until"/> (o como mucho <paramref name="max"/>) y devuelve el instante.</summary>
    private static TimeSpan Run(OnboardingMotion m, TimeSpan now, Func<OnboardingMotion, bool> until, TimeSpan max,
        Action<OnboardingMotion>? each = null)
    {
        var end = now + max;
        while (now < end && !until(m))
        {
            now += Frame;
            m.Step(now);
            each?.Invoke(m);
        }
        return now;
    }

    [Fact]
    public void It_drops_from_the_top_edge_with_a_bigger_bounce_than_the_notch()
    {
        var m = new OnboardingMotion();
        m.Start(Idle, Wizard, S, TimeSpan.Zero);
        Assert.Equal(OnboardingStage.Dropping, m.Stage);
        Assert.Equal(MathF.Round(44 * S), m.Width);
        Assert.Equal(0f, m.Height);
        Assert.Equal(0f, m.PillAlpha);

        float maxH = 0f;
        var now = Run(m, TimeSpan.Zero, x => x.Stage != OnboardingStage.Dropping, TimeSpan.FromSeconds(2),
            x => { if (x.Stage == OnboardingStage.Dropping) maxH = Math.Max(maxH, x.Height); });
        // El Abrir del notch se pasa un 2,9 %; aquí, más de un 10 %.
        Assert.True(maxH > Idle.Height * 1.1f, $"pico {maxH} con reposo {Idle.Height}");
        Assert.Equal(OnboardingStage.Holding, m.Stage);
        Assert.Equal(1f, m.LogoAlpha);
        Assert.Equal(0f, m.ContentAlpha);
        Assert.True(now < OnboardingMotion.DropMax + Frame + Frame);
    }

    [Fact]
    public void It_unfolds_to_the_wizard_size_and_then_stays_still()
    {
        var m = new OnboardingMotion();
        m.Start(Idle, Wizard, S, TimeSpan.Zero);
        float maxH = 0f, maxSquash = 0f;
        bool contentBeforeReveal = false;
        var now = Run(m, TimeSpan.Zero, x => x.Stage == OnboardingStage.Open, TimeSpan.FromSeconds(4), x =>
        {
            maxH = Math.Max(maxH, x.Height);
            maxSquash = Math.Max(maxSquash, Math.Abs(x.Squash));
            if (x.ContentAlpha > 0f && x.Height < Wizard.Height * 0.5f) contentBeforeReveal = true;
            Assert.False(x.Interactive && x.ContentAlpha < 1f);
        });
        Assert.Equal(OnboardingStage.Open, m.Stage);
        Assert.Equal(MathF.Round(Wizard.Width), m.Width);
        Assert.Equal(MathF.Round(Wizard.Height), m.Height);
        Assert.True(maxH > Wizard.Height, "el despliegue se pasa un poco y vuelve");
        Assert.True(maxSquash > 0f, "squash-stretch mientras el ancho abre");
        Assert.False(contentBeforeReveal);
        Assert.Equal(1f, m.ContentAlpha);
        Assert.Equal(0f, m.ContentOffsetY);
        Assert.Equal(1f, m.PillAlpha);
        Assert.True(m.Interactive);
        Assert.False(m.IsAnimating);   // abierto y quieto: el bucle se para
        Assert.True(now < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Finishing_shrinks_to_the_idle_notch_hands_off_once_and_then_ends()
    {
        var m = new OnboardingMotion();
        m.Start(Idle, Wizard, S, TimeSpan.Zero);
        var now = Run(m, TimeSpan.Zero, x => x.Stage == OnboardingStage.Open, TimeSpan.FromSeconds(4));
        var final = NotchLayout.TargetSize(NotchMode.Idle, S * 1.15f, 0f);   // el notch real, a su propia escala
        m.Finish(final, now);
        Assert.Equal(OnboardingStage.Collapsing, m.Stage);
        Assert.False(m.Interactive);

        float minH = float.MaxValue;
        now = Run(m, now, x => x.Stage == OnboardingStage.Landed, TimeSpan.FromSeconds(2),
            x => minH = Math.Min(minH, x.Height));
        Assert.Equal(OnboardingStage.Landed, m.Stage);
        Assert.Equal(MathF.Round(final.Width), m.Width);
        Assert.Equal(MathF.Round(final.Height), m.Height);
        Assert.True(minH >= MathF.Round(final.Height) - 0.5f, "Cerrar no se pasa");
        Assert.Equal(0f, m.ContentAlpha);

        int handoffs = 0;
        now = Run(m, now, x => x.IsGone, TimeSpan.FromSeconds(3), x => { if (x.TakeHandoff()) handoffs++; });
        Assert.Equal(1, handoffs);
        Assert.True(m.IsGone);
        Assert.Equal(1f, m.LandedAlpha);
        Assert.False(m.TakeHandoff());
    }

    [Fact]
    public void A_single_long_step_from_landed_to_gone_still_hands_off_once()
    {
        // El bucle de la ventana no corre durante un menú o un arrastre: el paso siguiente al aterrizar llega tarde y ya es Gone.
        var m = new OnboardingMotion();
        m.Start(Idle, Wizard, S, TimeSpan.Zero);
        var now = Run(m, TimeSpan.Zero, x => x.Stage == OnboardingStage.Open, TimeSpan.FromSeconds(4));
        m.Finish(Idle, now);
        now = Run(m, now, x => x.Stage == OnboardingStage.Landed, TimeSpan.FromSeconds(2));
        Assert.Equal(OnboardingStage.Landed, m.Stage);
        Assert.False(m.TakeHandoff());   // aún sin su contenido

        now += OnboardingMotion.LandIn + OnboardingMotion.HandoffHold + TimeSpan.FromMilliseconds(100);
        m.Step(now);
        Assert.True(m.IsGone);
        Assert.True(m.TakeHandoff());
        Assert.False(m.TakeHandoff());
    }

    [Fact]
    public void Without_a_notch_it_tucks_into_the_top_edge_and_never_hands_off()
    {
        var m = new OnboardingMotion();
        m.Start(Idle, Wizard, S, TimeSpan.Zero);
        var now = Run(m, TimeSpan.Zero, x => x.Stage == OnboardingStage.Open, TimeSpan.FromSeconds(4));
        m.Finish(SizeF.Empty, now);
        bool handoff = false;
        Run(m, now, x => x.IsGone, TimeSpan.FromSeconds(3), x => handoff |= x.TakeHandoff());
        Assert.True(m.IsGone);
        Assert.False(handoff);
        Assert.Equal(0f, m.PillAlpha);
        Assert.Equal(0f, m.Height);
    }

    [Fact]
    public void Finishing_during_the_entrance_still_lands()
    {
        var m = new OnboardingMotion();
        m.Start(Idle, Wizard, S, TimeSpan.Zero);
        var now = Run(m, TimeSpan.Zero, x => x.Stage == OnboardingStage.Unfolding, TimeSpan.FromSeconds(2));
        now += TimeSpan.FromMilliseconds(50);
        m.Step(now);
        m.Finish(Idle, now);
        Run(m, now, x => x.IsGone, TimeSpan.FromSeconds(4));
        Assert.True(m.IsGone);
    }

    [Fact]
    public void With_reduce_motion_there_are_only_fades_and_no_bounce()
    {
        var m = new OnboardingMotion { ReduceMotion = true };
        m.Start(Idle, Wizard, S, TimeSpan.Zero);
        float lastAlpha = -1f;
        var now = Run(m, TimeSpan.Zero, x => x.Stage == OnboardingStage.Open, TimeSpan.FromSeconds(1), x =>
        {
            Assert.Equal(MathF.Round(Wizard.Width), x.Width);
            Assert.Equal(MathF.Round(Wizard.Height), x.Height);
            Assert.Equal(0f, x.Squash);
            Assert.True(x.PillAlpha >= lastAlpha);
            lastAlpha = x.PillAlpha;
        });
        Assert.Equal(OnboardingStage.Open, m.Stage);
        Assert.Equal(1f, m.PillAlpha);
        Assert.True(now <= OnboardingMotion.Fade + Frame + Frame);

        m.Finish(Idle, now);
        Run(m, now, x => x.IsGone, TimeSpan.FromSeconds(2), x =>
        {
            // Nunca un tamaño intermedio: el asistente o el notch de reposo.
            bool wizard = x.Width == MathF.Round(Wizard.Width) && x.Height == MathF.Round(Wizard.Height);
            bool idle = x.Width == MathF.Round(Idle.Width) && x.Height == MathF.Round(Idle.Height);
            Assert.True(wizard || idle, $"{x.Width}×{x.Height}");
            if (idle && x.Stage != OnboardingStage.Gone) Assert.Equal(0f, x.ContentAlpha);
        });
        Assert.True(m.IsGone);
    }

    // ---- vista previa

    private static Func<SetupFeature, bool> Profile(SetupProfile p) =>
        SetupFeatures.Prefill(p, new AppConfig(), fresh: true, SetupProfile.Coder) is var v ? f => v[f] : null!;

    [Fact]
    public void Each_profile_previews_what_it_adds()
    {
        Assert.Equal(new[] { PreviewFace.None }, ProfilePreview.Scenes(Profile(SetupProfile.QuotaOnly)).Select(x => x.Face));
        Assert.Equal(new[] { PreviewFace.Rings, PreviewFace.Song, PreviewFace.Working, PreviewFace.Done },
            ProfilePreview.Scenes(Profile(SetupProfile.Coder)).Select(x => x.Face));
        var full = ProfilePreview.Scenes(Profile(SetupProfile.FullNotch));
        Assert.Equal(new[] { PreviewFace.Clock, PreviewFace.Lyrics, PreviewFace.Working, PreviewFace.Done }, full.Select(x => x.Face));
        Assert.True(full[0].Mascot);
        var minimal = ProfilePreview.Scenes(Profile(SetupProfile.Minimal));
        Assert.Equal(new[] { PreviewFace.Line, PreviewFace.Working, PreviewFace.Done }, minimal.Select(x => x.Face));
        Assert.Equal(NotchMode.Minimal, minimal[0].Mode);
    }

    [Fact]
    public void The_preview_loops_and_says_when_the_next_scene_comes()
    {
        var scenes = ProfilePreview.Scenes(Profile(SetupProfile.Coder));
        var d = ProfilePreview.SceneFor;
        Assert.Equal(0, ProfilePreview.SceneAt(scenes, TimeSpan.Zero));
        Assert.Equal(1, ProfilePreview.SceneAt(scenes, d + TimeSpan.FromMilliseconds(1)));
        Assert.Equal(0, ProfilePreview.SceneAt(scenes, d * 4));
        Assert.Equal(3, ProfilePreview.SceneAt(scenes, d * 8 - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(TimeSpan.FromMilliseconds(400), ProfilePreview.NextChangeIn(scenes, d * 2 - TimeSpan.FromMilliseconds(400)));
        Assert.Null(ProfilePreview.NextChangeIn(ProfilePreview.Scenes(Profile(SetupProfile.QuotaOnly)), TimeSpan.Zero));
    }

    [Fact]
    public void The_preview_of_the_filled_profile_follows_the_switches()
    {
        var w = new OnboardingWizard(new OnboardingContext(SetupProfilesTests.Machine(true,
            SetupProfilesTests.Mon(@"\\.\DISPLAY1", 0, 0, 1920, 1080, primary: true)), new AppConfig(), false, false, true));
        w.Next();
        w.Next();   // Qué se activa, con Programo
        w.ToggleFeature(SetupFeature.Clock);
        Assert.Equal(PreviewFace.Clock, ProfilePreview.Scenes(ProfilePreview.FeaturesFor(w, SetupProfile.Coder))[0].Face);
        Assert.Equal(PreviewFace.Line, ProfilePreview.Scenes(ProfilePreview.FeaturesFor(w, SetupProfile.Minimal))[0].Face);
    }
}
