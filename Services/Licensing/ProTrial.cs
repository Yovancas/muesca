namespace ClaudeBarWin.Services.Licensing;

/// <summary>
/// La prueba de 7 días (solo en la edición de la Store). PURA: el instante llega por parámetro. La fecha de inicio se guarda
/// en <c>config.json</c> la primera vez que arranca la edición de la Store y no hace falta que sea a prueba de trampas
/// (decisión del diseño: quien la trampee no iba a pagar).
/// </summary>
public static class ProTrial
{
    public const int Days = 7;
    public static readonly TimeSpan Length = TimeSpan.FromDays(Days);

    /// <summary>Días que quedan (1..7, redondeando hacia arriba: el último día es «1») mientras dura; null sin empezar o
    /// acabada. Con el reloj atrasado (inicio en el futuro), como mucho 7.</summary>
    public static int? DaysLeft(DateTime? startUtc, DateTime nowUtc)
    {
        if (startUtc is not { } start) return null;
        var left = start + Length - nowUtc;
        if (left <= TimeSpan.Zero) return null;
        return Math.Clamp((int)Math.Ceiling(left.TotalDays), 1, Days);
    }

    /// <summary>¿Empezó y ya se acabó?</summary>
    public static bool Ended(DateTime? startUtc, DateTime nowUtc) => startUtc is not null && DaysLeft(startUtc, nowUtc) is null;

    /// <summary>Cuánto falta para que cambien los días que quedan (o para que acabe); null si no está en marcha. Un único
    /// temporizador a ese instante basta: sin sondeos.</summary>
    public static TimeSpan? NextChangeIn(DateTime? startUtc, DateTime nowUtc)
    {
        if (startUtc is not { } start || DaysLeft(start, nowUtc) is null) return null;
        var left = start + Length - nowUtc;
        var whole = TimeSpan.FromDays(Math.Ceiling(left.TotalDays) - 1);
        var d = left - whole;
        return d > TimeSpan.Zero ? d : TimeSpan.FromDays(1);
    }

    /// <summary>¿Toca el único aviso «tu prueba ha terminado»? Gratis, con una prueba que acabó y aún sin enseñarlo (al
    /// enseñarlo se apunta en <c>config.json</c>: nunca insiste).</summary>
    public static bool EndNoticeDue(ProState state, bool trialEnded, bool alreadyShown) =>
        state == ProState.Free && trialEnded && !alreadyShown;

    /// <summary>El estado con la compra y la prueba: comprado manda; si no, la prueba mientras dure; si no, gratis.</summary>
    public static ProState Resolve(bool ownsPro, DateTime? trialStartUtc, DateTime nowUtc) =>
        ownsPro ? ProState.Pro : DaysLeft(trialStartUtc, nowUtc) is not null ? ProState.Trial : ProState.Free;
}
