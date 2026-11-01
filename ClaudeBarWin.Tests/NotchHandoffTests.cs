using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El relevo del asistente de bienvenida al notch de verdad: el host marca para aparecer ya asentada (sin crecer desde
/// 44 × 0) solo la ventana del monitor donde el asistente se encogió, y solo al crearse con él; las que vengan después,
/// como siempre.
/// </summary>
public class NotchHandoffTests
{
    private static readonly NotchOptions Opts = new(false, false, true, true, 0, "", false);

    [Fact]
    public void Only_the_window_on_the_wizard_monitor_appears_settled()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            string primary = Screen.PrimaryScreen!.DeviceName;
            using (var host = new NotchHost(hub, Opts, NotchMonitorMode.AllMonitors, Localization.Get("es"), show: false,
                       appearSettledOn: primary))
            {
                Assert.All(host.Windows, w => Assert.Equal(string.Equals(w.MonitorDevice, primary, StringComparison.OrdinalIgnoreCase),
                    w.AppearSettled));
            }
            using (var plain = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Localization.Get("es"), show: false))
                Assert.False(Assert.Single(plain.Windows).AppearSettled);
            using (var elsewhere = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Localization.Get("es"), show: false,
                       appearSettledOn: @"\\.\NO-SUCH-DISPLAY"))
                Assert.False(Assert.Single(elsewhere.Windows).AppearSettled);
        });
    }

    [Fact]
    public void Windows_created_later_appear_as_always()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            string primary = Screen.PrimaryScreen!.DeviceName;
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.FollowActive, Localization.Get("es"), show: false,
                appearSettledOn: primary);
            var first = Assert.Single(host.Windows);
            first.AppearSettled = false;   // lo que haría su OnShown
            host.Apply(Opts, NotchMonitorMode.AllMonitors, Localization.Get("es"));
            Assert.All(host.Windows, w => Assert.False(w.AppearSettled));
        });
    }

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
