using System.Text.RegularExpressions;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// ¿La letra que da una fuente es de la canción que se buscó? (letras v2). Hace falta porque una búsqueda puede
/// devolver otras canciones (la libre de LRCLIB, <c>q=</c>, sobre todo: otra del mismo artista o una que solo comparte
/// palabras) y, en cuanto hay una sincronizada de otra canción, el agregador deja de buscar y la elegiría si su duración
/// se pareciera más.
/// <list type="bullet">
/// <item>Título: alguna variante del buscado (tal cual, limpio con <see cref="TrackNameParser.CleanTitle"/>, sin
/// paréntesis ni corchetes, o cada trozo entre « - ») y alguna del de la fuente tienen las mismas palabras, o las del
/// más corto están todas en el más largo y son al menos la mitad («Seven» ≈ «Seven (feat. Latto)», pero
/// «Seven» ≠ «Seven Nation Army»).</item>
/// <item>Artista (si se buscó con artista): comparten una palabra significativa, coinciden sin espacios
/// («a-ha»), o el buscado aparece en el título de la fuente (entradas con el artista metido en el título).</item>
/// <item>También vale con título y artista de la fuente cruzados (entradas de LRCLIB con los campos al revés, o la
/// búsqueda «Canción - Artista» al revés).</item>
/// <item>Una letra sin título no se puede juzgar y se acepta (la fuente ya hizo su propia búsqueda).</item>
/// </list>
/// No mira la duración: de eso ya se encarga <see cref="LyricsAggregator.Best"/>. PURO.
/// </summary>
public static class LyricsMatch
{
    private static readonly Regex Brackets = new(@"[\(\[【][^\(\)\[\]【】]*[\)\]】]", RegexOptions.CultureInvariant);
    private static readonly Regex Dash = new(@"\s+[-–—]\s+", RegexOptions.CultureInvariant);

    // Palabras que comparten muchos artistas sin ser el mismo («The Weeknd» / «The White Stripes»).
    private static readonly HashSet<string> ArtistStopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "los", "las", "les", "del", "con", "feat", "featuring", "with", "und", "band",
    };

    /// <summary>true si <paramref name="c"/> parece la canción buscada en <paramref name="q"/>.</summary>
    public static bool IsMatch(SearchCandidate q, LyricsCandidate c)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(c);
        if (string.IsNullOrWhiteSpace(c.Title) || string.IsNullOrWhiteSpace(q.Title)) return true;
        return Direct(q.Title, q.Artist, c.Title, c.Artist) || Direct(q.Title, q.Artist, c.Artist, c.Title);
    }

    private static bool Direct(string qTitle, string qArtist, string cTitle, string cArtist) =>
        SameTitle(qTitle, cTitle) && SameArtist(qArtist, cArtist, cTitle);

    private static bool SameTitle(string a, string b)
    {
        var bs = Variants(b);
        foreach (var x in Variants(a))
            foreach (var y in bs)
                if (Close(x, y)) return true;
        return false;
    }

    // Mismas palabras, o las del más corto todas en el más largo y al menos la mitad de este.
    private static bool Close(string[] x, string[] y)
    {
        var (shortW, longW) = x.Length <= y.Length ? (x, y) : (y, x);
        if (shortW.Length == 0) return false;
        var set = new HashSet<string>(longW, StringComparer.Ordinal);
        return shortW.All(set.Contains) && shortW.Length * 2 >= longW.Length;
    }

    private static List<string[]> Variants(string raw)
    {
        var list = new List<string[]>();
        void add(string s)
        {
            var w = Words(s);
            if (w.Length > 0 && !list.Any(v => v.SequenceEqual(w))) list.Add(w);
        }

        var clean = TrackNameParser.CleanTitle(raw);
        var bare = Brackets.Replace(clean, " ");
        add(raw);
        add(clean);
        add(bare);
        var parts = Dash.Split(bare);
        if (parts.Length >= 2) foreach (var p in parts) add(p);
        return list;
    }

    private static bool SameArtist(string qArtist, string cArtist, string cTitle)
    {
        var q = TrackNameParser.Normalize(qArtist ?? "");
        if (q.Length == 0) return true;   // búsqueda solo por título
        var c = TrackNameParser.Normalize(cArtist ?? "");
        string qc = Compact(q), cc = Compact(c), tc = Compact(TrackNameParser.Normalize(cTitle ?? ""));

        if (qc == cc) return true;
        if (Math.Min(qc.Length, cc.Length) >= 4 && (cc.Contains(qc, StringComparison.Ordinal) || qc.Contains(cc, StringComparison.Ordinal)))
            return true;
        var cw = new HashSet<string>(c.Split(' '), StringComparer.Ordinal);
        if (q.Split(' ').Any(w => w.Length >= 3 && !ArtistStopWords.Contains(w) && cw.Contains(w))) return true;
        return qc.Length >= 4 && tc.Contains(qc, StringComparison.Ordinal);
    }

    private static string[] Words(string s) =>
        TrackNameParser.Normalize(s ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Compact(string normalized) => normalized.Replace(" ", "", StringComparison.Ordinal);
}
