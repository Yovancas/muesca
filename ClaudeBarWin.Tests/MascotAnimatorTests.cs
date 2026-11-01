using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="MascotAnimator"/> es PURO y elapsed-driven (regla de oro F3): dado
/// (fase, <c>elapsedMsEnFase</c>, semilla) devuelve <c>{ frameIndex, blinking, spinnerGlyph,
/// verbIndex }</c>. Sin <c>Random</c> ni reloj por dentro — el jitter del parpadeo es un hash de
/// un contador, así el resultado es determinista para la misma semilla. Tono juguetón: blink
/// esporádico (no en cada tick), spinner que cicla en Processing/Compacting, verbo dentro del
/// pool de la fase.
/// </summary>
public class MascotAnimatorTests
{
    private static MascotState Sample(SessionPhase phase, double elapsedMs, int seed = 0)
        => MascotAnimator.Sample(phase, elapsedMs, seed);

    // --- frameIndex válido -------------------------------------------------

    [Fact]
    public void FrameIndex_is_always_within_the_sprite_count()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            int count = MascotSprite.Frames(p).Count;
            for (double t = 0; t <= 6000; t += 37)
            {
                var st = Sample(p, t, seed: 3);
                Assert.InRange(st.FrameIndex, 0, count - 1);
            }
        }
    }

    [Fact]
    public void Static_phases_never_pick_a_second_frame()
    {
        // Idle y Ended tienen un único frame (estáticos en el bestiario): frameIndex siempre 0.
        foreach (var p in new[] { SessionPhase.Idle, SessionPhase.Ended })
            for (double t = 0; t <= 5000; t += 50)
                Assert.Equal(0, Sample(p, t).FrameIndex);
    }

    // --- blink esporádico + determinista -----------------------------------

    [Fact]
    public void Idle_blink_is_sporadic_not_every_tick()
    {
        // En Idle el parpadeo es ocasional: a lo largo de una ventana larga la MAYORÍA de los
        // instantes NO están parpadeando (si parpadeara siempre no sería un parpadeo).
        int blinks = 0, total = 0;
        for (double t = 0; t <= 20000; t += 33)
        {
            total++;
            if (Sample(SessionPhase.Idle, t, seed: 7).Blinking) blinks++;
        }
        Assert.True(blinks > 0, "debería parpadear alguna vez en 20 s");
        Assert.True(blinks < total / 2, $"el parpadeo no debe ser constante: {blinks}/{total}");
    }

    [Fact]
    public void Blink_is_deterministic_for_the_same_seed()
    {
        for (double t = 0; t <= 8000; t += 41)
        {
            var a = Sample(SessionPhase.Idle, t, seed: 12);
            var b = Sample(SessionPhase.Idle, t, seed: 12);
            Assert.Equal(a.Blinking, b.Blinking);
            Assert.Equal(a.FrameIndex, b.FrameIndex);
            Assert.Equal(a.VerbIndex, b.VerbIndex);
            Assert.Equal(a.SpinnerGlyph, b.SpinnerGlyph);
        }
    }

    [Fact]
    public void Different_seeds_can_blink_at_different_times()
    {
        // El jitter depende de la semilla: dos semillas distintas no parpadean idénticamente
        // (al menos en un instante difieren) → confirma que la semilla influye en el jitter.
        bool anyDiff = false;
        for (double t = 0; t <= 20000 && !anyDiff; t += 33)
        {
            if (Sample(SessionPhase.Idle, t, seed: 1).Blinking != Sample(SessionPhase.Idle, t, seed: 99).Blinking)
                anyDiff = true;
        }
        Assert.True(anyDiff, "el jitter debería variar con la semilla");
    }

    // --- spinner de glifos -------------------------------------------------

    [Fact]
    public void Processing_spinner_cycles_through_a_glyph_sequence()
    {
        // En Processing el spinner avanza por su secuencia: a lo largo del tiempo aparece más de
        // un glifo distinto, y todos pertenecen a la secuencia canónica.
        var seen = new HashSet<char>();
        for (double t = 0; t <= 4000; t += 80)
        {
            char g = Sample(SessionPhase.Processing, t).SpinnerGlyph;
            Assert.NotEqual('\0', g);
            Assert.Contains(g, MascotAnimator.SpinnerSequence);
            seen.Add(g);
        }
        Assert.True(seen.Count > 1, "el spinner debe ciclar por varios glifos");
    }

    [Fact]
    public void Compacting_also_has_a_spinner()
    {
        Assert.NotEqual('\0', Sample(SessionPhase.Compacting, 100).SpinnerGlyph);
    }

    [Fact]
    public void Non_working_phases_have_no_spinner()
    {
        // Idle / WaitingFor* / Ended no llevan spinner (es señal de "trabajo vivo").
        foreach (var p in new[] { SessionPhase.Idle, SessionPhase.WaitingForApproval,
                                  SessionPhase.WaitingForInput, SessionPhase.Ended })
            Assert.Equal('\0', Sample(p, 200).SpinnerGlyph);
    }

    [Fact]
    public void Spinner_advances_monotonically_in_phase_order()
    {
        // El índice del spinner crece con el tiempo (módulo la longitud de la secuencia): a t
        // mayor el glifo cambia respecto a t=0 en algún punto del primer ciclo.
        char g0 = Sample(SessionPhase.Processing, 0).SpinnerGlyph;
        char gLater = Sample(SessionPhase.Processing, MascotAnimator.SpinnerStepMs * 1.5).SpinnerGlyph;
        Assert.NotEqual(g0, gLater);
    }

    // --- F2: ángulo del arco del spinner (sustituye al glifo braille) ----------------------

    [Fact]
    public void Spinner_angle_is_within_a_full_turn()
    {
        // El ángulo de arranque del arco está en [0,360) para todo instante (lo consume g.DrawArc).
        for (double t = 0; t <= 5000; t += 13)
        {
            float a = MascotAnimator.SpinnerAngleAt(t);
            Assert.InRange(a, 0f, 360f);
            Assert.True(a < 360f, $"el ángulo debe quedar en [0,360), got {a} en t={t}");
        }
    }

    [Fact]
    public void Spinner_angle_advances_over_time_and_wraps()
    {
        // Gira: a t mayor (dentro de la primera vuelta) el ángulo crece; al completar la vuelta envuelve.
        Assert.Equal(0f, MascotAnimator.SpinnerAngleAt(0), 3);
        Assert.True(MascotAnimator.SpinnerAngleAt(MascotAnimator.SpinnerSpinMs * 0.25) >
                    MascotAnimator.SpinnerAngleAt(0));
        // Una vuelta completa vuelve cerca de 0 (módulo el periodo).
        Assert.Equal(MascotAnimator.SpinnerAngleAt(10), MascotAnimator.SpinnerAngleAt(10 + MascotAnimator.SpinnerSpinMs), 3);
    }

    [Fact]
    public void Spinner_angle_is_deterministic_and_clock_free()
    {
        for (double t = 0; t <= 4000; t += 29)
            Assert.Equal(MascotAnimator.SpinnerAngleAt(t), MascotAnimator.SpinnerAngleAt(t));
    }

    [Fact]
    public void Spinner_angle_is_carried_only_for_spinner_phases()
    {
        // La fase de trabajo lleva el ángulo del arco en el estado; las demás lo dejan a 0 (sin spinner).
        var working = Sample(SessionPhase.Processing, MascotAnimator.SpinnerSpinMs * 0.5);
        Assert.NotEqual('\0', working.SpinnerGlyph);
        Assert.Equal(MascotAnimator.SpinnerAngleAt(MascotAnimator.SpinnerSpinMs * 0.5), working.SpinnerAngleDeg, 3);

        foreach (var p in new[] { SessionPhase.Idle, SessionPhase.WaitingForApproval,
                                  SessionPhase.WaitingForInput, SessionPhase.Ended })
            Assert.Equal(0f, Sample(p, 1234).SpinnerAngleDeg);
    }

    [Fact]
    public void ReduceMotion_zeroes_the_spinner_angle()
    {
        // Con reduce-motion el spinner desaparece (señal y ángulo): nada de arco congelado.
        foreach (var p in new[] { SessionPhase.Processing, SessionPhase.Compacting })
        {
            var st = MascotAnimator.Sample(p, 555, seed: 2, reduceMotion: true);
            Assert.Equal('\0', st.SpinnerGlyph);
            Assert.Equal(0f, st.SpinnerAngleDeg);
        }
    }

    // --- verbo dentro del pool ---------------------------------------------

    [Fact]
    public void VerbIndex_is_within_the_phase_pool()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            int poolSize = MascotAnimator.VerbPoolSize(p);
            for (double t = 0; t <= 15000; t += 53)
            {
                int vi = Sample(p, t, seed: 4).VerbIndex;
                Assert.InRange(vi, 0, poolSize - 1);
            }
        }
    }

    [Fact]
    public void Every_phase_pool_has_three_to_five_verbs()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
            Assert.InRange(MascotAnimator.VerbPoolSize(p), 3, 5);
    }

    [Fact]
    public void Verb_rotates_over_time_within_a_phase()
    {
        // El verbo no es fijo: a lo largo del tiempo se ve más de uno del pool (rotación juguetona).
        var seen = new HashSet<int>();
        for (double t = 0; t <= 60000; t += 250)
            seen.Add(Sample(SessionPhase.Processing, t, seed: 2).VerbIndex);
        Assert.True(seen.Count > 1, "el verbo debe rotar dentro del pool");
    }

    // --- reduce-motion: puerta única → frame base estático (Tarea 7) -------

    [Fact]
    public void ReduceMotion_collapses_to_the_static_base_state_for_every_phase_and_time()
    {
        // Con reduceMotion=true el animador devuelve SIEMPRE el frame base: sin spinner (incluso en
        // Processing/Compacting, que normalmente sí lo llevan), sin parpadeo, frame 0, verbo 0. Así la
        // puerta única no deja ningún resto de animación (ni un spinner congelado).
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            for (double t = 0; t <= 6000; t += 37)
            {
                var st = MascotAnimator.Sample(p, t, seed: 9, reduceMotion: true);
                Assert.Equal('\0', st.SpinnerGlyph);
                Assert.False(st.Blinking);
                Assert.Equal(0, st.FrameIndex);
                Assert.Equal(0, st.VerbIndex);
            }
        }
    }

    [Fact]
    public void ReduceMotion_suppresses_the_spinner_that_is_otherwise_present()
    {
        // Regresión directa del bloqueante: en Processing/Compacting el spinner está vivo por defecto,
        // pero con reduce-motion debe desaparecer (no quedar congelado en SpinnerSequence[0] = '⠋').
        foreach (var p in new[] { SessionPhase.Processing, SessionPhase.Compacting })
        {
            Assert.NotEqual('\0', MascotAnimator.Sample(p, 0, seed: 1).SpinnerGlyph);
            Assert.Equal('\0', MascotAnimator.Sample(p, 0, seed: 1, reduceMotion: true).SpinnerGlyph);
        }
    }

    [Fact]
    public void ReduceMotion_matches_the_published_static_state()
    {
        Assert.Equal(MascotAnimator.StaticState, MascotAnimator.Sample(SessionPhase.Processing, 1234, reduceMotion: true));
        // El StaticState es el frame base esperado: sin movimiento de ningún tipo.
        Assert.Equal('\0', MascotAnimator.StaticState.SpinnerGlyph);
        Assert.False(MascotAnimator.StaticState.Blinking);
        Assert.Equal(0, MascotAnimator.StaticState.FrameIndex);
        Assert.Equal(0, MascotAnimator.StaticState.VerbIndex);
    }

    [Fact]
    public void Localized_verb_is_resolved_for_every_phase_and_language()
    {
        // Puente con i18n: el animador expone un verbo localizado (string) para cada (fase, idioma),
        // no vacío, índice dentro del pool. Comprueba EN y ES como muestra.
        foreach (var code in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(code);
            foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
            {
                var st = Sample(p, 1234, seed: 5);
                string verb = MascotAnimator.Verb(s, p, st.VerbIndex);
                Assert.False(string.IsNullOrWhiteSpace(verb), $"{code}/{p} verbo vacío");
            }
        }
    }
}

/// <summary>
/// Lenguaje de movimiento §4 #8: el verbo cambia cada 1700 ms con un cruce simultáneo de 120 ms
/// (lineal en alfa) en lugar de un cambio seco.
/// </summary>
public class MascotVerbCrossfadeTests
{
    private static (int Prev, int Cur, double Blend) At(double t, int seed = 11)
    {
        var st = MascotAnimator.Sample(SessionPhase.Processing, t, seed);
        return (st.PrevVerbIndex, st.VerbIndex, st.VerbBlend);
    }

    // Busca un cambio real de verbo (el hash puede repetir índice entre pasos).
    private static double FirstRealChange(int seed = 11)
    {
        for (int step = 1; step < 50; step++)
        {
            double t = step * MascotAnimator.VerbStepMs;
            if (MascotAnimator.Sample(SessionPhase.Processing, t - 1, seed).VerbIndex
                != MascotAnimator.Sample(SessionPhase.Processing, t + 1, seed).VerbIndex) return t;
        }
        throw new Xunit.Sdk.XunitException("sin cambio de verbo en 50 pasos");
    }

    [Fact]
    public void Verb_crossfades_linearly_for_120ms_after_a_change()
    {
        double t0 = FirstRealChange();
        var mid = At(t0 + 60);
        Assert.NotEqual(mid.Prev, mid.Cur);
        Assert.Equal(0.5, mid.Blend, 6);
        Assert.Equal(At(t0 - 1).Cur, mid.Prev);
        Assert.Equal(1.0, At(t0 + MascotAnimator.VerbCrossfadeMs).Blend, 9);
        Assert.Equal(1.0, At(t0 + 500).Blend, 9);
    }

    [Fact]
    public void No_crossfade_when_the_verb_repeats_or_with_reduce_motion()
    {
        var st = MascotAnimator.Sample(SessionPhase.Processing, 30, seed: 11);
        Assert.Equal(1.0, st.VerbBlend);                     // primer paso: no hay verbo previo
        var rm = MascotAnimator.Sample(SessionPhase.Processing, FirstRealChange() + 60, 11, reduceMotion: true);
        Assert.Equal(1.0, rm.VerbBlend);
        Assert.Equal(120, MascotAnimator.VerbCrossfadeMs);
        Assert.Equal(1700, MascotAnimator.VerbStepMs);
    }
}
