using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Bestiario ASCII propio (clean-room). Un único tamaño: el gatito compacto de 4 líneas
/// (la talla "grande" se retiró en v0.3.7 a petición de Yovan — solo queda la compacta).
/// La cara (ojos + nariz) cambia por fase; el cuerpo es fijo.
/// El animador cicla frames con (frameIndex % Count): Idle y Ended son estáticos;
/// el resto parpadea/pulsa con 2 frames.
/// </summary>
public static class MascotSprite
{
    /// <summary>Frames multilínea (cada frame = varias líneas) por fase.</summary>
    public static IReadOnlyList<string[]> Frames(SessionPhase phase) => Compact(phase);

    public static string LabelKey(SessionPhase phase) => phase.ToString();

    /// <summary>Cara base (ojo · nariz · ojo) de la fase.</summary>
    public static string BaseFace(SessionPhase phase) => Face(phase).Base;

    /// <summary>Frame completo con una cara concreta (p. ej. la del aterrizaje del Salto).</summary>
    public static string[] FrameWithFace(string face) => Frame(face);

    // Cara de 3 chars (ojo · nariz · ojo) por fase. Item1 = pose base, Item2 = pose alterna (parpadeo/guiño/pulso).
    private static (string Base, string Alt) Face(SessionPhase p) => p switch
    {
        SessionPhase.Idle               => ("-.-", "-.-"), // relajado, ojos entornados
        SessionPhase.Processing         => ("o.o", "-.-"), // trabajando: parpadeo
        SessionPhase.WaitingForApproval => ("O.O", "o.o"), // ¡atención! ojos abiertos que pulsan
        SessionPhase.WaitingForInput    => ("^.^", "^.-"), // contento, te espera: guiño
        SessionPhase.Compacting         => ("@.@", "o.o"), // mareado comprimiendo memoria
        SessionPhase.Ended              => ("x.x", "x.x"), // KO
        _                               => ("-.-", "-.-"),
    };

    // Idle y Ended se quedan quietos; los estados con trabajo o que piden atención cobran vida.
    private static bool Animated(SessionPhase p) =>
        p is SessionPhase.Processing or SessionPhase.Compacting
          or SessionPhase.WaitingForApproval or SessionPhase.WaitingForInput;

    // Gato propio, 4 líneas. Centrado en la columna de la nariz; cara variable, cuerpo fijo.
    private static IReadOnlyList<string[]> Compact(SessionPhase p)
    {
        var (baseF, altF) = Face(p);
        return Animated(p) ? new[] { Frame(baseF), Frame(altF) } : new[] { Frame(baseF) };
    }

    private static string[] Frame(string f) => new[]
    {
        " /\\_/\\",
        $"( {f} )",
        " > ^ <",
        "(\")_(\")",
    };
}
