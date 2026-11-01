using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="MusicIconMotion"/> es PURO: morph pausa ↔ reproducir con el muelle Ajuste (invertible a media transición) y
/// deslizamiento de siguiente/anterior (salida hasta la barra, hueco y entrada por el lado contrario). Quieto ⇒
/// <see cref="MusicIconMotion.IsAnimating"/> = false (0 % CPU en reposo); reduce-motion = cambio inmediato.
/// </summary>
public class MusicIconMotionTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(10);
    private static TimeSpan Ms(double ms) => T0 + TimeSpan.FromMilliseconds(ms);

    /// <summary>Avanza a 60 Hz de <paramref name="from"/> a <paramref name="until"/> (como el bucle de la ventana).</summary>
    private static void Run(MusicIconMotion m, TimeSpan from, TimeSpan until)
    {
        for (var now = from; now <= until; now += TimeSpan.FromMilliseconds(16)) m.Step(now);
        m.Step(until);
    }

    [Fact]
    public void Starts_at_rest_with_the_shape_of_the_initial_state()
    {
        var playing = new MusicIconMotion(playing: true);
        var paused = new MusicIconMotion(playing: false);
        Assert.Equal(0f, playing.Morph);
        Assert.Equal(1f, paused.Morph);
        Assert.False(playing.IsAnimating);
        Assert.Equal(0f, playing.NextOffset);
        Assert.Equal(0f, playing.PreviousOffset);
    }

    [Fact]
    public void Pausing_morphs_the_bars_into_the_triangle_and_settles()
    {
        var m = new MusicIconMotion(playing: true);
        m.SetPlaying(false, T0);
        Assert.True(m.IsAnimating);
        Assert.True(m.IsMorphing);
        Assert.Equal(0f, m.Morph);                       // en 0 todavía son las barras

        Run(m, T0, Ms(130));
        Assert.InRange(m.Morph, 0.3f, 0.9f);             // a mitad, a medio camino

        Run(m, Ms(130), Ms(280));
        Assert.True(m.Morph > 0.9f);                     // ~95 % a los ~280 ms (Ajuste)

        Run(m, Ms(280), Ms(900));
        Assert.Equal(1f, m.Morph);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Reversing_mid_morph_continues_from_where_it_is_without_a_jump()
    {
        var m = new MusicIconMotion(playing: true);
        m.SetPlaying(false, T0);
        Run(m, T0, Ms(120));
        float before = m.Morph;
        m.SetPlaying(true, Ms(120));
        m.Step(Ms(136));
        Assert.True(Math.Abs(m.Morph - before) < 0.15f, $"salto de {before} a {m.Morph}");
        Assert.True(m.Morph > 0f);

        Run(m, Ms(136), Ms(1000));
        Assert.Equal(0f, m.Morph);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Same_state_does_not_start_an_animation()
    {
        var m = new MusicIconMotion(playing: true);
        m.SetPlaying(true, T0);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Next_slides_into_the_bar_hides_and_comes_back_in_from_the_other_side()
    {
        var m = new MusicIconMotion(playing: true);
        m.Skip(next: true, T0);
        Assert.True(m.IsAnimating);
        m.Step(T0);
        Assert.Equal(0f, m.NextOffset);

        m.Step(Ms(MusicIconMotion.SkipOut.TotalMilliseconds / 2));
        Assert.InRange(m.NextOffset, 0.1f, 0.5f);        // ease-in: a mitad de la salida aún va despacio

        m.Step(Ms(MusicIconMotion.SkipOut.TotalMilliseconds + 10));
        Assert.Equal(1f, m.NextOffset);                  // hueco: solo se ve la barra

        double inMid = (MusicIconMotion.SkipOut + MusicIconMotion.SkipGap).TotalMilliseconds
            + MusicIconMotion.SkipIn.TotalMilliseconds / 2;
        m.Step(Ms(inMid));
        Assert.InRange(m.NextOffset, -0.4f, -0.01f);     // ease-out: entra deprisa y frena

        m.Step(Ms(MusicIconMotion.SkipDuration.TotalMilliseconds + 1));
        Assert.Equal(0f, m.NextOffset);
        Assert.False(m.IsAnimating);
        Assert.Equal(0f, m.PreviousOffset);              // el otro botón no se ha movido
    }

    [Fact]
    public void Skip_lasts_between_420_and_480_ms()
    {
        Assert.InRange(MusicIconMotion.SkipDuration.TotalMilliseconds, 420, 480);
    }

    [Fact]
    public void Clicking_again_while_entering_restarts_from_the_current_position()
    {
        var m = new MusicIconMotion(playing: true);
        m.Skip(next: false, T0);
        double entering = (MusicIconMotion.SkipOut + MusicIconMotion.SkipGap).TotalMilliseconds + 60;
        m.Step(Ms(entering));
        float before = m.PreviousOffset;
        Assert.True(before < 0f);

        m.Skip(next: false, Ms(entering));
        m.Step(Ms(entering));
        Assert.Equal(before, m.PreviousOffset);          // sin salto en el instante del reinicio
        m.Step(Ms(entering + 16));
        Assert.True(m.PreviousOffset - before is > 0f and < 0.2f);   // vuelve a ir hacia la barra, suave

        m.Step(Ms(entering + MusicIconMotion.SkipOut.TotalMilliseconds + 5));
        Assert.Equal(1f, m.PreviousOffset);
        m.Step(Ms(entering + MusicIconMotion.SkipDuration.TotalMilliseconds + 1));
        Assert.Equal(0f, m.PreviousOffset);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Clicking_again_while_only_the_bar_shows_keeps_the_course()
    {
        var m = new MusicIconMotion(playing: true);
        m.Skip(next: true, T0);
        double gap = MusicIconMotion.SkipOut.TotalMilliseconds + 20;
        m.Step(Ms(gap));
        m.Skip(next: true, Ms(gap));
        m.Step(Ms(MusicIconMotion.SkipDuration.TotalMilliseconds + 1));
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Reduce_motion_changes_at_once()
    {
        var m = new MusicIconMotion(playing: true) { ReduceMotion = true };
        m.SetPlaying(false, T0);
        Assert.Equal(1f, m.Morph);
        Assert.False(m.IsAnimating);

        m.Skip(next: true, T0);
        Assert.False(m.IsAnimating);
        Assert.Equal(0f, m.NextOffset);
    }

    [Fact]
    public void Turning_reduce_motion_on_mid_animation_settles_on_the_next_step()
    {
        var m = new MusicIconMotion(playing: true);
        m.SetPlaying(false, T0);
        m.Skip(next: true, T0);
        Run(m, T0, Ms(100));
        m.ReduceMotion = true;
        m.Step(Ms(116));
        Assert.Equal(1f, m.Morph);
        Assert.Equal(0f, m.NextOffset);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void After_the_gap_the_triangle_reappears_almost_whole()
    {
        var m = new MusicIconMotion(playing: true);
        m.Skip(next: true, T0);
        m.Step(Ms((MusicIconMotion.SkipOut + MusicIconMotion.SkipGap).TotalMilliseconds + 1));
        Assert.InRange(m.NextOffset, -MusicIconMotion.SkipEntry, -0.2f);   // ya se ve casi entero, no entra deslizando
    }

    [Fact]
    public void A_brief_paused_blip_right_after_a_skip_does_not_move_the_center_button()
    {
        var m = new MusicIconMotion(playing: true);
        m.Skip(next: true, T0);
        m.SetPlaying(false, Ms(40));                     // el reproductor cambia de pista y avisa «en pausa» un momento
        Run(m, T0, Ms(200));
        Assert.Equal(0f, m.Morph);
        Assert.False(m.IsMorphing);
        m.SetPlaying(true, Ms(200));                     // vuelve a sonar antes de acabar la espera

        Run(m, Ms(200), Ms(MusicIconMotion.SkipHold.TotalMilliseconds + 400));
        Assert.Equal(0f, m.Morph);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void A_real_pause_right_after_a_skip_morphs_once_the_hold_ends()
    {
        var m = new MusicIconMotion(playing: true);
        m.Skip(next: false, T0);
        m.SetPlaying(false, Ms(40));
        Assert.True(m.IsAnimating);                      // el bucle sigue vivo para aplicar el cambio al acabar la espera
        Run(m, T0, Ms(MusicIconMotion.SkipHold.TotalMilliseconds - 20));
        Assert.Equal(0f, m.Morph);

        Run(m, Ms(MusicIconMotion.SkipHold.TotalMilliseconds - 20), Ms(MusicIconMotion.SkipHold.TotalMilliseconds + 1200));
        Assert.Equal(1f, m.Morph);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Jump_places_the_shape_without_animating()
    {
        var m = new MusicIconMotion(playing: true);
        m.JumpPlaying(false);
        Assert.True(!m.Playing && m.Morph == 1f && !m.IsAnimating);
    }
}
