using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T13a: el agregado de gasto agrupa por familia DINÁMICA (<see cref="ModelFamily.FromId"/>), no por
/// la lista fija Opus/Sonnet/Haiku — el uso real de claude-fable-5 aparecía como "other". Además, con
/// más de 4 familias las menores se funden en "Otros" (cap del panel) sin perder gasto ni tokens.
/// </summary>
public class UsageAggregatorTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);

    private static UsageRecord Rec(string model, double hoursAgo, long outputTokens = 1_000_000)
        => new(Now.AddHours(-hoursAgo), model, 0, outputTokens, 0, 0, 0);

    private static UsageSnapshot Build(params UsageRecord[] recs)
        => UsageAggregator.Build(recs, Now, TimeSpan.FromHours(5), TimeSpan.FromDays(7));

    [Fact]
    public void Agrupa_por_familia_dinamica_no_por_lista_fija()
    {
        var snap = Build(
            Rec("claude-fable-5", 1), Rec("claude-mythos-1", 2), Rec("claude-opus-4-8", 3));

        Assert.Contains("Fable", snap.Week.CostByModel.Keys);
        Assert.Contains("Mythos", snap.Week.CostByModel.Keys);
        Assert.Contains("Opus", snap.Week.CostByModel.Keys);
        Assert.DoesNotContain("other", snap.Week.CostByModel.Keys); // la clave legacy desaparece
    }

    [Fact]
    public void Familia_sin_registros_en_la_ventana_no_aparece()
    {
        // Desaparición natural: el registro Opus queda FUERA de la ventana semanal → ni rastro.
        var snap = Build(Rec("claude-opus-4-8", 24 * 8), Rec("claude-fable-5", 1));

        Assert.DoesNotContain("Opus", snap.Week.CostByModel.Keys);
        Assert.Contains("Fable", snap.Week.CostByModel.Keys);
    }

    [Fact]
    public void Con_mas_de_cuatro_familias_las_menores_se_funden_en_Otros()
    {
        // 4 con tarifa (Fable/Opus/Sonnet/Haiku, T13b: el catálogo ya tarifica Fable) + 2 sin
        // patrón/familia nueva → 6 familias. El cap deja como mucho 4 entradas: las 3 de mayor
        // gasto con nombre + "Otros" con el resto fundido.
        var snap = Build(
            Rec("claude-opus-4-8", 1), Rec("claude-sonnet-4-5", 2), Rec("claude-haiku-4-5-20251001", 3),
            Rec("claude-fable-5", 4), Rec("claude-mythos-1", 5), Rec("<synthetic>", 6));

        Assert.True(snap.Week.CostByModel.Count <= 4,
            $"el panel no debe crecer sin límite: {snap.Week.CostByModel.Count} familias tras el cap");
        Assert.Contains(ModelFamily.Other, snap.Week.CostByModel.Keys);
        Assert.Contains("Fable", snap.Week.CostByModel.Keys);        // 50 $/Mtok out: top de gasto
        Assert.Contains("Opus", snap.Week.CostByModel.Keys);
        Assert.DoesNotContain("Haiku", snap.Week.CostByModel.Keys);  // fundida en Otros
        Assert.DoesNotContain("Mythos", snap.Week.CostByModel.Keys); // fundida en Otros
    }

    [Fact]
    public void El_cap_no_pierde_gasto_ni_tokens()
    {
        var recs = new[]
        {
            Rec("claude-opus-4-8", 1), Rec("claude-sonnet-4-5", 2), Rec("claude-haiku-4-5-20251001", 3),
            Rec("claude-fable-5", 4), Rec("claude-mythos-1", 5), Rec("<synthetic>", 6)
        };
        var capped = Build(recs).Week;

        double expectedCost = recs.Sum(Pricing.CostUsd);
        Assert.Equal(expectedCost, capped.CostByModel.Values.Sum(), 10);
        Assert.Equal(recs.Sum(r => r.TotalTokens), capped.TokensByModel.Values.Sum());
    }

    [Fact]
    public void Las_familias_conservadas_al_capar_son_las_de_mayor_gasto()
    {
        // Catálogo models.dev (T13b): Fable 50 $/Mtok de salida, Opus 4.8 = 25, Sonnet 15, Haiku 5;
        // mythos sin tarifa (no suma). El cap conserva las 3 de mayor gasto + Otros.
        var snap = Build(
            Rec("claude-opus-4-8", 1), Rec("claude-sonnet-4-5", 2), Rec("claude-haiku-4-5-20251001", 3),
            Rec("claude-fable-5", 4), Rec("claude-mythos-1", 5));

        var ordered = snap.Week.CostByModel.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        Assert.Equal("Fable", ordered[0]);
        Assert.Equal("Opus", ordered[1]);
        Assert.Equal("Sonnet", ordered[2]);
    }

    // ---- T13b: modelos sin tarifa en NINGUNA fuente (IsPriced=false) ----

    [Fact]
    public void Familia_sin_tarifa_no_suma_al_total_pero_su_fila_existe()
    {
        // "No mentir con 0": el gasto total solo suma lo TARIFADO; la familia sin tarifa queda
        // presente (la UI pinta "—") y marcada como unpriced.
        var snap = Build(Rec("claude-opus-4-8", 1), Rec("claude-mythos-1", 2));

        double opusOnly = Pricing.CostUsd(Rec("claude-opus-4-8", 1));
        Assert.Equal(opusOnly, snap.Week.CostUsd, 10);
        Assert.Contains("Mythos", snap.Week.CostByModel.Keys);
        Assert.Equal(0.0, snap.Week.CostByModel["Mythos"]);
        Assert.True(snap.Week.IsUnpriced("Mythos"));
        Assert.False(snap.Week.IsUnpriced("Opus"));
    }

    [Fact]
    public void Fable_ya_no_computa_cero_el_bug_real()
    {
        // El usuario usa claude-fable-5 a diario y su gasto computaba 0 € (lista fija en código).
        var snap = Build(Rec("claude-fable-5", 1));

        Assert.True(snap.Week.CostUsd > 0, "claude-fable-5 debe tener tarifa vía catálogo");
        Assert.False(snap.Week.IsUnpriced("Fable"));
    }

    [Fact]
    public void Al_capar_una_familia_sin_tarifa_el_marcador_pasa_a_Otros_solo_si_Otros_queda_a_cero()
    {
        // 4 tarifadas + mythos sin tarifa → mythos (gasto 0) se funde en Otros. Otros hereda el
        // marcador unpriced, pero IsUnpriced solo lo expone si Otros NO tiene gasto tarifado
        // (aquí Haiku también cae en Otros con gasto > 0 ⇒ Otros muestra su $ real, no "—").
        var snap = Build(
            Rec("claude-opus-4-8", 1), Rec("claude-sonnet-4-5", 2), Rec("claude-haiku-4-5-20251001", 3),
            Rec("claude-fable-5", 4), Rec("claude-mythos-1", 5));

        Assert.Contains(ModelFamily.Other, snap.Week.CostByModel.Keys);
        Assert.True(snap.Week.CostByModel[ModelFamily.Other] > 0);
        Assert.False(snap.Week.IsUnpriced(ModelFamily.Other));
    }

    [Fact]
    public void Ids_sin_patron_computan_en_Otros()
    {
        var snap = Build(Rec("<synthetic>", 1), Rec("", 2));
        Assert.Contains(ModelFamily.Other, snap.Week.CostByModel.Keys);
        Assert.Single(snap.Week.CostByModel);
    }

    // ---- UsageHistory: la gráfica bucketiza por la misma familia dinámica ----

    [Fact]
    public void UsageHistory_bucketiza_por_familia_dinamica()
    {
        var recs = new[] { new UsageRecord(Now.AddMinutes(-10), "claude-opus-4-8", 0, 1_000_000, 0, 0, 0) };
        var buckets = UsageHistory.Build(recs, ChartRange.Hours5, Now, Localization.Get("en").Culture);

        Assert.Contains(buckets, b => b.Cost("Opus") > 0);
        Assert.All(buckets, b => Assert.Equal(0, b.Cost("Sonnet")));
        Assert.Equal(buckets.Sum(b => b.CostUsd), buckets.Sum(b => b.Cost("Opus")), 10);
    }

    [Fact]
    public void UsageHistory_familia_fuera_de_la_ventana_no_aparece_en_ningun_bucket()
    {
        var recs = new[] { new UsageRecord(Now.AddDays(-2), "claude-opus-4-8", 0, 1_000_000, 0, 0, 0) };
        var buckets = UsageHistory.Build(recs, ChartRange.Hours5, Now, Localization.Get("en").Culture);

        Assert.All(buckets, b => Assert.DoesNotContain("Opus", b.CostByFamily.Keys));
    }
}
