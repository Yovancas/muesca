using ClaudeBarWin.Services;

/// <summary>Los textos nuevos de Ajustes (el estado de «Iniciar con Windows» en la Store y «Quitar la integración con Claude
/// Code»), en los 8 idiomas: ninguno cae al inglés, y los formatos llevan su hueco.</summary>
public class PrereleaseLocalizationTests
{
    private static readonly Func<Strings, string>[] Texts =
    {
        s => s.StartupChecking, s => s.StartupChanging,
        s => s.RemoveIntegration, s => s.RemoveIntegrationHint, s => s.RemoveIntegrationConfirm,
        s => s.RemoveIntegrationDoneFmt, s => s.RemoveIntegrationNothing,
    };

    [Theory]
    [InlineData("es")]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh-Hant")]
    public void Every_new_settings_text_is_translated(string code)
    {
        var en = Localization.Get("en");
        var s = Localization.Get(code);
        for (int i = 0; i < Texts.Length; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(Texts[i](s)), $"{code}: texto {i} vacío");
            Assert.NotEqual(Texts[i](en), Texts[i](s));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh-Hant")]
    public void The_removal_texts_say_what_they_touch(string code)
    {
        var s = Localization.Get(code);
        Assert.Contains("{0}", s.RemoveIntegrationDoneFmt);
        Assert.Contains(@"C:\x.bak", string.Format(s.RemoveIntegrationDoneFmt, @"C:\x.bak"));   // el formato no lanza
        Assert.Contains("settings.json", s.RemoveIntegrationConfirm);
        Assert.Contains("Claude Code", s.RemoveIntegration);
        Assert.DoesNotContain("ClaudeBar", s.RemoveIntegrationConfirm);
    }
}
