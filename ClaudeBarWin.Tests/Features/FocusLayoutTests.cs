using System.Drawing;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Geometría de la página Foco (<see cref="FocusLayout"/>), la MISMA para pintar y para el clic, y a qué tarea pasa el enlace
/// del foco (<see cref="FocusTasks"/>). PURO.
/// </summary>
public class FocusLayoutTests
{
    /// <summary>Zona de contenido del expandido de 480 a escala <paramref name="s"/> (márgenes de 18 y 12 / 14).</summary>
    private static RectangleF Inner(float s) => new(100f * s + 18f * s, 12f * s, (480f - 36f) * s, FocusLayout.ContentHeight * s);

    public static TheoryData<float> Scales() => new() { 1f, 1.15f, 1.25f, 1.5f, 2f };

    [Theory]
    [MemberData(nameof(Scales))]
    public void Everything_fits_inside_the_content_area_and_nothing_overlaps(float s)
    {
        var inner = Inner(s);
        var r = FocusLayout.Compute(inner, s);
        var all = new[] { r.Ring, r.Header, r.Phase, r.Primary, r.Skip, r.Reset, r.Task, r.Stats };
        foreach (var rect in all)
        {
            Assert.True(rect.Width > 0 && rect.Height > 0, $"{rect} vacío");
            Assert.True(rect.Left >= inner.Left - 0.01f && rect.Right <= inner.Right + 0.01f, $"{rect} se sale a lo ancho de {inner}");
            Assert.True(rect.Top >= inner.Top - 0.01f && rect.Bottom <= inner.Bottom + 0.01f, $"{rect} se sale a lo alto de {inner}");
        }
        // Columna derecha en filas, de arriba abajo y sin solaparse; los botones, en su fila y uno tras otro.
        var rows = new[] { r.Header, r.Phase, r.Primary, r.Task, r.Stats };
        for (int i = 1; i < rows.Length; i++) Assert.True(rows[i].Top >= rows[i - 1].Bottom - 0.01f, $"fila {i} pisa la anterior");
        Assert.True(r.Skip.Left >= r.Primary.Right && r.Reset.Left >= r.Skip.Right);
        Assert.True(r.Ring.Right <= r.Header.Left);
        Assert.Equal(r.Skip.Width, r.Skip.Height, 3);
        // El reloj y los puntos del ciclo, dentro del anillo.
        Assert.True(r.Ring.Contains(new PointF(r.Clock.Left + r.Clock.Width / 2f, r.Clock.Top + r.Clock.Height / 2f)));
        for (int i = 0; i < FocusDurations.MaxEvery; i++)
        {
            var dot = r.CycleDot(i, FocusDurations.MaxEvery);
            Assert.True(dot.Left >= r.Ring.Left + FocusLayout.RingStroke * s && dot.Right <= r.Ring.Right - FocusLayout.RingStroke * s,
                $"punto {i} fuera del anillo");
            Assert.True(dot.Bottom < r.Ring.Bottom - FocusLayout.RingStroke * s);
        }
        // Las barras de la semana y su texto, en su fila.
        for (int i = 0; i < PomodoroTimer.WeekDays; i++) Assert.True(r.Stats.Contains(r.StatsBar(i)), $"barra {i}");
        Assert.True(r.StatsLabel.Left >= r.StatsBar(PomodoroTimer.WeekDays - 1).Right);
        Assert.True(r.Task.Contains(r.Chip(r.Task)) && r.Stats.Contains(r.Chip(r.Stats)));
    }

    [Fact]
    public void The_content_fits_the_expanded_cap_with_the_alert_line_and_the_dots()
    {
        // 12 + contenido + 14, más la línea del aviso (22 + 4) y los puntos (16): el tope del expandido (184).
        Assert.True(12f + FocusLayout.ContentHeight + 14f + 26f + 16f <= 184f + 0.01f);
        Assert.True(FocusLayout.Ring <= FocusLayout.ContentHeight);
        Assert.True(FocusLayout.Ring + FocusLayout.RingGap + FocusLayout.PrimaryWidth + 2 * (FocusLayout.ButtonGap + FocusLayout.IconButton)
            <= 480f - 36f);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Clicks_land_on_what_is_painted_there(float s)
    {
        var r = FocusLayout.Compute(Inner(s), s);
        PointF Mid(RectangleF x) => new(x.Left + x.Width / 2f, x.Top + x.Height / 2f);
        Assert.Equal(FocusControl.Primary, FocusLayout.HitTest(Mid(r.Primary), r, locked: false).Control);
        Assert.Equal(FocusControl.Skip, FocusLayout.HitTest(Mid(r.Skip), r, locked: false).Control);
        Assert.Equal(FocusControl.Reset, FocusLayout.HitTest(Mid(r.Reset), r, locked: false).Control);
        Assert.Equal(FocusControl.Task, FocusLayout.HitTest(Mid(r.Task), r, locked: false).Control);
        // Sin el Pro: la tarea y la semana abren la tarjeta; los botones del pomodoro siguen siendo gratis.
        Assert.Equal(FocusControl.ProChip, FocusLayout.HitTest(Mid(r.Task), r, locked: true).Control);
        Assert.Equal(FocusControl.ProChip, FocusLayout.HitTest(Mid(r.Stats), r, locked: true).Control);
        Assert.Equal(FocusControl.Primary, FocusLayout.HitTest(Mid(r.Primary), r, locked: true).Control);
        // El anillo, la cabecera y la semana (con el Pro) no son botones: un clic suelto no pausa sin querer.
        Assert.Equal(FocusControl.None, FocusLayout.HitTest(Mid(r.Ring), r, locked: false).Control);
        Assert.Equal(FocusControl.None, FocusLayout.HitTest(Mid(r.Header), r, locked: false).Control);
        Assert.Equal(FocusControl.None, FocusLayout.HitTest(Mid(r.Stats), r, locked: false).Control);
        Assert.Equal(FocusControl.None, FocusLayout.HitTest(new PointF(-5, -5), r, locked: false).Control);
    }

    [Fact]
    public void The_closed_faces_keep_the_idle_height_and_the_working_slot_only_when_shown()
    {
        Assert.Equal(new SizeF(FocusFace.IdleWidth * 1.5f, FocusFace.IdleHeight * 1.5f), FocusFace.IdleSize(1.5f));
        Assert.Equal(30f, FocusFace.IdleHeight);
        Assert.Equal(FocusFace.WorkingSlot, FocusFace.WorkingExtra(true));
        Assert.Equal(0f, FocusFace.WorkingExtra(false));
        Assert.True(FocusFace.WorkingRing + 8f <= FocusFace.WorkingSlot);
    }

    // ---------------------------------------------------------------- tarea enlazada

    [Fact]
    public void The_task_link_cycles_through_the_pending_tasks_and_back_to_none()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var pending = new[] { a, b, c };
        Assert.Equal(a, FocusTasks.Next(pending, null));
        Assert.Equal(b, FocusTasks.Next(pending, a));
        Assert.Equal(c, FocusTasks.Next(pending, b));
        Assert.Null(FocusTasks.Next(pending, c));
        Assert.Equal(a, FocusTasks.Next(pending, Guid.NewGuid()));   // la enlazada ya no está pendiente: la primera
        Assert.Null(FocusTasks.Next(Array.Empty<Guid>(), a));
    }

    // ---- Decisión del dueño: saltar y reiniciar siguen como iconos, con su nombre en un tooltip del notch ----

    [Theory]
    [MemberData(nameof(Scales))]
    public void The_icon_tooltip_sits_just_above_its_button_without_covering_it_or_the_header(float s)
    {
        var inner = Inner(s);
        var r = FocusLayout.Compute(inner, s);
        foreach (var button in new[] { r.Skip, r.Reset })
        {
            var tip = FocusLayout.TipBox(button, 60f * s, inner, s);
            Assert.Equal(FocusLayout.TipHeight * s, tip.Height, 3);
            Assert.Equal(button.Top - FocusLayout.TipGap * s, tip.Bottom, 3);
            Assert.False(tip.IntersectsWith(button));
            Assert.True(tip.Top >= r.Header.Bottom - 0.01f, $"{tip} tapa la cabecera {r.Header}");
            Assert.True(tip.Left >= inner.Left - 0.01f && tip.Right <= inner.Right + 0.01f, $"{tip} se sale de {inner}");
            // Centrado sobre el icono si cabe.
            Assert.Equal(button.Left + button.Width / 2f, tip.Left + tip.Width / 2f, 2);
        }
    }

    [Fact]
    public void A_long_tooltip_stays_inside_the_page_on_the_right()
    {
        var inner = Inner(1f);
        var r = FocusLayout.Compute(inner, 1f);
        var tip = FocusLayout.TipBox(r.Reset, 400f, inner, 1f);
        Assert.Equal(inner.Right, tip.Right, 2);
        Assert.Equal(400f + 2f * FocusLayout.TipPadX, tip.Width, 2);
    }

    [Fact]
    public void Only_skip_and_reset_have_a_tooltip_with_their_name()
    {
        var t = new FocusTexts("24:59", "25 min", "Foco", "", "Foco", false, "Saltar a la siguiente fase", "Reiniciar esta fase",
            "Pausa", "", true, null, "", "", "");
        Assert.Equal("Saltar a la siguiente fase", t.TipFor(FocusControl.Skip));
        Assert.Equal("Reiniciar esta fase", t.TipFor(FocusControl.Reset));
        Assert.Null(t.TipFor(FocusControl.Primary));
        Assert.Null(t.TipFor(FocusControl.None));
    }

    // ---- Decisión del dueño: en el reposo, el pomodoro en marcha manda; en pausa, si suena música, vuelve la canción ----

    [Theory]
    [InlineData(FocusRun.Running, false, true)]
    [InlineData(FocusRun.Running, true, true)]     // en marcha: el mini anillo, aunque suene música
    [InlineData(FocusRun.Paused, false, true)]     // en pausa y sin música: sigue el anillo
    [InlineData(FocusRun.Paused, true, false)]     // en pausa con música: vuelve la canción
    [InlineData(FocusRun.Ready, false, false)]
    [InlineData(FocusRun.Ready, true, false)]
    public void The_idle_shows_the_running_pomodoro_but_a_paused_one_yields_to_the_song(FocusRun run, bool song, bool ring) =>
        Assert.Equal(ring, FocusFace.OnIdle(run, song));
}