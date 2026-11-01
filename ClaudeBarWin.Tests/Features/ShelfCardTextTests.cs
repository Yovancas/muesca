using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Tarjetas de la bandeja (F5 · B4), lógica pura: tamaño con la cultura de la interfaz, línea gris por tipo, nombres
/// recortados con «…», con una medida falsa de 6 px por carácter (<see cref="ShelfCardText"/>).
/// </summary>
public sealed class ShelfCardTextTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Strings Es = Localization.Get("es");
    private static readonly Strings En = Localization.Get("en");

    private static ShelfItem Item(ShelfKind kind, string value) => new(Guid.NewGuid(), kind, value, value, T0);

    // ---------------------------------------------------------------- tamaño

    [Theory]
    [InlineData(0L, "0 B", "0 B")]
    [InlineData(-5L, "0 B", "0 B")]
    [InlineData(512L, "512 B", "512 B")]
    [InlineData(1023L, "1023 B", "1023 B")]
    [InlineData(1024L, "1 KB", "1 KB")]
    [InlineData(1105L, "1,1 KB", "1.1 KB")]
    [InlineData(1536L, "1,5 KB", "1.5 KB")]
    [InlineData(10230L, "10 KB", "10 KB")]                      // 9,99 KB redondea a 10: sin decimal
    [InlineData(2_516_582L, "2,4 MB", "2.4 MB")]               // 2,4 × 1024²
    [InlineData(25_585_254L, "24 MB", "24 MB")]                // desde 10, sin decimales
    [InlineData(1_048_575L, "1 MB", "1 MB")]                   // 1023,999 KB no se queda en «1024 KB»
    [InlineData(1_073_741_824L, "1 GB", "1 GB")]
    [InlineData(6_047_313_952_768L, "5,5 TB", "5.5 TB")]
    public void Size_reads_like_the_Explorer_in_the_interface_culture(long bytes, string es, string en)
    {
        Assert.Equal(es, ShelfCardText.Size(bytes, Es.Culture));
        Assert.Equal(en, ShelfCardText.Size(bytes, En.Culture));
    }

    // ---------------------------------------------------------------- línea gris

    [Fact]
    public void Detail_is_the_size_or_the_kind()
    {
        var file = Item(ShelfKind.File, @"C:\Fotos\playa.jpg");
        var folder = Item(ShelfKind.Folder, @"C:\Fotos");
        var sized = new ShelfFileProbe(ShelfProbeState.File, 2_516_582, T0);

        Assert.Equal("2,4 MB", ShelfCardText.Detail(file, sized, Es));
        Assert.Equal("2.4 MB", ShelfCardText.Detail(file, sized, En));
        Assert.Equal("Carpeta", ShelfCardText.Detail(folder, null, Es));
        Assert.Equal("Folder", ShelfCardText.Detail(folder, new ShelfFileProbe(ShelfProbeState.Folder), En));
        Assert.Equal("Enlace", ShelfCardText.Detail(Item(ShelfKind.Link, "https://ex.com"), null, Es));
        Assert.Equal("Link", ShelfCardText.Detail(Item(ShelfKind.Link, "https://ex.com"), null, En));
        Assert.Equal("Texto", ShelfCardText.Detail(Item(ShelfKind.Text, "hola"), null, Es));
        Assert.Equal("Text", ShelfCardText.Detail(Item(ShelfKind.Text, "hola"), null, En));

        // Sin tamaño (aún sin mirar, unidad de red o ya no está): el tipo.
        Assert.Equal("Archivo", ShelfCardText.Detail(file, null, Es));
        Assert.Equal("File", ShelfCardText.Detail(file, ShelfFileProbe.Opaque, En));
        Assert.Equal("Archivo", ShelfCardText.Detail(file, ShelfFileProbe.Missing, Es));

        // Manda lo que hay en el disco: una carpeta donde había un archivo.
        Assert.Equal("Carpeta", ShelfCardText.Detail(file, new ShelfFileProbe(ShelfProbeState.Folder), Es));
    }

    [Fact]
    public void Card_labels_exist_in_spanish_and_english()
    {
        Assert.Equal(("Carpeta", "Archivo", "Enlace", "Texto"),
            (Es.NotchShelfFolder, Es.NotchShelfFile, Es.NotchShelfLink, Es.NotchShelfText));
        Assert.Equal(("Folder", "File", "Link", "Text"),
            (En.NotchShelfFolder, En.NotchShelfFile, En.NotchShelfLink, En.NotchShelfText));
    }

    // ---------------------------------------------------------------- nombres

    /// <summary>Medida falsa: 6 px por carácter (UTF-16).</summary>
    private static float Measure(string s) => s.Length * 6f;

    [Theory]
    [InlineData("informe.pdf", 66f, "informe.pdf")]                         // cabe justo: entero
    [InlineData("informe-anual-2026.pdf", 60f, "infor….pdf")]               // por el medio, conserva la extensión
    [InlineData("hola mundo grande.txt", 60f, "hola….txt")]                  // sin espacio antes de «…»
    [InlineData("LEEME-ANTES-DE-NADA", 60f, "LEEME-ANT…")]                  // sin extensión: por el final
    [InlineData(".gitignore-muy-largo", 60f, ".gitignor…")]                  // ese punto no es una extensión
    [InlineData("a.extremadamente", 60f, "a.extrema…")]                      // «extensión» demasiado larga
    [InlineData("foto.jpeg", 30f, "foto…")]                                  // ni con la extensión cabe: por el final
    [InlineData("larguísimo.txt", 6f, "…")]
    [InlineData("", 60f, "")]
    public void Names_are_trimmed_with_an_ellipsis(string name, float width, string expected)
    {
        var fit = ShelfCardText.FitName(name, width, Measure);
        Assert.Equal(expected, fit);
        Assert.True(Measure(fit) <= Math.Max(width, 6f));
    }

    [Fact]
    public void Only_files_keep_their_extension()
    {
        const string name = "mi.carpeta.larga";
        Assert.Equal("mi.c….larga", ShelfCardText.FitName(name, 66f, Measure));                       // archivo
        Assert.Equal("mi.carpet…", ShelfCardText.FitName(name, 60f, Measure, keepExtension: false)); // carpeta, enlace o nota
        Assert.Equal("Ver v2…", ShelfCardText.FitName("Ver v2.0 final", 42f, Measure, keepExtension: false));
    }

    [Fact]
    public void Trimming_never_splits_a_surrogate_pair()
    {
        const string name = "😀😀😀😀😀😀.png";
        string fit = ShelfCardText.FitName(name, 36f, Measure);
        Assert.Equal("😀😀…", fit);
        foreach (var w in new[] { 12f, 18f, 24f, 30f, 42f, 48f })
        {
            var f = ShelfCardText.FitName(name, w, Measure);
            int dots = f.IndexOf('…');
            Assert.True(dots < 1 || !char.IsHighSurrogate(f[dots - 1]), $"«{f}» a {w} px parte un emoji");
        }
    }
}
