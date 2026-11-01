namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Colores de las fases del pomodoro (anillo, puntos del ciclo, botón principal, aviso del fin de fase). El foco lleva el acento
/// del tema (el violeta de Muesca, #7B4DFF en el notch); los descansos, un verde y un azul cielo propios. Tienen que seguir
/// distinguiéndose a simple vista: el violeta y el azul cielo, los más cercanos, se separan ~58° de tono, y el anillo del foco
/// contrasta ≥3:1 con el negro del notch. Ninguno es el rojo de los avisos. Lógica pura: la usan el painter y sus tests.
/// </summary>
public static class FocusPalette
{
    /// <summary>Descanso corto: verde (emerald-400).</summary>
    public static readonly Color ShortBreak = Color.FromArgb(52, 211, 153);

    /// <summary>Descanso largo: azul cielo (sky-400).</summary>
    public static readonly Color LongBreak = Color.FromArgb(56, 189, 248);

    /// <summary>El color de la fase como relleno o trazo (anillo, puntos, botón).</summary>
    public static Color Fill(FocusPhase phase, Theme theme) => phase switch
    {
        FocusPhase.ShortBreak => ShortBreak,
        FocusPhase.LongBreak => LongBreak,
        _ => theme.Accent,
    };

    /// <summary>El color de la fase como TEXTO pequeño («¡Foco terminado!»): el acento legible para el foco; los de los
    /// descansos ya lo son sobre el negro del notch.</summary>
    public static Color Text(FocusPhase phase, Theme theme) => phase == FocusPhase.Focus ? theme.AccentText : Fill(phase, theme);

    /// <summary>Distancia de tono (0..180°) entre dos colores, por la vuelta más corta.</summary>
    public static float HueDistance(Color a, Color b)
    {
        float d = Math.Abs(a.GetHue() - b.GetHue()) % 360f;
        return d > 180f ? 360f - d : d;
    }
}
