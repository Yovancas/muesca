using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Lógica de color del <see cref="MascotRenderer"/> (PURA y testeable sin GDI+): <see cref="MascotRenderer.PhaseColor"/>
/// (color por fase) y <see cref="MascotRenderer.MoodColor"/> (tinte por humor). Cubre los fixes
/// v0.3.9: F1 (Idle → MascotIdle, ≥3:1 no textual en los 3 temas) y F3a (Happy → Accent).
/// </summary>
public class MascotRendererTests
{
    public static IEnumerable<object[]> Themes =>
        new[] { new object[] { Theme.Dark }, new object[] { Theme.Light }, new object[] { Theme.Cli } };

    // --- F1: la mascota Idle/default usa MascotIdle, no Neutral ------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void Idle_phase_color_is_the_idle_token(Theme t)
    {
        // La fase Idle (y el default) sale del token MascotIdle (≥3:1 sobre el fondo), no del Neutral
        // histórico (~2.3:1 en oscuro). El token resuelve a TextMuted en los 3 temas.
        Assert.Equal(t.MascotIdle.ToArgb(), MascotRenderer.PhaseColor(t, SessionPhase.Idle).ToArgb());
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Idle_phase_color_meets_non_text_contrast(Theme t)
    {
        // El glifo de la mascota es NO textual: WCAG 1.4.11 pide ≥3:1. Idle debe cumplirlo en los 3
        // temas (idealmente ≥4.5, que TextMuted ya cumple como texto).
        double r = ColorMath.ContrastRatio(MascotRenderer.PhaseColor(t, SessionPhase.Idle), t.Background);
        Assert.True(r >= 3.0, $"[{t.Id}] Idle contrasta {r:0.00}:1 (< 3.0)");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Active_phases_keep_their_semantic_color(Theme t)
    {
        // F1 NO toca las fases activas: conservan su color de fase (Warn/Ok/Critical).
        Assert.Equal(t.Warn.ToArgb(), MascotRenderer.PhaseColor(t, SessionPhase.WaitingForApproval).ToArgb());
        Assert.Equal(t.Warn.ToArgb(), MascotRenderer.PhaseColor(t, SessionPhase.WaitingForInput).ToArgb());
        Assert.Equal(t.Ok.ToArgb(), MascotRenderer.PhaseColor(t, SessionPhase.Processing).ToArgb());
        Assert.Equal(t.Ok.ToArgb(), MascotRenderer.PhaseColor(t, SessionPhase.Compacting).ToArgb());
        Assert.Equal(t.Critical.ToArgb(), MascotRenderer.PhaseColor(t, SessionPhase.Ended).ToArgb());
    }

    [Fact]
    public void Idle_no_longer_uses_neutral_that_failed_contrast_in_dark()
    {
        // Regresión directa de F1: en el tema por defecto (oscuro) Idle ya NO es Neutral (que fallaba
        // el 3:1). El nuevo color contrasta mejor.
        Assert.NotEqual(Theme.Dark.Neutral.ToArgb(), MascotRenderer.PhaseColor(Theme.Dark, SessionPhase.Idle).ToArgb());
        double before = ColorMath.ContrastRatio(Theme.Dark.Neutral, Theme.Dark.Background);
        double after = ColorMath.ContrastRatio(MascotRenderer.PhaseColor(Theme.Dark, SessionPhase.Idle), Theme.Dark.Background);
        Assert.True(after > before, $"Idle debería contrastar más que Neutral ({after:0.00} vs {before:0.00})");
    }

    // --- F3a: el humor Happy (celebración de reset) usa el ACENTO, no el verde Ok -----------

    [Theory]
    [MemberData(nameof(Themes))]
    public void Happy_mood_is_tinted_with_the_brand_accent(Theme t)
    {
        // Celebración de reset: el gato contento se tiñe del acento de marca (el violeta #7B4DFF en dark/light),
        // no del verde Ok — así "contento" no se confunde con "trabajando".
        Assert.Equal(t.Accent.ToArgb(), MascotRenderer.MoodColor(t, SessionPhase.Idle, Mood.Happy).ToArgb());
    }

    [Fact]
    public void Happy_mood_no_longer_inherits_ok_green()
    {
        // Pin de regresión: Happy ya NO usa Ok (el problema que motiva F3a).
        Assert.NotEqual(Theme.Dark.Ok.ToArgb(), MascotRenderer.MoodColor(Theme.Dark, SessionPhase.Processing, Mood.Happy).ToArgb());
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Other_moods_are_unchanged(Theme t)
    {
        // Alert sigue en Warn; Focused/Neutral caen al color de la fase.
        Assert.Equal(t.Warn.ToArgb(), MascotRenderer.MoodColor(t, SessionPhase.WaitingForApproval, Mood.Alert).ToArgb());
        Assert.Equal(MascotRenderer.PhaseColor(t, SessionPhase.Processing).ToArgb(),
                     MascotRenderer.MoodColor(t, SessionPhase.Processing, Mood.Focused).ToArgb());
        Assert.Equal(MascotRenderer.PhaseColor(t, SessionPhase.Idle).ToArgb(),
                     MascotRenderer.MoodColor(t, SessionPhase.Idle, Mood.Neutral).ToArgb());
    }
}
