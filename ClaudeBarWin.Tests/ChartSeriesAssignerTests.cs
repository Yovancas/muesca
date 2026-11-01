using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T13a: el color de cada serie del gráfico se asigna por RANGO de gasto (1.º → slot 0, 2.º → slot 1…)
/// pero de forma ESTABLE dentro de la sesión: mientras una familia siga presente conserva su slot
/// aunque el orden de gasto baile entre refrescos (que Opus no cambie de color cada 30 s). Las
/// familias ausentes liberan su slot para la siguiente nueva.
/// </summary>
public class ChartSeriesAssignerTests
{
    [Fact]
    public void Primer_reparto_asigna_slots_por_rango_de_gasto()
    {
        var a = new ChartSeriesAssigner();
        var slots = a.Assign(new[] { "Opus", "Fable", "Haiku" }); // ya ordenadas por gasto desc

        Assert.Equal(0, slots["Opus"]);
        Assert.Equal(1, slots["Fable"]);
        Assert.Equal(2, slots["Haiku"]);
    }

    [Fact]
    public void Familia_presente_conserva_su_slot_aunque_cambie_el_rango()
    {
        var a = new ChartSeriesAssigner();
        a.Assign(new[] { "Opus", "Fable" });
        var slots = a.Assign(new[] { "Fable", "Opus" }); // Fable adelanta a Opus en gasto

        Assert.Equal(0, slots["Opus"]);  // no cambia de color entre refrescos
        Assert.Equal(1, slots["Fable"]);
    }

    [Fact]
    public void Familia_ausente_libera_su_slot_para_la_siguiente_nueva()
    {
        var a = new ChartSeriesAssigner();
        a.Assign(new[] { "Opus", "Sonnet", "Haiku" });
        var slots = a.Assign(new[] { "Opus", "Haiku", "Mythos" }); // Sonnet desaparece, llega Mythos

        Assert.Equal(0, slots["Opus"]);
        Assert.Equal(2, slots["Haiku"]);
        Assert.Equal(1, slots["Mythos"]); // hereda el slot liberado por Sonnet
        Assert.DoesNotContain("Sonnet", slots.Keys);
    }

    [Fact]
    public void Familia_que_reaparece_toma_el_slot_libre_mas_bajo()
    {
        var a = new ChartSeriesAssigner();
        a.Assign(new[] { "Opus", "Fable" });
        a.Assign(new[] { "Fable" });                     // Opus desaparece → libera el 0
        var slots = a.Assign(new[] { "Fable", "Opus" }); // y vuelve

        Assert.Equal(1, slots["Fable"]); // estable
        Assert.Equal(0, slots["Opus"]);  // reasignado al hueco más bajo
    }

    [Fact]
    public void Asignaciones_repetidas_con_las_mismas_familias_son_idempotentes()
    {
        // El draw llama a Assign en las pasadas de medir y pintar: misma entrada → mismo resultado.
        var a = new ChartSeriesAssigner();
        var first = a.Assign(new[] { "Opus", "Fable", "Haiku" });
        var second = a.Assign(new[] { "Opus", "Fable", "Haiku" });

        Assert.Equal(first.OrderBy(kv => kv.Key), second.OrderBy(kv => kv.Key));
    }
}
