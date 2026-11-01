using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Letras v2 · A1: <see cref="TrackNameParser"/> (PURO) limpia títulos y canales de YouTube y propone los
/// candidatos de búsqueda por orden de probabilidad. Casos sacados de vídeos reales.
/// </summary>
public class TrackNameParserTests
{
    private const string Chrome = "Chrome";
    private const string Spotify = "Spotify.exe";

    private static TrackQuery Q(string title, string artist, string app = Chrome, double seconds = 200) =>
        new(title, artist, null, TimeSpan.FromSeconds(seconds), app);

    private static (string Title, string Artist)[] Pairs(TrackQuery q) =>
        TrackNameParser.Candidates(q).Select(c => (c.Title, c.Artist)).ToArray();

    // ---------------- Casos reales del plan ----------------

    [Fact]
    public void Three_parts_with_mc_prefix_tries_right_and_middle_with_left_as_artist()
    {
        var c = Pairs(Q("MC Kevin O Chris - Vai Rebola Pro Pai - Ela É Do Tipo", "Kevin O Chris"));

        Assert.Equal(("Ela É Do Tipo", "MC Kevin O Chris"), c[0]);
        Assert.Equal(("Vai Rebola Pro Pai", "MC Kevin O Chris"), c[1]);
        Assert.Contains(("Ela É Do Tipo", "Kevin O Chris"), c);
        Assert.Contains(("Vai Rebola Pro Pai", "Kevin O Chris"), c);
        Assert.True(c.Length <= TrackNameParser.MaxCandidates);
    }

    [Fact]
    public void Artist_dash_song_with_official_video_and_artist_channel()
    {
        var c = Pairs(Q("Bad Bunny - DtMF (Official Video)", "Bad Bunny"));

        Assert.Equal(("DtMF", "Bad Bunny"), c[0]);
        Assert.Equal(("Bad Bunny", "DtMF"), c[1]);   // orden inverso como respaldo
        Assert.Equal(2, c.Length);
    }

    [Fact]
    public void Vevo_channel_matches_left_part_ignoring_accents()
    {
        var c = Pairs(Q("Rosalía - DESPECHÁ (Video Oficial)", "RosaliaVEVO"));

        Assert.Equal(("DESPECHÁ", "Rosalía"), c[0]);
        Assert.Equal(("Rosalía", "DESPECHÁ"), c[1]);
        // «Rosalia» (el canal) y «Rosalía» (el título) son el mismo artista normalizado: sin duplicado.
        Assert.DoesNotContain(("DESPECHÁ", "Rosalia"), c);
    }

    [Fact]
    public void Topic_channel_is_the_artist_of_a_plain_title()
    {
        var c = Pairs(Q("Blinding Lights", "The Weeknd - Topic"));

        Assert.Equal(new[] { ("Blinding Lights", "The Weeknd") }, c);
    }

    [Fact]
    public void Several_artists_on_the_left_and_bare_4k_suffix()
    {
        var c = Pairs(Q("Peso Pluma, Natanael Cano - PRC [Official Video] 4K", "Peso Pluma"));

        Assert.Equal(("PRC", "Peso Pluma"), c[0]);
        Assert.Contains(("PRC", "Peso Pluma, Natanael Cano"), c);
        Assert.Contains(("Peso Pluma, Natanael Cano", "PRC"), c);
        Assert.All(c, p => Assert.DoesNotContain("4K", p.Title));
    }

    [Fact]
    public void Lyrics_channel_is_not_an_artist_and_feat_is_kept_as_second_candidate()
    {
        var c = Pairs(Q("Song (feat. X) | Lyrics", "Some Lyrics Channel"));

        Assert.Equal(("Song", ""), c[0]);
        Assert.Equal(("Song (feat. X)", ""), c[1]);
        Assert.Equal(2, c.Length);
    }

    [Fact]
    public void Spotify_title_without_dash_is_searched_as_today()
    {
        Assert.Equal(new[] { ("Blinding Lights", "The Weeknd") }, Pairs(Q("Blinding Lights", "The Weeknd", Spotify)));

        // Igual que LyricsQuery: primero tal cual, luego título limpio + artista principal.
        var c = Pairs(Q("Levitating (feat. DaBaby)", "Dua Lipa, DaBaby", Spotify));
        Assert.Equal(("Levitating (feat. DaBaby)", "Dua Lipa, DaBaby"), c[0]);
        Assert.Equal(("Levitating", "Dua Lipa"), c[1]);
    }

    [Fact]
    public void Spotify_version_suffix_after_dash_is_not_swapped()
    {
        var c = Pairs(Q("Bohemian Rhapsody - Remastered 2011", "Queen", Spotify));

        Assert.Equal(("Bohemian Rhapsody - Remastered 2011", "Queen"), c[0]);
        Assert.Equal(("Bohemian Rhapsody", "Queen"), c[1]);
        Assert.DoesNotContain(c, p => p.Title == "Remastered 2011");
    }

    // ---------------- Reglas ----------------

    [Theory]
    [InlineData("Canción (Official Video)", "Canción")]
    [InlineData("Canción [Official Music Video] [HD]", "Canción")]
    [InlineData("Canción (Lyrics)", "Canción")]
    [InlineData("Canción (Letra)", "Canción")]
    [InlineData("Canción (Visualizer)", "Canción")]
    [InlineData("Canción (Visualiser)", "Canción")]
    [InlineData("Canción [MV]", "Canción")]
    [InlineData("Canción [M/V]", "Canción")]
    [InlineData("Canción (Remastered 2009)", "Canción")]
    [InlineData("Canción (En Vivo)", "Canción")]
    [InlineData("Canción (Live at Wembley)", "Canción")]
    [InlineData("Canción (Explicit)", "Canción")]
    [InlineData("Canción (Clip Oficial)", "Canción")]
    [InlineData("Canción (VÍDEO OFICIAL)", "Canción")]
    [InlineData("Canción (Audio Oficial)", "Canción")]
    [InlineData("Canción (Remix)", "Canción (Remix)")]
    [InlineData("Canción ft. Otro", "Canción")]
    [InlineData("Canción feat. Otro (Official Video)", "Canción")]
    [InlineData("Canción (featuring Otro)", "Canción")]
    [InlineData("Canción (prod. Alguien)", "Canción")]
    [InlineData("Canción (con Otra)", "Canción")]
    [InlineData("Bailando con lobos", "Bailando con lobos")]
    [InlineData("Canción | Letra completa", "Canción")]
    [InlineData("Canción // Otra cosa", "Canción")]
    [InlineData("🔥 Canción 🔥  ", "Canción")]
    [InlineData("Canción   con   espacios", "Canción con espacios")]
    [InlineData("“Canción”", "Canción")]
    [InlineData("", "")]
    public void CleanTitle_rules(string raw, string expected) =>
        Assert.Equal(expected, TrackNameParser.CleanTitle(raw));

    [Fact]
    public void CleanTitle_keeps_the_feat_of_the_right_part_only()
    {
        // El «ft.» de la izquierda no se come el « - Canción» que le sigue.
        Assert.Equal("Artista - Canción", TrackNameParser.CleanTitle("Artista ft. Otro - Canción (Official Video) [4K]"));
        Assert.Equal("Artista - Canción", TrackNameParser.CleanTitle("Artista - Canción (Official Video) [4K] ft. X"));
    }

    [Theory]
    [InlineData("The Weeknd - Topic", "The Weeknd")]
    [InlineData("RosaliaVEVO", "Rosalia")]
    [InlineData("Bad Bunny Vevo", "Bad Bunny")]
    [InlineData("Karol G Official", "Karol G")]
    [InlineData("Shakira Oficial", "Shakira")]
    [InlineData("Imagine Dragons Music", "Imagine Dragons")]
    [InlineData("Music", "Music")]
    [InlineData("Dua Lipa, DaBaby", "Dua Lipa")]
    [InlineData("Simon & Garfunkel", "Simon")]
    [InlineData("Calvin Harris x Dua Lipa", "Calvin Harris")]
    [InlineData("Wisin y Yandel", "Wisin")]
    [InlineData("Lil Nas X", "Lil Nas X")]
    [InlineData("  ", "")]
    public void CleanArtist_rules(string raw, string expected) =>
        Assert.Equal(expected, TrackNameParser.CleanArtist(raw));

    [Fact]
    public void Song_dash_artist_puts_the_reverse_order_first_when_the_right_part_is_the_channel()
    {
        var c = Pairs(Q("Hasta Que Me Olvides - Luis Miguel", "Luis Miguel"));
        Assert.Equal(("Hasta Que Me Olvides", "Luis Miguel"), c[0]);
    }

    [Fact]
    public void Unknown_channel_defaults_to_artist_dash_song_and_also_tries_the_channel()
    {
        var c = Pairs(Q("Daft Punk - One More Time", "Random Uploader"));
        Assert.Equal(("One More Time", "Daft Punk"), c[0]);
        Assert.Equal(("Daft Punk", "One More Time"), c[1]);
        Assert.Contains(("Daft Punk - One More Time", "Random Uploader"), c);
    }

    [Fact]
    public void En_dash_and_em_dash_split_too()
    {
        Assert.Equal(("Canción", "Artista"), Pairs(Q("Artista – Canción", "Artista"))[0]);
        Assert.Equal(("Canción", "Artista"), Pairs(Q("Artista — Canción", "Artista"))[0]);
    }

    [Fact]
    public void Non_browser_app_still_splits_when_the_left_part_is_the_artist()
    {
        var c = Pairs(Q("Artista - Canción (Official Video)", "Artista", "SomePlayer.exe"));
        Assert.Contains(("Canción", "Artista"), c);
    }

    [Fact]
    public void Candidates_are_unique_ranked_and_at_most_six()
    {
        var all = TrackNameParser.Candidates(Q("A ft. B - C - D (feat. E) [Official Video]", "Some Channel"));

        Assert.InRange(all.Count, 1, TrackNameParser.MaxCandidates);
        Assert.Equal(Enumerable.Range(0, all.Count), all.Select(c => c.Rank));
        Assert.Equal(all.Count, all.Select(c => (c.Title.ToLowerInvariant(), c.Artist.ToLowerInvariant())).Distinct().Count());
        Assert.All(all, c => Assert.False(string.IsNullOrWhiteSpace(c.Title)));
    }

    [Fact]
    public void Empty_title_has_no_candidates()
    {
        Assert.Empty(TrackNameParser.Candidates(Q("", "Artista")));
        Assert.Empty(TrackNameParser.Candidates(Q("  (Official Video) ", "Artista")));
    }

    [Theory]
    [InlineData("Chrome", true)]
    [InlineData("MSEdge", true)]
    [InlineData("308046B0AF4A39CB", true)]                 // Firefox
    [InlineData("firefox.exe", true)]
    [InlineData("Opera", true)]
    [InlineData("Brave", true)]
    [InlineData("vivaldi.exe", true)]
    [InlineData("TheBrowserCompany.Arc_ttt1ap7aakyb4!Arc", true)]
    [InlineData("Spotify.exe", false)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", false)]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", false)]
    [InlineData("", false)]
    public void LooksLikeBrowserVideo_detects_browsers(string appId, bool expected) =>
        Assert.Equal(expected, TrackNameParser.LooksLikeBrowserVideo(appId));
}
