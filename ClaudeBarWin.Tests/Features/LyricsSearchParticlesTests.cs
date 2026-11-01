using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary><see cref="LyricsSearchParticles"/>: las letras que suben mientras se busca la letra (pedido del 29-oct).</summary>
public class LyricsSearchParticlesTests
{
    private const float W = 140f, H = 50f;

    [Fact]
    public void Same_instant_gives_the_same_frame()
    {
        for (double t = 0; t < 6; t += 0.37)
            Assert.Equal(LyricsSearchParticles.At(t, 12, W, H), LyricsSearchParticles.At(t, 12, W, H));
    }

    [Fact]
    public void Fill_writes_the_same_frame_as_At_into_the_callers_buffer()
    {
        var buf = new LyricsParticle[LyricsSearchParticles.Count];
        LyricsSearchParticles.Fill(buf, 2.345, W, H);
        Assert.Equal(LyricsSearchParticles.At(2.345, LyricsSearchParticles.Count, W, H), buf);
    }

    [Fact]
    public void Particles_stay_inside_the_area_with_alpha_between_0_and_1()
    {
        for (double t = 0; t < 20; t += 0.017)
            Assert.All(LyricsSearchParticles.At(t, 14, W, H), p =>
            {
                Assert.InRange(p.X, 0f, W);
                Assert.InRange(p.Y, 0f, H);
                Assert.InRange(p.Alpha, 0f, 1f);
                Assert.InRange(p.Scale, 0.5f, 1.5f);
                Assert.Contains(p.Glyph, LyricsSearchParticles.Glyphs);
            });
    }

    [Fact]
    public void Particles_are_not_all_in_phase()
    {
        foreach (var t in new[] { 0.0, 0.8, 3.3, 11.1 })
        {
            var ps = LyricsSearchParticles.At(t, 12, W, H);
            Assert.True(ps.Select(p => MathF.Round(p.Y)).Distinct().Count() >= 8, $"alturas casi iguales en t={t}");
            Assert.Contains(ps, p => p.Alpha > 0.3f);   // siempre hay letras a la vista
        }
    }

    [Fact]
    public void Each_particle_rises_during_its_cycle_and_fades_out_at_the_top()
    {
        // Partícula a partícula, en pasos cortos: la Y baja (sube en pantalla) hasta que la vuelta reinicia abajo.
        int restarts = 0;
        var prev = LyricsSearchParticles.At(0, 12, W, H);
        for (double t = 0.01; t < 5; t += 0.01)
        {
            var cur = LyricsSearchParticles.At(t, 12, W, H);
            for (int i = 0; i < cur.Length; i++)
            {
                if (cur[i].Y > prev[i].Y + 0.001f)
                {
                    restarts++;
                    Assert.True(prev[i].Alpha < 0.05f, $"la partícula {i} reinicia visible (alfa {prev[i].Alpha})");
                    Assert.True(prev[i].Y < H * 0.05f, $"la partícula {i} reinicia sin llegar arriba (y {prev[i].Y})");
                }
            }
            prev = cur;
        }
        Assert.InRange(restarts, 12, 12 * 4);   // cada una da 2–3 vueltas en 5 s
    }

    [Fact]
    public void Letters_fade_while_rising_instead_of_piling_up_at_the_top()
    {
        // Revisión del 29-oct: con el desvanecido por tiempo, unas 5 de 12 se quedaban casi quietas y visibles en una raya
        // pegada al borde de arriba. Ahora el alfa depende de la altura: en el 10 % superior apenas quedan letras visibles.
        int worst = 0;
        for (double t = 0; t < 30; t += 0.013)
        {
            int piled = LyricsSearchParticles.At(t, LyricsSearchParticles.Count, W, H)
                .Count(p => p.Alpha > 0.1f && p.Y < H * 0.1f);
            worst = Math.Max(worst, piled);
        }
        Assert.True(worst <= 2, $"hasta {worst} letras visibles amontonadas arriba");
    }

    [Fact]
    public void Letters_are_brightest_low_and_dim_near_the_top()
    {
        // A media altura siguen bien visibles; cerca del borde de arriba ya casi apagadas.
        for (double t = 0; t < 30; t += 0.013)
            Assert.All(LyricsSearchParticles.At(t, LyricsSearchParticles.Count, W, H), p =>
            {
                if (p.Y < H * 0.05f) Assert.True(p.Alpha < 0.15f, $"alfa {p.Alpha} pegada arriba (y {p.Y})");
            });
    }

    [Fact]
    public void Reduce_motion_shows_one_fixed_frame()
    {
        var a = LyricsSearchParticles.At(0.2, 12, W, H, still: true);
        Assert.Equal(a, LyricsSearchParticles.At(7.9, 12, W, H, still: true));
        Assert.Contains(a, p => p.Alpha > 0.2f);   // el fotograma fijo enseña letras
    }

    [Fact]
    public void Zero_area_keeps_every_particle_at_the_origin()
    {
        Assert.All(LyricsSearchParticles.At(1, 12, 0, 0), p => { Assert.Equal(0f, p.X); Assert.Equal(0f, p.Y); });
        Assert.Empty(LyricsSearchParticles.At(1, 0, W, H));
    }

    [Theory]
    [InlineData(LyricsState.Loading, true, true, false, false, true)]
    [InlineData(LyricsState.Idle, true, true, false, false, false)]
    [InlineData(LyricsState.Found, true, true, false, false, false)]
    [InlineData(LyricsState.Plain, true, true, false, false, false)]
    [InlineData(LyricsState.NotFound, true, true, false, false, false)]
    [InlineData(LyricsState.Error, true, true, false, false, false)]
    [InlineData(LyricsState.Loading, false, true, false, false, false)]   // vista de letra cerrada o página no visible
    [InlineData(LyricsState.Loading, true, false, false, false, false)]   // ventana oculta
    [InlineData(LyricsState.Loading, true, true, true, false, false)]     // oculta por pantalla completa
    [InlineData(LyricsState.Loading, true, true, false, true, false)]     // reduce-motion: fotograma fijo
    public void Keeps_repainting_only_while_loading_and_shown(LyricsState state, bool shown, bool visible, bool fullscreen,
        bool reduce, bool expected) =>
        Assert.Equal(expected, LyricsSearchParticles.Animates(state, shown, visible, fullscreen, reduce));
}
