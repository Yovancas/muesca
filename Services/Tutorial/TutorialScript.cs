using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Tutorial;

/// <summary>Capítulos del tutorial, en orden. Los nombres se guardan en la config (<c>TutorialChaptersDone</c>): no se renombran.</summary>
public enum TutorialChapter { Notch, Sessions, Permissions, Pages, Music, Tasks, Shelf, Usage, Focus, Tray }

/// <summary>Pasos del tutorial, en orden (los textos van en este orden: <c>TutorialStrings.StepTitles</c>).</summary>
public enum TutorialStep
{
    NotchIdle, NotchClose, NotchWorking, NotchDone, NotchQuota, NotchMinimal, NotchClock,
    SessionsList, SessionsStates, SessionsTerminal,
    PermissionArm, PermissionAllow,
    PagesWheel, PagesDots, PagesArrows,
    MusicPlay, MusicNext, MusicLyrics, MusicCompact,
    TasksCheck, TasksAdd, TasksReminder,
    ShelfDrop, ShelfOpen,
    UsagePage, UsageMode, UsageTile, UsageHover,
    FocusStart, FocusPause, FocusSkip, FocusMini, FocusTask,
    TrayPanel, TrayMenu, TrayMonitor, TraySettings, TrayTutorial,
}

/// <summary>Teclas que hacen el gesto de un paso sin ratón (Intro siempre; las flechas, en los pasos con dirección).</summary>
public enum TutorialKey { Enter, Left, Right, Up, Down }

/// <summary>
/// Un paso: su capítulo, el notch de demostración con que empieza (<see cref="Stage"/>), los gestos que lo completan, la
/// función del Pro que enseña (<c>null</c> = gratis) y si las flechas hacen su gesto (<see cref="Arrows"/>: si no, ← → mueven
/// entre pasos).
/// </summary>
public sealed record TutorialStepInfo(TutorialStep Step, TutorialChapter Chapter, TutorialDemo Stage,
    IReadOnlyList<TutorialAct> Goals, ProFeature? Pro = null, bool Arrows = false)
{
    /// <summary>¿Lleva el chip «Pro»? (lo que la edición gratis no tiene, según <see cref="ProGate"/>).</summary>
    public bool IsPro => Pro is { } f && !ProGate.IsUnlocked(f, ProState.Free);
}

/// <summary>
/// El guion del tutorial: capítulos, pasos, el notch de demostración de cada uno, qué gesto lo completa y su alternativa de
/// teclado. PURO (sin WinForms): lo usan la máquina (<see cref="TutorialMachine"/>), la ventana y los tests.
/// </summary>
public static class TutorialScript
{
    public static IReadOnlyList<TutorialChapter> Chapters { get; } = Enum.GetValues<TutorialChapter>();

    private static readonly TutorialDemo Open = new(Pointer: TutorialPointer.Expanded);

    private static readonly TutorialStepInfo[] Steps =
    {
        // 1. El notch: sus caras en reposo; el ratón encima lo abre.
        S(TutorialStep.NotchIdle, TutorialChapter.Notch, new(), TutorialAct.ExpandNotch),
        S(TutorialStep.NotchClose, TutorialChapter.Notch, Open, TutorialAct.LeaveNotch),
        S(TutorialStep.NotchWorking, TutorialChapter.Notch, new(Face: TutorialFace.Working), TutorialAct.ExpandNotch),
        S(TutorialStep.NotchDone, TutorialChapter.Notch, new(Face: TutorialFace.Done), TutorialAct.ExpandNotch),
        S(TutorialStep.NotchQuota, TutorialChapter.Notch, new(Face: TutorialFace.QuotaHigh), TutorialAct.ExpandNotch),
        S(TutorialStep.NotchMinimal, TutorialChapter.Notch, new(Face: TutorialFace.Minimal), TutorialAct.ExpandNotch),
        S(TutorialStep.NotchClock, TutorialChapter.Notch, new(Face: TutorialFace.Clock), TutorialAct.ExpandNotch),

        // 2. Sesiones: la lista, sus estados y el clic que trae la terminal.
        S(TutorialStep.SessionsList, TutorialChapter.Sessions, new(Face: TutorialFace.Working), TutorialAct.ExpandNotch),
        S(TutorialStep.SessionsStates, TutorialChapter.Sessions, Open with { Face = TutorialFace.Working }, TutorialAct.WheelSessions,
            arrows: true),
        S(TutorialStep.SessionsTerminal, TutorialChapter.Sessions, new(Face: TutorialFace.Permission), TutorialAct.ClickPermissionText,
            ProFeature.Permissions),

        // 3. Permisos (simulados): los botones se arman; Denegar y Permitir.
        S(TutorialStep.PermissionArm, TutorialChapter.Permissions, new(Face: TutorialFace.Permission), TutorialAct.PermissionDeny,
            ProFeature.Permissions),
        S(TutorialStep.PermissionAllow, TutorialChapter.Permissions, new(Face: TutorialFace.Permission), TutorialAct.PermissionAllow,
            ProFeature.Permissions),

        // 4. Páginas: la rueda, los puntos y las flechas.
        S(TutorialStep.PagesWheel, TutorialChapter.Pages, Open, TutorialAct.WheelPage, arrows: true),
        S(TutorialStep.PagesDots, TutorialChapter.Pages, Open, TutorialAct.DotPage, arrows: true),
        S(TutorialStep.PagesArrows, TutorialChapter.Pages, Open, TutorialAct.ArrowPage, arrows: true),

        // 5. Música y letras.
        S(TutorialStep.MusicPlay, TutorialChapter.Music, Open with { Page = NotchPage.Music }, TutorialAct.PlayPause, ProFeature.Music),
        S(TutorialStep.MusicNext, TutorialChapter.Music, Open with { Page = NotchPage.Music },
            new[] { TutorialAct.NextTrack, TutorialAct.PreviousTrack }, ProFeature.Music, arrows: true),
        S(TutorialStep.MusicLyrics, TutorialChapter.Music, Open with { Page = NotchPage.Music }, TutorialAct.ToggleLyrics,
            ProFeature.Music),
        S(TutorialStep.MusicCompact, TutorialChapter.Music, new(Face: TutorialFace.CompactLyrics), TutorialAct.HoverNotch,
            ProFeature.Music),

        // 6. Tareas y recordatorios.
        S(TutorialStep.TasksCheck, TutorialChapter.Tasks, Open with { Page = NotchPage.Tasks }, TutorialAct.CheckTask, ProFeature.Tasks),
        S(TutorialStep.TasksAdd, TutorialChapter.Tasks, Open with { Page = NotchPage.Tasks }, TutorialAct.AddTask, ProFeature.Tasks),
        S(TutorialStep.TasksReminder, TutorialChapter.Tasks, new(Face: TutorialFace.Reminder),
            new[] { TutorialAct.ReminderDone, TutorialAct.ReminderSnooze }, ProFeature.Tasks, arrows: true),

        // 7. Bandeja de archivos (el archivo es de ejemplo: no se guarda nada).
        S(TutorialStep.ShelfDrop, TutorialChapter.Shelf, new(), TutorialAct.DropFile, ProFeature.Shelf),
        S(TutorialStep.ShelfOpen, TutorialChapter.Shelf, Open with { Page = NotchPage.Shelf, ShelfFile = true },
            TutorialAct.OpenShelfItem, ProFeature.Shelf),

        // 8. Uso: llegar a la página, gasto y cuota, 5 h y 7 d, y el ratón por la gráfica.
        S(TutorialStep.UsagePage, TutorialChapter.Usage, Open,
            new[] { TutorialAct.WheelPage, TutorialAct.DotPage, TutorialAct.ArrowPage }, arrows: true),
        S(TutorialStep.UsageMode, TutorialChapter.Usage, Open with { Page = NotchPage.Usage }, TutorialAct.UsageMode, arrows: true),
        S(TutorialStep.UsageTile, TutorialChapter.Usage, Open with { Page = NotchPage.Usage }, TutorialAct.UsageTile, arrows: true),
        S(TutorialStep.UsageHover, TutorialChapter.Usage,
            Open with { Page = NotchPage.Usage, UsageMode = UsageChartMode.Quota, UsageFocus = QuotaFocus.Five },
            TutorialAct.ChartHover, ProFeature.NotchUsage, arrows: true),

        // 9. Foco (pomodoro): empezar, pausar, saltar, el mini anillo y, con Pro, la tarea enlazada.
        S(TutorialStep.FocusStart, TutorialChapter.Focus, Open with { Page = NotchPage.Focus }, TutorialAct.FocusPrimary),
        S(TutorialStep.FocusPause, TutorialChapter.Focus, Open with { Page = NotchPage.Focus, FocusRun = FocusRun.Running },
            TutorialAct.FocusPrimary),
        S(TutorialStep.FocusSkip, TutorialChapter.Focus, Open with { Page = NotchPage.Focus, FocusRun = FocusRun.Running },
            TutorialAct.FocusSkip),
        S(TutorialStep.FocusMini, TutorialChapter.Focus, Open with { Page = NotchPage.Focus, FocusRun = FocusRun.Running },
            TutorialAct.LeaveNotch),
        S(TutorialStep.FocusTask, TutorialChapter.Focus, Open with { Page = NotchPage.Focus, FocusRun = FocusRun.Running },
            TutorialAct.FocusTask, ProFeature.NotchFocus),

        // 10. La bandeja de Windows y el panel.
        S(TutorialStep.TrayPanel, TutorialChapter.Tray, new(Tray: TutorialTray.Icon), TutorialAct.TrayLeft),
        S(TutorialStep.TrayMenu, TutorialChapter.Tray, new(Tray: TutorialTray.Icon), TutorialAct.TrayRight),
        S(TutorialStep.TrayMonitor, TutorialChapter.Tray, new(Tray: TutorialTray.Menu), TutorialAct.MenuMonitor,
            ProFeature.MultiMonitor),
        S(TutorialStep.TraySettings, TutorialChapter.Tray, new(Tray: TutorialTray.Menu), TutorialAct.MenuSettings),
        S(TutorialStep.TrayTutorial, TutorialChapter.Tray, new(Tray: TutorialTray.Settings), TutorialAct.MenuTutorial),
    };

    private static TutorialStepInfo S(TutorialStep step, TutorialChapter chapter, TutorialDemo stage, TutorialAct goal,
        ProFeature? pro = null, bool arrows = false) => new(step, chapter, stage, new[] { goal }, pro, arrows);

    private static TutorialStepInfo S(TutorialStep step, TutorialChapter chapter, TutorialDemo stage, TutorialAct[] goals,
        ProFeature? pro = null, bool arrows = false) => new(step, chapter, stage, goals, pro, arrows);

    /// <summary>Todos los pasos, en orden.</summary>
    public static IReadOnlyList<TutorialStepInfo> All => Steps;

    public static TutorialStepInfo Info(TutorialStep step) => Steps[(int)step];

    /// <summary>Los pasos de un capítulo, en orden.</summary>
    public static IReadOnlyList<TutorialStep> StepsOf(TutorialChapter chapter) =>
        Steps.Where(s => s.Chapter == chapter).Select(s => s.Step).ToArray();

    /// <summary>¿Enseña el capítulo alguna función del Pro? (al acabarlo, sin Pro, la línea con el enlace).</summary>
    public static bool HasPro(TutorialChapter chapter) => Steps.Any(s => s.Chapter == chapter && s.IsPro);

    /// <summary>
    /// ¿Completa <paramref name="input"/> el paso? Tiene que ser uno de sus gestos y, en los que mueven algo, que de verdad lo
    /// haya movido (otra página, otro modo, el pomodoro en marcha o en pausa, la gráfica con el ratón encima…).
    /// </summary>
    public static bool Completes(TutorialStep step, TutorialInput input, TutorialDemo before, TutorialDemo after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (!Info(step).Goals.Contains(input.Act)) return false;
        return step switch
        {
            TutorialStep.PagesWheel or TutorialStep.PagesDots or TutorialStep.PagesArrows => after.Page != before.Page,
            TutorialStep.UsagePage => after.Page == NotchPage.Usage,
            TutorialStep.UsageMode => after.UsageMode != before.UsageMode,
            TutorialStep.FocusStart => after.FocusRun == FocusRun.Running,
            TutorialStep.FocusPause => after.FocusRun == FocusRun.Paused,
            TutorialStep.UsageHover => after.ChartAt >= 0,
            TutorialStep.TrayPanel => after.Tray == TutorialTray.Panel,
            TutorialStep.SessionsStates => after.SessionOffset != before.SessionOffset,
            TutorialStep.PermissionArm or TutorialStep.PermissionAllow or TutorialStep.TasksReminder =>
                before.Answer == TutorialAnswer.None && after.Answer != TutorialAnswer.None,
            _ => after != before || input.Act is TutorialAct.ExpandNotch or TutorialAct.LeaveNotch or TutorialAct.HoverNotch,
        };
    }

    /// <summary>
    /// El gesto que hace una tecla en este paso (la alternativa sin ratón), o <c>null</c> si esa tecla no hace nada aquí.
    /// Intro hace el gesto del paso; ← → (y ↑ ↓ en la lista de sesiones) los que tienen dirección.
    /// </summary>
    public static TutorialInput? KeyInput(TutorialStep step, TutorialDemo demo, TutorialKey key)
    {
        ArgumentNullException.ThrowIfNull(demo);
        var info = Info(step);
        int dir = key switch
        {
            TutorialKey.Left or TutorialKey.Up => -1,
            TutorialKey.Right or TutorialKey.Down => 1,
            _ => 0,
        };
        if (key != TutorialKey.Enter && !info.Arrows) return null;
        if (key is TutorialKey.Up or TutorialKey.Down && step != TutorialStep.SessionsStates) return null;
        int pages = TutorialDemo.Pages.Count;
        switch (step)
        {
            case TutorialStep.SessionsStates:
            {
                // Intro baja; arriba del todo, ↑ no movería nada: baja también.
                int d = dir == 0 || (dir < 0 && demo.SessionOffset == 0) ? 1 : dir;
                if (d > 0 && demo.SessionOffset >= TutorialDemo.MaxSessionOffset) d = -1;
                return new(TutorialAct.WheelSessions, d);
            }
            case TutorialStep.PagesWheel: return new(TutorialAct.WheelPage, dir == 0 ? 1 : dir);
            case TutorialStep.PagesArrows:
                return new(TutorialAct.ArrowPage, dir != 0 ? dir : demo.PageIndex < pages - 1 ? 1 : -1);
            case TutorialStep.PagesDots: return new(TutorialAct.DotPage, ((demo.PageIndex + (dir == 0 ? 1 : dir)) % pages + pages) % pages);
            case TutorialStep.UsagePage:
                return dir == 0 ? new(TutorialAct.DotPage, IndexOf(NotchPage.Usage)) : new(TutorialAct.WheelPage, dir);
            case TutorialStep.MusicNext: return new(dir < 0 ? TutorialAct.PreviousTrack : TutorialAct.NextTrack);
            case TutorialStep.TasksReminder: return new(dir < 0 ? TutorialAct.ReminderSnooze : TutorialAct.ReminderDone);
            case TutorialStep.UsageMode:
                return new(TutorialAct.UsageMode, dir == 0 ? (demo.UsageMode == UsageChartMode.Spend ? 1 : 0) : dir < 0 ? 0 : 1);
            case TutorialStep.UsageTile: return new(TutorialAct.UsageTile, dir < 0 ? 0 : 1);
            case TutorialStep.UsageHover:
            {
                const int stepBy = TutorialDemo.ChartSpan / 10;
                int at = demo.ChartAt < 0 ? TutorialDemo.ChartSpan * 2 / 3 : demo.ChartAt + dir * stepBy;
                return new(TutorialAct.ChartHover, Math.Clamp(at, 0, TutorialDemo.ChartSpan));
            }
            case TutorialStep.TasksCheck:
            {
                int row = 0;
                while (row < TutorialDemo.TaskCount - 1 && demo.TaskDone(row)) row++;
                return new(TutorialAct.CheckTask, row);
            }
            default: return new(info.Goals[0]);
        }
    }

    private static int IndexOf(NotchPage page)
    {
        for (int i = 0; i < TutorialDemo.Pages.Count; i++) if (TutorialDemo.Pages[i] == page) return i;
        return 0;
    }
}
