using System.Drawing;
using System.Text.Json;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Decisiones del dueño sobre la página Uso: recuerda modo, ventana y rango entre aperturas (en <c>config.json</c>, sin las
/// series ocultas); respeta «Mostrar gasto estimado» (apagado = Cuota y ni una cifra de gasto) y, sin el Pro, cambiar entre
/// Gasto y Cuota sigue libre.
/// </summary>
public class UsageViewStateTests
{
    [Fact]
    public void The_remembered_view_has_mode_window_and_range_but_never_hidden_series()
    {
        var props = typeof(UsageViewState).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Select(p => p.Name).ToArray();
        Assert.Equal(new[] { nameof(UsageViewState.Mode), nameof(UsageViewState.Focus), nameof(UsageViewState.Range) },
            props.Where(n => n != "EqualityContract").ToArray());
        Assert.Equal(new UsageViewState(UsageChartMode.Spend, QuotaFocus.None, UsageRanges.DefaultSpend), UsageViewState.Default);
    }

    [Fact]
    public void Odd_values_from_disk_fall_back_to_the_defaults()
    {
        var odd = new UsageViewState((UsageChartMode)7, (QuotaFocus)9, (ChartRange)42).Normalize();
        Assert.Equal(UsageViewState.Default, odd);
        var ok = new UsageViewState(UsageChartMode.Quota, QuotaFocus.Seven, ChartRange.Week1);
        Assert.Equal(ok, ok.Normalize());
    }

    [Fact]
    public void It_roundtrips_through_config_json_and_is_absent_by_default()
    {
        Assert.Null(new AppConfig().NotchUsageView);
        var c = new AppConfig { NotchUsageView = new UsageViewState(UsageChartMode.Quota, QuotaFocus.Five, ChartRange.Hours5) };
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c))!.Normalize();
        Assert.Equal(c.NotchUsageView, back.NotchUsageView);
        var bad = new AppConfig { NotchUsageView = new UsageViewState((UsageChartMode)5, QuotaFocus.Five, ChartRange.Hours5) }.Normalize();
        Assert.Equal(UsageChartMode.Spend, bad.NotchUsageView!.Mode);
    }

    // ---- «Mostrar gasto estimado» ----

    [Theory]
    [InlineData(UsageChartMode.Spend, true, UsageChartMode.Spend)]
    [InlineData(UsageChartMode.Quota, true, UsageChartMode.Quota)]
    [InlineData(UsageChartMode.Spend, false, UsageChartMode.Quota)]   // apagado: abre en Cuota (lo elegido se conserva)
    [InlineData(UsageChartMode.Quota, false, UsageChartMode.Quota)]
    public void Without_the_spend_setting_the_page_shows_quota(UsageChartMode chosen, bool showSpend, UsageChartMode shown) =>
        Assert.Equal(shown, UsageViewState.ShownMode(chosen, showSpend));

    private static readonly UsageRects R = UsageLayout.Compute(new RectangleF(0, 0, UsageLayout.PageWidth, 220), 1f);

    private static PointF Mid(RectangleF r) => new(r.Left + r.Width / 2f, r.Top + r.Height / 2f);

    [Fact]
    public void Without_the_spend_setting_the_mode_selector_does_not_answer()
    {
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(UsageControl.Segment, UsageLayout.HitTest(Mid(R.Segment(i)), R, 3, interactive: true, locked: false).Control);
            Assert.NotEqual(UsageControl.Segment,
                UsageLayout.HitTest(Mid(R.Segment(i)), R, 3, interactive: true, locked: false, modes: false).Control);
        }
    }

    [Fact]
    public void Without_pro_switching_spend_and_quota_stays_free()
    {
        // Sin el Pro (locked) y aunque no sea la ventana interactiva: el selector Gasto | Cuota responde (navegar no es Pro).
        for (int i = 0; i < 2; i++)
            Assert.Equal(new UsageHit(UsageControl.Segment, i),
                UsageLayout.HitTest(Mid(R.Segment(i)), R, 3, interactive: false, locked: true));
    }
}
