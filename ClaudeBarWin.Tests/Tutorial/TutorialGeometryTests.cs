using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.UI.Onboarding;
using ClaudeBarWin.UI.Tutorial;

namespace ClaudeBarWin.Tests.Tutorial;

/// <summary>La geometría del tutorial (<see cref="TutorialGeometry"/>): cabe en 1366 × 768 y escala con el DPI, el escenario
/// lleva el notch de demostración más grande, nada se pisa y el clic y el foco salen de la misma cuenta que se pinta.</summary>
public sealed class TutorialGeometryTests
{
    private static readonly RectangleF Pill = OnboardingGeometry.PillRect(TutorialGeometry.Width, TutorialGeometry.Height, 500f);
    private static readonly int Chapters = TutorialScript.Chapters.Count;

    private static Point Center(RectangleF r) => new((int)(r.Left + r.Width / 2f), (int)(r.Top + r.Height / 2f));

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void Fits_in_1366x768_with_the_taskbar_at_any_dpi(int dpi)
    {
        var monitor = new Rectangle(0, 0, 1366, 768);
        var work = new Rectangle(0, 0, 1366, 728);
        float s = OnboardingPlacement.Scale(dpi, monitor, work, TutorialGeometry.Width, TutorialGeometry.Height);
        var surface = OnboardingPlacement.Surface(s, TutorialGeometry.Width, TutorialGeometry.Height);
        Assert.True(surface.Height <= work.Height, $"{dpi}: {surface.Height} > {work.Height}");
        Assert.True(surface.Width <= monitor.Width, $"{dpi}: {surface.Width} > {monitor.Width}");
        if (dpi == 96) Assert.Equal(1f, s);
        Assert.True(s >= 1f, $"{dpi}: se encoge por debajo de 1 ({s})");
    }

    [Fact]
    public void Scales_with_dpi_on_a_big_screen()
    {
        var monitor = new Rectangle(0, 0, 3840, 2160);
        Assert.Equal(1.5f, OnboardingPlacement.Scale(144, monitor, monitor, TutorialGeometry.Width, TutorialGeometry.Height));
        Assert.Equal(2f, OnboardingPlacement.Scale(192, monitor, monitor, TutorialGeometry.Width, TutorialGeometry.Height));
    }

    [Fact]
    public void The_stage_holds_the_biggest_demo_notch_with_its_shadow()
    {
        var stage = TutorialGeometry.Stage(Pill, 1f);
        float s = TutorialGeometry.DemoScale;
        var rows = new PageRows(8, TutorialDemo.TaskCount + 1, 1);
        foreach (var page in TutorialDemo.Pages)
        {
            var size = NotchLayout.PageSize(page, rows, withAlert: true, withDots: true, s);
            Assert.True(size.Width <= stage.Width, $"{page}: {size.Width} > {stage.Width}");
            Assert.True(size.Height + NotchMotion.ShadowBlurRadius + NotchMotion.ShadowOffsetY <= stage.Height,
                $"{page}: {size.Height} no cabe con su sombra en {stage.Height}");
        }
        var permission = NotchLayout.PermissionButtonsSize(s, NotchLayout.PermissionDetailMaxLines);
        Assert.True(permission.Height <= stage.Height);
    }

    [Fact]
    public void The_blocks_do_not_overlap()
    {
        float s = 1f;
        Assert.True(TutorialGeometry.ChapterTitle(Pill, s).Bottom <= TutorialGeometry.ChapterMark(Pill, s, Chapters, 0).Top);
        Assert.True(TutorialGeometry.ChapterCell(Pill, s, Chapters, 0).Bottom <= TutorialGeometry.Stage(Pill, s).Top);
        Assert.True(TutorialGeometry.Stage(Pill, s).Bottom <= TutorialGeometry.StepLabel(Pill, s).Top);
        Assert.True(TutorialGeometry.StepLabel(Pill, s).Bottom <= TutorialGeometry.StepTitle(Pill, s).Top);
        Assert.True(TutorialGeometry.StepTitle(Pill, s).Bottom <= TutorialGeometry.StepText(Pill, s).Top);
        Assert.True(TutorialGeometry.StepText(Pill, s).Bottom <= TutorialGeometry.KeyHint(Pill, s).Top);
        Assert.True(TutorialGeometry.KeyHint(Pill, s).Bottom <= TutorialGeometry.Back(Pill, s).Top);
        Assert.True(TutorialGeometry.ProLink(Pill, s).Bottom <= TutorialGeometry.Next(Pill, s).Top);
        Assert.False(TutorialGeometry.KeyHint(Pill, s).IntersectsWith(TutorialGeometry.ProLink(Pill, s)));
        Assert.False(TutorialGeometry.StepTitle(Pill, s).IntersectsWith(TutorialGeometry.DoneBadge(Pill, s)));
        Assert.False(TutorialGeometry.ChapterLabel(Pill, s).IntersectsWith(TutorialGeometry.Exit(Pill, s)));
        Assert.True(TutorialGeometry.Next(Pill, s).Bottom <= Pill.Bottom);
    }

    [Fact]
    public void Chapter_marks_fill_the_row_without_overlapping()
    {
        for (int i = 0; i < Chapters; i++)
        {
            var r = TutorialGeometry.ChapterMark(Pill, 1f, Chapters, i);
            Assert.True(r.Width > 20f);
            Assert.True(r.Left >= Pill.Left + TutorialGeometry.PadX - 0.01f && r.Right <= Pill.Right - TutorialGeometry.PadX + 0.01f);
            if (i > 0) Assert.True(TutorialGeometry.ChapterMark(Pill, 1f, Chapters, i - 1).Right < r.Left);
        }
    }

    [Fact]
    public void Step_dots_sit_between_back_and_skip_for_the_longest_chapter()
    {
        int most = TutorialScript.Chapters.Max(c => TutorialScript.StepsOf(c).Count);
        var first = TutorialGeometry.StepDotCell(Pill, 1f, most, 0);
        var last = TutorialGeometry.StepDotCell(Pill, 1f, most, most - 1);
        Assert.True(first.Left >= TutorialGeometry.Back(Pill, 1f).Right, "el primer punto pisa «Atrás»");
        Assert.True(last.Right <= TutorialGeometry.Skip(Pill, 1f).Left, "el último punto pisa «Saltar paso»");
        Assert.False(TutorialGeometry.Skip(Pill, 1f).IntersectsWith(TutorialGeometry.Next(Pill, 1f)));
    }

    [Fact]
    public void Hit_test_finds_each_control_at_its_center()
    {
        int steps = 3;
        Assert.Equal(new TutorialHit(TutorialControl.Back), TutorialGeometry.HitTest(Center(TutorialGeometry.Back(Pill, 1f)), Pill, 1f,
            Chapters, steps, TutorialPhase.Step, false));
        Assert.Equal(new TutorialHit(TutorialControl.Next), TutorialGeometry.HitTest(Center(TutorialGeometry.Next(Pill, 1f)), Pill, 1f,
            Chapters, steps, TutorialPhase.Step, false));
        Assert.Equal(new TutorialHit(TutorialControl.Skip), TutorialGeometry.HitTest(Center(TutorialGeometry.Skip(Pill, 1f)), Pill, 1f,
            Chapters, steps, TutorialPhase.Step, false));
        Assert.Equal(new TutorialHit(TutorialControl.Exit), TutorialGeometry.HitTest(Center(TutorialGeometry.Exit(Pill, 1f)), Pill, 1f,
            Chapters, steps, TutorialPhase.Step, false));
        for (int i = 0; i < Chapters; i++)
            Assert.Equal(new TutorialHit(TutorialControl.Chapter, i),
                TutorialGeometry.HitTest(Center(TutorialGeometry.ChapterMark(Pill, 1f, Chapters, i)), Pill, 1f, Chapters, steps,
                    TutorialPhase.Step, false));
        for (int i = 0; i < steps; i++)
            Assert.Equal(new TutorialHit(TutorialControl.StepDot, i),
                TutorialGeometry.HitTest(Center(TutorialGeometry.StepDotCell(Pill, 1f, steps, i)), Pill, 1f, Chapters, steps,
                    TutorialPhase.Step, false));
        Assert.Equal(new TutorialHit(TutorialControl.Notch), TutorialGeometry.HitTest(Center(TutorialGeometry.Stage(Pill, 1f)), Pill,
            1f, Chapters, steps, TutorialPhase.Step, false));
        // El enlace del Pro solo cuando se ve; los puntos de los pasos, solo en un paso.
        var link = Center(TutorialGeometry.ProLink(Pill, 1f));
        Assert.Equal(TutorialControl.ProLink, TutorialGeometry.HitTest(link, Pill, 1f, Chapters, steps, TutorialPhase.ChapterEnd, true).Control);
        Assert.Equal(TutorialControl.None, TutorialGeometry.HitTest(link, Pill, 1f, Chapters, steps, TutorialPhase.ChapterEnd, false).Control);
        var dot = Center(TutorialGeometry.StepDotCell(Pill, 1f, steps, 1));
        Assert.Equal(TutorialControl.None, TutorialGeometry.HitTest(dot, Pill, 1f, Chapters, steps, TutorialPhase.ChapterEnd, false).Control);
        Assert.Equal(TutorialHit.None, TutorialGeometry.HitTest(new Point(-50, -50), Pill, 1f, Chapters, steps, TutorialPhase.Step, false));
    }

    [Fact]
    public void Tab_goes_through_the_usable_controls_and_wraps()
    {
        var order = TutorialGeometry.FocusOrder(Chapters, 4, TutorialPhase.Step, false, _ => true);
        Assert.Equal(TutorialControl.Notch, order[0].Control);
        Assert.DoesNotContain(order, h => h.Control == TutorialControl.StepDot);
        Assert.DoesNotContain(order, h => h.Control == TutorialControl.ProLink);
        Assert.Equal(1 + Chapters + 4, order.Count);   // notch, capítulos, Atrás, Saltar, Siguiente, Salir
        Assert.Contains(TutorialGeometry.FocusOrder(Chapters, 4, TutorialPhase.ChapterEnd, true, _ => true),
            h => h.Control == TutorialControl.ProLink);
        var noBack = TutorialGeometry.FocusOrder(Chapters, 4, TutorialPhase.Step, false, h => h.Control != TutorialControl.Back);
        Assert.DoesNotContain(noBack, h => h.Control == TutorialControl.Back);
        Assert.Equal(order[0], TutorialGeometry.MoveFocus(order, TutorialHit.None, 1));
        Assert.Equal(order[^1], TutorialGeometry.MoveFocus(order, TutorialHit.None, -1));
        Assert.Equal(order[0], TutorialGeometry.MoveFocus(order, order[^1], 1));
        Assert.Equal(order[^1], TutorialGeometry.MoveFocus(order, order[0], -1));
        Assert.Equal(TutorialHit.None, TutorialGeometry.MoveFocus(Array.Empty<TutorialHit>(), TutorialHit.None, 1));
    }

    [Fact]
    public void The_mock_tray_stays_inside_the_stage()
    {
        float s = 1f;
        var stage = TutorialGeometry.Stage(Pill, s);
        bool Inside(RectangleF r) => r.Left >= stage.Left - 0.01f && r.Top >= stage.Top - 0.01f && r.Right <= stage.Right + 0.01f
            && r.Bottom <= stage.Bottom + 0.01f;
        Assert.True(Inside(TutorialGeometry.Taskbar(stage, s)));
        Assert.True(Inside(TutorialGeometry.TrayIcon(stage, s)));
        Assert.True(Inside(TutorialGeometry.Panel(stage, s)));
        Assert.True(Inside(TutorialGeometry.Menu(stage, s)));
        Assert.True(Inside(TutorialGeometry.MonitorMenu(stage, s, 4)));
        Assert.True(Inside(TutorialGeometry.Settings(stage, s)));
        Assert.True(Inside(TutorialGeometry.SampleFile(stage, s)));
        Assert.True(Inside(TutorialGeometry.Effect(stage, s)));
        var items = Enum.GetValues<TutorialMenuItem>().Select(i => TutorialGeometry.MenuItemRect(stage, s, i)).ToList();
        for (int i = 1; i < items.Count; i++) Assert.True(items[i - 1].Bottom <= items[i].Top + 0.01f);
        Assert.True(items[^1].Bottom <= TutorialGeometry.Menu(stage, s).Bottom);
        var rows = Enum.GetValues<TutorialSettingsRow>().Select(r => TutorialGeometry.SettingsRowRect(stage, s, r)).ToList();
        Assert.True(rows[^1].Bottom <= TutorialGeometry.Settings(stage, s).Bottom);
        // El submenú sale a la izquierda del menú, sin taparlo.
        Assert.True(TutorialGeometry.MonitorMenu(stage, s, 4).Right <= TutorialGeometry.Menu(stage, s).Left + 2f * s + 0.01f);
        Assert.True(TutorialGeometry.Taskbar(stage, s).Top >= TutorialGeometry.Menu(stage, s).Bottom);
    }

    [Fact]
    public void Geometry_scales_linearly()
    {
        var pill2 = OnboardingGeometry.PillRect(TutorialGeometry.Width * 2f, TutorialGeometry.Height * 2f, 1000f);
        var a = TutorialGeometry.Next(Pill, 1f);
        var b = TutorialGeometry.Next(pill2, 2f);
        Assert.Equal(a.Width * 2f, b.Width, 3);
        Assert.Equal((a.Top - Pill.Top) * 2f, b.Top - pill2.Top, 3);
        Assert.Equal(TutorialGeometry.Stage(Pill, 1f).Height * 2f, TutorialGeometry.Stage(pill2, 2f).Height, 3);
    }
}
