using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T13b: las tarifas dejan de vivir hardcodeadas en C# — <see cref="PricingCatalog"/> resuelve la
/// tarifa de un id de modelo con la cadena caché en disco (&lt;7 días) → refresh online en background
/// (models.dev, opt-out) → snapshot embebido en el ensamblado. El bug real: claude-fable-5 computaba
/// 0 € porque no estaba en la lista fija Opus/Sonnet/Haiku.
/// </summary>
public class PricingCatalogTests
{
    /// <summary>Golden file: respuesta REAL de https://models.dev/api.json (provider anthropic entero
    /// + un recorte de openai para probar que SOLO se toma anthropic).</summary>
    private static readonly string GoldenPath =
        Path.Combine(AppContext.BaseDirectory, "TestData", "models-dev-sample.json");

    /// <summary>Ruta de caché única e INEXISTENTE (cada test su propia caja de arena).</summary>
    private static string TempCachePath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "claudebar-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "models-pricing.json");
    }

    /// <summary>Fetcher que FALLA siempre (simula sin red) — la cadena debe caer al snapshot.</summary>
    private static Func<CancellationToken, Task<string>> NoNet =>
        _ => Task.FromException<string>(new HttpRequestException("sin red"));

    private static UsageRecord Out1M(string model)
        => new(DateTime.UtcNow, model, 0, 1_000_000, 0, 0, 0);

    // ---------------- parseo (golden file con la respuesta real de models.dev) ----------------

    [Fact]
    public void Parsea_el_formato_completo_real_de_models_dev()
    {
        var cat = new PricingCatalog(GoldenPath, NoNet);
        var rate = cat.RateFor("claude-opus-4-8");

        Assert.NotNull(rate);
        Assert.Equal(5.0, rate!.Value.Input);
        Assert.Equal(25.0, rate.Value.Output);
        Assert.Equal(0.5, rate.Value.CacheRead);
        Assert.Equal(6.25, rate.Value.CacheWrite5m);  // cache_write del feed
        Assert.Equal(10.0, rate.Value.CacheWrite1h);  // derivado: input × 2.0 (proporción pública)
    }

    [Fact]
    public void Solo_toma_el_provider_anthropic_del_feed()
    {
        // El golden trae también openai (gpt-4o / gpt-5-pro): NO deben resolver tarifa.
        var cat = new PricingCatalog(GoldenPath, NoNet);
        Assert.Null(cat.RateFor("gpt-4o-2024-08-06"));
        Assert.Null(cat.RateFor("gpt-5-pro"));
    }

    // ---------------- cadena de fallback ----------------

    [Fact]
    public void Sin_red_ni_cache_usa_el_snapshot_embebido()
    {
        // El bug real de la auditoría: claude-fable-5 (uso diario) computaba 0 €.
        var cat = new PricingCatalog(TempCachePath(), NoNet);
        var cost = cat.CostFor(Out1M("claude-fable-5"));

        Assert.True(cost.IsPriced);
        Assert.Equal(50.0, cost.Usd, 10); // 1M tokens de salida × 50 $/Mtok (snapshot models.dev)
    }

    [Fact]
    public void La_cache_en_disco_gana_al_snapshot()
    {
        // Cadena (a): si hay caché parseable, sus tarifas pisan a las del snapshot.
        var path = TempCachePath();
        File.WriteAllText(path, """{ "models": { "claude-fable-5": { "input": 99, "output": 99 } } }""");
        var cat = new PricingCatalog(path, NoNet);

        Assert.Equal(99.0, cat.CostFor(Out1M("claude-fable-5")).Usd, 10);
        // Y los modelos que la caché no trae siguen resolviendo desde el snapshot (merge, no sustitución).
        Assert.True(cat.CostFor(Out1M("claude-opus-4-8")).IsPriced);
    }

    [Fact]
    public void Cache_corrupta_no_rompe_cae_al_snapshot()
    {
        var path = TempCachePath();
        File.WriteAllText(path, "esto no es JSON {{{");
        var cat = new PricingCatalog(path, NoNet);

        Assert.True(cat.CostFor(Out1M("claude-fable-5")).IsPriced);
    }

    // ---------------- matching ----------------

    [Fact]
    public void Match_exacto_ignora_mayusculas()
    {
        var cat = new PricingCatalog(TempCachePath(), NoNet);
        Assert.Equal(cat.RateFor("claude-fable-5"), cat.RateFor("Claude-Fable-5"));
    }

    [Fact]
    public void Sin_match_exacto_usa_la_familia_con_la_version_mas_alta()
    {
        var cat = new PricingCatalog(TempCachePath(), NoNet);

        // Un Opus futuro que el catálogo aún no lista → mejor Opus disponible. El más alto es
        // claude-opus-4-8 ([4,8] > [4,5] > [4]; los tokens-fecha 20250514… NO cuentan como versión):
        // input 5 / output 25, no el 15/75 del opus-4-1 antiguo.
        var rate = cat.RateFor("claude-opus-9");
        Assert.NotNull(rate);
        Assert.Equal(5.0, rate!.Value.Input);
        Assert.Equal(25.0, rate.Value.Output);
    }

    [Fact]
    public void Variantes_con_sufijo_no_listado_resuelven_por_familia()
    {
        // El id real puede llevar sufijos tipo "[1m]" (contexto 1M) que el catálogo no lista tal cual.
        var cat = new PricingCatalog(TempCachePath(), NoNet);
        var cost = cat.CostFor(Out1M("claude-fable-5[1m]"));

        Assert.True(cost.IsPriced);
        Assert.Equal(50.0, cost.Usd, 10);
    }

    // ---------------- derivación de tarifas de caché ----------------

    [Fact]
    public void Deriva_cache_write_y_cache_read_del_input_cuando_faltan()
    {
        // Proporciones públicas de Anthropic: write 5m = input×1.25, write 1h = input×2.0, read = input×0.1.
        var path = TempCachePath();
        File.WriteAllText(path, """{ "models": { "claude-test-1": { "input": 4, "output": 20 } } }""");
        var cat = new PricingCatalog(path, NoNet);

        var rate = cat.RateFor("claude-test-1");
        Assert.NotNull(rate);
        Assert.Equal(5.0, rate!.Value.CacheWrite5m, 10);  // 4 × 1.25
        Assert.Equal(8.0, rate.Value.CacheWrite1h, 10);   // 4 × 2.0
        Assert.Equal(0.4, rate.Value.CacheRead, 10);      // 4 × 0.1
    }

    // ---------------- modelo sin tarifa en NINGUNA fuente ----------------

    [Fact]
    public void Modelo_sin_tarifa_en_ninguna_fuente_devuelve_IsPriced_false()
    {
        var cat = new PricingCatalog(TempCachePath(), NoNet);

        Assert.False(cat.CostFor(Out1M("claude-mythos-1")).IsPriced);
        Assert.False(cat.CostFor(Out1M("<synthetic>")).IsPriced);
        Assert.Equal(0.0, cat.CostFor(Out1M("claude-mythos-1")).Usd);
    }

    // ---------------- refresh online (privacidad + caché atómica) ----------------

    [Fact]
    public async Task Toggle_off_no_toca_la_red()
    {
        int fetches = 0;
        var cat = new PricingCatalog(TempCachePath(),
            _ => { Interlocked.Increment(ref fetches); return Task.FromResult("{}"); });

        bool refreshed = await cat.RefreshAsync(onlineEnabled: false);

        Assert.False(refreshed);
        Assert.Equal(0, fetches); // OFF ⇒ ni un byte a la red: solo caché + snapshot
    }

    [Fact]
    public async Task Refresh_descarga_escribe_cache_y_activa_las_tarifas_nuevas()
    {
        int fetches = 0;
        string feed = File.ReadAllText(GoldenPath);
        var path = TempCachePath();
        var cat = new PricingCatalog(path,
            _ => { Interlocked.Increment(ref fetches); return Task.FromResult(feed); });

        bool refreshed = await cat.RefreshAsync(onlineEnabled: true);

        Assert.True(refreshed);
        Assert.Equal(1, fetches);
        Assert.True(File.Exists(path), "el refresh debe dejar la caché escrita (swap atómico)");
        Assert.False(File.Exists(path + ".tmp"), "no debe quedar un .tmp huérfano");
        // La caché escrita es re-parseable por una instancia nueva (round-trip completo).
        var reloaded = new PricingCatalog(path, NoNet);
        Assert.Equal(25.0, reloaded.RateFor("claude-opus-4-8")!.Value.Output);
    }

    [Fact]
    public async Task Cache_fresca_no_vuelve_a_la_red()
    {
        int fetches = 0;
        var path = TempCachePath();
        File.WriteAllText(path, """{ "models": { "claude-test-1": { "input": 1, "output": 5 } } }""");
        var cat = new PricingCatalog(path,
            _ => { Interlocked.Increment(ref fetches); return Task.FromResult("{}"); });

        bool refreshed = await cat.RefreshAsync(onlineEnabled: true);

        Assert.False(refreshed); // la caché tiene <7 días ⇒ nada que refrescar
        Assert.Equal(0, fetches);
    }

    [Fact]
    public async Task Feed_corrupto_no_escribe_cache_ni_rompe_el_catalogo()
    {
        var path = TempCachePath();
        var cat = new PricingCatalog(path, _ => Task.FromResult("esto no es JSON"));

        bool refreshed = await cat.RefreshAsync(onlineEnabled: true);

        Assert.False(refreshed);
        Assert.False(File.Exists(path), "un feed corrupto no debe pisar/crear la caché");
        Assert.True(cat.CostFor(Out1M("claude-fable-5")).IsPriced); // snapshot intacto
    }

    // ---------------- fachada Pricing ----------------

    [Fact]
    public void Pricing_CostUsd_sigue_funcionando_como_fachada()
    {
        // Los call sites históricos (UsageHistory, tests) siguen llamando Pricing.CostUsd.
        Assert.True(Pricing.CostUsd(Out1M("claude-opus-4-8")) > 0);
        Assert.Equal(0.0, Pricing.CostUsd(Out1M("claude-mythos-1")));
        Assert.False(Pricing.Cost(Out1M("claude-mythos-1")).IsPriced);
        Assert.True(Pricing.Cost(Out1M("claude-fable-5")).IsPriced); // el bug real, cerrado
    }

    // ---------------- strings localizadas (9 idiomas) ----------------

    [Fact]
    public void Strings_de_tarifa_estan_en_los_9_idiomas()
    {
        var codes = new[] { "en" }.Concat(Localization.Languages.Select(l => l.Code));
        foreach (var code in codes)
        {
            var s = Localization.Get(code);
            Assert.False(string.IsNullOrWhiteSpace(s.SpendNoRate), $"SpendNoRate vacío en '{code}'");
            Assert.False(string.IsNullOrWhiteSpace(s.PricingOnlineUpdate), $"PricingOnlineUpdate vacío en '{code}'");
            Assert.Contains("models.dev", s.PricingOnlineUpdateSubtitle); // subtítulo honesto: dice la fuente
        }
    }
}
