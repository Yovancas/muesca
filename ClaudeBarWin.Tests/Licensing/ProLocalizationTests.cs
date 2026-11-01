using System.Reflection;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests.Licensing;

/// <summary>Los textos de ClaudeBar Pro están en TODOS los idiomas: ninguno cae al inglés (salvo los nombres).</summary>
public class ProLocalizationTests
{
    /// <summary>Nombres o formatos que son iguales en todos los idiomas.</summary>
    private static readonly HashSet<string> SameEverywhere = new()
    {
        nameof(Strings.NotchProChip), nameof(Strings.NotchProTitle), nameof(Strings.ProStatePro), nameof(Strings.OnbProTag),
        nameof(Strings.MenuProFmt),
    };

    private static IEnumerable<PropertyInfo> ProProperties() =>
        typeof(Strings).GetProperties().Where(p => p.Name.StartsWith("NotchPro", StringComparison.Ordinal)
            || p.Name.StartsWith("ProState", StringComparison.Ordinal)
            || p.Name is nameof(Strings.OnbProTag) or nameof(Strings.OnbProTrialNote) or nameof(Strings.OnbProStoreNote)
                or nameof(Strings.OnbProActiveNote) or nameof(Strings.OnbProBuyNote)
            || p.Name is nameof(Strings.MenuProFmt) or nameof(Strings.ProMonitorsLocked) or nameof(Strings.ProThanks));

    [Theory]
    [InlineData("es")]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh-Hant")]
    public void Every_pro_text_is_translated(string code)
    {
        var en = Localization.Get("en");
        var s = Localization.Get(code);
        var props = ProProperties().ToList();
        Assert.True(props.Count >= 30, $"solo {props.Count} textos");
        foreach (var p in props)
        {
            if (p.GetValue(s) is string[] list)
            {
                var enList = (string[])p.GetValue(en)!;
                Assert.Equal(enList.Length, list.Length);
                for (int i = 0; i < list.Length; i++)
                {
                    Assert.False(string.IsNullOrWhiteSpace(list[i]), $"{code}.{p.Name}[{i}] vacío");
                    Assert.NotEqual(enList[i], list[i]);
                }
                continue;
            }
            var value = (string)p.GetValue(s)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{code}.{p.Name} vacío");
            if (!SameEverywhere.Contains(p.Name)) Assert.True(value != (string)p.GetValue(en)!, $"{code}.{p.Name} cae al inglés");
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
    public void Formats_take_their_argument(string code)
    {
        var s = Localization.Get(code);
        foreach (var fmt in new[] { s.MenuProFmt, s.ProStateTrialFmt, s.NotchProPriceFmt, s.NotchProTrialFmt })
            Assert.Contains("{0}", fmt);
        Assert.Equal(6, s.NotchProFeatures.Length);
    }

    /// <summary>La sexta ventaja ya no promete «lo que venga en la 1.x»: nombra las gráficas de uso y el pomodoro avanzado.</summary>
    [Theory]
    [InlineData("en", "Usage charts and advanced pomodoro")]
    [InlineData("es", "Gráficas de uso y pomodoro avanzado")]
    [InlineData("nl", "Gebruiksgrafieken en geavanceerde pomodoro")]
    [InlineData("fr", "Graphiques d'usage et pomodoro avancé")]
    [InlineData("de", "Nutzungsgrafiken & erweiterter Pomodoro")]
    [InlineData("ja", "使用量グラフと高度なポモドーロ")]
    [InlineData("ko", "사용량 그래프와 고급 뽀모도로")]
    [InlineData("zh-Hant", "用量圖表與進階番茄鐘")]
    public void The_sixth_feature_names_charts_and_advanced_pomodoro(string code, string expected)
    {
        var features = Localization.Get(code).NotchProFeatures;
        Assert.Equal(6, features.Length);
        Assert.Equal(expected, features[5]);
        Assert.DoesNotContain(features, f => f.Contains("1.x", StringComparison.Ordinal));
    }
}
