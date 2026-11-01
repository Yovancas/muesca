using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Tutorial;

namespace ClaudeBarWin.Tests.Tutorial;

/// <summary>El notch de demostración (<see cref="TutorialDemo"/>): el modo que enseñaría (con la precedencia del notch de
/// verdad) y lo que hace cada gesto.</summary>
public sealed class TutorialDemoTests
{
    private static TutorialDemo Do(TutorialDemo d, TutorialAct act, int value = 0) => d.Apply(new TutorialInput(act, value));

    [Fact]
    public void Mode_follows_the_notch_precedence()
    {
        var open = new TutorialDemo(Pointer: TutorialPointer.Expanded);
        Assert.Equal(NotchMode.Idle, new TutorialDemo().Mode);
        Assert.Equal(NotchMode.Expanded, open.Mode);
        // El Permiso manda sobre el expandido (ir con el ratón a sus botones no lo convierte en el panel).
        Assert.Equal(NotchMode.Permission, (open with { Face = TutorialFace.Permission }).Mode);
        // El recordatorio, mientras no se conteste.
        Assert.Equal(NotchMode.Reminder, (open with { Face = TutorialFace.Reminder }).Mode);
        Assert.Equal(NotchMode.Expanded, (open with { Face = TutorialFace.Reminder, Answer = TutorialAnswer.Allow }).Mode);
        // El expandido integra «Listo» y el 95 %; sin abrir, los avisos mandan sobre la vista previa.
        Assert.Equal(NotchMode.Expanded, (open with { Face = TutorialFace.Done }).Mode);
        Assert.Equal(NotchMode.Completed, new TutorialDemo(Face: TutorialFace.Done, Pointer: TutorialPointer.Peek).Mode);
        Assert.Equal(NotchMode.QuotaAlert, new TutorialDemo(Face: TutorialFace.QuotaHigh).Mode);
        Assert.Equal(NotchMode.Peek, new TutorialDemo(Face: TutorialFace.Working, Pointer: TutorialPointer.Peek).Mode);
        Assert.Equal(NotchMode.Working, new TutorialDemo(Face: TutorialFace.Working).Mode);
        Assert.Equal(NotchMode.Minimal, new TutorialDemo(Face: TutorialFace.Minimal).Mode);
        Assert.Equal(NotchMode.Idle, new TutorialDemo(Face: TutorialFace.Clock).Mode);
        Assert.Equal(NotchMode.Idle, new TutorialDemo(Face: TutorialFace.CompactLyrics).Mode);
        // En la bandeja de mentira, el notch de reposo se queda quieto arriba.
        Assert.Equal(NotchMode.Idle, (open with { Tray = TutorialTray.Menu }).Mode);
    }

    [Fact]
    public void Hover_opens_and_leaving_closes()
    {
        var d = Do(new TutorialDemo(Page: NotchPage.Music), TutorialAct.HoverNotch);
        Assert.Equal(TutorialPointer.Peek, d.Pointer);
        Assert.Equal(d, Do(d, TutorialAct.HoverNotch));
        d = Do(d, TutorialAct.ExpandNotch);
        Assert.Equal(TutorialPointer.Expanded, d.Pointer);
        Assert.Equal(NotchPage.Claude, d.Page);   // cada apertura empieza en Claude
        d = Do(d, TutorialAct.DotPage, 2);
        Assert.Equal(d, Do(d, TutorialAct.ExpandNotch));   // ya abierto: no vuelve a Claude
        d = Do(d with { SessionOffset = 2, Drafting = true, ChartAt = 300 }, TutorialAct.LeaveNotch);
        Assert.Equal(TutorialPointer.Away, d.Pointer);
        Assert.Equal(0, d.SessionOffset);
        Assert.False(d.Drafting);
        Assert.Equal(-1, d.ChartAt);
    }

    [Fact]
    public void Pages_wrap_with_the_wheel_but_not_with_the_arrows()
    {
        var first = new TutorialDemo(Pointer: TutorialPointer.Expanded);
        int n = TutorialDemo.Pages.Count;
        Assert.Equal(TutorialDemo.Pages[n - 1], Do(first, TutorialAct.WheelPage, -1).Page);
        Assert.Equal(TutorialDemo.Pages[1], Do(first, TutorialAct.WheelPage, 1).Page);
        Assert.Equal(first, Do(first, TutorialAct.ArrowPage, -1));
        var last = first with { Page = TutorialDemo.Pages[n - 1] };
        Assert.Equal(last, Do(last, TutorialAct.ArrowPage, 1));
        Assert.Equal(TutorialDemo.Pages[n - 2], Do(last, TutorialAct.ArrowPage, -1).Page);
        Assert.Equal(first, Do(first, TutorialAct.DotPage, 99));
        Assert.Equal(first, Do(first, TutorialAct.DotPage, -1));
        Assert.Equal(first, Do(first, TutorialAct.WheelPage, 0));
    }

    [Fact]
    public void The_session_list_scrolls_within_its_bounds()
    {
        var d = new TutorialDemo(Pointer: TutorialPointer.Expanded);
        Assert.Equal(0, Do(d, TutorialAct.WheelSessions, -1).SessionOffset);
        for (int i = 0; i < 20; i++) d = Do(d, TutorialAct.WheelSessions, 1);
        Assert.Equal(TutorialDemo.MaxSessionOffset, d.SessionOffset);
        Assert.True(TutorialDemo.MaxSessionOffset > 0, "la lista de ejemplo no cabe: la rueda tiene algo que hacer");
    }

    [Fact]
    public void Buttons_answer_once_and_only_on_their_notice()
    {
        var idle = new TutorialDemo();
        Assert.Equal(idle, Do(idle, TutorialAct.PermissionAllow));
        Assert.Equal(idle, Do(idle, TutorialAct.ReminderDone));
        Assert.Equal(idle, Do(idle, TutorialAct.ClickPermissionText));

        var ask = new TutorialDemo(Face: TutorialFace.Permission);
        var denied = Do(ask, TutorialAct.PermissionDeny);
        Assert.Equal(TutorialAnswer.Deny, denied.Answer);
        Assert.Equal(denied, Do(denied, TutorialAct.PermissionAllow));
        Assert.Equal(TutorialEffect.Terminal, Do(ask, TutorialAct.ClickPermissionText).Effect);

        var reminder = new TutorialDemo(Face: TutorialFace.Reminder);
        var done = Do(reminder, TutorialAct.ReminderDone);
        Assert.Equal(TutorialAnswer.Allow, done.Answer);
        Assert.True(done.TaskDone(0));
        var snoozed = Do(reminder, TutorialAct.ReminderSnooze);
        Assert.Equal(TutorialAnswer.Deny, snoozed.Answer);
        Assert.False(snoozed.TaskDone(0));
    }

    [Fact]
    public void Music_tasks_and_shelf_gestures()
    {
        var d = new TutorialDemo(Pointer: TutorialPointer.Expanded, Page: NotchPage.Music);
        Assert.False(Do(d, TutorialAct.PlayPause).Playing);
        Assert.Equal(1, Do(d, TutorialAct.NextTrack).Track);
        Assert.Equal(TutorialDemo.TrackCount - 1, Do(d, TutorialAct.PreviousTrack).Track);
        Assert.True(Do(d with { Playing = false }, TutorialAct.NextTrack).Playing);
        Assert.True(Do(d, TutorialAct.ToggleLyrics).Lyrics);

        var t = Do(d, TutorialAct.CheckTask, 1);
        Assert.True(t.TaskDone(1));
        Assert.False(Do(t, TutorialAct.CheckTask, 1).TaskDone(1));
        Assert.Equal(d, Do(d, TutorialAct.CheckTask, TutorialDemo.TaskCount));
        Assert.True(Do(d, TutorialAct.AddTask).Drafting);

        var closed = new TutorialDemo();
        Assert.Equal(closed, Do(closed, TutorialAct.OpenShelfItem));   // sin archivo, nada que abrir
        var dropped = Do(closed, TutorialAct.DropFile);
        Assert.True(dropped.ShelfFile);
        Assert.Equal(NotchMode.Expanded, dropped.Mode);
        Assert.Equal(NotchPage.Shelf, dropped.Page);
        Assert.Equal(TutorialEffect.OpenFile, Do(dropped, TutorialAct.OpenShelfItem).Effect);
    }

    [Fact]
    public void Usage_gestures()
    {
        var d = new TutorialDemo(Pointer: TutorialPointer.Expanded, Page: NotchPage.Usage);
        var quota = Do(d, TutorialAct.UsageMode, 1);
        Assert.Equal(UsageChartMode.Quota, quota.UsageMode);
        var seven = Do(d, TutorialAct.UsageTile, 1);
        Assert.Equal((UsageChartMode.Quota, QuotaFocus.Seven), (seven.UsageMode, seven.UsageFocus));
        Assert.Equal(QuotaFocus.None, Do(seven, TutorialAct.UsageMode, 0).UsageFocus);
        Assert.Equal(TutorialDemo.ChartSpan, Do(d, TutorialAct.ChartHover, 5000).ChartAt);
        Assert.Equal(-1, Do(d with { ChartAt = 400 }, TutorialAct.ChartHover, -1).ChartAt);
        Assert.Equal(-1, Do(d with { ChartAt = 400 }, TutorialAct.WheelPage, 1).ChartAt);
    }

    [Fact]
    public void Focus_gestures()
    {
        var d = new TutorialDemo(Pointer: TutorialPointer.Expanded, Page: NotchPage.Focus);
        var running = Do(d, TutorialAct.FocusPrimary);
        Assert.Equal(FocusRun.Running, running.FocusRun);
        Assert.Equal(FocusRun.Paused, Do(running, TutorialAct.FocusPrimary).FocusRun);
        Assert.Equal(FocusRun.Running, Do(Do(running, TutorialAct.FocusPrimary), TutorialAct.FocusPrimary).FocusRun);
        var skipped = Do(running, TutorialAct.FocusSkip);
        Assert.Equal((FocusPhase.ShortBreak, FocusRun.Ready), (skipped.FocusPhase, skipped.FocusRun));
        Assert.Equal(FocusPhase.Focus, Do(skipped, TutorialAct.FocusSkip).FocusPhase);
        var reset = Do(Do(running, TutorialAct.FocusPrimary), TutorialAct.FocusReset);
        Assert.Equal((FocusPhase.Focus, FocusRun.Ready), (reset.FocusPhase, reset.FocusRun));
        Assert.Equal(1, Do(d, TutorialAct.FocusTask).FocusTask);
        Assert.Equal(0, Do(d with { FocusTask = TutorialDemo.TaskCount - 1 }, TutorialAct.FocusTask).FocusTask);
    }

    [Fact]
    public void The_mock_tray_menu_opens_in_order()
    {
        var icon = new TutorialDemo(Tray: TutorialTray.Icon);
        Assert.Equal(TutorialTray.Panel, Do(icon, TutorialAct.TrayLeft).Tray);
        Assert.Equal(TutorialTray.Icon, Do(Do(icon, TutorialAct.TrayLeft), TutorialAct.TrayLeft).Tray);
        Assert.Equal(icon, Do(icon, TutorialAct.MenuSettings));   // sin el menú abierto, no hay entradas
        var menu = Do(icon, TutorialAct.TrayRight);
        Assert.Equal(TutorialTray.Menu, menu.Tray);
        Assert.Equal(TutorialTray.Monitor, Do(menu, TutorialAct.MenuMonitor).Tray);
        Assert.Equal(TutorialTray.Settings, Do(menu, TutorialAct.MenuSettings).Tray);
        Assert.Equal(TutorialEffect.TutorialHere, Do(Do(menu, TutorialAct.MenuSettings), TutorialAct.MenuTutorial).Effect);
        Assert.Equal(TutorialEffect.TutorialHere, Do(menu, TutorialAct.MenuTutorial).Effect);
    }
}
