using System.Drawing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="IdleScene"/> (F5 · B7): en reposo los anillos (7 s) se intercalan con el reloj (5 s) y el gato aparece tras
/// 15 s de calma, mira al cursor solo con el ratón encima y se duerme. PURO: el instante llega por parámetro; los cambios
/// salen por <see cref="IdleScene.NextWakeIn"/> (nada de timers periódicos) y, dormido en la raya, no queda ninguno.
/// </summary>
public class IdleSceneTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 18, 55, 10, DateTimeKind.Utc);
    private static readonly DateTime L0 = new(2026, 10, 29, 19, 55, 10, DateTimeKind.Local);

    private static IdleScene Calm(NotchMode mode = NotchMode.Idle)
    {
        var s = new IdleScene();
        s.Update(mode, calm: true, T0);
        return s;
    }

    [Fact]
    public void Idle_alternates_rings_7s_and_clock_5s()
    {
        var s = Calm();
        Assert.Equal(IdleFace.Quota, s.Face);
        Assert.Equal(0, s.Layout & IdleScene.LayoutClock);

        s.Update(NotchMode.Idle, true, T0.AddSeconds(7));
        Assert.Equal(IdleFace.Clock, s.Face);
        Assert.NotEqual(0, s.Layout & IdleScene.LayoutClock);

        s.Update(NotchMode.Idle, true, T0.AddSeconds(12));
        Assert.Equal(IdleFace.Quota, s.Face);
    }

    [Fact]
    public void Returning_to_idle_starts_again_on_the_rings()
    {
        var s = Calm();
        s.Update(NotchMode.Idle, true, T0.AddSeconds(8));
        Assert.Equal(IdleFace.Clock, s.Face);
        s.Update(NotchMode.Peek, true, T0.AddSeconds(9));        // hover: la vista previa no alterna
        Assert.Equal(IdleFace.Quota, s.Face);
        s.Update(NotchMode.Idle, true, T0.AddSeconds(10));
        Assert.Equal(IdleFace.Quota, s.Face);
        s.Update(NotchMode.Idle, true, T0.AddSeconds(17));
        Assert.Equal(IdleFace.Clock, s.Face);
    }

    [Theory]
    [InlineData(NotchMode.Minimal, true)]
    [InlineData(NotchMode.Working, false)]
    [InlineData(NotchMode.Idle, false)]      // reposo con Claude trabajando/avisos/música: no es calma
    public void Clock_only_in_calm_idle(NotchMode mode, bool calm)
    {
        var s = new IdleScene();
        s.Update(mode, calm, T0);
        s.Update(mode, calm, T0.AddSeconds(8));
        Assert.Equal(IdleFace.Quota, s.Face);
        Assert.Equal(0, s.Layout & IdleScene.LayoutClock);
    }

    [Fact]
    public void Clock_disabled_keeps_the_rings()
    {
        var s = new IdleScene { ClockEnabled = false };
        s.Update(NotchMode.Idle, true, T0);
        s.Update(NotchMode.Idle, true, T0.AddSeconds(8));
        Assert.Equal(IdleFace.Quota, s.Face);
    }

    [Fact]
    public void Mascot_appears_after_15s_of_calm()
    {
        var s = Calm();
        s.Update(NotchMode.Idle, true, T0.AddSeconds(14.9));
        Assert.False(s.MascotShown);
        s.Update(NotchMode.Idle, true, T0.AddSeconds(15));
        Assert.True(s.MascotShown);
        Assert.NotEqual(0, s.Layout & IdleScene.LayoutMascot);
    }

    [Fact]
    public void Mascot_replaces_the_line_when_shrunk()
    {
        var s = Calm();
        s.Update(NotchMode.Minimal, true, T0.AddSeconds(15));
        Assert.True(s.MascotShown);
        Assert.Equal(IdleScene.LayoutMascot, s.Layout);
        var size = IdleScene.SizeFor(NotchMode.Minimal, s.Layout, 0f, 1.5f);
        Assert.Equal(new SizeF(IdleScene.MinimalMascotWidth * 1.5f, IdleScene.MinimalMascotHeight * 1.5f), size);
    }

    [Fact]
    public void Activity_hides_the_mascot()
    {
        var s = Calm();
        s.Update(NotchMode.Idle, true, T0.AddSeconds(20));
        Assert.True(s.MascotShown);
        s.Update(NotchMode.Working, false, T0.AddSeconds(21));
        Assert.False(s.MascotShown);
        Assert.Equal(0, s.Layout);
        // Vuelve la calma: otros 15 s.
        s.Update(NotchMode.Idle, true, T0.AddSeconds(22));
        Assert.False(s.MascotShown);
        s.Update(NotchMode.Idle, true, T0.AddSeconds(37));
        Assert.True(s.MascotShown);
    }

    [Fact]
    public void Expanded_does_not_draw_it_but_keeps_its_clock()
    {
        var s = Calm();
        s.Update(NotchMode.Expanded, true, T0.AddSeconds(16));
        Assert.False(s.MascotShown);
        s.Update(NotchMode.Idle, true, T0.AddSeconds(17));
        Assert.True(s.MascotShown);
    }

    [Fact]
    public void Disabled_mascot_never_shows()
    {
        var s = new IdleScene { MascotEnabled = false };
        s.Update(NotchMode.Minimal, true, T0);
        s.Update(NotchMode.Minimal, true, T0.AddSeconds(30));
        Assert.False(s.MascotShown);
        Assert.Null(s.NextWakeIn(T0.AddSeconds(30), L0));
    }

    [Fact]
    public void Eyes_follow_the_pointer_only_while_over_the_notch()
    {
        var s = Calm(NotchMode.Peek);
        s.Update(NotchMode.Peek, true, T0.AddSeconds(16));
        Assert.True(s.OnPointer(new PointF(120f, 40f), T0.AddSeconds(16)));
        Assert.Equal(MascotState.LookAt, s.Look.State);
        Assert.Equal(new Point(2, 1), s.Look.Gaze);     // 120/60 = 2; 40/80 = 0,5 → 1 (redondeo)

        Assert.False(s.OnPointer(new PointF(121f, 40f), T0.AddSeconds(16.1)));   // mismo píxel: no se repinta

        Assert.True(s.OnPointer(null, T0.AddSeconds(17)));
        Assert.Equal(MascotState.Awake, s.Look.State);
        Assert.Equal(Point.Empty, s.Look.Gaze);
    }

    [Fact]
    public void Reduce_motion_keeps_the_eyes_still()
    {
        var s = new IdleScene { ReduceMotion = true };
        s.Update(NotchMode.Peek, true, T0);
        s.Update(NotchMode.Peek, true, T0.AddSeconds(16));
        s.OnPointer(new PointF(300f, 300f), T0.AddSeconds(16));
        Assert.Equal(Point.Empty, s.Look.Gaze);
    }

    [Fact]
    public void Pointer_over_the_sleeping_cat_wakes_it_and_it_looks()
    {
        var s = Calm(NotchMode.Minimal);
        var asleep = T0.AddSeconds(15 + 60 + 2);
        s.Update(NotchMode.Minimal, true, asleep);
        Assert.Equal(MascotState.Asleep, s.Look.State);

        s.Update(NotchMode.Peek, true, asleep);                     // el ratón entra: vista previa
        Assert.True(s.OnPointer(new PointF(-30f, 0f), asleep));
        Assert.Equal(MascotState.Waking, s.Look.State);
        Assert.True(s.MascotShown);

        s.Update(NotchMode.Peek, true, asleep + NotchMascot.WakingFor);
        Assert.Equal(MascotState.LookAt, s.Look.State);
        Assert.Equal(new Point(-1, 0), s.Look.Gaze);
    }

    [Fact]
    public void NextWakeIn_points_to_the_carousel_and_the_mascot()
    {
        var s = Calm();
        Assert.Equal(TimeSpan.FromSeconds(7), s.NextWakeIn(T0, L0));

        // Con el reloj a la vista también cuenta el cambio de minuto (19:55:10 local → 50 s; el carrusel, 5 s).
        s.Update(NotchMode.Idle, true, T0.AddSeconds(7));
        Assert.Equal(TimeSpan.FromSeconds(5), s.NextWakeIn(T0.AddSeconds(7), L0.AddSeconds(7)));
        var nearMinute = new DateTime(2026, 10, 29, 19, 55, 58, DateTimeKind.Local);
        Assert.Equal(TimeSpan.FromSeconds(2), s.NextWakeIn(T0.AddSeconds(7), nearMinute));
    }

    [Fact]
    public void Shrunk_and_asleep_costs_nothing()
    {
        var s = Calm(NotchMode.Minimal);
        Assert.Equal(TimeSpan.FromSeconds(15), s.NextWakeIn(T0, L0));      // aparecer
        var asleep = T0.AddSeconds(15 + 60 + 2);
        s.Update(NotchMode.Minimal, true, asleep);
        Assert.Equal(MascotState.Asleep, s.Look.State);
        Assert.Null(s.NextWakeIn(asleep, L0));
    }

    [Fact]
    public void Nothing_to_wake_for_without_calm()
    {
        var s = new IdleScene();
        s.Update(NotchMode.Working, false, T0);
        Assert.Null(s.NextWakeIn(T0, L0));
        s.Update(NotchMode.Expanded, true, T0);
        Assert.Null(s.NextWakeIn(T0.AddSeconds(1), L0));   // el gato no se pinta en el expandido: sin plazos
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Sizes_keep_the_current_idle_and_add_the_cat_slot(float k)
    {
        Assert.Equal(NotchLayout.TargetSize(NotchMode.Idle, k, 0f), IdleScene.SizeFor(NotchMode.Idle, 0, 0f, k));
        Assert.Equal(NotchLayout.TargetSize(NotchMode.Peek, k, 0f), IdleScene.SizeFor(NotchMode.Peek, 0, 0f, k));
        Assert.Equal(NotchLayout.TargetSize(NotchMode.Minimal, k, 0f), IdleScene.SizeFor(NotchMode.Minimal, 0, 0f, k));

        var idleCat = IdleScene.SizeFor(NotchMode.Idle, IdleScene.LayoutMascot, 0f, k);
        Assert.Equal((112f + IdleScene.MascotSlot) * k, idleCat.Width, 3);
        Assert.Equal(30f * k, idleCat.Height, 3);
        Assert.Equal((184f + IdleScene.MascotSlot) * k, IdleScene.SizeFor(NotchMode.Peek, IdleScene.LayoutMascot, 0f, k).Width, 3);

        // Reloj: el ancho del reposo salvo que la hora y la fecha no quepan (12 h en inglés).
        Assert.Equal(112f * k, IdleScene.SizeFor(NotchMode.Idle, IdleScene.LayoutClock, 70f, k).Width, 3);
        Assert.Equal((100f + 2 * IdleScene.ClockPadX) * k, IdleScene.SizeFor(NotchMode.Idle, IdleScene.LayoutClock, 100f, k).Width, 3);
        Assert.Equal((100f + 2 * IdleScene.ClockPadX + IdleScene.MascotSlot) * k,
            IdleScene.SizeFor(NotchMode.Idle, IdleScene.LayoutClock | IdleScene.LayoutMascot, 100f, k).Width, 3);
    }

    [Fact]
    public void Content_keeps_its_width_beside_the_cat()
    {
        var pill = new RectangleF(10f, 0f, (112f + IdleScene.MascotSlot) * 1.5f, 45f);
        var area = IdleScene.ContentArea(pill, mascot: true, 1.5f);
        Assert.Equal(112f * 1.5f, area.Width, 3);
        Assert.Equal(pill.Right, area.Right, 3);
        Assert.Equal(pill, IdleScene.ContentArea(pill, mascot: false, 1.5f));
    }

    public static IEnumerable<object[]> EyeCases()
    {
        foreach (var mode in new[] { NotchMode.Idle, NotchMode.Peek, NotchMode.Minimal })
        foreach (var k in new[] { 1f, 1.5f })
        foreach (var st in new[] { MascotState.Awake, MascotState.LookAt, MascotState.Sleepy, MascotState.Asleep, MascotState.Waking })
            yield return new object[] { mode, k, st };
    }

    /// <summary>Con la mirada al tope (±3,5 → ±4 px; −1..2) los ojos no se salen de la pastilla ni pisan el contenido.</summary>
    [Theory]
    [MemberData(nameof(EyeCases))]
    public void Eyes_stay_inside_the_pill_and_off_the_content(NotchMode mode, float k, MascotState st)
    {
        var size = IdleScene.SizeFor(mode, IdleScene.LayoutMascot, 0f, k);
        var pill = new RectangleF(100f, 0f, size.Width, size.Height);
        var center = IdleScene.MascotCenter(pill, mode, k);
        var content = IdleScene.ContentArea(pill, mode != NotchMode.Minimal, k);
        foreach (var gaze in new[] { new Point(-4, -1), new Point(4, 2), Point.Empty })
        {
            var (l, r) = IdleScene.EyeRects(center, new MascotLook(st, gaze, false), k);
            foreach (var e in new[] { l, r })
            {
                Assert.True(e.Width > 0 && e.Height > 0);
                Assert.True(pill.Contains(e), $"{mode} {st} {gaze}: ojo {e} fuera de {pill}");
                if (mode != NotchMode.Minimal)
                    Assert.True(e.Right <= content.Left + 16f * k, $"ojo {e} sobre el contenido {content}");
            }
            Assert.True(l.Right < r.Left);
        }
    }

    [Fact]
    public void Closed_eyes_are_thin_lines()
    {
        var c = new PointF(50f, 15f);
        var open = IdleScene.EyeRects(c, new MascotLook(MascotState.Awake, Point.Empty, false), 1f).Left;
        var blink = IdleScene.EyeRects(c, new MascotLook(MascotState.Awake, Point.Empty, true), 1f).Left;
        var asleep = IdleScene.EyeRects(c, new MascotLook(MascotState.Asleep, Point.Empty, false), 1f).Left;
        var sleepy = IdleScene.EyeRects(c, new MascotLook(MascotState.Sleepy, Point.Empty, false), 1f).Left;
        Assert.True(blink.Height < 2f && asleep.Height < 2f);
        Assert.True(sleepy.Height > blink.Height && sleepy.Height < open.Height);
    }
}
