using ClaudeBarWin.Models;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Recordatorios de tareas en el notch (F5 · B5): el aviso entra en la cola entre «Terminado» y el HUD, persiste hasta
/// «Hecha» / «+10 min» / descartarlo, sus botones se arman como los del Permiso, espera mientras el notch está oculto en
/// pantalla completa y la edición en línea mantiene el expandido abierto (<see cref="NotchStateMachine.SetHold"/>).
/// </summary>
public class NotchReminderTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Task1 = TaskAlerts.SessionId(Guid.NewGuid());
    private static readonly string Task2 = TaskAlerts.SessionId(Guid.NewGuid());

    private static NotchStateMachine New(bool hideFs = true, TimeSpan? autoMinimal = null)
    {
        var m = new NotchStateMachine(new NotchSettings(false, false, hideFs, autoMinimal));
        m.OnSessions(Array.Empty<SessionState>(), T0);
        return m;
    }

    [Fact]
    public void Reminder_priority_sits_between_Completed_and_the_HUD()
    {
        Assert.True(NotchAlertKind.Completed < NotchAlertKind.Reminder);
        Assert.True(NotchAlertKind.Reminder < NotchAlertKind.QuotaHud);
        Assert.Null(NotchAlert.DefaultDuration(NotchAlertKind.Reminder));   // persiste hasta que se contesta
    }

    [Fact]
    public void A_reminder_shows_as_its_own_mode_and_stays_until_cleared()
    {
        var m = New();
        m.OnTaskReminder(Task1, "Comprar pan", T0);
        Assert.Equal(NotchMode.Reminder, m.Mode);
        Assert.Equal(NotchAlertKind.Reminder, m.ActiveAlert!.Kind);
        Assert.Equal("Comprar pan", m.ActiveAlert.Title);

        m.Tick(T0.AddHours(2));
        Assert.Equal(NotchMode.Reminder, m.Mode);

        m.ClearTaskReminder(Task1, T0.AddHours(2));
        Assert.Equal(NotchMode.Idle, m.Mode);
        Assert.Null(m.ActiveAlert);
    }

    [Fact]
    public void Completed_goes_first_and_the_reminder_waits_its_turn()
    {
        var m = New();
        m.OnTaskReminder(Task1, "x", T0);
        m.OnSignal(new TaskCompleted("s", "web", T0), T0);
        Assert.Equal(NotchMode.Completed, m.Mode);
        m.Tick(T0 + NotchTiming.Completed);
        Assert.Equal(NotchMode.Reminder, m.Mode);
    }

    [Fact]
    public void Two_reminders_queue_one_after_the_other()
    {
        var m = New();
        m.OnTaskReminder(Task1, "uno", T0);
        m.OnTaskReminder(Task2, "dos", T0);
        Assert.Equal("uno", m.ActiveAlert!.Title);
        m.ClearTaskReminder(Task1, T0.AddSeconds(3));
        Assert.Equal("dos", m.ActiveAlert!.Title);
    }

    [Fact]
    public void Reminder_buttons_arm_after_the_same_delay_as_the_permission_ones()
    {
        var m = New();
        m.OnTaskReminder(Task1, "x", T0);
        Assert.True(m.ReminderActionable);
        Assert.False(m.ReminderArmed(T0));
        Assert.Equal(0f, m.ReminderArmProgress(T0));
        Assert.Equal(NotchTiming.PermissionArm, m.NextWakeIn(T0));          // repintar al armarse
        Assert.True(m.ReminderArmed(T0 + NotchTiming.PermissionArm));
        m.Tick(T0 + NotchTiming.PermissionArm);
        Assert.Null(m.NextWakeIn(T0 + NotchTiming.PermissionArm));          // persistente: nada más que esperar

        // Otro recordatorio a la vista: se arma otra vez.
        m.ClearTaskReminder(Task1, T0.AddSeconds(5));
        m.OnTaskReminder(Task2, "y", T0.AddSeconds(5));
        Assert.False(m.ReminderArmed(T0.AddSeconds(5)));
    }

    [Fact]
    public void A_reminder_that_comes_due_while_hidden_in_fullscreen_waits_and_shows_on_return()
    {
        var m = New(hideFs: true);
        m.OnFullscreen(true);
        m.OnTaskReminder(Task1, "x", T0);
        Assert.Equal(NotchMode.Hidden, m.Mode);
        Assert.Null(m.ActiveAlert);
        Assert.Null(m.NextWakeIn(T0));

        m.OnFullscreen(false);
        Assert.Equal(TimeSpan.Zero, m.NextWakeIn(T0.AddMinutes(30)));
        m.Tick(T0.AddMinutes(30));
        Assert.Equal(NotchMode.Reminder, m.Mode);
        Assert.False(m.ReminderArmed(T0.AddMinutes(30)));                    // se arma desde que SE VE
    }

    [Fact]
    public void Hovering_towards_the_buttons_does_not_turn_the_reminder_into_the_panel()
    {
        var m = New();
        m.OnTaskReminder(Task1, "x", T0);
        m.OnPointerEnter(T0);
        m.Tick(T0 + NotchTiming.HoverExpand);
        Assert.Equal(NotchMode.Reminder, m.Mode);
        Assert.True(m.ReminderActionable);

        // Contestado o descartado con el puntero encima: aparece el expandido que estaba debajo.
        m.ClearTaskReminder(Task1, T0.AddSeconds(2));
        Assert.Equal(NotchMode.Expanded, m.Mode);
    }

    [Fact]
    public void While_editing_a_task_the_expanded_integrates_the_reminder()
    {
        var m = New();
        m.OnPointerEnter(T0);
        m.SetHold(true, T0);
        m.OnTaskReminder(Task1, "x", T0.AddSeconds(1));
        Assert.Equal(NotchMode.Expanded, m.Mode);
        Assert.Equal(NotchAlertKind.Reminder, m.ActiveAlert!.Kind);
        Assert.False(m.ReminderActionable);

        m.SetHold(false, T0.AddSeconds(2));               // al acabar de editar, el recordatorio pasa a primer plano
        Assert.Equal(NotchMode.Reminder, m.Mode);
    }

    [Fact]
    public void A_pending_reminder_keeps_the_notch_from_shrinking()
    {
        var auto = TimeSpan.FromSeconds(30);
        var m = New(autoMinimal: auto);
        m.OnTaskReminder(Task1, "x", T0);
        m.Tick(T0 + auto + auto);
        Assert.Equal(NotchMode.Reminder, m.Mode);
    }

    [Fact]
    public void Hold_keeps_the_expanded_open_while_editing_and_releases_with_the_grace()
    {
        var m = New();
        m.OnPointerEnter(T0);
        m.Tick(T0 + NotchTiming.HoverExpand);
        Assert.Equal(NotchMode.Expanded, m.Mode);

        var t1 = T0.AddSeconds(1);
        m.SetHold(true, t1);
        m.OnPointerLeave(t1);
        m.Tick(t1.AddSeconds(10));
        Assert.Equal(NotchMode.Expanded, m.Mode);
        m.OnClick(t1.AddSeconds(10));                    // un clic «suelto» tampoco lo cierra mientras se edita
        Assert.Equal(NotchMode.Expanded, m.Mode);

        var t2 = t1.AddSeconds(20);
        m.SetHold(false, t2);
        Assert.Equal(NotchMode.Expanded, m.Mode);
        Assert.Equal(NotchTiming.CollapseGrace, m.NextWakeIn(t2));
        m.Tick(t2 + NotchTiming.CollapseGrace);
        Assert.NotEqual(NotchMode.Expanded, m.Mode);
    }

    [Fact]
    public void Releasing_the_hold_with_the_pointer_inside_stays_open()
    {
        var m = New();
        m.OnPointerEnter(T0);
        m.SetHold(true, T0);
        Assert.Equal(NotchMode.Expanded, m.Mode);
        m.SetHold(false, T0.AddSeconds(5));
        m.Tick(T0.AddSeconds(10));
        Assert.Equal(NotchMode.Expanded, m.Mode);
    }

    [Fact]
    public void Hiding_in_fullscreen_drops_the_hold()
    {
        var m = New(hideFs: true);
        m.SetHold(true, T0);
        m.OnFullscreen(true);
        m.OnFullscreen(false);
        m.Tick(T0.AddSeconds(1));
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    [Fact]
    public void Reminder_mode_has_a_size_with_room_for_the_buttons_and_opens_softly()
    {
        var s = NotchLayout.TargetSize(NotchMode.Reminder, 1f, 0f);
        Assert.Equal(NotchLayout.PermissionButtonsSize(1f), s);
        Assert.Equal(ClaudeBarWin.Services.Motion.Motion.SpringSoft, NotchLayout.PresetFor(NotchMode.Idle, NotchMode.Reminder));
        Assert.Equal(1f, NotchMotion.ShadowOpacity(NotchMode.Reminder));
    }

    [Fact]
    public void Reminders_chime_unless_muted_or_in_fullscreen()
    {
        var a = new NotchAlert(NotchAlertKind.Reminder, Task1, "x", null, T0, null);
        Assert.True(NotchSound.ShouldPlay(a, soundEnabled: true, fullscreen: false));
        Assert.False(NotchSound.ShouldPlay(a, soundEnabled: false, fullscreen: false));
        Assert.False(NotchSound.ShouldPlay(a, soundEnabled: true, fullscreen: true));
    }
}
