using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services;

/// <summary>Colour palette used by the dashboard and tray icon.</summary>
public sealed class Theme
{
    public required string Id { get; init; }
    public Color Background { get; init; }
    public Color Foreground { get; init; }
    public Color Dim { get; init; }
    public Color Track { get; init; }
    public Color Ok { get; init; }
    public Color Warn { get; init; }
    public Color Critical { get; init; }
    public Color Neutral { get; init; }

    // --- Tokens semánticos (Fase 1) ---
    public Color Accent { get; init; }
    public Color BgElevated { get; init; }
    public Color TextMuted { get; init; }
    public Color Separator { get; init; }

    /// <summary>
    /// Variante del acento legible cuando se usa como TEXTO/borde fino (no como relleno). El
    /// <see cref="Accent"/> de relleno lleva <see cref="OnAccent"/> encima y contrasta bien; pero
    /// pintado como texto sobre el fondo del panel puede caer por debajo de AA (el violeta de Muesca
    /// #7B4DFF sobre el fondo oscuro da ~3.7:1). Este token mueve el acento lo justo para texto pequeño
    /// (≥4.5:1) manteniendo el tinte: en oscuro lo aclara al lavanda del anillo del logo (#B49BFF) y en
    /// claro lo oscurece (#6236F0). En CLI coincide con <see cref="Accent"/> (el verde ya es legible).
    /// Si no se setea, cae a <see cref="Accent"/>.
    /// </summary>
    public Color? AccentTextOverride { get; init; }
    public Color AccentText => AccentTextOverride ?? Accent;

    /// <summary>
    /// Texto e iconos ENCIMA de un relleno <see cref="Accent"/> (botón principal, chip activo, visto
    /// bueno): el lado blanco/negro que más contrasta (<see cref="ColorMath.Contrast"/>). Con el violeta
    /// de Muesca es blanco (~4.8:1, AA); con el verde del CLI, negro. Antes cada painter llevaba su
    /// «texto oscuro sobre el acento», que con el naranja servía y con el violeta caería a ~3.7:1.
    /// </summary>
    public Color OnAccent => ColorMath.Contrast(Accent);

    /// <summary>
    /// Color de las muescas de umbral (warn/critical) sobre el <see cref="Track"/> de las barras de
    /// cuota (T3b). El valor histórico (<see cref="Separator"/>) era ≈ idéntico al Track en los 3 temas
    /// (CLI: exactamente el mismo color, ~1:1) → ticks invisibles. Cae a <see cref="TextMuted"/>, que
    /// cumple el contraste no textual WCAG 1.4.11 (≥3:1) sobre el Track en los 3 temas:
    /// oscuro #8E8E93/#3A3A3C ≈ 3.4:1 · claro #6C6C72/#D4D4D8 ≈ 3.5:1 · CLI #00963C/#003214 ≈ 3.7:1.
    /// Los temas importados heredan el fallback (su TextMuted); override disponible si un tema lo necesita.
    /// </summary>
    public Color? TickOnTrackOverride { get; init; }
    public Color TickOnTrack => TickOnTrackOverride ?? TextMuted;

    /// <summary>
    /// Color de la mascota en Idle/reposo (glifo NO textual → WCAG 1.4.11 pide ≥3:1, T-v039 F1). El
    /// valor histórico era <see cref="Neutral"/>, que sobre el fondo del panel caía a ~2.3:1 en oscuro
    /// (el tema por defecto) y rozaba el umbral en claro → el gatito en reposo se perdía contra el
    /// fondo. Cae a <see cref="TextMuted"/>, que ya cumple AA como texto (~5:1) en los 3 temas y por
    /// tanto supera de sobra el 3:1 no textual sobre el fondo. SOLO afecta al caso Idle/default; las
    /// fases activas (Warn/Ok/Critical por humor o fase) conservan su color semántico. Sin override
    /// cae a <see cref="TextMuted"/>; los temas importados heredan el fallback.
    /// </summary>
    public Color? MascotIdleOverride { get; init; }
    public Color MascotIdle => MascotIdleOverride ?? TextMuted;

    /// <summary>
    /// Variante del ámbar Warn legible como TEXTO pequeño (patrón <see cref="AccentText"/>, T6b). Los
    /// RELLENOS (barras, dots, badges) siguen usando <see cref="Warn"/>; este token solo aplica donde
    /// el ámbar se pinta como texto (línea de pace, % de cuota, marcador stale del footer). Sin
    /// override cae a <see cref="Warn"/>: oscuro #D97706 ≈ 5.6:1 y CLI #F2BF33 ≈ 12.3:1 ya cumplen
    /// AA; solo el tema claro lo oscurece (su #CA8A04 como texto caía a ~2.8:1).
    /// </summary>
    public Color? WarnTextOverride { get; init; }
    public Color WarnText => WarnTextOverride ?? Warn;

    /// <summary>
    /// Variante del rojo Critical legible como TEXTO pequeño (patrón <see cref="AccentText"/>, T6b).
    /// Los RELLENOS siguen usando <see cref="Critical"/>; este token solo aplica al texto (el más
    /// crítico del panel era el de peor contraste). Sin override cae a <see cref="Critical"/>: claro
    /// #DC2626 ≈ 4.6:1 y CLI #F24040 ≈ 5.6:1 ya cumplen AA; solo el tema oscuro lo aclara (su
    /// #DC2626 sobre el fondo caía a ~3.7:1).
    /// </summary>
    public Color? CriticalTextOverride { get; init; }
    public Color CriticalText => CriticalTextOverride ?? Critical;

    /// <summary>
    /// Fondo del realce de hover (T7b, §3 #6). El valor histórico (<see cref="BgElevated"/> puro) era
    /// invisible en claro (#FFFFFF sobre #FAFAFA ≈ 1.03:1) y en CLI (#0A100A sobre negro ≈ 1.05:1).
    /// Es decoración transitoria (no le aplica el 3:1 de WCAG 1.4.11) pero debe ser perceptible
    /// (≥ ~1.1:1 sobre el fondo). Sin override cae a <c>lerp(Background, Foreground, 0.08)</c>, el
    /// «+8 % de luminosidad» del lenguaje de movimiento (§4 #4); los temas importados heredan el
    /// fallback. Solo el CLI lo sube (con tan poco el verde casi no levanta del negro puro).
    /// </summary>
    public Color? HoverBgOverride { get; init; }
    public Color HoverBg => HoverBgOverride ?? ColorMath.Lerp(Background, Foreground, 0.08);

    /// <summary>
    /// Destello al pulsar una fila de sesión (micro-interacción 3 del §4): +14 % de luminosidad, es
    /// decir, el hover (+8 %) llevado otro 6,5 % hacia el primer plano (8 % + 92 %·6,5 % ≈ 14 %).
    /// </summary>
    public Color PressBg => ColorMath.Lerp(HoverBg, Foreground, 0.065);

    /// <summary>
    /// Relleno del knob del TogglePill (T9b, §3 #11). El knob histórico usaba el color del texto
    /// (negro sobre el track gris claro = "agujero perforado") o <c>ColorMath.Contrast(Accent)</c>
    /// (negro sobre el verde CLI). El estándar (iOS/Fluent) es knob BLANCO con borde sutil en todos
    /// los temas: sobre el track ON Accent #7B4DFF contrasta ~4.8:1 (≥3:1, WCAG 1.4.11) en
    /// oscuro/claro; sobre el verde CLI #00D959 (~1.9:1) y el track OFF claro #D1D1D6 (~1.5:1)
    /// delimita el borde <see cref="KnobBorder"/>, y el estado lo comunican posición + color del
    /// track (el knob no es la señal). Sin override cae a blanco.
    /// </summary>
    public Color? KnobFillOverride { get; init; }
    public Color KnobFill => KnobFillOverride ?? Color.White;

    /// <summary>
    /// Borde del knob (T9b): velo negro translúcido (25%, alpha 64) que delimita el círculo blanco
    /// sobre CUALQUIER track sin introducir un opaco nuevo por tema. No es texto (no aplica 4.5:1);
    /// compuesto sobre blanco da ≈#BFBFBF, que separa el knob incluso del track más parecido
    /// (OFF claro #D1D1D6). Un único valor para los 3 temas.
    /// </summary>
    public Color KnobBorder => Color.FromArgb(64, 0, 0, 0);

    /// <summary>
    /// Gama de colores de las SERIES de la gráfica apilada (T13a). Las familias de modelo son
    /// DINÁMICAS (Opus/Fable/Mythos/lo que venga): el slot se asigna por RANGO de gasto (1.º = slot
    /// 0, 2.º = slot 1…) vía <see cref="ChartSeriesAssigner"/>, no por nombre — antes la vista
    /// llevaba violet/sky/emerald/slate hardcodeados a Opus/Sonnet/Haiku/other. Dark/light comparten
    /// la gama histórica (fallback, también para temas importados); el CLI define la suya
    /// verde-fósforo. Tantos slots como el cap de familias (<see cref="ModelFamily.MaxFamilies"/>).
    /// </summary>
    public Color[]? ChartSeriesOverride { get; init; }
    public IReadOnlyList<Color> ChartSeries => ChartSeriesOverride ?? DefaultChartSeries;

    private static readonly Color[] DefaultChartSeries =
    {
        Color.FromArgb(167, 139, 250),   // violet  — slot 0 (mayor gasto)
        Color.FromArgb(56, 189, 248),    // sky     — slot 1
        Color.FromArgb(52, 211, 153),    // emerald — slot 2
        Color.FromArgb(148, 163, 184)    // slate   — slot 3
    };

    // Alias semánticos sobre los campos existentes (sin romper consumidores).
    public Color TextPrimary => Foreground;
    public Color TextSecondary => Dim;
    public Color BgBase => Background;

    public static readonly Theme Dark = new()
    {
        Id = "dark",
        Background = Color.FromArgb(24, 24, 27),
        Foreground = Color.FromArgb(244, 244, 245),
        Dim = Color.FromArgb(161, 161, 170),
        Track = Color.FromArgb(58, 58, 60),          // #3A3A3C (antes 63,63,70)
        Ok = Color.FromArgb(22, 163, 74),
        Warn = Color.FromArgb(217, 119, 6),
        Critical = Color.FromArgb(220, 38, 38),
        // Rojo crítico aclarado para TEXTO (#F87171, red-400): el relleno #DC2626 como texto pequeño
        // caía a ~3.7:1 sobre el fondo oscuro (la línea de pace crítica ilegible). Como texto ≈6.4:1
        // (AA, T6b). El relleno Critical de barras/badges no cambia.
        CriticalTextOverride = Color.FromArgb(0xF8, 0x71, 0x71),
        Neutral = Color.FromArgb(82, 82, 91),
        // Violeta de Muesca (#7B4DFF, el principio del degradado del logo): como relleno contrasta ~3.7:1
        // con el fondo y ~4.3:1 con el negro del notch (≥3:1 no textual) y lleva texto blanco a ~4.8:1.
        Accent = Brand.Violet,
        // Como TEXTO el violeta se queda en ~3.7:1 sobre el fondo oscuro: el lavanda del anillo del logo
        // (#B49BFF) sube a ~7.7:1 (AA) sin salir de la marca.
        AccentTextOverride = Brand.RingStart,
        BgElevated = Color.FromArgb(44, 44, 46),      // #2C2C2E
        TextMuted = Color.FromArgb(142, 142, 147),    // #8E8E93
        Separator = Color.FromArgb(56, 56, 58)        // #38383A
    };

    public static readonly Theme Light = new()
    {
        Id = "light",
        Background = Color.FromArgb(250, 250, 250),
        Foreground = Color.FromArgb(24, 24, 27),
        Dim = Color.FromArgb(113, 113, 122),
        Track = Color.FromArgb(212, 212, 216),
        // Verde de éxito oscurecido (#15803D, green-700): el anterior #16A34A caía a ~3.2:1 sobre el
        // fondo claro (texto pequeño ilegible: línea de salud, badges). Ahora ~4.8:1 (AA, T9).
        Ok = Color.FromArgb(21, 128, 61),
        Warn = Color.FromArgb(202, 138, 4),
        // Ámbar oscurecido para TEXTO (#A16207, yellow-700): el relleno #CA8A04 como texto pequeño
        // caía a ~2.8:1 sobre el fondo claro. Como texto ≈4.7:1 (AA, T6b). El relleno Warn no cambia.
        WarnTextOverride = Color.FromArgb(0xA1, 0x62, 0x07),
        Critical = Color.FromArgb(220, 38, 38),
        Neutral = Color.FromArgb(161, 161, 170),
        // El mismo violeta de Muesca: ~4.6:1 sobre el fondo claro (≥3:1 no textual) y texto blanco a ~4.8:1.
        Accent = Brand.Violet,
        // Acento-como-texto oscurecido (#6236F0): el violeta de relleno llega justo a ~4.6:1 como texto
        // sobre el fondo claro; este tono, del mismo tinte, da ~6.1:1 (AA holgado para texto pequeño,
        // botón «Importar tema», P1 #3).
        AccentTextOverride = Color.FromArgb(0x62, 0x36, 0xF0),
        BgElevated = Color.FromArgb(255, 255, 255),
        // Gris tenue subido (#6C6C72): el anterior #8E8E93 caía a ~3.1:1 sobre el fondo claro. Ahora
        // ~5.0:1 (AA, T9) — antes era el mismo valor que en oscuro pese a fondos opuestos.
        TextMuted = Color.FromArgb(108, 108, 114),
        Separator = Color.FromArgb(209, 209, 214)
    };

    public static readonly Theme Cli = new()
    {
        Id = "cli",
        Background = Color.FromArgb(0, 0, 0),
        Foreground = Color.FromArgb(0, 217, 89),
        Dim = Color.FromArgb(0, 140, 56),
        Track = Color.FromArgb(0, 50, 20),
        Ok = Color.FromArgb(0, 217, 89),
        Warn = Color.FromArgb(242, 191, 51),
        Critical = Color.FromArgb(242, 64, 64),
        Neutral = Color.FromArgb(90, 90, 90),
        Accent = Color.FromArgb(0, 217, 89),
        BgElevated = Color.FromArgb(10, 16, 10),
        // Realce de hover subido a lerp(negro, #00D959, 0.12) = #001A0B (≈1.15:1 sobre el fondo):
        // con el fallback 0.06 (#000D05, ~1.06:1) el verde casi no levanta del negro puro (T7b).
        HoverBgOverride = Color.FromArgb(0, 26, 11),
        // Verde tenue subido (#00963C): el anterior #006E2C caía a ~3.3:1 sobre el negro del CLI
        // (subtítulos/footer/verbo de mascota ilegibles, P1 #3). Ahora ~5.4:1 (AA texto pequeño).
        TextMuted = Color.FromArgb(0, 0x96, 0x3C),
        Separator = Color.FromArgb(0, 50, 20),
        // Gama verde-fósforo propia para las series del gráfico (T13a): el violet/sky de dark/light
        // rompía la estética monocroma de terminal. Rampa de luminancia (brillante → tenue) sobre
        // negro: slot 0 = mayor gasto, el más visible; los 4 se distinguen entre sí apilados.
        ChartSeriesOverride = new[]
        {
            Color.FromArgb(0, 255, 102),
            Color.FromArgb(0, 194, 79),
            Color.FromArgb(0, 140, 56),
            Color.FromArgb(0, 89, 36)
        }
    };

    public static Color StatusColor(Theme t, UI.UsageStatus s) => s switch
    {
        UI.UsageStatus.Critical => t.Critical,
        UI.UsageStatus.Warn => t.Warn,
        _ => t.Ok
    };

    /// <summary>
    /// Color de TEXTO por estado de pace (T6b): como <see cref="StatusColor"/> pero con las variantes
    /// legibles <see cref="CriticalText"/>/<see cref="WarnText"/> (AA ≥4.5:1 sobre el fondo del panel
    /// en los 3 temas; Ok ya cumple como texto). Para texto pequeño (% de cuota, glance del header,
    /// línea de pace); los rellenos siguen con <see cref="StatusColor"/>/<see cref="Warn"/>/<see cref="Critical"/>.
    /// </summary>
    public static Color PaceTextColor(Theme t, PaceStatus s) => s switch
    {
        PaceStatus.Critical => t.CriticalText,
        PaceStatus.Over => t.WarnText,
        _ => t.Ok
    };
}

public static class ThemeResolver
{
    public static Theme Resolve(AppConfig cfg)
    {
        string id = string.IsNullOrEmpty(cfg.Theme) ? "system" : cfg.Theme;
        if (id == "system")
            id = OsPrefersDark() ? "dark" : "light";

        return id switch
        {
            "light" => Theme.Light,
            "cli" => Theme.Cli,
            "imported" => cfg.ImportedTheme is { } it ? FromImported(it) : Theme.Dark,
            _ => Theme.Dark
        };
    }

    /// <summary>Reads Windows' app theme preference (Settings → Personalisation → Colours).</summary>
    public static bool OsPrefersDark()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return v is int i && i == 0;
        }
        catch
        {
            return true; // default to dark
        }
    }

    /// <summary>
    /// Reads Windows' <b>taskbar</b> theme preference (<c>SystemUsesLightTheme</c>) — distinta de
    /// <see cref="OsPrefersDark"/>, que lee el tema de las <i>apps</i> (<c>AppsUseLightTheme</c>).
    /// Determina si la barra de tareas es clara, para que el badge del tray adapte su contraste.
    /// Con fallback: si no se puede leer el registro, asume barra oscura (false).
    /// </summary>
    public static bool TaskbarIsLight()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0);
            return v is int i && i == 1;
        }
        catch
        {
            return false; // default to dark taskbar
        }
    }

    private static Theme FromImported(ImportedThemeColors c) => new()
    {
        Id = "imported",
        Background = Hex(c.Bg, Theme.Dark.Background),
        Foreground = Hex(c.Fg, Theme.Dark.Foreground),
        Dim = Hex(c.Dim, Theme.Dark.Dim),
        Track = Hex(c.Track, Theme.Dark.Track),
        Ok = Hex(c.Ok, Theme.Dark.Ok),
        Warn = Hex(c.Warn, Theme.Dark.Warn),
        Critical = Hex(c.Critical, Theme.Dark.Critical),
        Neutral = Theme.Dark.Neutral,
        Accent = Theme.Dark.Accent,
        // El acento como texto, del lado de la luminosidad del fondo importado: lavanda sobre fondos
        // oscuros, violeta oscuro sobre claros (el violeta de relleno no llega a AA en ninguno de los dos).
        AccentTextOverride = ColorMath.RelativeLuminance(Hex(c.Bg, Theme.Dark.Background)) > 0.18
            ? Theme.Light.AccentText : Theme.Dark.AccentText,
        BgElevated = ColorMath.Lerp(Hex(c.Bg, Theme.Dark.Background), Color.White, 0.06),
        TextMuted = Hex(c.Dim, Theme.Dark.TextMuted),
        Separator = Hex(c.Track, Theme.Dark.Separator)
    };

    private static Color Hex(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var h = hex.TrimStart('#');
            if (h.Length == 6)
                return Color.FromArgb(
                    Convert.ToInt32(h.Substring(0, 2), 16),
                    Convert.ToInt32(h.Substring(2, 2), 16),
                    Convert.ToInt32(h.Substring(4, 2), 16));
        }
        catch { }
        return fallback;
    }
}
