using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Datos de ejemplo de la página Uso (un día de trabajo con Opus y Sonnet, la cuota subiendo y un reinicio) y el frame de cada
/// estado: lo comparten los tests de pintado y las capturas.
/// </summary>
internal static class UsageFixtures
{
    public static readonly DateTime Now = new(2026, 10, 30, 17, 20, 0, DateTimeKind.Utc);
    private static readonly NotchPage[] Pages = NotchPages.Default.ToArray();

    public static AppSnapshot Snapshot(double fivePct = 64) => new()
    {
        Usage = new RealUsage
        {
            FiveHour = new UsageWindow(fivePct, new DateTimeOffset(Now).AddHours(1).AddMinutes(40)),
            SevenDay = new UsageWindow(38, new DateTimeOffset(Now).AddDays(2).AddHours(6)),
            SevenDayOpus = new UsageWindow(31, null),
            SevenDaySonnet = new UsageWindow(7, null),
            ExtraUsageEnabled = true,
        },
        LatestState = UsageFetchState.Ok,
        UsageAtUtc = Now,
        PaceFive = new PaceResult("5h", 64, 1.16, 55, new DateTimeOffset(Now).AddHours(1).AddMinutes(10),
            new DateTimeOffset(Now).AddHours(1).AddMinutes(40), true, PaceStatus.Over),
        PaceSeven = new PaceResult("7d", 38, 0.86, 44, null, new DateTimeOffset(Now).AddDays(2).AddHours(6), false, PaceStatus.Ok),
        Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
    };

    public static PlanInfo Plan => new("max", "default_claude_max_20x");

    /// <summary>24 h de gasto por horas: la mañana con Opus, la tarde con los dos.</summary>
    public static List<HistoryBucket> Spend()
    {
        var list = new List<HistoryBucket>();
        for (int i = 0; i < 24; i++)
        {
            var start = (Now - TimeSpan.FromHours(24 - i)).ToLocalTime();
            int h = (i + 17) % 24;
            double work = h is >= 9 and <= 20 ? 1 : 0.05;
            var by = new Dictionary<string, double>
            {
                ["Opus"] = Math.Round(work * (1.2 + Math.Sin(i * 0.7) * 0.9 + 0.9), 2),
                ["Sonnet"] = Math.Round(work * (0.4 + Math.Cos(i * 0.5) * 0.35 + 0.35), 2),
            };
            if (i % 7 == 3) by["Haiku"] = 0.12;
            list.Add(new HistoryBucket(start, start.ToString("HH'h'"), by.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
        return list;
    }

    /// <summary>5 h de muestras cada minuto: 5 h sube a 64 % tras un reinicio hace 3 h 20 min; 7 d, despacio. <paramref name="now"/>:
    /// la última muestra (por defecto <see cref="Now"/>; el reloj real para lo que compara con la hora de verdad).</summary>
    public static List<PctPoint> Percent(DateTime? now = null)
    {
        var list = new List<PctPoint>();
        var end = now ?? Now;
        for (int m = 300; m >= 0; m--)
        {
            var at = end.AddMinutes(-m);
            double sinceReset = 200 - m;
            double five = sinceReset < 0 ? 88 + (300 - m) * 0.08 : Math.Min(100, sinceReset * 0.32);
            if (m is > 120 and < 135) continue;   // un hueco corto: se une
            list.Add(new PctPoint(at, Math.Round(five, 1), Math.Round(35 + (300 - m) * 0.01, 1)));
        }
        return list;
    }

    public static UsageNumbers Numbers(Strings s, QuotaFocus focus = QuotaFocus.None) =>
        UsageText.Build(Snapshot(), Plan, Now, focus, s);

    public static UsageChartModel SpendChart(Strings s, IReadOnlySet<string>? hidden = null)
    {
        var data = Spend();
        var slots = new ChartSeriesAssigner().Assign(UsageChartBuilder.Families(data));
        return UsageChartBuilder.Spend(data, ChartRange.Day1, slots, hidden ?? new HashSet<string>(), s);
    }

    public static UsageChartModel QuotaChart(Strings s, bool projection = true, QuotaFocus focus = QuotaFocus.Five)
    {
        var snap = Snapshot();
        var q = new UsageQuotaInputs(snap.Usage!.FiveHour, snap.Usage.SevenDay, snap.PaceFive, snap.PaceSeven, new[] { 80, 95 });
        return UsageChartBuilder.Quota(Percent(), ChartRange.Hours5, Now, focus, q, new HashSet<string>(), projection, s);
    }

    /// <summary>El frame del expandido en la página Uso (con el aviso integrado o sin él).</summary>
    public static NotchFrame Frame(float s, UsageView? view, bool alert = false)
    {
        var size = NotchLayout.PageSize(NotchPage.Usage, default, alert, withDots: true, s);
        NotchAlert? a = alert ? new NotchAlert(NotchAlertKind.QuotaHud, "q", "5h", "80%", DateTime.UtcNow, null) : null;
        return new NotchFrame(size.Width, size.Height, s, NotchMode.Expanded, a, Array.Empty<SessionState>(),
            Snapshot().Usage, 0f, 1f, ShadowAlpha: 1f,
            Pages: new NotchPagesView(Pages, NotchPage.Usage, null, 1f, 0f, 0f, Array.IndexOf(Pages, NotchPage.Usage)),
            Locked: view is { Locked: true } ? NotchProLocks.UsageCharts : NotchProLocks.None, Usage: view);
    }

    /// <summary>Casilla del centro de la gráfica (para el hover de las capturas y los tests).</summary>
    public static int MidBucket(UsageChartModel m) => Math.Max(0, m.Count * 2 / 3);
}
