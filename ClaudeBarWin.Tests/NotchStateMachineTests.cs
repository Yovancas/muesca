using ClaudeBarWin.Models;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="NotchStateMachine"/>: PURA, el instante llega por parámetro. Decide el modo del notch a
/// partir de sesiones, señales, cuota, puntero y pantalla completa, y dice cuándo hay que despertarla
/// (<see cref="NotchStateMachine.NextWakeIn"/> = <c>null</c> en reposo: 0 % CPU).
/// </summary>
public class NotchStateMachineTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static NotchStateMachine New(bool openOnClick = false, bool minimal = false, bool hideFs = true,
        TimeSpan? autoMinimal = null) =>
        new(new NotchSettings(openOnClick, minimal, hideFs, autoMinimal));

    // ── Encoger a la rayita tras un rato sin actividad (pedido del 28-oct) ──────────────────────────────

    private static readonly TimeSpan Auto = TimeSpan.FromSeconds(30);

    [Fact]
    public void Idle_shrinks_to_Minimal_after_the_quiet_delay()
    {
        var m = New(autoMinimal: Auto);
        m.OnSessions(Array.Empty<SessionState>(), T0);
        Assert.Equal(NotchMode.Idle, m.Mode);
        Assert.Equal(Auto, m.NextWakeIn(T0));
        m.Tick(T0 + Auto - TimeSpan.FromSeconds(1));
        Assert.Equal(NotchMode.Idle, m.Mode);
        m.Tick(T0 + Auto);
        Assert.Equal(NotchMode.Minimal, m.Mode);
        Assert.Null(m.NextWakeIn(T0 + Auto));                 // encogido: nada más que despertar (0 % CPU)
    }

    /// <summary>Pedido del 29-oct: con una canción con letra sonando (letra compacta a la vista) el notch no se encoge;
    /// al acabar la letra (pausa, otra canción sin letra) el reloj de inactividad empieza de cero.</summary>
    [Fact]
    public void Lyrics_on_screen_keep_it_awake_and_restart_the_quiet_clock()
    {
        var m = New(autoMinimal: Auto);
        m.OnSessions(Array.Empty<SessionState>(), T0);
        m.SetKeepAwake(true, T0 + TimeSpan.FromSeconds(5));
        m.Tick(T0 + Auto + Auto);
        Assert.Equal(NotchMode.Idle, m.Mode);
        Assert.Null(m.NextWakeIn(T0 + Auto + Auto));          // nada que programar mientras dure

        var t1 = T0 + TimeSpan.FromMinutes(3);
        m.SetKeepAwake(false, t1);
        m.Tick(t1 + Auto - TimeSpan.FromSeconds(1));
        Assert.Equal(NotchMode.Idle, m.Mode);
        m.Tick(t1 + Auto);
        Assert.Equal(NotchMode.Minimal, m.Mode);

        m.SetKeepAwake(true, t1 + Auto + TimeSpan.FromSeconds(1));   // ya encogido: vuelve la letra → se despierta
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    /// <summary>Pedido del 29-oct: la letra compacta sigue en la Vista previa solo si debajo está el reposo (no Trabajando
    /// ni «Minimalista»), con el puntero encima y durante la gracia de salida.</summary>
    [Fact]
    public void Peek_over_idle_only_with_the_idle_underneath()
    {
        var m = New(openOnClick: true);
        m.OnSessions(Array.Empty<SessionState>(), T0);
        Assert.False(m.PeekOverIdle);                         // en reposo, sin puntero
        m.OnPointerEnter(T0);
        Assert.Equal(NotchMode.Peek, m.Mode);
        Assert.True(m.PeekOverIdle);
        m.OnPointerLeave(T0 + TimeSpan.FromSeconds(1));
        Assert.True(m.PeekOverIdle);                          // gracia de salida: sigue siendo la Vista previa
        m.Tick(T0 + TimeSpan.FromSeconds(5));
        Assert.Equal(NotchMode.Idle, m.Mode);
        Assert.False(m.PeekOverIdle);

        m.OnSessions(new[] { S("a", SessionPhase.Processing) }, T0 + TimeSpan.FromSeconds(6));
        m.OnPointerEnter(T0 + TimeSpan.FromSeconds(6));
        Assert.Equal(NotchMode.Peek, m.Mode);
        Assert.False(m.PeekOverIdle);                         // debajo, Trabajando

        var minimal = New(openOnClick: true, minimal: true);
        minimal.OnSessions(Array.Empty<SessionState>(), T0);
        minimal.OnPointerEnter(T0);
        Assert.Equal(NotchMode.Peek, minimal.Mode);
        Assert.False(minimal.PeekOverIdle);                   // debajo, la raya
    }

    [Fact]
    public void Claude_working_brings_it_back_and_the_quiet_clock_restarts()
    {
        var m = New(autoMinimal: Auto);
        m.OnSessions(Array.Empty<SessionState>(), T0);
        m.Tick(T0 + Auto);
        Assert.Equal(NotchMode.Minimal, m.Mode);

        var t1 = T0 + TimeSpan.FromMinutes(5);
        m.OnSessions(new[] { S("a", SessionPhase.Processing) }, t1);
        Assert.Equal(NotchMode.Working, m.Mode);
        var t2 = t1 + TimeSpan.FromMinutes(2);
        m.OnSessions(new[] { S("a", SessionPhase.WaitingForInput) }, t2);
        Assert.Equal(NotchMode.Idle, m.Mode);                 // no vuelve a encogerse de golpe
        m.Tick(t2 + Auto);
        Assert.Equal(NotchMode.Minimal, m.Mode);
    }

    [Fact]
    public void Alerts_and_hover_wake_it_up()
    {
        var m = New(autoMinimal: Auto);
        m.OnSessions(Array.Empty<SessionState>(), T0);
        m.Tick(T0 + Auto);

        var t1 = T0 + TimeSpan.FromMinutes(1);
        m.OnQuotaCrossing(new QuotaCrossing(QuotaWindowKind.FiveHour, 95, 96, null), t1);
        Assert.Equal(NotchMode.QuotaAlert, m.Mode);
        var after = t1 + TimeSpan.FromSeconds(10);
        m.Tick(after);
        Assert.Equal(NotchMode.Idle, m.Mode);                 // tras el aviso, despierto otra vez

        m.Tick(after + Auto);
        Assert.Equal(NotchMode.Minimal, m.Mode);
        var t2 = after + Auto + TimeSpan.FromSeconds(1);
        m.OnPointerEnter(t2);
        Assert.Equal(NotchMode.Peek, m.Mode);
        m.OnPointerLeave(t2 + TimeSpan.FromMilliseconds(100));
        m.Tick(t2 + TimeSpan.FromSeconds(1));
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    [Fact]
    public void Without_auto_minimal_it_never_wakes_in_idle()
    {
        var m = New();
        m.OnSessions(Array.Empty<SessionState>(), T0);
        Assert.Null(m.NextWakeIn(T0));
        m.Tick(T0 + TimeSpan.FromHours(3));
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    private static SessionState S(string id, SessionPhase phase) => new()
    {
        SessionId = id, Cwd = "C:\\p\\" + id, ProjectName = id, Phase = phase,
    };

    [Fact]
    public void Idle_without_sessions_activity()
    {
        var m = New();
        Assert.Equal(NotchMode.Idle, m.Mode);
        m.OnSessions(new[] { S("a", SessionPhase.WaitingForInput), S("b", SessionPhase.Idle) }, T0);
        Assert.Equal(NotchMode.Idle, m.Mode);
        m.OnSessions(Array.Empty<SessionState>(), T0);
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    [Fact]
    public void Processing_session_gives_Working()
    {
        var m = New();
        m.OnSessions(new[] { S("a", SessionPhase.Idle), S("b", SessionPhase.Processing) }, T0);
        Assert.Equal(NotchMode.Working, m.Mode);
        m.OnSessions(new[] { S("b", SessionPhase.Compacting) }, T0);
        Assert.Equal(NotchMode.Working, m.Mode);
    }

    [Fact]
    public void Hover_250ms_expands_when_not_OpenOnClick()
    {
        var m = New();
        m.OnPointerEnter(T0);
        m.Tick(T0.AddMilliseconds(200));
        Assert.Equal(NotchMode.Peek, m.Mode);
        Assert.Equal(TimeSpan.FromMilliseconds(50), m.NextWakeIn(T0.AddMilliseconds(200)));
        m.Tick(T0.AddMilliseconds(260));
        Assert.Equal(NotchMode.Expanded, m.Mode);
        Assert.Null(m.NextWakeIn(T0.AddMilliseconds(260)));
    }

    // ── Arrastrar a la bandeja (F5 · B4) ────────────────────────────────────────────────────────────────

    [Fact]
    public void Drag_enter_expands_at_once_even_with_OpenOnClick()
    {
        var m = New(openOnClick: true, autoMinimal: TimeSpan.FromSeconds(15));
        m.OnSessions(Array.Empty<SessionState>(), T0);
        m.Tick(T0.AddSeconds(20));
        Assert.Equal(NotchMode.Minimal, m.Mode);               // encogido a la raya: arrastrar lo despierta
        m.OnDragEnter(T0.AddSeconds(20));
        Assert.Equal(NotchMode.Expanded, m.Mode);
        Assert.Null(m.NextWakeIn(T0.AddSeconds(20)));          // abierto mientras se arrastra encima: nada que despertar
    }

    [Fact]
    public void Drag_leave_collapses_after_the_grace_and_a_drop_keeps_it_open()
    {
        var m = New();
        m.OnDragEnter(T0);
        m.OnPointerLeave(T0.AddSeconds(1));                    // el arrastre salió sin soltar
        Assert.Equal(NotchMode.Expanded, m.Mode);
        m.Tick(T0.AddSeconds(1) + NotchTiming.CollapseGrace);
        Assert.NotEqual(NotchMode.Expanded, m.Mode);

        m.OnDragEnter(T0.AddSeconds(5));                       // soltado: sigue abierto con el puntero encima
        m.Tick(T0.AddSeconds(30));
        Assert.Equal(NotchMode.Expanded, m.Mode);
    }

    [Fact]
    public void OpenOnClick_requires_click()
    {
        var m = New(openOnClick: true);
        m.OnPointerEnter(T0);
        m.Tick(T0.AddSeconds(2));
        Assert.Equal(NotchMode.Peek, m.Mode);
        Assert.Null(m.NextWakeIn(T0.AddSeconds(2)));
        m.OnClick(T0.AddSeconds(2));
        Assert.Equal(NotchMode.Expanded, m.Mode);
        m.OnClick(T0.AddSeconds(3));                 // segundo clic: contrae
        Assert.Equal(NotchMode.Peek, m.Mode);
    }

    [Fact]
    public void Leave_collapses_after_250ms_grace()
    {
        var m = New();
        m.OnPointerEnter(T0);
        m.Tick(T0.AddMilliseconds(300));
        Assert.Equal(NotchMode.Expanded, m.Mode);

        m.OnPointerLeave(T0.AddSeconds(1));
        m.Tick(T0.AddSeconds(1).AddMilliseconds(200));
        Assert.Equal(NotchMode.Expanded, m.Mode);    // gracia
        m.Tick(T0.AddSeconds(1).AddMilliseconds(250));
        Assert.Equal(NotchMode.Idle, m.Mode);
        Assert.Null(m.NextWakeIn(T0.AddSeconds(2)));

        // Volver a entrar durante la gracia cancela el colapso.
        m.OnPointerEnter(T0.AddSeconds(3));
        m.Tick(T0.AddSeconds(3).AddMilliseconds(300));
        m.OnPointerLeave(T0.AddSeconds(4));
        m.OnPointerEnter(T0.AddSeconds(4).AddMilliseconds(100));
        m.Tick(T0.AddSeconds(5));
        Assert.Equal(NotchMode.Expanded, m.Mode);
    }

    [Fact]
    public void Alert_shows_over_Working_and_returns_after_expiry()
    {
        var m = New();
        m.OnSessions(new[] { S("a", SessionPhase.Processing), S("b", SessionPhase.Processing) }, T0);
        m.OnSignal(new TaskCompleted("b", "b", T0), T0);
        Assert.Equal(NotchMode.Completed, m.Mode);
        Assert.Equal("b", m.ActiveAlert!.SessionId);
        Assert.Equal(NotchTiming.Completed, m.NextWakeIn(T0));

        m.Tick(T0 + NotchTiming.Completed);
        Assert.Equal(NotchMode.Working, m.Mode);
        Assert.Null(m.ActiveAlert);
        Assert.Null(m.NextWakeIn(T0 + NotchTiming.Completed));
    }

    [Fact]
    public void Permission_persists_until_cleared_and_quota_crossings_map_to_modes()
    {
        var m = New();
        m.OnSignal(new PermissionPending("a", "a", T0, "Bash", "npm test"), T0);
        Assert.Equal(NotchMode.Permission, m.Mode);
        Assert.Equal("Bash: npm test", m.ActiveAlert!.Detail);
        Assert.Null(m.NextWakeIn(T0));

        m.OnQuotaCrossing(new QuotaCrossing(QuotaWindowKind.FiveHour, 95, 96, null), T0);
        Assert.Equal(NotchMode.Permission, m.Mode);   // permiso > aviso 95
        m.OnSignal(new PermissionCleared("a", "a", T0.AddSeconds(1)), T0.AddSeconds(1));
        Assert.Equal(NotchMode.QuotaAlert, m.Mode);
        m.Tick(T0.AddSeconds(5.5));
        Assert.Equal(NotchMode.Idle, m.Mode);

        m.OnQuotaCrossing(new QuotaCrossing(QuotaWindowKind.SevenDay, 80, 81, null), T0.AddSeconds(6));
        Assert.Equal(NotchMode.QuotaHud, m.Mode);
        Assert.Equal(NotchTiming.QuotaHud, m.NextWakeIn(T0.AddSeconds(6)));
    }

    [Fact]
    public void SessionGone_drops_its_pending_permission()
    {
        var m = New();
        m.OnSignal(new PermissionPending("a", "a", T0, "Bash", null), T0);
        m.OnSignal(new SessionGone("a", "a", T0.AddSeconds(1)), T0.AddSeconds(1));
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    [Fact]
    public void Alert_while_Expanded_stays_Expanded_and_exposes_ActiveAlert()
    {
        var m = New();
        m.OnPointerEnter(T0);
        m.Tick(T0.AddMilliseconds(300));
        m.OnSignal(new TaskCompleted("a", "a", T0.AddSeconds(1)), T0.AddSeconds(1));
        Assert.Equal(NotchMode.Expanded, m.Mode);
        Assert.Equal(NotchAlertKind.Completed, m.ActiveAlert!.Kind);
    }

    [Fact]
    public void Fullscreen_hides_and_restores_previous_mode()
    {
        var m = New();
        m.OnSessions(new[] { S("a", SessionPhase.Processing) }, T0);
        m.OnFullscreen(true);
        Assert.Equal(NotchMode.Hidden, m.Mode);
        Assert.Null(m.NextWakeIn(T0));
        m.OnFullscreen(false);
        Assert.Equal(NotchMode.Working, m.Mode);

        // Con HideInFullscreen apagado no se oculta.
        var keep = New(hideFs: false);
        keep.OnFullscreen(true);
        Assert.Equal(NotchMode.Idle, keep.Mode);
    }

    [Fact]
    public void Alert_arriving_in_fullscreen_waits_until_it_ends()
    {
        var m = New();
        m.OnFullscreen(true);
        m.OnSignal(new TaskCompleted("a", "a", T0), T0);
        Assert.Equal(NotchMode.Hidden, m.Mode);
        Assert.Null(m.NextWakeIn(T0.AddSeconds(30)));
        m.OnFullscreen(false);
        Assert.Equal(TimeSpan.Zero, m.NextWakeIn(T0.AddSeconds(30)));   // pide un Tick inmediato
        m.Tick(T0.AddSeconds(30));
        Assert.Equal(NotchMode.Completed, m.Mode);  // su reloj empieza al verse
        Assert.Equal(NotchTiming.Completed, m.NextWakeIn(T0.AddSeconds(30)));
    }

    [Fact]
    public void Minimal_setting_maps_Idle_to_Minimal()
    {
        var m = New(minimal: true);
        Assert.Equal(NotchMode.Minimal, m.Mode);
        m.OnSessions(new[] { S("a", SessionPhase.Processing) }, T0);
        Assert.Equal(NotchMode.Working, m.Mode);
    }

    [Fact]
    public void NextWakeIn_is_null_when_nothing_pending()
    {
        var m = New();
        Assert.Null(m.NextWakeIn(T0));
        m.OnSessions(new[] { S("a", SessionPhase.Processing) }, T0);
        m.OnSignal(new ToolActivity("a", "a", T0, "Bash", "ls"), T0);
        Assert.Null(m.NextWakeIn(T0));
    }

    // ── Cambio de página con el ratón: la forma encoge bajo el cursor (bug del 30-oct) ─────────────────────

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    /// <summary>Expandido con el puntero encima (hover de 250 ms) en <see cref="T0"/> + 250 ms.</summary>
    private static NotchStateMachine ExpandedByHover()
    {
        var m = New();
        m.OnPointerEnter(T0);
        m.Tick(T0 + NotchTiming.HoverExpand);
        Assert.Equal(NotchMode.Expanded, m.Mode);
        return m;
    }

    /// <summary>El bug: clic en un punto (o rueda) hacia una página más baja, la forma encoge, el cursor queda fuera y el
    /// notch se cerraba a los 250 ms por «ratón fuera». Ahora sigue abierto mientras no pasen los 4 s.</summary>
    [Fact]
    public void User_page_change_that_leaves_the_pointer_outside_does_not_collapse()
    {
        var m = ExpandedByHover();
        var t = T0 + Ms(1000);
        m.OnUserPageChange(t);
        m.OnPointerLeave(t + Ms(200));                 // la forma nueva ya no está bajo el cursor
        m.OnPageShapeSettled(t + Ms(300));
        m.Tick(t + Ms(200) + NotchTiming.CollapseGrace + Ms(1));
        Assert.Equal(NotchMode.Expanded, m.Mode);
        m.Tick(t + NotchTiming.PageChangeHold - Ms(1));
        Assert.Equal(NotchMode.Expanded, m.Mode);
    }

    /// <summary>Sin volver a entrar, a los 4 s del cambio se cierra (y la máquina lo programa: nada de timers en la ventana).</summary>
    [Fact]
    public void User_page_change_collapses_after_the_hold_if_the_pointer_never_returns()
    {
        var m = ExpandedByHover();
        var t = T0 + Ms(1000);
        m.OnUserPageChange(t);
        m.OnPointerLeave(t + Ms(200));
        m.OnPageShapeSettled(t + Ms(300));
        Assert.Equal(NotchTiming.PageChangeHold - Ms(300), m.NextWakeIn(t + Ms(300)));
        m.Tick(t + NotchTiming.PageChangeHold);
        Assert.Equal(NotchMode.Idle, m.Mode);
        Assert.Null(m.NextWakeIn(t + NotchTiming.PageChangeHold));
    }

    /// <summary>Vuelve a entrar en la forma nueva y sale: cierre normal (gracia de siempre), el cambio ya no protege.</summary>
    [Fact]
    public void After_reentering_the_new_shape_leaving_collapses_normally()
    {
        var m = ExpandedByHover();
        var t = T0 + Ms(1000);
        m.OnUserPageChange(t);
        m.OnPointerLeave(t + Ms(200));
        m.OnPageShapeSettled(t + Ms(300));
        m.OnPointerEnter(t + Ms(1000));
        m.OnPointerLeave(t + Ms(1500));
        m.Tick(t + Ms(1500) + NotchTiming.CollapseGrace);
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    /// <summary>Si el cursor sigue dentro de la forma nueva cuando se asienta, salir después es el cierre normal.</summary>
    [Fact]
    public void Pointer_still_inside_when_the_shape_settles_ends_the_protection()
    {
        var m = ExpandedByHover();
        var t = T0 + Ms(1000);
        m.OnUserPageChange(t);
        m.OnPageShapeSettled(t + Ms(300));
        m.OnPointerLeave(t + Ms(800));
        m.Tick(t + Ms(800) + NotchTiming.CollapseGrace);
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    /// <summary>Volver a entrar MIENTRAS la forma aún se anima (la zona es la unión) no basta: decide el asentado.</summary>
    [Fact]
    public void Reentering_during_the_size_animation_does_not_end_the_protection_by_itself()
    {
        var m = ExpandedByHover();
        var t = T0 + Ms(1000);
        m.OnUserPageChange(t);
        m.OnPointerLeave(t + Ms(50));
        m.OnPointerEnter(t + Ms(100));
        m.OnPointerLeave(t + Ms(250));               // vuelve a quedar fuera de la forma nueva al asentarse
        m.OnPageShapeSettled(t + Ms(300));
        m.Tick(t + Ms(250) + NotchTiming.CollapseGrace + Ms(1));
        Assert.Equal(NotchMode.Expanded, m.Mode);
    }

    /// <summary>Un clic suelto sigue cerrando, y el siguiente expandido no hereda la protección.</summary>
    [Fact]
    public void Click_still_collapses_and_the_next_expansion_starts_clean()
    {
        var m = ExpandedByHover();
        var t = T0 + Ms(1000);
        m.OnUserPageChange(t);
        m.OnClick(t + Ms(100));
        Assert.Equal(NotchMode.Peek, m.Mode);
        m.OnClick(t + Ms(200));
        Assert.Equal(NotchMode.Expanded, m.Mode);
        m.OnPointerLeave(t + Ms(300));
        m.Tick(t + Ms(300) + NotchTiming.CollapseGrace);
        Assert.Equal(NotchMode.Idle, m.Mode);
    }

    /// <summary>Sin el expandido abierto un cambio de página no protege nada (p. ej. llega a destiempo).</summary>
    [Fact]
    public void Page_change_outside_the_expanded_is_ignored()
    {
        var m = New();
        m.OnPointerEnter(T0);
        m.OnUserPageChange(T0 + Ms(10));
        m.OnPointerLeave(T0 + Ms(100));
        m.Tick(T0 + Ms(100) + NotchTiming.CollapseGrace);
        Assert.Equal(NotchMode.Idle, m.Mode);
    }
}
