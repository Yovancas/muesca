using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T9: el subtítulo del plan en la cabecera repetía la palabra del plan ("Max · Max 5x"). El nivel
/// (tier) ya incluye el nombre del plan, así que se muestra una sola vez con prefijo "Plan" →
/// "Plan Max · 5x". Cubre los casos reales de <c>.credentials.json</c>.
/// </summary>
public class PlanInfoTests
{
    [Fact]
    public void Max_5x_is_not_duplicated()
    {
        // SubscriptionType "max" + tier "...max_5x": antes "Max · Max 5x" (repetía Max). Ahora una sola vez.
        var p = new PlanInfo("max", "default_claude_max_5x");
        Assert.Equal("Plan Max · 5x", p.Display);
    }

    [Fact]
    public void Max_20x_is_not_duplicated()
    {
        var p = new PlanInfo("max", "default_claude_max_20x");
        Assert.Equal("Plan Max · 20x", p.Display);
    }

    [Fact]
    public void Pro_shows_plan_prefix_without_redundant_word()
    {
        // tier "Pro" coincide con el sub "Pro" → no repetir: "Plan Pro".
        var p = new PlanInfo("pro", "default_claude_pro");
        Assert.Equal("Plan Pro", p.Display);
    }

    [Fact]
    public void Unknown_tier_shows_only_the_subscription_with_prefix()
    {
        var p = new PlanInfo("team", "some_unrecognised_tier");
        Assert.Equal("Plan Team", p.Display);
    }

    [Fact]
    public void Empty_subscription_keeps_unknown_label()
    {
        Assert.Equal("Plan desconocido", new PlanInfo("", "").Display);
    }

    [Fact]
    public void Display_never_repeats_the_subscription_word()
    {
        // Invariante general: el nombre del plan aparece como mucho una vez en el texto.
        foreach (var tier in new[] { "default_claude_max_5x", "default_claude_max_20x", "default_claude_pro" })
        {
            var d = new PlanInfo("max", tier).Display;
            int count = d.Split("Max", StringSplitOptions.None).Length - 1;
            Assert.True(count <= 1, $"'{d}' repite el nombre del plan");
        }
    }
}
