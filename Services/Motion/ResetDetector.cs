using ClaudeBarWin.Services;

namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Detecta que una ventana de cuota <b>se ha reseteado</b> para disparar la celebración in-panel de
/// la Fase 3 (destello "✓ cuota renovada" + humor contento de la mascota). PURO: el predicado
/// <see cref="Detect"/> es estático y sin estado; la instancia recuerda la última lectura por clave
/// (5h/7d) y dispara la celebración una <b>sola vez</b> por reset (no re-dispara con la misma
/// lectura). Sin reloj/red por dentro → determinista y testeable.
///
/// <para>Señales de reset (cualquiera basta):</para>
/// <list type="bullet">
/// <item>El <c>ResetsAt</c> <b>salta hacia adelante</b> más de <see cref="JumpThresholdMinutes"/>
/// minutos: la ventana arrancó de cero (nuevo horizonte de reset).</item>
/// <item>La utilización <b>cae en picado</b> (≥ <see cref="UtilFallPoints"/> puntos): se liberó
/// cuota de golpe, típico del reset aunque no se conozca el <c>ResetsAt</c>.</item>
/// </list>
/// Esto NO toca el sistema de notificaciones (eso es F4): es solo la señal visual del panel.
/// </summary>
public sealed class ResetDetector
{
    /// <summary>Salto mínimo del <c>ResetsAt</c> (minutos) para considerarlo una ventana nueva.</summary>
    public const double JumpThresholdMinutes = 10.0;

    /// <summary>Caída mínima de utilización (puntos porcentuales) para delatar un reset.</summary>
    public const double UtilFallPoints = 25.0;

    /// <summary>
    /// ¿La transición <paramref name="prev"/>→<paramref name="next"/> delata un reset de la ventana?
    /// Sin lectura previa (o lecturas nulas sin señal fiable) ⇒ <c>false</c>. PURO: misma entrada,
    /// misma salida.
    /// </summary>
    public static bool Detect(UsageWindow? prev, UsageWindow? next)
    {
        if (prev is null || next is null) return false;

        // (a) El ResetsAt salta muy hacia adelante: arrancó una ventana nueva.
        if (prev.ResetsAt is { } p && next.ResetsAt is { } n
            && (n - p).TotalMinutes > JumpThresholdMinutes)
            return true;

        // (b) La utilización cae en picado: se liberó cuota de golpe (reset).
        if (prev.UtilizationPct - next.UtilizationPct >= UtilFallPoints)
            return true;

        return false;
    }

    private readonly Dictionary<string, UsageWindow> _last = new();

    /// <summary>
    /// Registra la lectura <paramref name="window"/> de la ventana <paramref name="key"/> ("5h"/"7d")
    /// y devuelve <c>true</c> si respecto a la lectura anterior se detecta un reset (dispara una sola
    /// vez: la lectura queda guardada como nueva base). La primera lectura nunca dispara (no hay con
    /// qué comparar). Una lectura <c>null</c> se ignora (no actualiza la base ni dispara).
    /// </summary>
    public bool Observe(string key, UsageWindow? window)
    {
        if (window is null) return false;

        bool fired = _last.TryGetValue(key, out var prev) && Detect(prev, window);
        _last[key] = window;
        return fired;
    }
}
