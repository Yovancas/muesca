namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Funciones de easing PURAS: <c>t∈[0,1] → valor</c>. Sin estado, sin reloj, sin aleatoriedad
/// (regla de oro F3: el núcleo de animación es puro y elapsed-driven). La entrada se clampa a
/// [0,1] para que el muestreo nunca extrapole. <see cref="OutBack"/> sobrepasa 1 a propósito
/// (overshoot) para alimentar el bote de atención.
/// </summary>
public static class Easing
{
    /// <summary>Constante de overshoot estándar de las curvas "back" (~1.70158).</summary>
    private const double BackC1 = 1.70158;
    private const double BackC3 = BackC1 + 1.0;

    private static double Clamp01(double t) => t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);

    /// <summary>Ease-out cúbica: arranca rápido y frena. f(0)=0, f(1)=1, f(0.5)&gt;0.5.</summary>
    public static double OutCubic(double t)
    {
        t = Clamp01(t);
        double inv = 1.0 - t;
        return 1.0 - inv * inv * inv;
    }

    /// <summary>Ease-out cuadrática. Más suave que la cúbica. f(0)=0, f(1)=1, f(0.5)&gt;0.5.</summary>
    public static double OutQuad(double t)
    {
        t = Clamp01(t);
        double inv = 1.0 - t;
        return 1.0 - inv * inv;
    }

    /// <summary>Ease-in-out cúbica simétrica: lenta en los extremos, rápida en el medio. f(0.5)=0.5.</summary>
    public static double InOutCubic(double t)
    {
        t = Clamp01(t);
        if (t < 0.5)
            return 4.0 * t * t * t;
        double f = -2.0 * t + 2.0;
        return 1.0 - f * f * f / 2.0;
    }

    /// <summary>
    /// Ease-out con overshoot: sobrepasa 1 antes de asentarse en 1. Útil para el "boing" del
    /// bote de atención. f(0)=0, f(1)=1, pero f(t)&gt;1 para algún t∈(0,1).
    /// </summary>
    public static double OutBack(double t)
    {
        t = Clamp01(t);
        double p = t - 1.0;
        return 1.0 + BackC3 * p * p * p + BackC1 * p * p;
    }
}
