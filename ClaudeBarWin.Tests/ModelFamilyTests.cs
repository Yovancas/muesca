using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T13a: la familia de modelo se DERIVA del id ("claude-fable-5" → "Fable") sin listas de nombres
/// conocidos — antes <c>UsageAggregator.ModelLabel</c> hacía Contains(opus/sonnet/haiku) y el gasto
/// de cualquier modelo nuevo (Fable, Mythos, los que vengan) computaba como "other" a 0 €.
/// Regla: quitar el prefijo "claude-", tomar el primer token alfabético y capitalizarlo.
/// </summary>
public class ModelFamilyTests
{
    [Theory]
    [InlineData("claude-opus-4-8", "Opus")]
    [InlineData("claude-sonnet-4-5", "Sonnet")]
    [InlineData("claude-haiku-4-5-20251001", "Haiku")]   // id con fecha al final
    [InlineData("claude-fable-5", "Fable")]              // familia nueva: sin lista que mantener
    [InlineData("claude-fable-5[1m]", "Fable")]          // variante de contexto 1M
    [InlineData("claude-mythos-1", "Mythos")]
    [InlineData("claude-3-5-sonnet-20240620", "Sonnet")] // esquema antiguo: versión antes del nombre
    [InlineData("CLAUDE-OPUS-4", "Opus")]                // case-insensitive
    public void FromId_extrae_la_familia_de_ids_claude(string id, string family)
        => Assert.Equal(family, ModelFamily.FromId(id));

    [Theory]
    [InlineData("<synthetic>")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("gpt-4o")]      // sin prefijo claude-
    [InlineData("claude-")]     // prefijo sin resto
    [InlineData("claude-2.1")]  // sin token alfabético tras el prefijo
    public void FromId_sin_patron_reconocible_cae_a_Otros(string? id)
        => Assert.Equal(ModelFamily.Other, ModelFamily.FromId(id));

    // ---- Cap de familias: el panel no crece sin límite (máx. 4 entradas incluida "Otros") ----

    [Fact]
    public void Keep_con_cuatro_o_menos_familias_las_conserva_todas()
    {
        var totals = new Dictionary<string, double>
        { ["Opus"] = 10, ["Sonnet"] = 5, ["Fable"] = 3, ["Haiku"] = 1 };
        var keep = ModelFamily.Keep(totals);
        Assert.Equal(totals.Keys.OrderBy(k => k), keep.OrderBy(k => k));
    }

    [Fact]
    public void Keep_con_mas_de_cuatro_conserva_solo_las_tres_de_mayor_gasto()
    {
        // 5 familias → top 3 por gasto; las 2 menores se funden en "Otros" (4 entradas finales).
        var totals = new Dictionary<string, double>
        { ["Opus"] = 10, ["Fable"] = 8, ["Sonnet"] = 5, ["Haiku"] = 1, ["Mythos"] = 0.5 };
        var keep = ModelFamily.Keep(totals);
        Assert.Equal(new[] { "Fable", "Opus", "Sonnet" }, keep.OrderBy(k => k));
    }

    [Fact]
    public void Keep_al_capar_nunca_conserva_Otros_como_familia_propia()
    {
        // "Otros" es el cubo de DESTINO de la fusión: aunque tenga más gasto que otra familia,
        // no ocupa una de las 3 plazas con nombre (se le suman las menores).
        var totals = new Dictionary<string, double>
        { ["Opus"] = 10, [ModelFamily.Other] = 9, ["Sonnet"] = 5, ["Haiku"] = 2, ["Fable"] = 1 };
        var keep = ModelFamily.Keep(totals);
        Assert.DoesNotContain(ModelFamily.Other, keep);
        Assert.Equal(new[] { "Haiku", "Opus", "Sonnet" }, keep.OrderBy(k => k));
    }
}
