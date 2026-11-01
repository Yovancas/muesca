using System.Drawing;

namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Hit-test PURO del hover (Tarea 3 F3): dado un punto y una secuencia ordenada de pares
/// (clave → rect), devuelve la clave del <b>primer</b> rect que contiene el punto, o <c>null</c>
/// si ninguno. La precedencia es estable por orden de iteración — el llamador pasa los rects de
/// más específico a menos para resolver solapes de forma determinista.
///
/// <para>Solo geometría (sin GDI+/reloj/red) → 100% testeable. Reutiliza los diccionarios de rects
/// que ya mantiene <c>DashboardForm</c> (<c>_sectionRects</c>, <c>_tabRects</c>, <c>_modeRects</c>,
/// <c>_pctWinRects</c>, <c>_liveRowRects</c>, <c>_settingsRects</c>, etc.).</para>
/// </summary>
public static class HoverHitTest
{
    /// <summary>
    /// Devuelve la clave del primer par cuyo rect contiene <paramref name="point"/>, en el orden de
    /// <paramref name="rects"/>; <c>null</c> si ninguno (o el rect es vacío). No asigna ni ordena:
    /// recorre una vez.
    /// </summary>
    public static string? Resolve(Point point, IEnumerable<KeyValuePair<string, Rectangle>> rects)
    {
        foreach (var kv in rects)
        {
            var r = kv.Value;
            if (r.Width <= 0 || r.Height <= 0) continue; // un rect vacío nunca contiene el punto
            if (r.Contains(point)) return kv.Key;
        }
        return null;
    }
}
