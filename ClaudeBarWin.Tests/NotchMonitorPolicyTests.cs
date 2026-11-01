using System.Drawing;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Modo de monitor del notch (29-oct): <see cref="ForegroundFilter"/>, <see cref="ForegroundDebounce"/>,
/// <see cref="NotchMonitorChoice"/>, <see cref="NotchMonitorPolicy"/> y <see cref="NotchAlertRouting"/> son PUROS: ventanas,
/// monitores e instantes de mentira, sin Win32.
/// </summary>
public class NotchMonitorPolicyTests
{
    private const string Main = @"\\.\DISPLAY1", Right = @"\\.\DISPLAY2", Left = @"\\.\DISPLAY3";
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(10);

    private static TimeSpan Ms(double ms) => T0 + TimeSpan.FromMilliseconds(ms);

    private static ForegroundWindow Win(int hwnd, string? monitor, string cls = "Chrome_WidgetWin_1", bool visible = true,
        bool minimized = false, bool cloaked = false, bool own = false) =>
        new(new IntPtr(hwnd), monitor, cls, visible, minimized, cloaked, own);

    // ---------------------------------------------------------------- ventanas ignoradas

    [Fact]
    public void A_normal_visible_window_on_a_monitor_counts()
    {
        Assert.True(ForegroundFilter.Counts(Win(1, Right)));
        Assert.True(ForegroundFilter.Counts(Win(2, Main, cls: "CASCADIA_HOSTING_WINDOW_CLASS")));   // Windows Terminal
        Assert.True(ForegroundFilter.Counts(Win(3, Main, cls: "ApplicationFrameWindow")));          // app UWP
    }

    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("MultitaskingViewFrame")]          // Alt+Tab / vista de tareas
    [InlineData("XamlExplorerHostIslandWindow")]   // Alt+Tab de Windows 11
    [InlineData("Windows.UI.Core.CoreWindow")]     // Inicio, búsqueda, centro de notificaciones
    public void Shell_windows_are_ignored(string cls) =>
        Assert.False(ForegroundFilter.Counts(Win(1, Right, cls)));

    [Fact]
    public void Own_invisible_minimized_cloaked_or_monitorless_windows_are_ignored()
    {
        Assert.False(ForegroundFilter.Counts(Win(1, Right, own: true)));        // el panel de ClaudeBar
        Assert.False(ForegroundFilter.Counts(Win(1, Right, visible: false)));
        Assert.False(ForegroundFilter.Counts(Win(1, Right, minimized: true)));
        Assert.False(ForegroundFilter.Counts(Win(1, Right, cloaked: true)));    // otro escritorio virtual
        Assert.False(ForegroundFilter.Counts(Win(1, null)));
        Assert.False(ForegroundFilter.Counts(Win(0, Right)));                   // sin primer plano
    }

    // ---------------------------------------------------------------- debounce

    [Fact]
    public void The_monitor_settles_only_after_300_ms()
    {
        var d = new ForegroundDebounce();
        Assert.Equal(300, ForegroundDebounce.Delay.TotalMilliseconds);
        Assert.True(d.OnForeground(Win(1, Right), Ms(0)));
        Assert.Equal(TimeSpan.FromMilliseconds(300), d.NextWakeIn(Ms(0)));
        Assert.Null(d.Tick(Ms(299)));
        Assert.Equal(Right, d.Tick(Ms(300)));
        Assert.Equal(Right, d.Settled);
        Assert.Null(d.NextWakeIn(Ms(300)));                // nada pendiente: sin despertares (0 % CPU)
        Assert.Null(d.Tick(Ms(10_000)));
    }

    [Fact]
    public void Alt_tab_through_several_windows_settles_only_the_last_one()
    {
        var d = new ForegroundDebounce();
        d.OnForeground(Win(1, Right), Ms(0));
        d.OnForeground(Win(2, Left), Ms(120));
        d.OnForeground(Win(3, Main), Ms(250));             // cada una reinicia la cuenta
        Assert.Null(d.Tick(Ms(420)));
        Assert.Equal(TimeSpan.FromMilliseconds(130), d.NextWakeIn(Ms(420)));
        Assert.Equal(Main, d.Tick(Ms(550)));
    }

    [Fact]
    public void Ignored_windows_neither_cancel_nor_restart_the_wait()
    {
        var d = new ForegroundDebounce();
        d.OnForeground(Win(1, Right), Ms(0));
        Assert.True(d.OnForeground(Win(9, Main, "Shell_TrayWnd"), Ms(200)));   // clic en la barra del otro monitor
        Assert.Equal(TimeSpan.FromMilliseconds(100), d.NextWakeIn(Ms(200)));
        Assert.Equal(Right, d.Tick(Ms(300)));

        // Sin nada pendiente, una ignorada no deja nada pendiente: el notch se queda donde estaba.
        Assert.False(d.OnForeground(Win(8, Main, "Progman"), Ms(1000)));
        Assert.False(d.OnForeground(Win(7, Main, own: true), Ms(1000)));
        Assert.Null(d.Tick(Ms(5000)));
        Assert.Equal(Right, d.Settled);
    }

    [Fact]
    public void Coming_back_to_the_settled_window_cancels_and_is_not_news()
    {
        var d = new ForegroundDebounce();
        d.Seed(Win(1, Main));
        Assert.Equal(Main, d.Settled);
        Assert.False(d.OnForeground(Win(1, Main), Ms(0)));   // misma ventana, mismo monitor (se movió un píxel)
        d.OnForeground(Win(2, Right), Ms(100));
        Assert.False(d.OnForeground(Win(1, Main), Ms(200)));  // volvió antes de asentarse
        Assert.Null(d.Tick(Ms(1000)));

        // La misma ventana arrastrada a otro monitor SÍ es nueva; otra ventana en el mismo monitor, también.
        Assert.True(d.OnForeground(Win(1, Right), Ms(2000)));
        Assert.Equal(Right, d.Tick(Ms(2300)));
        Assert.True(d.OnForeground(Win(5, Right), Ms(3000)));
        Assert.Equal(Right, d.Tick(Ms(3300)));
    }

    [Fact]
    public void Seed_ignores_windows_that_do_not_count()
    {
        var d = new ForegroundDebounce();
        d.Seed(Win(1, Main, "Progman"));
        Assert.Null(d.Settled);
        // Entrar en el modo desde el panel de Ajustes (ventana propia): sin punto de partida viejo; el host usa el de casa.
        d.Seed(Win(2, Right));
        d.OnForeground(Win(3, Left), Ms(0));
        d.Seed(Win(4, Main, own: true));
        Assert.Null(d.Settled);
        Assert.False(d.IsPending);
        Assert.Null(d.Tick(Ms(1000)));
    }

    // ---------------------------------------------------------------- no moverse con el notch ocupado

    [Theory]
    [InlineData(NotchMode.Expanded, false, true)]
    [InlineData(NotchMode.Peek, false, true)]
    [InlineData(NotchMode.Permission, false, true)]
    [InlineData(NotchMode.Reminder, false, true)]
    [InlineData(NotchMode.Idle, true, true)]           // puntero encima
    [InlineData(NotchMode.Idle, false, false)]
    [InlineData(NotchMode.Working, false, false)]
    [InlineData(NotchMode.Completed, false, false)]
    [InlineData(NotchMode.Minimal, false, false)]
    [InlineData(NotchMode.Hidden, false, false)]       // oculto por pantalla completa: puede mudarse
    public void Follow_mode_holds_its_place_while_expanded_hovered_or_with_buttons(NotchMode mode, bool pointer, bool holds) =>
        Assert.Equal(holds, NotchMonitorPolicy.HoldsPlace(mode, pointer));

    [Theory]
    [InlineData(NotchMode.Expanded, false, true)]
    [InlineData(NotchMode.Peek, false, true)]
    [InlineData(NotchMode.Idle, true, true)]
    [InlineData(NotchMode.Permission, false, false)]   // el aviso se ve en todos: sus botones van con la ventana activa
    [InlineData(NotchMode.Reminder, false, false)]
    [InlineData(NotchMode.Idle, false, false)]
    public void All_monitors_primary_is_in_use_only_with_the_pointer_or_expanded(NotchMode mode, bool pointer, bool inUse) =>
        Assert.Equal(inUse, NotchMonitorPolicy.InUse(mode, pointer));

    [Fact]
    public void A_busy_notch_waits_and_moves_when_released()
    {
        var c = new NotchMonitorChoice(Main);
        Assert.False(c.Request(Right, busy: true));
        Assert.Equal(Main, c.Current);
        Assert.Equal(Right, c.Pending);
        Assert.False(c.Request(Left, busy: true));          // gana el último pedido
        Assert.Equal(Left, c.Pending);
        Assert.True(c.Release());
        Assert.Equal(Left, c.Current);
        Assert.Null(c.Pending);
        Assert.False(c.Release());                          // nada más que aplicar
    }

    [Fact]
    public void Asking_for_the_current_monitor_cancels_what_was_waiting()
    {
        var c = new NotchMonitorChoice(Main);
        c.Request(Right, busy: true);
        Assert.False(c.Request(Main, busy: true));          // volviste a una ventana del monitor del notch
        Assert.Null(c.Pending);
        Assert.False(c.Release());
        Assert.Equal(Main, c.Current);
        Assert.True(c.Request(Right, busy: false));         // libre: al momento
        Assert.Equal(Right, c.Current);
        Assert.False(c.Request(Right.ToLowerInvariant(), busy: false));   // sin distinguir mayúsculas
    }

    [Fact]
    public void A_click_forces_the_choice_and_drops_what_was_waiting()
    {
        var c = new NotchMonitorChoice(Main);
        c.Request(Right, busy: true);
        Assert.True(c.Force(Left));
        Assert.Equal(Left, c.Current);
        Assert.Null(c.Pending);
        Assert.False(c.Force(Left));
    }

    [Fact]
    public void A_disconnected_monitor_moves_the_choice_to_the_fallback_at_once()
    {
        var c = new NotchMonitorChoice(Right);
        c.Request(Left, busy: true);
        Assert.True(c.Retarget(new[] { Main }, Main));       // se desenchufaron los dos
        Assert.Equal(Main, c.Current);
        Assert.Null(c.Pending);
        Assert.False(c.Retarget(new[] { Main, Right }, Main));

        var primary = new NotchMonitorChoice("");             // "" = el principal: siempre existe
        Assert.False(primary.Retarget(new[] { Right }, Right));
    }

    [Fact]
    public void Follow_mode_does_not_follow_into_a_fullscreen_monitor_when_it_would_hide_there()
    {
        Assert.False(NotchMonitorPolicy.FollowsInto(Right, fullscreenMonitor: Right, hideInFullscreen: true));
        Assert.False(NotchMonitorPolicy.FollowsInto(Right.ToLowerInvariant(), Right, true));
        Assert.True(NotchMonitorPolicy.FollowsInto(Right, fullscreenMonitor: Right, hideInFullscreen: false));   // se vería igual
        Assert.True(NotchMonitorPolicy.FollowsInto(Right, fullscreenMonitor: Main, hideInFullscreen: true));
        Assert.True(NotchMonitorPolicy.FollowsInto(Right, fullscreenMonitor: null, hideInFullscreen: true));
    }

    [Fact]
    public void Follow_mode_steps_back_when_the_followed_window_goes_fullscreen()
    {
        var connected = new[] { Main, Right };
        Assert.Equal(Main, NotchMonitorPolicy.StepBackFrom(hiddenForFullscreen: true, hideInFullscreen: true, previous: Main, Right, connected));
        Assert.Null(NotchMonitorPolicy.StepBackFrom(false, true, Main, Right, connected));    // sigue a la vista
        Assert.Null(NotchMonitorPolicy.StepBackFrom(true, false, Main, Right, connected));    // no se oculta en pantalla completa
        Assert.Null(NotchMonitorPolicy.StepBackFrom(true, true, null, Right, connected));     // no vino de ningún sitio
        Assert.Null(NotchMonitorPolicy.StepBackFrom(true, true, Right, Right, connected));    // un solo monitor: se queda oculto
        Assert.Null(NotchMonitorPolicy.StepBackFrom(true, true, Left, Right, connected));     // el de antes se desconectó
    }

    // ---------------------------------------------------------------- principal efectivo

    private static NotchSlot Slot(string m, bool hidden = false, bool inUse = false) => new(m, hidden, inUse);

    [Fact]
    public void Effective_primary_is_the_chosen_monitor()
    {
        var slots = new[] { Slot(Main), Slot(Right), Slot(Left) };
        Assert.Equal(Right, NotchMonitorPolicy.EffectivePrimary(slots, Right, Main));
        Assert.Equal(Left, NotchMonitorPolicy.EffectivePrimary(slots, Left.ToLowerInvariant(), Main));
    }

    [Fact]
    public void Effective_primary_falls_back_to_home_then_to_the_first_visible()
    {
        // El elegido está a pantalla completa (oculto): sus botones no los vería nadie.
        var slots = new[] { Slot(Main), Slot(Right, hidden: true), Slot(Left) };
        Assert.Equal(Main, NotchMonitorPolicy.EffectivePrimary(slots, Right, Main));
        Assert.Equal(Left, NotchMonitorPolicy.EffectivePrimary(new[] { Slot(Main, hidden: true), Slot(Right, hidden: true), Slot(Left) },
            Right, Main));
        // Un monitor elegido que se desconectó: el de casa.
        Assert.Equal(Main, NotchMonitorPolicy.EffectivePrimary(slots, @"\\.\DISPLAY9", Main));
        // Sin elección todavía: el de casa.
        Assert.Equal(Main, NotchMonitorPolicy.EffectivePrimary(slots, null, Main));
        // Ninguno se ve: ninguno es interactivo.
        Assert.Null(NotchMonitorPolicy.EffectivePrimary(new[] { Slot(Main, hidden: true) }, Main, Main));
        Assert.Null(NotchMonitorPolicy.EffectivePrimary(Array.Empty<NotchSlot>(), Main, Main));
    }

    [Fact]
    public void Effective_primary_follows_the_active_window_but_not_while_in_use()
    {
        var chosen = new NotchMonitorChoice(Main);
        var slots = new[] { Slot(Main, inUse: true), Slot(Right) };
        bool busy = slots.Single(s => s.Monitor == chosen.Current).InUse;
        Assert.False(chosen.Request(Right, busy));                         // con el ratón encima del principal: espera
        Assert.Equal(Main, NotchMonitorPolicy.EffectivePrimary(slots, chosen.Current, Main));
        Assert.True(chosen.Release());                                     // el puntero salió
        Assert.Equal(Right, NotchMonitorPolicy.EffectivePrimary(slots, chosen.Current, Main));
        Assert.True(chosen.Force(Main));                                   // clic en el de solo lectura del principal
        Assert.Equal(Main, NotchMonitorPolicy.EffectivePrimary(slots, chosen.Current, Main));
    }

    // ---------------------------------------------------------------- botones y sonido

    private static NotchAlert Alert(NotchAlertKind kind, string? requestId = null) =>
        new(kind, "s1", "web", "Bash: npm test", DateTime.UtcNow, NotchAlert.DefaultDuration(kind), requestId);

    [Fact]
    public void Only_the_interactive_window_gets_buttons_and_sound()
    {
        var perm = Alert(NotchAlertKind.Permission, "req-1");
        var reminder = Alert(NotchAlertKind.Reminder);
        foreach (var a in new[] { perm, reminder })
        {
            Assert.True(NotchAlertRouting.ButtonsHere(a, interactive: true));
            Assert.False(NotchAlertRouting.ReadOnlyHere(a, interactive: true));
            Assert.False(NotchAlertRouting.ButtonsHere(a, interactive: false));
            Assert.True(NotchAlertRouting.ReadOnlyHere(a, interactive: false));   // «Espera tu OK · proyecto», sin botones
        }
        Assert.True(NotchAlertRouting.MayChime(interactive: true));
        Assert.False(NotchAlertRouting.MayChime(interactive: false));            // nunca dos campanitas
    }

    [Fact]
    public void Alerts_without_buttons_look_the_same_everywhere()
    {
        foreach (var a in new[] { Alert(NotchAlertKind.Permission), Alert(NotchAlertKind.Completed),
                     Alert(NotchAlertKind.QuotaAlert), Alert(NotchAlertKind.QuotaHud) })
        {
            Assert.False(NotchAlertRouting.HasButtons(a));
            Assert.False(NotchAlertRouting.ReadOnlyHere(a, interactive: false));
        }
        Assert.False(NotchAlertRouting.HasButtons(null));
    }

    [Fact]
    public void Exactly_one_window_sounds_across_all_monitors()
    {
        // Tres monitores, el principal efectivo es el de la derecha: solo ese suena y lleva botones.
        var slots = new[] { Slot(Main), Slot(Right), Slot(Left) };
        string? primary = NotchMonitorPolicy.EffectivePrimary(slots, Right, Main);
        var perm = Alert(NotchAlertKind.Permission, "req-1");
        var sounding = slots.Where(s => NotchAlertRouting.MayChime(s.Monitor == primary)
                                        && NotchSound.ShouldPlay(perm, soundEnabled: true, fullscreen: false)).ToList();
        Assert.Single(sounding);
        Assert.Equal(Right, sounding[0].Monitor);
        Assert.Single(slots, s => NotchAlertRouting.ButtonsHere(perm, s.Monitor == primary));
    }

    // ---------------------------------------------------------------- altas y bajas de monitores

    [Fact]
    public void All_monitors_wants_one_window_per_connected_monitor()
    {
        Assert.Equal(new[] { Main, Right }, NotchMonitorPolicy.Desired(NotchMonitorMode.AllMonitors, new[] { Main, Right, Main }, Main));
        Assert.Equal(new[] { Right }, NotchMonitorPolicy.Desired(NotchMonitorMode.Fixed, new[] { Main, Right }, Right));
        Assert.Equal(new[] { Left }, NotchMonitorPolicy.Desired(NotchMonitorMode.FollowActive, new[] { Main, Left }, Left));
    }

    [Fact]
    public void Connecting_a_monitor_adds_a_window_and_keeps_the_rest()
    {
        var (remove, add) = NotchMonitorPolicy.Reconcile(new[] { Main, Right }, new[] { Main, Right, Left });
        Assert.Empty(remove);
        Assert.Equal(new[] { Left }, add);
    }

    [Fact]
    public void Disconnecting_a_monitor_removes_only_its_window()
    {
        var (remove, add) = NotchMonitorPolicy.Reconcile(new[] { Main, Right, Left }, new[] { Main, Left });
        Assert.Equal(new[] { Right }, remove);
        Assert.Empty(add);
        // Sin distinguir mayúsculas: un nombre GDI igual no recrea su ventana.
        var same = NotchMonitorPolicy.Reconcile(new[] { Main.ToLowerInvariant() }, new[] { Main });
        Assert.Empty(same.Remove);
        Assert.Empty(same.Add);
    }

    [Fact]
    public void Going_back_to_one_window_keeps_the_one_already_on_the_target()
    {
        Assert.Equal(1, NotchMonitorPolicy.KeepForSingle(new[] { Main, Right, Left }, Right));
        Assert.Equal(0, NotchMonitorPolicy.KeepForSingle(new[] { Main, Right }, Left));   // ninguna allí: la primera, que se mueve
        Assert.Equal(-1, NotchMonitorPolicy.KeepForSingle(Array.Empty<string>(), Main));
    }

    // ---------------------------------------------------------------- pantalla completa monitor a monitor

    private static readonly IntPtr MonA = new(1), MonB = new(2);
    private static readonly Rectangle BoundsA = new(0, 0, 1920, 1080), BoundsB = new(1920, 0, 2560, 1440);

    [Fact]
    public void A_game_on_one_monitor_hides_only_that_monitors_notch()
    {
        var game = new ForegroundInfo(BoundsB, HasCaption: false, IsShellWindow: false, IsCloaked: false, IsOwnProcess: false, MonB);
        Assert.Equal(MonB, FullscreenPolicy.FullscreenMonitor(game, BoundsB, qunsState: 5));
        Assert.True(FullscreenPolicy.IsFullscreen(game, BoundsB, MonB, 5));
        Assert.False(FullscreenPolicy.IsFullscreen(game, BoundsA, MonA, 5));   // el notch del otro monitor sigue
        // D3D exclusivo / presentación: el monitor del primer plano, aunque el rect sea menor.
        var d3d = game with { Bounds = new Rectangle(1920, 0, 1280, 720) };
        Assert.Equal(MonB, FullscreenPolicy.FullscreenMonitor(d3d, BoundsB, FullscreenPolicy.QunsRunningD3DFullScreen));
        Assert.False(FullscreenPolicy.IsFullscreen(d3d, BoundsA, MonA, FullscreenPolicy.QunsRunningD3DFullScreen));
    }

    [Fact]
    public void No_monitor_is_fullscreen_with_a_maximized_window_or_the_desktop()
    {
        var maximized = new ForegroundInfo(new Rectangle(0, 0, 1920, 1032), true, false, false, false, MonA);
        Assert.Equal(IntPtr.Zero, FullscreenPolicy.FullscreenMonitor(maximized, BoundsA, 5));
        var desktop = new ForegroundInfo(BoundsA, false, IsShellWindow: true, false, false, MonA);
        Assert.Equal(IntPtr.Zero, FullscreenPolicy.FullscreenMonitor(desktop, BoundsA, FullscreenPolicy.QunsBusy));
        var noMonitor = new ForegroundInfo(BoundsA, false, false, false, false, IntPtr.Zero);
        Assert.Equal(IntPtr.Zero, FullscreenPolicy.FullscreenMonitor(noMonitor, BoundsA, FullscreenPolicy.QunsBusy));
    }
}
