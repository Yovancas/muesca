using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Tutorial;

namespace ClaudeBarWin.Tests.Tutorial;

/// <summary>
/// La máquina de pasos del tutorial (<see cref="TutorialMachine"/>) y su guion (<see cref="TutorialScript"/>), sin WinForms:
/// avanzar, saltar, volver, retomar, el cierre de capítulo, qué gesto completa cada paso y su alternativa de teclado.
/// </summary>
public sealed class TutorialMachineTests
{
    private static IEnumerable<TutorialStep> AllSteps => Enum.GetValues<TutorialStep>();

    public static IEnumerable<object[]> Steps => AllSteps.Select(s => new object[] { s });

    /// <summary>Una máquina en el paso <paramref name="step"/>, con su demostración inicial.</summary>
    private static TutorialMachine At(TutorialStep step)
    {
        var m = new TutorialMachine();
        var info = TutorialScript.Info(step);
        m.GoTo(info.Chapter);
        m.GoToStep(TutorialScript.StepsOf(info.Chapter).ToList().IndexOf(step));
        Assert.Equal(step, m.Step);
        return m;
    }

    // ---------------------------------------------------------------- guion

    [Fact]
    public void Every_step_is_in_the_script_once_and_in_enum_order()
    {
        Assert.Equal(AllSteps.ToArray(), TutorialScript.All.Select(i => i.Step).ToArray());
        for (int i = 0; i < TutorialScript.All.Count; i++) Assert.Equal((TutorialStep)i, TutorialScript.Info((TutorialStep)i).Step);
    }

    [Fact]
    public void Chapters_are_in_order_contiguous_and_each_has_at_least_two_steps()
    {
        var chapters = TutorialScript.All.Select(i => i.Chapter).ToList();
        for (int i = 1; i < chapters.Count; i++) Assert.True(chapters[i] >= chapters[i - 1], $"{TutorialScript.All[i].Step} fuera de orden");
        Assert.Equal(Enum.GetValues<TutorialChapter>(), chapters.Distinct().ToArray());
        foreach (var c in TutorialScript.Chapters) Assert.True(TutorialScript.StepsOf(c).Count >= 2, $"{c}: menos de dos pasos");
        Assert.Equal(10, TutorialScript.Chapters.Count);
    }

    [Fact]
    public void Every_step_has_a_goal()
    {
        Assert.All(TutorialScript.All, i => Assert.NotEmpty(i.Goals));
        Assert.All(TutorialScript.All, i => Assert.DoesNotContain(TutorialAct.None, i.Goals));
    }

    [Fact]
    public void Pro_steps_are_what_the_free_edition_does_not_have()
    {
        var pro = TutorialScript.All.Where(i => i.IsPro).Select(i => i.Step).ToHashSet();
        Assert.Contains(TutorialStep.PermissionArm, pro);
        Assert.Contains(TutorialStep.MusicLyrics, pro);
        Assert.Contains(TutorialStep.TasksReminder, pro);
        Assert.Contains(TutorialStep.ShelfDrop, pro);
        Assert.Contains(TutorialStep.UsageHover, pro);
        Assert.Contains(TutorialStep.FocusTask, pro);
        Assert.Contains(TutorialStep.TrayMonitor, pro);
        // Lo gratis no lleva el chip: el notch, las páginas, los números de Uso y el pomodoro básico.
        Assert.DoesNotContain(TutorialStep.NotchIdle, pro);
        Assert.DoesNotContain(TutorialStep.PagesWheel, pro);
        Assert.DoesNotContain(TutorialStep.UsageTile, pro);
        Assert.DoesNotContain(TutorialStep.FocusStart, pro);
        Assert.All(TutorialScript.All.Where(i => i.IsPro), i => Assert.False(ProGate.IsUnlocked(i.Pro!.Value, ProState.Free)));
        Assert.False(TutorialScript.HasPro(TutorialChapter.Notch));
        Assert.False(TutorialScript.HasPro(TutorialChapter.Pages));
        Assert.True(TutorialScript.HasPro(TutorialChapter.Permissions));
    }

    [Fact]
    public void The_tray_chapter_works_on_the_mock_tray_and_the_rest_on_the_notch()
    {
        foreach (var i in TutorialScript.All)
            Assert.Equal(i.Chapter == TutorialChapter.Tray, i.Stage.OnTray);
    }

    // ---------------------------------------------------------------- gestos y teclado

    [Theory]
    [MemberData(nameof(Steps))]
    public void Enter_does_the_gesture_of_every_step(TutorialStep step)
    {
        var m = At(step);
        Assert.False(m.StepDone);
        Assert.Equal(TutorialOutcome.Completed, m.HandleKey(TutorialKey.Enter));
        Assert.True(m.StepDone);
        Assert.Equal(TutorialStepStatus.Done, m.Status(step));
        Assert.True(m.CanAdvance);
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public void The_arrows_only_act_on_steps_with_a_direction(TutorialStep step)
    {
        var m = At(step);
        var info = TutorialScript.Info(step);
        Assert.Equal(info.Arrows, m.KeyActs(TutorialKey.Right));
        Assert.Equal(info.Arrows, m.KeyActs(TutorialKey.Left));
        Assert.Equal(step == TutorialStep.SessionsStates, m.KeyActs(TutorialKey.Down));
        if (info.Arrows) Assert.Equal(TutorialOutcome.Completed, m.HandleKey(TutorialKey.Right));
        else Assert.Equal(TutorialOutcome.Ignored, m.HandleKey(TutorialKey.Right));
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public void Each_goal_gesture_completes_its_step_from_its_stage(TutorialStep step)
    {
        // El gesto que haría el ratón (con el dato que lleva el teclado) completa el paso desde su demostración inicial.
        var m = At(step);
        var input = TutorialScript.KeyInput(step, m.Demo, TutorialKey.Enter)!.Value;
        Assert.Contains(input.Act, TutorialScript.Info(step).Goals);
        Assert.True(TutorialScript.Completes(step, input, m.Demo, m.Demo.Apply(input)));
    }

    [Fact]
    public void A_gesture_that_is_not_the_goal_moves_the_demo_but_does_not_complete()
    {
        var m = At(TutorialStep.MusicLyrics);
        Assert.Equal(TutorialOutcome.Changed, m.Handle(new TutorialInput(TutorialAct.PlayPause)));
        Assert.False(m.StepDone);
        Assert.False(m.Demo.Playing);
        Assert.Equal(TutorialOutcome.Completed, m.Handle(new TutorialInput(TutorialAct.ToggleLyrics)));
        // Ya hecho: seguir tocando mueve la demostración, pero no vuelve a «completar».
        Assert.Equal(TutorialOutcome.Changed, m.Handle(new TutorialInput(TutorialAct.ToggleLyrics)));
        Assert.True(m.StepDone);
    }

    [Fact]
    public void Page_steps_need_the_page_to_actually_change()
    {
        var m = At(TutorialStep.PagesDots);
        Assert.Equal(TutorialOutcome.Ignored, m.Handle(new TutorialInput(TutorialAct.DotPage, 0)));   // ya está en Claude
        Assert.False(m.StepDone);
        Assert.Equal(TutorialOutcome.Completed, m.Handle(new TutorialInput(TutorialAct.DotPage, 3)));

        var arrows = At(TutorialStep.PagesArrows);
        Assert.Equal(TutorialOutcome.Ignored, arrows.Handle(new TutorialInput(TutorialAct.ArrowPage, -1)));   // ‹ en la primera
        Assert.False(arrows.StepDone);
    }

    [Fact]
    public void The_usage_page_step_needs_to_land_on_usage()
    {
        var m = At(TutorialStep.UsagePage);
        Assert.Equal(TutorialOutcome.Changed, m.Handle(new TutorialInput(TutorialAct.WheelPage, -1)));   // a Bandeja (vuelta)
        Assert.False(m.StepDone);
        Assert.Equal(TutorialOutcome.Completed, m.Handle(new TutorialInput(TutorialAct.DotPage, 1)));
        Assert.Equal(NotchPage.Usage, m.Demo.Page);
    }

    [Fact]
    public void Focus_steps_follow_the_button()
    {
        var start = At(TutorialStep.FocusStart);
        Assert.Equal(TutorialOutcome.Completed, start.Handle(new TutorialInput(TutorialAct.FocusPrimary)));
        Assert.Equal(FocusRun.Running, start.Demo.FocusRun);

        var pause = At(TutorialStep.FocusPause);
        Assert.Equal(FocusRun.Running, pause.Demo.FocusRun);
        Assert.Equal(TutorialOutcome.Changed, pause.Handle(new TutorialInput(TutorialAct.FocusSkip)));   // no es pausar
        Assert.Equal(TutorialOutcome.Changed, pause.Handle(new TutorialInput(TutorialAct.FocusPrimary)));   // Ready → corre
        Assert.Equal(TutorialOutcome.Completed, pause.Handle(new TutorialInput(TutorialAct.FocusPrimary)));
        Assert.Equal(FocusRun.Paused, pause.Demo.FocusRun);
    }

    [Fact]
    public void Permission_steps_complete_once_with_their_button()
    {
        var m = At(TutorialStep.PermissionArm);
        Assert.Equal(TutorialOutcome.Changed, m.Handle(new TutorialInput(TutorialAct.PermissionAllow)));   // no es el del paso
        Assert.False(m.StepDone);
        // Ya contestado: el otro botón no hace nada (como el notch de verdad).
        Assert.Equal(TutorialOutcome.Ignored, m.Handle(new TutorialInput(TutorialAct.PermissionDeny)));
        Assert.Equal(TutorialAnswer.Allow, m.Demo.Answer);
        // Sin hacer, «Siguiente» no avanza; volver al paso trae la petición sin contestar.
        Assert.False(m.Next());
        m.GoToStep(0);
        Assert.Equal(TutorialAnswer.None, m.Demo.Answer);
        Assert.Equal(TutorialOutcome.Completed, m.Handle(new TutorialInput(TutorialAct.PermissionDeny)));
    }

    [Fact]
    public void The_none_gesture_is_ignored()
    {
        var m = new TutorialMachine();
        Assert.Equal(TutorialOutcome.Ignored, m.Handle(TutorialInput.None));
    }

    // ---------------------------------------------------------------- navegación

    [Fact]
    public void Starts_at_the_first_step_with_its_stage()
    {
        var m = new TutorialMachine();
        Assert.Equal(TutorialPhase.Step, m.Phase);
        Assert.Equal(TutorialChapter.Notch, m.Chapter);
        Assert.Equal(TutorialStep.NotchIdle, m.Step);
        Assert.Equal(TutorialScript.Info(TutorialStep.NotchIdle).Stage, m.Demo);
        Assert.Equal(1, m.ChapterNumber);
        Assert.Equal(1, m.StepNumber);
        Assert.Equal(TutorialScript.StepsOf(TutorialChapter.Notch).Count, m.StepCount);
        Assert.False(m.CanGoBack);
        Assert.False(m.CanAdvance);
    }

    [Fact]
    public void Next_waits_for_the_step_and_skip_moves_on()
    {
        var m = new TutorialMachine();
        Assert.False(m.Next());
        Assert.Equal(TutorialStep.NotchIdle, m.Step);
        Assert.True(m.Skip());
        Assert.Equal(TutorialStep.NotchClose, m.Step);
        Assert.Equal(TutorialStepStatus.Skipped, m.Status(TutorialStep.NotchIdle));
        m.HandleKey(TutorialKey.Enter);
        Assert.True(m.Next());
        Assert.Equal(TutorialStep.NotchWorking, m.Step);
        Assert.Equal(TutorialStepStatus.Done, m.Status(TutorialStep.NotchClose));
    }

    [Fact]
    public void Skipping_a_done_step_keeps_it_done()
    {
        var m = new TutorialMachine();
        m.HandleKey(TutorialKey.Enter);
        m.Skip();
        Assert.Equal(TutorialStepStatus.Done, m.Status(TutorialStep.NotchIdle));
    }

    [Fact]
    public void After_the_last_step_comes_the_chapter_end_and_it_is_saved_once()
    {
        var m = new TutorialMachine();
        int saved = 0;
        m.ProgressChanged += () => saved++;
        foreach (var _ in TutorialScript.StepsOf(TutorialChapter.Notch)) m.Skip();
        Assert.Equal(TutorialPhase.ChapterEnd, m.Phase);
        Assert.Equal(TutorialChapter.Notch, m.Chapter);
        Assert.True(m.IsChapterDone(TutorialChapter.Notch));
        Assert.Equal(1, saved);
        Assert.True(m.CanAdvance);
        // Volver al capítulo y terminarlo otra vez no lo guarda dos veces.
        m.Back();
        m.Next();
        Assert.Equal(TutorialPhase.ChapterEnd, m.Phase);
        Assert.Equal(1, saved);
        Assert.True(m.Next());
        Assert.Equal(TutorialChapter.Sessions, m.Chapter);
        Assert.Equal(0, m.StepIndex);
        Assert.Equal(TutorialPhase.Step, m.Phase);
    }

    [Fact]
    public void The_last_chapter_end_leads_to_the_finish()
    {
        var m = new TutorialMachine();
        m.GoTo(TutorialChapter.Tray);
        while (m.Phase == TutorialPhase.Step) m.Skip();
        Assert.Equal(TutorialPhase.ChapterEnd, m.Phase);
        Assert.True(m.Next());
        Assert.Equal(TutorialPhase.Finished, m.Phase);
        Assert.False(m.Next());
        Assert.False(m.Skip());
        Assert.True(m.CanGoBack);
        Assert.True(m.Back());
        Assert.Equal(TutorialPhase.ChapterEnd, m.Phase);
        Assert.True(m.Back());
        Assert.Equal(TutorialStep.TrayTutorial, m.Step);
    }

    [Fact]
    public void Back_crosses_chapters_and_resets_the_stage_but_keeps_it_done()
    {
        var m = new TutorialMachine();
        Assert.False(m.Back());
        m.GoTo(TutorialChapter.Sessions);
        Assert.True(m.Back());
        Assert.Equal(TutorialChapter.Notch, m.Chapter);
        Assert.Equal(TutorialStep.NotchClock, m.Step);
        m.HandleKey(TutorialKey.Enter);
        Assert.NotEqual(TutorialScript.Info(TutorialStep.NotchClock).Stage, m.Demo);
        m.Back();
        m.Next();   // NotchMinimal estaba sin hacer: no avanza
        Assert.Equal(TutorialStep.NotchMinimal, m.Step);
        m.Skip();
        Assert.Equal(TutorialStep.NotchClock, m.Step);
        Assert.Equal(TutorialScript.Info(TutorialStep.NotchClock).Stage, m.Demo);
        Assert.True(m.StepDone);
    }

    [Fact]
    public void GoTo_and_GoToStep_start_with_the_stage()
    {
        var m = new TutorialMachine();
        m.GoTo(TutorialChapter.Usage);
        Assert.Equal(TutorialStep.UsagePage, m.Step);
        m.GoToStep(3);
        Assert.Equal(TutorialStep.UsageHover, m.Step);
        Assert.Equal(TutorialScript.Info(TutorialStep.UsageHover).Stage, m.Demo);
        m.GoToStep(99);
        Assert.Equal(TutorialStep.UsageHover, m.Step);
        m.GoTo((TutorialChapter)42);
        Assert.Equal(TutorialChapter.Usage, m.Chapter);
        m.Restart();
        Assert.Equal(TutorialStep.NotchIdle, m.Step);
    }

    [Fact]
    public void Resumes_at_the_first_chapter_not_done()
    {
        var m = new TutorialMachine(new[] { TutorialChapter.Notch, TutorialChapter.Sessions, TutorialChapter.Pages });
        Assert.Equal(TutorialChapter.Permissions, m.Chapter);
        Assert.Equal(0, m.StepIndex);
        Assert.Equal(3, m.ChaptersDone.Count);

        var all = new TutorialMachine(TutorialScript.Chapters);
        Assert.Equal(TutorialChapter.Notch, all.Chapter);
        Assert.Equal(10, all.ChaptersDone.Count);
    }

    // ---------------------------------------------------------------- lo que se guarda

    [Fact]
    public void Parse_drops_unknown_names_and_keeps_order()
    {
        var parsed = TutorialMachine.Parse(new[] { "Usage", "Notch", "Nope", "Usage", "", "3", "Notch,Sessions", "notch", null! });
        Assert.Equal(new[] { TutorialChapter.Notch, TutorialChapter.Usage }, parsed);
        Assert.Empty(TutorialMachine.Parse(null));
        Assert.Equal(new[] { "Notch", "Usage", "Tray" },
            TutorialMachine.Format(new[] { TutorialChapter.Tray, TutorialChapter.Notch, TutorialChapter.Usage, TutorialChapter.Notch }));
        Assert.Empty(TutorialMachine.Format(new[] { (TutorialChapter)99 }));
    }

    [Fact]
    public void Config_normalizes_the_saved_chapters()
    {
        var cfg = new Config.AppConfig { TutorialChaptersDone = new[] { "Tray", "bogus", "Notch", "Tray" } }.Normalize();
        Assert.Equal(new[] { "Notch", "Tray" }, cfg.TutorialChaptersDone);
        Assert.Empty(new Config.AppConfig { TutorialChaptersDone = null! }.Normalize().TutorialChaptersDone);
    }
}
