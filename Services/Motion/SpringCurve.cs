namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Respuesta CERRADA de un muelle de masa 1 que arranca en reposo en 0 y va hacia 1, para las curvas
/// sin estado del panel (fundido por sección, chip). Coincide con integrar un <see cref="SpringValue"/>
/// con el mismo preset; los umbrales del preset no intervienen. PURA: recibe el tiempo en ms.
/// </summary>
public static class SpringCurve
{
    /// <summary>Progreso (sin recortar: Abrir pasa de 1) a <paramref name="tMs"/> del arranque.</summary>
    public static double Progress(double tMs, SpringPreset p)
    {
        if (!(tMs > 0)) return 0;
        double t = tMs / 1000.0;
        double w = Math.Sqrt(p.Stiffness);
        double z = p.Damping / (2 * w);
        if (Math.Abs(z - 1) < 1e-6)
            return 1 - (1 + w * t) * Math.Exp(-w * t);
        if (z < 1)
        {
            double wd = w * Math.Sqrt(1 - z * z);
            return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + z * w / wd * Math.Sin(wd * t));
        }
        double r = w * Math.Sqrt(z * z - 1);
        double r1 = -z * w + r, r2 = -z * w - r;
        return 1 - (r2 * Math.Exp(r1 * t) - r1 * Math.Exp(r2 * t)) / (r2 - r1);
    }
}
