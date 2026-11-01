using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Una línea de letra sincronizada: se muestra a partir de <paramref name="At"/>.</summary>
public sealed record LyricLine(TimeSpan At, string Text);

public enum LyricsStatus { Found, NotFound, Error }

/// <summary>
/// Resultado de buscar la letra de una pista. <see cref="LyricsStatus.NotFound"/> cubre también las
/// instrumentales y, para <see cref="LrclibClient"/>, las que solo tienen letra sin sincronizar («Sin letra»): no se
/// reintenta.
/// <para>Letras v2: una letra PLANA (sin marcas de tiempo) es <see cref="LyricsStatus.Found"/> con
/// <see cref="Synced"/> = false, <see cref="Lines"/> vacías y el texto en <see cref="PlainText"/> (ver
/// <see cref="Plain"/>). Se eligió un campo aparte y no líneas con un instante centinela para que nadie pueda
/// confundir una letra plana con una sincronizada: el <see cref="LyricsController"/> de hoy, que solo mira
/// <see cref="Lines"/>, la sigue tratando como «Sin letra».</para>
/// </summary>
public sealed record LyricsResult(LyricsStatus Status, IReadOnlyList<LyricLine> Lines)
{
    public static readonly LyricsResult NotFound = new(LyricsStatus.NotFound, Array.Empty<LyricLine>());
    public static readonly LyricsResult Error = new(LyricsStatus.Error, Array.Empty<LyricLine>());

    /// <summary>true (por defecto) = <see cref="Lines"/> con tiempos; false = letra plana en <see cref="PlainText"/>.</summary>
    public bool Synced { get; init; } = true;

    /// <summary>Texto de la letra plana (saltos <c>\n</c>, sin espacios al principio ni al final); null si es sincronizada.</summary>
    public string? PlainText { get; init; }

    /// <summary>Letra de origen (fuente, id, duración y cuerpo) cuando la da el <see cref="LyricsAggregator"/>: con ella el
    /// <see cref="LyricsController"/> la guarda en la caché y sabe qué fuente enseñar. null en <see cref="LrclibClient"/>
    /// y en los resultados sin letra.</summary>
    public LyricsCandidate? From { get; init; }

    /// <summary>Resultado <see cref="LyricsStatus.Found"/> para una letra concreta (sincronizada → LRC parseado; plana →
    /// <see cref="Plain"/>), con <see cref="From"/> = <paramref name="c"/>. null si no hay nada que enseñar (cuerpo vacío
    /// o «sincronizada» sin una sola marca válida). PURO.</summary>
    public static LyricsResult? FromCandidate(LyricsCandidate c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (string.IsNullOrWhiteSpace(c.Body)) return null;
        if (!c.Synced) return Plain(c.Body) with { From = c };
        var lines = LrcParser.Parse(c.Body);
        return lines.Count > 0 ? new LyricsResult(LyricsStatus.Found, lines) { From = c } : null;
    }

    /// <summary>Letra plana encontrada: normaliza los saltos de línea a <c>\n</c> y quita los espacios de los extremos.</summary>
    public static LyricsResult Plain(string text) =>
        new(LyricsStatus.Found, Array.Empty<LyricLine>())
        {
            Synced = false,
            PlainText = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim(),
        };
}

/// <summary>
/// Parser de LRC. PURO.
/// <list type="bullet">
/// <item>Varias marcas por línea (<c>[00:10.00][00:20.50]texto</c>) → una línea por marca.</item>
/// <item>Marcas <c>mm:ss</c>, <c>mm:ss.x</c>, <c>mm:ss.xx</c>, <c>mm:ss.xxx</c> y <c>mm:ss:xx</c>.</item>
/// <item><c>[offset:ms]</c>: positivo adelanta la letra (estándar LRC); nunca por debajo de 0.</item>
/// <item>Texto vacío → «♪» (pausa instrumental). Marcas por palabra (<c>&lt;mm:ss.xx&gt;</c>) se quitan.</item>
/// <item>Etiquetas de metadatos y líneas sin marca se ignoran. Resultado ordenado por instante (estable).</item>
/// </list>
/// </summary>
public static class LrcParser
{
    public const string PauseText = "♪";

    private static readonly Regex Stamp = new(@"^\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant);
    private static readonly Regex Offset = new(@"^\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex WordStamp = new(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", RegexOptions.CultureInvariant);

    public static IReadOnlyList<LyricLine> Parse(string lrc)
    {
        if (string.IsNullOrEmpty(lrc)) return Array.Empty<LyricLine>();

        long offsetMs = 0;
        var raw = new List<(TimeSpan At, string Text)>();
        foreach (var rawLine in lrc.Split('\n'))
        {
            var line = rawLine.Trim();
            var off = Offset.Match(line);
            if (off.Success)
            {
                if (long.TryParse(off.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var o))
                    offsetMs = o;
                continue;
            }

            var stamps = new List<TimeSpan>();
            int pos = 0;
            Match m;
            while ((m = Stamp.Match(line[pos..])).Success)
            {
                stamps.Add(ToTime(m));
                pos += m.Length;
            }
            if (stamps.Count == 0) continue;

            var text = WordStamp.Replace(line[pos..], "").Trim();
            if (text.Length == 0) text = PauseText;
            foreach (var s in stamps) raw.Add((s, text));
        }

        var shift = TimeSpan.FromMilliseconds(offsetMs);
        return raw
            .Select(r => new LyricLine(r.At - shift < TimeSpan.Zero ? TimeSpan.Zero : r.At - shift, r.Text))
            .OrderBy(l => l.At)
            .ToList();
    }

    /// <summary>Índice de la línea vigente en <paramref name="position"/>, adelantada <paramref name="lead"/>
    /// (80 ms) para compensar el repintado; −1 antes de la primera. <paramref name="lines"/> ordenadas.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan position, TimeSpan lead)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var t = position + lead;
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (lines[mid].At <= t) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    private static TimeSpan ToTime(Match m)
    {
        int min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int ms = 0;
        if (m.Groups[3].Success)
        {
            var f = m.Groups[3].Value;
            ms = int.Parse(f, CultureInfo.InvariantCulture) * f.Length switch { 1 => 100, 2 => 10, _ => 1 };
        }
        return new TimeSpan(0, 0, min, sec, ms);
    }
}

/// <summary>
/// URLs de búsqueda en LRCLIB para una pista, de más a menos precisa y sin duplicados. PURO.
/// <list type="number">
/// <item>Título y artista tal cual.</item>
/// <item>Título limpio (se corta en « (», « [» o « - ») + artista principal (antes de «,», «&amp;» o « feat»).</item>
/// <item>Búsqueda libre <c>q=</c> con título limpio y artista principal.</item>
/// </list>
/// Sin título no hay candidatos; sin artista se omite <c>artist_name</c>.
/// </summary>
public static class LyricsQuery
{
    public const string SearchUrl = "https://lrclib.net/api/search";

    private static readonly string[] TitleCuts = { " (", " [", " - " };
    private static readonly string[] ArtistCuts = { ",", "&", " feat" };

    public static IReadOnlyList<string> Candidates(string title, string artist)
    {
        title = (title ?? "").Trim();
        artist = (artist ?? "").Trim();
        if (title.Length == 0) return Array.Empty<string>();

        var cleanTitle = CutAtFirst(title, TitleCuts);
        var mainArtist = CutAtFirst(artist, ArtistCuts);

        var urls = new List<string>(3);
        void add(string url) { if (!urls.Contains(url)) urls.Add(url); }
        add(Track(title, artist));
        add(Track(cleanTitle, mainArtist));
        add($"{SearchUrl}?q={Uri.EscapeDataString((cleanTitle + " " + mainArtist).Trim())}");
        return urls;
    }

    private static string Track(string title, string artist) =>
        artist.Length == 0
            ? $"{SearchUrl}?track_name={Uri.EscapeDataString(title)}"
            : $"{SearchUrl}?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";

    private static string CutAtFirst(string s, string[] cuts)
    {
        int at = s.Length;
        foreach (var c in cuts)
        {
            int i = s.IndexOf(c, StringComparison.OrdinalIgnoreCase);
            if (i > 0 && i < at) at = i;
        }
        return s[..at].Trim();
    }
}
