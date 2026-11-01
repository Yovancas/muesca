using System.Globalization;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T2 (auditoría 2026-06-10): con Language=en la UI mostraba "$420,50" y "jue 02:12" porque los
/// format strings de números/moneda/fechas usaban la CurrentCulture del SO en vez de la cultura
/// del idioma ELEGIDO. Estas pruebas fijan el contrato: cada idioma lleva su CultureInfo
/// (en→en-US, es→es-ES…), "system" conserva la del SO, y los helpers de formato la respetan.
/// </summary>
public class LocalizationCultureTests
{
    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("es", "es-ES")]
    [InlineData("nl", "nl-NL")]
    [InlineData("fr", "fr-FR")]
    [InlineData("de", "de-DE")]
    [InlineData("ja", "ja-JP")]
    [InlineData("ko", "ko-KR")]
    [InlineData("zh-Hant", "zh-TW")]
    public void Cada_idioma_lleva_su_cultura_especifica(string code, string expectedCulture)
    {
        Assert.Equal(expectedCulture, Localization.Get(code).Culture.Name);
    }

    // ---- F11 (v0.3.9): el back del panel usa _s.Back ("‹ Volver"), no _s.Settings ("‹ Ajustes") ----
    // El string Back estaba "muerto" en el binario (solo lo definía es); el back se leía como TÍTULO.
    // Ahora Back debe estar PRESENTE y no vacío en LOS 9 IDIOMAS (invariante de i18n), así "‹ <Back>"
    // se lee como afordancia de volver en cualquier idioma.

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh-Hant")]
    public void Back_esta_presente_y_no_vacio_en_cada_idioma(string code)
    {
        var back = Localization.Get(code).Back;
        Assert.False(string.IsNullOrWhiteSpace(back), $"el idioma '{code}' no tiene string Back (back-behavior)");
    }

    [Fact]
    public void Back_esta_localizado_en_los_idiomas_no_ingleses()
    {
        // Cada idioma con traducción propia difiere del default inglés "Back" (revive el string muerto).
        // (en→"Back" es el default; los demás deben traducirlo, no caer al inglés.)
        var en = Localization.Get("en").Back; // "Back"
        var expected = new Dictionary<string, string>
        {
            ["es"] = "Volver", ["nl"] = "Terug", ["fr"] = "Retour", ["de"] = "Zurück",
            ["ja"] = "戻る", ["ko"] = "뒤로", ["zh-Hant"] = "返回",
        };
        foreach (var (code, word) in expected)
        {
            var back = Localization.Get(code).Back;
            Assert.Equal(word, back);
            Assert.NotEqual(en, back); // no cae al default inglés
        }
    }

    [Fact]
    public void Toda_cultura_de_idioma_es_especifica_no_neutral()
    {
        foreach (var (code, _) in Localization.Languages)
        {
            var ci = Localization.Get(code).Culture;
            Assert.False(ci.IsNeutralCulture, $"cultura de '{code}' es neutral ({ci.Name})");
            Assert.NotEqual(CultureInfo.InvariantCulture, ci);
        }
    }

    [Fact]
    public void ForConfig_con_idioma_explicito_usa_la_cultura_del_idioma_no_la_del_SO()
    {
        var s = Localization.ForConfig(new AppConfig { Language = "en" });
        Assert.Equal("en-US", s.Culture.Name);
    }

    [Fact]
    public void ForConfig_con_system_conserva_la_cultura_del_SO()
    {
        var s = Localization.ForConfig(new AppConfig { Language = "system" });
        Assert.Equal(CultureInfo.CurrentCulture, s.Culture);
    }

    // ---- Moneda ----

    [Fact]
    public void Money_formatea_con_punto_decimal_en_ingles()
    {
        Assert.Equal("$420.50", UsageFormat.Money(420.50, Localization.Get("en").Culture));
    }

    [Fact]
    public void Money_formatea_con_coma_decimal_en_espanol()
    {
        Assert.Equal("$420,50", UsageFormat.Money(420.50, Localization.Get("es").Culture));
    }

    // ---- Porcentaje (tween de %, glance, mini-filas de modelo) ----

    [Fact]
    public void Percent_usa_el_separador_decimal_de_la_cultura()
    {
        Assert.Equal("12.5%", UsageFormat.Percent(12.5, Localization.Get("en").Culture));
        Assert.Equal("12,5%", UsageFormat.Percent(12.5, Localization.Get("es").Culture));
    }

    [Fact]
    public void Percent_sin_decimales_omite_el_separador()
    {
        Assert.Equal("12%", UsageFormat.Percent(12.0, Localization.Get("en").Culture));
        Assert.Equal("12%", UsageFormat.Percent(12.0, Localization.Get("es").Culture));
    }

    // ---- Día abreviado del reset ("resets in 1h 39m · jue 02:12") ----

    [Fact]
    public void ResetAbsolute_usa_el_dia_abreviado_del_idioma_elegido()
    {
        var when = new DateTimeOffset(2026, 6, 2, 16, 42, 0, TimeSpan.Zero);
        var en = UsageFormat.ResetAbsolute(when, Localization.Get("en").Culture);
        var es = UsageFormat.ResetAbsolute(when, Localization.Get("es").Culture);

        // En inglés el día abreviado es inglés (independiente del TZ de la máquina)…
        Assert.Matches(@"^(Mon|Tue|Wed|Thu|Fri|Sat|Sun) \d{2}:\d{2}$", en);
        // …y en español difiere (lun./mar./mié./… según datos ICU del runtime).
        Assert.NotEqual(en, es);
        Assert.Equal(when.ToLocalTime().ToString("ddd HH:mm", Localization.Get("es").Culture), es);
    }

    // ---- Etiquetas del eje X de la gráfica (ddd de la vista semanal) ----

    [Fact]
    public void UsageHistory_etiqueta_los_dias_con_la_cultura_pedida()
    {
        var nowUtc = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var en = UsageHistory.Build(Enumerable.Empty<UsageRecord>(), ChartRange.Week1, nowUtc,
            Localization.Get("en").Culture);
        var es = UsageHistory.Build(Enumerable.Empty<UsageRecord>(), ChartRange.Week1, nowUtc,
            Localization.Get("es").Culture);

        Assert.Equal(7, en.Count);
        for (int i = 0; i < en.Count; i++)
        {
            Assert.Equal(en[i].StartLocal.ToString("ddd", Localization.Get("en").Culture), en[i].Label);
            Assert.Equal(es[i].StartLocal.ToString("ddd", Localization.Get("es").Culture), es[i].Label);
            Assert.NotEqual(en[i].Label, es[i].Label); // Mon≠lun., Tue≠mar., …
        }
    }
}
