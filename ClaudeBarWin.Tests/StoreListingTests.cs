using System.Text.RegularExpressions;
using ClaudeBarWin.Services;

/// <summary>
/// La ficha de la Store (store/listing/&lt;idioma&gt;.md): una por idioma de la app, con todos los apartados y dentro de los
/// límites de Partner Center y de las políticas (store/listing/README.md). Un texto que se pasa lo rechaza Partner Center
/// al enviar; uno que nombra otro producto en las palabras clave lo rechaza certificación (10.1.3).
/// </summary>
public class StoreListingTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "TestData", "store-listing");

    private static readonly string[] Sections =
    {
        "Nombre", "Subtítulo", "Descripción corta", "Descripción", "Novedades de esta versión", "Funciones", "Palabras clave",
        "Requisitos", "Complemento Pro: título", "Complemento Pro: descripción", "Complemento Pro: palabras clave",
        "Títulos de las capturas",
    };

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var (code, _) in Localization.Languages) data.Add(code);
        return data;
    }

    private static Dictionary<string, string> Read(string lang)
    {
        var path = Path.Combine(Dir, lang + ".md");
        Assert.True(File.Exists(path), $"falta la ficha de {lang}: store/listing/{lang}.md");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        return Regex.Matches(text, @"^## ([^\n]+)\n(.*?)(?=^## |\z)", RegexOptions.Singleline | RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value.Trim(), m => m.Groups[2].Value.Trim());
    }

    private static List<string> Numbered(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => Regex.Replace(l, @"^\d+\.\s*", "")).ToList();

    private static List<string> Bullets(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..].Trim()).ToList();

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_language_has_every_section(string lang)
    {
        var listing = Read(lang);
        foreach (var section in Sections)
            Assert.False(string.IsNullOrWhiteSpace(listing.GetValueOrDefault(section)), $"{lang}: falta «{section}»");
        Assert.Equal("Muesca", listing["Nombre"]);
        Assert.Equal("Muesca Pro", listing["Complemento Pro: título"]);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Texts_fit_the_Partner_Center_limits(string lang)
    {
        var l = Read(lang);
        Assert.InRange(l["Subtítulo"].Length, 1, 50);
        Assert.InRange(l["Descripción corta"].Length, 1, 270);   // recomendado; el campo admite 1000
        Assert.InRange(l["Descripción"].Length, 1, 10_000);
        Assert.InRange(l["Novedades de esta versión"].Length, 1, 1500);
        Assert.InRange(l["Complemento Pro: título"].Length, 1, 100);
        Assert.InRange(l["Complemento Pro: descripción"].Length, 1, 200);

        var features = Numbered(l["Funciones"]);
        Assert.InRange(features.Count, 1, 20);
        Assert.All(features, f => Assert.InRange(f.Length, 1, 200));

        var addOnKeywords = Bullets(l["Complemento Pro: palabras clave"]);
        Assert.InRange(addOnKeywords.Count, 1, 10);
        Assert.All(addOnKeywords, k => Assert.InRange(k.Length, 1, 30));

        // Un pie por captura, en el orden de store/screenshots.md (10).
        var captions = Numbered(l["Títulos de las capturas"]);
        Assert.Equal(10, captions.Count);
        Assert.All(captions, c => Assert.InRange(c.Length, 1, 200));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Search_terms_follow_policy_10_1_3(string lang)
    {
        var keywords = Bullets(Read(lang)["Palabras clave"]);
        Assert.InRange(keywords.Count, 1, 7);
        Assert.All(keywords, k => Assert.InRange(k.Length, 1, 40));
        Assert.True(keywords.Sum(k => k.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length) <= 21, $"{lang}: más de 21 palabras");
        // Ni títulos de otros productos ni precios.
        foreach (var k in keywords)
        {
            Assert.DoesNotContain("claude", k, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("anthropic", k, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"(?i)\b(free|gratis|gratuit|kostenlos|gratuito)\b|€|\$|無料|무료|免費", k);
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Before_uninstalling_the_listing_points_to_the_remove_integration_button(string lang)
    {
        // MSIX no ejecuta nada nuestro al desinstalar: la ficha manda al botón de Ajustes, con su nombre exacto en la app.
        var description = Read(lang)["Descripción"];
        var s = Localization.Get(lang);
        Assert.Contains(s.RemoveIntegration, description);
        Assert.Contains(s.Settings, description);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void The_description_discloses_the_dependency_first_and_is_honest(string lang)
    {
        var l = Read(lang);
        var description = l["Descripción"];
        // 10.2.4: la dependencia, al principio de la descripción.
        var first = description.Split('\n')[0];
        Assert.Contains("Claude Code", first);
        Assert.Contains("Anthropic", first);   // no afiliada
        Assert.Contains("Claude Code", l["Descripción corta"]);
        Assert.Contains("Anthropic", l["Descripción corta"]);
        // La marca, al final; los datos que salen, nombrados; y lo que pasa al acabar la prueba (10.8.4).
        Assert.Contains("Anthropic, PBC", description);
        Assert.Contains("lrclib.net", description);
        Assert.Contains("settings.json", description);
        Assert.Contains("7", description);
        // Sin precios en el texto: la Store los enseña en la moneda de cada mercado (store/listing/README.md).
        Assert.DoesNotMatch(@"\d+[.,]\d\d\s*€|€\s*\d|\$\s*\d", description);
        // Las funciones Pro van marcadas como Pro.
        Assert.Contains(Numbered(l["Funciones"]), f => f.StartsWith("Pro", StringComparison.Ordinal));
    }
}
