namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Separa dos versiones de un texto en prefijo común, tramo que cambia (viejo y nuevo) y sufijo común,
/// para cruzar solo los caracteres que cambian (micro-interacción 4: «reset en 2 h 13 min» → «12 min»
/// funde solo el «3»/«2»). PURO.
/// </summary>
public static class TextDiff
{
    public static (string Prefix, string OldMid, string NewMid, string Suffix) Split(string a, string b)
    {
        int max = Math.Min(a.Length, b.Length);
        int p = 0;
        while (p < max && a[p] == b[p]) p++;
        int s = 0;
        while (s < max - p && a[a.Length - 1 - s] == b[b.Length - 1 - s]) s++;
        return (a[..p], a[p..(a.Length - s)], b[p..(b.Length - s)], a[(a.Length - s)..]);
    }
}
