using System.Drawing;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="FullscreenPolicy"/> es PURA: con la foto de la ventana en primer plano decide si el notch
/// debe esconderse. Pantalla completa = ventana SIN caption que cubre el monitor del notch (juegos
/// borderless: Valorant/Fortnite) o QUNS 2/3/4 en ese monitor. Una maximizada normal NO cuenta.
/// </summary>
public class FullscreenPolicyTests
{
    private static readonly IntPtr NotchMon = new(1);
    private static readonly IntPtr OtherMon = new(2);
    private static readonly Rectangle Monitor = new(0, 0, 1920, 1080);
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1032); // sin la barra de tareas

    private const int QunsAcceptsNotifications = 5;
    private const int QunsBusy = 2;
    private const int QunsD3D = 3;

    private static ForegroundInfo Fg(Rectangle bounds, bool caption = false, bool shell = false,
                                     bool cloaked = false, bool own = false, IntPtr? mon = null) =>
        new(bounds, caption, shell, cloaked, own, mon ?? NotchMon);

    [Fact]
    public void Borderless_window_covering_notch_monitor_is_fullscreen()
    {
        Assert.True(FullscreenPolicy.IsFullscreen(Fg(Monitor), Monitor, NotchMon, QunsAcceptsNotifications));
        // Algunos juegos se pasan un píxel por cada lado: sigue cubriendo.
        var bigger = Rectangle.Inflate(Monitor, 1, 1);
        Assert.True(FullscreenPolicy.IsFullscreen(Fg(bigger), Monitor, NotchMon, QunsAcceptsNotifications));
    }

    [Fact]
    public void Maximized_window_with_caption_is_not()
    {
        // Maximizada normal: ocupa el área de trabajo y tiene caption.
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(WorkArea, caption: true), Monitor, NotchMon, QunsAcceptsNotifications));
        // Aunque (barra auto-oculta) llegue a cubrir el monitor entero, con caption no es pantalla completa.
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(Monitor, caption: true), Monitor, NotchMon, QunsAcceptsNotifications));
        // Borderless que NO cubre el monitor (ventana sin marco tipo widget) tampoco.
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(WorkArea), Monitor, NotchMon, QunsAcceptsNotifications));
    }

    [Fact]
    public void Covering_another_monitor_is_not()
    {
        var right = new Rectangle(1920, 0, 2560, 1440);
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(right, mon: OtherMon), Monitor, NotchMon, QunsAcceptsNotifications));
    }

    [Fact]
    public void Desktop_shell_window_is_not()
    {
        // Progman/WorkerW cubren el monitor sin caption, pero son el escritorio.
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(Monitor, shell: true), Monitor, NotchMon, QunsAcceptsNotifications));
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(Monitor, shell: true), Monitor, NotchMon, QunsD3D));
    }

    [Fact]
    public void Cloaked_or_own_window_is_not()
    {
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(Monitor, cloaked: true), Monitor, NotchMon, QunsAcceptsNotifications));
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(Monitor, own: true), Monitor, NotchMon, QunsAcceptsNotifications));
    }

    [Fact]
    public void Quns_D3D_on_same_monitor_is_fullscreen_even_if_rect_smaller()
    {
        // D3D exclusivo con resolución menor o con caption: QUNS manda.
        var small = new Rectangle(0, 0, 1280, 720);
        Assert.True(FullscreenPolicy.IsFullscreen(Fg(small), Monitor, NotchMon, QunsD3D));
        Assert.True(FullscreenPolicy.IsFullscreen(Fg(small, caption: true), Monitor, NotchMon, 4)); // presentación
    }

    [Fact]
    public void Quns_busy_on_other_monitor_is_not()
    {
        var right = new Rectangle(1920, 0, 2560, 1440);
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(right, mon: OtherMon), Monitor, NotchMon, QunsBusy));
        Assert.False(FullscreenPolicy.IsFullscreen(Fg(right, mon: OtherMon), Monitor, NotchMon, QunsD3D));
    }
}
