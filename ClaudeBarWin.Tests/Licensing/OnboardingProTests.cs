using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.UI.Onboarding;
using static ClaudeBarWin.Tests.SetupProfilesTests;

namespace ClaudeBarWin.Tests.Licensing;

/// <summary>El asistente de bienvenida marca lo Pro en «Qué se activa» y solo en la Store avisa de la prueba de 7 días.</summary>
public class OnboardingProTests
{
    private static readonly Strings Es = Localization.Get("es");
    private static readonly SetupMachine OneMonitor = Machine(true, Mon(@"\\.\DISPLAY1", 0, 0, 1920, 1080, primary: true));

    private static OnboardingView View(ProSummary? pro)
    {
        var ctx = new OnboardingContext(OneMonitor, new AppConfig().Normalize(), false, false, Fresh: true, Pro: pro);
        return OnboardingText.Build(new OnboardingWizard(ctx), Es);
    }

    [Fact]
    public void Music_lyrics_small_lyrics_and_tasks_carry_the_pro_tag()
    {
        var v = View(new ProSummary(ProState.Free, null, false));
        var pro = SetupFeatures.All.Where(f => v.Toggles[(int)f].Pro).ToArray();
        Assert.Equal(new[] { SetupFeature.Music, SetupFeature.Lyrics, SetupFeature.CompactLyrics, SetupFeature.Tasks }, pro);
        Assert.True(v.Permissions.Pro);   // Permitir / Denegar desde el notch
        Assert.False(v.Live.Pro);         // las sesiones en vivo son gratis
        foreach (var f in SetupFeatures.All)
            Assert.Equal(OnboardingText.ProFeatureOf(f) is not null, OnboardingText.IsPro(f));
    }

    [Fact]
    public void The_trial_is_only_announced_in_the_store()
    {
        Assert.Equal(Es.OnbProTrialNote, View(new ProSummary(ProState.Trial, 7, Store: true)).ProNote);
        Assert.Equal(Es.OnbProBuyNote, View(new ProSummary(ProState.Free, null, Store: true)).ProNote);
        Assert.Equal(Es.OnbProStoreNote, View(new ProSummary(ProState.Free, null, Store: false)).ProNote);
        Assert.Equal(Es.OnbProActiveNote, View(new ProSummary(ProState.Pro, null, Store: true)).ProNote);
        Assert.Null(View(null).ProNote);
        Assert.DoesNotContain("7", View(new ProSummary(ProState.Free, null, Store: false)).ProNote);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void The_pro_note_sits_in_the_free_slot_under_the_right_column(float s)
    {
        var pill = new RectangleF(0, 0, OnboardingGeometry.Width * s, OnboardingGeometry.Height * s);
        var note = OnboardingGeometry.ProNote(pill, s);
        var last = OnboardingGeometry.ToggleRow(pill, s, SetupFeatures.All[^1]);
        Assert.True(note.Top >= last.Bottom - 0.01f, $"{note} pisa {last}");
        Assert.True(note.Bottom <= OnboardingGeometry.FeaturesHint(pill, s).Top + 0.01f);
        Assert.True(pill.Contains(note));
        foreach (var f in SetupFeatures.All) Assert.False(note.IntersectsWith(OnboardingGeometry.ToggleRow(pill, s, f)));
    }
}
