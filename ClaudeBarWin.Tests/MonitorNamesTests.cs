using System.Drawing;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="MonitorNames"/> y <see cref="NotchMonitorMenu"/> son PUROS: con monitores de mentira (sin depender de los
/// reales) dan el nombre legible de cada uno, qué elemento del submenú de la bandeja va marcado, si se enseña y qué se
/// guarda al elegir.
/// </summary>
public class MonitorNamesTests
{
    private static readonly Strings Es = Localization.Get("es");
    private static readonly Strings En = Localization.Get("en");

    private static readonly MonitorInfo Main = new(@"\\.\DISPLAY1", new Rectangle(0, 0, 2560, 1440), true, "LG ULTRAGEAR");
    private static readonly MonitorInfo Right = new(@"\\.\DISPLAY2", new Rectangle(2560, 0, 1920, 1080), false, "DELL U2419H");
    private static readonly MonitorInfo Left = new(@"\\.\DISPLAY3", new Rectangle(-1920, 200, 1920, 1080), false, "DELL U2419H");
    private static readonly MonitorInfo Above = new(@"\\.\DISPLAY4", new Rectangle(320, -1080, 1920, 1080), false, null);

    [Fact]
    public void Label_has_friendly_name_resolution_and_side()
    {
        var labels = MonitorNames.Labels(new[] { Main, Right with { FriendlyName = "SAMSUNG" } }, Es);
        Assert.Equal("LG ULTRAGEAR · 2560×1440 · principal", labels[0]);
        Assert.Equal("SAMSUNG · 1920×1080 · derecha", labels[1]);
    }

    [Fact]
    public void Without_friendly_name_falls_back_to_monitor_number_from_gdi_name()
    {
        var labels = MonitorNames.Labels(new[] { Main, Above }, Es);
        Assert.Equal("Monitor 4 · 1920×1080 · arriba", labels[1]);

        // Sin número en el nombre GDI: su posición + 1. Nombre vacío o de espacios = sin nombre.
        var odd = new MonitorInfo("PANTALLA", new Rectangle(2560, 0, 1280, 1024), false, "   ");
        Assert.Equal("Monitor 2 · 1280×1024 · derecha", MonitorNames.Labels(new[] { Main, odd }, Es)[1]);
    }

    [Fact]
    public void Duplicate_names_get_numbered_in_order()
    {
        var labels = MonitorNames.Labels(new[] { Main, Right, Left }, Es);
        Assert.Equal("DELL U2419H 1 · 1920×1080 · derecha", labels[1]);
        Assert.Equal("DELL U2419H 2 · 1920×1080 · izquierda", labels[2]);
        Assert.Equal(3, labels.Distinct().Count());
        // El nombre único no se toca.
        Assert.StartsWith("LG ULTRAGEAR ·", labels[0]);
    }

    [Fact]
    public void Side_is_relative_to_primary()
    {
        var p = Main.Bounds;
        Assert.Equal(MonitorSide.Primary, MonitorNames.Side(Main, p));
        Assert.Equal(MonitorSide.Right, MonitorNames.Side(Right, p));
        Assert.Equal(MonitorSide.Left, MonitorNames.Side(Left, p));
        Assert.Equal(MonitorSide.Above, MonitorNames.Side(Above, p));
        Assert.Equal(MonitorSide.Below, MonitorNames.Side(Above with { Bounds = new Rectangle(0, 1440, 1920, 1080) }, p));
        // Principal que no empieza en 0,0 (no es lo normal): se mide igual contra él.
        var mainOff = Main with { Bounds = new Rectangle(1920, 0, 2560, 1440) };
        Assert.Equal(MonitorSide.Left, MonitorNames.Side(Right with { Bounds = new Rectangle(0, 0, 1920, 1080) }, mainOff.Bounds));
    }

    [Fact]
    public void Labels_are_localized()
    {
        var labels = MonitorNames.Labels(new[] { Main, Left, Above }, En);
        Assert.Equal("LG ULTRAGEAR · 2560×1440 · primary", labels[0]);
        Assert.EndsWith("· left", labels[1]);
        Assert.EndsWith("· above", labels[2]);
        Assert.NotEqual(En.NotchMonitorMenu, Es.NotchMonitorMenu);
        Assert.NotEqual(En.NotchMonitorFollowPrimary, Es.NotchMonitorFollowPrimary);
        Assert.NotEqual(En.NotchMonitorRight, Es.NotchMonitorRight);
    }

    [Fact]
    public void Primary_item_is_checked_when_following_primary_or_saved_monitor_is_gone()
    {
        var monitors = new[] { Main, Right };
        foreach (var current in new[] { "", null, @"\\.\DISPLAY9" })
        {
            var items = NotchMonitorMenu.Items(current, monitors, Es);
            Assert.Equal(3, items.Count);
            Assert.Equal("", items[0].DeviceName);
            Assert.Equal(Es.NotchMonitorFollowPrimary, items[0].Text);
            Assert.True(items[0].Checked);
            Assert.Single(items, i => i.Checked);
        }
    }

    [Fact]
    public void Current_monitor_is_checked_ignoring_case()
    {
        var items = NotchMonitorMenu.Items(@"\\.\display2", new[] { Main, Right }, Es);
        Assert.False(items[0].Checked);
        Assert.False(items[1].Checked);
        Assert.True(items[2].Checked);
        Assert.Equal(@"\\.\DISPLAY2", items[2].DeviceName);
        Assert.Equal("DELL U2419H · 1920×1080 · derecha", items[2].Text);
    }

    [Theory]
    [InlineData(true, 2, true)]
    [InlineData(true, 3, true)]
    [InlineData(true, 1, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 2, false)]
    public void Submenu_only_with_notch_on_and_two_or_more_monitors(bool notchEnabled, int monitors, bool visible) =>
        Assert.Equal(visible, NotchMonitorMenu.IsVisible(notchEnabled, monitors));

    /// <summary>
    /// Beta del 29-oct: «Monitor del notch» salía sin flecha y sin monitores. El relleno se hace en el Opening del menú,
    /// cuando el menú aún NO está en pantalla y <c>ToolStripItem.Visible</c> devuelve false aunque se acabe de poner a
    /// true: el código miraba Visible y salía antes de añadir los monitores. Arriba van los tres modos y un separador.
    /// </summary>
    [Fact]
    public void Filling_the_submenu_before_the_menu_is_shown_adds_every_monitor()
    {
        var chosen = new List<string>();
        using var menu = new ContextMenuStrip();
        var mi = new ToolStripMenuItem("Monitor del notch") { Visible = false };
        menu.Items.Add(mi);

        TrayAppContext.FillNotchMonitorMenu(mi, notchEnabled: true, NotchMonitorMode.Fixed, current: "", new[] { Main, Right }, Es,
            _ => { }, it => chosen.Add(it.DeviceName));

        Assert.True(mi.HasDropDownItems);
        Assert.Equal(3 + 1 + 3, mi.DropDownItems.Count);   // 3 modos, separador, «Principal» + 2 monitores
        mi.DropDownItems[6].PerformClick();
        Assert.Equal(new[] { Right.DeviceName }, chosen);

        // Rellenarlo otra vez (cada apertura) no duplica, y con un solo monitor queda vacío.
        TrayAppContext.FillNotchMonitorMenu(mi, true, NotchMonitorMode.Fixed, "", new[] { Main, Right }, Es, _ => { }, _ => { });
        Assert.Equal(7, mi.DropDownItems.Count);
        TrayAppContext.FillNotchMonitorMenu(mi, true, NotchMonitorMode.Fixed, "", new[] { Main }, Es, _ => { }, _ => { });
        Assert.False(mi.HasDropDownItems);
    }

    [Fact]
    public void Submenu_has_the_three_modes_as_radio_options_on_top()
    {
        var modes = new List<NotchMonitorMode>();
        using var menu = new ContextMenuStrip();
        var mi = new ToolStripMenuItem("Monitor del notch");
        menu.Items.Add(mi);

        TrayAppContext.FillNotchMonitorMenu(mi, true, NotchMonitorMode.FollowActive, "", new[] { Main, Right }, Es, modes.Add, _ => { });

        Assert.Equal(new[] { Es.NotchMonitorModeFixed, Es.NotchMonitorModeFollow, Es.NotchMonitorModeAll },
            mi.DropDownItems.Cast<ToolStripItem>().Take(3).Select(i => i.Text));
        var radios = mi.DropDownItems.Cast<ToolStripItem>().Take(3).Cast<ToolStripMenuItem>().ToList();
        Assert.Equal(new[] { false, true, false }, radios.Select(r => r.Checked));   // uno marcado: el actual
        Assert.IsType<ToolStripSeparator>(mi.DropDownItems[3]);
        radios[2].PerformClick();
        Assert.Equal(new[] { NotchMonitorMode.AllMonitors }, modes);
    }

    /// <summary>Sin el Pro (varios monitores es de pago): arriba la entrada que abre la tarjeta; debajo lo efectivo (Fijo y
    /// el principal), marcado y sin poder elegir otra cosa.</summary>
    [Fact]
    public void Without_pro_the_submenu_shows_the_primary_monitor_and_offers_pro()
    {
        int opened = 0;
        var picked = new List<string>();
        using var menu = new ContextMenuStrip();
        var mi = new ToolStripMenuItem("Monitor del notch");
        menu.Items.Add(mi);
        TrayAppContext.FillNotchMonitorMenu(mi, true, NotchMonitorMode.Fixed, "", new[] { Main, Right }, Es, _ => picked.Add("modo"),
            it => picked.Add(it.DeviceName), () => opened++);

        var items = mi.DropDownItems.Cast<ToolStripItem>().ToList();
        Assert.Equal(Es.ProMonitorsLocked, items[0].Text);
        Assert.IsType<ToolStripSeparator>(items[1]);
        Assert.Equal(2 + 3 + 1 + 3, items.Count);
        var modes = items.Skip(2).Take(3).Cast<ToolStripMenuItem>().ToList();
        Assert.All(modes, m => Assert.False(m.Enabled));
        Assert.True(modes[0].Checked);   // Fijo
        var monitors = items.Skip(6).Cast<ToolStripMenuItem>().ToList();
        Assert.All(monitors, m => Assert.False(m.Enabled));
        Assert.True(monitors[0].Checked);   // el principal
        items[0].PerformClick();
        foreach (var m in modes.Concat(monitors)) m.PerformClick();   // deshabilitadas: no hacen nada
        Assert.Equal(1, opened);
        Assert.Empty(picked);
    }

    [Theory]
    [InlineData(NotchMonitorMode.Fixed, true)]
    [InlineData(NotchMonitorMode.FollowActive, false)]
    [InlineData(NotchMonitorMode.AllMonitors, false)]
    public void Monitor_list_is_only_active_in_fixed_mode(NotchMonitorMode mode, bool enabled)
    {
        using var menu = new ContextMenuStrip();
        var mi = new ToolStripMenuItem("Monitor del notch");
        menu.Items.Add(mi);
        TrayAppContext.FillNotchMonitorMenu(mi, true, mode, @"\\.\DISPLAY2", new[] { Main, Right }, Es, _ => { }, _ => { });

        var monitors = mi.DropDownItems.Cast<ToolStripItem>().Skip(4).Cast<ToolStripMenuItem>().ToList();
        Assert.Equal(3, monitors.Count);
        Assert.All(monitors, m => Assert.Equal(enabled, m.Enabled));
        // El monitor guardado solo va marcado cuando manda (en Fijo); en los otros modos no engaña.
        Assert.Equal(enabled, monitors[2].Checked);
        Assert.Equal(enabled, NotchMonitorMenu.MonitorsEnabled(mode));
    }

    [Fact]
    public void Mode_items_mark_the_current_one_and_choosing_saves_it()
    {
        var items = NotchMonitorMenu.Modes(NotchMonitorMode.AllMonitors, En);
        Assert.Equal(new[] { NotchMonitorMode.Fixed, NotchMonitorMode.FollowActive, NotchMonitorMode.AllMonitors }, items.Select(i => i.Mode));
        Assert.Equal(new[] { false, false, true }, items.Select(i => i.Checked));
        Assert.Equal(new[] { true, false, false }, NotchMonitorMenu.Modes((NotchMonitorMode)9, En).Select(i => i.Checked));

        var cfg = new AppConfig { NotchMonitor = @"\\.\DISPLAY2" };
        NotchMonitorMenu.ChooseMode(cfg, NotchMonitorMode.FollowActive);
        Assert.Equal(NotchMonitorMode.FollowActive, cfg.NotchMonitorMode);
        Assert.Equal(@"\\.\DISPLAY2", cfg.NotchMonitor);   // el monitor fijo se conserva: es el de casa al volver a Fijo
        NotchMonitorMenu.ChooseMode(cfg, (NotchMonitorMode)9);
        Assert.Equal(NotchMonitorMode.Fixed, cfg.NotchMonitorMode);
    }

    [Fact]
    public void Choosing_saves_the_device_name()
    {
        var items = NotchMonitorMenu.Items("", new[] { Main, Right, Left }, Es);
        var cfg = new AppConfig { NotchMonitor = "" };
        NotchMonitorMenu.Choose(cfg, items[3]);
        Assert.Equal(@"\\.\DISPLAY3", cfg.NotchMonitor);
        NotchMonitorMenu.Choose(cfg, items[0]);   // «Principal (seguir al principal)»
        Assert.Equal("", cfg.NotchMonitor);
    }

    [Fact]
    public void Settings_row_shows_readable_name_or_primary()
    {
        var monitors = new[] { Main, Right };
        Assert.Equal("DELL U2419H · 1920×1080 · derecha", DashboardSettingsView.MonitorLabel(@"\\.\DISPLAY2", monitors, Es));
        Assert.Equal(Es.NotchMonitorPrimary, DashboardSettingsView.MonitorLabel("", monitors, Es));
        // Desconectado: el notch está en el principal, y así lo dice la fila.
        Assert.Equal(Es.NotchMonitorPrimary, DashboardSettingsView.MonitorLabel(@"\\.\DISPLAY7", monitors, Es));
        Assert.Equal(Es.NotchMonitorPrimary, DashboardSettingsView.MonitorLabel(@"\\.\DISPLAY2", Array.Empty<MonitorInfo>(), Es));
    }
}
