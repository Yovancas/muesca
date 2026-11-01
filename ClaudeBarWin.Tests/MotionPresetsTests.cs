using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Presets del lenguaje de movimiento (§2): siete pares (k, c) con sus umbrales de reposo por unidad.
/// Las cifras de la tabla (95 %, pasada, pico) salen de simular el propio <see cref="SpringValue"/>
/// a 1 ms por paso; las tolerancias cubren la diferencia con el subpaso de 0,25 ms del documento.
/// </summary>
public class MotionPresetsTests
{
    // Simula un muelle de 0 → target y devuelve (ms hasta el 95 %, valor máximo, ms del máximo, ms hasta asentar).
    private static (double T95, double Max, double TMax, double TSettle) Run(SpringPreset p, double target)
    {
        var s = new SpringValue(0, p);
        s.SetTarget(target);
        double t95 = double.NaN, max = 0, tMax = 0, tSettle = double.NaN;
        for (int ms = 1; ms <= 3000; ms++)
        {
            s.Advance(0.001);
            if (double.IsNaN(t95) && s.Value >= 0.95 * target) t95 = ms;
            if (s.Value > max) { max = s.Value; tMax = ms; }
            if (s.IsSettled) { tSettle = ms; break; }
        }
        return (t95, max, tMax, tSettle);
    }

    [Fact]
    public void Abrir_overshoots_2_9_percent_and_reaches_95_at_186ms()
    {
        Assert.Equal(new SpringPreset(280, 25, 0.5, 5), Motion.Abrir);
        var r = Run(Motion.Abrir, 300);
        Assert.InRange(r.T95, 180, 192);
        Assert.InRange(r.Max - 300, 8.2, 9.2);       // 8,7 px en Δ300
        Assert.InRange(r.TMax, 272, 292);             // pico a 282 ms
        Assert.InRange(r.TSettle, 480, 560);          // encaje ~520 ms
    }

    [Fact]
    public void Cerrar_is_critical_and_74_percent_of_abrir()
    {
        Assert.Equal(new SpringPreset(1200, 69, 0.5, 5), Motion.Cerrar);
        var r = Run(Motion.Cerrar, 300);
        Assert.InRange(r.T95, 132, 142);
        Assert.True(r.Max <= 300.0001, $"Cerrar no rebasa: {r.Max}");
        Assert.InRange(r.TSettle, 250, 320);
        double ratio = r.T95 / Run(Motion.Abrir, 300).T95;
        Assert.InRange(ratio, 0.65, 0.75);            // regla 3: salir dura 65-75 % de entrar
    }

    [Theory]
    [InlineData("Ajuste", 279)]
    [InlineData("Blando", 375)]
    public void Data_presets_reach_95_without_overshoot(string name, double t95)
    {
        var p = name == "Ajuste" ? Motion.Ajuste : Motion.Blando;
        var r = Run(p, 300);
        Assert.InRange(r.T95, t95 - 8, t95 + 8);
        Assert.True(r.Max <= 300.0001);
    }

    [Theory]
    [InlineData("Rapido", 159, 0.40)]
    [InlineData("HoverIn", 95, 0.25)]
    public void Opacity_presets_settle_finely_on_0_to_1(string name, double t95, double maxSettleS)
    {
        var p = name == "Rapido" ? Motion.Rapido : Motion.HoverIn;
        Assert.Equal(0.002, p.EpsX);
        Assert.Equal(0.02, p.EpsV);
        var r = Run(p, 1.0);
        Assert.InRange(r.T95, t95 - 6, t95 + 6);
        Assert.True(r.TSettle <= maxSettleS * 1000, $"asienta a {r.TSettle} ms");
    }

    // Atención: pico = 0,0326·v₀ a los 71 ms (v₀ 186 → 6 px; 124 → 4 px; 248 → 8 px).
    [Theory]
    [InlineData(124, 4.0)]
    [InlineData(186, 6.1)]
    [InlineData(248, 8.1)]
    public void Atencion_impulse_peaks_at_71ms(double v0, double peak)
    {
        var s = new SpringValue(0, Motion.Atencion);
        s.Impulse(v0);
        double max = 0, tMax = 0;
        for (int ms = 1; ms <= 400; ms++)
        {
            s.Advance(0.001);
            if (s.Value > max) { max = s.Value; tMax = ms; }
        }
        Assert.InRange(max, peak - 0.2, peak + 0.2);
        Assert.InRange(tMax, 66, 76);
    }

    [Fact]
    public void Unit_variants_share_the_pair_and_change_only_thresholds()
    {
        Assert.Equal((Motion.Ajuste.Stiffness, Motion.Ajuste.Damping), (Motion.AjustePct.Stiffness, Motion.AjustePct.Damping));
        Assert.Equal((Motion.Blando.Stiffness, Motion.Blando.Damping), (Motion.BlandoPct.Stiffness, Motion.BlandoPct.Damping));
        Assert.Equal((Motion.Ajuste.Stiffness, Motion.Ajuste.Damping), (Motion.AjusteAng.Stiffness, Motion.AjusteAng.Damping));
        Assert.Equal((Motion.Blando.Stiffness, Motion.Blando.Damping), (Motion.BlandoAng.Stiffness, Motion.BlandoAng.Damping));
        Assert.Equal((0.05, 0.5), (Motion.AjustePct.EpsX, Motion.AjustePct.EpsV));
        Assert.Equal((0.1, 1.0), (Motion.AjusteAng.EpsX, Motion.AjusteAng.EpsV));
        Assert.Equal(Motion.Rapido, Motion.Rapido01);
    }

    // Con el umbral fino de opacidad, el encaje final de un fundido 0..1 no se ve (< 0,5 %).
    [Fact]
    public void Opacity_snap_is_invisible()
    {
        var s = new SpringValue(0, Motion.Rapido);
        s.SetTarget(1.0);
        double before = 0;
        for (int ms = 1; ms <= 1000 && !s.IsSettled; ms++)
        {
            before = s.Value;
            s.Advance(0.001);
        }
        Assert.Equal(1.0, s.Value);
        Assert.True(1.0 - before < 0.005, $"salto final {1.0 - before}");
    }

    // Los presets heredados (notch) conservan sus umbrales globales.
    [Fact]
    public void Legacy_presets_keep_global_thresholds()
    {
        Assert.Equal(Motion.SpringEpsX, Motion.SpringSoft.EpsX);
        Assert.Equal(Motion.SpringEpsV, Motion.SpringCritical.EpsV);
    }

    // Asentado = mismos umbrales que el encaje de Advance: un muelle en reposo no deja residuo.
    [Fact]
    public void IsSettled_uses_the_preset_thresholds()
    {
        var s = new SpringValue(0, Motion.Ajuste);
        s.SetTarget(0.4);   // por debajo de 0,5 px: ya está «en reposo» para la geometría
        Assert.True(s.IsSettled);
        var o = new SpringValue(0, Motion.Rapido);
        o.SetTarget(0.4);   // en opacidad 0,4 es un fundido entero
        Assert.False(o.IsSettled);
    }

    // §4 #8: el color de humor/fase se mezcla con Rápido (95 % a ~159 ms).
    [Fact]
    public void Color_crossfade_reaches_95_percent_with_rapido()
    {
        var fade = new ColorCrossfade(System.Drawing.Color.FromArgb(255, 0, 0, 0));
        fade.SetTarget(System.Drawing.Color.FromArgb(255, 200, 0, 0));
        int ms = 0;
        while (fade.Current.R < 190 && ms < 1000) { fade.Advance(0.001); ms++; }
        Assert.InRange(ms, 150, 168);
    }

    // §4 #9: el alto del panel no rebasa su objetivo (Ajuste está críticamente amortiguado).
    [Fact]
    public void Panel_height_preset_is_ajuste() => Assert.True(Motion.Ajuste.Damping >= 2 * Math.Sqrt(Motion.Ajuste.Stiffness));
}
