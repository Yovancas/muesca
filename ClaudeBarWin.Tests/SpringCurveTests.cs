using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="SpringCurve"/>: respuesta cerrada de un muelle de masa 1 que arranca en reposo en 0 hacia 1.
/// Sirve para curvas sin estado (fundidos por sección, chip) y coincide con <see cref="SpringValue"/>.
/// </summary>
public class SpringCurveTests
{
    [Theory]
    [InlineData("Rapido")]
    [InlineData("Abrir")]
    [InlineData("Ajuste")]
    [InlineData("Blando")]
    [InlineData("Cerrar")]
    public void Closed_form_matches_the_integrated_spring(string name)
    {
        var p = name switch
        {
            "Rapido" => Motion.Rapido, "Abrir" => Motion.Abrir, "Ajuste" => Motion.Ajuste,
            "Blando" => Motion.Blando, _ => Motion.Cerrar,
        };
        var s = new SpringValue(0, p with { EpsX = 1e-9, EpsV = 1e-9 });
        s.SetTarget(1);
        for (int ms = 1; ms <= 600; ms++)
        {
            for (int k = 0; k < 4; k++) s.Advance(0.00025);   // subpaso de 0,25 ms, como el documento
            // Euler semiimplícito frente a la solución exacta: < 1 % de diferencia.
            if (ms % 25 == 0) Assert.True(Math.Abs(s.Value - SpringCurve.Progress(ms, p)) < 0.01, $"t={ms}");
        }
    }

    [Fact]
    public void Starts_at_zero_and_clamps_negative_time()
    {
        Assert.Equal(0.0, SpringCurve.Progress(0, Motion.Rapido), 12);
        Assert.Equal(0.0, SpringCurve.Progress(-10, Motion.Rapido), 12);
        Assert.Equal(1.0, SpringCurve.Progress(5000, Motion.Rapido), 6);
    }

    [Fact]
    public void Abrir_overshoots_and_critical_does_not()
    {
        double max = 0, maxC = 0;
        for (int t = 0; t < 800; t++)
        {
            max = Math.Max(max, SpringCurve.Progress(t, Motion.Abrir));
            maxC = Math.Max(maxC, SpringCurve.Progress(t, Motion.Rapido));
        }
        Assert.InRange(max, 1.025, 1.033);   // pasada 2,9 %
        Assert.True(maxC <= 1.0);
    }
}
