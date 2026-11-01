using System.Drawing;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Tests;

/// <summary><see cref="NotchAnimator"/>: coreografía pura del notch (lenguaje de movimiento §3.0):
/// forma con muelles y ejes desfasados, contenido que sale antes y entra a p 0,6, squash solo al abrir,
/// reduce-motion = salto y, sobre todo, nada animándose cuando todo está quieto (0 % CPU).</summary>
public class NotchAnimatorTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private static readonly SizeF IdleSize = new(150, 30), WorkingSize = new(300, 34), ExpandedSize = new(520, 150);

    /// <summary>Avanza en pasos de 4 ms (≈240 Hz) de <paramref name="from"/> a <paramref name="to"/> ms.</summary>
    private static void Run(NotchAnimator a, double from, double to, Action<NotchAnimator>? each = null)
    {
        for (double t = from; t <= to; t += 4)
        {
            a.Step(Ms(t));
            each?.Invoke(a);
        }
    }

    private static NotchAnimator Settled(NotchMode mode, SizeF size, out double t)
    {
        var a = new NotchAnimator();
        a.SetTarget(mode, null, size, 1f, Ms(0));
        Run(a, 0, 1500);
        t = 1500;
        Assert.False(a.IsAnimating);
        return a;
    }

    [Fact]
    public void First_target_appears_from_hidden_and_settles()
    {
        var a = new NotchAnimator();
        a.SetTarget(NotchMode.Idle, null, IdleSize, 1f, Ms(0));
        a.Step(Ms(0));
        Assert.True(a.PillAlpha < 0.05f);
        Assert.True(a.Height < 1f);
        Assert.True(a.IsAnimating);

        Run(a, 4, 1500);
        Assert.Equal(150f, a.Width);
        Assert.Equal(30f, a.Height);
        Assert.Equal(1f, a.PillAlpha, 3);
        Assert.Equal(1f, a.ContentAlpha);
        Assert.Equal(NotchMode.Idle, a.ShownMode);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void Shape_settles_for_the_permission_arming_only_when_it_stops_moving()
    {
        // Los botones del Permiso arman a max(400 ms, forma a < 1 px y |v| < 40 px/s): el clic se acierta sobre el
        // rectángulo animado y no debe valer mientras se mueve bajo el cursor.
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        Assert.True(a.ShapeSettled);
        a.SetTarget(NotchMode.Expanded, null, ExpandedSize, 1f, Ms(t));
        a.Step(Ms(t + 16));
        Assert.False(a.ShapeSettled);
        Run(a, t + 20, t + 1500);
        Assert.True(a.ShapeSettled);
    }

    [Fact]
    public void Settled_animator_does_not_move()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        a.Step(Ms(t + 500));
        Assert.Equal(150f, a.Width);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void Growing_starts_width_before_height()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        a.SetTarget(NotchMode.Expanded, null, ExpandedSize, 1f, Ms(t));
        Run(a, t, t + 32);
        Assert.True(a.Width > 151f);
        Assert.Equal(30f, a.Height);
        Run(a, t + 36, t + 1500);
        Assert.Equal(520f, a.Width);
        Assert.Equal(150f, a.Height);
    }

    [Fact]
    public void Shrinking_waits_then_moves_height_before_width()
    {
        var a = Settled(NotchMode.Expanded, ExpandedSize, out double t);
        a.SetTarget(NotchMode.Idle, null, IdleSize, 1f, Ms(t));
        Run(a, t, t + 32);
        Assert.Equal(520f, a.Width);
        Assert.Equal(150f, a.Height);
        Assert.True(a.ContentAlpha < 1f);           // el contenido ya se está yendo
        Run(a, t + 36, t + 72);
        Assert.True(a.Height < 150f);
        Assert.Equal(520f, a.Width);
        Run(a, t + 76, t + 1500);
        Assert.Equal(150f, a.Width);
        Assert.Equal(30f, a.Height);
    }

    [Fact]
    public void Content_fades_out_old_mode_then_in_new_one()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        a.SetTarget(NotchMode.Working, null, WorkingSize, 1f, Ms(t));
        Run(a, t, t + 20);
        Assert.Equal(NotchMode.Idle, a.ShownMode);
        Assert.InRange(a.ContentAlpha, 0.01f, 0.99f);
        Run(a, t + 24, t + 76);
        Assert.Equal(NotchMode.Working, a.ShownMode);
        Assert.Equal(0f, a.ContentAlpha);
        Run(a, t + 80, t + 1500);
        Assert.Equal(1f, a.ContentAlpha);
        Assert.Equal(0f, a.ContentOffsetY);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void Entering_content_rises_in_whole_pixels()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        a.SetTarget(NotchMode.Expanded, null, ExpandedSize, 1f, Ms(t));
        var offsets = new List<float>();
        Run(a, t, t + 600, x => { if (x.ContentAlpha > 0) offsets.Add(x.ContentOffsetY); });
        Assert.Contains(offsets, o => o > 0);
        Assert.All(offsets, o => Assert.Equal(MathF.Round(o), o));
        Assert.All(offsets, o => Assert.InRange(o, 0f, NotchMotion.ContentRise));
    }

    [Fact]
    public void Resizing_within_the_same_mode_does_not_fade_the_content()
    {
        var a = Settled(NotchMode.Working, WorkingSize, out double t);
        a.SetTarget(NotchMode.Working, null, new SizeF(380, 34), 1f, Ms(t));
        Run(a, t, t + 1500, x => Assert.Equal(1f, x.ContentAlpha));
        Assert.Equal(380f, a.Width);
    }

    [Fact]
    public void A_new_alert_of_the_same_mode_crossfades()
    {
        var a = new NotchAnimator();
        var first = new NotchAlert(NotchAlertKind.Completed, "a", "web", null, DateTime.UnixEpoch, null);
        var second = first with { SessionId = "b", Title = "api" };
        a.SetTarget(NotchMode.Completed, first, new SizeF(310, 60), 1f, Ms(0));
        Run(a, 0, 1500);
        a.SetTarget(NotchMode.Completed, second, new SizeF(310, 60), 1f, Ms(1500));
        Run(a, 1500, 1520);
        Assert.True(a.ContentAlpha < 1f);
        Run(a, 1524, 3000);
        Assert.Same(second, a.ShownAlert);
        Assert.Equal(1f, a.ContentAlpha);
    }

    [Fact]
    public void Reduce_motion_snaps_everything()
    {
        var a = new NotchAnimator { ReduceMotion = true };
        a.SetTarget(NotchMode.Idle, null, IdleSize, 1f, Ms(0));
        a.Step(Ms(0));
        Assert.Equal(150f, a.Width);
        Assert.Equal(1f, a.PillAlpha);
        a.SetTarget(NotchMode.Expanded, null, ExpandedSize, 1f, Ms(10));
        a.Step(Ms(10));
        Assert.Equal(520f, a.Width);
        Assert.Equal(150f, a.Height);
        Assert.Equal(NotchMode.Expanded, a.ShownMode);
        Assert.Equal(1f, a.ContentAlpha);
        Assert.Equal(0f, a.Squash);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void Squash_only_while_opening_a_wide_change()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        float maxOpen = 0;
        a.SetTarget(NotchMode.Expanded, null, ExpandedSize, 1f, Ms(t));
        Run(a, t, t + 1500, x => maxOpen = Math.Max(maxOpen, Math.Abs(x.Squash)));
        Assert.True(maxOpen > 0.3f);

        a.SetTarget(NotchMode.Idle, null, IdleSize, 1f, Ms(t + 1500));
        Run(a, t + 1500, t + 3000, x => Assert.Equal(0f, x.Squash));
    }

    [Fact]
    public void Dimming_lowers_the_whole_pill_and_restores_it()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        a.SetDimmed(true, Ms(t));
        Assert.True(a.IsAnimating);
        Run(a, t, t + 1000);
        Assert.Equal(NotchMotion.SnapDimAlpha, a.PillAlpha, 2);
        a.SetDimmed(false, Ms(t + 1000));
        Run(a, t + 1000, t + 2000);
        Assert.Equal(1f, a.PillAlpha, 3);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void Shadow_and_halo_follow_the_mode()
    {
        var a = new NotchAnimator();
        var alert = new NotchAlert(NotchAlertKind.QuotaAlert, "quota:5h", "5h", "96%", DateTime.UnixEpoch, null);
        a.SetTarget(NotchMode.QuotaAlert, alert, new SizeF(310, 60), 1f, Ms(0));
        Run(a, 0, 1500);
        Assert.Equal(1f, a.ShadowAlpha, 2);
        Assert.Equal(1f, a.HaloAlpha, 2);
        a.SetTarget(NotchMode.Idle, null, IdleSize, 1f, Ms(1500));
        Run(a, 1500, 3000);
        Assert.Equal(0f, a.ShadowAlpha, 2);
        Assert.Equal(0f, a.HaloAlpha, 2);
    }

    [Fact]
    public void Hidden_target_fades_the_pill_out()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        a.SetTarget(NotchMode.Hidden, null, SizeF.Empty, 1f, Ms(t));
        Assert.False(a.IsGone);
        Run(a, t, t + 1500);
        Assert.True(a.IsGone);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void Reset_makes_the_next_target_appear_again()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        a.Reset();
        a.SetTarget(NotchMode.Idle, null, IdleSize, 1f, Ms(t + 10));
        a.Step(Ms(t + 10));
        Assert.True(a.PillAlpha < 0.05f);
        Assert.True(a.IsAnimating);
    }

    [Fact]
    public void Spinner_only_counts_while_working_content_is_shown()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out _);
        Assert.False(a.SpinnerVisible(anyActive: true));
        var w = Settled(NotchMode.Working, WorkingSize, out _);
        Assert.True(w.SpinnerVisible(anyActive: true));
        Assert.False(w.SpinnerVisible(anyActive: false));
        w.ReduceMotion = true;
        Assert.False(w.SpinnerVisible(anyActive: true));
    }

    /// <summary>Bug real: con el notch abierto en la lista de sesiones, los círculos de «trabajando» se paraban al
    /// segundo (al asentarse la apertura el bucle se detenía porque el spinner solo contaba en Trabajando/Vista previa).</summary>
    [Fact]
    public void Spinner_keeps_running_in_expanded_while_the_session_list_is_on_screen()
    {
        var e = Settled(NotchMode.Expanded, ExpandedSize, out _);
        Assert.False(e.IsAnimating);
        Assert.True(e.SpinnerVisible(anyActive: true, sessionsOnScreen: true));
        Assert.False(e.SpinnerVisible(anyActive: true, sessionsOnScreen: false));
        Assert.False(e.SpinnerVisible(anyActive: false, sessionsOnScreen: true));
        Assert.False(e.SpinnerVisible(anyActive: true));
        e.ReduceMotion = true;
        Assert.False(e.SpinnerVisible(anyActive: true, sessionsOnScreen: true));
    }

    /// <summary>F5 · B3: otra maquetación dentro del mismo modo (reposo con anillos ↔ reposo con la canción) es un cambio
    /// de contenido: el viejo sale, la forma cambia con Abrir/Cerrar y el nuevo entra (lenguaje de movimiento §3.0); lo
    /// que se pinta (<see cref="NotchAnimator.ShownLayout"/>) cambia cuando el viejo ya se fue.</summary>
    [Fact]
    public void Layout_change_within_a_mode_crossfades_the_content()
    {
        var a = Settled(NotchMode.Idle, IdleSize, out double t);
        Assert.Equal(0, a.ShownLayout);
        a.SetTarget(NotchMode.Idle, null, new SizeF(200, 30), 1f, Ms(t), layout: 1);
        a.Step(Ms(t + 20));
        Assert.True(a.ContentAlpha < 1f);
        Assert.Equal(0, a.ShownLayout);              // aún sale el viejo
        Run(a, t + 24, t + 1500);
        Assert.Equal(1, a.ShownLayout);
        Assert.Equal(1f, a.ContentAlpha);
        Assert.Equal(200f, a.Width);
        Assert.False(a.IsAnimating);

        // Mismo modo y misma maquetación: solo se reajusta el tamaño, sin tocar el contenido.
        a.SetTarget(NotchMode.Idle, null, new SizeF(210, 30), 1f, Ms(t + 1500), layout: 1);
        Run(a, t + 1500, t + 3000, x => Assert.Equal(1f, x.ContentAlpha));
    }

    [Fact]
    public void Layout_snaps_with_reduce_motion_and_resets()
    {
        var a = new NotchAnimator { ReduceMotion = true };
        a.SetTarget(NotchMode.Idle, null, IdleSize, 1f, Ms(0), layout: 1);
        a.Step(Ms(0));
        Assert.Equal(1, a.ShownLayout);
        a.Reset();
        Assert.Equal(0, a.ShownLayout);
    }

    /// <summary>Letra compacta con el ratón encima (29-oct): la Vista previa pinta el contenido del reposo
    /// (<c>content</c>), sin cruce ni subida, así la línea de la letra no salta; la sombra sí es la de la Vista previa. Al
    /// salir vuelve igual, sin cruce.</summary>
    [Fact]
    public void Peek_with_idle_content_keeps_the_content_without_crossfade()
    {
        var size = new SizeF(300, 34);
        var a = new NotchAnimator();
        a.SetTarget(NotchMode.Idle, null, size, 1f, Ms(0), layout: 9);
        Run(a, 0, 1500);
        Assert.Equal(0f, a.ShadowAlpha);

        a.SetTarget(NotchMode.Peek, null, size, 1f, Ms(1500), layout: 9, content: NotchMode.Idle);
        Run(a, 1500, 3000, x =>
        {
            Assert.Equal(1f, x.ContentAlpha);
            Assert.Equal(0f, x.ContentOffsetY);
            Assert.Equal(NotchMode.Idle, x.ShownMode);
            Assert.Equal(9, x.ShownLayout);
            Assert.Equal(300f, x.Width);
        });
        Assert.Equal(NotchMotion.ShadowOpacity(NotchMode.Peek), a.ShadowAlpha, 2);
        Assert.False(a.IsAnimating);

        a.SetTarget(NotchMode.Idle, null, size, 1f, Ms(3000), layout: 9);
        Run(a, 3000, 4500, x => Assert.Equal(1f, x.ContentAlpha));
        Assert.Equal(NotchMode.Idle, a.ShownMode);
        Assert.Equal(0f, a.ShadowAlpha);
        Assert.False(a.IsAnimating);

        // La Vista previa de siempre (sin content) sí cambia el contenido.
        a.SetTarget(NotchMode.Peek, null, size, 1f, Ms(4500), layout: 9);
        Run(a, 4500, 4520);
        Assert.True(a.ContentAlpha < 1f);
        Run(a, 4524, 6000);
        Assert.Equal(NotchMode.Peek, a.ShownMode);
    }

    [Fact]
    public void Content_mode_snaps_with_reduce_motion_and_hiding_ignores_it()
    {
        var a = new NotchAnimator { ReduceMotion = true };
        a.SetTarget(NotchMode.Peek, null, IdleSize, 1f, Ms(0), layout: 9, content: NotchMode.Idle);
        a.Step(Ms(0));
        Assert.Equal(NotchMode.Idle, a.ShownMode);
        Assert.Equal(1f, a.ContentAlpha);

        a.SetTarget(NotchMode.Hidden, null, IdleSize, 1f, Ms(10), content: NotchMode.Idle);
        a.Step(Ms(10));
        Assert.Equal(0f, a.ContentAlpha);
        Assert.True(a.IsGone);
    }
}
