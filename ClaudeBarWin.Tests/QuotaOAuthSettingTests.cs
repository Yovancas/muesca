using System.Text.Json;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// «Leer la cuota con tu sesión de Claude Code (no oficial)»: activado por defecto (decisión del autor, 29-10-2026),
/// visible y apagable. Apagado, la cuota sale solo de la statusline y, sin sesión abierta, la interfaz enseña la última
/// lectura con su edad en lugar de un error. Todo puro: sin red, sin archivos del usuario.
/// </summary>
public class QuotaOAuthSettingTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static double Measure(string s) => s.Length * 10.0;

    // ---------- ajuste ----------

    [Fact]
    public void It_is_on_by_default_also_for_an_old_config_json()
    {
        Assert.True(new AppConfig().QuotaOAuth);
        Assert.True(JsonSerializer.Deserialize<AppConfig>("""{"RefreshSeconds":60}""")!.QuotaOAuth);
        Assert.False(JsonSerializer.Deserialize<AppConfig>("""{"QuotaOAuth":false}""")!.QuotaOAuth);
    }

    [Fact]
    public void The_settings_action_toggles_it()
    {
        var cfg = new AppConfig();
        DashboardSettingsView.ActionFor("toggle:QuotaOAuth")!(cfg);
        Assert.False(cfg.QuotaOAuth);
        DashboardSettingsView.ActionFor("toggle:QuotaOAuth")!(cfg);
        Assert.True(cfg.QuotaOAuth);
    }

    [Fact]
    public void Its_texts_say_what_it_does_with_the_token_in_spanish_and_english()
    {
        var es = Localization.Get("es");
        Assert.Equal("Leer la cuota con tu sesión de Claude Code (no oficial)", es.QuotaOAuthLabel);
        Assert.Contains("solo con api.anthropic.com", es.QuotaOAuthSubtitle);
        Assert.Contains("No lo guarda ni lo envía a ningún otro sitio", es.QuotaOAuthSubtitle);
        Assert.Contains("Anthropic no lo documenta", es.QuotaOAuthSubtitle);
        Assert.Contains("puede dejar de funcionar", es.QuotaOAuthSubtitle);

        var en = Localization.Get("en");
        Assert.Equal("Read quota with your Claude Code session (unofficial)", en.QuotaOAuthLabel);
        Assert.Contains("only with api.anthropic.com", en.QuotaOAuthSubtitle);
        Assert.Contains("may stop working", en.QuotaOAuthSubtitle);
    }

    // ---------- estado que ve el usuario ----------

    [Theory]
    [InlineData(true, true, UsageFetchState.AuthExpired, UsageFetchState.Ok)]        // statusline fresca manda
    [InlineData(false, true, UsageFetchState.Ok, UsageFetchState.Ok)]
    [InlineData(true, false, UsageFetchState.RateLimited, UsageFetchState.RateLimited)]
    [InlineData(true, false, UsageFetchState.AuthExpired, UsageFetchState.AuthExpired)]
    [InlineData(false, false, UsageFetchState.AuthExpired, UsageFetchState.StatuslineOnly)] // sin API no hay error de API
    [InlineData(false, false, UsageFetchState.Ok, UsageFetchState.StatuslineOnly)]
    public void Effective_state(bool oauth, bool liveStatusline, UsageFetchState api, UsageFetchState expected)
        => Assert.Equal(expected, QuotaPolicy.EffectiveState(oauth, liveStatusline, api));

    [Fact]
    public void Statusline_only_path_works_on_its_own()
    {
        // Snapshot del helper de hace 25 min (sin sesión abierta desde entonces), sin ninguna lectura de la API.
        long seen = new DateTimeOffset(Now.AddMinutes(-25)).ToUnixTimeSeconds();
        long reset5 = new DateTimeOffset(Now.AddHours(2)).ToUnixTimeSeconds();
        long reset7 = new DateTimeOffset(Now.AddDays(3)).ToUnixTimeSeconds();
        static string Win(int pct, long reset) => $"{{\"used_percentage\":{pct},\"resets_at\":{reset}}}";
        var snap = RateLimitSnapshotStore.Parse(
            $"{{\"v\":1,\"session_id\":\"s\",\"seen_at\":{seen},\"changed_at\":{seen}," +
            $"\"rate_limits\":{{\"five_hour\":{Win(41, reset5)},\"seven_day\":{Win(12, reset7)}}}}}")!;

        var merged = QuotaMerger.Merge(new[] { snap }, api: null, DateTime.MinValue, Now);
        Assert.NotNull(merged);
        Assert.Equal(41, merged!.Usage.FiveHour!.UtilizationPct);
        Assert.Equal(12, merged.Usage.SevenDay!.UtilizationPct);
        Assert.True(merged.FromStatusline);

        bool live = merged.FromStatusline && QuotaPolicy.IsFresh(snap.SeenUtc, Now);
        Assert.False(live);                                           // 25 min > 10 min: sin sesión
        var state = QuotaPolicy.EffectiveState(oauthEnabled: false, live, UsageFetchState.Ok);
        Assert.Equal(UsageFetchState.StatuslineOnly, state);

        var s = Localization.Get("es");
        var app = new AppSnapshot { Usage = merged.Usage, LatestState = state, UsageAtUtc = merged.ObservedUtc };
        var footer = FooterLayout.Build(app, s, stale: false, sticky: false, Now, 10_000, Measure)
            .Single(l => l.Kind == FooterLayout.LineKind.Footer).Text;
        Assert.StartsWith("Última lectura hace 25 min", footer);
        Assert.DoesNotContain("⚠", footer);
        Assert.DoesNotContain(s.PreviousDataFooter, footer);
    }

    [Fact]
    public void A_fresh_statusline_without_the_api_is_live_data()
    {
        var state = QuotaPolicy.EffectiveState(oauthEnabled: false,
            liveFromStatusline: QuotaPolicy.IsFresh(Now.AddMinutes(-2), Now), UsageFetchState.Ok);
        Assert.Equal(UsageFetchState.Ok, state);
    }

    [Fact]
    public void Statusline_only_without_any_reading_says_to_open_claude_code()
    {
        var es = Localization.Get("es");
        var app = new AppSnapshot { LatestState = UsageFetchState.StatuslineOnly, UsageAtUtc = Now };
        Assert.Equal("Sin cuota todavía — abre una sesión de Claude Code", UsageFormat.StateMessage(app.LatestState, es));

        var footer = FooterLayout.Build(app, es, stale: false, sticky: false, Now, 10_000, Measure)
            .Single(l => l.Kind == FooterLayout.LineKind.Footer).Text;
        Assert.Equal(es.HintClickToHide, footer);                     // ni «datos previos» ni una edad inventada
    }

    [Theory]
    [InlineData(0.5, "Última lectura hace <1 min", "Last reading <1 min ago")]
    [InlineData(12, "Última lectura hace 12 min", "Last reading 12 min ago")]
    [InlineData(135, "Última lectura hace 2 h", "Last reading 2 h ago")]
    [InlineData(60 * 50, "Última lectura hace 2 d", "Last reading 2 d ago")]
    public void Last_reading_text(double minutesAgo, string es, string en)
    {
        Assert.Equal(es, UsageFormat.LastReadingAt(Now.AddMinutes(-minutesAgo), Now, Localization.Get("es")));
        Assert.Equal(en, UsageFormat.LastReadingAt(Now.AddMinutes(-minutesAgo), Now, Localization.Get("en")));
    }

    [Fact]
    public void Relative_minutes_text_did_not_change()
    {
        var es = Localization.Get("es");
        Assert.Equal(string.Format(es.AgoFormat, "12 min"), UsageFormat.RelativeMinutesAt(Now.AddMinutes(-12), Now, es));
        Assert.Equal(string.Format(es.AgoFormat, "<1 min"), UsageFormat.RelativeMinutesAt(Now, Now, es));
    }

    // ---------- fila de ajustes ----------

    private static Dictionary<string, Rectangle> DrawSection(AppConfig cfg, TapStatus status, out int measured, out int painted)
    {
        using var bmp = new Bitmap(400, 600);
        using var g = Graphics.FromImage(bmp);
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();
        measured = DashboardSettingsView.QuotaSourceSection(g, false, status, null, Now, 0, 0, 360, cfg, s,
            Theme.Dark, Typography.Body, Typography.Caption, new Dictionary<string, Rectangle>());
        painted = DashboardSettingsView.QuotaSourceSection(g, true, status, null, Now, 0, 0, 360, cfg, s,
            Theme.Dark, Typography.Body, Typography.Caption, rects);
        return rects;
    }

    [Fact]
    public void The_row_is_visible_and_the_cli_refresh_depends_on_it()
    {
        // Waiting: su subtítulo no cambia con el ajuste, así se compara solo lo que añade la fila nueva.
        var on = DrawSection(new AppConfig(), TapStatus.Waiting, out int m1, out int p1);
        Assert.True(on.ContainsKey("toggle:QuotaOAuth"));
        Assert.True(on.ContainsKey("toggle:QuotaCliRefresh"));
        Assert.Equal(m1, p1);
        Assert.True(on["toggle:QuotaOAuth"].Bottom <= on["toggle:QuotaCliRefresh"].Top);   // la CLI va debajo
        Assert.True(on["toggle:QuotaCliRefresh"].Left > on["toggle:QuotaOAuth"].Left);     // y sangrada

        var off = DrawSection(new AppConfig { QuotaOAuth = false }, TapStatus.Waiting, out int m2, out int p2);
        Assert.True(off.ContainsKey("toggle:QuotaOAuth"));            // el maestro siempre responde
        Assert.False(off.ContainsKey("toggle:QuotaCliRefresh"));      // sin lectura por la sesión, la CLI no pinta nada
        Assert.Equal(m2, p2);
        Assert.Equal(p1, p2);                                         // la geometría no salta al cambiarlo

        DrawSection(new AppConfig { QuotaOAuth = false }, TapStatus.Off, out int m3, out int p3);
        Assert.Equal(m3, p3);                                         // con «actívalo para ver la cuota», medir==pintar
    }

    [Fact]
    public void With_both_sources_off_the_statusline_row_says_to_turn_it_on()
    {
        var s = Localization.Get("es");
        Assert.Equal(s.TapOff, DashboardSettingsView.TapSubtitle(TapStatus.Off, null, Now, s, apiOn: true));
        Assert.Equal("Sin activar · actívalo para ver la cuota",
            DashboardSettingsView.TapSubtitle(TapStatus.Off, null, Now, s, apiOn: false));
        // Con la statusline activa el aviso no cambia nada.
        Assert.Equal(s.TapWaiting, DashboardSettingsView.TapSubtitle(TapStatus.Waiting, null, Now, s, apiOn: false));
    }
}
