using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="NotchHost"/> sin mostrar ventanas (en un hilo STA, como la app): Fijo crea UNA ventana con los ajustes de
/// siempre y sin hooks propios; «todos los monitores» crea una por monitor con una sola interactiva; cambiar de modo en
/// caliente reutiliza la ventana que ya está. Las mudanzas animadas, el hover y la pantalla completa se verifican en vivo.
/// </summary>
public class NotchHostTests
{
    private static readonly NotchOptions Opts = new(false, false, true, true, 0, "", false);

    private static IReadOnlyList<string> Screens() => Screen.AllScreens.Select(s => s.DeviceName).ToList();

    [Fact]
    public void Fixed_is_one_window_with_the_options_as_given_and_no_hooks_of_its_own()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts with { SizePercent = 130 }, NotchMonitorMode.Fixed, Localization.Get("es"), show: false);
            var w = Assert.Single(host.Windows);
            Assert.Equal(Opts with { SizePercent = 130 }, w.Options);   // MonitorDeviceName "" = el principal, como siempre
            Assert.True(w.Interactive);
            Assert.False(w.StrictMonitor);
            Assert.False(host.FollowsForeground);
            Assert.Equal(Screen.PrimaryScreen!.DeviceName, w.MonitorDevice);

            // Ajustes nuevos: la MISMA ventana, como el ApplyOptions de siempre.
            host.Apply(Opts with { Minimal = true }, NotchMonitorMode.Fixed, Localization.Get("en"));
            Assert.Same(w, Assert.Single(host.Windows));
            Assert.True(w.Options.Minimal);
            Assert.Equal(Localization.Get("en"), w.Strings);
        });
    }

    [Fact]
    public void All_monitors_is_one_window_per_monitor_with_exactly_one_interactive()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.AllMonitors, Localization.Get("es"), show: false);
            var screens = Screens();
            Assert.Equal(screens.Count, host.Windows.Count);
            Assert.Equal(screens.OrderBy(s => s), host.Windows.Select(w => w.MonitorDevice).OrderBy(s => s));
            Assert.All(host.Windows, w => Assert.True(w.StrictMonitor));
            Assert.Single(host.Windows, w => w.Interactive);
            Assert.True(host.FollowsForeground);
        });
    }

    [Fact]
    public void Switching_modes_live_keeps_the_window_that_is_already_there()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Localization.Get("es"), show: false);
            var first = Assert.Single(host.Windows);

            host.Apply(Opts, NotchMonitorMode.AllMonitors, Localization.Get("es"));
            Assert.Contains(first, host.Windows);                       // la del principal sigue: sin recrearla
            Assert.Equal(Screens().Count, host.Windows.Count);

            host.Apply(Opts, NotchMonitorMode.FollowActive, Localization.Get("es"));
            var follow = Assert.Single(host.Windows);
            Assert.True(follow.Interactive);
            Assert.False(follow.StrictMonitor);
            Assert.True(host.FollowsForeground);

            host.Apply(Opts, NotchMonitorMode.Fixed, Localization.Get("es"));
            var back = Assert.Single(host.Windows);
            Assert.Same(follow, back);
            Assert.Equal("", back.Options.MonitorDeviceName);          // vuelve a «seguir al principal», como estaba guardado
            Assert.False(host.FollowsForeground);                      // sin hooks propios otra vez
        });
    }

    [Fact]
    public void A_window_closed_behind_the_hosts_back_leaves_the_list()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Localization.Get("es"), show: false);
            var w = Assert.Single(host.Windows);
            w.Close();                                   // Alt+F4 con el notch activado (editando una tarea)
            w.Dispose();
            Assert.Empty(host.Windows);                  // el host lo sabe; la nueva llega en el siguiente mensaje
            host.Apply(Opts, NotchMonitorMode.Fixed, Localization.Get("es"));
            var again = Assert.Single(host.Windows);
            Assert.NotSame(w, again);
            Assert.False(again.IsDisposed);
        });
    }

    [Fact]
    public void Disposing_closes_every_window_and_leaves_the_broker_accepting()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            var host = new NotchHost(hub, Opts, NotchMonitorMode.AllMonitors, Localization.Get("es"), show: false);
            var windows = host.Windows.ToList();
            host.Dispose();
            Assert.All(windows, w => Assert.True(w.IsDisposed));
            Assert.Empty(host.Windows);
            Assert.True(hub.Permissions.Accepting);
        });
    }

    // ---- una ventana que se cierra sola (Alt+F4 con el foco en ella editando una tarea): antes desaparecía hasta reiniciar

    /// <summary>Lo que el host deja para el siguiente mensaje del hilo de UI, ejecutado a mano (sin bombear mensajes).</summary>
    private sealed class PostQueue
    {
        private readonly Queue<Action> _work = new();
        public int Pending => _work.Count;
        public void Post(Action work) => _work.Enqueue(work);
        public void RunAll() { while (_work.Count > 0) _work.Dequeue()(); }
    }

    [Fact]
    public void A_window_closed_from_outside_is_dropped_and_created_again()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            var ui = new PostQueue();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Localization.Get("es"), show: false, ui.Post);
            var first = Assert.Single(host.Windows);

            first.Close();
            Assert.True(first.IsDisposed);
            Assert.Empty(host.Windows);                 // fuera ya: nadie le pasa ajustes a una ventana muerta

            ui.RunAll();                                // en el siguiente mensaje, una nueva
            var again = Assert.Single(host.Windows);
            Assert.NotSame(first, again);
            Assert.False(again.IsDisposed);
            Assert.True(again.Interactive);
            Assert.Equal(Opts, again.Options);
        });
    }

    [Fact]
    public void Settings_arriving_after_a_window_died_go_to_a_new_one_and_never_to_the_dead_one()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            var ui = new PostQueue();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.FollowActive, Localization.Get("es"), show: false, ui.Post);
            var first = Assert.Single(host.Windows);
            first.Dispose();

            host.Apply(Opts with { Minimal = true }, NotchMonitorMode.FollowActive, Localization.Get("en"));   // antes de la recreación
            var again = Assert.Single(host.Windows);
            Assert.NotSame(first, again);
            Assert.True(again.Options.Minimal);

            ui.RunAll();                                // la recreación pendiente ya no tiene nada que hacer: no hay dos
            Assert.Same(again, Assert.Single(host.Windows));
        });
    }

    [Fact]
    public void With_one_per_monitor_a_dead_window_never_keeps_the_buttons()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            var ui = new PostQueue();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.AllMonitors, Localization.Get("es"), show: false, ui.Post);
            var interactive = Assert.Single(host.Windows, w => w.Interactive);

            interactive.Close();
            Assert.DoesNotContain(interactive, host.Windows);
            ui.RunAll();

            Assert.Equal(Screens().Count, host.Windows.Count);   // su monitor vuelve a tener notch
            Assert.All(host.Windows, w => Assert.False(w.IsDisposed));
            Assert.Single(host.Windows, w => w.Interactive);
        });
    }

    [Fact]
    public void Windows_the_host_closes_itself_schedule_nothing()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            var ui = new PostQueue();
            var host = new NotchHost(hub, Opts, NotchMonitorMode.AllMonitors, Localization.Get("es"), show: false, ui.Post);
            host.Apply(Opts, NotchMonitorMode.Fixed, Localization.Get("es"));   // con varios monitores, cierra las demás
            Assert.Equal(0, ui.Pending);
            var windows = host.Windows.ToList();
            host.Dispose();                                                     // y al apagar el notch, todas
            Assert.All(windows, w => Assert.True(w.IsDisposed));
            Assert.Equal(0, ui.Pending);                                        // ninguna se va a volver a crear
        });
    }

    [Fact]
    public void A_recreation_still_pending_when_the_host_is_disposed_creates_nothing()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            var ui = new PostQueue();
            var host = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Localization.Get("es"), show: false, ui.Post);
            host.Windows[0].Close();                    // se cierra sola: la recreación queda para el siguiente mensaje
            Assert.Equal(1, ui.Pending);
            host.Dispose();                             // el notch se apaga antes
            ui.RunAll();
            Assert.Empty(host.Windows);
        });
    }

    [Fact]
    public void Usage_reaches_every_window_and_each_range_is_asked_once()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Localization.Get("es"), show: false);
            var asked = new List<ChartRange>();
            host.UsageSeriesRequested += asked.Add;
            host.SetUsage(new UsageInput(UsageFixtures.Snapshot(), UsageFixtures.Plan));
            var w = Assert.Single(host.Windows);
            Assert.Equal("64%", w.UsageNumbersView!.Tiles[0].Pct);
            Assert.Equal("Plan Max · 20x", w.UsageNumbersView.Plan);

            // Como un clic en el anillo de 7 d: la página Uso con la semana destacada pide su rango, una sola vez.
            Assert.True(w.OpenUsagePage(QuotaFocus.Seven));
            Assert.Equal(new[] { ChartRange.Week1 }, asked);
            Assert.True(w.UsageNumbersView!.Tiles[1].Focused);
            Assert.True(w.UsageChart!.Loading);

            host.SetUsageSeries(ChartRange.Week1, new UsageSeriesData(UsageFixtures.Spend(), UsageFixtures.Percent(), DateTime.UtcNow));
            Assert.Equal(UsageChartMode.Quota, w.UsageChart!.Mode);
            Assert.Equal(ChartRange.Week1, w.UsageChart.Range);
            Assert.False(w.UsageChart.Loading);

            Assert.True(w.OpenUsagePage(QuotaFocus.Seven));
            Assert.Single(asked);   // los datos son frescos: no se vuelven a pedir
            Assert.True(w.OpenUsagePage(QuotaFocus.Five));
            Assert.Equal(new[] { ChartRange.Week1, ChartRange.Hours5 }, asked);
            host.UsageSeriesFailed(ChartRange.Hours5);
            Assert.True(w.OpenUsagePage(QuotaFocus.Five));
            Assert.Equal(3, asked.Count);   // tras un fallo se puede volver a pedir
        });
    }

    [Fact]
    public void A_tray_refresh_with_the_quota_chart_on_screen_does_not_freeze_it()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Localization.Get("es"), show: false);
            host.SetUsage(new UsageInput(UsageFixtures.Snapshot(), UsageFixtures.Plan));
            // La hora del fixture (no la del reloj): con DateTime.UtcNow, pasadas las 5 h del fixture la serie de 5 h sale vacía.
            host.SetUsageSeries(ChartRange.Hours5, new UsageSeriesData(UsageFixtures.Spend(), UsageFixtures.Percent(), UsageFixtures.Now));
            var w = Assert.Single(host.Windows);
            Assert.True(w.OpenUsagePage(QuotaFocus.Five));
            var chart = w.UsageChart!;
            Assert.Equal(UsageChartMode.Quota, chart.Mode);
            Assert.False(w.UsageChartAnimating);

            // Cada refresco de la bandeja trae una foto nueva con los mismos valores: la gráfica no se rehace.
            host.SetUsage(new UsageInput(UsageFixtures.Snapshot(), UsageFixtures.Plan));
            Assert.Same(chart, w.UsageChart);
            Assert.False(w.UsageChartAnimating);

            // El % cambia en vivo: la gráfica nueva, de golpe (una transición sin bucle se quedaría congelada en 0).
            host.SetUsage(new UsageInput(UsageFixtures.Snapshot(fivePct: 65), UsageFixtures.Plan));
            Assert.NotSame(chart, w.UsageChart);
            Assert.False(w.UsageChartAnimating);
            Assert.Equal("65%", w.UsageChart!.Series[0].Value);
        });
    }

    [Fact]
    public void Without_pro_the_usage_chart_switches_without_transitions()
    {
        foreach (var locked in new[] { NotchProLocks.UsageCharts, NotchProLocks.None })
            RunSta(() =>
            {
                var hub = new ClaudeStateHub();
                using var host = new NotchHost(hub, Opts with { Locked = locked }, NotchMonitorMode.Fixed, Localization.Get("es"),
                    show: false);
                host.SetUsage(new UsageInput(UsageFixtures.Snapshot(), UsageFixtures.Plan));
                host.SetUsageSeries(ChartRange.Hours5, new UsageSeriesData(UsageFixtures.Spend(), UsageFixtures.Percent(), DateTime.UtcNow));
                var w = Assert.Single(host.Windows);
                Assert.True(w.OpenUsagePage(QuotaFocus.Five));
                bool pro = locked == NotchProLocks.None;

                // El selector Gasto | Cuota: con Pro, transición; sin Pro, salto.
                var before = w.UsageChart;
                Assert.True(w.SetUsageMode(UsageChartMode.Spend));
                Assert.NotSame(before, w.UsageChart);
                Assert.Equal(pro, w.UsageChartAnimating);
            });
    }

    [Fact]
    public void Another_range_shows_loading_and_a_failure_reaches_the_window()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Localization.Get("es"), show: false);
            var asked = new List<ChartRange>();
            host.UsageSeriesRequested += asked.Add;
            host.SetUsage(new UsageInput(UsageFixtures.Snapshot(), UsageFixtures.Plan));
            host.SetUsageSeries(ChartRange.Hours5, new UsageSeriesData(UsageFixtures.Spend(), UsageFixtures.Percent(), DateTime.UtcNow));
            var w = Assert.Single(host.Windows);
            Assert.True(w.OpenUsagePage(QuotaFocus.Five));
            Assert.Equal(UsageChartStatus.Ready, w.UsageStatus);

            // 30D sin datos: se piden y, mientras, la gráfica de 5H se queda pero ya no cuenta como la del rango.
            Assert.True(w.SetUsageRange(ChartRange.Month1));
            Assert.Equal(new[] { ChartRange.Month1 }, asked);
            Assert.Equal(ChartRange.Hours5, w.UsageChart!.Range);
            Assert.Equal(UsageChartStatus.Loading, w.UsageStatus);

            host.UsageSeriesFailed(ChartRange.Month1);
            Assert.Equal(UsageChartStatus.Failed, w.UsageStatus);

            host.SetUsageSeries(ChartRange.Month1, new UsageSeriesData(UsageFixtures.Spend(), UsageFixtures.Percent(), DateTime.UtcNow));
            Assert.Equal(ChartRange.Month1, w.UsageChart!.Range);
            Assert.Equal(UsageChartStatus.Ready, w.UsageStatus);
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
