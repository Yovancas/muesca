using System.Runtime.InteropServices;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Log real del 3 y 5-oct-2026: tras un cierre de la ventana del panel, cada clic en el icono daba
/// ObjectDisposedException y el panel no volvía a abrirse hasta reiniciar ClaudeBar. El panel no debe destruirse
/// nunca por un cierre (Alt+F4 o WM_CLOSE), por muchos que lleguen ni en qué orden: solo se oculta.
/// </summary>
public class DashboardCloseTests
{
    private const int WM_CLOSE = 0x0010;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;
    private const int WM_QUERYENDSESSION = 0x0011;
    private const int WM_ENDSESSION = 0x0016;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    [Fact]
    public void External_wm_close_only_hides_the_panel()
    {
        RunSta(() =>
        {
            using var form = new DashboardForm();
            var hwnd = form.Handle;

            SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            Assert.False(form.IsDisposed);
            Assert.True(form.IsHandleCreated);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void Alt_f4_hides_the_panel_without_disposing_it()
    {
        RunSta(() =>
        {
            using var form = new DashboardForm();
            var hwnd = form.Handle;

            SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero); // Alt+F4 / menú de sistema

            Assert.False(form.IsDisposed);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void Repeated_closes_of_any_kind_never_dispose_the_panel()
    {
        // Revisión adversarial: con la versión que clasificaba por CloseReason, el motivo del PRIMER cierre se quedaba
        // pegado y un WM_CLOSE posterior heredaba otro motivo (p. ej. WindowsShutDown tras un apagado cancelado).
        RunSta(() =>
        {
            using var form = new DashboardForm();
            var hwnd = form.Handle;

            SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero);
            SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            SendMessage(hwnd, WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);
            SendMessage(hwnd, WM_ENDSESSION, IntPtr.Zero, IntPtr.Zero); // apagado cancelado (wParam = FALSE)
            SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero);

            Assert.False(form.IsDisposed);
            Assert.True(form.IsHandleCreated);
        });
    }

    [Fact]
    public void Dispose_still_releases_the_panel_on_app_exit()
    {
        RunSta(() =>
        {
            var form = new DashboardForm();
            _ = form.Handle;
            form.Dispose();
            Assert.True(form.IsDisposed);
        });
    }
}
