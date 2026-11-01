using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Limpia títulos y canales (sobre todo de vídeos de YouTube) y propone qué buscar. PURO.
/// <list type="bullet">
/// <item>Título: fuera los paréntesis/corchetes de ruido (official, video, audio, lyric(s), letra, visualizer, mv, m/v,
/// hd, 4k, 8k, remaster(ed), live, en vivo, explicit, oficial…; insensible a mayúsculas y acentos), los «4K»/«HD»
/// sueltos al final, <c>ft./feat./featuring/prod.</c> y lo que sigue (hasta el siguiente « - »), <c>(con …)</c>,
/// <c>| …</c>, <c>// …</c>, emojis, comillas y espacios dobles.</item>
/// <item>Canal: fuera « - Topic», «VEVO» final, «Official»/«Oficial» final y «Music» final («X Music»); el artista
/// principal es lo de antes de <c>,</c> <c>&amp;</c> « x » « y » o « feat». Un canal de letras («… Lyrics»,
/// «Letras …», karaoke) no es el artista: se busca solo por título.</item>
/// <item>Título con « - », « – » o « — » (en el navegador, o si una de las partes es el canal): (derecha, izquierda) y
/// el inverso (izquierda, derecha); si la DERECHA es el canal, al revés. Con dos guiones también la parte central.</item>
/// <item>El título con su «feat» se prueba después del título sin él. Fuera del navegador (Spotify…) el primer
/// candidato es el de siempre: título y artista tal cual (como <see cref="LyricsQuery"/>).</item>
/// <item>Sin duplicados (comparando normalizado: sin acentos ni mayúsculas) y como mucho <see cref="MaxCandidates"/>.</item>
/// </list>
/// </summary>
public static class TrackNameParser
{
    public const int MaxCandidates = 6;

    private const RegexOptions Opts = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // Sobre el texto NORMALIZADO del paréntesis (sin acentos, minúsculas, «m/v» → «m v»).
    private static readonly Regex NoiseWords = new(
        @"\b(?:official|oficial|video|audio|lyrics?|letra|visuali[sz]er|mv|m v|hd|4k|8k|remaster(?:ed)?|live|en vivo|explicit)\b",
        Opts);
    private static readonly Regex FeatWords = new(@"^(?:feat|ft|featuring|prod|con)\b", Opts);

    private static readonly Regex Brackets = new(@"\s*[\(\[【]([^\(\)\[\]【】]*)[\)\]】]", Opts);
    private static readonly Regex BareFeat = new(@"\s+(?:feat\.?|ft\.?|featuring|prod\.)\s+.*?(?=\s+[-–—]\s+|$)", Opts);
    private static readonly Regex BareNoiseTail = new(@"(?:\s+(?:4k|8k|hd|hq|mv|m/v))+\s*$", Opts);
    private static readonly Regex Dash = new(@"\s+[-–—]\s+", RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly Regex Topic = new(@"\s*-\s*topic$", Opts);
    private static readonly Regex Vevo = new(@"\s*vevo$", Opts);
    private static readonly Regex OfficialTail = new(@"\s*(?:official|oficial)$", Opts);
    private static readonly Regex MusicTail = new(@"\s+music$", Opts);
    private static readonly Regex ArtistSplit = new(@"\s*(?:,|&|\s(?:x|y|feat\.?|ft\.?|featuring)\s)", Opts);
    private static readonly Regex LyricsChannel = new(@"\b(?:lyrics?|letras?|karaoke)\b", Opts);

    private static readonly string[] Browsers =
    {
        "chrome", "chromium", "msedge", "firefox", "308046b0af4a39cb", "opera", "brave", "vivaldi",
        "thebrowsercompany", "arc.exe", "yandex", "waterfox", "librewolf", "floorp", "zen.exe",
    };

    private static readonly string[] TodayCuts = { " (", " [", " - " };

    /// <summary>Título sin ruido de YouTube ni «feat». Vacío si no queda nada.</summary>
    public static string CleanTitle(string raw) => Clean(raw, keepFeat: false);

    /// <summary>Artista principal limpio de un canal o de una lista de artistas.</summary>
    public static string CleanArtist(string raw) => MainArtist(CleanChannel(raw));

    /// <summary>¿La app es un navegador (título = vídeo, artista = canal)?</summary>
    public static bool LooksLikeBrowserVideo(string sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId)) return false;
        var id = sourceAppId.ToLowerInvariant();
        return Browsers.Any(id.Contains);
    }

    public static IReadOnlyList<SearchCandidate> Candidates(TrackQuery q)
    {
        ArgumentNullException.ThrowIfNull(q);
        var title = (q.Title ?? "").Trim();
        var artist = (q.Artist ?? "").Trim();
        if (title.Length == 0) return Array.Empty<SearchCandidate>();

        var list = new List<(string Title, string Artist)>(MaxCandidates);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void add(string t, string a)
        {
            t = Tidy(t);
            a = Tidy(a);
            if (t.Length == 0 || list.Count >= MaxCandidates) return;
            if (seen.Add(Normalize(t) + "\u001F" + Normalize(a))) list.Add((t, a));
        }

        bool browser = LooksLikeBrowserVideo(q.SourceAppId ?? "");
        var channel = CleanChannel(artist);
        if (LyricsChannel.IsMatch(Normalize(channel))) channel = "";
        var main = MainArtist(channel);

        if (!browser) add(title, artist);   // como hoy: tal cual primero
        AddPairs(Clean(title, keepFeat: false), main, channel, browser, add);
        if (!browser) add(CutAtFirst(title, TodayCuts), MainArtist(artist));
        AddPairs(Clean(title, keepFeat: true), main, channel, browser, add);

        return list.Select((p, i) => new SearchCandidate(p.Title, p.Artist, i)).ToList();
    }

    /// <summary>Sin acentos, en minúsculas, solo letras y dígitos separados por un espacio. Para comparar y para
    /// claves de caché.</summary>
    internal static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                if (space && sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else space = true;
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void AddPairs(string clean, string main, string channel, bool browser, Action<string, string> add)
    {
        var parts = Dash.Split(clean).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count >= 2)
        {
            string left = parts[0], right = parts[^1];
            bool leftIs = SameArtist(left, main);
            bool rightIs = !leftIs && SameArtist(right, main);
            if (browser || leftIs || rightIs)
            {
                var artistPart = rightIs ? right : left;
                var titles = new List<string> { rightIs ? left : right };
                if (parts.Count >= 3) titles.Add(string.Join(" - ", parts.Skip(1).Take(parts.Count - 2)));
                var artistMain = MainArtist(artistPart);

                foreach (var t in titles) add(t, artistMain);
                foreach (var t in titles) add(t, artistPart);
                add(artistPart, titles[0]);   // orden inverso: «Canción - Artista»
                if (main.Length > 0) foreach (var t in titles) add(t, main);
                if (!leftIs && !rightIs) add(clean, main);   // el « - » podía ser parte del título
                return;
            }
            // Fuera del navegador, lo de después del « - » es una versión («Remastered 2011»): fuera, como hoy.
            add(left, main);
            add(left, channel);
            return;
        }
        add(clean, main);
        add(clean, channel);
    }

    private static bool SameArtist(string part, string channelMain)
    {
        var c = Compact(channelMain);
        if (c.Length == 0) return false;
        foreach (var candidate in new[] { MainArtist(part), part })
        {
            var p = Compact(candidate);
            if (p.Length == 0) continue;
            if (p == c) return true;
            if (Math.Min(p.Length, c.Length) >= 4 && (p.Contains(c, StringComparison.Ordinal) || c.Contains(p, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    private static string Clean(string raw, bool keepFeat)
    {
        var s = StripSymbols(raw ?? "");

        int cut = IndexOfAny(s, "|", "｜", "//");
        if (cut > 0) s = s[..cut];

        s = Brackets.Replace(s, m =>
        {
            var inner = Normalize(m.Groups[1].Value);
            return NoiseWords.IsMatch(inner) || (!keepFeat && FeatWords.IsMatch(inner)) ? " " : m.Value;
        });
        if (!keepFeat) s = BareFeat.Replace(s, "");
        s = BareNoiseTail.Replace(s, "");
        s = s.Replace("\"", " ").Replace("“", " ").Replace("”", " ").Replace("„", " ");
        return Tidy(s).Trim('-', '–', '—', ':', ' ');
    }

    private static string CleanChannel(string raw)
    {
        var s = Tidy(StripSymbols(raw ?? ""));
        string prev;
        do
        {
            prev = s;
            s = StripTail(s, Topic);
            s = StripTail(s, Vevo);
            s = StripTail(s, OfficialTail);
            s = StripTail(s, MusicTail);
        } while (s != prev);
        return s;
    }

    // Quita la cola si queda algo delante («Music» a secas sigue siendo «Music»).
    private static string StripTail(string s, Regex tail)
    {
        var m = tail.Match(s);
        if (!m.Success || m.Index == 0) return s;
        return s[..m.Index].Trim();
    }

    private static string MainArtist(string artist)
    {
        artist = Tidy(artist);
        var m = ArtistSplit.Match(artist);
        return m.Success && m.Index > 0 ? artist[..m.Index].Trim() : artist;
    }

    // Emojis y otros símbolos (♪, ★, ©), modificadores de tono, selectores de variante y caracteres de formato (ZWJ).
    private static string StripSymbols(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var r in s.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(r);
            bool drop = cat is UnicodeCategory.OtherSymbol or UnicodeCategory.Format
                        || (cat == UnicodeCategory.ModifierSymbol && r.Value >= 0x1F000)
                        || r.Value is >= 0xFE00 and <= 0xFE0F;
            if (drop) sb.Append(' ');
            else sb.Append(r.ToString());
        }
        return sb.ToString();
    }

    private static string Compact(string s) => Normalize(s).Replace(" ", "", StringComparison.Ordinal);

    private static string Tidy(string s) => Spaces.Replace(s ?? "", " ").Trim();

    private static int IndexOfAny(string s, params string[] needles)
    {
        int at = -1;
        foreach (var n in needles)
        {
            int i = s.IndexOf(n, StringComparison.Ordinal);
            if (i >= 0 && (at < 0 || i < at)) at = i;
        }
        return at;
    }

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
