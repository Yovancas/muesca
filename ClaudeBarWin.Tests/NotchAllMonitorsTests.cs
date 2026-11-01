using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Un notch por monitor (29-oct): la confirmación de un Permiso contestado en otra ventana, el aviso pintado en solo lectura,
/// el contenedor compartido (<see cref="NotchShared"/>: campanitas, recordatorios, «alguien ve el notch») y dos ventanas
/// sobre el MISMO hub y el mismo contenedor. Sin mostrar ventanas.
/// </summary>
public class NotchAllMonitorsTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly NotchOptions Opts = new(false, false, true, true, 0, "", false);

    private static NotchStateMachine New() => new(new NotchSettings(false, Minimal: false, HideInFullscreen: true));

    private static PermissionPending Req(string session = "a", string id = "r1") =>
        new(session, session, T0, "Bash", "npm test", id);

    // ---------------------------------------------------------------- contestado en otra ventana

    [Fact]
    public void A_read_only_window_shows_the_answer_given_elsewhere_and_then_drops_the_alert()
    {
        var mirror = New();
        mirror.OnSignal(Req(), T0);
        Assert.Equal(NotchMode.Permission, mirror.Mode);

        var t1 = T0.AddSeconds(2);
        mirror.OnPermissionAnsweredElsewhere("a", "r1", PermissionVerdict.Allow, t1);
        Assert.Equal(PermissionVerdict.Allow, mirror.PermissionAnswer);
        Assert.False(mirror.PermissionActionable);
        Assert.Equal(NotchTiming.PermissionConfirm, mirror.NextWakeIn(t1));

        // El broker cierra la petición (señal que llega después): no se queda en «Responde en la terminal».
        mirror.OnSignal(new PermissionRequestClosed("a", "a", t1, "r1"), t1);
        Assert.Equal("r1", mirror.ActiveAlert!.RequestId);

        mirror.Tick(t1 + NotchTiming.PermissionConfirm);
        Assert.Null(mirror.ActiveAlert);
        Assert.NotEqual(NotchMode.Permission, mirror.Mode);
    }

    [Fact]
    public void An_answer_for_another_request_or_without_verdict_changes_nothing()
    {
        var mirror = New();
        mirror.OnSignal(Req(id: "r2"), T0);
        mirror.OnPermissionAnsweredElsewhere("a", "r1", PermissionVerdict.Deny, T0);     // otra petición (ya sustituida)
        mirror.OnPermissionAnsweredElsewhere("b", "r2", PermissionVerdict.Deny, T0);     // otra sesión
        mirror.OnPermissionAnsweredElsewhere("a", "r2", PermissionVerdict.None, T0);     // sin decisión
        Assert.Equal(PermissionVerdict.None, mirror.PermissionAnswer);
        Assert.True(mirror.PermissionActionable);
    }

    // ---------------------------------------------------------------- pintado en solo lectura

    private static readonly Size Surface = new(700, 200);

    /// <summary>Pinta el aviso a escala 1 con el alto del Permiso con botones y devuelve el brillo (canal R) del borde de abajo
    /// de los dos botones (por debajo del texto) y si el painter dice que pintó un Permiso con botones.</summary>
    private static (int Deny, int Allow, bool PermissionPainted) Paint(NotchAlert alert, NotchMode mode, bool readOnly,
        PermissionVerdict answer = PermissionVerdict.None)
    {
        var size = NotchLayout.PermissionButtonsSize(1f);
        using var bmp = new Bitmap(Surface.Width, Surface.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        painter.Paint(g, Surface, new NotchFrame(size.Width, size.Height, 1f, mode, alert,
            Array.Empty<SessionState>(), null, 0f, 1f, PermissionArm: 1f, PermissionAnswer: answer, ReadOnly: readOnly));
        var (deny, allow) = NotchGeometry.PermissionButtons(NotchGeometry.PillRect(size.Width, size.Height, Surface), 1f);
        return (bmp.GetPixel(deny.Left + 4, deny.Bottom - 3).R, bmp.GetPixel(allow.Left + 4, allow.Bottom - 3).R,
            painter.LastPermissionPaint is not null);
    }

    [Fact]
    public void Read_only_permission_has_no_buttons()
    {
        var alert = new NotchAlert(NotchAlertKind.Permission, "a", "web", "Bash: npm test", T0, null, "r1");
        var live = Paint(alert, NotchMode.Permission, readOnly: false);
        Assert.True(live.Deny > 10 && live.Allow > 10, $"{live}");
        Assert.True(live.PermissionPainted);

        var ro = Paint(alert, NotchMode.Permission, readOnly: true);
        Assert.Equal((0, 0, false), ro);               // ni botones ni «visto entero» para el armado
        Assert.Equal((0, 0, false), Paint(alert, NotchMode.Permission, readOnly: true, answer: PermissionVerdict.Allow));
    }

    [Fact]
    public void Read_only_reminder_has_no_buttons()
    {
        var alert = new NotchAlert(NotchAlertKind.Reminder, "task:1", "Comprar pan", null, T0, null);
        var live = Paint(alert, NotchMode.Reminder, readOnly: false);
        Assert.True(live.Deny > 10 && live.Allow > 10, $"{live}");
        Assert.Equal((0, 0, false), Paint(alert, NotchMode.Reminder, readOnly: true));
    }

    [Fact]
    public void Read_only_alert_is_the_size_of_the_read_only_permission()
    {
        Assert.Equal(NotchLayout.TargetSize(NotchMode.Permission, 1.5f, 0f), NotchLayout.ReadOnlyAlertSize(1.5f));
        Assert.True(NotchLayout.ReadOnlyAlertSize(1f).Height < NotchLayout.PermissionButtonsSize(1f).Height);
    }

    [Fact]
    public void Read_only_hint_is_translated()
    {
        var en = Localization.Get("en");
        var es = Localization.Get("es");
        Assert.Equal("Haz clic para responder aquí", es.NotchMirrorClickHint);
        Assert.NotEqual(en.NotchMirrorClickHint, es.NotchMirrorClickHint);
        Assert.NotEqual(en.NotchMonitorModeFixed, es.NotchMonitorModeFixed);
        Assert.NotEqual(en.NotchMonitorModeFollow, es.NotchMonitorModeFollow);
        Assert.NotEqual(en.NotchMonitorModeAll, es.NotchMonitorModeAll);
        Assert.NotEqual(en.NotchMonitorFixed, es.NotchMonitorFixed);
    }

    // ---------------------------------------------------------------- contenedor compartido

    private static NotchShared Shared(ClaudeStateHub hub) => new(hub, Opts, work => work());

    [Fact]
    public void The_broker_accepts_requests_while_any_window_is_visible()
    {
        var hub = new ClaudeStateHub();
        using var shared = Shared(hub);
        object a = new(), b = new();
        shared.SetHidden(a, false);
        shared.SetHidden(b, false);
        shared.SetHidden(a, true);                     // juego en el monitor de «a»
        Assert.True(hub.Permissions.Accepting);
        shared.SetHidden(b, true);                     // y en el de «b»: nadie lo vería
        Assert.False(hub.Permissions.Accepting);
        shared.SetHidden(b, false);
        Assert.True(hub.Permissions.Accepting);
        shared.SetHidden(b, true);
        shared.Forget(b);
        Assert.False(hub.Permissions.Accepting);       // queda «a», oculta
        shared.Forget(a);
        Assert.True(hub.Permissions.Accepting);        // sin ventanas: no se queda rechazando para la siguiente
    }

    [Fact]
    public void A_single_window_hides_and_shows_like_before()
    {
        var hub = new ClaudeStateHub();
        using var shared = Shared(hub);
        object w = new();
        shared.SetHidden(w, false);
        Assert.True(hub.Permissions.Accepting);
        shared.SetHidden(w, true);
        Assert.False(hub.Permissions.Accepting);
        shared.SetHidden(w, false);
        Assert.True(hub.Permissions.Accepting);
    }

    [Fact]
    public void Fullscreen_on_any_monitor_silences_every_window()
    {
        // Juego a pantalla completa en el monitor de «a»: ese notch se oculta y los botones pasan a «b», pero «b» no suena.
        using var shared = Shared(new ClaudeStateHub());
        object a = new(), b = new();
        Assert.False(shared.AnyFullscreen);
        shared.SetFullscreen(a, true);
        Assert.True(shared.AnyFullscreen);
        Assert.True(NotchAlertRouting.QuietForFullscreen(here: false, anyWindow: shared.AnyFullscreen));
        shared.SetFullscreen(a, false);
        Assert.False(shared.AnyFullscreen);

        shared.SetFullscreen(b, true);
        shared.SetFullscreen(b, true);                 // repetido: cuenta una vez
        shared.SetFullscreen(b, false);
        Assert.False(shared.AnyFullscreen);

        shared.SetFullscreen(a, true);
        shared.Forget(a);                              // se cierra la ventana del juego (monitor desconectado)
        Assert.False(shared.AnyFullscreen);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]                    // con una sola ventana, su propio vigilante: como siempre
    [InlineData(false, true, true)]                    // la interactiva en otro monitor que el del juego
    [InlineData(true, true, true)]
    public void A_window_stays_quiet_with_fullscreen_here_or_on_any_other_window(bool here, bool any, bool quiet) =>
        Assert.Equal(quiet, NotchAlertRouting.QuietForFullscreen(here, any));

    [Fact]
    public void A_window_hidden_for_fullscreen_stops_wanting_lyrics()
    {
        using var shared = Shared(new ClaudeStateHub());
        object a = new();
        shared.UpdateLyrics(a, wanted: true);
        shared.SetHidden(a, true);
        // Nadie más la quiere: el «sí» viejo de la oculta no cuenta (ninguna petición para un notch que no se ve).
        Assert.False(LyricsWantedBy(shared, a));
        shared.SetHidden(a, false);
        Assert.False(LyricsWantedBy(shared, a));      // hasta que su Sync la vuelva a pedir
        shared.UpdateLyrics(a, wanted: true);
        Assert.True(LyricsWantedBy(shared, a));
    }

    private static bool LyricsWantedBy(NotchShared shared, object window)
    {
        var field = typeof(NotchShared).GetField("_lyricsWanted",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var map = (Dictionary<object, bool>)field!.GetValue(shared)!;
        return map.TryGetValue(window, out bool wanted) && wanted;
    }

    [Fact]
    public void A_permission_request_chimes_once_whichever_window_shows_it()
    {
        using var shared = Shared(new ClaudeStateHub());
        Assert.True(shared.TryChimeRequest("perm:r1"));
        Assert.False(shared.TryChimeRequest("perm:r1"));   // re-armado, o el principal efectivo cambió de ventana
        Assert.True(shared.TryChimeRequest("perm:r2"));
    }

    [Fact]
    public void Clearing_a_reminder_reaches_every_window_and_answers_are_broadcast()
    {
        using var shared = Shared(new ClaudeStateHub());
        var cleared = new List<string>();
        var answers = new List<(string, string, PermissionVerdict)>();
        shared.ReminderCleared += cleared.Add;
        shared.ReminderCleared += id => cleared.Add("2:" + id);
        shared.PermissionAnswered += (_, s, r, v) => answers.Add((s, r, v));

        shared.ClearReminder("task:1");
        shared.AnnouncePermissionAnswer(new object(), "a", "r1", PermissionVerdict.Deny);

        Assert.Equal(new[] { "task:1", "2:task:1" }, cleared);
        Assert.Equal(new[] { ("a", "r1", PermissionVerdict.Deny) }, answers);
        Assert.Empty(shared.ActiveReminders);
    }

    // ---------------------------------------------------------------- dos ventanas, un hub

    [Fact]
    public void Two_windows_on_one_hub_mirror_the_same_alert_and_hold_their_place()
    {
        var hub = new ClaudeStateHub();
        using var shared = Shared(hub);
        using var primary = new NotchForm(hub, Opts with { MonitorDeviceName = "" }, Localization.Get("es"), shared);
        using var mirror = new NotchForm(hub, Opts, Localization.Get("es"), shared) { Interactive = false };
        Assert.True(primary.Interactive);
        Assert.False(mirror.Interactive);
        Assert.False(primary.HoldsPlace);

        var done = hub.Permissions.Register(new PermissionRequestInfo("r1", "a", "web", "Bash", "npm test", DateTime.UtcNow, true));
        hub.SyncPermissions(DateTime.UtcNow);
        // El aviso está en las dos (con botones en una, en solo lectura en la otra) y ninguna se muda mientras tanto.
        Assert.True(primary.HoldsPlace);
        Assert.True(mirror.HoldsPlace);
        Assert.False(primary.InUse);                    // un aviso no retiene al principal efectivo
        Assert.False(done.IsCompleted);
    }

    [Fact]
    public void Closing_one_window_keeps_the_shared_services_for_the_others()
    {
        var hub = new ClaudeStateHub();
        using var shared = Shared(hub);
        var a = new NotchForm(hub, Opts, null, shared);
        using var b = new NotchForm(hub, Opts, null, shared);
        a.Dispose();
        Assert.True(shared.TryChimeRequest("perm:x"));     // el contenedor sigue vivo
        Assert.True(hub.Permissions.Accepting);
        hub.PublishSessions(Array.Empty<SessionState>(), DateTime.UtcNow);   // la que queda sigue enganchada y la cerrada no
        Assert.True(a.IsDisposed);
        Assert.False(b.IsDisposed);
    }
}
