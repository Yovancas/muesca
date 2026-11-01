using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El fin de una fase del pomodoro en el notch: su aviso entra en la cola detrás de los recordatorios (nunca tapa un Permiso, el
/// aviso rojo de cuota, «Terminado» ni un recordatorio; el HUD del 80 % y el aviso de la app esperan), se enseña como el
/// recordatorio con su botón armado, manda sobre el expandido salvo editando una tarea y sale solo; su sonido discreto, sus
/// reglas y el despertar del pomodoro entre los demás plazos.
/// </summary>
public class NotchFocusAlertTests
{
    private static readonly DateTime T0 = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static NotchStateMachine New()
    {
        var m = new NotchStateMachine(new NotchSettings(false, false, true));
        m.OnSessions(Array.Empty<SessionState>(), T0);
        return m;
    }

    private static void End(NotchStateMachine m, DateTime at, FocusPhase ended = FocusPhase.Focus, FocusPhase next = FocusPhase.ShortBreak) =>
        m.OnFocusEnd(ended.ToString(), next.ToString(), at);

    [Fact]
    public void Its_priority_sits_after_reminders_and_before_the_hud_and_the_app_notice()
    {
        Assert.True(NotchAlertKind.Permission < NotchAlertKind.Focus);
        Assert.True(NotchAlertKind.QuotaAlert < NotchAlertKind.Focus);
        Assert.True(NotchAlertKind.Completed < NotchAlertKind.Focus);
        Assert.True(NotchAlertKind.Reminder < NotchAlertKind.Focus);
        Assert.True(NotchAlertKind.Focus < NotchAlertKind.QuotaHud);
        Assert.True(NotchAlertKind.Focus < NotchAlertKind.Notice);
        Assert.Equal(NotchTiming.FocusEnd, NotchAlert.DefaultDuration(NotchAlertKind.Focus));
    }

    [Fact]
    public void It_shows_with_its_button_arms_like_the_reminder_and_leaves_by_itself()
    {
        var m = New();
        End(m, T0);
        Assert.Equal(NotchMode.Reminder, m.Mode);
        Assert.Equal(NotchAlertKind.Focus, m.ActiveAlert!.Kind);
        Assert.Equal(nameof(FocusPhase.Focus), m.ActiveAlert.Title);           // datos, no texto final
        Assert.Equal(nameof(FocusPhase.ShortBreak), m.ActiveAlert.Detail);
        Assert.True(m.FocusAlertActionable);
        Assert.False(m.ReminderActionable);
        Assert.False(m.ReminderArmed(T0.AddMilliseconds(100)));
        Assert.True(m.ReminderArmed(T0 + NotchTiming.PermissionArm));
        Assert.Equal(NotchTiming.PermissionArm, m.NextWakeIn(T0));   // repintar al armarse

        m.Tick(T0 + NotchTiming.FocusEnd);
        Assert.Null(m.ActiveAlert);
        Assert.Equal(NotchMode.Idle, m.Mode);
        Assert.False(m.FocusAlertActionable);
    }

    [Fact]
    public void Clearing_it_takes_it_out_and_a_new_one_replaces_the_old()
    {
        var m = New();
        End(m, T0);
        End(m, T0.AddSeconds(2), FocusPhase.ShortBreak, FocusPhase.Focus);
        Assert.Equal(nameof(FocusPhase.ShortBreak), m.ActiveAlert!.Title);
        m.ClearFocusAlert(T0.AddSeconds(3));
        Assert.Null(m.ActiveAlert);
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    [Fact]
    public void It_never_covers_a_permission_quota_alert_completed_or_a_reminder()
    {
        var m = New();
        End(m, T0);
        m.OnSignal(new PermissionPending("a", "web", T0, "Bash", "npm test", "req-1"), T0);
        Assert.Equal(NotchMode.Permission, m.Mode);
        m.OnSignal(new PermissionCleared("a", "web", T0.AddSeconds(1)), T0.AddSeconds(1));
        Assert.Equal(NotchAlertKind.Focus, m.ActiveAlert!.Kind);

        var m2 = New();
        End(m2, T0);
        m2.OnTaskReminder(TaskAlerts.SessionId(Guid.NewGuid()), "Llamar", T0);
        Assert.Equal(NotchAlertKind.Reminder, m2.ActiveAlert!.Kind);
        m2.OnSignal(new TaskCompleted("s", "web", T0), T0);
        Assert.Equal(NotchAlertKind.Completed, m2.ActiveAlert!.Kind);
        m2.OnQuotaCrossing(new QuotaCrossing(QuotaWindowKind.FiveHour, 95, 95.4, null), T0);
        Assert.Equal(NotchAlertKind.QuotaAlert, m2.ActiveAlert!.Kind);
    }

    [Fact]
    public void The_hud_and_the_app_notice_wait_for_it()
    {
        var m = New();
        m.OnQuotaCrossing(new QuotaCrossing(QuotaWindowKind.FiveHour, 80, 80.2, null), T0);
        m.OnNotice("pro:trial-ended", T0);
        End(m, T0);
        Assert.Equal(NotchAlertKind.Focus, m.ActiveAlert!.Kind);
        m.Tick(T0 + NotchTiming.FocusEnd);
        Assert.Equal(NotchAlertKind.QuotaHud, m.ActiveAlert!.Kind);   // su reloj empieza al verse
    }

    [Fact]
    public void It_wins_over_the_panel_that_opens_while_it_shows()
    {
        var m = New();
        End(m, T0);
        m.OnPointerEnter(T0.AddSeconds(1));
        m.Tick(T0.AddSeconds(1) + NotchTiming.HoverExpand);   // el hover abriría el panel
        Assert.Equal(NotchMode.Reminder, m.Mode);             // ir con el ratón al botón no abre el panel
        Assert.True(m.FocusAlertActionable);
    }

    [Fact]
    public void With_the_panel_already_open_it_stays_on_top_without_closing_it()
    {
        var m = New();
        m.OnClick(T0);
        Assert.Equal(NotchMode.Expanded, m.Mode);
        End(m, T0.AddSeconds(1));
        Assert.Equal(NotchMode.Expanded, m.Mode);             // cada 5–25 min: no interrumpe lo que se está mirando
        Assert.Equal(NotchAlertKind.Focus, m.ActiveAlert!.Kind);
        Assert.False(m.FocusAlertActionable);
        m.OnClick(T0.AddSeconds(2));                          // al cerrar el panel, el aviso con su botón
        Assert.Equal(NotchMode.Reminder, m.Mode);
        m.OnPointerEnter(T0.AddSeconds(3));
        m.Tick(T0.AddSeconds(3) + NotchTiming.HoverExpand);
        Assert.Equal(NotchMode.Reminder, m.Mode);             // y ya manda como siempre
    }

    [Fact]
    public void While_typing_a_task_it_never_takes_the_panel()
    {

        var editing = New();
        End(editing, T0);
        editing.SetHold(true, T0.AddSeconds(1));
        Assert.Equal(NotchMode.Expanded, editing.Mode);   // integrado arriba: no corta la edición
        Assert.Equal(NotchAlertKind.Focus, editing.ActiveAlert!.Kind);
        Assert.False(editing.FocusAlertActionable);
    }

    [Fact]
    public void Hidden_in_full_screen_it_waits_and_its_clock_starts_when_seen()
    {
        var m = New();
        m.OnFullscreen(true);
        End(m, T0);
        Assert.Equal(NotchMode.Hidden, m.Mode);
        m.OnFullscreen(false);
        var back = T0.AddMinutes(5);
        m.Tick(back);
        Assert.Equal(NotchMode.Reminder, m.Mode);
        m.Tick(back + NotchTiming.FocusEnd - TimeSpan.FromSeconds(1));
        Assert.Equal(NotchAlertKind.Focus, m.ActiveAlert!.Kind);
    }

    [Fact]
    public void It_has_buttons_only_in_the_interactive_window()
    {
        var a = new NotchAlert(NotchAlertKind.Focus, NotchStateMachine.FocusAlertId, "Focus", "ShortBreak", T0, NotchTiming.FocusEnd);
        Assert.True(NotchAlertRouting.HasButtons(a));
        Assert.True(NotchAlertRouting.ButtonsHere(a, interactive: true));
        Assert.True(NotchAlertRouting.ReadOnlyHere(a, interactive: false));
        Assert.False(NotchAlertRouting.ReadOnlyHere(a, interactive: true));
    }

    // ---------------------------------------------------------------- sonido

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]   // el sonido del notch apagado lo calla todo
    [InlineData(true, false, false, false)]   // su propio ajuste
    [InlineData(true, true, true, false)]     // nunca en pantalla completa
    public void The_discreet_sound_follows_both_settings_and_never_plays_in_full_screen(bool notch, bool focus, bool fullscreen,
        bool expected) => Assert.Equal(expected, NotchSound.ShouldPlayFocus(notch, focus, fullscreen));

    [Fact]
    public void The_discreet_sound_is_a_valid_wav_softer_than_the_chime()
    {
        var wav = NotchSound.BuildFocusChime();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        static int Peak(byte[] w)
        {
            int peak = 0;
            for (int i = 44; i + 1 < w.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(w, i)));
            return peak;
        }
        Assert.True(Peak(wav) < Peak(NotchSound.BuildChime()), "debe ser más discreto que la campanita");
        Assert.True(Peak(wav) > 1000, "pero sonar");
        Assert.True(wav.Length > 44 + 22050);   // más de medio segundo a 22 kHz y 16 bits
        Assert.False(NotchSound.ShouldPlay(new NotchAlert(NotchAlertKind.Focus, "focus", "Focus", "ShortBreak", T0, null),
            soundEnabled: true, fullscreen: false));   // la campanita no: lleva el suyo
    }

    // ---------------------------------------------------------------- despertares

    [Fact]
    public void The_pomodoro_wake_joins_the_others_and_is_dropped_in_full_screen()
    {
        var s1 = TimeSpan.FromSeconds(1);
        var s5 = TimeSpan.FromSeconds(5);
        var m60 = TimeSpan.FromMinutes(1);
        Assert.Equal(s1, NotchForm.NextWake(s5, null, null, hiddenForFullscreen: false, focus: s1));
        Assert.Equal(s5, NotchForm.NextWake(s5, null, null, hiddenForFullscreen: false, focus: m60));
        Assert.Equal(m60, NotchForm.NextWake(null, null, null, hiddenForFullscreen: false, focus: m60));
        Assert.Null(NotchForm.NextWake(null, null, null, hiddenForFullscreen: true, focus: s1));
        Assert.Null(NotchForm.NextWake(null, null, null, hiddenForFullscreen: false));   // parado o en pausa: nada
    }
}
