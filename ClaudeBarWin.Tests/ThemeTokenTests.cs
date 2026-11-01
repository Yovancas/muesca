using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests;

public class ThemeTokenTests
{
    public static IEnumerable<object[]> Themes =>
        new[] { new object[] { Theme.Dark }, new object[] { Theme.Light }, new object[] { Theme.Cli } };

    [Theory]
    [MemberData(nameof(Themes))]
    public void All_tokens_are_opaque(Theme t)
    {
        var tokens = new[] { t.TextPrimary, t.TextSecondary, t.TextMuted, t.Separator, t.Track,
                             t.BgBase, t.BgElevated, t.Accent, t.Ok, t.Warn, t.Critical, t.Neutral,
                             t.AccentText, t.TickOnTrack, t.WarnText, t.CriticalText, t.HoverBg,
                             t.MascotIdle };
        Assert.All(tokens, c => Assert.True(c.A > 0, "token transparente/sin setear"));
    }

    [Fact]
    public void Dark_and_light_accent_is_the_muesca_violet()
    {
        // El acento es el violeta de la marca (principio del degradado de branding/muesca.svg), ya no el naranja de Claude.
        Assert.Equal(Color.FromArgb(0x7B, 0x4D, 0xFF), Theme.Dark.Accent);
        Assert.Equal(Brand.Violet, Theme.Light.Accent);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Accent_is_not_the_old_claude_orange(Theme t)
    {
        // El naranja coral (#CC785C, tono ~15°) era el de Claude: ningún token de acento puede volver a esa zona del círculo.
        foreach (var c in new[] { t.Accent, t.AccentText })
        {
            float hue = c.GetHue();
            Assert.False(hue < 45f || hue > 345f, $"[{t.Id}] acento {c} con tono {hue:0}° (naranja/rojo)");
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_on_the_accent_meets_aa(Theme t)
    {
        // Botón principal, chip activo, visto bueno: el texto ENCIMA del relleno de acento. Con el violeta es blanco (~4.8:1);
        // el «texto oscuro sobre el acento» de antes caería a ~3.7:1.
        double r = ColorMath.ContrastRatio(t.OnAccent, t.Accent);
        Assert.True(r >= 4.5, $"[{t.Id}] OnAccent sobre Accent contrasta {r:0.00}:1 (< 4.5)");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Hovered_accent_keeps_the_text_on_it_legible(Theme t)
    {
        // El hover de los botones de acento no puede bajar el texto de encima de AA (aclarar el violeta lo dejaba en ~4.1:1).
        foreach (var k in new[] { 0.25, 0.5, 1.0 })
        {
            var hover = ColorMath.HoverFill(t.Accent, k);
            Assert.NotEqual(t.Accent.ToArgb(), hover.ToArgb());
            double r = ColorMath.ContrastRatio(ColorMath.Contrast(hover), hover);
            Assert.True(r >= 4.5, $"[{t.Id}] texto sobre el acento con hover {k} contrasta {r:0.00}:1 (< 4.5)");
            Assert.Equal(t.OnAccent.ToArgb(), ColorMath.Contrast(hover).ToArgb());
        }
    }

    [Fact]
    public void Accent_fill_meets_non_text_contrast_on_panels_and_on_the_notch()
    {
        // Toggles, puntos, anillos, flechas y foco (WCAG 1.4.11, ≥3:1): sobre el fondo de cada tema y sobre el negro del notch.
        Assert.True(ColorMath.ContrastRatio(Theme.Dark.Accent, Theme.Dark.Background) >= 3.0);
        Assert.True(ColorMath.ContrastRatio(Theme.Light.Accent, Theme.Light.Background) >= 3.0);
        Assert.True(ColorMath.ContrastRatio(Theme.Dark.Accent, Color.Black) >= 3.0);
        Assert.True(ColorMath.ContrastRatio(Theme.Cli.Accent, Theme.Cli.Background) >= 3.0);
    }

    [Fact]
    public void Accent_as_text_meets_aa_on_the_black_notch()
    {
        // El notch es negro siempre (pinta con el tema oscuro): el acento como texto («Hecho», «Muesca Pro», la fase del foco).
        double r = ColorMath.ContrastRatio(Theme.Dark.AccentText, Color.Black);
        Assert.True(r >= 4.5, $"AccentText sobre el negro del notch contrasta {r:0.00}:1 (< 4.5)");
    }

    [Fact]
    public void Dark_has_two_background_levels()
        => Assert.NotEqual(Theme.Dark.BgBase, Theme.Dark.BgElevated);

    [Fact]
    public void Semantic_aliases_map_to_existing_fields()
    {
        Assert.Equal(Theme.Dark.Foreground, Theme.Dark.TextPrimary);
        Assert.Equal(Theme.Dark.Dim, Theme.Dark.TextSecondary);
        Assert.Equal(Theme.Dark.Background, Theme.Dark.BgBase);
    }

    // --- T9: contraste del tema claro (auditoría visual: gris tenue y verdes ilegibles) ---

    [Fact]
    public void Light_text_muted_meets_aa_small_text_contrast()
    {
        // WCAG AA para texto pequeño exige ≥ 4.5:1. El valor anterior (#8E8E93) caía a ~3.1:1.
        double r = ColorMath.ContrastRatio(Theme.Light.TextMuted, Theme.Light.Background);
        Assert.True(r >= 4.5, $"TextMuted del tema claro contrasta {r:0.00}:1 (< 4.5)");
    }

    [Fact]
    public void Light_success_green_meets_aa_small_text_contrast()
    {
        // El verde de éxito (Ok) se usa también como texto pequeño (línea de salud, badges). El valor
        // anterior (#16A34A) caía a ~3.2:1 sobre el fondo claro.
        double r = ColorMath.ContrastRatio(Theme.Light.Ok, Theme.Light.Background);
        Assert.True(r >= 4.5, $"verde de éxito del tema claro contrasta {r:0.00}:1 (< 4.5)");
    }

    [Fact]
    public void Light_secondary_text_keeps_aa_contrast()
    {
        // No regresar el gris secundario (#71717A) que ya cumplía (~4.6:1).
        double r = ColorMath.ContrastRatio(Theme.Light.TextSecondary, Theme.Light.Background);
        Assert.True(r >= 4.5, $"TextSecondary del tema claro contrasta {r:0.00}:1 (< 4.5)");
    }

    // --- v0.3.5 P1 #3: subir el contraste donde fallaba (texto pequeño AA ≥ 4.5:1) ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_muted_meets_aa_small_text_contrast_in_every_theme(Theme t)
    {
        // El gris/verde tenue (subtítulos, footer, verbo de mascota) debe ser legible en TODOS los temas.
        // El CLI tenía #006E2C (~3.3:1 sobre negro) → ilegible; ahora #00963C (~5.4:1). Un único token
        // TextMuted por tema que cumple AA.
        double r = ColorMath.ContrastRatio(t.TextMuted, t.Background);
        Assert.True(r >= 4.5, $"[{t.Id}] TextMuted contrasta {r:0.00}:1 (< 4.5)");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Accent_as_text_meets_aa_small_text_contrast_in_every_theme(Theme t)
    {
        // El acento usado como TEXTO/borde fino (botón "Importar tema") debe cumplir AA. El violeta de
        // relleno (#7B4DFF) se queda en ~3.7:1 como texto sobre el fondo oscuro y justo en ~4.6:1 sobre el
        // claro → AccentText lo aclara en oscuro (#B49BFF, el anillo del logo) y lo oscurece en claro (#6236F0).
        double r = ColorMath.ContrastRatio(t.AccentText, t.Background);
        Assert.True(r >= 4.5, $"[{t.Id}] AccentText contrasta {r:0.00}:1 (< 4.5)");
    }

    [Fact]
    public void AccentText_falls_back_to_accent_only_where_it_is_already_legible()
    {
        // En CLI el verde ya es legible como texto → AccentText == Accent (sin override).
        Assert.Equal(Theme.Cli.Accent, Theme.Cli.AccentText);
        // El violeta no llega a AA como texto en oscuro: se aclara al lavanda del anillo del logo.
        Assert.Equal(Brand.RingStart, Theme.Dark.AccentText);
        // En claro se oscurece, con el mismo tinte (tono violeta).
        Assert.NotEqual(Theme.Light.Accent, Theme.Light.AccentText);
        Assert.True(FocusPalette.HueDistance(Theme.Light.AccentText, Theme.Light.Accent) < 10f);
    }

    // --- T3b: ticks de umbral de QuotaBar invisibles (Separator ≈ Track en los 3 temas) ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Tick_on_track_meets_non_text_contrast_in_every_theme(Theme t)
    {
        // WCAG 1.4.11 (contraste no textual) exige ≥3:1 para indicadores gráficos. Los ticks de umbral
        // usaban Separator, ≈ idéntico al Track en los 3 temas (CLI: exacto, ~1:1) → invisibles.
        double r = ColorMath.ContrastRatio(t.TickOnTrack, t.Track);
        Assert.True(r >= 3.0, $"[{t.Id}] TickOnTrack contrasta {r:0.00}:1 (< 3.0) sobre Track");
    }

    [Fact]
    public void TickOnTrack_falls_back_to_text_muted()
    {
        // Sin override, el token cae a TextMuted (que ya cumple ≥3:1 sobre Track en los 3 temas);
        // los temas importados heredan el fallback sin tener que mapear un campo nuevo.
        Assert.Equal(Theme.Dark.TextMuted, Theme.Dark.TickOnTrack);
        Assert.Equal(Theme.Light.TextMuted, Theme.Light.TickOnTrack);
        Assert.Equal(Theme.Cli.TextMuted, Theme.Cli.TickOnTrack);
    }

    // --- T6b: tokens de TEXTO Warn/Critical (patrón AccentText) + barrido global AA ≥ 4.5:1 ---

    public static IEnumerable<object[]> TextTokens()
    {
        foreach (var t in new[] { Theme.Dark, Theme.Light, Theme.Cli })
            foreach (var (name, c) in new (string, Color)[]
            {
                ("TextPrimary", t.TextPrimary), ("TextSecondary", t.TextSecondary),
                ("TextMuted", t.TextMuted), ("AccentText", t.AccentText),
                ("Ok(texto)", t.Ok),         // el verde Ok se usa directo como texto (pace, %, salud)
                ("WarnText", t.WarnText), ("CriticalText", t.CriticalText),
            })
                yield return new object[] { t, name, c };
    }

    [Theory]
    [MemberData(nameof(TextTokens))]
    public void Every_text_token_meets_aa_small_text_contrast(Theme t, string name, Color c)
    {
        // Barrido global (T6b): TODO color que la UI pinta como texto pequeño debe cumplir AA (≥4.5:1)
        // sobre el fondo del panel, en los 3 temas. Antes fallaban Light.Warn (2.8:1) y Dark.Critical
        // (3.7:1) — el texto más crítico del panel era el de peor contraste.
        double r = ColorMath.ContrastRatio(c, t.Background);
        Assert.True(r >= 4.5, $"[{t.Id}] {name} contrasta {r:0.00}:1 (< 4.5) sobre el fondo");
    }

    [Fact]
    public void WarnText_falls_back_to_warn_and_only_light_overrides()
    {
        // En oscuro (5.6:1) y CLI (12.3:1) el ámbar de relleno ya es legible como texto → sin override.
        Assert.Equal(Theme.Dark.Warn, Theme.Dark.WarnText);
        Assert.Equal(Theme.Cli.Warn, Theme.Cli.WarnText);
        // El claro SÍ lo oscurece (#CA8A04 como texto = 2.8:1).
        Assert.NotEqual(Theme.Light.Warn, Theme.Light.WarnText);
    }

    [Fact]
    public void CriticalText_falls_back_to_critical_and_only_dark_overrides()
    {
        // En claro (4.6:1) y CLI (5.6:1) el rojo de relleno ya es legible como texto → sin override.
        Assert.Equal(Theme.Light.Critical, Theme.Light.CriticalText);
        Assert.Equal(Theme.Cli.Critical, Theme.Cli.CriticalText);
        // El oscuro SÍ lo aclara (#DC2626 como texto = 3.7:1).
        Assert.NotEqual(Theme.Dark.Critical, Theme.Dark.CriticalText);
    }

    [Fact]
    public void Status_fill_tokens_do_not_change_with_t6()
    {
        // T6b toca SOLO las variantes de texto: los rellenos Warn/Critical (barras, dots, badges)
        // conservan sus valores. Pin de regresión del "fills no cambian".
        Assert.Equal(Color.FromArgb(202, 138, 4), Theme.Light.Warn);
        Assert.Equal(Color.FromArgb(220, 38, 38), Theme.Dark.Critical);
    }

    // --- T7b: token HoverBg (el realce con BgElevated puro era invisible en claro y CLI) ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Hover_bg_is_perceptibly_distinct_from_background_in_every_theme(Theme t)
    {
        // El realce de hover es decoración transitoria (no necesita el 3:1 de 1.4.11), pero SÍ debe
        // ser perceptible. BgElevated puro daba claro #FFF/#FAFAFA ≈ 1.03:1 y CLI #0A100A/negro ≈
        // 1.05:1 (invisibles); el token nuevo debe levantar ≥ ~1.1:1 del fondo en los 3 temas.
        Assert.NotEqual(t.Background.ToArgb(), t.HoverBg.ToArgb());
        double r = ColorMath.ContrastRatio(t.HoverBg, t.Background);
        Assert.True(r >= 1.10, $"[{t.Id}] HoverBg contrasta {r:0.000}:1 (< 1.10) sobre el fondo: hover invisible");
    }

    [Fact]
    public void HoverBg_falls_back_to_bg_fg_lerp_and_only_cli_overrides()
    {
        // Sin override el token cae a lerp(Background, Foreground, 0.08) (+8 % de luminosidad del
        // lenguaje de movimiento §4 #4) — también para los temas importados, sin mapear un campo nuevo.
        // El CLI sube la mezcla (con tan poco el verde casi no levanta del negro puro).
        Assert.Equal(ColorMath.Lerp(Theme.Dark.Background, Theme.Dark.Foreground, 0.08), Theme.Dark.HoverBg);
        Assert.Equal(ColorMath.Lerp(Theme.Light.Background, Theme.Light.Foreground, 0.08), Theme.Light.HoverBg);
        Assert.NotEqual(ColorMath.Lerp(Theme.Cli.Background, Theme.Cli.Foreground, 0.08), Theme.Cli.HoverBg);
    }

    // --- T9b: knob del TogglePill blanco con borde sutil (§3 #11: "agujero perforado") ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Knob_fill_is_white_in_every_theme(Theme t)
    {
        // Estándar iOS/Fluent: knob BLANCO en todos los temas. El histórico usaba el color del texto
        // (negro sobre el track claro = "agujero perforado") o ColorMath.Contrast(Accent).
        Assert.Equal(Color.White.ToArgb(), t.KnobFill.ToArgb());
    }

    [Fact]
    public void Knob_border_is_a_translucent_scrim()
    {
        // El borde es un velo negro translúcido: delimita el círculo blanco sobre CUALQUIER track
        // (claro u oscuro) sin introducir un color opaco nuevo por tema.
        var b = Theme.Dark.KnobBorder;
        Assert.True(b.A > 0 && b.A < 255, $"el borde del knob debe ser translúcido (alpha {b.A})");
        Assert.Equal(Theme.Light.KnobBorder, Theme.Dark.KnobBorder);
        Assert.Equal(Theme.Cli.KnobBorder, Theme.Dark.KnobBorder);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Knob_fill_meets_non_text_contrast_on_accent_track_or_border_compensates(Theme t)
    {
        // Sobre el track ON (Accent) el knob blanco da ~4.8:1 en oscuro/claro (≥3:1, WCAG 1.4.11).
        // En CLI (verde #00D959) cae a ~1.9:1: ahí delimita el borde KnobBorder y el estado lo
        // comunican posición + color del track. Documentamos el suelo real (~1.9:1) como pin.
        double r = ColorMath.ContrastRatio(t.KnobFill, t.Accent);
        Assert.True(r >= 1.85, $"[{t.Id}] knob sobre Accent contrasta {r:0.00}:1 (< 1.85)");
    }

    // --- T13a: gama de las series del gráfico (token por tema; la vista no lleva hexes) ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chart_series_has_at_least_four_opaque_slots(Theme t)
    {
        // El cap de familias es 4 (3 con nombre + "Otros"): cada tema debe aportar al menos 4
        // colores de serie, opacos (van como relleno del área apilada).
        Assert.True(t.ChartSeries.Count >= 4, $"[{t.Id}] solo {t.ChartSeries.Count} slots de serie");
        Assert.All(t.ChartSeries, c => Assert.Equal(255, c.A));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chart_series_slots_are_distinct_within_a_theme(Theme t)
        => Assert.Equal(t.ChartSeries.Count, t.ChartSeries.Select(c => c.ToArgb()).Distinct().Count());

    [Fact]
    public void Cli_chart_series_uses_its_own_phosphor_gamut()
    {
        // El CLI no hereda el violet/sky de dark/light: gama propia verde-fósforo (G domina en RGB).
        Assert.NotEqual(Theme.Dark.ChartSeries[0].ToArgb(), Theme.Cli.ChartSeries[0].ToArgb());
        Assert.All(Theme.Cli.ChartSeries, c => Assert.True(c.G > c.R && c.G > c.B,
            $"slot CLI {c} no es verde-fósforo (G debe dominar)"));
    }

    [Fact]
    public void Dark_and_light_keep_the_legacy_series_gamut()
    {
        // Pin de regresión: dark/light conservan la gama que antes iba hardcodeada en la vista
        // (violet/sky/emerald/slate) — T13a solo cambia QUIÉN recibe cada color (por rango).
        Assert.Equal(Color.FromArgb(167, 139, 250).ToArgb(), Theme.Dark.ChartSeries[0].ToArgb());
        Assert.Equal(Color.FromArgb(56, 189, 248).ToArgb(), Theme.Dark.ChartSeries[1].ToArgb());
        Assert.Equal(Color.FromArgb(52, 211, 153).ToArgb(), Theme.Dark.ChartSeries[2].ToArgb());
        Assert.Equal(Color.FromArgb(148, 163, 184).ToArgb(), Theme.Dark.ChartSeries[3].ToArgb());
        Assert.Equal(Theme.Dark.ChartSeries.Select(c => c.ToArgb()),
                     Theme.Light.ChartSeries.Select(c => c.ToArgb()));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Pace_text_color_maps_to_text_variants(Theme t)
    {
        // El selector de color de TEXTO por estado de pace (QuotaBar %, glance del header, línea de
        // pace) usa las variantes AA, no los rellenos.
        Assert.Equal(t.CriticalText, Theme.PaceTextColor(t, PaceStatus.Critical));
        Assert.Equal(t.WarnText, Theme.PaceTextColor(t, PaceStatus.Over));
        Assert.Equal(t.Ok, Theme.PaceTextColor(t, PaceStatus.Ok));
    }

    // --- v0.3.9 F1: color de la mascota Idle (glifo no textual, WCAG 1.4.11 ≥3:1) ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Mascot_idle_meets_non_text_contrast_in_every_theme(Theme t)
    {
        // El gatito en reposo es un glifo NO textual: WCAG 1.4.11 exige ≥3:1 sobre el fondo. El viejo
        // Neutral caía a ~2.3:1 en oscuro (el tema por defecto). MascotIdle → TextMuted lo lleva a ~5:1.
        double r = ColorMath.ContrastRatio(t.MascotIdle, t.Background);
        Assert.True(r >= 3.0, $"[{t.Id}] MascotIdle contrasta {r:0.00}:1 (< 3.0) sobre el fondo");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Mascot_idle_clears_the_old_neutral_floor(Theme t)
    {
        // Regresión directa: MascotIdle debe contrastar MEJOR que el Neutral histórico (que fallaba el
        // 3:1 en oscuro). En oscuro Neutral daba ~2.3:1; TextMuted ~5:1.
        double idle = ColorMath.ContrastRatio(t.MascotIdle, t.Background);
        double neutral = ColorMath.ContrastRatio(t.Neutral, t.Background);
        Assert.True(idle >= neutral, $"[{t.Id}] MascotIdle ({idle:0.00}) no mejora sobre Neutral ({neutral:0.00})");
    }

    [Fact]
    public void Dark_neutral_failed_the_non_text_floor_motivating_the_token()
    {
        // Pin del problema que motiva F1: el Neutral del tema por defecto (oscuro) caía bajo 3:1.
        double r = ColorMath.ContrastRatio(Theme.Dark.Neutral, Theme.Dark.Background);
        Assert.True(r < 3.0, $"se esperaba que Dark.Neutral fallara el 3:1, contrasta {r:0.00}:1");
    }

    [Fact]
    public void MascotIdle_falls_back_to_text_muted()
    {
        // Sin override el token cae a TextMuted (ya AA como texto en los 3 temas); los temas importados
        // heredan el fallback sin mapear un campo nuevo.
        Assert.Equal(Theme.Dark.TextMuted, Theme.Dark.MascotIdle);
        Assert.Equal(Theme.Light.TextMuted, Theme.Light.MascotIdle);
        Assert.Equal(Theme.Cli.TextMuted, Theme.Cli.MascotIdle);
    }
}
