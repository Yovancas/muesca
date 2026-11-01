namespace ClaudeBarWin.Services;

/// <summary>
/// Envoltorio de texto por palabras, <b>puro</b>: el cálculo de ancho se inyecta como
/// <c>Func&lt;string,double&gt;</c> (en producción es <c>g.MeasureString(s, font).Width</c>), así
/// que el algoritmo se testea sin GDI+/fuentes. Lo usa el footer del panel para que el sello de
/// privacidad y el hint dejen de truncarse al ancho fijo (340 px) — F2 dejó esa pega abierta.
/// </summary>
public static class TextWrap
{
    /// <summary>
    /// Parte <paramref name="text"/> en líneas que no excedan <paramref name="maxWidth"/> según
    /// <paramref name="measure"/>, rompiendo solo en límites de palabra. Una palabra más ancha que
    /// el máximo se mantiene en su propia línea (no se trunca ni se pierde). Colapsa secuencias de
    /// espacios. Texto vacío / solo espacios devuelve una única línea vacía. Determinista.
    /// </summary>
    public static List<string> WordWrap(string text, double maxWidth, Func<string, double> measure)
    {
        var words = (text ?? string.Empty).Split(' ', '\t', '\n', '\r')
            .Where(w => w.Length > 0)
            .ToArray();
        var lines = new List<string>();
        if (words.Length == 0) { lines.Add(string.Empty); return lines; }

        string current = string.Empty;
        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current = word; // primera palabra de la línea: entra siempre (aunque desborde sola).
                continue;
            }
            string candidate = current + " " + word;
            if (measure(candidate) <= maxWidth)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        lines.Add(current);
        return lines;
    }

    /// <summary>
    /// Como <see cref="WordWrap"/>, pero trata el separador suelto (por defecto "·") como punto de
    /// ruptura PREFERENTE (T9a, §3 #10): el texto se agrupa en segmentos entre separadores y, al no
    /// caber el siguiente segmento CON su " · ", se rompe la línea ahí y el separador se OMITE (ni
    /// cierra la línea anterior ni abre la nueva — antes el footer ES dejaba líneas que empezaban
    /// por "· sin telemetría"). Un segmento más ancho que el máximo cae al wrap por palabras; el
    /// segmento siguiente puede reengancharse a su última línea si cabe con separador. Puro y
    /// determinista (misma entrada → misma salida) → medir==pintar.
    /// </summary>
    public static List<string> WordWrapSeparated(string text, double maxWidth,
        Func<string, double> measure, string separator = "·")
    {
        // Tokeniza como WordWrap y agrupa en segmentos partiendo en cada separador SUELTO (token
        // exacto). Separadores consecutivos / al borde no generan segmentos vacíos.
        var segments = new List<List<string>> { new() };
        foreach (var word in (text ?? string.Empty).Split(' ', '\t', '\n', '\r').Where(w => w.Length > 0))
        {
            if (word == separator) { if (segments[^1].Count > 0) segments.Add(new()); }
            else segments[^1].Add(word);
        }
        segments.RemoveAll(seg => seg.Count == 0);
        if (segments.Count == 0) return new List<string> { string.Empty };

        var lines = new List<string>();
        string current = string.Empty;
        foreach (var seg in segments)
        {
            string segText = string.Join(' ', seg);
            string joined = current.Length == 0 ? segText : current + " " + separator + " " + segText;
            if (measure(joined) <= maxWidth) { current = joined; continue; }
            // Ruptura preferente: el segmento entero pasa a la línea siguiente SIN el separador.
            if (current.Length > 0) { lines.Add(current); current = string.Empty; }
            if (measure(segText) <= maxWidth) { current = segText; continue; }
            // Segmento más ancho que el máximo: wrap por palabras dentro del segmento; la última
            // línea queda abierta por si el siguiente segmento cabe a su lado.
            var inner = WordWrap(segText, maxWidth, measure);
            for (int i = 0; i < inner.Count - 1; i++) lines.Add(inner[i]);
            current = inner[^1];
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    /// <summary>Elipsis Unicode (un solo glifo, más estrecho que "...").</summary>
    public const string Ellipsis = "…";

    /// <summary>
    /// Recorta <paramref name="text"/> con una elipsis medida para que NO exceda
    /// <paramref name="maxWidth"/> según <paramref name="measure"/>. Si ya cabe, lo devuelve intacto.
    /// Recorta carácter a carácter desde la cola hasta que <c>prefijo + "…"</c> entra; si ni un solo
    /// carácter + elipsis cabe, devuelve solo la elipsis (o cadena vacía si ni la elipsis cabe).
    /// Puro y determinista (misma entrada → misma salida) para garantizar medir==pintar.
    /// </summary>
    public static string Ellipsize(string text, double maxWidth, Func<string, double> measure)
    {
        text ??= string.Empty;
        if (text.Length == 0 || measure(text) <= maxWidth) return text;

        double ellW = measure(Ellipsis);
        if (ellW > maxWidth) return string.Empty; // ni la elipsis cabe
        // Recorta desde la cola hasta que prefijo+elipsis entre.
        for (int len = text.Length - 1; len >= 1; len--)
        {
            string candidate = text[..len] + Ellipsis;
            if (measure(candidate) <= maxWidth) return candidate;
        }
        return Ellipsis; // solo la elipsis cabe
    }

    /// <summary>
    /// Acota una línea con tope derecho (T8): recorta <paramref name="text"/> (vía
    /// <see cref="Ellipsize"/>) para que, pintada desde <paramref name="drawX"/>, NUNCA rebase
    /// <paramref name="rightEdge"/> − <paramref name="margin"/>. Generaliza el <c>FitHeaderLine</c>
    /// de la cabecera para cualquier línea right-bounded (plan del chrome, línea de pace, línea de
    /// reset, nombres de sesión, claves de gasto). Pura y determinista → medir==pintar.
    /// </summary>
    public static string FitLine(string text, int drawX, int rightEdge, int margin, Func<string, double> measure)
    {
        int avail = Math.Max(0, rightEdge - margin - drawX);
        return Ellipsize(text, avail, measure);
    }
}
