using ClaudeBarWin.Models;
using ClaudeBarWin.Services.Mascot;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="MascotMood"/> es una máquina de humor PURA con histéresis (dwell mínimo antes de
/// cambiar) y decay (vuelve a <see cref="Mood.Neutral"/> tras N ms sin eventos). Elapsed-driven:
/// <c>Update</c> recibe el delta de tiempo por parámetro; nunca lee el reloj. Tono juguetón =
/// rango emocional más expresivo, pero la histéresis evita el parpadeo de humor ante cambios
/// rápidos de fase.
/// </summary>
public class MascotMoodTests
{
    private static MascotMood Fresh() => new();

    [Fact]
    public void Starts_neutral()
        => Assert.Equal(Mood.Neutral, Fresh().Current);

    [Fact]
    public void Attention_event_enters_alert()
    {
        var m = Fresh();
        m.Update(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 16);
        Assert.Equal(Mood.Alert, m.Current);
    }

    [Fact]
    public void Reset_event_enters_happy()
    {
        var m = Fresh();
        m.Update(SessionPhase.Idle, MoodEvent.ResetCelebrated, 16);
        Assert.Equal(Mood.Happy, m.Current);
    }

    [Fact]
    public void Long_processing_enters_focused()
    {
        var m = Fresh();
        m.Update(SessionPhase.Processing, MoodEvent.LongProcessing, 16);
        Assert.Equal(Mood.Focused, m.Current);
    }

    // --- Histéresis (dwell) ------------------------------------------------

    [Fact]
    public void Does_not_leave_alert_before_the_dwell_elapses()
    {
        var m = Fresh();
        m.Update(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 16);
        Assert.Equal(Mood.Alert, m.Current);

        // Aunque la fase deje de pedir atención inmediatamente, el humor NO cambia antes del dwell.
        m.Update(SessionPhase.Processing, MoodEvent.None, MascotMood.DwellMs / 2);
        Assert.Equal(Mood.Alert, m.Current);
    }

    [Fact]
    public void Rapid_phase_flips_do_not_flicker_the_mood()
    {
        var m = Fresh();
        m.Update(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 16);
        // Varios cambios de fase rápidos por debajo del dwell: el humor se mantiene estable.
        for (int i = 0; i < 5; i++)
            m.Update(i % 2 == 0 ? SessionPhase.Processing : SessionPhase.Idle, MoodEvent.None, 10);
        Assert.Equal(Mood.Alert, m.Current);
    }

    // --- Decay -------------------------------------------------------------

    [Fact]
    public void Decays_to_neutral_after_decay_ms_without_events()
    {
        var m = Fresh();
        m.Update(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 16);
        // Pasado el decay sin nuevos eventos, vuelve a neutro.
        m.Update(SessionPhase.Idle, MoodEvent.None, MascotMood.DecayMs + 1);
        Assert.Equal(Mood.Neutral, m.Current);
    }

    [Fact]
    public void Happy_decays_to_neutral()
    {
        var m = Fresh();
        m.Update(SessionPhase.Idle, MoodEvent.ResetCelebrated, 16);
        Assert.Equal(Mood.Happy, m.Current);
        m.Update(SessionPhase.Idle, MoodEvent.None, MascotMood.DecayMs + 1);
        Assert.Equal(Mood.Neutral, m.Current);
    }

    [Fact]
    public void A_new_event_refreshes_the_decay_window()
    {
        var m = Fresh();
        m.Update(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 16);
        // Casi expira…
        m.Update(SessionPhase.WaitingForApproval, MoodEvent.None, MascotMood.DecayMs - 10);
        Assert.Equal(Mood.Alert, m.Current);
        // …pero un evento nuevo reinicia la ventana de decay.
        m.Update(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 20);
        m.Update(SessionPhase.WaitingForApproval, MoodEvent.None, MascotMood.DecayMs - 10);
        Assert.Equal(Mood.Alert, m.Current);
    }

    [Fact]
    public void Higher_priority_event_overrides_after_dwell()
    {
        var m = Fresh();
        m.Update(SessionPhase.Processing, MoodEvent.LongProcessing, 16);
        Assert.Equal(Mood.Focused, m.Current);
        // Pasado el dwell, una petición de atención (más prioritaria) sí cambia el humor.
        m.Update(SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, MascotMood.DwellMs + 1);
        Assert.Equal(Mood.Alert, m.Current);
    }

    [Fact]
    public void Update_is_deterministic_and_clock_free()
    {
        // Misma secuencia de (fase, evento, delta) ⇒ mismo humor, sin depender del reloj.
        var a = Fresh();
        var b = Fresh();
        var steps = new (SessionPhase, MoodEvent, double)[]
        {
            (SessionPhase.Processing, MoodEvent.LongProcessing, 16),
            (SessionPhase.Processing, MoodEvent.None, 500),
            (SessionPhase.WaitingForApproval, MoodEvent.AttentionRequired, 2000),
            (SessionPhase.Idle, MoodEvent.None, 100),
        };
        foreach (var (p, e, d) in steps) { a.Update(p, e, d); b.Update(p, e, d); }
        Assert.Equal(a.Current, b.Current);
    }
}
