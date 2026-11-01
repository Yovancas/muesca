using System.Text.Json;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

public class DashboardConfigTests
{
    [Fact]
    public void Defaults_quota_and_sessions_expanded_spend_and_chart_collapsed()
    {
        var c = new AppConfig();
        Assert.False(c.CollapsedQuota);
        Assert.False(c.CollapsedSessions);
        Assert.True(c.CollapsedSpend);
        Assert.True(c.CollapsedChart);
    }

    [Fact]
    public void Collapsed_roundtrip_json_and_old_mascotsize_key_is_ignored()
    {
        var c = new AppConfig { CollapsedQuota = true, CollapsedChart = false };
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c))!;
        Assert.True(back.CollapsedQuota);
        Assert.False(back.CollapsedChart);

        // Migración v0.3.7: un config.json viejo con "MascotSize" no rompe la deserialización
        // (la clave desconocida se ignora; solo queda la mascota compacta).
        var legacy = JsonSerializer.Deserialize<AppConfig>("""{"MascotSize":"large","ShowMascot":true}""")!;
        Assert.True(legacy.ShowMascot);
    }

    // ---- Panel de ajustes: ActionFor traduce claves a mutaciones de config ----

    [Fact]
    public void ActionFor_position_cycles_through_all_five_and_wraps()
    {
        var c = new AppConfig { DashboardPosition = "BottomRight" };
        var seen = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            DashboardSettingsView.ActionFor("cycle:position")!(c);
            seen.Add(c.DashboardPosition);
        }
        Assert.Equal(
            new[] { "BottomLeft", "TopRight", "TopLeft", "Center", "BottomRight", "BottomLeft" },
            seen);
    }

    [Fact]
    public void ActionFor_language_cycles_system_then_eight_codes_and_wraps()
    {
        var c = new AppConfig { Language = "system" };
        DashboardSettingsView.ActionFor("cycle:lang")!(c);
        Assert.Equal("en", c.Language);

        // Avanzar hasta el último y comprobar que envuelve a "system".
        c.Language = Localization.LanguageCycle[^1];
        DashboardSettingsView.ActionFor("cycle:lang")!(c);
        Assert.Equal("system", c.Language);
    }

    [Fact]
    public void ActionFor_opacity_and_threshold_parse_compound_keys()
    {
        var c = new AppConfig();
        DashboardSettingsView.ActionFor("opacity:0.7")!(c);
        Assert.Equal(0.7, c.DashboardOpacity, 3);

        DashboardSettingsView.ActionFor("threshold:80/95")!(c);
        Assert.Equal(80, c.WarnThresholdPct);
        Assert.Equal(95, c.CriticalThresholdPct);
    }

    [Fact]
    public void ActionFor_icon_mode_sets_display_mode()
    {
        var c = new AppConfig();
        DashboardSettingsView.ActionFor("icon:pace")!(c);
        Assert.Equal("pace", c.IconDisplayMode);
        DashboardSettingsView.ActionFor("icon:both")!(c);
        Assert.Equal("both", c.IconDisplayMode);
    }

    [Fact]
    public void ActionFor_milestone_toggles_membership_in_array()
    {
        var c = new AppConfig { NotifyMilestones = new[] { 25, 50, 75, 95 } };
        DashboardSettingsView.ActionFor("milestone:50")!(c);   // quita
        Assert.Equal(new[] { 25, 75, 95 }, c.NotifyMilestones);
        DashboardSettingsView.ActionFor("milestone:50")!(c);   // re-añade, ordenado
        Assert.Equal(new[] { 25, 50, 75, 95 }, c.NotifyMilestones);
    }

    [Fact]
    public void ActionFor_suppress_when_focused_toggles()
    {
        var c = new AppConfig { SuppressWhenFocused = true };
        DashboardSettingsView.ActionFor("toggle:Suppress")!(c);
        Assert.False(c.SuppressWhenFocused);
    }

    [Fact]
    public void ActionFor_special_keys_return_null_for_host_routing()
    {
        Assert.Null(DashboardSettingsView.ActionFor("special:importtheme"));
        Assert.Null(DashboardSettingsView.ActionFor("special:hooktoggle"));
        Assert.Null(DashboardSettingsView.ActionFor("nonexistent"));
    }

    [Fact]
    public void ActionFor_reduce_motion_toggles()
    {
        var c = new AppConfig { ReduceMotion = false };
        DashboardSettingsView.ActionFor("toggle:ReduceMotion")!(c);
        Assert.True(c.ReduceMotion);
        DashboardSettingsView.ActionFor("toggle:ReduceMotion")!(c);
        Assert.False(c.ReduceMotion);
    }
}
