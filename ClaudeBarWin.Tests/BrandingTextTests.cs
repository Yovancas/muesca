using System.Collections;
using System.Reflection;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// La app se llama Muesca (docs/branding.md): ningún texto que ve el usuario dice «ClaudeBar», en ningún idioma, y «Acerca
/// de» explica qué es y que no está afiliada a Anthropic. Los nombres internos (claudebar-helper, claudebar.log) sí pueden
/// aparecer: son los nombres reales de archivos y comandos.
/// </summary>
public class BrandingTextTests
{
    public static IEnumerable<object[]> Languages() => Localization.Languages.Select(l => new object[] { l.Code });

    /// <summary>Todos los textos de un objeto de textos (los de <see cref="Strings"/> y, dentro, los del tutorial).</summary>
    private static IEnumerable<(string Name, string Text)> AllTexts(object strings, string prefix = "")
    {
        // Solo las de instancia: TutorialStrings tiene estáticas (English, Spanish…) que llevarían a una recursión sin fin.
        foreach (var p in strings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var value = p.GetValue(strings);
            switch (value)
            {
                case string text:
                    yield return (prefix + p.Name, text);
                    break;
                case TutorialStrings tut:
                    foreach (var t in AllTexts(tut, prefix + p.Name + ".")) yield return t;
                    break;
                case IEnumerable list when value is not string:
                    int i = 0;
                    foreach (var item in list)
                        if (item is string s) yield return ($"{prefix}{p.Name}[{i++}]", s);
                    break;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_text_says_the_old_name(string code)
    {
        var texts = AllTexts(Localization.Get(code)).ToList();
        Assert.True(texts.Count > 400, $"solo {texts.Count} textos: ¿cambió la forma de Strings?");
        foreach (var (name, text) in texts)
            Assert.False(text.Contains("ClaudeBar", StringComparison.Ordinal), $"{code}.{name}: «{text}»");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void About_says_what_it_is_and_that_it_is_not_anthropic(string code)
    {
        var s = Localization.Get(code);
        Assert.Contains("Muesca", s.AboutProduct);
        Assert.Contains("Claude Code", s.AboutProduct);
        Assert.Contains("Bekay", s.AboutProduct);
        Assert.Contains("Anthropic", s.AboutDisclaimer);
        Assert.Contains("Claude", s.AboutDisclaimer);
        if (code != "en")
        {
            var en = Localization.Get("en");
            Assert.NotEqual(en.AboutProduct, s.AboutProduct);
            Assert.NotEqual(en.AboutDisclaimer, s.AboutDisclaimer);
        }
    }

    [Fact]
    public void The_spanish_disclaimer_is_the_agreed_one()
    {
        Assert.Equal("No afiliado a Anthropic. Claude es una marca de Anthropic.", Localization.Get("es").AboutDisclaimer);
        Assert.Equal("Muesca, el notch para Claude Code · de Bekay", Localization.Get("es").AboutProduct);
    }

    [Fact]
    public void The_brand_constants_are_the_new_ones()
    {
        Assert.Equal("Muesca", Brand.Name);
        Assert.Equal("Bekay", Brand.Publisher);
        Assert.Equal("https://github.com/Yovancas/muesca", Brand.RepoUrl);
    }

    // ---------------------------------------------------------------- «Acerca de» en Ajustes

    [Fact]
    public void Settings_about_ends_with_the_product_line_and_the_disclaimer_below_import()
    {
        using var bmp = new Bitmap(400, 4000);
        using var g = Graphics.FromImage(bmp);
        var s = Localization.Get("es");
        var cfg = new Config.AppConfig();
        var rects = new Dictionary<string, Rectangle>();
        int measured = DashboardSettingsView.Draw(g, draw: false, 12, 0, 360, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.Draw(g, draw: true, 12, 0, 360, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);
        Assert.Equal(measured, painted);   // medir == pintar, con las dos notas
        var import = rects["special:importtheme"];
        // Las dos notas van debajo del botón de importar: ocupan al menos dos líneas de la fuente pequeña.
        Assert.True(painted - import.Bottom >= 2 * (int)Typography.Caption.GetHeight(g));
    }

    [Fact]
    public void A_note_wraps_in_a_narrow_column_and_measures_what_it_paints()
    {
        using var bmp = new Bitmap(200, 400);
        using var g = Graphics.FromImage(bmp);
        var text = Localization.Get("de").AboutDisclaimer;
        int wide = DashboardSettingsView.NoteRow(g, draw: false, text, 0, 0, 1000, Theme.Dark, Typography.Caption);
        int narrow = DashboardSettingsView.NoteRow(g, draw: false, text, 0, 0, 120, Theme.Dark, Typography.Caption);
        Assert.True(narrow > wide, "en una columna estrecha la nota baja de línea");
        Assert.Equal(narrow, DashboardSettingsView.NoteRow(g, draw: true, text, 0, 0, 120, Theme.Dark, Typography.Caption));
        Assert.Equal(7, DashboardSettingsView.NoteRow(g, draw: true, "  ", 0, 7, 120, Theme.Dark, Typography.Caption));
    }
}
