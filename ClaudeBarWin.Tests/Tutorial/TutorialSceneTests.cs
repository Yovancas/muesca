using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.UI.Notch;
using ClaudeBarWin.UI.Onboarding;
using ClaudeBarWin.UI.Tutorial;

namespace ClaudeBarWin.Tests.Tutorial;

/// <summary>
/// El tutorial en movimiento sin ventana (<see cref="TutorialScene"/>): los gestos de verdad sobre el notch de demostración
/// (pasar el ratón, los botones que se arman, la rueda, arrastrar el archivo, la bandeja de mentira), el teclado, el «hecho»,
/// reducir movimiento, el cierre de capítulo con el enlace del Pro y el 0 % de CPU quieto.
/// </summary>
public sealed class TutorialSceneTests : IDisposable
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);
    private readonly OnboardingPainter _painter = new(Localization.Get("es"));
    private TimeSpan _now;

    public void Dispose() => _painter.Dispose();

    private TutorialScene Scene(TutorialChapter? chapter = null, int step = 0, bool reduce = false, ProState pro = ProState.Free,
        IEnumerable<TutorialChapter>? done = null)
    {
        var machine = new TutorialMachine(done);
        if (chapter is { } c) machine.GoTo(c);
        machine.GoToStep(step);
        var scene = new TutorialScene(machine, Localization.Get("es"), _painter, pro, reduce,
            () => new DateTime(2026, 10, 30, 18, 55, 0), () => new DateTime(2026, 10, 30, 17, 20, 0, DateTimeKind.Utc));
        scene.Place(1f, OnboardingPlacement.Surface(1f, TutorialGeometry.Width, TutorialGeometry.Height));
        _now = TimeSpan.Zero;
        scene.Start(_now);
        Run(scene, TimeSpan.FromSeconds(3), s => s.Choreography.Stage == OnboardingStage.Open && !s.IsAnimating);
        Assert.Equal(OnboardingStage.Open, scene.Choreography.Stage);
        return scene;
    }

    private void Run(TutorialScene scene, TimeSpan max, Func<TutorialScene, bool>? until = null)
    {
        var end = _now + max;
        while (_now < end && !(until?.Invoke(scene) ?? false))
        {
            _now += Frame;
            scene.Step(_now);
        }
    }

    private static Point Center(RectangleF r) => new((int)(r.Left + r.Width / 2f), (int)(r.Top + r.Height / 2f));

    private static RectangleF Stage(TutorialScene scene) => TutorialGeometry.Stage(
        UI.Onboarding.OnboardingGeometry.PillRect(TutorialGeometry.Width, TutorialGeometry.Height, scene.Pill.Left + scene.Pill.Width / 2f),
        scene.Scale);

    /// <summary>La pastilla del notch de demostración, en coordenadas de la superficie del tutorial.</summary>
    private static RectangleF DemoPill(TutorialScene scene)
    {
        var f = scene.Frame().Notch!;
        var stage = Stage(scene);
        var pill = NotchGeometry.PillRect(f.Width, f.Height, TutorialGeometry.StageSurface(stage));
        pill.Offset(MathF.Round(stage.Left), MathF.Round(stage.Top));
        return pill;
    }

    private void Click(TutorialScene scene, Point p, bool right = false)
    {
        scene.PointerDown(p, _now, right);
        scene.PointerUp(p, _now, right);
    }

    // ---------------------------------------------------------------- reposo y teclado

    [Fact]
    public void At_rest_nothing_moves_and_nothing_wakes()
    {
        var scene = Scene();
        Assert.False(scene.IsAnimating);
        Assert.Null(scene.NextWakeIn(_now));
        Assert.NotNull(scene.Frame().Notch);
        Assert.Equal(NotchMode.Idle, scene.Frame().Notch!.Mode);
    }

    [Fact]
    public void Enter_does_the_step_with_the_done_pop_and_then_moves_on()
    {
        var scene = Scene();
        Assert.True(scene.Key(Keys.Enter, false, _now));
        Assert.True(scene.View.Done);
        Assert.True(scene.Frame().DonePop < 1f, "el visto entra pequeño y crece con el muelle");
        Run(scene, TimeSpan.FromSeconds(1));
        Assert.Equal(1f, scene.Frame().DonePop, 2);
        Assert.Equal(1f, scene.Frame().DoneAlpha, 2);
        Assert.Equal(NotchMode.Expanded, scene.Frame().Notch!.Mode);

        Assert.True(scene.Key(Keys.Enter, false, _now));
        Assert.Equal(TutorialStep.NotchClose, scene.Machine.Step);
        Assert.NotNull(scene.Frame().Previous);   // cruce del bloque del paso
        Assert.False(scene.View.Done);
    }

    [Fact]
    public void Holding_enter_does_not_rush_through_the_steps()
    {
        var scene = Scene();
        scene.Key(Keys.Enter, false, _now);
        for (int i = 0; i < 10; i++) scene.Key(Keys.Enter, false, _now, repeat: true);
        Assert.Equal(TutorialStep.NotchIdle, scene.Machine.Step);
    }

    [Fact]
    public void Reduce_motion_jumps_instead_of_animating()
    {
        var scene = Scene(reduce: true);
        scene.Key(Keys.Enter, false, _now);
        scene.Step(_now + Frame);
        Assert.Equal(1f, scene.Frame().DonePop);
        Assert.Equal(1f, scene.Frame().DoneAlpha);
        scene.Key(Keys.Enter, false, _now);
        scene.Step(_now + Frame);
        Assert.Null(scene.Frame().Previous);
    }

    [Fact]
    public void Tab_walks_the_controls_and_enter_presses_the_focused_one()
    {
        var scene = Scene(TutorialChapter.Sessions, 1);
        scene.Key(Keys.Tab, false, _now);
        Assert.True(scene.Frame().ShowFocus);
        Assert.Equal(new TutorialHit(TutorialControl.Notch), scene.Frame().Focus);
        scene.Key(Keys.Tab, true, _now);
        Assert.Equal(new TutorialHit(TutorialControl.Exit), scene.Frame().Focus);
        scene.Key(Keys.Tab, true, _now);
        scene.Key(Keys.Tab, true, _now);
        scene.Key(Keys.Tab, true, _now);
        Assert.Equal(new TutorialHit(TutorialControl.Back), scene.Frame().Focus);
        scene.Key(Keys.Enter, false, _now);
        Assert.Equal(TutorialStep.SessionsList, scene.Machine.Step);
    }

    [Fact]
    public void Arrows_do_the_gesture_on_steps_with_a_direction_and_move_between_steps_otherwise()
    {
        var pages = Scene(TutorialChapter.Pages);
        pages.Key(Keys.Right, false, _now);
        Assert.True(pages.View.Done);
        Assert.Equal(NotchPage.Usage, pages.Machine.Demo.Page);
        pages.Key(Keys.Right, false, _now);   // hecho: → pasa al siguiente
        Assert.Equal(TutorialStep.PagesDots, pages.Machine.Step);
        pages.Key(Keys.Left, false, _now);    // sin hacer y con dirección: hace el gesto…
        Assert.True(pages.View.Done);
        pages.Key(Keys.Left, false, _now);    // …y hecho, ← vuelve
        Assert.Equal(TutorialStep.PagesWheel, pages.Machine.Step);
    }

    // ---------------------------------------------------------------- gestos de verdad

    [Fact]
    public void Resting_the_pointer_on_the_demo_notch_opens_it()
    {
        var scene = Scene();
        var notch = DemoPill(scene);
        var p = new Point((int)(notch.Left + notch.Width / 2f), (int)(notch.Top + notch.Height / 2f));
        Assert.True(scene.PointerMove(p, _now));
        Assert.True(scene.OverControl);
        Assert.Equal(TutorialPointer.Peek, scene.Machine.Demo.Pointer);
        Assert.NotNull(scene.NextWakeIn(_now));   // el plazo del hover
        Run(scene, NotchTiming.HoverExpand + TimeSpan.FromMilliseconds(50));
        Assert.Equal(TutorialPointer.Expanded, scene.Machine.Demo.Pointer);
        Assert.True(scene.View.Done);

        // Siguiente paso (cerrarlo): sacar el ratón y esperar la gracia.
        scene.Key(Keys.Enter, false, _now);
        Run(scene, TimeSpan.FromSeconds(1));
        var open = DemoPill(scene);
        scene.PointerMove(Center(open), _now);
        scene.PointerMove(new Point(5, 5), _now);
        Run(scene, NotchTiming.CollapseGrace + TimeSpan.FromMilliseconds(50));
        Assert.Equal(TutorialPointer.Away, scene.Machine.Demo.Pointer);
        Assert.True(scene.View.Done);
    }

    [Fact]
    public void Permission_buttons_only_answer_once_armed()
    {
        var scene = Scene(TutorialChapter.Permissions);
        var pill = DemoPill(scene);
        var (deny, _) = NotchGeometry.PermissionButtons(new RectangleF(0, 0, pill.Width, pill.Height), scene.Scale);
        var p = new Point((int)(pill.Left + deny.Left + deny.Width / 2f), (int)(pill.Top + deny.Top + deny.Height / 2f));
        scene.PointerMove(p, _now);
        Assert.Equal(1f, scene.Frame().Notch!.PermissionArm);   // ya se armó mientras se abría la escena…
        // …así que se vuelve a entrar en el paso para pillarlo sin armar.
        scene.Activate(new TutorialHit(TutorialControl.StepDot, 1), _now);
        scene.Activate(new TutorialHit(TutorialControl.StepDot, 0), _now);
        scene.Step(_now += Frame);
        Click(scene, p);
        Assert.False(scene.View.Done);
        Assert.Equal(TutorialAnswer.None, scene.Machine.Demo.Answer);
        Run(scene, TimeSpan.FromSeconds(1.5));
        Assert.Equal(1f, scene.Frame().Notch!.PermissionArm);
        Click(scene, p);
        Assert.True(scene.View.Done);
        Assert.Equal(TutorialAnswer.Deny, scene.Machine.Demo.Answer);
        Assert.Equal(Services.State.PermissionVerdict.Deny, scene.Frame().Notch!.PermissionAnswer);
    }

    [Fact]
    public void The_wheel_on_the_open_notch_turns_the_page()
    {
        var scene = Scene(TutorialChapter.Pages);
        var pill = DemoPill(scene);
        var p = new Point((int)(pill.Left + 40), (int)(pill.Top + 30));   // la columna de la cuota, no la lista
        scene.PointerMove(p, _now);
        Assert.True(scene.Wheel(-120, p, _now));
        Assert.Equal(NotchPage.Usage, scene.Machine.Demo.Page);
        Assert.True(scene.View.Done);
        Assert.False(scene.Wheel(-120, new Point(-100, -100), _now));
    }

    [Fact]
    public void Dragging_the_sample_file_onto_the_notch_drops_it_on_the_shelf()
    {
        var scene = Scene(TutorialChapter.Shelf);
        Assert.True(scene.Frame().Stage!.File);
        var file = Center(TutorialGeometry.SampleFile(Stage(scene), scene.Scale));
        scene.PointerDown(file, _now);
        var notch = DemoPill(scene);
        var over = new Point((int)(notch.Left + notch.Width / 2f), (int)(notch.Top + notch.Height / 2f));
        scene.PointerMove(over, _now);
        Assert.NotNull(scene.Frame().Stage!.Drag);
        Assert.Equal(NotchPage.Shelf, scene.Machine.Demo.Page);   // encima, el notch se abre ya en la Bandeja
        scene.PointerUp(over, _now);
        Assert.True(scene.View.Done);
        Assert.True(scene.Machine.Demo.ShelfFile);
        Assert.False(scene.Frame().Stage!.File);
    }

    [Fact]
    public void The_mock_tray_answers_left_and_right_clicks()
    {
        var scene = Scene(TutorialChapter.Tray);
        var icon = Center(TutorialGeometry.TrayIcon(Stage(scene), scene.Scale));
        Click(scene, icon);
        Assert.Equal(TutorialTray.Panel, scene.Machine.Demo.Tray);
        Assert.True(scene.View.Done);
        scene.Key(Keys.Enter, false, _now);
        Click(scene, icon, right: true);
        Assert.Equal(TutorialTray.Menu, scene.Machine.Demo.Tray);
        Assert.True(scene.View.Done);
        scene.Key(Keys.Enter, false, _now);
        scene.Key(Keys.Enter, false, _now);   // Monitores: Intro hace el gesto
        scene.Key(Keys.Enter, false, _now);
        Assert.Equal(TutorialStep.TraySettings, scene.Machine.Step);
        var settings = Center(TutorialGeometry.MenuItemRect(Stage(scene), scene.Scale, TutorialMenuItem.Settings));
        scene.PointerMove(settings, _now);
        Assert.Equal((int)TutorialMenuItem.Settings, scene.Frame().Stage!.MenuHover);
        Click(scene, settings);
        Assert.Equal(TutorialTray.Settings, scene.Machine.Demo.Tray);
        Assert.True(scene.View.Done);
    }

    // ---------------------------------------------------------------- capítulos, Pro y salir

    [Fact]
    public void Finishing_a_chapter_reports_progress_and_offers_pro_without_it()
    {
        var scene = Scene(TutorialChapter.Permissions);
        int saved = 0, pro = 0;
        scene.ProgressChanged += () => saved++;
        scene.ProRequested += () => pro++;
        foreach (var _ in TutorialScript.StepsOf(TutorialChapter.Permissions))
        {
            scene.Key(Keys.Enter, false, _now);
            scene.Key(Keys.Enter, false, _now);
        }
        Assert.Equal(TutorialPhase.ChapterEnd, scene.Machine.Phase);
        Assert.Equal(1, saved);
        Assert.NotNull(scene.View.ProLink);
        scene.Activate(new TutorialHit(TutorialControl.ProLink), _now);
        Assert.Equal(1, pro);
    }

    [Fact]
    public void With_pro_the_chapter_end_has_no_link()
    {
        var scene = Scene(TutorialChapter.Permissions, pro: ProState.Pro);
        foreach (var _ in TutorialScript.StepsOf(TutorialChapter.Permissions)) scene.Activate(new TutorialHit(TutorialControl.Skip), _now);
        Assert.Equal(TutorialPhase.ChapterEnd, scene.Machine.Phase);
        Assert.Null(scene.View.ProLink);
    }

    [Fact]
    public void Escape_leaves_and_the_scene_goes_away()
    {
        var scene = Scene();
        Assert.True(scene.Key(Keys.Escape, false, _now));
        Run(scene, TimeSpan.FromSeconds(3), s => s.IsGone);
        Assert.True(scene.IsGone);
    }

    [Fact]
    public void Resumes_at_the_first_chapter_not_done()
    {
        var scene = Scene(done: new[] { TutorialChapter.Notch, TutorialChapter.Sessions });
        Assert.Equal(TutorialChapter.Permissions, scene.Machine.Chapter);
        Assert.True(scene.View.Chapters[0].Done);
        Assert.True(scene.View.Chapters[2].Current);
    }

    [Fact]
    public void Every_step_paints_without_throwing_in_every_language()
    {
        foreach (var (code, _) in Localization.Languages)
        {
            using var painter = new OnboardingPainter(Localization.Get(code));
            var machine = new TutorialMachine();
            var scene = new TutorialScene(machine, Localization.Get(code), painter, ProState.Free, reduceMotion: false,
                () => new DateTime(2026, 10, 30, 18, 55, 0));
            var surface = OnboardingPlacement.Surface(1f, TutorialGeometry.Width, TutorialGeometry.Height);
            scene.Place(1f, surface);
            var now = TimeSpan.Zero;
            scene.Start(now);
            using var bmp = new Bitmap(surface.Width, surface.Height);
            using var g = Graphics.FromImage(bmp);
            void Paint()
            {
                for (int i = 0; i < 60; i++) scene.Step(now += Frame);
                var f = scene.Frame();
                painter.PaintTutorialShadow(g, surface, f);
                painter.PaintTutorial(g, surface, f);
            }
            while (machine.Phase != TutorialPhase.Finished)
            {
                Paint();
                scene.Key(Keys.Enter, false, now);
                Paint();
                scene.Key(Keys.Enter, false, now);
            }
            Paint();
        }
    }

    // ---------------------------------------------------------------- el reloj de la demo

    [Fact]
    public void The_demo_counts_resets_with_its_own_clock()
    {
        // El reloj de los ejemplos está en el pasado (como en las capturas): con el de la máquina, la de 5 h ya habría
        // reiniciado y el notch diría «resetea en reseteando…».
        var scene = Scene();
        var frame = scene.Frame().Notch!;
        var clock = new DateTimeOffset(2026, 10, 30, 17, 20, 0, TimeSpan.Zero);
        Assert.Equal(clock, frame.Clock);
        var es = Localization.Get("es");
        Assert.Equal("1h 40m", UsageFormat.CountdownAt(frame.Quota!.FiveHour!.ResetsAt, es.Resetting, frame.Clock!.Value));
        Assert.Equal("3d 0h", UsageFormat.CountdownAt(frame.Quota.SevenDay!.ResetsAt, es.Resetting, frame.Clock.Value));
    }

    [Fact]
    public void The_samples_reset_after_their_clock_whatever_the_machine_says()
    {
        var utc = new DateTime(2020, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var samples = new TutorialSamples(Localization.Get("en"), utc, new DateTime(2020, 1, 1, 9, 0, 0));
        Assert.Equal(new DateTimeOffset(utc), samples.Clock);
        Assert.Equal(TimeSpan.FromMinutes(100), samples.Quota.FiveHour!.ResetsAt!.Value - samples.Clock);
        Assert.True(samples.Quota.SevenDay!.ResetsAt > samples.Clock);
        Assert.True(samples.HighQuota.FiveHour!.ResetsAt > samples.Clock);
    }

    // ---------------------------------------------------------------- el paso 1 empieza en reposo

    [Fact]
    public void The_first_step_starts_with_the_demo_notch_at_rest()
    {
        var scene = Scene();
        Assert.Equal(TutorialStep.NotchIdle, scene.Machine.Step);
        Assert.Equal(TutorialPointer.Away, scene.Machine.Demo.Pointer);
        Run(scene, TimeSpan.FromSeconds(2));
        Assert.Equal(NotchMode.Idle, scene.Frame().Notch!.Mode);
        Assert.False(scene.View.Done);
    }

    [Fact]
    public void A_pointer_resting_where_the_notch_unfolds_does_not_open_it()
    {
        // El cursor estaba donde aparece el notch de demostración mientras la ventana se desplegaba: no cuenta como pasar el
        // ratón. Al salir y volver a entrar, sí.
        var machine = new TutorialMachine();
        var scene = new TutorialScene(machine, Localization.Get("es"), _painter, ProState.Free, false,
            () => new DateTime(2026, 10, 30, 18, 55, 0), () => new DateTime(2026, 10, 30, 17, 20, 0, DateTimeKind.Utc));
        scene.Place(1f, OnboardingPlacement.Surface(1f, TutorialGeometry.Width, TutorialGeometry.Height));
        _now = TimeSpan.Zero;
        scene.Start(_now);
        Run(scene, TimeSpan.FromSeconds(3), s => s.Frame().Notch is not null);
        Assert.NotNull(scene.Frame().Notch);
        scene.PointerMove(Center(DemoPill(scene)), _now);
        Run(scene, TimeSpan.FromSeconds(3), s => s.Choreography.Stage == OnboardingStage.Open && !s.IsAnimating);
        scene.PointerMove(Center(DemoPill(scene)), _now);   // ya desplegado, el cursor sigue quieto encima
        Run(scene, NotchTiming.HoverExpand + TimeSpan.FromMilliseconds(300));
        Assert.Equal(TutorialPointer.Away, machine.Demo.Pointer);
        Assert.Equal(NotchMode.Idle, scene.Frame().Notch!.Mode);
        Assert.False(scene.View.Done);

        scene.PointerMove(new Point(5, 5), _now);
        scene.PointerMove(Center(DemoPill(scene)), _now);
        Assert.Equal(TutorialPointer.Peek, machine.Demo.Pointer);
        Run(scene, NotchTiming.HoverExpand + TimeSpan.FromMilliseconds(50));
        Assert.Equal(TutorialPointer.Expanded, machine.Demo.Pointer);
        Assert.True(scene.View.Done);
    }

    [Fact]
    public void Coming_back_to_the_first_step_with_the_pointer_on_the_notch_starts_it_at_rest()
    {
        var scene = Scene();
        var p = Center(DemoPill(scene));
        scene.PointerMove(p, _now);
        Run(scene, NotchTiming.HoverExpand + TimeSpan.FromMilliseconds(50));
        Assert.True(scene.View.Done);
        scene.Key(Keys.Enter, false, _now);                                   // al paso 2 (cerrarlo)
        Run(scene, TimeSpan.FromSeconds(1));
        Assert.Equal(TutorialStep.NotchClose, scene.Machine.Step);
        scene.Activate(new TutorialHit(TutorialControl.Back), _now);         // y otra vez al 1, con el ratón encima
        Assert.Equal(TutorialStep.NotchIdle, scene.Machine.Step);
        scene.PointerMove(new Point(p.X + 1, p.Y), _now);
        Run(scene, NotchTiming.HoverExpand + TimeSpan.FromMilliseconds(300));
        Assert.Equal(TutorialPointer.Away, scene.Machine.Demo.Pointer);
        Run(scene, TimeSpan.FromSeconds(1));
        Assert.Equal(NotchMode.Idle, scene.Frame().Notch!.Mode);

        // Salir y volver a pasar el ratón: ahora sí se abre (el gesto del paso).
        scene.PointerMove(new Point(5, 5), _now);
        scene.PointerMove(Center(DemoPill(scene)), _now);
        Run(scene, NotchTiming.HoverExpand + TimeSpan.FromMilliseconds(50));
        Assert.Equal(TutorialPointer.Expanded, scene.Machine.Demo.Pointer);
    }

    [Fact]
    public void A_notch_that_shrinks_away_from_a_resting_pointer_opens_on_the_next_real_hover()
    {
        // El cursor se queda en un lado del notch abierto (fuera de donde va el de reposo). Al volver al paso 1, el notch
        // encoge y deja el cursor fuera sin que nunca contara como dentro: el primer movimiento fuera tiene que rearmarlo, y
        // entrar después lo abre (antes se quedaba sin abrir hasta salir y entrar dos veces).
        var scene = Scene();
        scene.PointerMove(Center(DemoPill(scene)), _now);
        Run(scene, NotchTiming.HoverExpand + TimeSpan.FromMilliseconds(50));
        Run(scene, TimeSpan.FromSeconds(1));
        var open = DemoPill(scene);
        // A la izquierda y a media altura del abierto: lejos de sus esquinas redondeadas y del notch de reposo, centrado arriba.
        var corner = new Point((int)(open.Left + open.Width * 0.15f), (int)(open.Top + open.Height * 0.5f));
        scene.PointerMove(corner, _now);
        scene.Key(Keys.Enter, false, _now);                              // al paso 2
        Run(scene, TimeSpan.FromSeconds(1));
        scene.Activate(new TutorialHit(TutorialControl.Back), _now);    // y otra vez al 1, con el cursor ahí
        Run(scene, TimeSpan.FromSeconds(1));
        Assert.Equal(TutorialStep.NotchIdle, scene.Machine.Step);
        var idle = DemoPill(scene);
        Assert.False(idle.Contains(corner), "ese punto del abierto queda fuera del notch en reposo");

        scene.PointerMove(new Point(corner.X + 1, corner.Y), _now);     // primer movimiento: ya fuera
        scene.PointerMove(Center(idle), _now);                           // y entra
        Assert.Equal(TutorialPointer.Peek, scene.Machine.Demo.Pointer);
        Run(scene, NotchTiming.HoverExpand + TimeSpan.FromMilliseconds(50));
        Assert.Equal(TutorialPointer.Expanded, scene.Machine.Demo.Pointer);
    }

    [Fact]
    public void Leaving_the_window_rearms_the_hover()
    {
        var scene = Scene();
        var p = Center(DemoPill(scene));
        scene.PointerMove(p, _now);
        Run(scene, NotchTiming.HoverExpand + TimeSpan.FromMilliseconds(50));
        scene.Key(Keys.Enter, false, _now);
        Run(scene, TimeSpan.FromSeconds(1));
        scene.Activate(new TutorialHit(TutorialControl.Back), _now);    // el paso 1 empieza con el cursor encima: desarmado
        scene.PointerLeave(_now);                                         // sale de la ventana
        Run(scene, TimeSpan.FromSeconds(1));
        scene.PointerMove(Center(DemoPill(scene)), _now);                // vuelve directamente sobre el notch
        Assert.Equal(TutorialPointer.Peek, scene.Machine.Demo.Pointer);
    }
}
