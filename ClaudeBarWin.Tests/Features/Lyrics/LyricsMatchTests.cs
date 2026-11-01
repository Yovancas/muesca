using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="LyricsMatch"/> (PURO): ¿la letra que da una fuente es de la canción que se buscó? Los casos «no» son
/// falsos positivos reales del banco de 80 títulos de YouTube (mismo artista, otra canción, o una canción cualquiera:
/// los que devuelve también la búsqueda libre de LRCLIB); los «sí», entradas reales con metadatos raros que SÍ son la
/// canción.
/// </summary>
public class LyricsMatchTests
{
    private static bool M(string qTitle, string qArtist, string cTitle, string cArtist) =>
        LyricsMatch.IsMatch(new SearchCandidate(qTitle, qArtist, 0),
            new LyricsCandidate("X", "1", cTitle, cArtist, TimeSpan.FromSeconds(200), true, "[00:01.00]hola"));

    // ---------------- La misma canción ----------------

    [Theory]
    [InlineData("Gasolina", "Daddy Yankee", "Gasolina", "Daddy Yankee")]
    [InlineData("Obsesion", "Aventura", "Obsesión", "Aventura")]                         // acentos
    [InlineData("God's Plan", "Drake", "God’s Plan", "Drake")]                            // apóstrofo tipográfico
    [InlineData("Seven", "Jung Kook", "Seven (feat. Latto)", "Jung Kook")]                // feat en el título
    [InlineData("Seven", "Jung Kook", "Seven (feat. Latto) - Clean Ver.", "정국, Jung Kook")]
    [InlineData("Anti-Hero", "Taylor Swift", "Anti-Hero (Acoustic Version)", "Taylor Swift")]
    [InlineData("Despacito", "Luis Fonsi", "Despacito - Remix", "Luis Fonsi, Daddy Yankee, Justin Bieber")]
    [InlineData("Dynamite", "BTS", "Dynamite", "BTS (防弹少年团)")]                         // artista con alias en chino
    [InlineData("Take On Me", "a-ha", "Take On Me", "a-ha")]                              // artista de una letra por palabra
    [InlineData("X (EQUIS)", "Nicky Jam", "X", "Nicky Jam, J Balvin")]
    [InlineData("SHAKIRA", "Bizarrap", "SHAKIRA || BZRP Music Sessions #53", "Bizarrap")]
    [InlineData("Shakira: Bzrp Music Sessions, Vol. 53", "Bizarrap", "Shakira | BZRP Music Sessions #53", "Bizarrap")]
    [InlineData("Danza Kuduro", "Don Omar", "ME130 - DANZA KUDURO - DON OMAR & LUCENZO", "DANZA KUDURO")]   // artista en el título
    [InlineData("Uptown Funk", "Mark Ronson", "Mark Ronson", "Uptown Funk (Official Video) ft. Bruno Mars")]   // campos al revés
    [InlineData("Don Omar & Lucenzo", "Danza Kuduro", "Danza Kuduro", "Don Omar")]        // búsqueda al revés
    [InlineData("Rosas", "", "Rosas", "La Oreja de Van Gogh")]                            // sin artista: solo el título
    public void Same_song_is_accepted(string qt, string qa, string ct, string ca) =>
        Assert.True(M(qt, qa, ct, ca));

    // ---------------- Otra canción ----------------

    [Theory]
    [InlineData("X (EQUIS)", "Nicky Jam", "Hasta el Amanecer", "Nicky Jam")]               // mismo artista, otra canción
    [InlineData("Índigo", "Camilo", "Vida de Rico", "Camilo")]
    [InlineData("God's Plan", "Drake", "2 Hard 4 The Radio", "Drake")]
    [InlineData("Paint The Town Red", "Doja Cat", "Best Friend (feat. Doja Cat, Jamie & CHANMINA) [Remix]", "Saweetie, Doja Cat")]
    [InlineData("Volver Volver", "Vicente Fernández", "Mexico Lindo", "Vicente Fernández")]
    [InlineData("Dynamite", "BTS", "Film out", "BTS (防弹少年团)")]
    [InlineData("Gasolina", "Daddy Yankee", "演员 (Live)", "赵英俊, 薛之谦")]                // cualquier cosa
    [InlineData("Flowers", "Miley Cyrus", "Flowers (Cover)", "宋雨琦")]                    // mismo título, otro artista
    [InlineData("Seven", "Jung Kook", "Seven Nation Army", "The White Stripes")]
    [InlineData("Seven", "", "Seven Nation Army", "The White Stripes")]                    // una palabra no basta para un título largo
    public void Other_song_is_rejected(string qt, string qa, string ct, string ca) =>
        Assert.False(M(qt, qa, ct, ca));

    [Fact]
    public void Candidate_without_title_cannot_be_judged_and_is_kept()
    {
        Assert.True(M("Gasolina", "Daddy Yankee", "", ""));
        Assert.True(M("Gasolina", "Daddy Yankee", "  ", "Daddy Yankee"));
    }

    [Fact]
    public void Empty_search_title_rejects_nothing()
    {
        Assert.True(M("", "Daddy Yankee", "Gasolina", "Daddy Yankee"));
    }

    [Fact]
    public void Validates_arguments()
    {
        var c = new LyricsCandidate("X", "1", "t", "a", TimeSpan.Zero, true, "b");
        Assert.Throws<ArgumentNullException>(() => LyricsMatch.IsMatch(null!, c));
        Assert.Throws<ArgumentNullException>(() => LyricsMatch.IsMatch(new SearchCandidate("t", "a", 0), null!));
    }
}
